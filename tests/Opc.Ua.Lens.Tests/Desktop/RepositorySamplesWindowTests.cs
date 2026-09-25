/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Samples;
using UaLens.Tests.Administration;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Desktop
{
    [TestFixture]
    [Platform("Win,Linux")]
    [NonParallelizable]
    public sealed class RepositorySamplesWindowTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public Task StartRequiresTrustedSetupAndFreshConsentWithoutConnecting(int sampleId)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                await using var context = new DesktopConnectionContext();
                var stub = new SampleStub();
                var sample = (RepositorySampleId)sampleId;
                Uri? used = null;
                await using var window = new RepositorySamplesWindow(
                    stub.Service.Object, context.Telemetry.CreateLogger("Samples"), endpoint => used = endpoint,
                    _ => Task.FromResult<string?>(s_sourceRoot));
                window.Show(DesktopInteraction.Owner);
                Assert.That(stub.Configurations, Is.Zero);
                Assert.That(stub.Starts, Is.Zero);
                Assert.That(Button(window, "StartSampleButton").IsEnabled, Is.False);
                Click(window, "StartSampleButton");
                Assert.That(stub.Starts, Is.Zero);
                Control<ComboBox>(window, "SampleBox").SelectedIndex = (int)sample;
                await ConfigureAsync(window).ConfigureAwait(true);
                Assert.That(stub.Selection, Is.EqualTo(sample));
                Assert.That(stub.Source, Is.EqualTo(new RepositorySampleSource(
                    s_sourceRoot, RepositorySampleBuildConfiguration.Release, RepositorySampleFramework.Net10)));
                Assert.That(Button(window, "StartSampleButton").IsEnabled, Is.False);
                Control<NumericUpDown>(window, "RunSecondsBox").Value = 90;
                Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked = true;
                bool consumed = false;
                stub.Start = (options, _) =>
                {
                    consumed = Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked == false;
                    Assert.That(options.RunSeconds, Is.EqualTo(90));
                    return Task.FromResult(stub.BeginRun(RepositorySamplePhase.AdvertisedReady));
                };
                await StatusAsync(window, "AdvertisedReady", () => Click(window, "StartSampleButton"))
                    .ConfigureAwait(true);
                Assert.That(consumed, Is.True);
                Assert.That(stub.Starts, Is.EqualTo(1));
                Assert.That(Button(window, "StartSampleButton").IsEnabled, Is.False);
                Assert.That(Control<ComboBox>(window, "SampleBox").IsEnabled, Is.False);
                Click(window, "UseSampleEndpointButton");
                Assert.That(used, Is.EqualTo(s_endpoint));
                Assert.That(context.Connection.IsConnected, Is.False);
                Assert.That(context.Discoveries, Is.Empty);
                Assert.That(stub.Snapshot.SecureConnectAuthorized, Is.False);
                await StatusAsync(window, "Stopped", () => Click(window, "StopSampleButton")).ConfigureAwait(true);
                Assert.That(stub.Stops, Is.EqualTo(1));
                Assert.That(Button(window, "UseSampleEndpointButton").IsEnabled, Is.False);
                Assert.That(Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked, Is.False);
                stub.Service.Verify(service => service.DisposeAsync(), Times.Never);
            });
        }

        [Test]
        public Task ChangingBuildOrDurationRevokesTheCorrespondingConfirmation()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                await using var context = new DesktopConnectionContext();
                var stub = new SampleStub();
                await using var window = new RepositorySamplesWindow(
                    stub.Service.Object, context.Telemetry.CreateLogger("Samples"), _ => { },
                    _ => Task.FromResult<string?>(s_sourceRoot));
                window.Show(DesktopInteraction.Owner);
                await ConfigureAsync(window).ConfigureAwait(true);
                Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked = true;
                Assert.That(Button(window, "StartSampleButton").IsEnabled, Is.True);
                Control<CheckBox>(window, "TrustSourceBox").IsChecked = false;
                Control<CheckBox>(window, "TrustSourceBox").IsChecked = true;
                Assert.That(Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked, Is.False);
                Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked = true;
                Control<NumericUpDown>(window, "RunSecondsBox").Value = 30;
                Assert.That(Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked, Is.False);
                Assert.That(Control<CheckBox>(window, "TrustSourceBox").IsChecked, Is.True);
                Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked = true;
                Control<ComboBox>(window, "BuildConfigurationBox").SelectedItem =
                    RepositorySampleBuildConfiguration.Debug;
                Assert.That(Control<CheckBox>(window, "TrustSourceBox").IsChecked, Is.False);
                Assert.That(Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked, Is.False);
                Assert.That(Button(window, "StartSampleButton").IsEnabled, Is.False);
                Assert.That(stub.Configurations, Is.EqualTo(1));
                Assert.That(stub.Starts, Is.Zero);
            });
        }

        [Test]
        public Task ARemovedBuildRequiresSetupAgainInsteadOfReusingTheOldApproval()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                await using var context = new DesktopConnectionContext();
                var stub = new SampleStub();
                await using var window = new RepositorySamplesWindow(
                    stub.Service.Object, context.Telemetry.CreateLogger("Samples"), _ => { },
                    _ => Task.FromResult<string?>(s_sourceRoot));
                window.Show(DesktopInteraction.Owner);
                await ConfigureAsync(window).ConfigureAwait(true);
                stub.Start = (_, _) =>
                {
                    stub.RequireSetup();
                    return Task.FromException<RepositorySampleSnapshot>(
                        new RepositorySampleException(RepositorySampleFailure.Prerequisite, "Build removed."));
                };
                Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked = true;
                await StatusAsync(window, "Build removed", () => Click(window, "StartSampleButton")).ConfigureAwait(
                    true);
                Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked = true;
                Assert.That(Button(window, "StartSampleButton").IsEnabled, Is.False);
                Assert.That(Control<CheckBox>(window, "ConfirmSampleRunBox").IsEnabled, Is.False);
                Assert.That(stub.Starts, Is.EqualTo(1));
                await StatusAsync(window, "Configured", () => Click(window, "ConfigureSampleButton")).ConfigureAwait(
                    true);
                Assert.That(Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked, Is.False);
                Assert.That(stub.Configurations, Is.EqualTo(2));
            });
        }

        [Test]
        public Task ClosingCancelsStartupAndWaitsForOwnedCleanup()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                await using var context = new DesktopConnectionContext();
                var stub = new SampleStub();
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var ready = new TaskCompletionSource<RepositorySampleSnapshot>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken startupToken = default;
                stub.Start = (_, token) =>
                {
                    startupToken = token;
                    stub.BeginRun(RepositorySamplePhase.Starting);
                    entered.TrySetResult();
                    return ready.Task.WaitAsync(token);
                };
                stub.Stop = async () =>
                {
                    stopEntered.TrySetResult();
                    await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                    return stub.FinishRun();
                };
                var window = new RepositorySamplesWindow(
                    stub.Service.Object, context.Telemetry.CreateLogger("Samples"), _ => { },
                    _ => Task.FromResult<string?>(s_sourceRoot));
                window.Show(DesktopInteraction.Owner);
                try
                {
                    await ConfigureAsync(window).ConfigureAwait(true);
                    Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked = true;
                    Click(window, "StartSampleButton");
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                    Task close = window.DisposeAsync().AsTask();
                    await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                    Assert.That(startupToken.IsCancellationRequested, Is.True);
                    Assert.That(close.IsCompleted, Is.False);
                    Assert.That(window.IsVisible, Is.True);
                    Assert.That(stub.Stops, Is.EqualTo(1));
                    stopped.TrySetResult();
                    await close.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                    Assert.That(window.IsVisible, Is.False);
                    Assert.That(stub.Snapshot.OwnsResources, Is.False);
                    Assert.That(context.Connection.IsConnected, Is.False);
                    stub.Service.Verify(service => service.DisposeAsync(), Times.Never);
                }
                finally
                {
                    stopped.TrySetResult();
                    ready.TrySetResult(stub.Snapshot);
                    await window.DisposeAsync().ConfigureAwait(true);
                }
            });
        }

        [Test]
        public Task FailedCloseKeepsCleanupRetryAvailableAndNeverReenablesStart()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                await using var context = new DesktopConnectionContext();
                var stub = new SampleStub();
                await using var window = new RepositorySamplesWindow(
                    stub.Service.Object, context.Telemetry.CreateLogger("Samples"), _ => { },
                    _ => Task.FromResult<string?>(s_sourceRoot));
                window.Show(DesktopInteraction.Owner);
                await ConfigureAsync(window).ConfigureAwait(true);
                Control<CheckBox>(window, "ConfirmSampleRunBox").IsChecked = true;
                await StatusAsync(window, "AdvertisedReady", () => Click(window, "StartSampleButton"))
                    .ConfigureAwait(true);
                stub.Stop = () => Task.FromResult(stub.Snapshot with
                {
                    Phase = RepositorySamplePhase.CleanupRequired,
                    OwnsResources = true
                });
                await StatusAsync(window, "still owns resources", window.Close).ConfigureAwait(true);
                Assert.That(window.IsVisible, Is.True);
                Assert.That(Button(window, "StopSampleButton").IsEnabled, Is.True);
                Assert.That(Button(window, "StartSampleButton").IsEnabled, Is.False);
                stub.Stop = () => Task.FromResult(stub.FinishRun());
                await StatusAsync(window, "Stopped", () => Click(window, "StopSampleButton")).ConfigureAwait(true);
                await window.DisposeAsync().ConfigureAwait(true);
                Assert.That(window.IsVisible, Is.False);
                Assert.That(stub.Snapshot.OwnsResources, Is.False);
            });
        }

        [Test]
        public Task MainWindowCloseRetriesOwnedSampleCleanupInsteadOfForcingExit()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                await using var context = new DesktopConnectionContext();
                using var files = new TemporaryCertificateStores();
                var stub = new SampleStub();
                stub.BeginRun(RepositorySamplePhase.AdvertisedReady);
                stub.Stop = () => Task.FromResult(stub.Snapshot with
                {
                    Phase = RepositorySamplePhase.CleanupRequired,
                    OwnsResources = true
                });
                await using var model = new MainViewModel(context.Telemetry, context.Connection);
                var window = new MainWindow(
                    model, new AppearancePreferences(Path.Combine(files.Root, "appearance.json")),
                    favoritesPath: Path.Combine(files.Root, "favorites.json"), repositorySamples: stub.Service.Object);
                window.Show(DesktopInteraction.Owner);
                TextBlock error = Control<TextBlock>(window, "OperationErrorText");
                await DesktopInteraction.ChangedAsync(error, () => error.Text?.Contains(
                    "owned repository sample", StringComparison.Ordinal) == true, () =>
                {
                    window.Close();
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(window.IsVisible, Is.True);
                Assert.That(stub.Stops, Is.EqualTo(1));
                var retryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                stub.Stop = async () =>
                {
                    retryEntered.TrySetResult();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                    return stub.FinishRun();
                };
                try
                {
                    window.Close();
                    await retryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                    Assert.That(window.IsVisible, Is.True);
                    Assert.That(stub.Snapshot.OwnsResources, Is.True);
                    release.TrySetResult();
                    await window.DisposeAsync().ConfigureAwait(true);
                    Assert.That(stub.Stops, Is.EqualTo(2));
                    Assert.That(stub.Snapshot.OwnsResources, Is.False);
                }
                finally
                {
                    release.TrySetResult();
                    stub.FinishRun();
                    window.Close();
                }
            });
        }

        [Test]
        public Task ShellOpensTheModelessSampleWindowWithoutAddingAToolOrStartingAnything()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                await using var context = new DesktopConnectionContext();
                using var files = new TemporaryCertificateStores();
                var stub = new SampleStub();
                await using var model = new MainViewModel(context.Telemetry, context.Connection);
                await using var window = new MainWindow(
                    model, new AppearancePreferences(Path.Combine(files.Root, "appearance.json")),
                    favoritesPath: Path.Combine(files.Root, "favorites.json"), repositorySamples: stub.Service.Object);
                window.Show(DesktopInteraction.Owner);
                Button settings = Button(window, "ConnectionSettingsButton");
                MenuItem entry = ((MenuFlyout)settings.Flyout!).Items.OfType<MenuItem>()
                    .Single(item => item.Name == "MenuRepositorySamples");
                RepositorySamplesWindow samples = await DesktopInteraction.OpenedAsync<RepositorySamplesWindow>(
                    () => entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))).ConfigureAwait(true);
                Assert.That(samples.Owner, Is.SameAs(window));
                Assert.That(PluginRegistry.All.Count, Is.EqualTo(17));
                Assert.That(stub.Configurations, Is.Zero);
                Assert.That(stub.Starts, Is.Zero);
                Assert.That(context.Discoveries, Is.Empty);
                await window.DisposeAsync().ConfigureAwait(true);
                Assert.That(samples.IsVisible, Is.False);
                Assert.That(stub.Stops, Is.EqualTo(1));
                stub.Service.Verify(service => service.DisposeAsync(), Times.Never);
            });
        }

        private static async Task ConfigureAsync(RepositorySamplesWindow window)
        {
            TextBox root = Control<TextBox>(window, "SourceRootBox");
            await DesktopInteraction.ChangedAsync(root, () => root.Text == s_sourceRoot, () =>
            {
                Click(window, "BrowseSourceButton");
                return Task.CompletedTask;
            }).ConfigureAwait(true);
            Control<CheckBox>(window, "TrustSourceBox").IsChecked = true;
            await StatusAsync(window, "Configured", () => Click(window, "ConfigureSampleButton")).ConfigureAwait(true);
        }

        private static Task StatusAsync(RepositorySamplesWindow window, string fragment, Action action)
        {
            TextBlock status = Control<TextBlock>(window, "SampleStatus");
            return DesktopInteraction.ChangedAsync(
                status, () => status.Text?.Contains(fragment, StringComparison.Ordinal) == true, () =>
                {
                    action();
                    return Task.CompletedTask;
                });
        }

        private static T Control<T>(Control window, string name) where T : Control
        {
            return DesktopInteraction.Control<T>(window, name);
        }

        private static Button Button(Control window, string name)
        {
            return Control<Button>(window, name);
        }

        private static void Click(Control window, string name)
        {
            DesktopInteraction.Click(Button(window, name));
        }

        private sealed class SampleStub
        {
            public SampleStub()
            {
                m_completion = Task.FromResult(Snapshot);
                Start = (_, _) => Task.FromResult(BeginRun(RepositorySamplePhase.AdvertisedReady));
                Stop = () => Task.FromResult(FinishRun());
                Service.SetupGet(service => service.Snapshot).Returns(() => Snapshot);
                Service.SetupGet(service => service.Completion).Returns(() => m_completion);
                Service.Setup(service => service.ConfigureAsync(
                    It.IsAny<RepositorySampleId>(), It.IsAny<RepositorySampleSource>(), It.IsAny<CancellationToken>()))
                    .Returns((RepositorySampleId sample, RepositorySampleSource source, CancellationToken _) =>
                    {
                        Configurations++;
                        Selection = sample;
                        Source = source;
                        Snapshot = new RepositorySampleSnapshot(
                            sample,
                            RepositorySamplePhase.Configured,
                            "Fixture setup");
                        m_completion = Task.FromResult(Snapshot);
                        return m_completion;
                    });
                Service.Setup(service => service.StartAsync(
                    It.IsAny<RepositorySampleRunOptions>(), It.IsAny<CancellationToken>()))
                    .Returns((RepositorySampleRunOptions options, CancellationToken token) =>
                    {
                        Starts++;
                        return Start(options, token);
                    });
                Service.Setup(service => service.StopAsync(It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Stops++;
                        return Stop();
                    });
            }

            public Mock<IRepositorySampleService> Service { get; } = new(MockBehavior.Strict);

            public RepositorySampleSnapshot Snapshot { get; private set; } = new(
                RepositorySampleId.ConsoleReferenceServer,
                RepositorySamplePhase.RequiresConfiguration,
                "Select source");

            public RepositorySampleSource? Source { get; private set; }

            public RepositorySampleId Selection { get; private set; }

            public int Configurations { get; private set; }

            public int Starts { get; private set; }

            public int Stops { get; private set; }

            public Func<RepositorySampleRunOptions, CancellationToken,
                Task<RepositorySampleSnapshot>> Start
            { get; set; }

            public Func<Task<RepositorySampleSnapshot>> Stop { get; set; }

            public RepositorySampleSnapshot BeginRun(RepositorySamplePhase phase)
            {
                Snapshot = Snapshot with
                {
                    Phase = phase,
                    Message = "Fixture run",
                    OwnsResources = true,
                    Evidence = new RepositorySampleEvidence(s_endpoint, "urn:fixture", "Fixture", "urn:fixture:product",
                        new ByteString(new byte[32])),
                    Endpoint = s_endpoint,
                    ProcessId = 12345
                };
                m_finished = new TaskCompletionSource<RepositorySampleSnapshot>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                m_completion = m_finished.Task;
                return Snapshot;
            }

            public RepositorySampleSnapshot FinishRun()
            {
                Snapshot = Snapshot with
                {
                    Phase = RepositorySamplePhase.Stopped,
                    Message = "Fixture stopped",
                    OwnsResources = false
                };
                m_finished?.TrySetResult(Snapshot);
                m_completion = Task.FromResult(Snapshot);
                return Snapshot;
            }

            public void RequireSetup()
            {
                Snapshot = Snapshot with
                {
                    Phase = RepositorySamplePhase.RequiresConfiguration,
                    Message = "Build removed.",
                    OwnsResources = false
                };
            }

            private Task<RepositorySampleSnapshot> m_completion;
            private TaskCompletionSource<RepositorySampleSnapshot>? m_finished;
        }

        private static readonly string s_sourceRoot = Path.Combine(Path.GetTempPath(), "ualens-trusted-fixture");
        private static readonly Uri s_endpoint = new("opc.tcp://localhost:62542/Quickstarts/ReferenceServer");
    }
}
