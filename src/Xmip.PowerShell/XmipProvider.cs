using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Provider;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// The live Xmip namespace. Paths map directly to Xmip scope URIs; no
/// filesystem is invented beneath them. Set-Item applies scope operations.
/// </summary>
[CmdletProvider("Xmip", ProviderCapabilities.ShouldProcess)]
public sealed class XmipProvider : NavigationCmdletProvider
{
    protected override Collection<PSDriveInfo> InitializeDefaultDrives()
    {
        return
        [
            new PSDriveInfo(
                "Xmip", ProviderInfo, string.Empty,
                "Live Xmip nodes, services, processes and locations", null),
        ];
    }

    protected override bool IsValidPath(string path) => path is not null;

    protected override bool ItemExists(string path)
    {
        string scope = ToScope(path);
        return scope == ScopeTree.Root
            || PromptMonitor.Health.Any(record => ScopeTree.Beneath(record.Scope, scope));
    }

    protected override bool IsItemContainer(string path)
    {
        string scope = ToScope(path);
        int depth = ScopeTree.Parts(scope).Length;

        return PromptMonitor.Health.Any(record =>
            ScopeTree.Beneath(record.Scope, scope)
            && ScopeTree.Parts(record.Scope).Length > depth);
    }

    protected override void GetItem(string path)
    {
        string scope = ToScope(path);

        if (!ItemExists(path))
        {
            WriteError(new ErrorRecord(
                new ItemNotFoundException($"Nothing at {scope}."),
                "XmipScopeNotFound", ErrorCategory.ObjectNotFound, scope));
            return;
        }

        WriteItemObject(Item(scope), path, IsItemContainer(path));
    }

    protected override void GetChildItems(string path, bool recurse)
    {
        WriteChildren(path, recurse);
    }

    protected override void GetChildNames(string path, ReturnContainers returnContainers)
    {
        foreach (ScopeItem child in PromptMonitor.ChildrenAt(ToScope(path)))
        {
            _ = returnContainers;
            WriteItemObject(child.Name, child.Name, child.IsContainer);
        }
    }

    protected override void SetItem(string path, object value)
    {
        string scope = ToScope(path);
        string action = LanguagePrimitives.ConvertTo<string>(value);
        string who = Environment.UserName;

        if (ShouldProcess(scope, action))
        {
            ScopeOperation operation = PromptMonitor.Control(scope, action, who);

            if (!operation.Applied)
            {
                WriteError(new ErrorRecord(
                    new InvalidOperationException(operation.Result),
                    "XmipScopeOperationRefused", ErrorCategory.InvalidOperation, scope));
                return;
            }

            WriteItemObject(operation, path, IsItemContainer(path));
        }
    }

    private void WriteChildren(string path, bool recurse)
    {
        foreach (ScopeItem child in PromptMonitor.ChildrenAt(ToScope(path)))
        {
            string childPath = MakePath(path, child.Name);
            WriteItemObject(child, childPath, child.IsContainer);

            if (recurse && child.IsContainer)
            {
                WriteChildren(childPath, true);
            }
        }
    }

    private static ScopeItem Item(string scope) => PromptMonitor.ScopeAt(scope);

    /// <summary>Convert a provider path or Xmip URI to the canonical scope.</summary>
    public static string ToScope(string path)
    {
        if (path.StartsWith("xmip://", StringComparison.OrdinalIgnoreCase))
        {
            return ScopeTree.Join(ScopeTree.Parts(path));
        }

        if (path.StartsWith("xmip:", StringComparison.OrdinalIgnoreCase))
        {
            path = path[5..];
        }

        return ScopeTree.Join(
            path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
    }

}
