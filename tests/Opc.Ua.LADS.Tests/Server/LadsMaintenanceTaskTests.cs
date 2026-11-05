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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the maintenance task state machine, its methods and the provider hook.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsMaintenanceTaskTests
    {
        /// <summary>
        /// Exercises maintenance commands with gated notification sinks across state, event, and detail updates.
        /// </summary>
        [TestCase(LadsMaintenanceCommand.Start, "Condition")]
        [TestCase(LadsMaintenanceCommand.Stop, "Condition")]
        [TestCase(LadsMaintenanceCommand.Reset, "Condition")]
        [TestCase(LadsMaintenanceCommand.Start, "Event")]
        [TestCase(LadsMaintenanceCommand.Stop, "Event")]
        [TestCase(LadsMaintenanceCommand.Reset, "Event")]
        [TestCase(LadsMaintenanceCommand.Stop, "Detail")]
        [TestCase(LadsMaintenanceCommand.Reset, "Detail")]
        public async Task MaintenanceReturnsBeforeAnIsolatedAsyncNotificationSinkAsync(
            LadsMaintenanceCommand command, string sink)
        {
            var notification = new LadsNotificationTestSupport.Gate();
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(MaintenanceReturnsBeforeAnIsolatedAsyncNotificationSinkAsync),
                builder =>
                {
                    builder.Services.AddSingleton<ILadsDeviceProvider>(new InMemoryLadsDeviceProvider());
                    builder.Services.AddSingleton<ILadsStore>(new InMemoryLadsStore());
                },
                options: options => options.TimeProvider = new LadsNotificationTestSupport.FixedClock())
                .ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            var task = (LadsMaintenanceTask)device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            if (command != LadsMaintenanceCommand.Start)
            {
                await task.StartAsync().AsTask().WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
            }
            if (command == LadsMaintenanceCommand.Reset)
            {
                await task.StopAsync().AsTask().WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
            }
            LadsMaintenanceStateValue expected = command switch
            {
                LadsMaintenanceCommand.Start => LadsMaintenanceStateValue.Executing,
                LadsMaintenanceCommand.Stop => LadsMaintenanceStateValue.Finished,
                _ => LadsMaintenanceStateValue.Planned
            };
            NodeState node = sink == "Detail"
                ? (command == LadsMaintenanceCommand.Stop ? task.Node.LastExecutionDate! : task.Node.PlannedDate!)
                : task.Node.EventId!;
            NodeStateChangedAsyncHandler? previousState = node.OnStateChangedAsync;
            NodeStateReportEventAsyncHandler? previousReport = task.Node.OnReportEventAsync;
            if (sink == "Event")
            {
                task.Node.OnReportEventAsync = async (context, source, snapshot, token) =>
                {
                    Assert.That(task.State, Is.EqualTo(expected));
                    Assert.That(task.Node.ActiveState!.Id!.Value, Is.EqualTo(command != LadsMaintenanceCommand.Stop));
                    await notification.WaitAsync(token).ConfigureAwait(false);
                    if (previousReport != null)
                    {
                        await previousReport(context, source, snapshot, token).ConfigureAwait(false);
                    }
                };
            }
            else
            {
                node.OnStateChangedAsync = async (context, source, masks, token) =>
                {
                    Assert.That(task.State, Is.EqualTo(expected));
                    Assert.That(masks & NodeStateChangeMasks.Value, Is.Not.EqualTo(NodeStateChangeMasks.None));
                    await notification.WaitAsync(token).ConfigureAwait(false);
                    if (previousState != null)
                    {
                        await previousState(context, source, masks, token).ConfigureAwait(false);
                    }
                };
            }
            var returned = LadsNotificationTestSupport.NewSignal();
            var queuedCommands = new ConcurrentQueue<Task<Exception?>>();
            Task? operation = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                // Gate only this sink. An earlier incomplete async sink would let the method
                // return already and conceal a later synchronous notification bridge.
                operation = Task.Run(async () =>
                {
                    Task pending = InvokeMaintenance(task, command).AsTask();
                    returned.TrySetResult(true);
                    await pending.ConfigureAwait(false);
                });
                await notification.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                await returned.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(notification.Token.CanBeCanceled, Is.False);
                Assert.That(task.State, Is.EqualTo(expected));
                await AssertCommandGateHeldAsync(task, command, queuedCommands).ConfigureAwait(false);
                notification.Release.TrySetResult(true);
                await operation.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                Assert.That(notification.Calls, Is.EqualTo(1));
                Assert.That(task.State, Is.EqualTo(expected));
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                node.OnStateChangedAsync = previousState;
                task.Node.OnReportEventAsync = previousReport;
                notification.Release.TrySetResult(true);
                if (operation != null)
                {
                    try
                    {
                        await LadsNotificationTestSupport.DrainAsync(operation).ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
                try
                {
                    await LadsNotificationTestSupport.DrainQueuedCommandsAsync(queuedCommands, operation)
                        .ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    cleanupFailures.Add(failure);
                }
            }
            LadsNotificationTestSupport.ThrowFailures(testFailure, cleanupFailures);
        }

        /// <summary>
        /// Verifies maintenance notifications retain ordering and the command gate until completion.
        /// </summary>
        [TestCase(LadsMaintenanceCommand.Start, false, "None")]
        [TestCase(LadsMaintenanceCommand.Stop, false, "None")]
        [TestCase(LadsMaintenanceCommand.Reset, false, "None")]
        [TestCase(LadsMaintenanceCommand.Start, true, "None")]
        [TestCase(LadsMaintenanceCommand.Stop, true, "None")]
        [TestCase(LadsMaintenanceCommand.Reset, true, "None")]
        [TestCase(LadsMaintenanceCommand.Start, false, "Condition")]
        [TestCase(LadsMaintenanceCommand.Stop, false, "Condition")]
        [TestCase(LadsMaintenanceCommand.Reset, false, "Condition")]
        [TestCase(LadsMaintenanceCommand.Start, false, "Event")]
        [TestCase(LadsMaintenanceCommand.Stop, false, "Event")]
        [TestCase(LadsMaintenanceCommand.Reset, false, "Event")]
        [TestCase(LadsMaintenanceCommand.Start, false, "Save")]
        [TestCase(LadsMaintenanceCommand.Stop, false, "Save")]
        [TestCase(LadsMaintenanceCommand.Reset, false, "Save")]
        public async Task MaintenanceAwaitsOrderedNotificationsAndRetainsCommandGateAsync(
            LadsMaintenanceCommand command,
            bool cancelAfterTransition,
            string failureStage)
        {
            var transition = new LadsNotificationTestSupport.Gate();
            var detail = new LadsNotificationTestSupport.Gate();
            var condition = new LadsNotificationTestSupport.Gate();
            var report = new LadsNotificationTestSupport.Gate();
            var save = new LadsNotificationTestSupport.Gate();
            var queuedCommands = new ConcurrentQueue<Task<Exception?>>();
            var failure = new InvalidOperationException("Maintenance " + failureStage + " failed.");
            condition.Failure = failureStage == "Condition" ? failure : null;
            report.Failure = failureStage == "Event" ? failure : null;
            save.Failure = failureStage == "Save" ? failure : null;
            var innerStore = new InMemoryLadsStore();
            var store = new Mock<ILadsStore>();
            store.Setup(value => value.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string identity, CancellationToken token) => innerStore.LoadAsync(identity, token));
            bool armed = false;
            store.Setup(value => value.SaveAsync(
                    It.IsAny<string>(), It.IsAny<LadsStoredDeviceData>(), It.IsAny<CancellationToken>()))
                .Returns((string identity, LadsStoredDeviceData data, CancellationToken token) =>
                    SaveAsync(identity, data, token));
            async ValueTask SaveAsync(string identity, LadsStoredDeviceData data, CancellationToken token)
            {
                if (armed)
                {
                    await save.WaitAsync(token).ConfigureAwait(false);
                }
                await innerStore.SaveAsync(identity, data, token).ConfigureAwait(false);
            }
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(MaintenanceAwaitsOrderedNotificationsAndRetainsCommandGateAsync),
                builder =>
                {
                    builder.Services.AddSingleton<ILadsDeviceProvider>(new InMemoryLadsDeviceProvider());
                    builder.Services.AddSingleton(store.Object);
                },
                options: options => options.TimeProvider = new LadsNotificationTestSupport.FixedClock())
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var task = (LadsMaintenanceTask)device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            if (command != LadsMaintenanceCommand.Start)
            {
                await task.StartAsync().AsTask().WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
            }
            if (command == LadsMaintenanceCommand.Reset)
            {
                await task.StopAsync().AsTask().WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
            }
            LadsMaintenanceStateValue expected = command switch
            {
                LadsMaintenanceCommand.Start => LadsMaintenanceStateValue.Executing,
                LadsMaintenanceCommand.Stop => LadsMaintenanceStateValue.Finished,
                _ => LadsMaintenanceStateValue.Planned
            };
            bool active = command != LadsMaintenanceCommand.Stop;
            var stateNode = task.Node.MaintenanceState!.CurrentState!;
            var eventIdNode = task.Node.EventId!;
            NodeState? detailNode = command switch
            {
                LadsMaintenanceCommand.Stop => task.Node.LastExecutionDate,
                LadsMaintenanceCommand.Reset => task.Node.PlannedDate,
                _ => null
            };
            NodeStateChangedHandler? previousSyncState = stateNode.OnStateChanged;
            NodeStateChangedHandler? previousSyncCondition = eventIdNode.OnStateChanged;
            NodeStateReportEventHandler? previousSyncReport = task.Node.OnReportEvent;
            NodeStateChangedAsyncHandler? previousState = stateNode.OnStateChangedAsync;
            NodeStateChangedAsyncHandler? previousCondition = eventIdNode.OnStateChangedAsync;
            NodeStateChangedAsyncHandler? previousDetail = detailNode?.OnStateChangedAsync;
            NodeStateReportEventAsyncHandler? previousReport = task.Node.OnReportEventAsync;
            var order = new List<string>();
            void StateChanged(LadsMaintenanceStateValue state)
            {
                Assert.That(state, Is.EqualTo(expected));
                order.Add("state-changed");
            }
            task.StateChanged += StateChanged;
            void EventReported(ISystemContext context, NodeState node, IFilterTarget snapshot)
            {
                order.Add("event-reported");
            }
            task.Node.EventReported += EventReported;
            stateNode.OnStateChanged = (context, node, masks) =>
            {
                previousSyncState?.Invoke(context, node, masks);
                order.Add("transition-sync");
            };
            eventIdNode.OnStateChanged = (context, node, masks) =>
            {
                previousSyncCondition?.Invoke(context, node, masks);
                order.Add("condition-sync");
            };
            task.Node.OnReportEvent = (context, node, snapshot) =>
            {
                previousSyncReport?.Invoke(context, node, snapshot);
                order.Add("event-sync");
            };
            stateNode.OnStateChangedAsync = async (context, node, masks, token) =>
            {
                Assert.That(task.State, Is.EqualTo(expected));
                Assert.That(stateNode.Value.Text, Is.EqualTo(expected.ToString()));
                order.Add("transition");
                await transition.WaitAsync(token).ConfigureAwait(false);
                if (previousState != null)
                {
                    await previousState(context, node, masks, token).ConfigureAwait(false);
                }
            };
            if (detailNode != null)
            {
                detailNode.OnStateChangedAsync = async (context, node, masks, token) =>
                {
                    Assert.That(task.State, Is.EqualTo(expected));
                    Assert.That(task.LastExecutionDate.ToDateTime(),
                        Is.EqualTo(LadsNotificationTestSupport.FixedClock.Now.UtcDateTime));
                    order.Add("detail");
                    await detail.WaitAsync(token).ConfigureAwait(false);
                    if (previousDetail != null)
                    {
                        await previousDetail(context, node, masks, token).ConfigureAwait(false);
                    }
                };
            }
            eventIdNode.OnStateChangedAsync = async (context, node, masks, token) =>
            {
                Assert.That(task.State, Is.EqualTo(expected));
                Assert.That(task.Node.ActiveState!.Id!.Value, Is.EqualTo(active));
                Assert.That(task.Node.Severity!.Value, Is.EqualTo(active ? 500 : 100));
                Assert.That(task.Node.Retain!.Value, Is.True);
                // Reentrant commands must remain serialized; cancel the wait rather than
                // awaiting a valid nested command that would deadlock on the owning gate.
                await AssertCommandGateHeldAsync(task, command, queuedCommands).ConfigureAwait(false);
                order.Add("condition");
                await condition.WaitAsync(token).ConfigureAwait(false);
                if (previousCondition != null)
                {
                    await previousCondition(context, node, masks, token).ConfigureAwait(false);
                }
            };
            IFilterTarget? reportedEvent = null;
            task.Node.OnReportEventAsync = async (context, node, snapshot, token) =>
            {
                reportedEvent = snapshot;
                Assert.That(task.State, Is.EqualTo(expected));
                var filterContext = new FilterContext(context.NamespaceUris, context.TypeTable, context.Telemetry);
                Variant activeValue = snapshot.GetAttributeValue(
                    filterContext, default,
                    [task.Node.ActiveState!.BrowseName, task.Node.ActiveState.Id!.BrowseName],
                    Attributes.Value, default);
                Assert.That(activeValue.TryGetValue(out bool snapshotActive), Is.True);
                Assert.That(snapshotActive, Is.EqualTo(active));
                Variant severityValue = snapshot.GetAttributeValue(
                    filterContext, default, [task.Node.Severity!.BrowseName], Attributes.Value, default);
                Assert.That(severityValue.TryGetValue(out ushort severity), Is.True);
                Assert.That(severity, Is.EqualTo(active ? 500 : 100));
                order.Add("event");
                await report.WaitAsync(token).ConfigureAwait(false);
                if (previousReport != null)
                {
                    await previousReport(context, node, snapshot, token).ConfigureAwait(false);
                }
            };
            using var cancellation = new CancellationTokenSource();
            var returned = LadsNotificationTestSupport.NewSignal();
            Task? operation = null;
            var expectedOrder = new List<string> { "transition-sync", "transition" };
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                armed = true;
                operation = Task.Run(async () =>
                {
                    Task pending = InvokeMaintenance(task, command, cancellation.Token).AsTask();
                    returned.TrySetResult(true);
                    await pending.ConfigureAwait(false);
                });
                await transition.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                await returned.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(order, Is.EqualTo(expectedOrder));
                Assert.That(condition.Entered.Task.IsCompleted, Is.False);
                await AssertCommandGateHeldAsync(task, command, queuedCommands).ConfigureAwait(false);
                if (cancelAfterTransition)
                {
                    cancellation.Cancel();
                }
                Assert.That(transition.Token.CanBeCanceled, Is.False);
                transition.Release.TrySetResult(true);
                expectedOrder.Add("state-changed");
                if (detailNode != null)
                {
                    await detail.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                    expectedOrder.Add("detail");
                    Assert.That(order, Is.EqualTo(expectedOrder));
                    Assert.That(operation.IsCompleted, Is.False);
                    Assert.That(condition.Entered.Task.IsCompleted, Is.False);
                    Assert.That(detail.Token.CanBeCanceled, Is.False);
                    await AssertCommandGateHeldAsync(task, command, queuedCommands).ConfigureAwait(false);
                    detail.Release.TrySetResult(true);
                }

                await condition.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                expectedOrder.AddRange(["condition-sync", "condition"]);
                Assert.That(order, Is.EqualTo(expectedOrder));
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(report.Entered.Task.IsCompleted, Is.False);
                Assert.That(save.Entered.Task.IsCompleted, Is.False);
                Assert.That(condition.Token.CanBeCanceled, Is.False);
                if (command == LadsMaintenanceCommand.Stop)
                {
                    Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Failure));
                    Assert.That(task.LastExecutionDate.ToDateTime(),
                        Is.EqualTo(LadsNotificationTestSupport.FixedClock.Now.UtcDateTime));
                    Assert.That(task.Node.LastExecutionDate!.Value, Is.EqualTo(task.LastExecutionDate));
                    Assert.That(task.Node.Message!.Value.Text, Is.EqualTo("Gated stop."));
                }
                if (command == LadsMaintenanceCommand.Reset)
                {
                    Assert.That(task.Node.PlannedDate!.Value.ToDateTime(),
                        Is.EqualTo(task.LastExecutionDate.ToDateTime() + TimeSpan.FromDays(30)));
                }
                await AssertCommandGateHeldAsync(task, command, queuedCommands).ConfigureAwait(false);
                condition.Release.TrySetResult(true);

                if (failureStage != "Condition")
                {
                    await report.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                    expectedOrder.Add("event");
                    Assert.That(order, Is.EqualTo(expectedOrder));
                    Assert.That(operation.IsCompleted, Is.False);
                    Assert.That(save.Entered.Task.IsCompleted, Is.False);
                    Assert.That(report.Token.CanBeCanceled, Is.False);
                    await AssertCommandGateHeldAsync(task, command, queuedCommands).ConfigureAwait(false);
                    report.Release.TrySetResult(true);
                    if (failureStage != "Event" && !cancelAfterTransition)
                    {
                        await save.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                        Assert.That(order, Is.EqualTo(expectedOrder.Append("event-reported").ToArray()));
                        Assert.That(operation.IsCompleted, Is.False);
                        Assert.That(save.Token, Is.EqualTo(cancellation.Token),
                            "The save retains the existing caller-cancellation contract.");
                        await AssertCommandGateHeldAsync(task, command, queuedCommands).ConfigureAwait(false);
                        save.Release.TrySetResult(true);
                    }
                }
                if (failureStage != "None")
                {
                    await Assert.ThatAsync(
                        async () => await operation
                            .WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                }
                else if (cancelAfterTransition)
                {
                    await Assert.ThatAsync(
                        async () => await operation
                            .WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    Assert.That(save.Entered.Task.IsCompleted, Is.False);
                }
                else
                {
                    await operation.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                    LadsStoredDeviceData saved = (await innerStore.LoadAsync(device.StoreIdentity)
                        .ConfigureAwait(false))!;
                    Assert.That(saved.MaintenanceTasks.ToList().Single(value => value.Name == task.Name).State,
                        Is.EqualTo(expected));
                }
                Assert.That(task.State, Is.EqualTo(expected), "Notification/save failure must not roll back state.");
                Assert.That(report.Calls, Is.EqualTo(failureStage == "Condition" ? 0 : 1));
                Assert.That(reportedEvent, failureStage == "Condition" ? Is.Null : Is.Not.Null);
                Assert.That(order.Count(value => value == "event-reported"),
                    Is.EqualTo(failureStage is "Condition" or "Event" ? 0 : 1));
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                armed = false;
                task.StateChanged -= StateChanged;
                task.Node.EventReported -= EventReported;
                stateNode.OnStateChanged = previousSyncState;
                eventIdNode.OnStateChanged = previousSyncCondition;
                task.Node.OnReportEvent = previousSyncReport;
                stateNode.OnStateChangedAsync = previousState;
                eventIdNode.OnStateChangedAsync = previousCondition;
                if (detailNode != null)
                {
                    detailNode.OnStateChangedAsync = previousDetail;
                }
                task.Node.OnReportEventAsync = previousReport;
                transition.Release.TrySetResult(true);
                detail.Release.TrySetResult(true);
                condition.Release.TrySetResult(true);
                report.Release.TrySetResult(true);
                save.Release.TrySetResult(true);
                if (operation != null)
                {
                    try
                    {
                        await LadsNotificationTestSupport.DrainAsync(operation).ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
                try
                {
                    await LadsNotificationTestSupport.DrainQueuedCommandsAsync(queuedCommands, operation)
                        .ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    cleanupFailures.Add(failure);
                }
            }
            LadsNotificationTestSupport.ThrowFailures(testFailure, cleanupFailures);
            // A valid subsequent command proves release on success, cancellation and every tested failure.
            LadsMaintenanceCommand next = command switch
            {
                LadsMaintenanceCommand.Start => LadsMaintenanceCommand.Stop,
                LadsMaintenanceCommand.Stop => LadsMaintenanceCommand.Reset,
                _ => LadsMaintenanceCommand.Start
            };
            await InvokeMaintenance(task, next).AsTask().WaitAsync(LadsNotificationTestSupport.Timeout)
                .ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(command switch
            {
                LadsMaintenanceCommand.Start => LadsMaintenanceStateValue.Finished,
                LadsMaintenanceCommand.Stop => LadsMaintenanceStateValue.Planned,
                _ => LadsMaintenanceStateValue.Executing
            }));
        }

        private static ValueTask InvokeMaintenance(
            ILadsMaintenanceTask task, LadsMaintenanceCommand command, CancellationToken token = default)
        {
            return command switch
            {
                LadsMaintenanceCommand.Start => task.StartAsync(token),
                LadsMaintenanceCommand.Stop => task.StopAsync(
                    MaintenanceTaskResultEnum.Failure, new LocalizedText("Gated stop."), token),
                _ => task.ResetAsync(token)
            };
        }

        private static async Task AssertCommandGateHeldAsync(
            ILadsMaintenanceTask task,
            LadsMaintenanceCommand command,
            ConcurrentQueue<Task<Exception?>> queuedCommands)
        {
            using var cancellation = new CancellationTokenSource();
            // The same command is invalid in the committed destination state. If the gate were
            // released early, Require would fault inline, before reaching any notification sink.
            Task<Task> invocation = Task.Factory.StartNew(
                () => InvokeMaintenance(task, command, cancellation.Token).AsTask(),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
            Task<Exception?> observer = ObserveMaintenanceCommandAsync(invocation);
            queuedCommands.Enqueue(observer);
            ExceptionDispatchInfo? testFailure = null;
            bool cancellationObserved = false;
            try
            {
                Task invocationCompleted = await Task.WhenAny(
                    invocation, Task.Delay(LadsNotificationTestSupport.Timeout)).ConfigureAwait(false);
                Assert.That(invocationCompleted, Is.SameAs(invocation));
                Task queued = await invocation.ConfigureAwait(false);
                Assert.That(queued.IsCompleted, Is.False, "The command gate was released before notification/save.");
                cancellation.Cancel();
                await Assert.ThatAsync(
                    async () => await queued.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                cancellationObserved = true;
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                cancellation.Cancel();
            }
            if (!cancellationObserved && testFailure == null)
            {
                testFailure = ExceptionDispatchInfo.Capture(
                    new InvalidOperationException("The queued maintenance command was not canceled."));
            }
            testFailure?.Throw();
        }

        private static async Task<Exception?> ObserveMaintenanceCommandAsync(Task<Task> invocation)
        {
            try
            {
                Task queued = await invocation.ConfigureAwait(false);
                await queued.ConfigureAwait(false);
                return null;
            }
            catch (Exception failure)
            {
                return failure;
            }
        }

        [Test]
        public async Task MaintenanceTaskCyclesThroughItsStatesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(MaintenanceTaskCyclesThroughItsStatesAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            var states = new List<LadsMaintenanceStateValue>();
            task.StateChanged += states.Add;

            Assert.That(task.Name, Is.EqualTo(LadsIncubator.MaintenanceName));
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Planned));
            Assert.That(task.LastExecutionDate, Is.EqualTo(DateTimeUtc.MinValue));

            DateTime before = DateTime.UtcNow.AddSeconds(-1);
            await task.StartAsync().ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));
            await task.StopAsync(MaintenanceTaskResultEnum.Success, new LocalizedText("Calibrated."))
                .ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Finished));
            Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Success));
            Assert.That(task.LastExecutionDate.ToDateTime(), Is.GreaterThanOrEqualTo(before));

            await task.ResetAsync().ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Planned));
            Assert.That(
                states,
                Is.EqualTo(new[]
                {
                    LadsMaintenanceStateValue.Executing,
                    LadsMaintenanceStateValue.Finished,
                    LadsMaintenanceStateValue.Planned
                }));

            NodeId planned = await server.ChildAsync(task.NodeId, "PlannedDate").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(planned).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out DateTimeUtc next), Is.True);
            Assert.That(
                next.ToDateTime(),
                Is.EqualTo(task.LastExecutionDate.ToDateTime() + TimeSpan.FromDays(30)));
        }

        [Test]
        public async Task InvalidMaintenanceTransitionsAreRejectedAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(InvalidMaintenanceTransitionsAreRejectedAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.StopAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.ResetAsync()).ConfigureAwait(false);
            await task.StartAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.StartAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.ResetAsync()).ConfigureAwait(false);
            await task.StopAsync(MaintenanceTaskResultEnum.Failure).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.StartAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => task.StopAsync()).ConfigureAwait(false);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Finished));
            Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Failure));
        }

        [Test]
        public async Task MaintenanceMethodsAreCallableAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(MaintenanceMethodsAreCallableAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            NodeId node = await server.ChildAsync(device.NodeId, "Maintenance", LadsIncubator.MaintenanceName)
                .ConfigureAwait(false);
            Assert.That(node, Is.EqualTo(task.NodeId));

            CallMethodResult start = await server.CallByNameAsync(node, "StartTask").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(start.StatusCode), Is.True, start.StatusCode.ToString());
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));

            CallMethodResult invalid = await server.CallByNameAsync(
                node,
                "StopTask",
                Variant.From(99),
                Variant.From(new LocalizedText("Bad code."))).ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));

            CallMethodResult stop = await server.CallByNameAsync(
                node,
                "StopTask",
                Variant.From((int)MaintenanceTaskResultEnum.Undetermined),
                Variant.From(new LocalizedText("Partly done."))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(stop.StatusCode), Is.True, stop.StatusCode.ToString());
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Finished));
            Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Undetermined));

            CallMethodResult again = await server.CallByNameAsync(node, "StartTask").ConfigureAwait(false);
            Assert.That(again.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));

            CallMethodResult reset = await server.CallByNameAsync(node, "ResetTask").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(reset.StatusCode), Is.True, reset.StatusCode.ToString());
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Planned));

            NodeId lastExecution = await server.ChildAsync(node, "LastExecutionDate").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(lastExecution).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out DateTimeUtc date), Is.True);
            Assert.That(date, Is.EqualTo(task.LastExecutionDate));
        }

        [Test]
        public async Task DeviceProviderCanVetoMaintenanceCommandsAsync()
        {
            var provider = new Mock<ILadsDeviceProvider>();
            provider
                .Setup(p => p.OnMaintenanceTaskAsync(
                    It.IsAny<ILadsMaintenanceTask>(),
                    LadsMaintenanceCommand.Start,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            provider
                .Setup(p => p.OnMaintenanceTaskAsync(
                    It.IsAny<ILadsMaintenanceTask>(),
                    LadsMaintenanceCommand.Stop,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(DeviceProviderCanVetoMaintenanceCommandsAsync),
                services: builder => builder.Services.AddSingleton(provider.Object)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);

            await task.StartAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadUserAccessDenied,
                () => task.StopAsync()).ConfigureAwait(false);

            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));
            provider.Verify(
                p => p.OnMaintenanceTaskAsync(task, LadsMaintenanceCommand.Start, It.IsAny<CancellationToken>()),
                Times.Once);
            provider.Verify(
                p => p.OnMaintenanceTaskAsync(task, LadsMaintenanceCommand.Stop, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Bounded notification gates shared by the program and maintenance regression tests.
        /// </summary>
        internal static class LadsNotificationTestSupport
        {
            internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

            internal static TaskCompletionSource<bool> NewSignal()
            {
                return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal static async Task DrainAsync(Task operation)
            {
                // Cleanup observes faults without replacing the behavioral assertions in the test body.
                await operation.ContinueWith(
                    completed => { _ = completed.Exception; },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                    .WaitAsync(Timeout).ConfigureAwait(false);
            }

            internal static async Task DrainQueuedCommandsAsync(
                ConcurrentQueue<Task<Exception?>> queuedCommands,
                Task? operation)
            {
                var observers = new List<Task<Exception?>>();
                var observedObservers = new HashSet<Task<Exception?>>();
                Task timeout = Task.Delay(Timeout);
                bool timedOut = false;
                while (true)
                {
                    foreach (Task<Exception?> observer in queuedCommands.ToArray())
                    {
                        if (observedObservers.Add(observer))
                        {
                            observers.Add(observer);
                        }
                    }

                    bool operationCompleted = operation == null || operation.IsCompleted;
                    if (operationCompleted)
                    {
                        foreach (Task<Exception?> observer in queuedCommands.ToArray())
                        {
                            if (observedObservers.Add(observer))
                            {
                                observers.Add(observer);
                            }
                        }
                    }

                    Task<Exception?>[] pending = observers.Where(observer => !observer.IsCompleted).ToArray();
                    if (pending.Length == 0 && operationCompleted)
                    {
                        break;
                    }

                    if (timeout.IsCompleted)
                    {
                        timedOut = true;
                        break;
                    }

                    var waits = new List<Task> { Task.Delay(TimeSpan.FromMilliseconds(10)), timeout };
                    if (pending.Length > 0)
                    {
                        waits.Add(Task.WhenAny(pending));
                    }
                    if (operation != null && !operationCompleted)
                    {
                        waits.Add(operation);
                    }
                    await Task.WhenAny(waits).ConfigureAwait(false);
                }

                var cleanupFailures = new List<Exception>();
                if (timedOut)
                {
                    cleanupFailures.Add(
                        new TimeoutException(
                            "Queued maintenance command observers did not drain before the deadline; "
                            + "later observer failures may not be collected."));
                }

                foreach (Task<Exception?> observer in observers)
                {
                    if (!observer.IsCompleted)
                    {
                        // Keep observing task faults after the deadline, but do not claim that later
                        // returned observer failures are collected.
                        _ = observer.ContinueWith(
                            completedObserver => { _ = completedObserver.Exception; },
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                        continue;
                    }

                    try
                    {
                        Exception? failure = await observer.ConfigureAwait(false);
                        if (failure != null && failure is not OperationCanceledException)
                        {
                            cleanupFailures.Add(failure);
                        }
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
                ThrowFailures(null, cleanupFailures);
            }

            internal static void ThrowFailures(
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

            internal sealed class Gate
            {
                internal TaskCompletionSource<bool> Entered { get; } = NewSignal();

                internal TaskCompletionSource<bool> Release { get; } = NewSignal();

                internal CancellationToken Token { get; private set; }

                internal Exception? Failure { get; set; }

                internal int Calls => Volatile.Read(ref m_calls);

                internal async ValueTask WaitAsync(CancellationToken token)
                {
                    if (Interlocked.Increment(ref m_calls) != 1)
                    {
                        return;
                    }
                    Token = token;
                    Entered.TrySetResult(true);
                    await Release.Task.WaitAsync(Timeout, token).ConfigureAwait(false);
                    if (Failure != null)
                    {
                        throw Failure;
                    }
                }

                private int m_calls;
            }

            internal sealed class FixedClock : TimeProvider
            {
                internal static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

                public override DateTimeOffset GetUtcNow() => Now;
            }
        }
    }
}
