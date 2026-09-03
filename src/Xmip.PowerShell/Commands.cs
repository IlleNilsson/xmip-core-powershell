using System.Management.Automation;
using Xmip.PowerShell.Interop;

namespace Xmip.PowerShell;

// The three operator answers the boundary can give today, as cmdlets. Each is
// the same answer `xmip` gives on the command line — abi, status, probe — and
// must stay so: two surfaces disagreeing over one boundary is the BizTalk
// console-versus-provider drift ADR-0014 exists to prevent.
//
// Objects out, never text. The cli renders for a human; a cmdlet's caller
// pipes, filters and compares, and a rendered string can do none of that.

/// <summary>
/// <para type="synopsis">The module boundary this build speaks.</para>
/// </summary>
[Cmdlet(VerbsCommon.Get, "XmipAbi")]
[OutputType(typeof(AbiInfo))]
public sealed class GetXmipAbiCommand : Cmdlet
{
    protected override void ProcessRecord()
    {
        WriteObject(new AbiInfo(
            XmipAbi.AbiVersion,
            XmipAbi.Entrypoint,
            XmipAbi.LibraryFileName("xmip_core_transport_file")));
    }
}

/// <summary>What <see cref="GetXmipAbiCommand"/> answers.</summary>
public sealed record AbiInfo(uint AbiVersion, string Entrypoint, string ExampleLibraryName);

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
[OutputType(typeof(StatusInfo))]
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
            var status = (XmipStatus)code;

            WriteObject(new StatusInfo(
                code,
                Enum.IsDefined(status) ? status.ToString() : "Unknown",
                status.Explain(),
                status.IsRetryable(),
                status.IsTerminal()));
        }
    }
}

/// <summary>What <see cref="ConvertFromXmipStatusCommand"/> answers.</summary>
public sealed record StatusInfo(
    int Code, string Name, string Meaning, bool Retryable, bool Terminal);

/// <summary>
/// <para type="synopsis">Load a module library and report what it says it
/// is.</para>
/// </summary>
/// <remarks>
/// The first of the seven conformance rules in section 11 of the header: the
/// module exports the entrypoint, accepts the host's abi_version, fills the
/// descriptor, and destroys cleanly. Log lines the module emits during the
/// probe arrive on the verbose stream.
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

            ModuleProbe.Result answer;

            try
            {
                answer = ModuleProbe.Probe(path);
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

            foreach (var line in answer.Log)
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
                answer.LastError));
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
    string LastError);
