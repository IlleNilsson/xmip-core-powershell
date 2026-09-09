using System.Management.Automation;
using Xmip.Abi.Module;
using Xmip.Abi.Operate;

namespace Xmip.PowerShell;

// The first cmdlets that reach a running Xmip. ADR-0027 said the three in
// Commands.cs describe the binding and none of them talks to a runtime; these
// two cross the operator boundary in xmip_operate.h, through the same
// Xmip.Abi.Operate.Operator the cli and the GUI hold. Objects out, never text.

/// <summary>
/// <para type="synopsis">Health at and beneath a scope, from a runtime
/// library.</para>
/// </summary>
/// <remarks>
/// Worst first. Each record is the runtime's own snapshot — a state, how far
/// from healthy, the one line of evidence, and when it was observed, so a
/// stalled publisher is not mistaken for an idle estate (ADR-0027 clause 6).
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipHealth")]
[OutputType(typeof(HealthRecord))]
public sealed class GetXmipHealthCommand : PSCmdlet
{
    private Operator? _runtime;

    /// <summary>
    /// <para type="description">Path to the runtime's native library, the one
    /// exporting xmip_operate_v1.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    public string Library { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The Xmip URI to ask about, such as
    /// xmip:///edge-01. Everything beneath it is included.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 1, ValueFromPipeline = true)]
    public string[] Scope { get; set; } = [];

    protected override void BeginProcessing()
    {
        _runtime = LoadRuntime(this, Library);
    }

    protected override void ProcessRecord()
    {
        foreach (var scope in Scope)
        {
            var records = _runtime!.Health(scope);

            if (records.Count == 0)
            {
                WriteError(new ErrorRecord(
                    new ItemNotFoundException($"Nothing at {scope}."),
                    "XmipScopeNotFound",
                    ErrorCategory.ObjectNotFound,
                    scope));

                continue;
            }

            foreach (var record in records)
            {
                WriteObject(record);
            }
        }
    }

    protected override void EndProcessing()
    {
        _runtime?.Dispose();
    }

    protected override void StopProcessing()
    {
        _runtime?.Dispose();
    }

    /// <summary>
    /// Load the runtime for a cmdlet, or end the cmdlet with the reason. A
    /// runtime that cannot be loaded is a terminating condition: there is no
    /// next scope to try.
    /// </summary>
    internal static Operator LoadRuntime(PSCmdlet cmdlet, string library)
    {
        var path = cmdlet.GetUnresolvedProviderPathFromPSPath(library);
        var runtime = Operator.Load(path, out var reason);

        if (runtime is null)
        {
            cmdlet.ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException(reason),
                "XmipRuntimeUnloadable",
                ErrorCategory.ResourceUnavailable,
                path));
        }

        return runtime!;
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
/// applied.
/// </remarks>
[Cmdlet(VerbsDiagnostic.Test, "XmipNodeConfiguration")]
[OutputType(typeof(NodeConfigurationInfo))]
public sealed class TestXmipNodeConfigurationCommand : PSCmdlet
{
    private Operator? _runtime;

    /// <summary>
    /// <para type="description">Path to the runtime's native library, the one
    /// exporting xmip_validate_v1.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    public string Library { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Path to the node configuration TOML.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 1, ValueFromPipeline = true)]
    public string[] Path { get; set; } = [];

    protected override void BeginProcessing()
    {
        _runtime = GetXmipHealthCommand.LoadRuntime(this, Library);
    }

    protected override void ProcessRecord()
    {
        foreach (var configuration in Path)
        {
            var path = GetUnresolvedProviderPathFromPSPath(configuration);

            if (!File.Exists(path))
            {
                WriteError(new ErrorRecord(
                    new FileNotFoundException($"No file at {path}.", path),
                    "XmipNodeConfigurationMissing",
                    ErrorCategory.ObjectNotFound,
                    path));

                continue;
            }

            var answer = _runtime!.Validate(File.ReadAllText(path));

            WriteObject(new NodeConfigurationInfo(
                path,
                answer.IsValid,
                (int)answer.Status,
                answer.Status.Explain(),
                [.. answer.Problems]));
        }
    }

    protected override void EndProcessing()
    {
        _runtime?.Dispose();
    }

    protected override void StopProcessing()
    {
        _runtime?.Dispose();
    }
}

/// <summary>What <see cref="TestXmipNodeConfigurationCommand"/> answers.</summary>
public sealed record NodeConfigurationInfo(
    string Path,
    bool Valid,
    int Status,
    string StatusMeaning,
    string[] Problems);
