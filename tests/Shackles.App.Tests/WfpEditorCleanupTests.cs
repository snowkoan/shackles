using Shackles.App.Views;
using Shackles.Wfp;

namespace Shackles.App.Tests;

[TestClass]
public sealed class WfpEditorCleanupTests
{
    [TestMethod]
    [DataRow(WfpTransportProtocol.Any)]
    [DataRow(WfpTransportProtocol.Icmp)]
    public void InactivePortDraftIsIgnoredWithoutRequiringValidText(WfpTransportProtocol protocol)
    {
        var port = WfpWorkspaceView.ParsePortForProtocol("unfinished draft", "Remote port", "RemotePortTextBox", protocol);
        Assert.IsNull(port);

        var normalized = WfpWorkspaceView.ValidateEditorRuleOptions(Rule() with { Protocol = protocol, RemotePort = port });
        Assert.IsNull(normalized.RemotePort);
    }

    [TestMethod]
    [DataRow(WfpTransportProtocol.Tcp)]
    [DataRow(WfpTransportProtocol.Udp)]
    public void ReturningToPortProtocolUsesTheRetainedDraft(WfpTransportProtocol protocol)
    {
        const string draft = "443";
        Assert.IsNull(WfpWorkspaceView.ParsePortForProtocol(draft, "Remote port", "RemotePortTextBox", WfpTransportProtocol.Any));
        Assert.AreEqual((ushort)443, WfpWorkspaceView.ParsePortForProtocol(draft, "Remote port", "RemotePortTextBox", protocol));
    }

    [TestMethod]
    public void InvalidActivePortIdentifiesItsField()
    {
        var error = Assert.ThrowsExactly<WfpWorkspaceView.EditorValidationException>(() =>
            WfpWorkspaceView.ParsePortForProtocol("65536", "Local port", "LocalPortTextBox", WfpTransportProtocol.Tcp));
        Assert.AreEqual("LocalPortTextBox", error.FieldName);
        StringAssert.Contains(error.Message, "65535");
    }

    [TestMethod]
    public void InvalidAddressScopesIdentifyTheCorrespondingField()
    {
        var local = Assert.ThrowsExactly<WfpWorkspaceView.EditorValidationException>(() =>
            WfpWorkspaceView.ValidateEditorRuleOptions(Rule() with { LocalNetwork = "not-an-ip" }));
        Assert.AreEqual("LocalNetworkTextBox", local.FieldName);

        var remote = Assert.ThrowsExactly<WfpWorkspaceView.EditorValidationException>(() =>
            WfpWorkspaceView.ValidateEditorRuleOptions(Rule() with { RemoteNetwork = "not-an-ip" }));
        Assert.AreEqual("RemoteNetworkTextBox", remote.FieldName);
    }

    [TestMethod]
    public void ConflictingAddressFamiliesIdentifyTheRemoteScope()
    {
        var error = Assert.ThrowsExactly<WfpWorkspaceView.EditorValidationException>(() =>
            WfpWorkspaceView.ValidateEditorRuleOptions(Rule() with
            {
                LocalNetwork = "127.0.0.1/32",
                RemoteNetwork = "::1/128"
            }));
        Assert.AreEqual("RemoteNetworkTextBox", error.FieldName);
        StringAssert.Contains(error.Message, "same IP version");
    }

    [TestMethod]
    public void EmptyDirectionAndIpSelectionsIdentifyEditableControls()
    {
        var direction = Assert.ThrowsExactly<WfpWorkspaceView.EditorValidationException>(() =>
            WfpWorkspaceView.ValidateEditorRuleOptions(Rule() with { Directions = WfpTrafficDirection.None }));
        Assert.AreEqual("OutboundCheckBox", direction.FieldName);

        var version = Assert.ThrowsExactly<WfpWorkspaceView.EditorValidationException>(() =>
            WfpWorkspaceView.ValidateEditorRuleOptions(Rule() with { IpVersions = WfpIpVersion.None }));
        Assert.AreEqual("Ipv4CheckBox", version.FieldName);
    }

    [TestMethod]
    public void MissingExecutableIdentifiesItsPathField()
    {
        var error = Assert.ThrowsExactly<WfpWorkspaceView.EditorValidationException>(() =>
            WfpWorkspaceView.ValidateEditorRuleOptions(Rule() with { ExecutablePath = string.Empty }));
        Assert.AreEqual("ExecutablePathTextBox", error.FieldName);
    }

    private static WfpBlockRuleOptions Rule() => new(
        typeof(WfpSession).Assembly.Location,
        WfpTrafficDirection.Both,
        WfpIpVersion.All,
        WfpTransportProtocol.Any);
}
