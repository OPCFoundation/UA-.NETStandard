/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the ConditionState class.
    /// </summary>
    [TestFixture]
    [Category("ConditionState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConditionStateTests
    {
        private static readonly TimeSpan s_activeStateTimeout = TimeSpan.FromSeconds(4);
        private ISystemContext m_context;
        private ITelemetryContext m_telemetry;

        /// <summary>
        /// Verifies async setters reject cancellation before mutating their state.
        /// </summary>
        [TestCase("enable")]
        [TestCase("severity")]
        [TestCase("acknowledged")]
        [TestCase("confirmed")]
        [TestCase("active")]
        [TestCase("shelving")]
        [TestCase("silence")]
        [TestCase("latched")]
        public async Task AsyncSettersRejectPreCanceledTokensBeforeMutationAsync(string setter)
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            condition.SetAcknowledgedState(m_context, false);
            condition.ConfirmedState = new TwoStateVariableState(condition);
            condition.ConfirmedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ConfirmedState), default, false);
            condition.SetConfirmedState(m_context, false);
            condition.SetSeverity(m_context, EventSeverity.Low);

            AlarmConditionState alarm = CreateAlarmWithNestedStates();
            alarm.ShelvingState = new ShelvedStateMachineState(alarm);
            alarm.ShelvingState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ShelvingState), default, false);
            alarm.ShelvingState.UnshelveTime =
                PropertyState<double>.With<VariantBuilder>(alarm.ShelvingState);
            alarm.SetShelvingState(m_context, false, false, 0);
            alarm.ClearChangeMasks(m_context, true);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Func<CancellationToken, ValueTask> operation = setter switch
            {
                "enable" => token => condition.SetEnableStateAsync(m_context, false, token),
                "severity" => token => condition.SetSeverityAsync(m_context, EventSeverity.High, token),
                "acknowledged" => token => condition.SetAcknowledgedStateAsync(m_context, true, token),
                "confirmed" => token => condition.SetConfirmedStateAsync(m_context, true, token),
                "active" => token => alarm.SetActiveStateAsync(m_context, true, token),
                "shelving" => token => alarm.SetShelvingStateAsync(m_context, true, false, 1000, token),
                "silence" => token => alarm.SetSilenceStateAsync(m_context, false, token),
                "latched" => token => alarm.SetLatchedStateAsync(m_context, true, token),
                _ => throw new ArgumentOutOfRangeException(nameof(setter))
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await operation(cancellation.Token).ConfigureAwait(false));

            switch (setter)
            {
                case "enable":
                    Assert.That(condition.EnabledState!.Id!.Value, Is.True);
                    break;
                case "severity":
                    Assert.That(condition.Severity!.Value, Is.EqualTo((ushort)EventSeverity.Low));
                    break;
                case "acknowledged":
                    Assert.That(condition.AckedState!.Id!.Value, Is.False);
                    break;
                case "confirmed":
                    Assert.That(condition.ConfirmedState!.Id!.Value, Is.False);
                    break;
                case "active":
                    Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
                    Assert.That(alarm.LatchedState!.Id!.Value, Is.False);
                    Assert.That(alarm.SilenceState!.Id!.Value, Is.True);
                    break;
                case "shelving":
                    Assert.That(
                        alarm.ShelvingState!.CurrentState!.Id!.Value,
                        Is.EqualTo(ObjectIds.ShelvedStateMachineType_Unshelved));
                    break;
                case "silence":
                    Assert.That(alarm.SilenceState!.Id!.Value, Is.True);
                    break;
                case "latched":
                    Assert.That(alarm.LatchedState!.Id!.Value, Is.False);
                    break;
            }
        }

        /// <summary>
        /// Verifies async enable updates commit state, await notification, and bypass the sync override.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task AsyncEnableStateSetterUsesAsyncOverrideAndAwaitsNotificationAsync()
        {
            var condition = new TestAsyncSetterConditionState();
            condition.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            await condition.ClearChangeMasksAsync(m_context, true).ConfigureAwait(false);

            var notificationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int notifications = 0;
            NodeStateChangedAsyncHandler handler = async (_, _, _, _) =>
            {
                notifications++;
                notificationEntered.TrySetResult(true);
                await releaseNotification.Task.ConfigureAwait(false);
            };
            condition.EnabledState!.Id!.StateChangedAsync += handler;
            Task<Task>? invocation = null;
            Task<Exception?>? updateObserver = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                invocation = Task.Factory.StartNew(
                    () => condition.SetEnableStateAsync(m_context, false).AsTask(),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                updateObserver = ObserveActiveStateUpdateAsync(invocation);
                Task invocationCompleted = await Task.WhenAny(invocation, Task.Delay(s_activeStateTimeout))
                    .ConfigureAwait(false);
                Assert.That(invocationCompleted, Is.SameAs(invocation));
                Task operation = await invocation.ConfigureAwait(false);
                Task notificationCompleted = await Task.WhenAny(
                    notificationEntered.Task,
                    Task.Delay(s_activeStateTimeout)).ConfigureAwait(false);
                Assert.That(notificationCompleted, Is.SameAs(notificationEntered.Task));
                Assert.That(operation.IsCompleted, Is.False);
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                releaseNotification.TrySetResult(true);
                condition.EnabledState!.Id!.StateChangedAsync -= handler;
                if (invocation != null)
                {
                    updateObserver ??= ObserveActiveStateUpdateAsync(invocation);
                    try
                    {
                        Task observerCompleted = await Task.WhenAny(
                            updateObserver,
                            Task.Delay(s_activeStateTimeout)).ConfigureAwait(false);
                        Assert.That(observerCompleted, Is.SameAs(updateObserver),
                            "The enable-state update observer did not finish after notification release.");
                        Exception? updateFailure = await updateObserver.ConfigureAwait(false);
                        if (updateFailure != null)
                        {
                            cleanupFailures.Add(updateFailure);
                        }
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
            }

            ThrowTestAndCleanupFailures(testFailure, cleanupFailures);

            Assert.That(condition.EnabledState!.Id!.Value, Is.False);
            Assert.That(condition.SyncEnableSetterCalls, Is.EqualTo(1));
            Assert.That(condition.AsyncEnableSetterCalls, Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the no-bool severity setter dispatches to its async override and awaits notification.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task AsyncSeveritySetterUsesAsyncOverrideAndAwaitsNotificationAsync()
        {
            var condition = new TestAsyncSetterConditionState();
            condition.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            condition.SetSeverity(m_context, EventSeverity.Low);
            await condition.ClearChangeMasksAsync(m_context, true).ConfigureAwait(false);

            var notificationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int notifications = 0;
            NodeStateChangedAsyncHandler handler = async (_, _, _, _) =>
            {
                notifications++;
                notificationEntered.TrySetResult(true);
                await releaseNotification.Task.ConfigureAwait(false);
            };
            condition.Severity!.StateChangedAsync += handler;
            Task<Task>? invocation = null;
            Task<Exception?>? updateObserver = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                invocation = Task.Factory.StartNew(
                    () => condition.SetSeverityAsync(m_context, EventSeverity.High).AsTask(),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                updateObserver = ObserveActiveStateUpdateAsync(invocation);
                Task invocationCompleted = await Task.WhenAny(invocation, Task.Delay(s_activeStateTimeout))
                    .ConfigureAwait(false);
                Assert.That(invocationCompleted, Is.SameAs(invocation));
                Task operation = await invocation.ConfigureAwait(false);
                Task notificationCompleted = await Task.WhenAny(
                    notificationEntered.Task,
                    Task.Delay(s_activeStateTimeout)).ConfigureAwait(false);
                Assert.That(notificationCompleted, Is.SameAs(notificationEntered.Task));
                Assert.That(operation.IsCompleted, Is.False);
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                releaseNotification.TrySetResult(true);
                condition.Severity!.StateChangedAsync -= handler;
                if (invocation != null)
                {
                    updateObserver ??= ObserveActiveStateUpdateAsync(invocation);
                    try
                    {
                        Task observerCompleted = await Task.WhenAny(
                            updateObserver,
                            Task.Delay(s_activeStateTimeout)).ConfigureAwait(false);
                        Assert.That(observerCompleted, Is.SameAs(updateObserver),
                            "The severity update observer did not finish after notification release.");
                        Exception? updateFailure = await updateObserver.ConfigureAwait(false);
                        if (updateFailure != null)
                        {
                            cleanupFailures.Add(updateFailure);
                        }
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
            }

            ThrowTestAndCleanupFailures(testFailure, cleanupFailures);

            Assert.That(condition.Severity!.Value, Is.EqualTo((ushort)EventSeverity.High));
            Assert.That(condition.SyncSeveritySetterCalls, Is.EqualTo(1));
            Assert.That(condition.AsyncSeveritySetterCalls, Is.EqualTo(1));
            Assert.That(condition.AsyncSeverityWithChangeMasksSetterCalls, Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the bool severity overload defers notification without dispatching through other overrides.
        /// </summary>
        [Test]
        public async Task AsyncSeveritySetterCanDeferNotificationAsync()
        {
            var condition = new TestAsyncSetterConditionState();
            condition.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            condition.SetSeverity(m_context, EventSeverity.Low);
            await condition.ClearChangeMasksAsync(m_context, true).ConfigureAwait(false);
            int notifications = 0;
            NodeStateChangedAsyncHandler handler = (_, _, _, _) =>
            {
                notifications++;
                return default;
            };
            condition.Severity!.StateChangedAsync += handler;

            Task<Task>? severityUpdateInvocation = null;
            Task<Exception?>? severityUpdateObserver = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                severityUpdateInvocation = Task.Factory.StartNew(
                    () => condition.SetSeverityAsync(
                        m_context,
                        EventSeverity.High,
                        clearChangeMasks: false).AsTask(),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                severityUpdateObserver = ObserveActiveStateUpdateAsync(severityUpdateInvocation);
                Task invocationCompleted = await Task.WhenAny(
                    severityUpdateInvocation,
                    Task.Delay(s_activeStateTimeout)).ConfigureAwait(false);
                Assert.That(invocationCompleted, Is.SameAs(severityUpdateInvocation));
                Task severityUpdate = await severityUpdateInvocation.ConfigureAwait(false);
                Task updateCompleted = await Task.WhenAny(
                    severityUpdate,
                    Task.Delay(s_activeStateTimeout)).ConfigureAwait(false);
                Assert.That(updateCompleted, Is.SameAs(severityUpdate));
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                if (severityUpdateInvocation != null)
                {
                    severityUpdateObserver ??= ObserveActiveStateUpdateAsync(severityUpdateInvocation);
                    try
                    {
                        Task observerCompleted = await Task.WhenAny(
                            severityUpdateObserver,
                            Task.Delay(s_activeStateTimeout)).ConfigureAwait(false);
                        Assert.That(observerCompleted, Is.SameAs(severityUpdateObserver),
                            "The severity update observer did not finish during cleanup.");
                        Exception? updateFailure = await severityUpdateObserver.ConfigureAwait(false);
                        if (updateFailure != null)
                        {
                            cleanupFailures.Add(updateFailure);
                        }
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
            }
            if (testFailure != null || cleanupFailures.Count != 0)
            {
                condition.Severity!.StateChangedAsync -= handler;
            }
            ThrowTestAndCleanupFailures(testFailure, cleanupFailures);

            Assert.That(condition.Severity!.Value, Is.EqualTo((ushort)EventSeverity.High));
            Assert.That(condition.Severity!.ChangeMasks, Is.Not.EqualTo(NodeStateChangeMasks.None));
            Assert.That(notifications, Is.Zero);
            Assert.That(condition.SyncSeveritySetterCalls, Is.EqualTo(1));
            Assert.That(condition.AsyncSeveritySetterCalls, Is.Zero);
            Assert.That(condition.AsyncSeverityWithChangeMasksSetterCalls, Is.EqualTo(1));

            await condition.ClearChangeMasksAsync(m_context, true).ConfigureAwait(false);
            condition.Severity!.StateChangedAsync -= handler;

            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(condition.Severity!.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Verifies cancellation during the first nested notification does not interrupt an active-state update.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task ActiveStateCompletesNestedUpdatesAfterCancellationDuringFirstNotificationAsync()
        {
            AlarmConditionState alarm = CreateAlarmWithNestedStates();

            var order = new List<string>();
            var notificationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            alarm.LatchedState!.Id!.StateChangedAsync += async (_, _, _, cancellationToken) =>
            {
                Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                order.Add("latched");
                notificationEntered.TrySetResult(true);
                await releaseNotification.Task.ConfigureAwait(false);
            };
            alarm.SilenceState!.Id!.StateChangedAsync += (_, _, _, _) =>
            {
                order.Add("silence");
                return default;
            };
            alarm.ActiveState!.Id!.StateChangedAsync += (_, _, _, _) =>
            {
                order.Add("active");
                return default;
            };

            using var cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            Task<Task>? invocation = null;
            Task<Exception?>? updateObserver = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                invocation = Task.Factory.StartNew(
                    () => alarm.SetActiveStateAsync(m_context, true, token).AsTask(),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                updateObserver = ObserveActiveStateUpdateAsync(invocation);
                Task invocationCompleted = await Task.WhenAny(invocation, Task.Delay(s_activeStateTimeout))
                    .ConfigureAwait(false);
                Assert.That(invocationCompleted, Is.SameAs(invocation));
                Task update = await invocation.ConfigureAwait(false);
                Task completed = await Task.WhenAny(notificationEntered.Task, Task.Delay(s_activeStateTimeout))
                    .ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(notificationEntered.Task));
                cancellation.Cancel();
                Assert.That(update.IsCompleted, Is.False);
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                releaseNotification.TrySetResult(true);
                if (invocation != null)
                {
                    updateObserver ??= ObserveActiveStateUpdateAsync(invocation);
                    try
                    {
                        Task observerCompleted = await Task.WhenAny(updateObserver, Task.Delay(s_activeStateTimeout))
                            .ConfigureAwait(false);
                        Assert.That(observerCompleted, Is.SameAs(updateObserver),
                            "The active-state update observer did not finish after notification release.");
                        Exception? updateFailure = await updateObserver.ConfigureAwait(false);
                        if (updateFailure != null)
                        {
                            cleanupFailures.Add(updateFailure);
                        }
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
            }

            ThrowTestAndCleanupFailures(testFailure, cleanupFailures);

            Assert.That(alarm.LatchedState.Id.Value, Is.True);
            Assert.That(alarm.SilenceState.Id.Value, Is.False);
            Assert.That(alarm.ActiveState.Id.Value, Is.True);
            Assert.That(order, Is.EqualTo(s_activationNotificationOrder));
        }

        /// <summary>
        /// Verifies cancellation during one-shot unshelving does not interrupt the active-state update.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task ActiveStateCompletesOneShotUnshelvingAfterCancellationDuringNotificationAsync()
        {
            AlarmConditionState alarm = CreateAlarmWithNestedStates();
            alarm.ShelvingState = new ShelvedStateMachineState(alarm);
            alarm.ShelvingState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ShelvingState), default, false);
            alarm.ShelvingState.UnshelveTime =
                PropertyState<double>.With<VariantBuilder>(alarm.ShelvingState);
            alarm.SetShelvingState(m_context, true, true, 1000);
            alarm.ClearChangeMasks(m_context, true);

            var notificationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var order = new List<string>();
            alarm.ShelvingState.CurrentState!.Id!.StateChangedAsync += async (_, _, _, cancellationToken) =>
            {
                Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                order.Add("shelving");
                notificationEntered.TrySetResult(true);
                await releaseNotification.Task.ConfigureAwait(false);
            };
            alarm.ActiveState!.Id!.StateChangedAsync += (_, _, _, _) =>
            {
                order.Add("active");
                return default;
            };

            using var cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            Task<Task>? invocation = null;
            Task<Exception?>? updateObserver = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                invocation = Task.Factory.StartNew(
                    () => alarm.SetActiveStateAsync(m_context, false, token).AsTask(),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                updateObserver = ObserveActiveStateUpdateAsync(invocation);
                Task invocationCompleted = await Task.WhenAny(invocation, Task.Delay(s_activeStateTimeout))
                    .ConfigureAwait(false);
                Assert.That(invocationCompleted, Is.SameAs(invocation));
                Task update = await invocation.ConfigureAwait(false);
                Task completed = await Task.WhenAny(notificationEntered.Task, Task.Delay(s_activeStateTimeout))
                    .ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(notificationEntered.Task));
                cancellation.Cancel();
                Assert.That(update.IsCompleted, Is.False);
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                releaseNotification.TrySetResult(true);
                if (invocation != null)
                {
                    updateObserver ??= ObserveActiveStateUpdateAsync(invocation);
                    try
                    {
                        Task observerCompleted = await Task.WhenAny(updateObserver, Task.Delay(s_activeStateTimeout))
                            .ConfigureAwait(false);
                        Assert.That(observerCompleted, Is.SameAs(updateObserver),
                            "The active-state update observer did not finish after notification release.");
                        Exception? updateFailure = await updateObserver.ConfigureAwait(false);
                        if (updateFailure != null)
                        {
                            cleanupFailures.Add(updateFailure);
                        }
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
            }

            ThrowTestAndCleanupFailures(testFailure, cleanupFailures);

            Assert.That(alarm.ShelvingState.CurrentState.Id.Value, Is.EqualTo(
                ObjectIds.ShelvedStateMachineType_Unshelved));
            Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
            Assert.That(order, Is.EqualTo(s_unshelvingNotificationOrder));
        }

        /// <summary>
        /// Observes both stages of an active-state update without leaving task failures unobserved.
        /// </summary>
        private static async Task<Exception?> ObserveActiveStateUpdateAsync(Task<Task> invocation)
        {
            try
            {
                Task update = await invocation.ConfigureAwait(false);
                await update.ConfigureAwait(false);
                return null;
            }
            catch (Exception failure)
            {
                return failure;
            }
        }

        /// <summary>
        /// Rethrows the test failure and any distinct failures encountered during cleanup.
        /// </summary>
        /// <param name="testFailure">The captured test failure, if any.</param>
        /// <param name="cleanupFailures">The failures encountered while releasing test operations.</param>
        private static void ThrowTestAndCleanupFailures(
            ExceptionDispatchInfo? testFailure,
            List<Exception> cleanupFailures)
        {
            if (testFailure != null)
            {
                if (cleanupFailures.Count == 0)
                {
                    testFailure.Throw();
                    return;
                }
                var failures = new List<Exception>(cleanupFailures.Count + 1)
                {
                    testFailure.SourceException
                };
                foreach (Exception cleanupFailure in cleanupFailures)
                {
                    if (!failures.Any(failure => ReferenceEquals(failure, cleanupFailure)))
                    {
                        failures.Add(cleanupFailure);
                    }
                }
                if (failures.Count == 1)
                {
                    testFailure.Throw();
                    return;
                }
                throw new AggregateException("The test and its cleanup both failed.", failures);
            }

            List<Exception> distinctFailures = cleanupFailures
                .Where((failure, index) => !cleanupFailures.Take(index)
                    .Any(previous => ReferenceEquals(previous, failure)))
                .ToList();
            if (distinctFailures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(distinctFailures[0]).Throw();
                return;
            }
            if (distinctFailures.Count > 1)
            {
                throw new AggregateException("Test cleanup failed.", distinctFailures);
            }
        }

        /// <summary>
        /// Verifies a failed async notification restores masks so a later clear can report the committed change.
        /// </summary>
        [Test]
        public async Task AsyncSetterRestoresChangeMasksWhenNotificationFailsAsync()
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            condition.SetAcknowledgedState(m_context, false);
            condition.ClearChangeMasks(m_context, true);
            condition.UpdateChangeMasks(NodeStateChangeMasks.Value);
            var failure = new InvalidOperationException("Notification failed.");
            NodeStateChangedAsyncHandler failureHandler = (_, _, _, _) =>
                new ValueTask(Task.FromException(failure));
            condition.AckedState!.Id!.StateChangedAsync += failureHandler;

            InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await condition.SetAcknowledgedStateAsync(m_context, true).ConfigureAwait(false));

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(condition.AckedState.Id.Value, Is.True);
            Assert.That(condition.AckedState.Id.ChangeMasks, Is.Not.EqualTo(NodeStateChangeMasks.None));
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.Value));

            condition.AckedState.Id.StateChangedAsync -= failureHandler;
            await condition.ClearChangeMasksAsync(m_context, true).ConfigureAwait(false);

            Assert.That(condition.AckedState.Id.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Verifies synchronous and asynchronous calls dispatch to their matching setter overrides.
        /// </summary>
        [Test]
        public async Task AcknowledgedStateCallsMatchingSyncOrAsyncOverrideAsync()
        {
            var condition = new TestAcknowledgeableConditionState();
            condition.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);

            condition.SetAcknowledgedState(m_context, true);

            Assert.That(condition.SyncSetterCalls, Is.EqualTo(1));
            Assert.That(condition.AsyncSetterCalls, Is.Zero);

            await condition.SetAcknowledgedStateAsync(m_context, false).ConfigureAwait(false);

            Assert.That(condition.SyncSetterCalls, Is.EqualTo(1));
            Assert.That(condition.AsyncSetterCalls, Is.EqualTo(1));
            Assert.That(condition.AckedState!.Id!.Value, Is.False);
        }

        /// <summary>
        /// Verifies async reporting bypasses synchronous overrides and keeps the current EventId resolvable.
        /// </summary>
        [Test]
        public async Task AsyncEventReportingUsesAsyncOverrideAndReachesSinkAsync()
        {
            var syncOnly = new TestSynchronousReportConditionState();
            syncOnly.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("SyncOnly"), default, true);
            int syncOnlySinkCalls = 0;
            syncOnly.EventReported += (_, _, _) => syncOnlySinkCalls++;
            ByteString syncOnlyEventId = syncOnly.EventId!.Value;

            var paired = new TestPairedReportConditionState();
            paired.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Paired"), default, true);
            int pairedSinkCalls = 0;
            paired.EventReported += (_, _, _) => pairedSinkCalls++;
            ByteString pairedEventId = paired.EventId!.Value;

            await syncOnly.ReportEventAsync(m_context, CreateEventSnapshot(syncOnly)).ConfigureAwait(false);
            await paired.ReportEventAsync(m_context, CreateEventSnapshot(paired)).ConfigureAwait(false);

            Assert.That(syncOnly.SyncReportCalls, Is.Zero);
            Assert.That(syncOnlySinkCalls, Is.EqualTo(1));
            Assert.That(syncOnly.GetEventByEventId(syncOnlyEventId), Is.SameAs(syncOnly));
            Assert.That(paired.SyncReportCalls, Is.Zero);
            Assert.That(paired.AsyncReportCalls, Is.EqualTo(1));
            Assert.That(pairedSinkCalls, Is.EqualTo(1));
            Assert.That(paired.GetEventByEventId(pairedEventId), Is.SameAs(paired));
        }

        /// <summary>
        /// Verifies synchronous reporting calls synchronous overrides and keeps the current EventId resolvable.
        /// </summary>
        [Test]
        public void SynchronousEventReportingUsesSyncOverrideAndReachesSink()
        {
            var syncOnly = new TestSynchronousReportConditionState();
            syncOnly.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("SyncOnly"), default, true);
            int syncOnlySinkCalls = 0;
            syncOnly.EventReported += (_, _, _) => syncOnlySinkCalls++;
            ByteString syncOnlyEventId = syncOnly.EventId!.Value;

            var paired = new TestPairedReportConditionState();
            paired.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Paired"), default, true);
            int pairedSinkCalls = 0;
            paired.EventReported += (_, _, _) => pairedSinkCalls++;
            ByteString pairedEventId = paired.EventId!.Value;

            syncOnly.ReportEvent(m_context, CreateEventSnapshot(syncOnly));
            paired.ReportEvent(m_context, CreateEventSnapshot(paired));

            Assert.That(syncOnly.SyncReportCalls, Is.EqualTo(1));
            Assert.That(syncOnlySinkCalls, Is.EqualTo(1));
            Assert.That(syncOnly.GetEventByEventId(syncOnlyEventId), Is.SameAs(syncOnly));
            Assert.That(paired.SyncReportCalls, Is.EqualTo(1));
            Assert.That(paired.AsyncReportCalls, Is.Zero);
            Assert.That(pairedSinkCalls, Is.EqualTo(1));
            Assert.That(paired.GetEventByEventId(pairedEventId), Is.SameAs(paired));
        }

        /// <summary>
        /// Verifies async reporting retains a superseded EventId for acknowledgement and rejects an unknown ID.
        /// </summary>
        [Test]
        public async Task AsyncEventReportingRetainsSupersededEventIdForAcknowledgeAsync()
        {
            var condition = new TestPairedReportConditionState();
            condition.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            condition.SetAcknowledgedState(m_context, false);
            condition.EventId!.Value = Uuid.NewUuid().ToByteString();
            ByteString reportedEventId = condition.EventId!.Value;

            await condition.ReportEventAsync(m_context, CreateEventSnapshot(condition)).ConfigureAwait(false);

            condition.EventId.Value = Uuid.NewUuid().ToByteString();
            Assert.That(condition.EventId.Value, Is.Not.EqualTo(reportedEventId));

            ServiceResult unknown = condition.CallAcknowledge(m_context, Uuid.NewUuid().ToByteString());
            Assert.That(unknown.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(condition.AckedState!.Id!.Value, Is.False);

            ServiceResult acknowledged = condition.CallAcknowledge(m_context, reportedEventId);
            Assert.That(ServiceResult.IsGood(acknowledged), Is.True, acknowledged.ToString());
            Assert.That(condition.AckedState.Id.Value, Is.True);
        }

        /// <summary>
        /// Characterizes that a pre-canceled async report retains its EventId before reporting is canceled.
        /// </summary>
        [Test]
        public async Task CanceledAsyncEventReportingRetainsUnpublishedEventIdAsync()
        {
            var condition = new TestPairedReportConditionState();
            condition.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            condition.SetAcknowledgedState(m_context, false);
            condition.EventId!.Value = Uuid.NewUuid().ToByteString();
            ByteString reportedEventId = condition.EventId!.Value;
            int sinkCalls = 0;
            condition.EventReported += (_, _, _) => sinkCalls++;

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await condition.ReportEventAsync(
                    m_context, CreateEventSnapshot(condition), cancellation.Token).ConfigureAwait(false));

            Assert.That(sinkCalls, Is.Zero);
            condition.EventId.Value = Uuid.NewUuid().ToByteString();
            ServiceResult acknowledged = condition.CallAcknowledge(m_context, reportedEventId);

            Assert.That(ServiceResult.IsGood(acknowledged), Is.True, acknowledged.ToString());
            Assert.That(condition.AckedState!.Id!.Value, Is.True);
        }

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(m_telemetry);
            // Add OPC UA namespace
            messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            m_context = new SystemContext(m_telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris
            };
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            (m_context as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Verifies deferred activation and deactivation keep exclusive limit and active states consistent.
        /// </summary>
        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        [TestCase(true, true, false)]
        public async Task ExclusiveLimitDeferredActiveStatePreservesLimitInvariantAsync(
            bool asynchronous, bool active, bool highLimit)
        {
            var alarm = new ExclusiveLimitAlarmState(null);
            alarm.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Exclusive"), default, true);
            if (highLimit)
            {
                alarm.HighLimit = PropertyState<double>.With<VariantBuilder>(alarm);
            }
            else
            {
                alarm.HighLimit = null;
            }
            alarm.SetEnableState(m_context, true);
            alarm.SetLimitState(m_context, active ? LimitAlarmStates.Inactive : LimitAlarmStates.High);
            alarm.Retain!.Value = false;
            alarm.ClearChangeMasks(m_context, true);
            int retentionCalls = 0;
            alarm.RetainStateProvider = _ =>
            {
                retentionCalls++;
                return true;
            };
            int activeNotifications = 0;
            int limitNotifications = 0;
            alarm.ActiveState!.Id!.StateChanged += (_, _, _) => activeNotifications++;
            alarm.LimitState!.CurrentState!.Id!.StateChanged += (_, _, _) => limitNotifications++;

            AlarmConditionState baseReference = alarm;
            if (asynchronous)
            {
                await baseReference.SetActiveStateAsync(
                    m_context, active, clearChangeMasks: false, updateRetainState: false).ConfigureAwait(false);
            }
            else
            {
                baseReference.SetActiveState(m_context, active, clearChangeMasks: false, updateRetainState: false);
            }

            NodeId expected = !active ? default :
                highLimit ? ObjectIds.ExclusiveLimitStateMachineType_High :
                ObjectIds.ExclusiveLimitStateMachineType_Low;
            Assert.That(alarm.LimitState.CurrentState.Id.Value, Is.EqualTo(expected));
            Assert.That(alarm.ActiveState.Id.Value, Is.EqualTo(active));
            Assert.That(alarm.Retain.Value, Is.False);
            Assert.That(retentionCalls, Is.Zero);
            Assert.That(activeNotifications, Is.Zero);
            Assert.That(limitNotifications, Is.Zero);
            Assert.That(alarm.ActiveState.Id.ChangeMasks, Is.Not.EqualTo(NodeStateChangeMasks.None));
            Assert.That(alarm.LimitState.CurrentState.Id.ChangeMasks, Is.Not.EqualTo(NodeStateChangeMasks.None));

            await alarm.ClearChangeMasksAsync(m_context, true).ConfigureAwait(false);

            Assert.That(activeNotifications, Is.EqualTo(1));
            Assert.That(limitNotifications, Is.EqualTo(1));
            Assert.That(alarm.ActiveState.Id.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(alarm.LimitState.CurrentState.Id.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(retentionCalls, Is.Zero);
        }

        /// <summary>
        /// Verifies notification and retention options independently apply to exclusive limit updates.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task ExclusiveLimitActiveStateHonorsNotificationAndRetentionOptionsAsync(
            bool asynchronous, bool clearChangeMasks, bool updateRetainState)
        {
            var alarm = new ExclusiveLimitAlarmState(null);
            alarm.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Exclusive"), default, true);
            alarm.SetEnableState(m_context, true);
            alarm.SetLimitState(m_context, LimitAlarmStates.High);
            alarm.Retain!.Value = false;
            alarm.ClearChangeMasks(m_context, true);
            int retentionCalls = 0;
            alarm.RetainStateProvider = _ =>
            {
                retentionCalls++;
                return true;
            };
            int notifications = 0;
            alarm.LimitState!.CurrentState!.Id!.StateChanged += (_, _, _) => notifications++;

            if (asynchronous)
            {
                await alarm.SetActiveStateAsync(m_context, false, clearChangeMasks, updateRetainState)
                    .ConfigureAwait(false);
            }
            else
            {
                alarm.SetActiveState(m_context, false, clearChangeMasks, updateRetainState);
            }

            Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
            Assert.That(alarm.LimitState.CurrentState.Id.Value.IsNull, Is.True);
            Assert.That(retentionCalls, Is.EqualTo(updateRetainState ? 1 : 0));
            Assert.That(alarm.Retain.Value, Is.EqualTo(updateRetainState));
            Assert.That(notifications, Is.EqualTo(clearChangeMasks ? 1 : 0));
            Assert.That(alarm.LimitState.CurrentState.Id.ChangeMasks == NodeStateChangeMasks.None,
                Is.EqualTo(clearChangeMasks));
        }

        /// <summary>
        /// Verifies deferred activation preserves an already selected exclusive limit.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExclusiveLimitDeferredActivationPreservesSelectedLimitAsync(bool asynchronous)
        {
            var alarm = new ExclusiveLimitAlarmState(null);
            alarm.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Exclusive"), default, true);
            alarm.SetLimitState(m_context, LimitAlarmStates.HighHigh);

            if (asynchronous)
            {
                await alarm.SetActiveStateAsync(
                    m_context, true, clearChangeMasks: false, updateRetainState: false).ConfigureAwait(false);
            }
            else
            {
                alarm.SetActiveState(m_context, true, clearChangeMasks: false, updateRetainState: false);
            }

            Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
            Assert.That(alarm.LimitState!.CurrentState!.Id!.Value,
                Is.EqualTo(ObjectIds.ExclusiveLimitStateMachineType_HighHigh));
        }

        /// <summary>
        /// Verifies cancellation prevents both exclusive limit and active-state mutation.
        /// </summary>
        [Test]
        public async Task ExclusiveLimitDeferredUpdateRejectsCancellationBeforeMutationAsync()
        {
            var alarm = new ExclusiveLimitAlarmState(null);
            alarm.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Exclusive"), default, true);
            alarm.SetLimitState(m_context, LimitAlarmStates.High);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await alarm.SetActiveStateAsync(
                    m_context, false, clearChangeMasks: false, updateRetainState: false, cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
            Assert.That(alarm.LimitState!.CurrentState!.Id!.Value,
                Is.EqualTo(ObjectIds.ExclusiveLimitStateMachineType_High));
        }

        /// <summary>
        /// Verifies rejected deferred one-shot unshelving does not partially change the limit state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExclusiveLimitDeferredUnshelvingRejectsBeforeLimitMutationAsync(bool asynchronous)
        {
            var alarm = new ExclusiveLimitAlarmState(null);
            alarm.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Exclusive"), default, true);
            alarm.SetLimitState(m_context, LimitAlarmStates.High);
            alarm.ShelvingState = new ShelvedStateMachineState(alarm);
            alarm.ShelvingState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ShelvingState), default, false);
            alarm.ShelvingState.UnshelveTime =
                PropertyState<double>.With<VariantBuilder>(alarm.ShelvingState);
            alarm.MaxTimeShelved = null;
            alarm.SetShelvingState(m_context, true, true, 0);

            if (asynchronous)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await alarm.SetActiveStateAsync(
                        m_context, false, clearChangeMasks: false, updateRetainState: false).ConfigureAwait(false));
            }
            else
            {
                Assert.That(() => alarm.SetActiveState(
                    m_context, false, clearChangeMasks: false, updateRetainState: false),
                    Throws.TypeOf<InvalidOperationException>());
            }

            Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
            Assert.That(alarm.LimitState!.CurrentState!.Id!.Value,
                Is.EqualTo(ObjectIds.ExclusiveLimitStateMachineType_High));
            Assert.That(alarm.ShelvingState.CurrentState!.Id!.Value,
                Is.EqualTo(ObjectIds.ShelvedStateMachineType_OneShotShelved));
        }

        /// <summary>
        /// Verifies option-enabled overloads preserve existing active and limit override dispatch.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExclusiveLimitOptionOverloadsPreserveDefaultOverrideDispatchAsync(bool asynchronous)
        {
            var alarm = new TestExclusiveLimitAlarmState();
            alarm.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Exclusive"), default, true);
            AlarmConditionState baseReference = alarm;

            if (asynchronous)
            {
                await baseReference.SetActiveStateAsync(
                    m_context, false, clearChangeMasks: true, updateRetainState: true).ConfigureAwait(false);
            }
            else
            {
                baseReference.SetActiveState(m_context, false, clearChangeMasks: true, updateRetainState: true);
            }

            Assert.That(alarm.SyncActiveCalls, Is.EqualTo(asynchronous ? 0 : 1));
            Assert.That(alarm.AsyncActiveCalls, Is.EqualTo(asynchronous ? 1 : 0));
            Assert.That(alarm.SyncLimitCalls, Is.EqualTo(asynchronous ? 0 : 1));
            Assert.That(alarm.AsyncLimitCalls, Is.EqualTo(asynchronous ? 1 : 0));
            Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
            Assert.That(alarm.LimitState!.CurrentState!.Id!.Value.IsNull, Is.True);
        }

        /// <summary>
        /// Counts existing exclusive-limit setter overrides.
        /// </summary>
        private sealed class TestExclusiveLimitAlarmState : ExclusiveLimitAlarmState
        {
            /// <summary>
            /// Creates the test alarm.
            /// </summary>
            public TestExclusiveLimitAlarmState()
                : base(null)
            {
            }

            /// <summary>
            /// Gets the synchronous active setter call count.
            /// </summary>
            public int SyncActiveCalls { get; private set; }

            /// <summary>
            /// Gets the asynchronous active setter call count.
            /// </summary>
            public int AsyncActiveCalls { get; private set; }

            /// <summary>
            /// Gets the synchronous limit setter call count.
            /// </summary>
            public int SyncLimitCalls { get; private set; }

            /// <summary>
            /// Gets the asynchronous limit setter call count.
            /// </summary>
            public int AsyncLimitCalls { get; private set; }

            /// <inheritdoc/>
            public override void SetActiveState(ISystemContext context, bool active)
            {
                SyncActiveCalls++;
                base.SetActiveState(context, active);
            }

            /// <inheritdoc/>
            public override ValueTask SetActiveStateAsync(
                ISystemContext context, bool active, CancellationToken cancellationToken = default)
            {
                AsyncActiveCalls++;
                return base.SetActiveStateAsync(context, active, cancellationToken);
            }

            /// <inheritdoc/>
            public override void SetLimitState(ISystemContext context, LimitAlarmStates limit)
            {
                SyncLimitCalls++;
                base.SetLimitState(context, limit);
            }

            /// <inheritdoc/>
            public override ValueTask SetLimitStateAsync(
                ISystemContext context, LimitAlarmStates limit, CancellationToken cancellationToken = default)
            {
                AsyncLimitCalls++;
                return base.SetLimitStateAsync(context, limit, cancellationToken);
            }
        }

        /// <summary>
        /// Creates a snapshot used to report the specified condition.
        /// </summary>
        private InstanceStateSnapshot CreateEventSnapshot(ConditionState condition)
        {
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(m_context, condition);
            return snapshot;
        }

        /// <summary>
        /// Counts calls to synchronous and asynchronous enable and severity setter overloads.
        /// </summary>
        private sealed class TestAsyncSetterConditionState : AcknowledgeableConditionState
        {
            /// <summary>
            /// Creates the test condition.
            /// </summary>
            public TestAsyncSetterConditionState()
                : base(null)
            {
            }

            /// <summary>
            /// Gets the synchronous enable setter call count.
            /// </summary>
            public int SyncEnableSetterCalls { get; private set; }

            /// <summary>
            /// Gets the asynchronous enable setter call count.
            /// </summary>
            public int AsyncEnableSetterCalls { get; private set; }

            /// <summary>
            /// Gets the synchronous severity setter call count.
            /// </summary>
            public int SyncSeveritySetterCalls { get; private set; }

            /// <summary>
            /// Gets the asynchronous severity setter call count.
            /// </summary>
            public int AsyncSeveritySetterCalls { get; private set; }

            /// <summary>
            /// Gets the asynchronous severity setter call count for the bool overload.
            /// </summary>
            public int AsyncSeverityWithChangeMasksSetterCalls { get; private set; }

            /// <inheritdoc/>
            public override void SetEnableState(ISystemContext context, bool enabled)
            {
                SyncEnableSetterCalls++;
                base.SetEnableState(context, enabled);
            }

            /// <inheritdoc/>
            public override async ValueTask SetEnableStateAsync(
                ISystemContext context,
                bool enabled,
                CancellationToken cancellationToken = default)
            {
                AsyncEnableSetterCalls++;
                await base.SetEnableStateAsync(context, enabled, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public override void SetSeverity(ISystemContext context, EventSeverity severity)
            {
                SyncSeveritySetterCalls++;
                base.SetSeverity(context, severity);
            }

            /// <inheritdoc/>
            public override async ValueTask SetSeverityAsync(
                ISystemContext context,
                EventSeverity severity,
                CancellationToken cancellationToken = default)
            {
                AsyncSeveritySetterCalls++;
                await base.SetSeverityAsync(context, severity, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public override async ValueTask SetSeverityAsync(
                ISystemContext context,
                EventSeverity severity,
                bool clearChangeMasks,
                CancellationToken cancellationToken = default)
            {
                AsyncSeverityWithChangeMasksSetterCalls++;
                await base.SetSeverityAsync(context, severity, clearChangeMasks, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates an alarm with latched and silence state variables for async setter tests.
        /// </summary>
        private AlarmConditionState CreateAlarmWithNestedStates()
        {
            var alarm = new AlarmConditionState(m_telemetry, null!);
            alarm.Create(m_context, new NodeId(Guid.NewGuid()), QualifiedName.From("Alarm"), default, true);
            alarm.SetEnableState(m_context, true);
            alarm.LatchedState = new TwoStateVariableState(alarm);
            alarm.LatchedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.LatchedState), default, true);
            alarm.SilenceState = new TwoStateVariableState(alarm);
            alarm.SilenceState.Create(
                m_context, default, QualifiedName.From(BrowseNames.SilenceState), default, true);
            alarm.SetSilenceState(m_context, true);
            alarm.ClearChangeMasks(m_context, true);
            return alarm;
        }

        /// <summary>
        /// Counts calls to the synchronous and asynchronous acknowledgement setter overrides.
        /// </summary>
        private sealed class TestAcknowledgeableConditionState : AcknowledgeableConditionState
        {
            /// <summary>
            /// Creates the test condition.
            /// </summary>
            public TestAcknowledgeableConditionState()
                : base(null)
            {
            }

            /// <summary>
            /// Gets the synchronous acknowledgement setter call count.
            /// </summary>
            public int SyncSetterCalls { get; private set; }

            /// <summary>
            /// Gets the asynchronous acknowledgement setter call count.
            /// </summary>
            public int AsyncSetterCalls { get; private set; }

            /// <inheritdoc/>
            public override void SetAcknowledgedState(ISystemContext context, bool acknowledged)
            {
                SyncSetterCalls++;
                base.SetAcknowledgedState(context, acknowledged);
            }

            /// <inheritdoc/>
            public override async ValueTask SetAcknowledgedStateAsync(
                ISystemContext context,
                bool acknowledged,
                CancellationToken cancellationToken = default)
            {
                AsyncSetterCalls++;
                await base.SetAcknowledgedStateAsync(context, acknowledged, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Counts calls to the synchronous event reporting override.
        /// </summary>
        private sealed class TestSynchronousReportConditionState : AcknowledgeableConditionState
        {
            /// <summary>
            /// Creates the test condition.
            /// </summary>
            public TestSynchronousReportConditionState()
                : base(null)
            {
            }

            /// <summary>
            /// Gets the synchronous report override call count.
            /// </summary>
            public int SyncReportCalls { get; private set; }

            /// <inheritdoc/>
            public override void ReportEvent(ISystemContext context, IFilterTarget e)
            {
                SyncReportCalls++;
                base.ReportEvent(context, e);
            }
        }

        /// <summary>
        /// Counts calls to paired synchronous and asynchronous event reporting overrides.
        /// </summary>
        private sealed class TestPairedReportConditionState : AcknowledgeableConditionState
        {
            /// <summary>
            /// Creates the test condition.
            /// </summary>
            public TestPairedReportConditionState()
                : base(null)
            {
            }

            /// <summary>
            /// Gets the synchronous report override call count.
            /// </summary>
            public int SyncReportCalls { get; private set; }

            /// <summary>
            /// Gets the asynchronous report override call count.
            /// </summary>
            public int AsyncReportCalls { get; private set; }

            /// <summary>
            /// Invokes the acknowledge method for the supplied event identifier.
            /// </summary>
            /// <param name="context">The system context.</param>
            /// <param name="eventId">The event identifier.</param>
            /// <returns>The acknowledge method result.</returns>
            public ServiceResult CallAcknowledge(ISystemContext context, ByteString eventId)
            {
                return OnAcknowledgeCalled(context, Acknowledge!, NodeId, eventId, LocalizedText.From("ack"));
            }

            /// <inheritdoc/>
            public override void ReportEvent(ISystemContext context, IFilterTarget e)
            {
                SyncReportCalls++;
                base.ReportEvent(context, e);
            }

            /// <inheritdoc/>
            public override async ValueTask ReportEventAsync(
                ISystemContext context,
                IFilterTarget e,
                CancellationToken cancellationToken = default)
            {
                AsyncReportCalls++;
                await base.ReportEventAsync(context, e, cancellationToken).ConfigureAwait(false);
            }
        }

        [TestCase(true, true, true, true)]
        [TestCase(false, true, true, false)]
        [TestCase(false, false, true, true)]
        [TestCase(true, true, false, false)]
        public async Task AdditionalRetentionPreservesStandardAndDisabledRulesAsync(
            bool additionalRetain, bool acknowledged, bool enabled, bool expectedRetain)
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_context, new NodeId(1u), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, !enabled);
            condition.SetAcknowledgedState(m_context, acknowledged);
            int evaluated = 0;
            condition.RetainStateProvider = current =>
            {
                Assert.That(current, Is.SameAs(condition));
                evaluated++;
                return additionalRetain;
            };
            MethodState method = enabled ? condition.Enable! : condition.Disable!;
            ServiceResult result = await method.CallAsync(m_context, condition.NodeId, [], [], [])
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(condition.Retain!.Value, Is.EqualTo(expectedRetain));
            Assert.That(evaluated, enabled ? Is.GreaterThan(0) : Is.Zero);
        }

        [Test]
        public void BranchPreservesAdditionalRetentionProvider()
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_context, new NodeId(1u), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            condition.RetainStateProvider = current => !current.BranchId!.Value.IsNull;
            ConditionState? branch = condition.CreateBranch(m_context, new NodeId(2u));
            Assert.That(branch, Is.Not.Null);
            Assert.That(branch!.RetainStateProvider, Is.SameAs(condition.RetainStateProvider));
            Assert.That(branch.RetainStateProvider!(branch), Is.True);
            Assert.That(condition.RetainStateProvider(condition), Is.False);
        }

        /// <summary>
        /// Retains attribute changes for one later publication without evaluating an external retention provider.
        /// </summary>
        [TestCase("active", true)]
        [TestCase("active", false)]
        [TestCase("acknowledged", true)]
        [TestCase("acknowledged", false)]
        [TestCase("latched", true)]
        [TestCase("latched", false)]
        [TestCase("silenced", true)]
        [TestCase("silenced", false)]
        [TestCase("severity", true)]
        public void DeferredConditionAttributesPublishOnlyWhenRequested(string attribute, bool value)
        {
            var alarm = new AlarmConditionState(m_telemetry, null!);
            alarm.Create(m_context, new NodeId(1u), QualifiedName.From("Alarm"), default, true);
            alarm.SetEnableState(m_context, true);
            alarm.LatchedState = new TwoStateVariableState(alarm);
            alarm.LatchedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.LatchedState), default, true);
            alarm.SilenceState = new TwoStateVariableState(alarm);
            alarm.SilenceState.Create(
                m_context, default, QualifiedName.From(BrowseNames.SilenceState), default, true);
            switch (attribute)
            {
                case "active":
                    alarm.SetActiveState(m_context, !value);
                    break;
                case "acknowledged":
                    alarm.SetAcknowledgedState(m_context, !value);
                    break;
                case "latched":
                    alarm.SetLatchedState(m_context, !value);
                    break;
                case "silenced":
                    alarm.SetSilenceState(m_context, !value);
                    break;
            }
            if (attribute == "active" && value)
            {
                alarm.SetLatchedState(m_context, false);
                alarm.SetSilenceState(m_context, true);
            }
            alarm.Retain!.Value = false;
            alarm.ClearChangeMasks(m_context, true);
            int retentionCalls = 0;
            alarm.RetainStateProvider = _ =>
            {
                retentionCalls++;
                return true;
            };
            int notifications = 0;
            int nestedNotifications = 0;
            if (attribute == "active" && value)
            {
                alarm.LatchedState.Id!.StateChanged += (_, _, _) => nestedNotifications++;
                alarm.SilenceState.Id!.StateChanged += (_, _, _) => nestedNotifications++;
            }
            BaseVariableState observed = attribute switch
            {
                "active" => alarm.ActiveState!.Id!,
                "acknowledged" => alarm.AckedState!.Id!,
                "latched" => alarm.LatchedState.Id!,
                "silenced" => alarm.SilenceState.Id!,
                _ => alarm.Severity!
            };
            observed.StateChanged += (_, _, _) => notifications++;
            switch (attribute)
            {
                case "active":
                    alarm.SetActiveState(
                        m_context, value, clearChangeMasks: false, updateRetainState: false);
                    break;
                case "acknowledged":
                    alarm.SetAcknowledgedState(
                        m_context, value, clearChangeMasks: false, updateRetainState: false);
                    break;
                case "latched":
                    alarm.SetLatchedState(m_context, value, clearChangeMasks: false);
                    break;
                case "silenced":
                    alarm.SetSilenceState(m_context, value, clearChangeMasks: false);
                    break;
                default:
                    alarm.SetSeverity(m_context, EventSeverity.High, clearChangeMasks: false);
                    break;
            }
            Assert.That(retentionCalls, Is.Zero);
            Assert.That(alarm.Retain.Value, Is.False);
            Assert.That(notifications, Is.Zero);
            Assert.That(nestedNotifications, Is.Zero);
            Assert.That(observed.ChangeMasks, Is.Not.EqualTo(NodeStateChangeMasks.None));
            if (attribute == "severity")
            {
                Assert.That(observed.Value.TryGetValue(out ushort severity), Is.True);
                Assert.That(severity, Is.EqualTo((ushort)EventSeverity.High));
            }
            else
            {
                Assert.That(observed.Value.TryGetValue(out bool state), Is.True);
                Assert.That(state, Is.EqualTo(value));
            }
            alarm.ClearChangeMasks(m_context, true);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(observed.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(retentionCalls, Is.Zero);
            if (attribute == "active" && value)
            {
                Assert.That(alarm.LatchedState.Id!.Value, Is.True);
                Assert.That(alarm.SilenceState.Id!.Value, Is.False);
                Assert.That(nestedNotifications, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// Preserves context-free attribute updates and forwards the notification context unchanged.
        /// </summary>
        [TestCase("active")]
        [TestCase("acknowledged")]
        [TestCase("latched")]
        [TestCase("silenced")]
        [TestCase("severity")]
        public void ConditionSettersPreserveContextFreeNotifications(string attribute)
        {
            var alarm = new AlarmConditionState(m_telemetry, null!);
            alarm.Create(m_context, new NodeId(1u), QualifiedName.From("Alarm"), default, true);
            alarm.SetEnableState(m_context, true);
            alarm.SetAcknowledgedState(m_context, false);
            alarm.LatchedState = new TwoStateVariableState(alarm);
            alarm.LatchedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.LatchedState), default, true);
            alarm.SilenceState = new TwoStateVariableState(alarm);
            alarm.SilenceState.Create(
                m_context, default, QualifiedName.From(BrowseNames.SilenceState), default, true);
            alarm.ClearChangeMasks(m_context, true);
            BaseVariableState observed = attribute switch
            {
                "active" => alarm.ActiveState!.Id!,
                "acknowledged" => alarm.AckedState!.Id!,
                "latched" => alarm.LatchedState.Id!,
                "silenced" => alarm.SilenceState.Id!,
                _ => alarm.Severity!
            };
            int notifications = 0;
            observed.StateChanged += (context, _, _) =>
            {
                Assert.That(context, Is.Null);
                notifications++;
            };
            switch (attribute)
            {
                case "active":
                    alarm.SetActiveState(null!, true);
                    break;
                case "acknowledged":
                    alarm.SetAcknowledgedState(null!, true);
                    break;
                case "latched":
                    alarm.SetLatchedState(null!, true);
                    break;
                case "silenced":
                    alarm.SetSilenceState(null!, true);
                    break;
                default:
                    alarm.SetSeverity(null!, EventSeverity.High);
                    break;
            }
            if (attribute == "severity")
            {
                Assert.That(observed.Value.TryGetValue(out ushort severity), Is.True);
                Assert.That(severity, Is.EqualTo((ushort)EventSeverity.High));
            }
            else
            {
                Assert.That(observed.Value.TryGetValue(out bool state), Is.True);
                Assert.That(state, Is.True);
            }
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(observed.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Retention can still be derived while state-change notifications are deferred.
        /// </summary>
        [Test]
        public void DeferredAcknowledgementCanEvaluateRetentionExplicitly()
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_context, new NodeId(1u), QualifiedName.From("Condition"), default, true);
            condition.SetEnableState(m_context, true);
            condition.SetAcknowledgedState(m_context, false);
            int retentionCalls = 0;
            condition.RetainStateProvider = _ =>
            {
                retentionCalls++;
                return true;
            };
            int notifications = 0;
            condition.AckedState!.Id!.StateChanged += (_, _, _) => notifications++;
            condition.SetAcknowledgedState(
                m_context, true, clearChangeMasks: false, updateRetainState: true);
            Assert.That(retentionCalls, Is.EqualTo(1));
            Assert.That(condition.Retain!.Value, Is.True);
            Assert.That(condition.AckedState.Id.Value, Is.True);
            Assert.That(notifications, Is.Zero);
            condition.ClearChangeMasks(m_context, true);
            Assert.That(notifications, Is.EqualTo(1));
        }

        /// <summary>
        /// Test that SetEnableState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetEnableStateUpdatesTimestampAndClearsChangeMasks()
        {
            var condition = new ConditionState(null);
            condition.Create(m_context, new NodeId(1), QualifiedName.From("Condition"), default, true);

            // Set initial state
            DateTimeUtc beforeTime = DateTimeUtc.Now;
            condition.SetEnableState(m_context, true);
            DateTimeUtc afterTime = DateTimeUtc.Now;

            // Verify timestamp is updated
            Assert.That(condition.EnabledState!.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.EnabledState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared (all should be None)
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(condition.EnabledState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetSeverity updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetSeverityUpdatesTimestampAndClearsChangeMasks()
        {
            var condition = new ConditionState(null);
            condition.Create(m_context, new NodeId(1), QualifiedName.From("Condition"), default, true);

            DateTimeUtc beforeTime = DateTimeUtc.Now;
            condition.SetSeverity(m_context, EventSeverity.High);
            DateTimeUtc afterTime = DateTimeUtc.Now;

            // Verify timestamps are updated
            Assert.That(condition.Severity!.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.Severity.Timestamp, Is.LessThanOrEqualTo(afterTime));
            Assert.That(condition.LastSeverity!.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.LastSeverity.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetActiveState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetActiveStateUpdatesTimestampAndClearsChangeMasks()
        {
            var alarm = new AlarmConditionState(m_telemetry, null!);
            alarm.Create(m_context, new NodeId(1), QualifiedName.From("Alarm"), default, true);

            DateTimeUtc beforeTime = DateTimeUtc.Now;
            alarm.SetActiveState(m_context, true);
            DateTimeUtc afterTime = DateTimeUtc.Now;

            // Verify timestamp is updated
            Assert.That(alarm.ActiveState!.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(alarm.ActiveState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(alarm.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(alarm.ActiveState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetSuppressedState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetSuppressedStateUpdatesTimestampAndClearsChangeMasks()
        {
            var alarm = new AlarmConditionState(m_telemetry, null!);
            alarm.Create(m_context, new NodeId(1), QualifiedName.From("Alarm"), default, true);
            alarm.SuppressedState = new TwoStateVariableState(alarm);
            alarm.SuppressedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.SuppressedState), default, false);

            DateTimeUtc beforeTime = DateTimeUtc.Now;
            alarm.SetSuppressedState(m_context, true);
            DateTimeUtc afterTime = DateTimeUtc.Now;

            // Verify timestamp is updated
            Assert.That(alarm.SuppressedState.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(alarm.SuppressedState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(alarm.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(alarm.SuppressedState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetAcknowledgedState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetAcknowledgedStateUpdatesTimestampAndClearsChangeMasks()
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_context, new NodeId(1), QualifiedName.From("AckCondition"), default, true);

            DateTimeUtc beforeTime = DateTimeUtc.Now;
            condition.SetAcknowledgedState(m_context, true);
            DateTimeUtc afterTime = DateTimeUtc.Now;

            // Verify timestamp is updated
            Assert.That(condition.AckedState!.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.AckedState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(condition.AckedState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetConfirmedState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetConfirmedStateUpdatesTimestampAndClearsChangeMasks()
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_context, new NodeId(1), QualifiedName.From("AckCondition"), default, true);
            condition.ConfirmedState = new TwoStateVariableState(condition);
            condition.ConfirmedState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ConfirmedState), default, false);

            DateTimeUtc beforeTime = DateTimeUtc.Now;
            condition.SetConfirmedState(m_context, true);
            DateTimeUtc afterTime = DateTimeUtc.Now;

            // Verify timestamp is updated
            Assert.That(condition.ConfirmedState.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.ConfirmedState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(condition.ConfirmedState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetShelvingState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetShelvingStateUpdatesTimestampAndClearsChangeMasks()
        {
            var alarm = new AlarmConditionState(m_telemetry, null!);
            alarm.Create(m_context, new NodeId(1), QualifiedName.From("Alarm"), default, true);
            alarm.ShelvingState = new ShelvedStateMachineState(alarm);
            alarm.ShelvingState.Create(
                m_context, default, QualifiedName.From(BrowseNames.ShelvingState), default, false);
            alarm.ShelvingState.UnshelveTime = PropertyState<double>.With<VariantBuilder>(alarm.ShelvingState);

            DateTimeUtc beforeTime = DateTimeUtc.Now;
            alarm.SetShelvingState(m_context, true, false, 1000);
            DateTimeUtc afterTime = DateTimeUtc.Now;

            // Verify timestamp is updated on UnshelveTime
            Assert.That(alarm.ShelvingState.UnshelveTime.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(alarm.ShelvingState.UnshelveTime.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(alarm.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that subscribed clients are notified when SetActiveState is called.
        /// This simulates the issue scenario.
        /// </summary>
        [Test]
        public void SetActiveStateNotifiesSubscribers()
        {
            var alarm = new AlarmConditionState(m_telemetry, null!);
            alarm.Create(m_context, new NodeId(1), QualifiedName.From("Alarm"), default, true);

            // Initially inactive
            alarm.SetActiveState(m_context, false);

            // Now activate the alarm - timestamp should be greater than or equal to the initial timestamp
            // since both could be set to DateTimeUtc.Now which has limited precision
            DateTimeUtc beforeActivation = DateTimeUtc.Now;
            alarm.SetActiveState(m_context, true);
            DateTimeUtc afterActivation = DateTimeUtc.Now;

            // Verify that the timestamp is within the expected range
            Assert.That(alarm.ActiveState!.Timestamp, Is.GreaterThanOrEqualTo(beforeActivation));
            Assert.That(alarm.ActiveState.Timestamp, Is.LessThanOrEqualTo(afterActivation));

            // Verify that change masks were cleared (indicating subscribers were notified)
            Assert.That(alarm.ActiveState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that UpdateStateAfterEnable calls EvaluateRetainStateOnEnable
        /// and that the default implementation sets Retain based on GetRetainState.
        /// </summary>
        [Test]
        public void UpdateStateAfterEnableCallsEvaluateRetainStateOnEnable()
        {
            // Arrange
            var condition = new ConditionState(null);
            condition.Create(m_context, default, QualifiedName.From("TestCondition"), default, true);

            // Initially disabled
            condition.SetEnableState(m_context, false);
            Assert.That(condition.EnabledState!.Id!.Value, Is.False);
            Assert.That(condition.Retain!.Value, Is.False);

            // Act - Enable the condition
            condition.SetEnableState(m_context, true);

            // Assert - Enabled state should be true
            Assert.That(condition.EnabledState.Id.Value, Is.True);

            // The default implementation should call UpdateRetainState which uses GetRetainState
            // For base ConditionState with no branches, GetRetainState returns false when enabled
            Assert.That(condition.Retain.Value, Is.False);
        }

        /// <summary>
        /// Test that UpdateStateAfterDisable sets Retain to false as per specification.
        /// </summary>
        [Test]
        public void UpdateStateAfterDisableSetsRetainToFalse()
        {
            // Arrange
            var condition = new TestConditionStateWithRetain(null!);
            condition.Create(m_context, default, QualifiedName.From("TestCondition"), default, true);

            // Enable the condition and set retain to true
            condition.SetEnableState(m_context, true);
            condition.ForceRetain(true);
            Assert.That(condition.Retain!.Value, Is.True);

            // Act - Disable the condition
            condition.SetEnableState(m_context, false);

            // Assert - Retain should be false per specification
            Assert.That(condition.EnabledState!.Id!.Value, Is.False);
            Assert.That(condition.Retain.Value, Is.False);
        }

        /// <summary>
        /// Test that a derived class can override EvaluateRetainStateOnEnable
        /// to provide custom logic for determining the Retain value.
        /// </summary>
        [Test]
        public void DerivedClassCanOverrideEvaluateRetainStateOnEnable()
        {
            // Arrange
            var condition = new TestConditionStateWithCustomRetain(null!);
            condition.Create(m_context, default, QualifiedName.From("TestCondition"), default, true);

            // Initially disabled
            condition.SetEnableState(m_context, false);
            Assert.That(condition.Retain!.Value, Is.False);

            // Act - Enable the condition
            condition.SetEnableState(m_context, true);

            // Assert - The custom implementation should have been called
            Assert.That(condition.EnabledState!.Id!.Value, Is.True);
            Assert.That(condition.EvaluateCalled, Is.True);
            Assert.That(condition.Retain.Value, Is.True);  // Custom logic always sets to true
        }

        /// <summary>
        /// Test that the branch collection can be safely created, enumerated and
        /// cleared from multiple threads concurrently without throwing.
        /// Reproduces the race that previously crashed the process when the
        /// branch dictionary was enumerated while being modified.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public void ConcurrentBranchAccessIsThreadSafe()
        {
            var condition = new ConditionState(null);
            condition.Create(m_context, new NodeId(1), QualifiedName.From("Condition"), default, true);
            // Ensure each created branch receives a unique EventId.
            condition.AutoReportStateChanges = true;
            condition.SetEnableState(m_context, true);

            const int iterations = 2000;
            using var cts = new System.Threading.CancellationTokenSource();

            // Writer: continuously creates and clears branches.
            var writer = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    condition.CreateBranch(m_context, new NodeId((uint)(i + 2)));
                    if (i % 10 == 0)
                    {
                        condition.ClearBranches();
                    }
                }
            });

            // Reader: continuously enumerates and counts branches.
            var reader = System.Threading.Tasks.Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    foreach (System.Collections.Generic.KeyValuePair<string, ConditionState> branch
                        in condition.GetBranches())
                    {
                        Assert.That(branch.Value, Is.Not.Null);
                    }

                    _ = condition.GetBranchCount();
                }
            });

            Assert.That(() =>
            {
                try
                {
                    writer.GetAwaiter().GetResult();
                }
                finally
                {
                    cts.Cancel();
                }

                reader.GetAwaiter().GetResult();
            }, Throws.Nothing);
        }

        /// <summary>
        /// Test condition that exposes method to force Retain value for testing.
        /// </summary>
        private sealed class TestConditionStateWithRetain : ConditionState
        {
            public TestConditionStateWithRetain(NodeState parent)
                : base(parent)
            {
            }

            public void ForceRetain(bool value)
            {
                Retain!.Value = value;
            }
        }

        /// <summary>
        /// Test condition that overrides EvaluateRetainStateOnEnable.
        /// </summary>
        private sealed class TestConditionStateWithCustomRetain : ConditionState
        {
            public bool EvaluateCalled { get; private set; }

            public TestConditionStateWithCustomRetain(NodeState parent)
                : base(parent)
            {
            }

            protected override void EvaluateRetainStateOnEnable(ISystemContext context)
            {
                EvaluateCalled = true;
                // Custom logic: always set Retain to true when enabled
                Retain!.Value = true;
            }
        }

        /// <summary>The expected notification order when an alarm activates.</summary>
        private static readonly string[] s_activationNotificationOrder = ["latched", "silence", "active"];

        /// <summary>The expected notification order when a one-shot alarm becomes inactive.</summary>
        private static readonly string[] s_unshelvingNotificationOrder = ["shelving", "active"];
    }
}
