using System.Management.Automation;
using Xmip.Surface;

namespace Xmip.PowerShell;

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
    private ConfigurationRuntime? _runtime;

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
        _runtime = new ConfigurationRuntime(ModuleSurface.Runtime(library));

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
