using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// The non-terminating error <c>Get-XmipScope -FailedMessage</c> writes where
/// a list of the Messages that failed is no answer, in the sentence
/// <c>xmip message</c> says it with (<see cref="MessageOperation.Unlisted"/>):
/// <c>XmipFailedMessagesNotListed</c> where the surface cannot list them,
/// <c>XmipFailedMessagesUnanswered</c> where it asked and Xmip Storage did not
/// answer.
/// </summary>
public static class FailedMessageError
{
    /// <summary>The error for <paramref name="failed"/>, listed at
    /// <paramref name="scope"/> from <paramref name="source"/>; null where it
    /// is an answer, none failing among them.</summary>
    public static ErrorRecord? Of(FailedMessageList failed, string scope, string source)
    {
        ArgumentNullException.ThrowIfNull(failed);

        if (failed.Listed)
        {
            return null;
        }

        bool asked = failed.Failure.Length > 0;

        return new ErrorRecord(
            new InvalidOperationException(MessageOperation.Unlisted(failed, scope, source)),
            asked ? "XmipFailedMessagesUnanswered" : "XmipFailedMessagesNotListed",
            asked ? ErrorCategory.ReadError : ErrorCategory.ResourceUnavailable,
            scope);
    }
}
