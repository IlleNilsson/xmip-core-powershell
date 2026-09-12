using Xmip.Abi.Operate;
using Xmip.Surface;
using System.Globalization;

namespace Xmip.PowerShell;

/// <summary>One coloured part of the compact prompt segment.</summary>
public sealed record XmipPromptPart(string Text, ConsoleColor Color);

/// <summary>A prompt-safe, independently coloured rendering of a snapshot.</summary>
public sealed record XmipPromptSegment(IReadOnlyList<XmipPromptPart> Parts)
{
    public XmipPromptSegment(string text, ConsoleColor color)
        : this([new XmipPromptPart(text, color)])
    {
    }

    /// <summary>The uncoloured equivalent, useful outside an interactive host.</summary>
    public string Text => string.Concat(Parts.Select(part => part.Text));
}

/// <summary>
/// Follows Xmip in the background and exposes only an atomic cached segment to
/// PowerShell's prompt function. The prompt thread never touches the runtime.
/// </summary>
public static class PromptMonitor
{
    private static readonly object Gate = new();
    private static CancellationTokenSource? _stop;
    private static Task? _worker;
    private static XmipPromptSegment _current = new("[Xmip connecting]", ConsoleColor.DarkGray);
    private static IOperatorSurface? _surface;
    private static IReadOnlyList<HealthRecord> _health = [];
    private static ActivitySummary _activity = new(
        ScopeTree.Root, null, null, null, null, null, null);

    /// <summary>The latest cached segment; reading it cannot block.</summary>
    public static XmipPromptSegment Current => Volatile.Read(ref _current);

    /// <summary>The latest health snapshot used by the prompt and provider.</summary>
    public static IReadOnlyList<HealthRecord> Health => Volatile.Read(ref _health);

    /// <summary>The latest cluster activity used by the prompt and provider.</summary>
    public static ActivitySummary Activity => Volatile.Read(ref _activity);

    /// <summary>
    /// Activity for an explicit provider query. The prompt itself never calls
    /// this; it reads <see cref="Activity"/> without blocking.
    /// </summary>
    public static ActivitySummary ActivityAt(string scope)
    {
        if (scope == ScopeTree.Root)
        {
            return Activity;
        }

        IOperatorSurface? surface = Volatile.Read(ref _surface);

        try
        {
            return surface?.Activity(scope)
                ?? new ActivitySummary(scope, null, null, null, null, null, null);
        }
        catch (ObjectDisposedException)
        {
            return new ActivitySummary(scope, null, null, null, null, null, null);
        }
    }

    /// <summary>Describe one provider path through the shared surface model.</summary>
    public static ScopeItem ScopeAt(string scope)
    {
        IOperatorSurface? surface = Volatile.Read(ref _surface);

        try
        {
            return surface?.Describe(scope)
                ?? ScopeItem.From(scope,
                    [.. Health.Where(record => ScopeTree.Beneath(record.Scope, scope))],
                    ActivityAt(scope));
        }
        catch (ObjectDisposedException)
        {
            return ScopeItem.From(scope,
                [.. Health.Where(record => ScopeTree.Beneath(record.Scope, scope))],
                ActivityAt(scope));
        }
    }

    /// <summary>Read direct provider children through the shared surface model.</summary>
    public static IReadOnlyList<ScopeItem> ChildrenAt(string scope)
    {
        IOperatorSurface? surface = Volatile.Read(ref _surface);

        try
        {
            return surface?.Children(scope) ?? [];
        }
        catch (ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>Begin observing once. Safe to call on repeated imports.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_worker is { IsCompleted: false })
            {
                return;
            }

            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            _current = new XmipPromptSegment("[Xmip connecting]", ConsoleColor.DarkGray);
            _worker = Task.Run(() => ObserveAsync(_stop.Token));
        }
    }

    /// <summary>End observation. The worker owns and later disposes its surface.</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            _stop?.Cancel();
        }
    }

    private static async Task ObserveAsync(CancellationToken stop)
    {
        IOperatorSurface? surface = null;

        try
        {
            surface = OpenSurface();
            Volatile.Write(ref _surface, surface);

            await foreach (SurfaceChange _ in surface.WatchAsync(stop).ConfigureAwait(false))
            {
                Publish(surface);
            }
        }
        catch (OperationCanceledException)
        {
            // Remove-Module is the normal end.
        }
        catch (Exception)
        {
            Volatile.Write(
                ref _current,
                new XmipPromptSegment("[Xmip unavailable]", ConsoleColor.DarkRed));
        }
        finally
        {
            Volatile.Write(ref _surface, null);
            if (surface is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static IOperatorSurface OpenSurface()
    {
        string? snapshot = Environment.GetEnvironmentVariable("XMIP_SNAPSHOT");

        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            return new SnapshotOperator(Path.GetFullPath(snapshot));
        }

        string moduleDirectory = Path.GetDirectoryName(typeof(PromptMonitor).Assembly.Location)
            ?? AppContext.BaseDirectory;
        string runtime = RuntimeLibrary.Choose(
            configured: null,
            Environment.GetEnvironmentVariable(RuntimeLibrary.EnvironmentVariable),
            moduleDirectory,
            moduleDirectory);

        return new NativeOperator(runtime);
    }

    private static void Publish(IOperatorSurface surface)
    {
        IReadOnlyList<HealthRecord> records = surface.Health(ScopeTree.Root);
        ActivitySummary activity = surface.Activity(ScopeTree.Root);
        Volatile.Write(ref _health, records);
        Volatile.Write(ref _activity, activity);

        if (records.Count == 0 && !activity.HasValues)
        {
            Volatile.Write(
                ref _current,
                new XmipPromptSegment("[Xmip unavailable]", ConsoleColor.DarkRed));
            return;
        }

        Volatile.Write(
            ref _current,
            new XmipPromptSegment(
            [
                new("[", ConsoleColor.DarkGray),
                Part("R", activity.Received, ConsoleColor.Cyan),
                new(" ", ConsoleColor.DarkGray),
                Part("P", activity.Processed, ConsoleColor.Blue),
                new(" ", ConsoleColor.DarkGray),
                Part("S", activity.Sent, ConsoleColor.Green),
                new(" ", ConsoleColor.DarkGray),
                Part("T", activity.Retrying, ConsoleColor.Yellow),
                new(" ", ConsoleColor.DarkGray),
                Part("F", activity.Failed, ConsoleColor.Red),
                new("]", ConsoleColor.DarkGray),
            ]));
    }

    /// <summary>Apply a provider action to a live scope.</summary>
    public static ScopeOperation Control(string scope, string action, string who)
    {
        IOperatorSurface surface = Volatile.Read(ref _surface)
            ?? throw new InvalidOperationException("Xmip operator surface is not connected.");

        ScopeAction parsed = action.ToLowerInvariant() switch
        {
            "paused" or "pause" => ScopeAction.Pause,
            "resume" or "resumed" => ScopeAction.Resume,
            "start" or "started" => ScopeAction.Start,
            "stop" or "stopped" => ScopeAction.Stop,
            "restart" or "restarted" => ScopeAction.Restart,
            _ => throw new ArgumentException(
                "Action must be Pause, Resume, Start, Stop, or Restart.", nameof(action)),
        };

        return surface.ControlScope(scope, parsed, who);
    }

    private static string Figure(ulong? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture) ?? "–";

    private static XmipPromptPart Part(string letter, ulong? value, ConsoleColor active) =>
        new($"{letter}{Figure(value)}", value is > 0 ? active : ConsoleColor.DarkGray);
}
