using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// The non-terminating error <c>Get-XmipScope -FailedJourney</c> writes where
/// a list of the Journeys that failed is no answer, in the sentence
/// <c>xmip-cli journey</c> says it with (<see cref="JourneyOperation.Unlisted"/>):
/// <c>XmipFailedJourneysNotListed</c> where the surface cannot list them,
/// <c>XmipFailedJourneysUnanswered</c> where it asked and Xmip Storage did not
/// answer.
/// </summary>
public static class FailedJourneyError
{
    /// <summary>The error for <paramref name="failed"/>, listed at
    /// <paramref name="scope"/> from <paramref name="source"/>; null where it
    /// is an answer, none failing among them.</summary>
    public static ErrorRecord? Of(FailedJourneyList failed, string scope, string source)
    {
        ArgumentNullException.ThrowIfNull(failed);

        if (failed.Listed)
        {
            return null;
        }

        bool asked = failed.Failure.Length > 0;

        return new ErrorRecord(
            new InvalidOperationException(JourneyOperation.Unlisted(failed, scope, source)),
            asked ? "XmipFailedJourneysUnanswered" : "XmipFailedJourneysNotListed",
            asked ? ErrorCategory.ReadError : ErrorCategory.ResourceUnavailable,
            scope);
    }
}
