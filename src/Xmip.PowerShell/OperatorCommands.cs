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
    /// xmip:///edge-01, or a wildcard over the scopes that exist, such as
    /// xmip:///C1/node/R*. Everything beneath each is included.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [SupportsWildcards]
    public string[] Scope { get; set; } = [];

    /// <inheritdoc />
    protected override void Process()
    {
        foreach (string argument in Scope)
        {
            if (Select(argument) is not { } chosen)
            {
                continue;
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
/// <c>-Scope xmip:///C1/*</c> is every scope directly beneath the cluster,
/// worst first — the topmost matches — and a row's <c>Worst</c> is the next
/// scope to ask about on the way to the cause. Until 2026-09-26 the module
/// had no row and no figure: <c>Get-XmipHealth</c> answered with every leaf
/// beneath a scope, thousands for one node.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipScope")]
[OutputType(typeof(ScopeItem))]
public sealed class GetXmipScopeCommand : XmipSurfaceCommand
{
    /// <summary>
    /// <para type="description">The Xmip URI to describe, such as xmip:///C1,
    /// or a wildcard over the scopes that exist, such as xmip:///C1/node/*.
    /// Omitted, the cluster the surface publishes.</para>
    /// </summary>
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Scope { get; set; } = [];

    /// <inheritdoc />
    protected override void Process()
    {
        foreach (string argument in Scope.Length == 0 ? [string.Empty] : Scope)
        {
            if (Select(argument) is not { } chosen)
            {
                continue;
            }

            IEnumerable<ScopeItem> rows = chosen.Scopes
                .Select(Surface.Describe)
                .Where(row => row.Health is not null || row.Figures.HasValues);

            // A pattern names each topmost scope it matched, worst first, as a
            // level of the tree reads; a literal scope is the one row.
            ScopeIndex index = Surface.Index();
            rows = chosen.Patterned
                ? ScopeTree.WorstFirst([.. rows], row => Standing(index, row))
                : rows;

            int written = 0;

            foreach (ScopeItem row in rows)
            {
                WriteObject(row);
                written++;
            }

            if (written == 0)
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

    // A row as it stands in the worst-first order: its worst leaf's mood and
    // severity under its own scope, as a branch of the tree stands.
    private static HealthRecord Standing(ScopeIndex index, ScopeItem row)
    {
        HealthRecord? worst = index.Worst(row.Scope);

        return new HealthRecord(
            row.Scope,
            worst?.State ?? HealthState.Fine,
            worst?.Severity ?? 0,
            string.Empty,
            default);
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
