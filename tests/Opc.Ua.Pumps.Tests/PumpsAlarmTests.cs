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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Builders;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Verifies pump alarm mappings, measurement boundaries and the inherited standard condition lifecycle.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [NonParallelizable]
    public sealed class PumpsAlarmTests
    {
        /// <summary>
        /// Starts the bounded shared address-space owner.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new PumpsServerFixture();
            await m_fixture.StartAsync();
        }

        /// <summary>
        /// Creates independent inputs and conditions for each case.
        /// </summary>
        [SetUp]
        public async Task CreatePumpAsync()
        {
            m_pump = await m_fixture.Manager.CreatePumpAsync(m_fixture.PumpName($"AlarmPump{++m_sequence}"));
        }

        /// <summary>
        /// Releases the owner and its registered alarm resources.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync();
        }

        /// <summary>
        /// Every explicit NE107 category creates its concrete DI alarm and required source references.
        /// </summary>
        [TestCase(DeviceHealthEnumeration.FAILURE, typeof(FailureAlarmState))]
        [TestCase(DeviceHealthEnumeration.CHECK_FUNCTION, typeof(CheckFunctionAlarmState))]
        [TestCase(DeviceHealthEnumeration.OFF_SPEC, typeof(OffSpecAlarmState))]
        [TestCase(DeviceHealthEnumeration.MAINTENANCE_REQUIRED, typeof(MaintenanceRequiredAlarmState))]
        public async Task ExplicitDiagnosticMappingCreatesConcreteDiTypeAsync(
            DeviceHealthEnumeration category,
            Type expected)
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "Diagnostic", category);
            AlarmConditionState alarm = binding.Alarm;
            NodeState type = m_fixture.Manager.FindPredefinedNode(
                ((BaseInstanceState)group.Node).TypeDefinitionId)!;

            Assert.Multiple(() =>
            {
                Assert.That(alarm, Is.TypeOf(expected));
                Assert.That(alarm.EventType!.Value, Is.EqualTo(alarm.TypeDefinitionId));
                Assert.That(alarm.InputNode!.Value, Is.EqualTo(input.NodeId));
                Assert.That(alarm.SourceNode!.Value, Is.EqualTo(input.NodeId));
                Assert.That(input.ReferenceExists(ReferenceTypeIds.HasCondition, false, alarm.NodeId), Is.True);
                Assert.That(
                    input.ReferenceExists(ReferenceTypeIds.GeneratesEvent, false, alarm.TypeDefinitionId), Is.True);
                Assert.That(
                    type.ReferenceExists(ReferenceTypeIds.GeneratesEvent, false, alarm.TypeDefinitionId), Is.True);
                Assert.That(m_fixture.Manager.FindPredefinedNode(alarm.NodeId), Is.SameAs(alarm));
                Assert.That(alarm.Enable!.OnCallMethod, Is.Not.Null);
                Assert.That(alarm.Disable!.OnCallMethod, Is.Not.Null);
                Assert.That(alarm.Acknowledge!.OnCall, Is.Not.Null);
            });
        }

        /// <summary>
        /// Activation, acknowledgement and enablement preserve the inherited retained-condition behavior.
        /// </summary>
        [Test]
        public async Task DiagnosticUsesActualInputAndStandardAcknowledgeDisableEnableRefreshAsync()
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "Failure", DeviceHealthEnumeration.FAILURE);
            AlarmConditionState alarm = binding.Alarm;
            await binding.EvaluateAsync();
            Assert.That(alarm.Retain!.Value, Is.False);

            group.SetDiscrete(BrowseNames.BearingFault, true);
            await binding.EvaluateAsync();
            ByteString activeEvent = alarm.EventId!.Value;
            await binding.EvaluateAsync();
            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
                Assert.That(alarm.AckedState!.Id!.Value, Is.False);
                Assert.That(alarm.Retain.Value, Is.True);
                Assert.That(alarm.EventId.Value, Is.EqualTo(activeEvent), "unchanged inputs do not emit duplicates");
            });
            var events = new List<IFilterTarget>();
            alarm.ConditionRefresh(m_fixture.Manager.SystemContext, events, true);
            Assert.That(events, Has.Count.EqualTo(1));

            group.SetDiscrete(BrowseNames.BearingFault, false);
            await binding.EvaluateAsync();
            Assert.That(alarm.Retain.Value, Is.True, "inactive but unacknowledged conditions remain retained");
            ServiceResult ack = alarm.Acknowledge!.OnCall!(
                m_fixture.Manager.SystemContext, alarm.Acknowledge, alarm.NodeId,
                alarm.EventId.Value, new LocalizedText("Acknowledged"));
            TwoStateVariableState ackedState = alarm.AckedState
                ?? throw new AssertionException("The condition has no AckedState.");
            PropertyState<bool> ackedId = ackedState.Id
                ?? throw new AssertionException("The AckedState has no Id property.");
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(ack), Is.True);
                Assert.That(ackedId.Value, Is.True);
                Assert.That(alarm.Retain.Value, Is.False);
            });
            events.Clear();
            alarm.ConditionRefresh(m_fixture.Manager.SystemContext, events, true);
            Assert.That(events, Is.Empty);

            var errors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult disabled = await alarm.Disable!.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [], errors, output);
            group.SetDiscrete(BrowseNames.BearingFault, true);
            ByteString disabledEvent = alarm.EventId.Value;
            await binding.EvaluateAsync();
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(disabled), Is.True);
                Assert.That(alarm.EnabledState!.Id!.Value, Is.False);
                Assert.That(alarm.Retain.Value, Is.False);
                Assert.That(alarm.EventId.Value, Is.EqualTo(disabledEvent));
            });
            ServiceResult enabled = await alarm.Enable!.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [], errors, output);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(enabled), Is.True);
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
                Assert.That(alarm.Retain.Value, Is.True);
            });
        }

        /// <summary>
        /// Awaits asynchronous event reporting without synchronously blocking the evaluation caller.
        /// </summary>
        [Test]
        public async Task EvaluationAwaitsGatedEventReportAsync()
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "GatedReport", DeviceHealthEnumeration.FAILURE);
            await binding.EvaluateAsync();

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocationReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            binding.Alarm.OnReportEventAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };
            group.SetDiscrete(BrowseNames.BearingFault, true);

            Task evaluation = Task.Factory.StartNew(
                () =>
                {
                    ValueTask pending = binding.EvaluateAsync();
                    invocationReturned.TrySetResult(true);
                    return pending.AsTask();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
            try
            {
                Task readiness = Task.WhenAll(entered.Task, invocationReturned.Task);
                Task completed = await Task.WhenAny(readiness, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(completed, Is.SameAs(readiness),
                    "the event sink must enter and evaluation must return while it is gated");
                Assert.That(release.Task.IsCompleted, Is.False, "the event sink must still be gated");
                Assert.That(evaluation.IsCompleted, Is.False, "evaluation must remain pending while the sink is gated");
            }
            finally
            {
                release.TrySetResult(true);
                Task drained = await Task.WhenAny(evaluation, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(drained, Is.SameAs(evaluation), "evaluation must finish after the sink is released");
            }

            await evaluation;
        }

        /// <summary>
        /// Awaits state notifications under the evaluation gate and retains synchronous sink ordering.
        /// </summary>
        [Test]
        public async Task EvaluationAwaitsGatedStateNotificationAfterSynchronousSinkAsync()
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "GatedStateNotification", DeviceHealthEnumeration.FAILURE);
            await binding.EvaluateAsync();

            var notifications = new List<string>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocationReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            binding.Alarm.OnStateChanged = (_, _, _) => notifications.Add("sync");
            binding.Alarm.OnStateChangedAsync = async (_, _, _, _) =>
            {
                notifications.Add("async");
                if (notifications.Count == 2)
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                }
            };
            binding.Alarm.UpdateChangeMasks(NodeStateChangeMasks.Value);
            group.SetDiscrete(BrowseNames.BearingFault, true);

            Task evaluation = Task.Factory.StartNew(
                () =>
                {
                    ValueTask pending = binding.EvaluateAsync();
                    invocationReturned.TrySetResult(true);
                    return pending.AsTask();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
            try
            {
                Task readiness = Task.WhenAll(entered.Task, invocationReturned.Task);
                Task completed = await Task.WhenAny(readiness, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(completed, Is.SameAs(readiness),
                    "the synchronous sink and gated async sink must run before EvaluateAsync returns");
                Assert.That(notifications, Has.Count.EqualTo(2));
                Assert.That(notifications[0], Is.EqualTo("sync"));
                Assert.That(notifications[1], Is.EqualTo("async"));
                Assert.That(release.Task.IsCompleted, Is.False, "the state sink must still be gated");
                Assert.That(evaluation.IsCompleted, Is.False, "evaluation must await the state sink");
            }
            finally
            {
                release.TrySetResult(true);
                Task drained = await Task.WhenAny(evaluation, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(drained, Is.SameAs(evaluation), "evaluation must finish after the sink is released");
            }

            await evaluation;
        }

        /// <summary>
        /// Propagates a state-sink failure before reporting an event or applying later alarm setters.
        /// </summary>
        [Test]
        public async Task EvaluationPropagatesStateNotificationFailureBeforeEventReportAsync()
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "FailedStateNotification", DeviceHealthEnumeration.FAILURE);
            await binding.EvaluateAsync();

            var failure = new InvalidOperationException("state notification failed");
            bool reported = false;
            binding.Alarm.OnStateChangedAsync = (_, _, _, _) => throw failure;
            binding.Alarm.UpdateChangeMasks(NodeStateChangeMasks.Value);
            binding.Alarm.OnReportEventAsync = (_, _, _, _) =>
            {
                reported = true;
                return default;
            };
            group.SetDiscrete(BrowseNames.BearingFault, true);

            try
            {
                InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await binding.EvaluateAsync());

                Assert.Multiple(() =>
                {
                    Assert.That(observed, Is.SameAs(failure));
                    Assert.That(binding.Alarm.Severity!.Value, Is.EqualTo((ushort)EventSeverity.High));
                    Assert.That(binding.Alarm.ActiveState!.Id!.Value, Is.False);
                    Assert.That(reported, Is.False);
                });
            }
            finally
            {
                binding.Alarm.OnStateChangedAsync = null;
                binding.Alarm.OnReportEventAsync = null;
            }
            await binding.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await binding.EvaluateAsync());
        }

        /// <summary>
        /// A failed notification leaves live mutations visible but does not suppress the next input evaluation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EvaluationRetriesIncompleteStateAfterNotificationFailureAsync(bool returnToNormal)
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "RetryState", DeviceHealthEnumeration.FAILURE);
            await binding.EvaluateAsync();
            var failure = new InvalidOperationException("notification failed once");
            int notifications = 0;
            int reports = 0;
            binding.Alarm.UpdateChangeMasks(NodeStateChangeMasks.Value);
            binding.Alarm.OnStateChangedAsync = (_, _, _, _) =>
            {
                if (++notifications == 1)
                {
                    throw failure;
                }
                return default;
            };
            binding.Alarm.OnReportEventAsync = (_, _, _, _) =>
            {
                reports++;
                return default;
            };
            group.SetDiscrete(BrowseNames.BearingFault, true);
            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await binding.EvaluateAsync());
            Assert.That(observed, Is.SameAs(failure));
            Assert.That(binding.Alarm.Severity!.Value, Is.EqualTo((ushort)EventSeverity.High));
            Assert.That(binding.Alarm.ActiveState!.Id!.Value, Is.False);
            Assert.That(reports, Is.Zero);
            if (returnToNormal)
            {
                group.SetDiscrete(BrowseNames.BearingFault, false);
            }
            await binding.EvaluateAsync();
            Assert.That(binding.Alarm.ActiveState!.Id!.Value, Is.EqualTo(!returnToNormal));
            Assert.That(binding.Alarm.Severity!.Value,
                Is.EqualTo((ushort)(returnToNormal ? EventSeverity.Min : EventSeverity.High)));
            if (!returnToNormal)
            {
                Assert.That(binding.Alarm.AckedState!.Id!.Value, Is.False);
            }
            Assert.That(notifications, Is.GreaterThanOrEqualTo(2));
            Assert.That(reports, Is.EqualTo(1));
            await binding.EvaluateAsync();
            Assert.That(reports, Is.EqualTo(1), "only a completed update may suppress duplicates");
        }

        /// <summary>
        /// Failed or canceled delivery retries the same completed snapshot without rebuilding live condition state.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EvaluationRetriesPendingReportWithoutChangingEventIdAsync(bool canceled)
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "RetryReport", DeviceHealthEnumeration.FAILURE);
            await binding.EvaluateAsync();
            int attempts = 0;
            IFilterTarget? firstSnapshot = null;
            binding.Alarm.OnReportEventAsync = (_, _, snapshot, _) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    firstSnapshot = snapshot;
                    if (canceled)
                    {
                        throw new OperationCanceledException();
                    }
                    throw new InvalidOperationException("report failed once");
                }
                Assert.That(snapshot, Is.SameAs(firstSnapshot));
                return default;
            };
            group.SetDiscrete(BrowseNames.BearingFault, true);
            if (canceled)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await binding.EvaluateAsync());
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await binding.EvaluateAsync());
            }
            ByteString eventId = binding.Alarm.EventId!.Value;
            Assert.That(binding.Alarm.ActiveState!.Id!.Value, Is.True);
            await binding.EvaluateAsync();
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(binding.Alarm.EventId.Value, Is.EqualTo(eventId));
            await binding.EvaluateAsync();
            Assert.That(attempts, Is.EqualTo(2));
        }

        /// <summary>
        /// A later condition update remains queued behind a failed report without holding the mutation gate.
        /// </summary>
        [Test]
        public async Task EvaluationRetainsReportOrderAcrossConcurrentUpdateAndFailureAsync()
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "OrderedRetry", DeviceHealthEnumeration.FAILURE);
            await binding.EvaluateAsync();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IFilterTarget? failedSnapshot = null;
            binding.Alarm.OnReportEventAsync = async (_, _, snapshot, _) =>
            {
                failedSnapshot = snapshot;
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                throw new InvalidOperationException("first delivery failed");
            };
            group.SetDiscrete(BrowseNames.BearingFault, true);
            Task firstEvaluation = binding.EvaluateAsync().AsTask();
            try
            {
                Task ready = await Task.WhenAny(entered.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(ready, Is.SameAs(entered.Task));
                group.SetDiscrete(BrowseNames.BearingFault, false);
                Task laterEvaluation = binding.EvaluateAsync().AsTask();
                Task completed = await Task.WhenAny(laterEvaluation, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(completed, Is.SameAs(laterEvaluation),
                    "event delivery must not hold the mutation gate");
                await laterEvaluation;
                Assert.That(binding.Alarm.ActiveState!.Id!.Value, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                Task drained = await Task.WhenAny(firstEvaluation, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(drained, Is.SameAs(firstEvaluation));
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await firstEvaluation);
            }
            var reports = new List<IFilterTarget>();
            binding.Alarm.OnReportEventAsync = (_, _, snapshot, _) =>
            {
                reports.Add(snapshot);
                return default;
            };
            await binding.EvaluateAsync();
            Assert.That(reports, Has.Count.EqualTo(2));
            Assert.That(reports[0], Is.SameAs(failedSnapshot));
            Assert.That(reports[1], Is.Not.SameAs(failedSnapshot));
            await binding.EvaluateAsync();
            Assert.That(reports, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Disposal drains notifications, queued evaluations and event reports before releasing the gate.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposalDrainsAdmittedEvaluationsAndRejectsNewEvaluationsAsync(bool gateEventReport)
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "DisposalDrain", DeviceHealthEnumeration.FAILURE);
            await binding.EvaluateAsync();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (gateEventReport)
            {
                binding.Alarm.OnReportEventAsync = async (_, _, _, _) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                };
            }
            else
            {
                binding.Alarm.UpdateChangeMasks(NodeStateChangeMasks.Value);
                binding.Alarm.OnStateChangedAsync = async (_, _, _, _) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                };
            }
            group.SetDiscrete(BrowseNames.BearingFault, true);
            Task evaluation = binding.EvaluateAsync().AsTask();
            Task queued = binding.EvaluateAsync().AsTask();
            Task? disposal = null;
            try
            {
                Task ready = await Task.WhenAny(entered.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(ready, Is.SameAs(entered.Task));
                disposal = binding.DisposeAsync().AsTask();
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(binding.DisposeAsync().AsTask(), Is.SameAs(disposal));
                await Assert.ThrowsAsync<ObjectDisposedException>(async () => await binding.EvaluateAsync());
                Assert.That(evaluation.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                Task drain = Task.WhenAll(evaluation, queued, disposal ?? binding.DisposeAsync().AsTask());
                Task finished = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.That(finished, Is.SameAs(drain), "all admitted evaluations and disposal must finish");
                await drain;
            }
            await binding.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await binding.EvaluateAsync());
        }

        /// <summary>
        /// The owning node manager releases alarm bindings without requiring sample or client cleanup.
        /// </summary>
        [Test]
        public async Task NodeManagerDisposalReleasesAlarmBindingAsync()
        {
            await using var fixture = new PumpsServerFixture();
            await fixture.StartAsync();
            IPumpBuilder pump = await fixture.Manager.CreatePumpAsync(fixture.PumpName("DisposalOwner"));
            IPumpGroupBuilder group = pump.Supervision(BrowseNames.SupervisionMechanics);
            group.SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "OwnedBinding", DeviceHealthEnumeration.FAILURE);
            await fixture.Manager.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await binding.EvaluateAsync());
            await binding.DisposeAsync();
        }

        /// <summary>
        /// Uses the shared suppression/shelving methods without replacing severity or source time.
        /// </summary>
        [Test]
        public async Task ConfiguredSeveritySuppressionAndOneShotShelvingUseStandardLifecycleAsync()
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics)
                .SetDiscrete(BrowseNames.BearingFault, true);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            DateTimeUtc sourceTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            input.Timestamp = sourceTime;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "ShelvableFailure", DeviceHealthEnumeration.FAILURE,
                new PumpAlarmOptions
                {
                    ActiveSeverity = EventSeverity.Medium,
                    Suppression = true,
                    Shelving = true,
                    MaximumShelvingTime = TimeSpan.FromMinutes(5)
                });
            await binding.EvaluateAsync();
            AlarmConditionState alarm = binding.Alarm;
            Assert.That(alarm.Severity!.Value, Is.EqualTo((ushort)EventSeverity.Medium));
            Assert.That(alarm.Time!.Value, Is.EqualTo(sourceTime));
            Assert.That(alarm.MaxTimeShelved!.Value, Is.EqualTo(300_000));
            var errors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult suppressed = await alarm.Suppress!.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [], errors, output);
            Assert.That(ServiceResult.IsGood(suppressed), Is.True, suppressed.ToString());
            Assert.That(alarm.SuppressedState!.Id!.Value, Is.True);
            ServiceResult unsuppressed = await alarm.Unsuppress!.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [], errors, output);
            Assert.That(ServiceResult.IsGood(unsuppressed), Is.True, unsuppressed.ToString());
            Assert.That(alarm.SuppressedState.Id.Value, Is.False);
            ServiceResult shelved = await alarm.ShelvingState!.OneShotShelve!.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [], errors, output);
            Assert.That(ServiceResult.IsGood(shelved), Is.True, shelved.ToString());
            Assert.That(alarm.ShelvingState.CurrentState!.Id!.Value,
                Is.EqualTo(Opc.Ua.ObjectIds.ShelvedStateMachineType_OneShotShelved));
            group.SetDiscrete(BrowseNames.BearingFault, false);
            await binding.EvaluateAsync();
            Assert.That(alarm.ShelvingState.CurrentState.Id.Value,
                Is.EqualTo(Opc.Ua.ObjectIds.ShelvedStateMachineType_Unshelved));
            group.SetDiscrete(BrowseNames.BearingFault, true);
            await binding.EvaluateAsync();
            ServiceResult excessiveDuration = await alarm.ShelvingState.TimedShelve!.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [Variant.From(300_001d)], errors, output);
            Assert.That(excessiveDuration.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadShelvingTimeOutOfRange));
            ServiceResult timed = await alarm.ShelvingState.TimedShelve.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [Variant.From(60_000d)], errors, output);
            Assert.That(ServiceResult.IsGood(timed), Is.True, timed.ToString());
            Assert.That(alarm.ShelvingState.CurrentState.Id.Value,
                Is.EqualTo(Opc.Ua.ObjectIds.ShelvedStateMachineType_TimedShelved));
            ServiceResult unshelved = await alarm.ShelvingState.Unshelve!.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [], errors, output);
            Assert.That(ServiceResult.IsGood(unshelved), Is.True, unshelved.ToString());
            Assert.That(alarm.ShelvingState.CurrentState.Id.Value,
                Is.EqualTo(Opc.Ua.ObjectIds.ShelvedStateMachineType_Unshelved));
            Assert.That(alarm.Severity.Value, Is.EqualTo((ushort)EventSeverity.Medium));
        }

        /// <summary>
        /// The timer callback releases shelving without clearing a separately applied suppression.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task TimedUnshelveCallbackRestoresStandardStateAndPreservesSuppressionAsync(bool suppressed)
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics)
                .SetDiscrete(BrowseNames.BearingFault, true);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddDiagnosticAlarmAsync(
                group, input, "TimedFailure", DeviceHealthEnumeration.FAILURE,
                new PumpAlarmOptions
                {
                    Suppression = true,
                    Shelving = true,
                    MaximumShelvingTime = TimeSpan.FromMinutes(5)
                });
            await binding.EvaluateAsync();
            AlarmConditionState alarm = binding.Alarm;
            var errors = new List<ServiceResult>();
            var output = new List<Variant>();
            if (suppressed)
            {
                ServiceResult suppression = await alarm.Suppress!.CallAsync(
                    m_fixture.Manager.SystemContext, alarm.NodeId, [], errors, output);
                Assert.That(ServiceResult.IsGood(suppression), Is.True, suppression.ToString());
            }
            ServiceResult shelving = await alarm.ShelvingState!.TimedShelve!.CallAsync(
                m_fixture.Manager.SystemContext, alarm.NodeId, [Variant.From(60_000d)], errors, output);
            Assert.That(ServiceResult.IsGood(shelving), Is.True, shelving.ToString());
            Assert.That(alarm.ShelvingState.CurrentState!.Id!.Value,
                Is.EqualTo(Opc.Ua.ObjectIds.ShelvedStateMachineType_TimedShelved));
            Assert.That(alarm.OnTimedUnshelve, Is.Not.Null);
            ServiceResult expiration = alarm.OnTimedUnshelve(m_fixture.Manager.SystemContext, alarm);
            Assert.That(ServiceResult.IsGood(expiration), Is.True, expiration.ToString());
            Assert.Multiple(() =>
            {
                Assert.That(alarm.ShelvingState.CurrentState.Id.Value,
                    Is.EqualTo(Opc.Ua.ObjectIds.ShelvedStateMachineType_Unshelved));
                Assert.That(alarm.ShelvingState.UnshelveTime!.Value, Is.Zero);
                Assert.That(alarm.SuppressedState!.Id!.Value, Is.EqualTo(suppressed));
                Assert.That(alarm.SuppressedOrShelved!.Value, Is.EqualTo(suppressed));
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
                Assert.That(alarm.AckedState!.Id!.Value, Is.False);
            });
        }

        /// <summary>
        /// Values immediately around and on each threshold select the exclusive limit state.
        /// </summary>
        [TestCase(-1.0, LimitAlarmStates.LowLow)]
        [TestCase(0.0, LimitAlarmStates.Low)]
        [TestCase(0.01, LimitAlarmStates.Low)]
        [TestCase(9.99, LimitAlarmStates.Low)]
        [TestCase(10.0, LimitAlarmStates.Inactive)]
        [TestCase(10.01, LimitAlarmStates.Inactive)]
        [TestCase(50.0, LimitAlarmStates.Inactive)]
        [TestCase(89.99, LimitAlarmStates.Inactive)]
        [TestCase(90.0, LimitAlarmStates.Inactive)]
        [TestCase(90.01, LimitAlarmStates.High)]
        [TestCase(99.99, LimitAlarmStates.High)]
        [TestCase(100.0, LimitAlarmStates.High)]
        [TestCase(100.01, LimitAlarmStates.HighHigh)]
        public async Task MeasurementSelectsExactlyOneExclusiveLimitStateAsync(double value, LimitAlarmStates expected)
        {
            IPumpGroupBuilder group = m_pump.Measurements.SetAnalog(BrowseNames.MassFlow, value);
            var input = (BaseVariableState)group.Child(BrowseNames.MassFlow)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddMeasurementAlarmAsync(
                group, input, "Limits", new PumpMeasurementLimits { LowLow = 0, Low = 10, High = 90, HighHigh = 100 });
            await binding.EvaluateAsync();
            var alarm = (ExclusiveLimitAlarmState)binding.Alarm;
            NodeId expectedState = expected switch
            {
                LimitAlarmStates.LowLow => new NodeId(Opc.Ua.Objects.ExclusiveLimitStateMachineType_LowLow),
                LimitAlarmStates.Low => new NodeId(Opc.Ua.Objects.ExclusiveLimitStateMachineType_Low),
                LimitAlarmStates.High => new NodeId(Opc.Ua.Objects.ExclusiveLimitStateMachineType_High),
                LimitAlarmStates.HighHigh => new NodeId(Opc.Ua.Objects.ExclusiveLimitStateMachineType_HighHigh),
                _ => NodeId.Null
            };
            Assert.Multiple(() =>
            {
                Assert.That(alarm.LimitState!.CurrentState!.Id!.Value, Is.EqualTo(expectedState));
                Assert.That(alarm.ActiveState!.Id!.Value, Is.EqualTo(expected != LimitAlarmStates.Inactive));
                Assert.That(alarm.InputNode!.Value, Is.EqualTo(input.NodeId));
                Assert.That(alarm.HighLimit!.Value, Is.EqualTo(90));
                Assert.That(alarm.LowLowLimit!.Value, Is.Zero);
            });
            foreach (uint identifier in new[]
            {
                ObjectTypes.MeasurementsType,
                ObjectTypes.DriveMeasurementsType,
                ObjectTypes.InletConnectionMeasurementsType,
                ObjectTypes.OutletConnectionMeasurementsType
            })
            {
                NodeState type = m_fixture.Manager.FindPredefinedNode(
                    PumpsModel.TypeNodeId(identifier, m_fixture.Manager.SystemContext.NamespaceUris))!;
                Assert.That(type.ReferenceExists(ReferenceTypeIds.GeneratesEvent, false, alarm.TypeDefinitionId),
                    Is.True);
            }
        }

        /// <summary>
        /// Invalid quality preserves the last active state until a usable measurement arrives.
        /// </summary>
        [Test]
        public async Task OmittedLimitsAndBadReadingsDoNotFabricateNormalStateAsync()
        {
            IPumpGroupBuilder group = m_pump.Measurements.SetAnalog(BrowseNames.MassFlow, 20);
            var input = (BaseVariableState)group.Child(BrowseNames.MassFlow)!;
            PumpAlarmBinding binding = await m_fixture.Manager.Runtime.AddMeasurementAlarmAsync(
                group, input, "HighOnly", new PumpMeasurementLimits { High = 10 });
            await binding.EvaluateAsync();
            group.SetAnalog(BrowseNames.MassFlow, double.NaN);
            await binding.EvaluateAsync();
            Assert.Multiple(() =>
            {
                Assert.That(binding.Alarm.Quality!.Value, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
                Assert.That(binding.Alarm.ActiveState!.Id!.Value, Is.True);
                Assert.That(((ExclusiveLimitAlarmState)binding.Alarm).LowLimit, Is.Null);
            });
            group.SetAnalog(BrowseNames.MassFlow, 1);
            input.StatusCode = StatusCodes.BadNoCommunication;
            await binding.EvaluateAsync();
            Assert.Multiple(() =>
            {
                Assert.That(binding.Alarm.Quality!.Value, Is.EqualTo((StatusCode)StatusCodes.BadNoCommunication));
                Assert.That(binding.Alarm.ActiveState!.Id!.Value, Is.True);
            });
            input.StatusCode = StatusCodes.Good;
            await binding.EvaluateAsync();
            Assert.That(binding.Alarm.ActiveState!.Id!.Value, Is.False);
        }

        /// <summary>
        /// Invalid configuration cannot publish a partly configured condition.
        /// </summary>
        [Test]
        public async Task RejectsInvalidMappingsLimitsForeignInputsAndDuplicateNamesBeforePublicationAsync()
        {
            IPumpGroupBuilder group = m_pump.Supervision(BrowseNames.SupervisionMechanics)
                .SetDiscrete(BrowseNames.BearingFault, false);
            var input = (BaseVariableState)group.Child(BrowseNames.BearingFault)!;
            PumpsRuntime runtime = m_fixture.Manager.Runtime;
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await runtime.AddDiagnosticAlarmAsync(group, input, "Invalid", DeviceHealthEnumeration.NORMAL));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await runtime.AddDiagnosticAlarmAsync(group, input, "Invalid", (DeviceHealthEnumeration)999));
            foreach (PumpAlarmOptions options in new PumpAlarmOptions[]
            {
                new() { ActiveSeverity = (EventSeverity)0 },
                new() { ActiveSeverity = (EventSeverity)1001 },
                new() { Shelving = true },
                new() { Shelving = true, MaximumShelvingTime = TimeSpan.FromSeconds(-1) }
            })
            {
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await runtime.AddDiagnosticAlarmAsync(
                        group, input, "InvalidLifecycle", DeviceHealthEnumeration.FAILURE, options));
            }
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtime.AddMeasurementAlarmAsync(group, input, "Invalid", new PumpMeasurementLimits()));
            IPumpGroupBuilder measurements = m_pump.Measurements.SetAnalog(BrowseNames.MassFlow, 1);
            var measurement = (BaseVariableState)measurements.Child(BrowseNames.MassFlow)!;
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtime.AddMeasurementAlarmAsync(measurements, measurement, "Invalid",
                    new PumpMeasurementLimits { Low = 20, High = 10 }));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtime.AddDiagnosticAlarmAsync(group, measurement, "Foreign", DeviceHealthEnumeration.FAILURE));
            await runtime.AddDiagnosticAlarmAsync(group, input, "Existing", DeviceHealthEnumeration.FAILURE);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtime.AddDiagnosticAlarmAsync(group, input, "Existing", DeviceHealthEnumeration.FAILURE));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await runtime.AddDiagnosticAlarmAsync(group, input, "Cancelled", DeviceHealthEnumeration.FAILURE,
                    cancellationToken: new CancellationToken(true)));
            var children = new List<BaseInstanceState>();
            group.Node.GetChildren(m_fixture.Manager.SystemContext, children);
            Assert.That(children.FindAll(child => child is AlarmConditionState), Has.Count.EqualTo(1));
        }

        /// <summary>The shared bounded server fixture.</summary>
        private PumpsServerFixture m_fixture = null!;

        /// <summary>The pump isolated to each case.</summary>
        private IPumpBuilder m_pump = null!;

        /// <summary>The sequence used for unique instance names.</summary>
        private int m_sequence;
    }
}
