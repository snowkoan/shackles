using System.Runtime.InteropServices;
using Shackles.Wesp.Internal;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespActivityDecoderTests
{
    [TestMethod]
    public void BlockedActivityUsesCurrentProcessIdentityAndKeepsConfiguredRulePath()
    {
        using var process = new NativeProcessProperties(
            4120,
            @"C:\Program Files\Cursor\Cursor.exe");
        using var target = new NativeStringProperties(
            WespRuleCompiler.RegistryKeyNtPathProperty,
            @"\REGISTRY\MACHINE\Software\Protected\Settings");
        using var eventParameters = NativeEventParameters.ForRegistryKeyObject(
            target.Query);
        var notification = new NativeEventNotificationDataV1
        {
            EventId = 77,
            RuleAction = EspRuleAction.Block,
            EventType = EspEventType.RegistrySetValue,
            CurrentProcessQuery = process.Query,
            EventParameters = eventParameters.Pointer
        };
        var descriptor = new WespRuleActivityDescriptor(
            WespActivityKind.Blocked,
            WespActivityResourceKind.Registry,
            "Registry value write blocked",
            @"\REGISTRY\MACHINE\Software\Protected",
            WespActivityProcessSource.CurrentProcess,
            WespActivityTargetSource.RegistryKeyObject);
        var observedAt = new DateTimeOffset(2026, 9, 10, 12, 34, 56, TimeSpan.Zero);

        var activity = WespActivityDecoder.Decode(
            in notification,
            descriptor,
            observedAt);

        Assert.AreEqual(WespActivityKind.Blocked, activity.ActivityKind);
        Assert.AreEqual("Cursor.exe", activity.ProcessName);
        Assert.AreEqual(4120, activity.ProcessId);
        Assert.AreEqual(
            @"\REGISTRY\MACHINE\Software\Protected",
            activity.ConfiguredPath);
        Assert.AreEqual(
            @"\REGISTRY\MACHINE\Software\Protected\Settings",
            activity.TargetPath);
        Assert.AreEqual(77UL, activity.EventId);
        Assert.AreEqual(observedAt, activity.ObservedAt);
    }

    [TestMethod]
    public void FileActivityUsesIoRequestorInsteadOfCurrentProcess()
    {
        using var currentProcess = new NativeProcessProperties(
            4,
            @"C:\Windows\System32\ntoskrnl.exe");
        using var requestorProcess = new NativeProcessProperties(
            5128,
            @"C:\Program Files\Cursor\Cursor.exe");
        using var target = new NativeStringProperties(
            WespRuleCompiler.FileObjectNormalizedDosPathProperty,
            @"C:\Protected\notes.txt",
            WespRuleCompiler.FileObjectNormalizedNtPathProperty,
            @"\Device\HarddiskVolume4\Protected\notes.txt");
        using var eventParameters = NativeEventParameters.ForIoRequestor(
            requestorProcess.Query,
            target.Query);
        var notification = new NativeEventNotificationDataV1
        {
            EventId = 78,
            RuleAction = EspRuleAction.Block,
            EventType = EspEventType.FileObjectWrite,
            CurrentProcessQuery = currentProcess.Query,
            EventParameters = eventParameters.Pointer
        };
        var descriptor = new WespRuleActivityDescriptor(
            WespActivityKind.Blocked,
            WespActivityResourceKind.File,
            "File write blocked",
            @"C:\Protected",
            WespActivityProcessSource.RequestorProcess,
            WespActivityTargetSource.IoTargetFileObject);

        var activity = WespActivityDecoder.Decode(
            in notification,
            descriptor,
            DateTimeOffset.UtcNow);

        Assert.AreEqual("Cursor.exe", activity.ProcessName);
        Assert.AreEqual(5128, activity.ProcessId);
        Assert.AreEqual(@"C:\Protected\notes.txt", activity.TargetPath);
        Assert.AreEqual(@"C:\Protected", activity.ConfiguredPath);
    }

    [TestMethod]
    public void StartedActivityUsesNewProcessAndObservedImagePath()
    {
        using var newProcess = new NativeProcessProperties(
            6200,
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
        using var eventParameters = NativeEventParameters.ForNewProcess(newProcess.Query);
        var notification = new NativeEventNotificationDataV1
        {
            EventId = 79,
            RuleAction = EspRuleAction.Notify,
            EventType = EspEventType.ProcessCreate,
            EventParameters = eventParameters.Pointer
        };
        var descriptor = new WespRuleActivityDescriptor(
            WespActivityKind.ProcessStarted,
            WespActivityResourceKind.Process,
            "Child application started",
            string.Empty,
            WespActivityProcessSource.NewProcess,
            WespActivityTargetSource.NewProcessImage);

        var activity = WespActivityDecoder.Decode(
            in notification,
            descriptor,
            DateTimeOffset.UtcNow);

        Assert.AreEqual(WespActivityKind.ProcessStarted, activity.ActivityKind);
        Assert.AreEqual("powershell.exe", activity.ProcessName);
        Assert.AreEqual(6200, activity.ProcessId);
        Assert.AreEqual(
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            activity.TargetPath);
        Assert.AreEqual(string.Empty, activity.ConfiguredPath);
    }

    [TestMethod]
    public void MissingImagePathKeepsNameUnknownAndRetainsPid()
    {
        using var process = new NativeProcessProperties(9100, imagePath: null);
        var query = process.Query;

        var identity = WespActivityDecoder.DecodeProcessIdentity(in query);

        Assert.AreEqual("Unknown process", identity.ProcessName);
        Assert.AreEqual(9100, identity.ProcessId);
        Assert.IsNull(identity.ImagePath);
    }

    [TestMethod]
    public void DosPathErrorFallsBackToNtImagePath()
    {
        using var process = new NativeProcessProperties(
            9200,
            imagePath: null,
            imageNtPath: @"\Device\HarddiskVolume4\Program Files\Cursor\Cursor.exe",
            includeDosPathError: true);
        var query = process.Query;

        var identity = WespActivityDecoder.DecodeProcessIdentity(in query);

        Assert.AreEqual("Cursor.exe", identity.ProcessName);
        Assert.AreEqual(
            @"\Device\HarddiskVolume4\Program Files\Cursor\Cursor.exe",
            identity.ImagePath);
    }

    [TestMethod]
    public void DosImagePathIsPreferredWhenBothFormsAreAvailable()
    {
        using var process = new NativeProcessProperties(
            9250,
            @"C:\Program Files\Cursor\Cursor.exe",
            @"\Device\HarddiskVolume4\Program Files\Cursor\Cursor.exe");
        var query = process.Query;

        var identity = WespActivityDecoder.DecodeProcessIdentity(in query);

        Assert.AreEqual(@"C:\Program Files\Cursor\Cursor.exe", identity.ImagePath);
    }

    [TestMethod]
    public void FileTargetFallsBackToNormalizedNtPathWhenDosPathIsAnError()
    {
        using var process = new NativeProcessProperties(
            9300,
            @"C:\Program Files\Cursor\Cursor.exe");
        using var target = new NativeStringProperties(
            WespRuleCompiler.FileObjectNormalizedDosPathProperty,
            value: null,
            WespRuleCompiler.FileObjectNormalizedNtPathProperty,
            @"\Device\Mup\server\share\notes.txt",
            includePreferredError: true);
        using var eventParameters = NativeEventParameters.ForIoRequestor(
            process.Query,
            target.Query);
        var notification = new NativeEventNotificationDataV1
        {
            EventId = 80,
            RuleAction = EspRuleAction.Block,
            EventType = EspEventType.FileObjectRead,
            EventParameters = eventParameters.Pointer
        };
        var descriptor = new WespRuleActivityDescriptor(
            WespActivityKind.Blocked,
            WespActivityResourceKind.File,
            "File read blocked",
            @"\Device\Mup",
            WespActivityProcessSource.RequestorProcess,
            WespActivityTargetSource.IoTargetFileObject);

        var activity = WespActivityDecoder.Decode(
            in notification,
            descriptor,
            DateTimeOffset.UtcNow);

        Assert.AreEqual(@"\Device\Mup\server\share\notes.txt", activity.TargetPath);
    }

    [TestMethod]
    public void RegistryCreateUsesProspectiveCanonicalKeyPath()
    {
        using var process = new NativeProcessProperties(
            9400,
            @"C:\Program Files\Cursor\Cursor.exe");
        using var target = new NativeStringProperties(
            WespRuleCompiler.RegistryKeyNtPathProperty,
            @"\REGISTRY\MACHINE\Software\Protected\NewKey");
        using var eventParameters = NativeEventParameters.ForRegistryKey(target.Query);
        var notification = new NativeEventNotificationDataV1
        {
            EventId = 82,
            RuleAction = EspRuleAction.Block,
            EventType = EspEventType.RegistryCreateKey,
            CurrentProcessQuery = process.Query,
            EventParameters = eventParameters.Pointer
        };
        var descriptor = new WespRuleActivityDescriptor(
            WespActivityKind.Blocked,
            WespActivityResourceKind.Registry,
            "Registry key creation blocked",
            @"\REGISTRY\MACHINE\Software\Protected",
            WespActivityProcessSource.CurrentProcess,
            WespActivityTargetSource.RegistryKey);

        var activity = WespActivityDecoder.Decode(
            in notification,
            descriptor,
            DateTimeOffset.UtcNow);

        Assert.AreEqual(
            @"\REGISTRY\MACHINE\Software\Protected\NewKey",
            activity.TargetPath);
        Assert.AreEqual(
            @"\REGISTRY\MACHINE\Software\Protected",
            activity.ConfiguredPath);
    }

    [TestMethod]
    public void UnvalidatedLayoutActivityDoesNotNeedProcessPayload()
    {
        var notification = new NativeEventNotificationDataV1Header
        {
            EventId = 81,
            RuleAction = EspRuleAction.Notify
        };
        var descriptor = new WespRuleActivityDescriptor(
            WespActivityKind.ProcessStarted,
            WespActivityResourceKind.Process,
            "Child application started",
            string.Empty,
            WespActivityProcessSource.NewProcess,
            WespActivityTargetSource.NewProcessImage);

        var activity = WespActivityDecoder.DecodeWithoutProcessDetails(
            in notification,
            descriptor,
            DateTimeOffset.UtcNow);

        Assert.AreEqual("Unknown process", activity.ProcessName);
        Assert.IsNull(activity.ProcessId);
        Assert.AreEqual(string.Empty, activity.ConfiguredPath);
        Assert.AreEqual("Process image unavailable", activity.TargetPath);
        Assert.AreEqual(81UL, activity.EventId);
    }

    [TestMethod]
    public void MissingFileTargetUsesSafeFallbackWithoutChangingConfiguredPath()
    {
        var notification = new NativeEventNotificationDataV1
        {
            EventId = 83,
            RuleAction = EspRuleAction.Block,
            EventType = EspEventType.FileObjectWrite
        };
        var descriptor = new WespRuleActivityDescriptor(
            WespActivityKind.Blocked,
            WespActivityResourceKind.File,
            "File write blocked",
            @"C:\Protected",
            WespActivityProcessSource.RequestorProcess,
            WespActivityTargetSource.IoTargetFileObject);

        var activity = WespActivityDecoder.Decode(
            in notification,
            descriptor,
            DateTimeOffset.UtcNow);

        Assert.AreEqual("File target unavailable", activity.TargetPath);
        Assert.AreEqual(@"C:\Protected", activity.ConfiguredPath);
    }

    [TestMethod]
    public void UnvalidatedLayoutKeepsBlockedRuleSeparateFromUnavailableTarget()
    {
        var notification = new NativeEventNotificationDataV1Header
        {
            EventId = 84,
            RuleAction = EspRuleAction.Block
        };
        var descriptor = new WespRuleActivityDescriptor(
            WespActivityKind.Blocked,
            WespActivityResourceKind.Registry,
            "Registry key deletion blocked",
            @"\REGISTRY\MACHINE\Software\Protected",
            WespActivityProcessSource.CurrentProcess,
            WespActivityTargetSource.RegistryKeyObject);

        var activity = WespActivityDecoder.DecodeWithoutProcessDetails(
            in notification,
            descriptor,
            DateTimeOffset.UtcNow);

        Assert.AreEqual(
            @"\REGISTRY\MACHINE\Software\Protected",
            activity.ConfiguredPath);
        Assert.AreEqual("Registry target unavailable", activity.TargetPath);
        Assert.AreEqual("Unknown process", activity.ProcessName);
        Assert.IsNull(activity.ProcessId);
    }

    private sealed class NativeProcessProperties : IDisposable
    {
        private readonly List<nint> _allocations = [];

        internal NativeProcessProperties(
            uint processId,
            string? imagePath,
            string? imageNtPath = null,
            bool includeDosPathError = false)
        {
            var properties = new List<NativeProperty>
            {
                new()
                {
                    PropertyId = WespRuleCompiler.ProcessIdProperty,
                    Type = EspVariantType.UInt32,
                    Value = (nint)processId
                }
            };

            AddStringProperty(
                properties,
                WespRuleCompiler.ProcessImageNtPathProperty,
                imageNtPath);
            if (imagePath is not null)
            {
                AddStringProperty(
                    properties,
                    WespRuleCompiler.ProcessImageDosPathProperty,
                    imagePath);
            }
            else if (includeDosPathError)
            {
                properties.Add(new NativeProperty
                {
                    PropertyId = WespRuleCompiler.ProcessImageDosPathProperty,
                    Type = EspVariantType.Error,
                    Value = unchecked((nint)(int)0x80070002)
                });
            }

            var propertyBuffer = Marshal.AllocHGlobal(
                checked(properties.Count * WespAbiV013.PropertySize));
            _allocations.Add(propertyBuffer);
            for (var index = 0; index < properties.Count; index++)
            {
                Marshal.StructureToPtr(
                    properties[index],
                    nint.Add(propertyBuffer, index * WespAbiV013.PropertySize),
                    fDeleteOld: false);
            }

            Query = new NativePropertyQuery
            {
                PropertiesCount = (uint)properties.Count,
                Properties = propertyBuffer
            };
        }

        internal NativePropertyQuery Query { get; }

        public void Dispose()
        {
            foreach (var allocation in Enumerable.Reverse(_allocations))
            {
                Marshal.FreeHGlobal(allocation);
            }
        }

        private nint Allocate<T>(T value) where T : struct
        {
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            _allocations.Add(pointer);
            Marshal.StructureToPtr(value, pointer, fDeleteOld: false);
            return pointer;
        }

        private void AddStringProperty(
            List<NativeProperty> properties,
            uint propertyId,
            string? value)
        {
            if (value is null)
            {
                return;
            }

            var stringBuffer = Marshal.StringToHGlobalUni(value);
            _allocations.Add(stringBuffer);
            var unicodeStringPointer = Allocate(new NativeUnicodeString
            {
                LengthBytes = checked((ushort)(value.Length * sizeof(char))),
                Buffer = stringBuffer
            });
            properties.Add(new NativeProperty
            {
                PropertyId = propertyId,
                Type = EspVariantType.UnicodeString,
                Value = unicodeStringPointer
            });
        }
    }

    private sealed class NativeStringProperties : IDisposable
    {
        private readonly List<nint> _allocations = [];

        internal NativeStringProperties(
            uint preferredPropertyId,
            string? value,
            uint? fallbackPropertyId = null,
            string? fallbackValue = null,
            bool includePreferredError = false)
        {
            var properties = new List<NativeProperty>();
            if (value is not null)
            {
                AddStringProperty(properties, preferredPropertyId, value);
            }
            else if (includePreferredError)
            {
                properties.Add(new NativeProperty
                {
                    PropertyId = preferredPropertyId,
                    Type = EspVariantType.Error,
                    Value = unchecked((nint)(int)0x80070002)
                });
            }

            if (fallbackPropertyId.HasValue && fallbackValue is not null)
            {
                AddStringProperty(
                    properties,
                    fallbackPropertyId.Value,
                    fallbackValue);
            }

            var propertyBuffer = Marshal.AllocHGlobal(
                checked(properties.Count * WespAbiV013.PropertySize));
            _allocations.Add(propertyBuffer);
            for (var index = 0; index < properties.Count; index++)
            {
                Marshal.StructureToPtr(
                    properties[index],
                    nint.Add(propertyBuffer, index * WespAbiV013.PropertySize),
                    fDeleteOld: false);
            }

            Query = new NativePropertyQuery
            {
                PropertiesCount = (uint)properties.Count,
                Properties = propertyBuffer
            };
        }

        internal NativePropertyQuery Query { get; }

        public void Dispose()
        {
            foreach (var allocation in Enumerable.Reverse(_allocations))
            {
                Marshal.FreeHGlobal(allocation);
            }
        }

        private nint Allocate<T>(T value) where T : struct
        {
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            _allocations.Add(pointer);
            Marshal.StructureToPtr(value, pointer, fDeleteOld: false);
            return pointer;
        }

        private void AddStringProperty(
            List<NativeProperty> properties,
            uint propertyId,
            string value)
        {
            var stringBuffer = Marshal.StringToHGlobalUni(value);
            _allocations.Add(stringBuffer);
            var unicodeStringPointer = Allocate(new NativeUnicodeString
            {
                LengthBytes = checked((ushort)(value.Length * sizeof(char))),
                Buffer = stringBuffer
            });
            properties.Add(new NativeProperty
            {
                PropertyId = propertyId,
                Type = EspVariantType.UnicodeString,
                Value = unicodeStringPointer
            });
        }
    }

    private sealed class NativeEventParameters : IDisposable
    {
        private readonly List<nint> _allocations;

        private NativeEventParameters(nint pointer, List<nint> allocations)
        {
            Pointer = pointer;
            _allocations = allocations;
        }

        internal nint Pointer { get; }

        internal static NativeEventParameters ForNewProcess(NativePropertyQuery query)
        {
            var allocations = new List<nint>();
            var processInfo = Allocate(
                new NativeProcessInfoIdentity { ProcessQuery = query },
                allocations);
            return new NativeEventParameters(processInfo, allocations);
        }

        internal static NativeEventParameters ForIoRequestor(
            NativePropertyQuery query,
            NativePropertyQuery targetQuery = default)
        {
            var allocations = new List<nint>();
            var processInfo = Allocate(
                new NativeProcessInfoIdentity { ProcessQuery = query },
                allocations);
            var targetFileObject = targetQuery.Properties == 0
                ? 0
                : Allocate(
                    new NativeEventObjectInfoIdentity
                    {
                        PropertyQuery = targetQuery
                    },
                    allocations);
            var operation = Allocate(
                new NativeIoOperationIdentity
                {
                    RequestorProcess = processInfo,
                    TargetFileObject = targetFileObject
                },
                allocations);
            return new NativeEventParameters(operation, allocations);
        }

        internal static NativeEventParameters ForRegistryKey(
            NativePropertyQuery query)
        {
            var allocations = new List<nint>();
            var keyInfo = Allocate(
                new NativeEventObjectInfoIdentity { PropertyQuery = query },
                allocations);
            return new NativeEventParameters(keyInfo, allocations);
        }

        internal static NativeEventParameters ForRegistryKeyObject(
            NativePropertyQuery query)
        {
            var allocations = new List<nint>();
            var keyInfo = Allocate(
                new NativeEventObjectInfoIdentity { PropertyQuery = query },
                allocations);
            var keyObjectInfo = Allocate(
                new NativeRegistryKeyObjectInfoIdentity
                {
                    RegistryKeyInfo = keyInfo
                },
                allocations);
            return new NativeEventParameters(keyObjectInfo, allocations);
        }

        public void Dispose()
        {
            foreach (var allocation in Enumerable.Reverse(_allocations))
            {
                Marshal.FreeHGlobal(allocation);
            }
        }

        private static nint Allocate<T>(T value, List<nint> allocations)
            where T : struct
        {
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            allocations.Add(pointer);
            Marshal.StructureToPtr(value, pointer, fDeleteOld: false);
            return pointer;
        }
    }
}
