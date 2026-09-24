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
    protected override void ProcessRecord()
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
                    WriteError(new ErrorRecord(
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
public sealed class TestXmipNodeConfigurationCommand : PSCmdlet
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
    protected override void BeginProcessing()
    {
        string? library = string.IsNullOrWhiteSpace(Library)
            ? null
            : GetUnresolvedProviderPathFromPSPath(Library);
        _runtime = new NativeOperator(ModuleSurface.Runtime(library));

        if (!_runtime.IsLoaded)
        {
            // A runtime that cannot be loaded is a terminating condition:
            // there is no next document it could judge.
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException(_runtime.Reason),
                "XmipRuntimeUnloadable",
                ErrorCategory.ResourceUnavailable,
                _runtime.Path));
        }
    }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        foreach (string configuration in Path)
        {
            string path = GetUnresolvedProviderPathFromPSPath(configuration);
            ConfigurationVerdict verdict = _runtime!.Validate(path);

            if (!File.Exists(path))
            {
                WriteError(new ErrorRecord(
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
    protected override void EndProcessing()
    {
        _runtime?.Dispose();
    }

    /// <inheritdoc />
    protected override void StopProcessing()
    {
        _runtime?.Dispose();
    }
}
