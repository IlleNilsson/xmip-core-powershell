using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">The Event subscriptions the cluster's nodes hold,
/// and pause, resume or remove on one of them.</para>
/// </summary>
/// <remarks>
/// One cmdlet for the noun, the act a parameter on it (ADR-0014, amendment
/// 2026-09-15; ADR-0065, amendment 2026-09-29), as <c>xmip-cli
/// subscriptions</c> takes it as an option. Which subscriptions a call
/// selects and in what order is <see cref="SubscriptionQuery"/>'s, the one
/// every surface asks, and how an act reaches the node that holds the
/// subscription is the surface's. Listing, objects out:
/// <see cref="SubscriptionRecord"/>. Acting, with -WhatIf and -Confirm:
/// <see cref="SubscriptionOperation"/>, one per subscription, and a
/// subscription listed by an earlier call pipes into an act by its Node and
/// Id.
/// </remarks>
[Cmdlet(
    VerbsCommon.Get, "XmipSubscription",
    DefaultParameterSetName = ListSet,
    SupportsShouldProcess = true)]
[OutputType(typeof(SubscriptionRecord), ParameterSetName = [ListSet])]
[OutputType(typeof(SubscriptionOperation), ParameterSetName = [PauseSet, ResumeSet, RemoveSet])]
public sealed class GetXmipSubscriptionCommand : XmipSurfaceCommand
{
    private const string ListSet = "List";
    private const string PauseSet = "Pause";
    private const string ResumeSet = "Resume";
    private const string RemoveSet = "Remove";

    /// <summary>
    /// <para type="description">A scope pattern, * and ?, over each
    /// subscription's node and the scope its filter reaches — */R1 is
    /// everything node R1 holds.</para>
    /// </summary>
    [Parameter(Position = 0, ParameterSetName = ListSet)]
    [SupportsWildcards]
    public string? Pattern { get; set; }

    /// <summary>
    /// <para type="description">Where the drill stands: a cluster, such as
    /// xmip:///C1, or a node, such as xmip:///C1/node/R1, and every
    /// subscription held there. An act names the node.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    [Parameter(Mandatory = true, ParameterSetName = PauseSet, ValueFromPipelineByPropertyName = true)]
    [Parameter(Mandatory = true, ParameterSetName = ResumeSet, ValueFromPipelineByPropertyName = true)]
    [Parameter(Mandatory = true, ParameterSetName = RemoveSet, ValueFromPipelineByPropertyName = true)]
    [Alias("Node")]
    public string? Location { get; set; }

    /// <summary>
    /// <para type="description">One subscription, by its number on its
    /// node.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    [Parameter(Mandatory = true, ParameterSetName = PauseSet, ValueFromPipelineByPropertyName = true)]
    [Parameter(Mandatory = true, ParameterSetName = ResumeSet, ValueFromPipelineByPropertyName = true)]
    [Parameter(Mandatory = true, ParameterSetName = RemoveSet, ValueFromPipelineByPropertyName = true)]
    public ulong? Id { get; set; }

    /// <summary>
    /// <para type="description">The column to sort by: subscriber, cluster,
    /// node, action, state, queued, delivered, missed or since. Omitted,
    /// subscriber.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    [ArgumentCompleter(typeof(SubscriptionColumns))]
    public string? Sort { get; set; }

    /// <summary>
    /// <para type="description">Greatest first. Omitted, least first.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    public SwitchParameter Descending { get; set; }

    /// <summary>
    /// <para type="description">Hold the subscription's delivery; its queue
    /// keeps filling up to its capacity, and what a full queue refuses is
    /// counted as missed.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = PauseSet)]
    public SwitchParameter Pause { get; set; }

    /// <summary>
    /// <para type="description">Deliver again, what queued first.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ResumeSet)]
    public SwitchParameter Resume { get; set; }

    /// <summary>
    /// <para type="description">Unsubscribe it.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = RemoveSet)]
    public SwitchParameter Remove { get; set; }

    /// <summary>
    /// <para type="description">Who acts, as the node's audit records it. The
    /// current user when omitted.</para>
    /// </summary>
    [Parameter(ParameterSetName = PauseSet)]
    [Parameter(ParameterSetName = ResumeSet)]
    [Parameter(ParameterSetName = RemoveSet)]
    public string? Who { get; set; }

    /// <summary>What this invocation asks, in the query's words.</summary>
    public SubscriptionQuery Query()
    {
        return new SubscriptionQuery
        {
            Pattern = Pattern,
            Location = Location,
            Id = Id,
            Sort = Sort,
            Order = Descending ? "descending" : null,
        };
    }

    /// <inheritdoc />
    protected override void Process()
    {
        IReadOnlyList<SubscriptionRecord> chosen;

        try
        {
            chosen = Query().Apply(Surface.Subscriptions().Subscriptions);
        }
        catch (ArgumentException refused)
        {
            Stop(new ErrorRecord(
                new ArgumentException(English.Refusal(refused)),
                "XmipSubscriptionQueryRefused",
                ErrorCategory.InvalidArgument,
                Query()));

            return;
        }

        if (ParameterSetName == ListSet)
        {
            foreach (SubscriptionRecord entry in chosen)
            {
                WriteObject(entry);
            }

            return;
        }

        Act(chosen);
    }

    private void Act(IReadOnlyList<SubscriptionRecord> chosen)
    {
        SubscriptionAct act = ParameterSetName switch
        {
            PauseSet => SubscriptionAct.Pause,
            ResumeSet => SubscriptionAct.Resume,
            _ => SubscriptionAct.Remove,
        };
        string target = $"subscription {Id} on {Location}";

        if (Location is null || ScopeTree.Node(Location).Length == 0
            || chosen.SingleOrDefault(entry => ScopeTree.Beneath(Location, entry.Node))
                is not { } one)
        {
            Refuse(new ErrorRecord(
                new ItemNotFoundException(
                    $"REFUSED: no {target} is listed; it was removed, or never made."),
                "XmipSubscriptionNotFound",
                ErrorCategory.ObjectNotFound,
                target));

            return;
        }

        if (!ShouldProcess(target, SubscriptionOperation.Word(act)))
        {
            return;
        }

        Acting(target);
        SubscriptionOperation done = Surface.Act(one, act, ScopeOperation.Who(Who));

        if (!done.Applied)
        {
            Refuse(new ErrorRecord(
                new InvalidOperationException(done.Result),
                "XmipSubscriptionActRefused",
                ErrorCategory.InvalidOperation,
                target));

            return;
        }

        Acted(target, done.Result);
        WriteObject(done);
    }
}

/// <summary>What Tab offers for <c>-Sort</c>: the columns
/// <see cref="SubscriptionQuery"/> orders by, so no word list is kept
/// here.</summary>
public sealed class SubscriptionColumns : IArgumentCompleter
{
    /// <inheritdoc />
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        System.Management.Automation.Language.CommandAst commandAst,
        System.Collections.IDictionary fakeBoundParameters)
    {
        string typed = (wordToComplete ?? string.Empty).Trim('\'', '"');

        return SubscriptionQuery.Columns
            .Where(column => column.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Select(column => new CompletionResult(
                column, column, CompletionResultType.ParameterValue, column));
    }
}
