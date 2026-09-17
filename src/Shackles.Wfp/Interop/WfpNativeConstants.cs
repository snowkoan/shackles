namespace Shackles.Wfp.Interop;

internal static class WfpNativeConstants
{
    internal const uint RpcAuthenticationWinNt = 10;
    internal const uint DynamicSession = 0x00000001;
    internal const uint ActionBlock = 0x00001001;
    internal const uint TokenQuery = 0x0008;
    internal const uint TokenElevation = 20;
    internal const uint SecurityDescriptorRevision = 1;
    internal const uint FwpActrlMatchFilter = 0x00000001;
    internal const uint FwpFilterNotFound = 0x80320003;
    internal const uint FwpProviderNotFound = 0x80320005;
    internal const uint FwpSubLayerNotFound = 0x80320007;
    internal const uint FwpAlreadyExists = 0x80320009;

    internal static readonly Guid ProviderKey = new("A83A3F77-A83F-4179-8162-6240B86A9C64");
    internal static readonly Guid SubLayerKey = new("B30CCFCA-A7FA-4D56-9F15-40807CD17BAD");

    internal static readonly Guid AleAuthReceiveAcceptV4 = new("E1CD9FE7-F4B5-4273-96C0-592E487B8650");
    internal static readonly Guid AleAuthReceiveAcceptV6 = new("A3B42C97-9F04-4672-B87E-CEE9C483257F");
    internal static readonly Guid AleAuthConnectV4 = new("C38D57D1-05A7-4C33-904F-7FBCEEE60E82");
    internal static readonly Guid AleAuthConnectV6 = new("4A72393B-319F-44BC-84C3-BA54DCB3B6B4");

    internal static readonly Guid ConditionAleAppId = new("D78E1E87-8644-4EA5-9437-D809ECEFC971");
    internal static readonly Guid ConditionAleUserId = new("AF043A0A-B34D-4F86-979C-C90371AF6E66");
    internal static readonly Guid ConditionLocalAddress = new("D9EE00DE-C1EF-4617-BFE3-FFD8F5A08957");
    internal static readonly Guid ConditionRemoteAddress = new("B235AE9A-1D64-49B8-A44C-5FF3D9095045");
    internal static readonly Guid ConditionProtocol = new("3971EF2B-623E-4F9A-8CB1-6E79B806B9A7");
    internal static readonly Guid ConditionLocalPort = new("0C1BA1AF-5765-453F-AF22-A8F791AC775B");
    internal static readonly Guid ConditionRemotePort = new("C35A604D-D22B-4E1A-91B4-68F674EE674B");
    internal static readonly Guid ConditionLocalInterface = new("4CD62A49-59C3-4969-B7F3-BDA5D32890A4");
}
