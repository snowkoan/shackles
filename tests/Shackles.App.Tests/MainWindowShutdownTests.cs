using System.ComponentModel;
using System.Reflection;
using System.Windows;
using Shackles.App.Models;
using Shackles.App.Services;
using Shackles.App.ViewModels;

namespace Shackles.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MainWindowShutdownTests
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyCleanupDefersCloseUntilClosingCallbackReturns(bool initialized)
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var fixture = new Fixture();
            try
            {
                if (initialized)
                {
                    await fixture.Main.InitializeAsync();
                }

                var args = new CancelEventArgs();
                InvokeClosing(fixture.Window, args);

                Assert.IsTrue(args.Cancel, "The original close must be canceled while cleanup is queued.");
                Assert.IsFalse(fixture.Closed.Task.IsCompleted, "Cleanup must not close the window inside its closing callback.");
                Assert.AreEqual(0, fixture.Service.DisposeCalls, "Cleanup must begin after the closing callback returns.");

                await fixture.Closed.Task.WaitAsync(CloseTimeout);

                Assert.AreEqual(1, fixture.Service.DisposeCalls);
                Assert.AreEqual(1, fixture.ClosedCount);
            }
            finally { fixture.Dispose(); }
        });
    }

    [TestMethod]
    public async Task RepeatedImmediateCloseDisposesOnceAndAllowsTheFinalClose()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var fixture = new Fixture();
            try
            {
                fixture.Window.Close();
                fixture.Window.Close();

                Assert.IsFalse(fixture.Closed.Task.IsCompleted);
                Assert.AreEqual(0, fixture.Service.DisposeCalls);
                await fixture.Closed.Task.WaitAsync(CloseTimeout);

                Assert.AreEqual(1, fixture.Service.DisposeCalls);
                Assert.AreEqual(1, fixture.ClosedCount);
                Assert.AreEqual(3, fixture.ClosingCount, "Both immediate attempts are canceled before the final permitted close.");
            }
            finally { fixture.Dispose(); }
        });
    }

    [TestMethod]
    public async Task LoadedDuringClosingDoesNotStartInitialization()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var fixture = new Fixture();
            try
            {
                var args = new CancelEventArgs();
                InvokeClosing(fixture.Window, args);
                fixture.Window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                Assert.IsTrue(args.Cancel);
                Assert.IsFalse(fixture.Main.HasPendingOperations, "Startup must not race cleanup after closing begins.");
                Assert.AreEqual(0, fixture.ProcessReadCount, "Startup must not read processes after closing begins.");
                await fixture.Closed.Task.WaitAsync(CloseTimeout);

                Assert.AreEqual(0, fixture.ProcessReadCount);
                Assert.AreEqual(1, fixture.Service.DisposeCalls);
                Assert.AreEqual(1, fixture.ClosedCount);
            }
            finally { fixture.Dispose(); }
        });
    }

    private static void InvokeClosing(MainWindow window, CancelEventArgs args) =>
        typeof(MainWindow).GetMethod("Window_Closing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [window, args]);

    private sealed class Fixture : IDisposable
    {
        private int _processReads;

        public FakeService Service { get; } = new();
        public MainViewModel Main { get; }
        public MainWindow Window { get; }
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ClosingCount { get; private set; }
        public int ClosedCount { get; private set; }
        public int ProcessReadCount => Volatile.Read(ref _processReads);

        public Fixture()
        {
            Main = new MainViewModel(Service, () =>
            {
                Interlocked.Increment(ref _processReads);
                return [];
            });
            Window = new MainWindow(Main);
            Window.Closing += (_, _) => ClosingCount++;
            Window.Closed += (_, _) =>
            {
                ClosedCount++;
                Closed.TrySetResult();
            };
        }

        public void Dispose()
        {
            Window.Dispose();
            Window.Close();
        }
    }

    private sealed class FakeService : IJobControlService
    {
        public JobCapabilitySet Capabilities => JobCapabilitySet.Unavailable("test");
        public int DisposeCalls { get; private set; }
        public ProcessIdentityCaptureResult CaptureProcessIdentity(int processId) =>
            throw new AssertFailedException("An empty shutdown fixture must not inspect processes.");
        public IJobSession CreateJob(string? name) =>
            throw new AssertFailedException("A shutdown fixture must not create jobs.");
        public IJobSession OpenJob(string name) =>
            throw new AssertFailedException("A shutdown fixture must not open jobs.");
        public void Dispose() => DisposeCalls++;
    }
}
