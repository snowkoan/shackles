using System.Globalization;
using System.Runtime.InteropServices;
using System.Reflection;
using Shackles.Wesp.Internal;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespAbiV013Tests
{
    [TestMethod]
    public void ProfileDocumentsBundledPreview013ClientBuild()
    {
        var fields = typeof(WespAbiV013).GetFields(
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.AreEqual(
            "0.1.0.154553750",
            fields.Single(field => field.Name == nameof(WespAbiV013.FileVersion)).GetRawConstantValue());
        Assert.AreEqual(
            "0.1.0.154553750+3639f00d",
            fields.Single(field => field.Name == nameof(WespAbiV013.ProductVersion)).GetRawConstantValue());
    }

    [TestMethod]
    public void LeafLayoutsMatchPreview013Abi()
    {
        Assert.AreEqual(32, Marshal.SizeOf<NativeStringComparison>());
        Assert.AreEqual(56, Marshal.SizeOf<NativeIntegerComparison>());
        Assert.AreEqual(64, Marshal.SizeOf<NativeContextKeyComparison>());
        Assert.AreEqual(40, Marshal.SizeOf<NativeContextKeyUpdate>());
        Assert.AreEqual(16, Marshal.SizeOf<NativeContextKeyConfig>());
        Assert.AreEqual(16, Marshal.SizeOf<NativeUnicodeString>());
        Assert.AreEqual(16, Marshal.SizeOf<NativeProperty>());
        Assert.AreEqual(16, Marshal.SizeOf<NativePropertyQuery>());
        Assert.AreEqual(24, Marshal.SizeOf<NativeRuleUpdateEntry>());

        Assert.AreEqual(24, OffsetOf<NativeStringComparison>(nameof(NativeStringComparison.StringBuffer)));
        Assert.AreEqual(48, OffsetOf<NativeIntegerComparison>(nameof(NativeIntegerComparison.RawValue)));
        Assert.AreEqual(56, OffsetOf<NativeContextKeyComparison>(nameof(NativeContextKeyComparison.RawValue)));
        Assert.AreEqual(24, OffsetOf<NativeContextKeyUpdate>(nameof(NativeContextKeyUpdate.IntegerValue)));
        Assert.AreEqual(8, OffsetOf<NativeContextKeyConfig>(nameof(NativeContextKeyConfig.Updates)));
        Assert.AreEqual(8, OffsetOf<NativeUnicodeString>(nameof(NativeUnicodeString.Buffer)));
        Assert.AreEqual(8, OffsetOf<NativeProperty>(nameof(NativeProperty.Value)));
        Assert.AreEqual(8, OffsetOf<NativePropertyQuery>(nameof(NativePropertyQuery.Properties)));
        Assert.AreEqual(8, OffsetOf<NativeRuleUpdateEntry>(nameof(NativeRuleUpdateEntry.Rule)));
    }

    [TestMethod]
    public void ExistingProcessIdentityIdsMatchPreview013Abi()
    {
        Assert.AreEqual(2u, CompilerConstant(nameof(WespRuleCompiler.ProcessCreateTimeProperty)));
        Assert.AreEqual(6u, CompilerConstant(nameof(WespRuleCompiler.ProcessIdProperty)));
        Assert.AreEqual(20u, CompilerConstant(nameof(WespRuleCompiler.ProcessImageNtPathProperty)));
        Assert.AreEqual(21u, CompilerConstant(nameof(WespRuleCompiler.ProcessImageDosPathProperty)));
        Assert.AreEqual(5, EnumValue<EspVariantType>(nameof(EspVariantType.UInt32)));
        Assert.AreEqual(8, EnumValue<EspVariantType>(nameof(EspVariantType.UnicodeString)));
        Assert.AreEqual(10, EnumValue<EspVariantType>(nameof(EspVariantType.FileTime)));
    }

    [TestMethod]
    public void ProcessCreateOffsetsMatchPreview013Abi()
    {
        Assert.AreEqual(1328, Marshal.SizeOf<NativeProcessCreateConfig>());
        Assert.AreEqual(8, OffsetOf<NativeProcessCreateConfig>(nameof(NativeProcessCreateConfig.NewProcessFilter)));
        Assert.AreEqual(16, OffsetOf<NativeProcessCreateConfig>(nameof(NativeProcessCreateConfig.NewProcessContext)));
        Assert.AreEqual(32, OffsetOf<NativeProcessCreateConfig>(nameof(NativeProcessCreateConfig.NewProcessIncludeInNotification)));
        Assert.AreEqual(48, OffsetOf<NativeProcessCreateConfig>(nameof(NativeProcessCreateConfig.NewProcessPropertiesCount)));
        Assert.AreEqual(56, OffsetOf<NativeProcessCreateConfig>(nameof(NativeProcessCreateConfig.NewProcessProperties)));
        Assert.AreEqual(400, OffsetOf<NativeProcessCreateConfig>(nameof(NativeProcessCreateConfig.ImageFileObjectFilter)));
        Assert.AreEqual(520, OffsetOf<NativeProcessCreateConfig>(nameof(NativeProcessCreateConfig.ImageFileObjectType)));
        Assert.AreEqual(
            944,
            OffsetOf<NativeProcessCreateConfig>(
                nameof(NativeProcessCreateConfig.CreatingThreadOwningProcessFilter)));
    }

    [TestMethod]
    public void IoOffsetsMatchPreview013Abi()
    {
        Assert.AreEqual(1864, Marshal.SizeOf<NativeIoEventConfig>());
        Assert.AreEqual(544, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.RequestorProcessFilter)));
        Assert.AreEqual(568, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.RequestorProcessIncludeInNotification)));
        Assert.AreEqual(584, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.RequestorProcessPropertiesCount)));
        Assert.AreEqual(592, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.RequestorProcessProperties)));
        Assert.AreEqual(936, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.TargetFileObjectFilter)));
        Assert.AreEqual(960, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.TargetFileObjectIncludeInNotification)));
        Assert.AreEqual(976, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.TargetFileObjectPropertiesCount)));
        Assert.AreEqual(984, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.TargetFileObjectProperties)));
        Assert.AreEqual(1056, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.TargetFileObjectType)));
        Assert.AreEqual(1384, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.FirstArgumentFilter)));
        Assert.AreEqual(1400, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.SecondArgumentFilter)));
        Assert.AreEqual(1416, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.ThirdArgumentFilter)));
        Assert.AreEqual(1408, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.ParentTargetFileObjectFilter)));
        Assert.AreEqual(1528, OffsetOf<NativeIoEventConfig>(nameof(NativeIoEventConfig.ParentTargetFileObjectType)));
    }

    [TestMethod]
    public void RegistryOffsetsMatchPreview013Abi()
    {
        Assert.AreEqual(600, Marshal.SizeOf<NativeRegistryEventConfig>());
        Assert.AreEqual(8, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.RegistryKeyFilter)));
        Assert.AreEqual(32, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.RegistryKeyIncludeInNotification)));
        Assert.AreEqual(48, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.RegistryKeyPropertiesCount)));
        Assert.AreEqual(56, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.RegistryKeyProperties)));
        Assert.AreEqual(72, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.RegistryKeyObjectKeyFilter)));
        Assert.AreEqual(96, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.RegistryKeyObjectKeyIncludeInNotification)));
        Assert.AreEqual(112, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.RegistryKeyObjectKeyPropertiesCount)));
        Assert.AreEqual(120, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.RegistryKeyObjectKeyProperties)));
        Assert.AreEqual(128, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.CreateDesiredAccessFilter)));
        Assert.AreEqual(80, OffsetOf<NativeRegistryEventConfig>(nameof(NativeRegistryEventConfig.OpenDesiredAccessFilter)));
    }

    [TestMethod]
    public void RuleDescriptorOffsetsMatchPreview013Abi()
    {
        Assert.AreEqual(1128, Marshal.SizeOf<NativeRuleDescriptor>());
        Assert.AreEqual(96, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.EventTypeConfig)));
        Assert.AreEqual(688, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.CurrentProcessFilter)));
        Assert.AreEqual(712, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.CurrentProcessIncludeInNotification)));
        Assert.AreEqual(728, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.CurrentProcessPropertiesCount)));
        Assert.AreEqual(736, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.CurrentProcessProperties)));
        Assert.AreEqual(1104, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.Action)));
        Assert.AreEqual(1112, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.BlockReason)));
        Assert.AreEqual(1112, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.NotifyAsyncEventQueue)));
        Assert.AreEqual(1120, OffsetOf<NativeRuleDescriptor>(nameof(NativeRuleDescriptor.BlockAsyncEventQueue)));
    }

    [TestMethod]
    public void AsyncEventQueueLayoutsMatchPreview013Abi()
    {
        Assert.AreEqual(32, Marshal.SizeOf<NativeEventQueueDescriptor>());
        Assert.AreEqual(0, OffsetOf<NativeEventQueueDescriptor>(nameof(NativeEventQueueDescriptor.QueueId)));
        Assert.AreEqual(16, OffsetOf<NativeEventQueueDescriptor>(nameof(NativeEventQueueDescriptor.QueueType)));
        Assert.AreEqual(20, OffsetOf<NativeEventQueueDescriptor>(nameof(NativeEventQueueDescriptor.Lifetime)));
        Assert.AreEqual(24, OffsetOf<NativeEventQueueDescriptor>(nameof(NativeEventQueueDescriptor.MaxCapacityBytes)));
        Assert.AreEqual(28, OffsetOf<NativeEventQueueDescriptor>(nameof(NativeEventQueueDescriptor.NotificationVersion)));

        Assert.AreEqual(32, Marshal.SizeOf<NativeEventNotification>());
        Assert.AreEqual(0, OffsetOf<NativeEventNotification>(nameof(NativeEventNotification.EventQueueId)));
        Assert.AreEqual(16, OffsetOf<NativeEventNotification>(nameof(NativeEventNotification.Version)));
        Assert.AreEqual(24, OffsetOf<NativeEventNotification>(nameof(NativeEventNotification.NotificationData)));

        Assert.AreEqual(32, Marshal.SizeOf<NativeEventNotificationDataV1Header>());
        Assert.AreEqual(0, OffsetOf<NativeEventNotificationDataV1Header>(nameof(NativeEventNotificationDataV1Header.EventId)));
        Assert.AreEqual(8, OffsetOf<NativeEventNotificationDataV1Header>(nameof(NativeEventNotificationDataV1Header.RuleId)));
        Assert.AreEqual(24, OffsetOf<NativeEventNotificationDataV1Header>(nameof(NativeEventNotificationDataV1Header.RuleAction)));
        Assert.AreEqual(28, OffsetOf<NativeEventNotificationDataV1Header>(nameof(NativeEventNotificationDataV1Header.NotificationFlags)));

        Assert.AreEqual(168, Marshal.SizeOf<NativeEventNotificationDataV1>());
        Assert.AreEqual(80, OffsetOf<NativeEventNotificationDataV1>(nameof(NativeEventNotificationDataV1.CurrentProcessQuery)));
        Assert.AreEqual(120, OffsetOf<NativeEventNotificationDataV1>(nameof(NativeEventNotificationDataV1.EventType)));
        Assert.AreEqual(160, OffsetOf<NativeEventNotificationDataV1>(nameof(NativeEventNotificationDataV1.EventParameters)));
        Assert.AreEqual(24, Marshal.SizeOf<NativeProcessInfoIdentity>());
        Assert.AreEqual(8, OffsetOf<NativeProcessInfoIdentity>(nameof(NativeProcessInfoIdentity.ProcessQuery)));
        Assert.AreEqual(32, Marshal.SizeOf<NativeIoOperationIdentity>());
        Assert.AreEqual(16, OffsetOf<NativeIoOperationIdentity>(nameof(NativeIoOperationIdentity.RequestorProcess)));
        Assert.AreEqual(24, OffsetOf<NativeIoOperationIdentity>(nameof(NativeIoOperationIdentity.TargetFileObject)));
        Assert.AreEqual(24, Marshal.SizeOf<NativeEventObjectInfoIdentity>());
        Assert.AreEqual(8, OffsetOf<NativeEventObjectInfoIdentity>(nameof(NativeEventObjectInfoIdentity.PropertyQuery)));
        Assert.AreEqual(32, Marshal.SizeOf<NativeRegistryKeyObjectInfoIdentity>());
        Assert.AreEqual(
            24,
            OffsetOf<NativeRegistryKeyObjectInfoIdentity>(
                nameof(NativeRegistryKeyObjectInfoIdentity.RegistryKeyInfo)));
    }

    [TestMethod]
    public void RegistryEventIdsMatchPreview013Abi()
    {
        var registryEvents = Enum.GetValues<EspEventType>()
            .Where(eventType => (int)eventType is >= 7000 and <= 7014)
            .OrderBy(eventType => (int)eventType)
            .ToArray();

        Assert.HasCount(15, registryEvents);
        for (var index = 0; index < registryEvents.Length; index++)
        {
            Assert.AreEqual(7000 + index, (int)registryEvents[index]);
        }
    }

    private static int OffsetOf<T>(string fieldName) where T : struct =>
        checked((int)Marshal.OffsetOf<T>(fieldName));

    private static uint CompilerConstant(string fieldName) =>
        (uint)(typeof(WespRuleCompiler).GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.NonPublic)?.GetRawConstantValue() ??
            throw new InvalidOperationException($"Missing WESP compiler constant {fieldName}."));

    private static int EnumValue<T>(string fieldName) where T : struct, Enum =>
        Convert.ToInt32(Enum.Parse<T>(fieldName), CultureInfo.InvariantCulture);
}
