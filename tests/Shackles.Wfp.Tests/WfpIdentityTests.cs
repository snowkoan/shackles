using System.Text;

namespace Shackles.Wfp.Tests;

[TestClass]
public sealed class WfpIdentityTests
{
    [TestMethod]
    public void ProviderDataCarriesMarkerSessionAndOptionalRuleKeys()
    {
        var sessionKey = Guid.Parse("01234567-89AB-CDEF-0123-456789ABCDEF");
        var ruleKey = Guid.Parse("89ABCDEF-0123-4567-89AB-CDEF01234567");

        var infrastructure = WfpSession.CreateProviderData(sessionKey, null);
        var rule = WfpSession.CreateProviderData(sessionKey, ruleKey);

        Assert.HasCount(24, infrastructure);
        Assert.HasCount(40, rule);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("SHWFP001"), rule[..8]);
        CollectionAssert.AreEqual(sessionKey.ToByteArray(), rule[8..24]);
        CollectionAssert.AreEqual(ruleKey.ToByteArray(), rule[24..40]);
    }

    [TestMethod]
    public void FilterDisplayNameIncludesEveryEnumerationIdentity()
    {
        var sessionKey = Guid.NewGuid();
        var ruleKey = Guid.NewGuid();
        var firstFilterKey = Guid.NewGuid();
        var secondFilterKey = Guid.NewGuid();

        var first = WfpSession.CreateFilterDisplayName(
            sessionKey,
            ruleKey,
            "out-v4",
            firstFilterKey);
        var second = WfpSession.CreateFilterDisplayName(
            sessionKey,
            ruleKey,
            "out-v4",
            secondFilterKey);

        Assert.AreNotEqual(first, second);
        StringAssert.StartsWith(first, "Shackles/WFP/");
        StringAssert.Contains(first, sessionKey.ToString("N"));
        StringAssert.Contains(first, ruleKey.ToString("N"));
        StringAssert.Contains(first, firstFilterKey.ToString("N"));
        StringAssert.Contains(first, "/out-v4/");
    }
}
