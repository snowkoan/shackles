using Shackles.Wesp.Internal;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespStartupStateCleanupTests
{
    [TestMethod]
    public void SuccessfulPreviousRegistrationRemovalIsAccepted()
    {
        WespStartupStateCleanup.ConfirmPreviousRegistrationRemoved(0);
    }

    [TestMethod]
    public void MissingPreviousRegistrationIsAlreadyClean()
    {
        WespStartupStateCleanup.ConfirmPreviousRegistrationRemoved(
            WespStartupStateCleanup.HResultNotFound);
    }

    [TestMethod]
    public void ConnectedPreviousClientStopsStartupBeforeRulesAreInstalled()
    {
        var exception = Assert.ThrowsExactly<WespException>(() =>
            WespStartupStateCleanup.ConfirmPreviousRegistrationRemoved(
                WespStartupStateCleanup.HResultInvalidState));

        Assert.AreEqual(WespOperation.ClearPreviousClientState, exception.Operation);
        Assert.AreEqual(WespStartupStateCleanup.HResultInvalidState, exception.NativeHResult);
        StringAssert.Contains(exception.Message, "still connected");
        StringAssert.Contains(exception.Message, "before any new Shackles rules were installed");
    }

    [TestMethod]
    public void UnexpectedPreviousRegistrationCleanupFailureIsNotIgnored()
    {
        const int failure = unchecked((int)0x80070005);

        var exception = Assert.ThrowsExactly<WespException>(() =>
            WespStartupStateCleanup.ConfirmPreviousRegistrationRemoved(failure));

        Assert.AreEqual(WespOperation.ClearPreviousClientState, exception.Operation);
        Assert.AreEqual(failure, exception.NativeHResult);
        StringAssert.Contains(exception.Message, "could not clear Shackles' prior client state");
        StringAssert.Contains(exception.Message, "0x80070005");
    }

    [TestMethod]
    public void SuccessfulConnectedRuleCleanupIsAccepted()
    {
        WespStartupStateCleanup.ConfirmAllRulesRemoved(0);
    }

    [TestMethod]
    public void ConnectedRuleCleanupFailureStopsStartupBeforeRuleInstallation()
    {
        const int failure = unchecked((int)0x80004005);

        var exception = Assert.ThrowsExactly<WespException>(() =>
            WespStartupStateCleanup.ConfirmAllRulesRemoved(failure));

        Assert.AreEqual(WespOperation.ClearRules, exception.Operation);
        Assert.AreEqual(failure, exception.NativeHResult);
        StringAssert.Contains(exception.Message, "clean rule set");
        StringAssert.Contains(exception.Message, "before any new Shackles rules were installed");
    }
}
