using Shackles.Wesp.Internal;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespStartupTroubleshootingTests
{
    [TestMethod]
    public void StartupMessageExplainsHighIntegrityRequirement()
    {
        var exception = new WespException(
            WespOperation.CheckIntegrity,
            "The current Shackles process is running at medium integrity.");

        var message = WespStartupTroubleshooting.FormatStartupFailure(
            exception,
            WespDriverServiceStatus.Running);

        StringAssert.Contains(message, "requires Shackles to run with high integrity");
        StringAssert.Contains(message, "Run as administrator");
        StringAssert.Contains(message, "Technical details:");
        StringAssert.Contains(message, "medium integrity");
        Assert.IsFalse(message.Contains("test-signing", StringComparison.Ordinal));
        Assert.IsFalse(message.Contains("driver", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void StartupMessagePresentsPossibilitiesAndPreservesTechnicalDetails()
    {
        var exception = new WespException(
            WespOperation.RegisterClient,
            "The system cannot find the file specified.",
            unchecked((int)0x80070002));

        var message = WespStartupTroubleshooting.FormatStartupFailure(
            exception,
            WespDriverServiceStatus.NotRunning);

        StringAssert.Contains(message, "Things to check:");
        StringAssert.Contains(message, "Ensure Windows test-signing is enabled");
        StringAssert.Contains(message, "Ensure the WESP driver is installed and running");
        StringAssert.Contains(message, "installed but does not currently report that it is running");
        StringAssert.Contains(message, "Ensure no other Shackles instance");
        StringAssert.Contains(message, "Technical details:");
        StringAssert.Contains(message, "0x80070002");
    }

    [TestMethod]
    public void StartupMessageReportsWhenTheWespDriverServiceIsMissing()
    {
        var message = WespStartupTroubleshooting.FormatStartupFailure(
            new WespException(WespOperation.ConnectClient, "native failure"),
            WespDriverServiceStatus.NotInstalled);

        StringAssert.Contains(message, "The 'wesp' driver service was not found");
        StringAssert.Contains(message, "native failure");
    }

    [TestMethod]
    [DataRow(WespOperation.ClearPreviousClientState)]
    [DataRow(WespOperation.ClearRules)]
    public void StartupCleanupFailuresRetainTheStandardTroubleshootingSuggestions(
        WespOperation operation)
    {
        var message = WespStartupTroubleshooting.FormatStartupFailure(
            new WespException(operation, "cleanup failure"),
            WespDriverServiceStatus.Running);

        StringAssert.Contains(message, "Things to check:");
        StringAssert.Contains(message, "test-signing");
        StringAssert.Contains(message, "no other Shackles instance");
        StringAssert.Contains(message, "cleanup failure");
    }

    [TestMethod]
    public void StartupMessageDoesNotSuggestDriverFixesForPolicyValidationFailures()
    {
        var exception = new WespException(
            WespOperation.ValidatePolicy,
            "The protected folder was not found.");

        var message = WespStartupTroubleshooting.FormatStartupFailure(
            exception,
            WespDriverServiceStatus.NotRunning);

        Assert.AreEqual(exception.Message, message);
        Assert.IsFalse(message.Contains("Things to check:", StringComparison.Ordinal));
    }
}
