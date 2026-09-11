using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal enum WespActivityProcessSource
{
    CurrentProcess,
    RequestorProcess,
    NewProcess
}

internal enum WespActivityTargetSource
{
    None,
    NewProcessImage,
    IoTargetFileObject,
    RegistryKey,
    RegistryKeyObject
}

internal sealed record WespRuleActivityDescriptor(
    WespActivityKind ActivityKind,
    WespActivityResourceKind ResourceKind,
    string Operation,
    string ConfiguredPath,
    WespActivityProcessSource ProcessSource = WespActivityProcessSource.CurrentProcess,
    WespActivityTargetSource TargetSource = WespActivityTargetSource.None);

internal readonly record struct WespProcessIdentity(
    string ProcessName,
    int? ProcessId,
    string? ImagePath);

internal static class WespActivityDecoder
{
    private const int MaximumPropertyCount = 64;

    internal static WespActivity Decode(
        in NativeEventNotificationDataV1 notification,
        WespRuleActivityDescriptor descriptor,
        DateTimeOffset observedAt)
    {
        var query = GetProcessQuery(in notification, descriptor.ProcessSource);
        var identity = DecodeProcessIdentity(in query);
        var targetPath = DecodeTargetPath(
            in notification,
            descriptor,
            identity.ImagePath);

        return new WespActivity(
            observedAt,
            descriptor.ActivityKind,
            descriptor.ResourceKind,
            descriptor.Operation,
            descriptor.ConfiguredPath,
            targetPath,
            identity.ProcessName,
            identity.ProcessId,
            notification.EventId);
    }

    internal static WespActivity DecodeWithoutProcessDetails(
        in NativeEventNotificationDataV1Header notification,
        WespRuleActivityDescriptor descriptor,
        DateTimeOffset observedAt)
    {
        return new(
            observedAt,
            descriptor.ActivityKind,
            descriptor.ResourceKind,
            descriptor.Operation,
            descriptor.ConfiguredPath,
            GetUnavailableTarget(descriptor.ResourceKind),
            "Unknown process",
            ProcessId: null,
            notification.EventId);
    }

    internal static WespProcessIdentity DecodeProcessIdentity(
        in NativePropertyQuery query)
    {
        int? processId = null;
        string? imageNtPath = null;
        string? imageDosPath = null;

        if (query.Properties != 0 &&
            query.PropertiesCount is > 0 and <= MaximumPropertyCount)
        {
            for (var index = 0u; index < query.PropertiesCount; index++)
            {
                var propertyPointer = nint.Add(
                    query.Properties,
                    checked((int)index * WespAbiV013.PropertySize));
                var property = Marshal.PtrToStructure<NativeProperty>(propertyPointer);
                if (property.PropertyId == WespRuleCompiler.ProcessIdProperty &&
                    property.Type == EspVariantType.UInt32)
                {
                    var nativeProcessId = unchecked((uint)property.Value.ToInt64());
                    if (nativeProcessId <= int.MaxValue)
                    {
                        processId = (int)nativeProcessId;
                    }
                }
                else if (property.Type == EspVariantType.UnicodeString &&
                         property.PropertyId is WespRuleCompiler.ProcessImageNtPathProperty or
                             WespRuleCompiler.ProcessImageDosPathProperty)
                {
                    var value = DecodeUnicodeString(property.Value);
                    if (property.PropertyId == WespRuleCompiler.ProcessImageDosPathProperty)
                    {
                        imageDosPath = value;
                    }
                    else
                    {
                        imageNtPath = value;
                    }
                }
            }
        }

        var imagePath = imageDosPath ?? imageNtPath;
        var processName = GetProcessName(imagePath);
        return new WespProcessIdentity(processName, processId, imagePath);
    }

    private static string DecodeTargetPath(
        in NativeEventNotificationDataV1 notification,
        WespRuleActivityDescriptor descriptor,
        string? processImagePath)
    {
        string? targetPath = descriptor.TargetSource switch
        {
            WespActivityTargetSource.NewProcessImage => processImagePath,
            WespActivityTargetSource.IoTargetFileObject => DecodeFileTargetPath(
                notification.EventParameters),
            WespActivityTargetSource.RegistryKey => DecodeRegistryKeyPath(
                notification.EventParameters),
            WespActivityTargetSource.RegistryKeyObject => DecodeRegistryKeyObjectPath(
                notification.EventParameters),
            _ => null
        };

        return string.IsNullOrWhiteSpace(targetPath)
            ? GetUnavailableTarget(descriptor.ResourceKind)
            : targetPath;
    }

    private static string? DecodeFileTargetPath(nint eventParameters)
    {
        if (eventParameters == 0)
        {
            return null;
        }

        var operation = Marshal.PtrToStructure<NativeIoOperationIdentity>(eventParameters);
        var query = GetEventObjectQuery(operation.TargetFileObject);
        return DecodeStringProperty(
            in query,
            WespRuleCompiler.FileObjectNormalizedDosPathProperty,
            WespRuleCompiler.FileObjectNormalizedNtPathProperty);
    }

    private static string? DecodeRegistryKeyPath(nint eventParameters)
    {
        var query = GetEventObjectQuery(eventParameters);
        return DecodeStringProperty(
            in query,
            WespRuleCompiler.RegistryKeyNtPathProperty);
    }

    private static string? DecodeRegistryKeyObjectPath(nint eventParameters)
    {
        if (eventParameters == 0)
        {
            return null;
        }

        var keyObject = Marshal.PtrToStructure<NativeRegistryKeyObjectInfoIdentity>(
            eventParameters);
        return DecodeRegistryKeyPath(keyObject.RegistryKeyInfo);
    }

    private static NativePropertyQuery GetEventObjectQuery(nint objectInfo)
    {
        if (objectInfo == 0)
        {
            return default;
        }

        return Marshal.PtrToStructure<NativeEventObjectInfoIdentity>(objectInfo)
            .PropertyQuery;
    }

    private static string? DecodeStringProperty(
        in NativePropertyQuery query,
        uint preferredPropertyId,
        uint? fallbackPropertyId = null)
    {
        string? preferredValue = null;
        string? fallbackValue = null;

        if (query.Properties == 0 ||
            query.PropertiesCount is 0 or > MaximumPropertyCount)
        {
            return null;
        }

        for (var index = 0u; index < query.PropertiesCount; index++)
        {
            var propertyPointer = nint.Add(
                query.Properties,
                checked((int)index * WespAbiV013.PropertySize));
            var property = Marshal.PtrToStructure<NativeProperty>(propertyPointer);
            if (property.Type != EspVariantType.UnicodeString)
            {
                continue;
            }

            if (property.PropertyId == preferredPropertyId)
            {
                preferredValue = DecodeUnicodeString(property.Value);
            }
            else if (fallbackPropertyId.HasValue &&
                     property.PropertyId == fallbackPropertyId.Value)
            {
                fallbackValue = DecodeUnicodeString(property.Value);
            }
        }

        return preferredValue ?? fallbackValue;
    }

    private static NativePropertyQuery GetProcessQuery(
        in NativeEventNotificationDataV1 notification,
        WespActivityProcessSource source)
    {
        if (source == WespActivityProcessSource.NewProcess)
        {
            if (notification.EventType == EspEventType.ProcessCreate &&
                notification.EventParameters != 0)
            {
                var processInfo = Marshal.PtrToStructure<NativeProcessInfoIdentity>(
                    notification.EventParameters);
                return processInfo.ProcessQuery;
            }

            return default;
        }

        if (source == WespActivityProcessSource.RequestorProcess)
        {
            if (notification.EventParameters != 0)
            {
                var operation = Marshal.PtrToStructure<NativeIoOperationIdentity>(
                    notification.EventParameters);
                if (operation.RequestorProcess != 0)
                {
                    var processInfo = Marshal.PtrToStructure<NativeProcessInfoIdentity>(
                        operation.RequestorProcess);
                    return processInfo.ProcessQuery;
                }
            }

            return default;
        }

        return notification.CurrentProcessQuery;
    }

    private static string? DecodeUnicodeString(nint value)
    {
        if (value == 0)
        {
            return null;
        }

        var nativeString = Marshal.PtrToStructure<NativeUnicodeString>(value);
        if (nativeString.Buffer == 0 ||
            nativeString.LengthBytes == 0 ||
            (nativeString.LengthBytes & 1) != 0)
        {
            return null;
        }

        return Marshal.PtrToStringUni(
            nativeString.Buffer,
            nativeString.LengthBytes / sizeof(char));
    }

    private static string GetProcessName(string? imagePath)
    {
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var fileName = Path.GetFileName(imagePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return "Unknown process";
    }

    private static string GetUnavailableTarget(WespActivityResourceKind resourceKind) =>
        resourceKind switch
        {
            WespActivityResourceKind.Process => "Process image unavailable",
            WespActivityResourceKind.File => "File target unavailable",
            WespActivityResourceKind.Registry => "Registry target unavailable",
            _ => "Target unavailable"
        };
}
