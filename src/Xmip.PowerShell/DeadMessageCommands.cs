using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">The Messages no Subscription matched, kept in each
/// node's Dead Message Queue, one opened, and Replay on one of them.</para>
/// </summary>
/// <remarks>
/// One cmdlet for the noun, the act a parameter on it (ADR-0014, amendment
/// 2026-09-15; ADR-0052, amendment 2026-10-01), as <c>xmip-cli
/// dead-messages</c> takes it as an option. Each entry is an accepted Message
/// kept in the Ledger with its receive context, its gate verdicts, its
/// promoted properties and every Subscription's decline; it is not a dead
/// letter queue: a failed Journey never goes there. Which entries a call
/// selects and in what order is <see cref="DeadMessageQuery"/>'s, the one
/// every surface asks, and how a Replay reaches the node whose queue keeps
/// the Message is the surface's. Listing, objects out:
/// <see cref="DeadMessageRecord"/>, opened with <c>Format-List</c>.
/// Replaying, with -WhatIf and -Confirm: <see cref="DeadMessageOperation"/>,
/// and an entry listed by an earlier call pipes into a Replay by its Node and
/// Message.
/// </remarks>
[Cmdlet(
    VerbsCommon.Get, "XmipDeadMessage",
    DefaultParameterSetName = ListSet,
    SupportsShouldProcess = true)]
[OutputType(typeof(DeadMessageRecord), ParameterSetName = [ListSet])]
[OutputType(typeof(DeadMessageOperation), ParameterSetName = [ReplaySet])]
public sealed class GetXmipDeadMessageCommand : XmipSurfaceCommand
{
    private const string ListSet = "List";
    private const string ReplaySet = "Replay";

    /// <summary>
    /// <para type="description">A scope pattern, * and ? over each entry's
    /// node, or its node and Message as one scope — */&lt;node&gt; is everything
    /// that node's queue keeps, */dead-message/0199* every Message whose
    /// identifier begins 0199.</para>
    /// </summary>
    [Parameter(Position = 0, ParameterSetName = ListSet)]
    [SupportsWildcards]
    public string? Pattern { get; set; }

    /// <summary>
    /// <para type="description">Where the drill stands: a cluster, such as
    /// xmip:///&lt;cluster&gt;, or a node, such as
    /// xmip:///&lt;cluster&gt;/node/&lt;node&gt;, and every entry its queues
    /// keep. A Replay names the node.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    [Parameter(
        Mandatory = true, ParameterSetName = ReplaySet, ValueFromPipelineByPropertyName = true)]
    [Alias("Node")]
    public string? Location { get; set; }

    /// <summary>
    /// <para type="description">One Message, by its identifier, in the queue
    /// of the node at -Location.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    [Parameter(
        Mandatory = true, ParameterSetName = ReplaySet, ValueFromPipelineByPropertyName = true)]
    public string? Message { get; set; }

    /// <summary>
    /// <para type="description">The column to sort by: received, message,
    /// cluster, node, receive-location or declines. Omitted, received: the
    /// oldest first.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    [ArgumentCompleter(typeof(DeadMessageColumns))]
    public string? Sort { get; set; }

    /// <summary>
    /// <para type="description">Greatest first. Omitted, least first.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    public SwitchParameter Descending { get; set; }

    /// <summary>
    /// <para type="description">Once a Subscription is added or fixed: route
    /// the Message again against its node's Subscriptions of now, open a
    /// Journey for each match and take it out of the queue, once. A Message
    /// that still matches nothing stays, and the Replay is refused.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ReplaySet)]
    public SwitchParameter Replay { get; set; }

    /// <summary>
    /// <para type="description">Who acts, as the node's audit records it. The
    /// current user when omitted.</para>
    /// </summary>
    [Parameter(ParameterSetName = ReplaySet)]
    public string? Who { get; set; }

    /// <summary>What this invocation asks, in the query's words.</summary>
    public DeadMessageQuery Query()
    {
        return new DeadMessageQuery
        {
            Pattern = Pattern,
            Location = Location,
            Message = Message,
            Sort = Sort,
            Order = Descending ? "descending" : null,
        };
    }

    /// <inheritdoc />
    protected override void Process()
    {
        IReadOnlyList<DeadMessageRecord> chosen;

        try
        {
            chosen = Query().Apply(Surface.DeadMessages().DeadMessages);
        }
        catch (ArgumentException refused)
        {
            Stop(new ErrorRecord(
                new ArgumentException(English.Refusal(refused)),
                "XmipDeadMessageQueryRefused",
                ErrorCategory.InvalidArgument,
                Query()));

            return;
        }

        if (ParameterSetName == ListSet)
        {
            foreach (DeadMessageRecord entry in chosen)
            {
                WriteObject(entry);
            }

            return;
        }

        Act(chosen);
    }

    private void Act(IReadOnlyList<DeadMessageRecord> chosen)
    {
        const DeadMessageAct act = DeadMessageAct.Replay;
        string target = $"Message {Message} in the Dead Message Queue of {Location}";

        if (Location is null || ScopeTree.Node(Location).Length == 0
            || chosen.SingleOrDefault(entry => ScopeTree.Beneath(Location, entry.Node))
                is not { } one)
        {
            Refuse(new ErrorRecord(
                new ItemNotFoundException($"REFUSED: no {target} is listed."),
                "XmipDeadMessageNotFound",
                ErrorCategory.ObjectNotFound,
                target));

            return;
        }

        if (!ShouldProcess(target, DeadMessageOperation.Word(act)))
        {
            return;
        }

        Acting(target);
        DeadMessageOperation done = Surface.Act(one, act, ScopeOperation.Who(Who));

        if (!done.Applied)
        {
            Refuse(new ErrorRecord(
                new InvalidOperationException(done.Result),
                "XmipDeadMessageReplayRefused",
                ErrorCategory.InvalidOperation,
                target));

            return;
        }

        Acted(target, done.Result);
        WriteObject(done);
    }
}

/// <summary>What Tab offers for <c>-Sort</c>: the columns
/// <see cref="DeadMessageQuery"/> orders by, so no word list is kept
/// here.</summary>
public sealed class DeadMessageColumns : IArgumentCompleter
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

        return DeadMessageQuery.Columns
            .Where(column => column.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Select(column => new CompletionResult(
                column, column, CompletionResultType.ParameterValue, column));
    }
}
