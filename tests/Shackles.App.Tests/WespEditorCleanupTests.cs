using System.ComponentModel;
using System.Reflection;
using Shackles.App.Views;

namespace Shackles.App.Tests;

[TestClass]
public sealed class WespEditorCleanupTests
{
    [TestMethod]
    public void ResourceAccessDraftNotifiesOnlyWhenNormalizedValueChanges()
    {
        var changes = 0;
        var draftType = typeof(WespWorkspaceView).GetNestedType(
            "WespResourceRuleDraft", BindingFlags.NonPublic)!;
        var draft = Activator.CreateInstance(
            draftType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { @"C:\test", 0, (Action)(() => changes++) }, null)!;
        var access = draftType.GetProperty("AccessIndex")!;
        var notifications = new List<string?>();
        ((INotifyPropertyChanged)draft).PropertyChanged += (_, args) =>
            notifications.Add(args.PropertyName);

        access.SetValue(draft, 1);
        access.SetValue(draft, 1);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(1, access.GetValue(draft));
        Assert.HasCount(1, notifications);
        Assert.AreEqual("AccessIndex", notifications.Single());

        access.SetValue(draft, 7);
        access.SetValue(draft, 0);
        Assert.AreEqual(2, changes);
        Assert.AreEqual(0, access.GetValue(draft));
        Assert.HasCount(2, notifications);
    }
}
