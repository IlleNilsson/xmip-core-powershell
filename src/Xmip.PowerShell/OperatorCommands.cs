using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

// The cmdlets that reach a running Xmip. ADR-0027 said the three in
// Commands.cs describe the binding and none of them talks to a runtime; these
// read through Xmip.Surface, the model the executable and the GUI read, so a
// cmdlet and `xmip-cli` answer one question from one surface in one way
// (ADR-0052 clause 1). Objects out, never text.

/// <summary>
/// <para type="synopsis">Health at and beneath a scope.</para>
/// </summary>
/// <remarks>
/// Worst first. Each record is the publisher's own — a state, how far from
/// healthy, the one line of evidence, and when it was observed, so a stalled
/// publisher is not mistaken for an idle estate (ADR-0027 clause 6). The
/// surface is the one <c>xmip-cli health</c> reads, and a scope may be a
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
    /// what xmip-cli health --follow does. A wildcard is matched again at
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
/// The drill, in PowerShell: the same <see cref="ScopeItem"/> <c>xmip-cli
/// show</c> and <c>list</c> render and the web views draw (ADR-0052). With no
/// scope it is the cluster; a wildcard names several, so
/// <c>-Scope xmip:///&lt;cluster&gt;/*</c> is every scope directly beneath the cluster,
/// worst first — the topmost matches — and a row's <c>Worst</c> is the next
/// scope to ask about on the way to the cause. Until 2026-09-26 the module
/// had no row and no figure: <c>Get-XmipHealth</c> answered with every leaf
/// beneath a scope, thousands for one node.
/// <para>A Send Port's row says, in its evidence, how many Journeys failed
/// there and the last with why (runtime-model.md section 13). The Journeys
/// that failed and the acts on one are parameters here rather than cmdlets
/// of their own: <c>-FailedJourney</c> lists every one at or beneath the
/// scope, a page of each Port from <c>-Offset</c>, at most <c>-Limit</c>, as
/// <c>xmip-cli journey</c> lists them; <c>-Journey &lt;id&gt; -Retry</c> or
/// <c>-Dismiss</c>, with -WhatIf and -Confirm, on the Send Port's scope or
/// its node's, emits the <see cref="JourneyOperation"/> <c>xmip-cli
/// journey</c> renders.</para>
/// </remarks>
[Cmdlet(
    VerbsCommon.Get, "XmipScope",
    DefaultParameterSetName = ReadSet,
    SupportsShouldProcess = true)]
[OutputType(typeof(ScopeItem), ParameterSetName = [ReadSet])]
[OutputType(typeof(JourneyOperation), ParameterSetName = [RetrySet, DismissSet])]
[OutputType(typeof(FailedJourneyRecord), ParameterSetName = [FailedSet])]
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
    /// Send Port's scope where the Journey is said to have failed, such as
    /// xmip:///&lt;cluster&gt;/node/&lt;node&gt;/send/&lt;Port&gt;, or its node's.</para>
    /// </summary>
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Scope { get; set; } = [];

    /// <summary>
    /// <para type="description">Keep answering: the rows now, then again each
    /// time the publication advances and they changed, until Ctrl+C — what
    /// xmip-cli --follow does. A wildcard is matched again at every change.
    /// Follows the first scope named.</para>
    /// </summary>
    [Parameter(ParameterSetName = ReadSet)]
    public SwitchParameter Follow { get; set; }

    /// <summary>
    /// <para type="description">The Journeys that failed at the Send Ports at
    /// or beneath the scope, oldest first, each with its Send Port and why:
    /// read from Xmip Storage where the node runs in this process, or the
    /// oldest hundred of each Port its publication carries. How many wait at
    /// each Port, and where its next page starts, are said with -Verbose.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = FailedSet)]
    public SwitchParameter FailedJourney { get; set; }

    /// <summary>
    /// <para type="description">The place in each Send Port's queue to read
    /// from, as the last page's next said. The oldest when omitted.</para>
    /// </summary>
    [Parameter(ParameterSetName = FailedSet)]
    public ulong Offset { get; set; }

    /// <summary>
    /// <para type="description">The most Journeys of each Send Port. A
    /// hundred when omitted.</para>
    /// </summary>
    [Parameter(ParameterSetName = FailedSet)]
    public uint Limit { get; set; }

    /// <summary>
    /// <para type="description">A Journey that failed, by its identifier, as
    /// its Send Port's row says it: the one -Retry or -Dismiss acts on.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = RetrySet)]
    [Parameter(Mandatory = true, ParameterSetName = DismissSet)]
    public string? Journey { get; set; }

    /// <summary>
    /// <para type="description">Send the Journey again, its tries begun anew,
    /// from the end of its Send Port's queue — or from its place, where it
    /// blocks a Sequential Send Port.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = RetrySet)]
    public SwitchParameter Retry { get; set; }

    /// <summary>
    /// <para type="description">Give the Journey up: written Dismissed, its
    /// history, Message and Stream kept, and taken out of its Send Port's
    /// queue.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = DismissSet)]
    public SwitchParameter Dismiss { get; set; }

    /// <summary>
    /// <para type="description">Who acts on the Journey, as the node's audit
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
            Act(ParameterSetName == RetrySet ? JourneyAct.Retry : JourneyAct.Dismiss);

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

    // The Journeys that failed at or beneath each scope named — the cluster
    // the surface publishes where none is — one row each.
    private void Failed()
    {
        foreach (string scope in Scope.Length == 0 ? [Surface.Root()] : Scope)
        {
            foreach (FailedJourneyPort port in Surface.FailedJourneys(scope, Offset, Limit).Ports)
            {
                WriteVerbose(
                    $"{port.Node} Send Port {port.SendPort}: {port.Count} failed in its queue"
                    + (port.Next is { } next ? $"; the next page from -Offset {next}" : string.Empty));

                foreach (FailedJourneyRecord journey in port.Journeys)
                {
                    WriteObject(journey);
                }
            }
        }
    }

    // An act names one Journey on one scope, no wildcard: the node that sends
    // its Send Port, or the Port's scope beneath it.
    private void Act(JourneyAct act)
    {
        string journey = Journey ?? string.Empty;
        string target = $"the Journey {journey} sent at {string.Join(", ", Scope)}";

        if (Scope is not [var scope] || WildcardPattern.ContainsWildcardCharacters(scope))
        {
            Refuse(new ErrorRecord(
                new ArgumentException(
                    $"REFUSED: to {JourneyOperation.Word(act)} {target}, name one scope: the "
                    + "Send Port's, such as xmip:///<cluster>/node/<node>/send/<Port>, or its "
                    + "node's."),
                "XmipJourneyScopeRefused",
                ErrorCategory.InvalidArgument,
                target));

            return;
        }

        if (!ShouldProcess(target, JourneyOperation.Word(act)))
        {
            return;
        }

        Acting(target);
        JourneyOperation done = Surface.Act(scope, journey, act, ScopeOperation.Who(Who));

        if (!done.Applied)
        {
            Refuse(new ErrorRecord(
                new InvalidOperationException(done.Result),
                "XmipJourneyActRefused",
                ErrorCategory.InvalidOperation,
                target));

            return;
        }

        Acted(target, done.Result);
        WriteObject(done);
    }
}

/// <summary>
/// <para type="synopsis">Whether a node configuration file would start, judged
/// by the runtime that would start it.</para>
/// </summary>
/// <remarks>
/// The file's text crosses, not its path: the runtime validates a proposed
/// document and publishes nothing (ADR-0027 clause 9). Test, because that is
/// what it does — a document goes in, a verdict comes out, and nothing is
/// applied. The verdict is the <see cref="ConfigurationVerdict"/>
/// <c>xmip-cli validate</c> renders and the desktop decides from.
/// </remarks>
[Cmdlet(VerbsDiagnostic.Test, "XmipNodeConfiguration")]
[OutputType(typeof(ConfigurationVerdict))]
public sealed class TestXmipNodeConfigurationCommand : XmipCommand
{
    private NativeOperator? _runtime;

    /// <summary>
    /// <para type="description">Path to the node configuration TOML.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string[] Path { get; set; } = [];

    /// <summary>
    /// <para type="description">The runtime's native library to ask, the one
    /// exporting xmip_validate_v1. Omitted, it is found by the one rule:
    /// RuntimeLibrary in the module's document, else XMIP_RUNTIME_LIBRARY,
    /// else beside the module.</para>
    /// </summary>
    [Parameter]
    public string? Library { get; set; }

    /// <inheritdoc />
    protected override void Begin()
    {
        string? library = string.IsNullOrWhiteSpace(Library)
            ? null
            : GetUnresolvedProviderPathFromPSPath(Library);
        _runtime = new NativeOperator(ModuleSurface.Runtime(library));

        if (!_runtime.IsLoaded)
        {
            // A runtime that cannot be loaded is a terminating condition:
            // there is no next document it could judge.
            Stop(new ErrorRecord(
                new InvalidOperationException(_runtime.Reason),
                "XmipRuntimeUnloadable",
                ErrorCategory.ResourceUnavailable,
                _runtime.Path));
        }
    }

    /// <inheritdoc />
    protected override void Process()
    {
        foreach (string configuration in Path)
        {
            string path = GetUnresolvedProviderPathFromPSPath(configuration);
            ConfigurationVerdict verdict = _runtime!.Validate(path);

            if (!File.Exists(path))
            {
                Refuse(new ErrorRecord(
                    new FileNotFoundException(verdict.Said, path),
                    "XmipNodeConfigurationMissing",
                    ErrorCategory.ObjectNotFound,
                    path));

                continue;
            }

            WriteObject(verdict);
        }
    }

    /// <inheritdoc />
    protected override void End()
    {
        _runtime?.Dispose();
    }

    /// <inheritdoc />
    protected override void StopProcessing()
    {
        _runtime?.Dispose();
    }
}
