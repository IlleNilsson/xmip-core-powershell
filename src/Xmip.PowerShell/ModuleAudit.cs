using System.Collections;
using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// How this module audits (ADR-0062): as program <c>Xmip.PowerShell</c>,
/// through the audit capability in the runtime's library
/// (<see cref="ProgramAudit"/>), into the directory <c>xmip.powershell.toml</c>
/// names as <c>AuditDirectory</c>, resolved from beside the module — unset,
/// the capability decides: <c>XMIP_AUDIT_DIRECTORY</c>, else the operating
/// system's log. What a cmdlet records is <see cref="XmipCommand"/>'s; what
/// the prompt records is <see cref="PromptMonitor"/>'s. Nothing here writes a
/// record of its own.
/// </summary>
public static class ModuleAudit
{
    /// <summary>The program's name on every record.</summary>
    public const string Program = "Xmip.PowerShell";

    private static int _watching;

    /// <summary>The module's audit, as its document says now. The document is
    /// read at each use, so an edit to it is followed without a re-import.</summary>
    public static ProgramAudit Open()
    {
        return new ProgramAudit(
            Program,
            ProgramAudit.Stated(TomlDocument.Read(ModuleSurface.DocumentPath),
                ModuleSurface.Directory));
    }

    /// <summary>Audit what this module leaves unhandled in the session, once
    /// per loaded copy of the module however often it is imported.</summary>
    public static void Watch()
    {
        if (Interlocked.Exchange(ref _watching, 1) == 0)
        {
            Open().WatchUnhandled();
        }
    }

    /// <summary>What a cmdlet was invoked with, as properties: every bound
    /// parameter and who ran it. None of this module's parameters is a
    /// secret; the user and password a -Remote address may carry are left
    /// out of every record by the audit capability.</summary>
    public static Dictionary<string, string> Properties(InvocationInfo? invocation)
    {
        Dictionary<string, string> said = new(StringComparer.Ordinal)
        {
            ["user"] = Environment.UserName,
        };

        foreach ((string name, object? value) in invocation?.BoundParameters
            ?? new Dictionary<string, object>())
        {
            said[name] = value switch
            {
                null => string.Empty,
                string text => text,
                SwitchParameter flag => flag.IsPresent ? "yes" : "no",
                IEnumerable many => string.Join(", ", many.Cast<object?>()),
                _ => value.ToString() ?? string.Empty,
            };
        }

        return said;
    }

    /// <summary>Record a cmdlet's error as a failure of
    /// <paramref name="action"/>: its exception, and its identifier, category
    /// and target as properties beside <paramref name="properties"/>.</summary>
    public static AuditOutcome? Failed(
        string action, ErrorRecord error, Dictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(properties);

        properties["errorId"] = error.FullyQualifiedErrorId ?? string.Empty;
        properties["category"] = error.CategoryInfo.Category.ToString();

        if (error.TargetObject is { } target)
        {
            properties["target"] = target.ToString() ?? string.Empty;
        }

        return Open().Failed(action, error.Exception, properties);
    }
}
