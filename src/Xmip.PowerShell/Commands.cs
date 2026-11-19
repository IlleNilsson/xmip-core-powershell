using System.Management.Automation;
using Xmip.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

// The three operator answers that describe the binding, as cmdlets. Each is
// the same answer `xmip` gives on the command line — abi, status, probe — and
// must stay so: two surfaces disagreeing over one boundary is the BizTalk
// console-versus-provider drift ADR-0014 exists to prevent. Since 2026-09-09
// that is held by construction rather than by care: both call Xmip.Abi in
// xmip-core-abi, the one binding over the header; since 2026-11-17 both take
// the answer itself from the runtime — AbiBoundaries and StatusMeaning,
// section 28.3 — and the probe from the runtime's one loader, through the C
// ABI (xmip_module_probe_v1), and render or emit it.
//
// Objects out, never text. The cli renders for a human; a cmdlet's caller
// pipes, filters and compares, and a rendered string can do none of that.

/// <summary>
/// <para type="synopsis">The module boundary this build speaks.</para>
/// </summary>
/// <remarks>
/// Both boundaries, versioned apart (ADR-0027 clause 2): the module boundary
/// a Module plugs into and the operator boundary a surface drives from — the
/// same <see cref="AbiBoundaries"/> <c>xmip abi</c> prints.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipAbi")]
[OutputType(typeof(AbiBoundaries))]
public sealed class GetXmipAbiCommand : XmipCommand
{
    /// <inheritdoc />
    protected override void Process()
    {
        WriteObject(RuntimeLibrary.Rules.Sentences.Boundaries());
    }
}

/// <summary>
/// <para type="synopsis">What a status code from the boundary means.</para>
/// </summary>
/// <remarks>
/// ConvertFrom, because that is what it does: a number crosses the boundary
/// and becomes something an operator can act on. The grouping is the useful
/// part — a caller error will fail again unchanged, and only some codes are
/// worth a retry.
/// </remarks>
[Cmdlet(VerbsData.ConvertFrom, "XmipStatus")]
[OutputType(typeof(StatusMeaning))]
public sealed class ConvertFromXmipStatusCommand : XmipCommand
{
    /// <summary>
    /// <para type="description">The code as the boundary returned it.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public int[] Code { get; set; } = [];

    /// <inheritdoc />
    protected override void Process()
    {
        foreach (var code in Code)
        {
            WriteObject(RuntimeLibrary.Rules.Sentences.Meaning(code));
        }
    }
}

/// <summary>
/// <para type="synopsis">Load a module library and report what it says it
/// is.</para>
/// </summary>
/// <remarks>
/// The first of the seven conformance rules in section 11 of the header: the
/// module exports the entrypoint, accepts the host's abi_version, fills the
/// descriptor, and destroys cleanly. The same probe as <c>xmip probe</c>, and
/// deliberately so: the runtime's one loader opens the library, as a node
/// opens one, and judges it by the rule a node judges it by
/// (<c>xmip_operate.h</c> section 26; ADR-0012, amendment 2026-11-17). The
/// lines the module logged come back with the answer and go to the verbose
/// stream. Whether the module conforms is the probe's own judgment
/// (<see cref="ModuleProbed.Refusal"/>), the one the cli exits on, and so is
/// whether it loaded at all (<see cref="ModuleProbed.Unloadable"/>); the
/// object written is the probe's own answer.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipModuleDescriptor")]
[OutputType(typeof(ModuleProbed))]
public sealed class GetXmipModuleDescriptorCommand : XmipCommand
{
    /// <summary>
    /// <para type="description">Path to the loadable library.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string[] Library { get; set; } = [];

    private RuntimeModules? _modules;

    /// <inheritdoc />
    protected override void Begin()
    {
        try
        {
            _modules = RuntimeLibrary.Rules.Modules;
        }
        catch (InvalidOperationException unloaded)
        {
            Stop(new ErrorRecord(
                unloaded, "XmipRuntimeUnloadable", ErrorCategory.ResourceUnavailable, null));
        }
    }

    /// <inheritdoc />
    protected override void Process()
    {
        foreach (var library in Library)
        {
            var path = GetUnresolvedProviderPathFromPSPath(library);
            ModuleProbed answer = _modules!.Probe(path);

            if (!answer.Loaded)
            {
                Refuse(new ErrorRecord(
                    new InvalidDataException(answer.Unloadable),
                    "XmipModuleUnloadable",
                    ErrorCategory.InvalidData,
                    path));

                continue;
            }

            foreach (var line in answer.Log)
            {
                WriteVerbose(line);
            }

            WriteObject(answer);
        }
    }
}
