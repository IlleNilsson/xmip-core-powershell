using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">The scripts that upgrade each Xmip database on a
/// server from the schema version it holds.</para>
/// </summary>
/// <remarks>
/// Every Xmip database records the schema version it holds, and every
/// surface shows it at <c>xmip:///&lt;cluster&gt;/node/&lt;node&gt;/storage/&lt;domain&gt;</c>
/// (open problem 41; ADR-0015, amendment 2026-10-20). A release's schema only
/// adds to the one before; this answers the difference, one
/// <see cref="DatabaseScript"/> to each database on <c>-Server</c>, named
/// <c>04-&lt;domain&gt;-upgrade-&lt;from&gt;-&lt;to&gt;.sql</c>, for IT to run as
/// the schema's owner — written into <c>-Path</c> where one is given, nothing
/// overwritten. The scripts are the runtime's, reached through the
/// <see cref="RuntimeDatabases"/> <c>xmip database</c> uses, and written
/// by the <see cref="DatabaseScripts"/> it uses.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipDatabaseScript", SupportsShouldProcess = true)]
[OutputType(typeof(DatabaseScript))]
public sealed class GetXmipDatabaseScriptCommand : XmipCommand
{
    private RuntimeDatabases? _databases;

    /// <summary>
    /// <para type="description">The database server: postgresql, mssql or
    /// mariadb.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateSet("postgresql", "mssql", "mariadb")]
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The schema version the databases hold.</para>
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateRange(1, uint.MaxValue)]
    public uint From { get; set; }

    /// <summary>
    /// <para type="description">The version to bring them to; this build's
    /// unless given.</para>
    /// </summary>
    [Parameter]
    [ValidateRange(1, uint.MaxValue)]
    public uint To { get; set; }

    /// <summary>
    /// <para type="description">The folder the scripts are written into, made
    /// where it is missing; the scripts are only emitted unless given.
    /// Nothing is overwritten.</para>
    /// </summary>
    [Parameter]
    public string? Path { get; set; }

    /// <summary>
    /// <para type="description">The runtime's native library to ask.
    /// Omitted, it is found by the one rule: RuntimeLibrary in the module's
    /// document, else XMIP_RUNTIME_LIBRARY, else beside the module.</para>
    /// </summary>
    [Parameter]
    public string? Library { get; set; }

    /// <inheritdoc />
    protected override void Begin()
    {
        string? library = string.IsNullOrWhiteSpace(Library)
            ? null
            : GetUnresolvedProviderPathFromPSPath(Library);
        _ = ModuleSurface.Runtime(library);

        try
        {
            _databases = RuntimeLibrary.Rules.Databases;
        }
        catch (InvalidOperationException unloaded)
        {
            Stop(new ErrorRecord(
                unloaded, "XmipRuntimeUnloadable", ErrorCategory.ResourceUnavailable, Library));
        }
    }

    /// <inheritdoc />
    protected override void Process()
    {
        DatabaseUpgrade upgrade = _databases!.Upgrade(Server, From, To);

        if (!upgrade.Ok)
        {
            Refuse(new ErrorRecord(
                new InvalidOperationException(upgrade.Refused),
                "XmipDatabaseUpgradeRefused",
                ErrorCategory.InvalidArgument,
                Server));

            return;
        }

        if (Path is not null)
        {
            string folder = GetUnresolvedProviderPathFromPSPath(Path);

            if (!ShouldProcess(folder, $"Write the {Server} upgrade from version {From}"))
            {
                return;
            }

            if (DatabaseScripts.Write(upgrade, folder, out string refused) is null)
            {
                Refuse(new ErrorRecord(
                    new IOException(refused),
                    "XmipDatabaseScriptThere",
                    ErrorCategory.ResourceExists,
                    folder));

                return;
            }
        }

        WriteObject(upgrade.Scripts, enumerateCollection: true);
    }
}
