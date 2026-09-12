using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>A prompt-safe rendering of the latest operator snapshot.</summary>
public sealed record XmipPromptSegment(string Text, ConsoleColor Color);

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

    /// <summary>The latest cached segment; reading it cannot block.</summary>
    public static XmipPromptSegment Current => Volatile.Read(ref _current);

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
        HealthState state = ScopeTree.Rollup(records) ?? HealthState.Done;
        string name = records.Count == 0 ? "unavailable" : state.ToString().ToLowerInvariant();

        ConsoleColor color = state switch
        {
            HealthState.Fine => ConsoleColor.Green,
            HealthState.Working => ConsoleColor.Cyan,
            HealthState.Paused or HealthState.Stressed or HealthState.Holding =>
                ConsoleColor.Yellow,
            HealthState.Exhausted or HealthState.Done => ConsoleColor.Red,
            _ => ConsoleColor.DarkGray,
        };

        Volatile.Write(ref _current, new XmipPromptSegment($"[Xmip {name}]", color));
    }
}
