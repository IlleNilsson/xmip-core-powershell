using System.Management.Automation;
using Xmip.Abi;
using Xmip.Abi.Module;

namespace Xmip.PowerShell;

// The three operator answers that describe the binding, as cmdlets. Each is
// the same answer `xmip` gives on the command line — abi, status, probe — and
// must stay so: two surfaces disagreeing over one boundary is the BizTalk
// console-versus-provider drift ADR-0014 exists to prevent. Since 2026-09-09
// that is held by construction rather than by care: both call Xmip.Abi in
// xmip-core-abi, the one binding over the header; since 2026-09-24 both take
// the answer itself from there too — AbiBoundaries, StatusMeaning and the
// probe's own judgement — and render or emit it.
//
// Objects out, never text. The cli renders for a human; a cmdlet's caller
// pipes, filters and compares, and a rendered string can do none of that.

/// <summary>
/// <para type="synopsis">The module boundary this build speaks.</para>
/// </summary>
/// <remarks>
/// Both boundaries, versioned apart (ADR-0027 clause 2): the module boundary
/// a Module plugs into and the operator boundary a surface drives from — the
/// same <see cref="AbiBoundaries"/> <c>xmip-cli abi</c> prints.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipAbi")]
[OutputType(typeof(AbiBoundaries))]
public sealed class GetXmipAbiCommand : Cmdlet
{
    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        WriteObject(AbiBoundaries.Current);
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
public sealed class ConvertFromXmipStatusCommand : Cmdlet
{
    /// <summary>
    /// <para type="description">The code as the boundary returned it.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public int[] Code { get; set; } = [];

    protected override void ProcessRecord()
    {
        foreach (var code in Code)
        {
            WriteObject(StatusMeaning.Of(code));
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
/// deliberately so. The one difference is earned rather than drift — the cli
/// writes module log lines to stderr as they arrive, while a cmdlet has a
/// verbose stream, so here they are collected and emitted after the probe.
/// Whether the module conforms is the probe's own judgement
/// (<see cref="ModuleProbe.Result.Complaint"/>), the one the cli exits on.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipModuleDescriptor")]
[OutputType(typeof(ModuleDescriptorInfo))]
public sealed class GetXmipModuleDescriptorCommand : PSCmdlet
{
    /// <summary>
    /// <para type="description">Path to the loadable library.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string[] Library { get; set; } = [];

    protected override void ProcessRecord()
    {
        foreach (var library in Library)
        {
            var path = GetUnresolvedProviderPathFromPSPath(library);
            List<string> log = [];

            ModuleProbe.Result answer;

            try
            {
                answer = ModuleProbe.Probe(path, log.Add);
            }
            catch (Exception failure) when
                (failure is DllNotFoundException
                    or EntryPointNotFoundException
                    or BadImageFormatException)
            {
                WriteError(new ErrorRecord(
                    failure, "XmipModuleUnloadable", ErrorCategory.InvalidData, path));

                continue;
            }

            foreach (var line in log)
            {
                WriteVerbose(line);
            }

            WriteObject(new ModuleDescriptorInfo(
                path,
                (int)answer.Status,
                answer.Status.Explain(),
                answer.Provider,
                answer.Module,
                answer.Standard,
                answer.AbiVersion,
                answer.TraitVersion,
                answer.ModuleVersion,
                answer.LastError,
                answer.Conforms,
                answer.Complaint));
        }
    }
}

/// <summary>What <see cref="GetXmipModuleDescriptorCommand"/> answers.</summary>
public sealed record ModuleDescriptorInfo(
    string Library,
    int Status,
    string StatusMeaning,
    string Provider,
    string Module,
    string Standard,
    uint AbiVersion,
    string TraitVersion,
    string ModuleVersion,
    string LastError,
    bool Conforms,
    string Complaint);
