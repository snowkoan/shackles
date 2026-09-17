using System.Runtime.InteropServices;
using Shackles.Wfp.Interop;

namespace Shackles.Wfp.Tests;

[TestClass]
public sealed class WfpInteropLayoutTests
{
    [TestMethod]
    public void NativeStructuresMatchWindowsSdkLayout()
    {
        if (IntPtr.Size == 8)
        {
            Assert.AreEqual(16, Marshal.SizeOf<NativeDisplayData>());
            Assert.AreEqual(16, Marshal.SizeOf<NativeByteBlob>());
            Assert.AreEqual(16, Marshal.SizeOf<NativeByteArray16>());
            Assert.AreEqual(16, Marshal.SizeOf<NativeValue>());
            Assert.AreEqual(16, Marshal.SizeOf<NativeConditionValue>());
            Assert.AreEqual(72, Marshal.SizeOf<NativeSession>());
            Assert.AreEqual(64, Marshal.SizeOf<NativeProvider>());
            Assert.AreEqual(72, Marshal.SizeOf<NativeSubLayer>());
            Assert.AreEqual(40, Marshal.SizeOf<NativeFilterCondition>());
            Assert.AreEqual(200, Marshal.SizeOf<NativeFilter>());
        }
        else
        {
            Assert.AreEqual(8, Marshal.SizeOf<NativeDisplayData>());
            Assert.AreEqual(8, Marshal.SizeOf<NativeByteBlob>());
            Assert.AreEqual(16, Marshal.SizeOf<NativeByteArray16>());
            Assert.AreEqual(8, Marshal.SizeOf<NativeValue>());
            Assert.AreEqual(8, Marshal.SizeOf<NativeConditionValue>());
            Assert.AreEqual(48, Marshal.SizeOf<NativeSession>());
            Assert.AreEqual(40, Marshal.SizeOf<NativeProvider>());
            Assert.AreEqual(44, Marshal.SizeOf<NativeSubLayer>());
            Assert.AreEqual(28, Marshal.SizeOf<NativeFilterCondition>());
            Assert.AreEqual(152, Marshal.SizeOf<NativeFilter>());
        }

        Assert.AreEqual(8, Marshal.SizeOf<NativeV4AddressAndMask>());
        Assert.AreEqual(17, Marshal.SizeOf<NativeV6AddressAndMask>());
        Assert.AreEqual(20, Marshal.SizeOf<NativeAction>());
        Assert.AreEqual(16, Marshal.SizeOf<NativeFilterContext>());
        Assert.AreEqual(4, Marshal.SizeOf<NativeTokenElevation>());
    }

    [TestMethod]
    public void PointerBearingFieldsHaveArchitectureCorrectOffsets()
    {
        if (IntPtr.Size == 8)
        {
            Assert.AreEqual(8, Marshal.OffsetOf<NativeByteBlob>("Data").ToInt32());
            Assert.AreEqual(8, Marshal.OffsetOf<NativeValue>("Value").ToInt32());
            Assert.AreEqual(120, Marshal.OffsetOf<NativeFilter>("Conditions").ToInt32());
            Assert.AreEqual(168, Marshal.OffsetOf<NativeFilter>("Reserved").ToInt32());
        }
        else
        {
            Assert.AreEqual(4, Marshal.OffsetOf<NativeByteBlob>("Data").ToInt32());
            Assert.AreEqual(4, Marshal.OffsetOf<NativeValue>("Value").ToInt32());
            Assert.AreEqual(84, Marshal.OffsetOf<NativeFilter>("Conditions").ToInt32());
            Assert.AreEqual(128, Marshal.OffsetOf<NativeFilter>("Reserved").ToInt32());
        }
    }
}
