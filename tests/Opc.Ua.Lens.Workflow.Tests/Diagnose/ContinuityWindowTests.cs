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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Diagnostics;
using UaLens.Plugins.Continuity;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Diagnose
{
    /// <summary>
    /// Exercises actual Continuity controls without opening a protocol connection or controlling a server.
    /// </summary>
    [TestFixture]
    [Platform("Win,Linux")]
    [Category("LensDesktopMainline")]
    [NonParallelizable]
    public sealed class ContinuityWindowTests
    {
        /// <summary>
        /// Keeps scenario selection, requested settings and restore separate from starting a workload.
        /// </summary>
        [Test]
        public Task NativeContinuitySettingsAndPresetsNeverStartOnRestoreAsync()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new WindowContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ShowAsync().ConfigureAwait(true);
                    var selector = Find<ComboBox>(context.View, "ContinuityScenario");
                    foreach (ContinuityScenarioChoice choice in context.Plugin.Scenarios)
                    {
                        selector.SelectedItem = choice;
                        Assert.That(context.Plugin.SelectedScenario, Is.EqualTo(choice));
                        Assert.That(context.Plugin.ScenarioDescription, Is.Not.EqualTo("Select a scenario."));
                    }
                    Assert.That(context.Plugin.SupportsDuplicate, Is.False);
                    Assert.That(context.Plugin.HeaderToolbar, Is.Null);
                    Assert.That(context.Plugin.ContributeMenuItems(), Is.Empty);
                    var targets = Find<TextBox>(context.View, "ContinuityTargets");
                    targets.Text = string.Empty;
                    Assert.That(context.Plugin.CanStart, Is.False);
                    Press(Find<Button>(context.View, "ContinuityUseSelection"));
                    Assert.That(context.Plugin.Status, Does.StartWith("Select a Variable"));
                    context.SelectedTarget = "nsu=urn:ualens:continuity;s=counter";
                    Press(Find<Button>(context.View, "ContinuityUseSelection"));
                    Assert.That(targets.Text, Is.EqualTo(context.SelectedTarget));
                    Find<CheckBox>(context.View, "ContinuityMonotonic").IsChecked = true;
                    Press(Find<Button>(context.View, "ContinuityClockSample"));
                    Assert.That(targets.Text, Is.EqualTo("i=2258"));
                    Assert.That(context.Plugin.MonotonicSample, Is.False);
                    Find<NumericUpDown>(context.View, "ContinuityPublishInterval").Value = 250;
                    Find<NumericUpDown>(context.View, "ContinuitySampleInterval").Value = 75;
                    Find<NumericUpDown>(context.View, "ContinuityQueue").Value = 23;
                    Find<NumericUpDown>(context.View, "ContinuityPartitionSize").Value = 4;
                    Find<NumericUpDown>(context.View, "ContinuityKeepAlive").Value = 9;
                    Find<NumericUpDown>(context.View, "ContinuityLifetime").Value = 90;
                    Find<NumericUpDown>(context.View, "ContinuityDurableHours").Value = 2;
                    Find<CheckBox>(context.View, "ContinuityDurable").IsChecked = true;
                    System.Text.Json.JsonElement saved = context.Plugin.CaptureState();
                    context.Plugin.PublishingIntervalMs = 1000;
                    context.Plugin.QueueSize = 1;
                    context.Plugin.Durable = false;

                    await context.Plugin.RestoreStateAsync(saved).ConfigureAwait(true);

                    Assert.That(context.Plugin.PublishingIntervalMs, Is.EqualTo(250));
                    Assert.That(context.Plugin.SamplingIntervalMs, Is.EqualTo(75));
                    Assert.That(context.Plugin.QueueSize, Is.EqualTo(23));
                    Assert.That(context.Plugin.ItemsPerPartition, Is.EqualTo(4));
                    Assert.That(context.Plugin.KeepAliveCount, Is.EqualTo(9));
                    Assert.That(context.Plugin.LifetimeCount, Is.EqualTo(90));
                    Assert.That(context.Plugin.DurableLifetimeHours, Is.EqualTo(2));
                    Assert.That(context.Plugin.Durable, Is.True);
                    Assert.That(context.Plugin.Status, Does.Contain("prepared offline"));
                    Assert.That(context.Plugin.CanConfigure, Is.True);
                    context.Backend.Verify(value => value.StartAsync(
                        It.IsAny<ContinuityConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
                    context.Backend.Verify(value => value.StepAsync(It.IsAny<CancellationToken>()), Times.Never);
                    context.Window.Content = null;
                    await context.Plugin.ExportCommand.ExecuteAsync(null).ConfigureAwait(true);
                    Assert.That(context.Plugin.Status, Is.EqualTo("Export requires a desktop storage provider."));
                }
            });
        }

        /// <summary>
        /// Separates starting, saving, restoring and stopping while showing actual backend evidence.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public Task NativeContinuityRequiresExplicitStartAndEachRestoreStepAsync(bool durable)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new WindowContext();
                await using (context.ConfigureAwait(true))
                {
                    ContinuityConfiguration? applied = null;
                    context.Backend.Setup(value => value.StartAsync(
                        It.IsAny<ContinuityConfiguration>(), It.IsAny<CancellationToken>()))
                        .Callback((ContinuityConfiguration configuration, CancellationToken _) =>
                        {
                            applied = configuration;
                            context.Timeline.Record(ContinuityEvidenceKind.Data, "Owned sample callback",
                                context.Correlation, 13, 42, 7, DateTime.UtcNow, DateTime.UtcNow,
                                StatusCodes.Good.Code, 31);
                        }).Returns(Task.CompletedTask);
                    int steps = 0;
                    context.Backend.Setup(value => value.StepAsync(It.IsAny<CancellationToken>()))
                        .Returns(() =>
                        {
                            steps++;
                            return Task.FromResult(new ContinuityStepResult(
                                durable && steps == 1, durable && steps == 1
                                    ? "Saved own snapshot; waiting for your restart."
                                    : "Observed the configured transition."));
                        });
                    await context.ShowAsync().ConfigureAwait(true);
                    Select(context, durable
                        ? ContinuityScenario.GracefulDurableRestore : ContinuityScenario.TransferOnLoad);
                    Button start = Find<Button>(context.View, "ContinuityStart");
                    Button step = Find<Button>(context.View, "ContinuityStep");
                    Button stop = Find<Button>(context.View, "ContinuityStop");
                    Assert.That(step.IsEffectivelyEnabled, Is.False);

                    await PressAsync(start, context.Plugin.StartCommand).ConfigureAwait(true);

                    Assert.That(applied, Is.Not.Null);
                    Assert.That(applied!.Targets[0].ToString(), Is.EqualTo("i=2258"));
                    Assert.That(applied.Durable, Is.EqualTo(durable));
                    Assert.That(start.IsEffectivelyEnabled, Is.False);
                    Assert.That(step.IsEffectivelyEnabled, Is.True);
                    Assert.That(stop.IsEffectivelyEnabled, Is.True);
                    Assert.That(Find<ComboBox>(context.View, "ContinuityScenario").IsEnabled, Is.False);
                    Assert.That(Find<TextBox>(context.View, "ContinuityTargets").IsEffectivelyEnabled, Is.False);
                    Assert.That(context.Plugin.TimelineEntries.Any(entry =>
                        entry.Kind == ContinuityEvidenceKind.Data &&
                        entry.ClientSessionId == context.Correlation &&
                        entry.ClientSubscriptionId == 13 &&
                        entry.PartitionServerId == 42 &&
                        entry.SequenceNumber == 7), Is.True);
                    Assert.That(context.Plugin.DiagnosticRows.Single().Value, Is.EqualTo("250 ms / queue 23"));
                    Assert.That(context.Plugin.UiTimingText, Does.Contain("not packet receipt"));
                    Find<TabControl>(context.View).SelectedIndex = 1;
                    Assert.That(context.Plugin.ObservationText, Does.Contain("not proven loss"));
                    System.Text.Json.JsonElement running = context.Plugin.CaptureState();
                    await Assert.ThatAsync(() => context.Plugin.RestoreStateAsync(running),
                        Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(true);

                    await PressAsync(step, context.Plugin.StepCommand).ConfigureAwait(true);

                    if (durable)
                    {
                        Assert.That(context.Plugin.StepLabel, Is.EqualTo("Restore after your graceful restart"));
                        Assert.That(step.IsEffectivelyEnabled, Is.True);
                        Assert.That(steps, Is.EqualTo(1));
                        await PressAsync(step, context.Plugin.StepCommand).ConfigureAwait(true);
                    }
                    Assert.That(step.IsEffectivelyEnabled, Is.False);
                    Assert.That(steps, Is.EqualTo(durable ? 2 : 1));

                    await PressAsync(stop, context.Plugin.StopCommand).ConfigureAwait(true);

                    Assert.That(context.Plugin.CanConfigure, Is.True);
                    Assert.That(context.Plugin.Status, Does.StartWith("Stopped."));
                    context.Backend.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
                    context.Session.Verify(value => value.DisposeAsync(), Times.Never);
                }
            });
        }

        /// <summary>
        /// Keeps Stop usable while an asynchronous start is pending and joins cancellation before cleanup.
        /// </summary>
        [Test]
        public Task NativeContinuityStopCancelsAndJoinsPendingStartAsync()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new WindowContext();
                await using (context.ConfigureAwait(true))
                {
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    CancellationToken observed = default;
                    context.Backend.Setup(value => value.StartAsync(
                        It.IsAny<ContinuityConfiguration>(), It.IsAny<CancellationToken>()))
                        .Returns(async (ContinuityConfiguration _, CancellationToken token) =>
                        {
                            observed = token;
                            entered.SetResult();
                            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                        });
                    await context.ShowAsync().ConfigureAwait(true);
                    Press(Find<Button>(context.View, "ContinuityStart"));
                    Task start = context.Plugin.StartCommand.ExecutionTask ??
                        throw new AssertionException("The Start control did not dispatch its command.");
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                    Button stop = Find<Button>(context.View, "ContinuityStop");
                    Assert.That(stop.IsEffectivelyEnabled, Is.True);

                    await PressAsync(stop, context.Plugin.StopCommand).ConfigureAwait(true);
                    await start.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

                    Assert.That(observed.IsCancellationRequested, Is.True);
                    Assert.That(context.Plugin.CanConfigure, Is.True);
                    Assert.That(context.Plugin.CanStop, Is.False);
                    Assert.That(context.Plugin.Status, Does.Contain("Start failed:").And.Contain("Stopped."));
                    context.Backend.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
                    context.Session.Verify(value => value.DisposeAsync(), Times.Never);
                }
            });
        }

        /// <summary>
        /// Stops only the lab after a real primary-object replacement, not a transient connection notification.
        /// </summary>
        [Test]
        public Task NativeContinuityPrimaryReplacementStopsOnlyOwnedWorkAsync()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new WindowContext();
                await using (context.ConfigureAwait(true))
                {
                    await context.ShowAsync().ConfigureAwait(true);
                    await PressAsync(Find<Button>(context.View, "ContinuityStart"), context.Plugin.StartCommand)
                        .ConfigureAwait(true);
                    await context.Plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
                    context.Backend.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
                    var replacement = new Mock<ISession>(MockBehavior.Strict);
                    context.Primary = replacement.Object;

                    await context.Plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);

                    Assert.That(context.Plugin.Status, Does.StartWith("Stopped."));
                    Assert.That(context.Plugin.TimelineEntries.Any(entry =>
                        entry.Kind == ContinuityEvidenceKind.Connection &&
                        entry.Detail.Contains("explicitly detached/replaced", StringComparison.Ordinal)), Is.True);
                    context.Backend.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
                    context.Session.Verify(value => value.DisposeAsync(), Times.Never);
                    replacement.VerifyNoOtherCalls();
                }
            });
        }

        /// <summary>
        /// Shows setup, snapshot and operation failures rather than presenting a successful or retryable run.
        /// </summary>
        [TestCase("setup")]
        [TestCase("snapshot")]
        [TestCase("start")]
        [TestCase("step")]
        [TestCase("cleanup")]
        public Task NativeContinuityFailuresRemainVisibleAsync(string stage)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var context = new WindowContext();
                await using (context.ConfigureAwait(true))
                {
                    var failure = new InvalidOperationException("Owned " + stage + " rejected.");
                    if (stage == "setup")
                    {
                        context.Backend.Setup(value => value.CheckSetup(It.IsAny<ContinuityConfiguration>()))
                            .Throws(failure);
                    }
                    else if (stage == "snapshot")
                    {
                        context.Backend.Setup(value => value.CaptureDiagnostics()).Throws(failure);
                    }
                    else if (stage == "start")
                    {
                        context.Backend.Setup(value => value.StartAsync(
                            It.IsAny<ContinuityConfiguration>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                    }
                    else if (stage == "step")
                    {
                        context.Backend.Setup(value => value.StepAsync(It.IsAny<CancellationToken>()))
                            .ThrowsAsync(failure);
                    }
                    else
                    {
                        context.Backend.Setup(value => value.StopAsync(It.IsAny<CancellationToken>()))
                            .ThrowsAsync(failure);
                    }
                    await context.ShowAsync().ConfigureAwait(true);
                    Select(context, ContinuityScenario.RecreateOwnedSubscription);
                    if (stage == "setup")
                    {
                        Assert.That(context.Plugin.SetupText, Does.StartWith("Availability unknown:"));
                        Assert.That(context.Plugin.CanStart, Is.False);
                        context.Backend.Verify(value => value.StartAsync(
                            It.IsAny<ContinuityConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
                        return;
                    }
                    if (stage == "snapshot")
                    {
                        Assert.That(context.Plugin.DiagnosticRows.Single().Name, Is.EqualTo("Snapshot unavailable"));
                        Assert.That(context.Plugin.DiagnosticRows.Single().Value,
                            Is.EqualTo(nameof(InvalidOperationException)));
                        return;
                    }
                    await PressAsync(Find<Button>(context.View, "ContinuityStart"), context.Plugin.StartCommand)
                        .ConfigureAwait(true);
                    if (stage == "cleanup")
                    {
                        context.ExpectedDisposeFailure = failure;
                    }
                    if (stage == "start")
                    {
                        Assert.That(context.Plugin.Status,
                            Does.Contain("Start failed:").And.Contain(nameof(InvalidOperationException)));
                        Assert.That(context.Plugin.CanConfigure, Is.True);
                    }
                    else if (stage == "step")
                    {
                        await PressAsync(Find<Button>(context.View, "ContinuityStep"), context.Plugin.StepCommand)
                            .ConfigureAwait(true);
                        Assert.That(context.Plugin.Status,
                            Does.StartWith("Step failed:").And.Contain(nameof(InvalidOperationException)));
                        Assert.That(context.Plugin.CanStart, Is.False);
                    }
                    else
                    {
                        await PressAsync(Find<Button>(context.View, "ContinuityStop"), context.Plugin.StopCommand)
                            .ConfigureAwait(true);
                        Assert.That(context.Plugin.Status,
                            Does.StartWith("Cleanup could not be confirmed:")
                                .And.Contain(nameof(InvalidOperationException)));
                        Assert.That(context.Plugin.CanStart, Is.False);
                    }
                }
            });
        }

        private static void Select(WindowContext context, ContinuityScenario scenario)
        {
            Find<ComboBox>(context.View, "ContinuityScenario").SelectedItem =
                context.Plugin.Scenarios.Single(choice => choice.Scenario == scenario);
        }

        private static T Find<T>(Control root, string? automationId = null)
            where T : Control
        {
            return root.GetVisualDescendants().OfType<T>().Single(control =>
                automationId is null || AutomationProperties.GetAutomationId(control) == automationId);
        }

        private static void Press(Button button)
        {
            Assert.That(button.IsEffectivelyEnabled, Is.True);
            var key = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                Source = button
            };
            button.RaiseEvent(key);
            Assert.That(key.Handled, Is.True);
        }

        private static async Task PressAsync(Button button, IAsyncRelayCommand command)
        {
            Assert.That(button.Command, Is.SameAs(command));
            Press(button);
            await (command.ExecutionTask ??
                throw new AssertionException("The native button did not dispatch its command."))
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        }

        private sealed class WindowContext : IAsyncDisposable
        {
            public WindowContext()
            {
                Primary = Session.Object;
                Backend.Setup(value => value.CheckSetup(It.IsAny<ContinuityConfiguration>()))
                    .Returns(new ContinuitySetup(ContinuityAvailability.Supported, "Explicit owned fixture."));
                Backend.Setup(value => value.CaptureDiagnostics()).Returns(s_diagnostics);
                Backend.Setup(value => value.StartAsync(
                    It.IsAny<ContinuityConfiguration>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                Backend.Setup(value => value.StepAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ContinuityStepResult(false, "Owned transition observed."));
                Backend.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                Backend.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
                var factory = new Mock<IContinuityBackendFactory>(MockBehavior.Strict);
                factory.Setup(value => value.Create(It.IsAny<ContinuityTimeline>()))
                    .Callback((ContinuityTimeline timeline) => Timeline = timeline).Returns(Backend.Object);
                Plugin = new ContinuityPlugin(factory.Object, () => Primary, () => SelectedTarget);
                View = Plugin.View ?? throw new AssertionException("The Continuity view was not created.");
                Window = new Window { Width = 980, Height = 640, Content = View };
            }

            public Mock<IContinuityBackend> Backend { get; } = new(MockBehavior.Strict);

            public Mock<ISession> Session { get; } = new(MockBehavior.Strict);

            public ISession? Primary { get; set; }

            public string? SelectedTarget { get; set; }

            public ContinuityTimeline Timeline { get; private set; } = null!;

            public ContinuityPlugin Plugin { get; }

            public Control View { get; }

            public Window Window { get; }

            public Guid Correlation { get; } = Guid.NewGuid();

            public InvalidOperationException? ExpectedDisposeFailure { get; set; }

            public async Task ShowAsync()
            {
                await DesktopInteraction.OpenedAsync<Window>(() => Window.Show(DesktopInteraction.Owner))
                    .ConfigureAwait(true);
                Plugin.OnActivated();
            }

            public async ValueTask DisposeAsync()
            {
                Plugin.OnDeactivated();
                try
                {
                    if (ExpectedDisposeFailure is { } expected)
                    {
                        await Assert.ThatAsync(() => Plugin.DisposeAsync().AsTask(),
                            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(expected.Message))
                            .ConfigureAwait(true);
                    }
                    else
                    {
                        await Plugin.DisposeAsync().ConfigureAwait(true);
                    }
                }
                finally
                {
                    Window.Close();
                }
                Backend.Verify(value => value.DisposeAsync(), Times.Once);
            }

            private static readonly ArrayOf<DiagnosticMetric> s_diagnostics =
                [new("Owned revised settings", "250 ms / queue 23")];
        }
    }
}
