using System.Management.Automation;
using Xmip.Abi.Module;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>The runtime information <see cref="GetXmipRuntimeCommand"/> reads.</summary>
public enum RuntimeView
{
    Health,
    Abi,
    Status,
    Module
}

/// <summary>The runtime invariant <see cref="TestXmipRuntimeCommand"/> tests.</summary>
public enum RuntimeTest
{
    NodeConfiguration
}

/// <summary>The state <see cref="SetXmipRuntimeCommand"/> applies to a scope.</summary>
public enum RuntimeState
{
    Paused,
    Running
}

/// <summary>
/// <para type="synopsis">One view of Xmip's runtime boundary.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "XmipRuntime")]
[OutputType(
    typeof(HealthRecord),
    typeof(AbiInfo),
    typeof(StatusInfo),
    typeof(ModuleDescriptorInfo))]
public sealed class GetXmipRuntimeCommand : PSCmdlet
{
    private Operator? _runtime;

    /// <summary><para type="description">The runtime information to read.</para></summary>
    [Parameter(Position = 0)]
    public RuntimeView View { get; set; } = RuntimeView.Health;

    /// <summary><para type="description">A native runtime or Module library.</para></summary>
    [Parameter]
    public string Library { get; set; } = string.Empty;

    /// <summary><para type="description">Xmip URIs whose health is read.</para></summary>
    [Parameter(ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    public string[] Scope { get; set; } = [];

    /// <summary><para type="description">Boundary status codes to explain.</para></summary>
    [Parameter(ValueFromPipelineByPropertyName = true)]
    public int[] Code { get; set; } = [];

    /// <inheritdoc />
    protected override void BeginProcessing()
    {
        RuntimeCommandGuard.RequireLibrary(View, Library, ThrowTerminatingError);

        if (View == RuntimeView.Health)
        {
            _runtime = RuntimeCommandGuard.Load(
                GetUnresolvedProviderPathFromPSPath,
                ThrowTerminatingError,
                Library);
        }
    }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        RuntimeCommandGuard.RequireInput(View, Scope, Code, ThrowTerminatingError);

        switch (View)
        {
            case RuntimeView.Abi:
                WriteObject(RuntimeReader.Abi());
                break;
            case RuntimeView.Status:
                WriteObject(RuntimeReader.Status(Code), true);
                break;
            case RuntimeView.Module:
                WriteObject(RuntimeReader.Module(
                    GetUnresolvedProviderPathFromPSPath,
                    WriteVerbose,
                    WriteError,
                    Library));
                break;
            default:
                WriteObject(RuntimeReader.Health(_runtime!, Scope, WriteError), true);
                break;
        }
    }

    /// <inheritdoc />
    protected override void EndProcessing() => _runtime?.Dispose();

    /// <inheritdoc />
    protected override void StopProcessing() => _runtime?.Dispose();
}

/// <summary>
/// <para type="synopsis">Whether a runtime input satisfies an Xmip invariant.</para>
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "XmipRuntime")]
[OutputType(typeof(NodeConfigurationInfo))]
public sealed class TestXmipRuntimeCommand : PSCmdlet
{
    private Operator? _runtime;

    /// <summary><para type="description">The invariant to test.</para></summary>
    [Parameter(Position = 0)]
    public RuntimeTest Target { get; set; } = RuntimeTest.NodeConfiguration;

    /// <summary><para type="description">The runtime native library.</para></summary>
    [Parameter(Mandatory = true)]
    public string Library { get; set; } = string.Empty;

    /// <summary><para type="description">Node configuration TOML files.</para></summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public string[] Path { get; set; } = [];

    /// <inheritdoc />
    protected override void BeginProcessing()
    {
        _runtime = RuntimeCommandGuard.Load(
            GetUnresolvedProviderPathFromPSPath,
            ThrowTerminatingError,
            Library);
    }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        WriteObject(RuntimeReader.Configuration(
            GetUnresolvedProviderPathFromPSPath,
            WriteError,
            _runtime!,
            Path), true);
    }

    /// <inheritdoc />
    protected override void EndProcessing() => _runtime?.Dispose();

    /// <inheritdoc />
    protected override void StopProcessing() => _runtime?.Dispose();
}

/// <summary>
/// <para type="synopsis">Pauses or resumes everything at and beneath a scope.</para>
/// </summary>
[Cmdlet(VerbsCommon.Set, "XmipRuntime", SupportsShouldProcess = true)]
[OutputType(typeof(ScopeOperation))]
public sealed class SetXmipRuntimeCommand : PSCmdlet
{
    private Operator? _runtime;

    /// <summary><para type="description">The runtime native library.</para></summary>
    [Parameter(Mandatory = true)]
    public string Library { get; set; } = string.Empty;

    /// <summary><para type="description">Xmip URIs to pause or resume.</para></summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public string[] Scope { get; set; } = [];

    /// <summary><para type="description">Paused or Running.</para></summary>
    [Parameter(Mandatory = true)]
    public RuntimeState State { get; set; }

    /// <summary><para type="description">Who requested a pause.</para></summary>
    [Parameter]
    public string Who { get; set; } = Environment.UserName;

    /// <inheritdoc />
    protected override void BeginProcessing()
    {
        _runtime = RuntimeCommandGuard.Load(
            GetUnresolvedProviderPathFromPSPath,
            ThrowTerminatingError,
            Library);
    }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        foreach (var scope in Scope)
        {
            if (ShouldProcess(scope, State.ToString()))
            {
                ScopeOperation operation = RuntimeWriter.Set(_runtime!, scope, State, Who);

                if (!operation.Applied)
                {
                    WriteError(new ErrorRecord(
                        new InvalidOperationException(operation.Result),
                        "XmipScopeOperationRefused",
                        ErrorCategory.InvalidOperation,
                        scope));

                    continue;
                }

                WriteObject(operation);
            }
        }
    }

    /// <inheritdoc />
    protected override void EndProcessing() => _runtime?.Dispose();

    /// <inheritdoc />
    protected override void StopProcessing() => _runtime?.Dispose();
}
