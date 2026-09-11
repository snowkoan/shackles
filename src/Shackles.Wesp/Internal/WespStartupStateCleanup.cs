namespace Shackles.Wesp.Internal;

internal static class WespStartupStateCleanup
{
    internal const int HResultNotFound = unchecked((int)0x80070490);
    internal const int HResultInvalidState = unchecked((int)0x8007139F);

    internal static void ConfirmPreviousRegistrationRemoved(int hresult)
    {
        if (hresult >= 0 || hresult == HResultNotFound)
        {
            return;
        }

        var detail = hresult == HResultInvalidState
            ? "WESP would not clear Shackles' prior client state because that client is still connected. Startup stopped before any new Shackles rules were installed."
            : "WESP could not clear Shackles' prior client state. Startup stopped before any new Shackles rules were installed.";
        throw WespException.FromHResult(
            WespOperation.ClearPreviousClientState,
            hresult,
            detail);
    }

    internal static void ConfirmAllRulesRemoved(int hresult)
    {
        if (hresult >= 0)
        {
            return;
        }

        throw WespException.FromHResult(
            WespOperation.ClearRules,
            hresult,
            "WESP could not confirm a clean rule set for the Shackles client. Startup stopped before any new Shackles rules were installed.");
    }
}
