namespace Xmip.PowerShell.Interop;

/// <summary>
/// Every status the boundary can return. Section 3 of the header.
/// </summary>
/// <remarks>
/// The numbers are grouped by who is at fault, and the grouping is the useful
/// part: a caller error will fail again unchanged, a data error means the input
/// is wrong rather than the call, and an environment error may well succeed on
/// its own later.
/// </remarks>
internal enum XmipStatus
{
    Ok = 0,

    // Caller error. The call was wrong; repeating it unchanged will fail again.
    Invalid = -1,
    Unsupported = -2,
    State = -3,
    NotFound = -4,

    // Data. The input is at fault, not the caller and not the environment.
    Malformed = -10,
    Contract = -11,
    Truncated = -12,

    // Environment.
    Io = -20,
    Timeout = -21,
    Unavailable = -22,
    Auth = -23,
    Capacity = -24,

    // Control.
    Cancelled = -30,
    Again = -31,

    // Terminal. The module instance is unusable and must be destroyed.
    Internal = -40,
    Panic = -41,
}

internal static class XmipStatusExtensions
{
    /// <summary>
    /// Whether trying again could plausibly succeed.
    /// </summary>
    /// <remarks>
    /// A property of the code, not of the call site, so that
    /// xmip-core-resilience can decide without knowing the module. Timeout,
    /// Unavailable, Capacity and Again. Nothing else — and in particular not
    /// <see cref="XmipStatus.Io"/>, which covers faults that will repeat.
    /// </remarks>
    public static bool IsRetryable(this XmipStatus status) => status
        is XmipStatus.Timeout
        or XmipStatus.Unavailable
        or XmipStatus.Capacity
        or XmipStatus.Again;

    /// <summary>
    /// Whether the module instance is finished and must be destroyed.
    /// </summary>
    public static bool IsTerminal(this XmipStatus status) => status
        is XmipStatus.Internal
        or XmipStatus.Panic;

    public static string Explain(this XmipStatus status) => status switch
    {
        XmipStatus.Ok => "the call succeeded",

        XmipStatus.Invalid => "an argument was outside its contract",
        XmipStatus.Unsupported => "well formed, and not implemented here",
        XmipStatus.State => "the wrong lifecycle state for this call",
        XmipStatus.NotFound => "the thing asked for does not exist",

        XmipStatus.Malformed => "not the standard it claims to be",
        XmipStatus.Contract => "well formed, and violates the contract",
        XmipStatus.Truncated => "the stream ended mid-structure",

        XmipStatus.Io => "an input or output fault",
        XmipStatus.Timeout => "the call did not answer in time",
        XmipStatus.Unavailable => "the peer refused, or is down",
        XmipStatus.Auth => "authentication or authorisation was refused",
        XmipStatus.Capacity => "a quota, limit or resource was exhausted",

        XmipStatus.Cancelled => "the host asked for cancellation",
        XmipStatus.Again => "would block; not a failure",

        XmipStatus.Internal => "a defect in the module",
        XmipStatus.Panic => "unwinding was caught at the boundary",

        _ => "not a status this build knows",
    };
}
