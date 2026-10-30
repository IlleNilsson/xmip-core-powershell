using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

// The cmdlets that reach a running Xmip. ADR-0027 said the three in
// Commands.cs describe the binding and none of them talks to a runtime; these
// read through Xmip.Surface, the model the executable and the GUI read, so a
// cmdlet and `xmip` answer one question from one surface in one way
// (ADR-0052 clause 1). Objects out, never text.

/// <summary>
/// <para type="synopsis">Health at and beneath a scope.</para>
/// </summary>
/// <remarks>
/// Worst first. Each record is the publisher's own — a state, how far from
/// healthy, the one line of evidence, and when it was observed, so a stalled
/// publisher is not mistaken for an idle estate (ADR-0027 clause 6). The
/// surface is the one <c>xmip health</c> reads, and a scope may be a
/// wildcard, answered for each topmost scope it names and never rolled up
/// together (ADR-0041).
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipHealth")]
[OutputType(typeof(HealthRecord))]
public sealed class GetXmipHealthCommand : XmipSurfaceCommand
{
    /// <summary>
    /// <para type="description">The Xmip URI to ask about, such as
    /// xmip:///&lt;cluster&gt;, or a wildcard over the scopes that exist, such
    /// as xmip:///&lt;cluster&gt;/node/*. Everything beneath each is included.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [SupportsWildcards]
    public string[] Scope { get; set; } = [];

    /// <summary>
    /// <para type="description">Keep answering: the records now, then again
    /// each time the publication advances and they changed, until Ctrl+C —
    /// what xmip health --follow does. A wildcard is matched again at
    /// every change. Follows the first scope named.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Follow { get; set; }

    /// <inheritdoc />
    protected override void Process()
    {
        foreach (string argument in Scope)
        {
            if (Select(argument) is not { } chosen)
            {
                continue;
            }

            if (Follow)
            {
                Following(chosen, (surface, now) =>
                    (IReadOnlyList<HealthRecord>)[.. now.Scopes.SelectMany(surface.Health)]);

                return;
            }

            foreach (string scope in chosen.Scopes)
            {
                IReadOnlyList<HealthRecord> records = Surface.Health(scope);

                if (records.Count == 0)
                {
                    Refuse(new ErrorRecord(
                        new ItemNotFoundException(English.NothingAt(scope, Surface.Source)),
                        "XmipScopeNotFound",
                        ErrorCategory.ObjectNotFound,
                        scope));

                    continue;
                }

                foreach (HealthRecord record in records)
                {
                    WriteObject(record);
                }
            }
        }
    }
}

/// <summary>
/// <para type="synopsis">One scope as a row of the tree: its mood, the leaf that
/// explains it and that leaf's evidence, and its six figures.</para>
/// </summary>
/// <remarks>
/// The drill, in PowerShell: the same <see cref="ScopeItem"/> <c>xmip
/// show</c> and <c>list</c> render and the web views draw (ADR-0052). With no
/// scope it is the cluster; a wildcard names several, so
/// <c>-Scope xmip:///&lt;cluster&gt;/*</c> is every scope directly beneath the cluster,
/// worst first — the topmost matches — and a row's <c>Worst</c> is the next
/// scope to ask about on the way to the cause. Until 2026-09-26 the module
/// had no row and no figure: <c>Get-XmipHealth</c> answered with every leaf
/// beneath a scope, thousands for one node.
/// <para>A Send Port's row says, in its evidence, how many Messages failed
/// there and the last with why (runtime-model.md section 13). The Messages
/// that failed and the acts on one are parameters here rather than cmdlets
/// of their own: <c>-FailedMessage</c> lists every one at or beneath the
/// scope, a page of each Port from <c>-Offset</c>, at most <c>-Limit</c>, as
/// <c>xmip message</c> lists them; <c>-Journey &lt;id&gt; -Retry</c> or
/// <c>-Dismiss</c>, with -WhatIf and -Confirm, on the Send Port's scope or
/// its node's, emits the <see cref="MessageOperation"/> <c>xmip
/// message</c> renders.</para>
/// </remarks>
[Cmdlet(
    VerbsCommon.Get, "XmipScope",
    DefaultParameterSetName = ReadSet,
    SupportsShouldProcess = true)]
[OutputType(typeof(ScopeItem), ParameterSetName = [ReadSet])]
[OutputType(typeof(MessageOperation), ParameterSetName = [RetrySet, DismissSet])]
[OutputType(typeof(FailedMessageRecord), ParameterSetName = [FailedSet])]
public sealed class GetXmipScopeCommand : XmipSurfaceCommand
{
    private const string ReadSet = "Read";
    private const string FailedSet = "Failed";
    private const string RetrySet = "Retry";
    private const string DismissSet = "Dismiss";

    /// <summary>
    /// <para type="description">The Xmip URI to describe, such as
    /// xmip:///&lt;cluster&gt;, or a wildcard over the scopes that exist, such
    /// as xmip:///&lt;cluster&gt;/node/*.
    /// Omitted, the cluster the surface publishes. With -Journey, the one
    /// Send Port's scope where its Message is said to have failed, such as
    /// xmip:///&lt;cluster&gt;/node/&lt;node&gt;/send/&lt;Port&gt;, or its node's.</para>
    /// </summary>
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Scope { get; set; } = [];

    /// <summary>
    /// <para type="description">Keep answering: the rows now, then again each
    /// time the publication advances and they changed, until Ctrl+C — what
    /// xmip --follow does. A wildcard is matched again at every change.
    /// Follows the first scope named.</para>
    /// </summary>
    [Parameter(ParameterSetName = ReadSet)]
    public SwitchParameter Follow { get; set; }

    /// <summary>
    /// <para type="description">The Messages that failed at the Send Ports at
    /// or beneath the scope, oldest first, each with its Send Port and why:
    /// read from Xmip Storage where the node runs in this process, or the
    /// oldest of each Port its publication carries, its
    /// <c>[tuning] failed_window</c>, a hundred by default. How many wait at
    /// each Port, and where its next page starts, are said with -Verbose.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = FailedSet)]
    public SwitchParameter FailedMessage { get; set; }

    /// <summary>
    /// <para type="description">Where each Send Port's page starts, 0 the
    /// oldest when omitted. Read from Xmip Storage, a place in the Port's
    /// index of failed Messages, as the last page's next said; read from a
    /// publication, which carries only its node's window and no places in
    /// the index, a place in the Port's queue, and no next follows.</para>
    /// </summary>
    [Parameter(ParameterSetName = FailedSet)]
    public ulong Offset { get; set; }

    /// <summary>
    /// <para type="description">The most Messages of each Send Port. A
    /// hundred when omitted.</para>
    /// </summary>
    [Parameter(ParameterSetName = FailedSet)]
    public uint Limit { get; set; }

    /// <summary>
    /// <para type="description">The Journey of a Message that failed, by its identifier,
    /// as its Send Port's row says it: the one -Retry or -Dismiss acts on.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = RetrySet)]
    [Parameter(Mandatory = true, ParameterSetName = DismissSet)]
    public string? Journey { get; set; }

    /// <summary>
    /// <para type="description">Send the Message again, its tries begun anew,
    /// from the end of its Send Port's queue — or from its place, where it
    /// blocks its sequence.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = RetrySet)]
    public SwitchParameter Retry { get; set; }

    /// <summary>
    /// <para type="description">Give the Message up: written Dismissed, its
    /// history, Message and Stream kept, and taken out of its Send Port's
    /// queue.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = DismissSet)]
    public SwitchParameter Dismiss { get; set; }

    /// <summary>
    /// <para type="description">Who acts on the Message, as the node's audit
    /// records it. The current user when omitted.</para>
    /// </summary>
    [Parameter(ParameterSetName = RetrySet)]
    [Parameter(ParameterSetName = DismissSet)]
    public string? Who { get; set; }

    /// <inheritdoc />
    protected override void Process()
    {
        if (ParameterSetName == FailedSet)
        {
            Failed();

            return;
        }

        if (ParameterSetName != ReadSet)
        {
            Act(ParameterSetName == RetrySet ? MessageAct.Retry : MessageAct.Dismiss);

            return;
        }

        foreach (string argument in Scope.Length == 0 ? [string.Empty] : Scope)
        {
            if (Select(argument) is not { } chosen)
            {
                continue;
            }

            if (Follow)
            {
                Following(chosen, ScopeItem.Selected);

                return;
            }

            // A pattern names each topmost scope it matched, worst first, as a
            // level of the tree reads; a literal scope is the one row.
            IReadOnlyList<ScopeItem> rows = ScopeItem.Selected(Surface, chosen);

            foreach (ScopeItem row in rows)
            {
                WriteObject(row);
            }

            if (rows.Count == 0)
            {
                Refuse(new ErrorRecord(
                    new ItemNotFoundException(
                        English.NothingAt(chosen.Argument, Surface.Source)),
                    "XmipScopeNotFound",
                    ErrorCategory.ObjectNotFound,
                    chosen.Argument));
            }
        }
    }

    // The Messages that failed at or beneath each scope named — the cluster
    // the surface publishes where none is — one row each, and only for an
    // answer: a surface that cannot list them, or asked and was not answered,
    // says so as a non-terminating error in the words xmip says it with.
    private void Failed()
    {
        foreach (string scope in Scope.Length == 0 ? [Surface.Root()] : Scope)
        {
            FailedMessageList failed = Surface.FailedMessages(scope, Offset, Limit);

            if (FailedMessageError.Of(failed, scope, Surface.Source) is { } unlisted)
            {
                Refuse(unlisted);
                continue;
            }

            foreach (FailedMessagePort port in failed.Ports)
            {
                if (port.Blocked)
                {
                    WriteWarning(Blocking(port));
                }

                WriteVerbose(
                    $"{port.Node} Send Port {port.SendPort}: {port.Count} failed in its queue"
                    + (port.Next is { } next
                        ? $"; the next page from -Offset {next}"
                        : string.Empty));

                foreach (FailedMessageRecord journey in port.Messages)
                {
                    WriteObject(journey);
                }
            }
        }
    }

    // A blocked key, said loud: every Message of its key waits behind the
    // first that failed until it is retried or dismissed (ADR-0024,
    // amendment 2026-10-27).
    private static string Blocking(FailedMessagePort port)
    {
        return $"{port.Node} Send Port {port.SendPort} blocks its sequence: every Message of "
            + "its key waits behind the first that failed until it is retried or dismissed";
    }

    // An act names one Message on one scope, no wildcard: the node that sends
    // its Send Port, or the Port's scope beneath it.
    private void Act(MessageAct act)
    {
        string journey = Journey ?? string.Empty;
        string target = $"the Message of the Journey {journey} sent at {string.Join(", ", Scope)}";

        if (Scope is not [var scope] || WildcardPattern.ContainsWildcardCharacters(scope))
        {
            Refuse(new ErrorRecord(
                new ArgumentException(
                    $"REFUSED: to {MessageOperation.Word(act)} {target}, name one scope: the "
                    + "Send Port's, such as xmip:///<cluster>/node/<node>/send/<Port>, or its "
                    + "node's."),
                "XmipMessageScopeRefused",
                ErrorCategory.InvalidArgument,
                target));

            return;
        }

        if (!ShouldProcess(target, MessageOperation.Word(act)))
        {
            return;
        }

        Acting(target);
        MessageOperation done = Surface.Act(scope, journey, act, ScopeOperation.Who(Who));

        if (!done.Applied)
        {
            Refuse(new ErrorRecord(
                new InvalidOperationException(done.Result),
                "XmipMessageActRefused",
                ErrorCategory.InvalidOperation,
                target));

            return;
        }

        Acted(target, done.Result);
        WriteObject(done);
    }
}
