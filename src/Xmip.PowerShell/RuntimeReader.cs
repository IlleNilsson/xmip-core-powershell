using System.Management.Automation;
using Xmip.Abi.Module;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>The private implementation behind the three runtime cmdlets.</summary>
internal static class RuntimeReader
{
    internal static AbiInfo Abi()
    {
        return new AbiInfo(
            ModuleAbi.AbiVersion,
            ModuleAbi.Entrypoint,
            ModuleAbi.LibraryFileName("xmip_core_transport_file"));
    }

    internal static IEnumerable<StatusInfo> Status(IEnumerable<int> codes)
    {
        foreach (var code in codes)
        {
            var status = (XmipStatus)code;

            yield return new StatusInfo(
                code,
                Enum.IsDefined(status) ? status.ToString() : "Unknown",
                status.Explain(),
                status.IsRetryable(),
                status.IsTerminal());
        }
    }

    internal static ModuleDescriptorInfo? Module(
        Func<string, string> resolve,
        Action<string> verbose,
        Action<ErrorRecord> error,
        string library)
    {
        var path = resolve(library);
        List<string> log = [];

        try
        {
            ModuleProbe.Result answer = ModuleProbe.Probe(path, log.Add);
            log.ForEach(verbose);

            return new ModuleDescriptorInfo(
                path,
                (int)answer.Status,
                answer.Status.Explain(),
                answer.Provider,
                answer.Module,
                answer.Standard,
                answer.AbiVersion,
                answer.TraitVersion,
                answer.ModuleVersion,
                answer.LastError);
        }
        catch (Exception failure) when (failure is
            DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error(new ErrorRecord(
                failure, "XmipModuleUnloadable", ErrorCategory.InvalidData, path));

            return null;
        }
    }

    internal static IEnumerable<HealthRecord> Health(
        Operator runtime,
        IEnumerable<string> scopes,
        Action<ErrorRecord> error)
    {
        foreach (string scope in scopes)
        {
            IReadOnlyList<HealthRecord> records = runtime.Health(scope);

            if (records.Count == 0)
            {
                error(new ErrorRecord(
                    new ItemNotFoundException($"Nothing at {scope}."),
                    "XmipScopeNotFound",
                    ErrorCategory.ObjectNotFound,
                    scope));

                continue;
            }

            foreach (HealthRecord record in records)
            {
                yield return record;
            }
        }
    }

    internal static IEnumerable<NodeConfigurationInfo> Configuration(
        Func<string, string> resolve,
        Action<ErrorRecord> error,
        Operator runtime,
        IEnumerable<string> configurations)
    {
        foreach (var configuration in configurations)
        {
            var path = resolve(configuration);

            if (!File.Exists(path))
            {
                error(new ErrorRecord(
                    new FileNotFoundException($"No file at {path}.", path),
                    "XmipNodeConfigurationMissing",
                    ErrorCategory.ObjectNotFound,
                    path));

                continue;
            }

            var answer = runtime.Validate(File.ReadAllText(path));

            yield return new NodeConfigurationInfo(
                path,
                answer.IsValid,
                (int)answer.Status,
                answer.Status.Explain(),
                [.. answer.Problems]);
        }
    }
}

/// <summary>Arguments and loading shared by runtime commands.</summary>
internal static class RuntimeCommandGuard
{
    internal static Operator Load(
        Func<string, string> resolve,
        Action<ErrorRecord> terminate,
        string library)
    {
        var path = resolve(library);
        var runtime = Operator.Load(path, out var reason);

        if (runtime is null)
        {
            terminate(new ErrorRecord(
                new InvalidOperationException(reason),
                "XmipRuntimeUnloadable",
                ErrorCategory.ResourceUnavailable,
                path));
        }

        return runtime!;
    }

    internal static void RequireLibrary(
        RuntimeView view,
        string library,
        Action<ErrorRecord> terminate)
    {
        bool missing = view switch
        {
            RuntimeView.Health => string.IsNullOrWhiteSpace(library),
            RuntimeView.Module => string.IsNullOrWhiteSpace(library),
            _ => false
        };

        if (missing)
        {
            terminate(new ErrorRecord(
                new ArgumentException($"The {view} view is missing a required parameter."),
                "XmipRuntimeArgumentMissing",
                ErrorCategory.InvalidArgument,
                view));
        }
    }

    internal static void RequireInput(
        RuntimeView view,
        string[] scopes,
        int[] codes,
        Action<ErrorRecord> terminate)
    {
        bool missing = view switch
        {
            RuntimeView.Health => scopes.Length == 0,
            RuntimeView.Status => codes.Length == 0,
            _ => false
        };

        if (missing)
        {
            terminate(new ErrorRecord(
                new ArgumentException($"The {view} view is missing its input."),
                "XmipRuntimeInputMissing",
                ErrorCategory.InvalidArgument,
                view));
        }
    }
}

/// <summary>The private mutation behind Set-XmipRuntime.</summary>
internal static class RuntimeWriter
{
    internal static ScopeOperation Set(
        Operator runtime,
        string scope,
        RuntimeState state,
        string who)
    {
        ScopeAction action = state == RuntimeState.Paused ? ScopeAction.Pause : ScopeAction.Resume;
        XmipStatus status = state == RuntimeState.Paused
            ? runtime.PauseScope(scope, who)
            : runtime.ResumeScope(scope);
        string result = state == RuntimeState.Paused
            ? English.Paused(scope, status)
            : English.Resumed(scope, status);

        return new ScopeOperation(scope, action, status == XmipStatus.Ok, result);
    }
}
