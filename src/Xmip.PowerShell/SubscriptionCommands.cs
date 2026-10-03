using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">The Subscriptions the cluster's nodes route by, and
/// pause or resume on one of them.</para>
/// </summary>
/// <remarks>
/// One cmdlet for the noun, the act a parameter on it (ADR-0014, amendment
/// 2026-09-15; ADR-0013, amendment 2026-09-30), as <c>xmip-cli
/// subscriptions</c> takes it as an option. A Subscription picks a
/// published Message up and opens a Journey; it is configuration, so there
/// is no remove: <see cref="SubscriptionOperation.Configured"/>. Not an
/// Event subscription (<c>Get-XmipEventSubscription</c>). Which
/// Subscriptions a call selects and in what order is
/// <see cref="SubscriptionQuery"/>'s, the one every surface asks, and how an
/// act reaches the node that routes by the Subscription is the surface's.
/// Listing, objects out: <see cref="SubscriptionRecord"/>. Acting, with
/// -WhatIf and -Confirm: <see cref="SubscriptionOperation"/>, one per
/// Subscription, and a Subscription listed by an earlier call pipes into an
/// act by its Node and Name.
/// </remarks>
[Cmdlet(
    VerbsCommon.Get, "XmipSubscription",
    DefaultParameterSetName = ListSet,
    SupportsShouldProcess = true)]
[OutputType(typeof(SubscriptionRecord), ParameterSetName = [ListSet])]
[OutputType(typeof(SubscriptionOperation), ParameterSetName = [PauseSet, ResumeSet])]
public sealed class GetXmipSubscriptionCommand : XmipSurfaceCommand
{
    private const string ListSet = "List";
    private const string PauseSet = "Pause";
    private const string ResumeSet = "Resume";

    /// <summary>
    /// <para type="description">A scope pattern, * and ?, over each
    /// Subscription's node, or its node and name as one scope —
    /// */&lt;node&gt; is everything that node routes by, */subscription/edi* every
    /// Subscription whose name begins edi.</para>
    /// </summary>
    [Parameter(Position = 0, ParameterSetName = ListSet)]
    [SupportsWildcards]
    public string? Pattern { get; set; }

    /// <summary>
    /// <para type="description">Where the drill stands: a cluster, such as
    /// xmip:///&lt;cluster&gt;, or a node, such as
    /// xmip:///&lt;cluster&gt;/node/&lt;node&gt;, and every
    /// Subscription routed by there. An act names the node.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    [Parameter(
        Mandatory = true, ParameterSetName = PauseSet, ValueFromPipelineByPropertyName = true)]
    [Parameter(
        Mandatory = true, ParameterSetName = ResumeSet, ValueFromPipelineByPropertyName = true)]
    [Alias("Node")]
    public string? Location { get; set; }

    /// <summary>
    /// <para type="description">One Subscription, by its configured name on
    /// its node.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    [Parameter(
        Mandatory = true, ParameterSetName = PauseSet, ValueFromPipelineByPropertyName = true)]
    [Parameter(
        Mandatory = true, ParameterSetName = ResumeSet, ValueFromPipelineByPropertyName = true)]
    public string? Name { get; set; }

    /// <summary>
    /// <para type="description">The column to sort by: subscription, cluster,
    /// node, filter, destination, state, picked-up, held or since. Omitted,
    /// subscription.</para>
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
    /// <para type="description">Hold what the Subscription matches: each
    /// Message is kept in the node's runtime store and counted as held, not
    /// picked up. A pause survives a restart of the node.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = PauseSet)]
    public SwitchParameter Pause { get; set; }

    /// <summary>
    /// <para type="description">Pick up what it held, oldest first, and what
    /// it matches from then on.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ResumeSet)]
    public SwitchParameter Resume { get; set; }

    /// <summary>
    /// <para type="description">Who acts, as the node's audit records it. The
    /// current user when omitted.</para>
    /// </summary>
    [Parameter(ParameterSetName = PauseSet)]
    [Parameter(ParameterSetName = ResumeSet)]
    public string? Who { get; set; }

    /// <summary>What this invocation asks, in the query's words.</summary>
    public SubscriptionQuery Query()
    {
        return new SubscriptionQuery
        {
            Pattern = Pattern,
            Location = Location,
            Name = Name,
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
        SubscriptionAct act = ParameterSetName == PauseSet
            ? SubscriptionAct.Pause
            : SubscriptionAct.Resume;
        string target = $"Subscription '{Name}' on {Location}";

        if (Location is null || ScopeTree.Node(Location).Length == 0
            || chosen.SingleOrDefault(entry => ScopeTree.Beneath(Location, entry.Node))
                is not { } one)
        {
            Refuse(new ErrorRecord(
                new ItemNotFoundException(
                    $"REFUSED: no {target} is listed; its Application does not draw it there."),
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
