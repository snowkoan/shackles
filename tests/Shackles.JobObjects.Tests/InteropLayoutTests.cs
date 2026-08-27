using System.Runtime.InteropServices;
using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects.Tests;

[TestClass]
public sealed class InteropLayoutTests
{
    [TestMethod]
    public void NativeStructuresMatchWindowsSdkLayout()
    {
        if (IntPtr.Size == 8)
        {
            Assert.AreEqual(64, Marshal.SizeOf<NativeBasicLimitInformation>());
            Assert.AreEqual(144, Marshal.SizeOf<NativeExtendedLimitInformation>());
            Assert.AreEqual(16, Marshal.SizeOf<NativeGroupAffinity>());
            Assert.AreEqual(104, Marshal.SizeOf<NativeStartupInfo>());
            Assert.AreEqual(24, Marshal.SizeOf<NativeProcessInformation>());
        }
        else
        {
            Assert.AreEqual(44, Marshal.SizeOf<NativeBasicLimitInformation>());
            Assert.AreEqual(108, Marshal.SizeOf<NativeExtendedLimitInformation>());
            Assert.AreEqual(12, Marshal.SizeOf<NativeGroupAffinity>());
            Assert.AreEqual(68, Marshal.SizeOf<NativeStartupInfo>());
            Assert.AreEqual(16, Marshal.SizeOf<NativeProcessInformation>());
        }

        Assert.AreEqual(8, Marshal.SizeOf<NativeCpuRateControlInformation>());
        Assert.AreEqual(16, Marshal.SizeOf<NativeNetworkRateControlInformation>());
        Assert.AreEqual(72, Marshal.SizeOf<NativeNotificationLimitInformation2>());
        Assert.AreEqual(104, Marshal.SizeOf<NativeLimitViolationInformation2>());
    }
}
