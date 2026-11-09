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
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Integration;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Exercises storage/address-space compensation, identity ownership and complete shutdown cleanup.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsTransactionTests
    {
        /// <summary>
        /// Restores template storage and native registration for failures before and after each node change.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TemplatePublicationFailureRestoresStorageAndBrowsingAndAllowsRetryAsync(
            bool remove, bool failAfterChange)
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = failAfterChange };
            var store = new FaultingStore();
            await using LadsHostedServer server = await StartAsync(
                nameof(TemplatePublicationFailureRestoresStorageAndBrowsingAndAllowsRetryAsync), store, nodes)
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var programs = (LadsProgramManager)device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            string id = remove ? LadsIncubator.QuickTemplate : "Added";
            nodes.FailNextAdd = !remove;
            nodes.FailNextRemove = remove;
            await Assert.ThrowsAsync<IOException>(async () =>
            {
                if (remove)
                {
                    await programs.RemoveTemplateAsync(id).ConfigureAwait(false);
                }
                else
                {
                    await programs.AddTemplateAsync(Template(id)).ConfigureAwait(false);
                }
            });
            Assert.That(programs.Templates.ToList().Any(template => template.Id == id), Is.EqualTo(remove));
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.FunctionalUnits[0].Templates.ToList().Any(template => template.Id == id),
                Is.EqualTo(remove));
            Assert.That(saved.FunctionalUnits[0].RemovedTemplateIds.Count, Is.Zero);
            AssertTemplateNode(device, programs, id, remove);
            if (!remove)
            {
                Assert.That(nodes.LastAdded, Is.Not.Null);
                AssertSubtreeRegistration(device, nodes.LastAdded!, present: false);
            }
            if (remove)
            {
                Assert.That(await programs.RemoveTemplateAsync(id).ConfigureAwait(false), Is.True);
            }
            else
            {
                await programs.AddTemplateAsync(Template(id)).ConfigureAwait(false);
            }
            AssertTemplateNode(device, programs, id, !remove);
        }

        /// <summary>
        /// Preserves the previous result and its client-readable files when publication or removal fails.
        /// </summary>
        [TestCase("add")]
        [TestCase("remove")]
        [TestCase("prune")]
        public async Task ResultPublicationFailureKeepsRetainedFilesBrowsableAndAllowsRetryAsync(string operation)
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            var store = new FaultingStore();
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ResultPublicationFailureKeepsRetainedFilesBrowsableAndAllowsRetryAsync) + operation,
                builder =>
                {
                    builder.Services.AddSingleton<ILadsNodeOperations>(nodes);
                    builder.AddLads(options =>
                    {
                        options.Store = store;
                        options.MaxRetainedResults = operation == "prune" ? 1 : 10;
                    }).ConfigureLads(LadsIncubator.Configure);
                }).ConfigureAwait(false);
            var device = (LadsDevice)await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;
            string retainedId = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult retained = await programs.WaitForResultAsync(retainedId).ConfigureAwait(false);
            nodes.FailNextAdd = operation == "add";
            nodes.FailNextRemove = operation != "add";
            await Assert.ThrowsAsync<IOException>(async () =>
            {
                if (operation == "remove")
                {
                    await programs.RemoveResultAsync(retainedId).ConfigureAwait(false);
                }
                else
                {
                    string failedId = await programs.StartProgramAsync(LadsIncubator.QuickTemplate)
                        .ConfigureAwait(false);
                    await programs.WaitForResultAsync(failedId).ConfigureAwait(false);
                }
            });
            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(programs.Results[0], Is.SameAs(retained));
            Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.OperationCycles, Is.EqualTo(1));
            Assert.That(saved.FunctionalUnits[0].Results.Count, Is.EqualTo(1));
            Assert.That(saved.FunctionalUnits[0].Results[0].RunId, Is.EqualTo(retainedId));
            Assert.That(saved.FunctionalUnits[0].Results[0].Files[0].Content.ToArray(),
                Is.EqualTo(retained.Files[0].Content.ToArray()));
            Opc.Ua.LADS.Client.LadsFunctionalUnit remoteUnit = await connection.ChamberAsync().ConfigureAwait(false);
            Opc.Ua.LADS.Client.LadsResult? remote = await remoteUnit.FindResultAsync(retainedId).ConfigureAwait(false);
            Assert.That(remote, Is.Not.Null);
            Assert.That(remote!.Files.Count, Is.EqualTo(retained.Files.Count));
            for (int index = 0; index < retained.Files.Count; index++)
            {
                ByteString downloaded = await connection.Client.ReadResultFileAsync(remote.Files[index])
                    .ConfigureAwait(false);
                Assert.That(downloaded.ToArray(), Is.EqualTo(retained.Files[index].Content.ToArray()));
            }
            if (operation != "remove")
            {
                await unit.ClearAsync().ConfigureAwait(false);
                string retry = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
                await programs.WaitForResultAsync(retry).ConfigureAwait(false);
                Assert.That(device.OperationCycleCounter, Is.EqualTo(2));
            }
            Assert.That(await programs.RemoveResultAsync(retainedId).ConfigureAwait(false),
                Is.EqualTo(operation != "prune"));
        }

        /// <summary>
        /// Rejects mutations after incomplete compensation and rebuilds the retained snapshot on restart.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedCompensationQuarantinesDeviceAndPreservesBothErrorsAsync(bool storageRollback)
        {
            var nodes = new FaultingNodeOperations
            {
                FailAfterChange = true,
                FailRemovalBeforeChange = true
            };
            var store = new FaultingStore();
            await using (LadsHostedServer server = await StartAsync(
                nameof(FailedCompensationQuarantinesDeviceAndPreservesBothErrorsAsync), store, nodes)
                .ConfigureAwait(false))
            {
                var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
                ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
                ILadsProgramManager programs = unit.Programs!;
                nodes.FailNextAdd = true;
                nodes.FailNextRemove = !storageRollback;
                store.FailSaveNumber = storageRollback ? store.Saves + 2 : 0;
                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await programs.AddTemplateAsync(Template("Uncertain")).ConfigureAwait(false));
                string[] messages = failure!.Flatten().InnerExceptions.Select(error => error.Message).ToArray();
                Assert.That(messages, Does.Contain("Node add failed."));
                Assert.That(messages, Does.Contain(storageRollback ? "Store save failed." : "Node remove failed."));
                Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
                Assert.That(programs.Templates.ToList().Any(template => template.Id == "Uncertain"), Is.False);
                AssertTemplateNode(device, (LadsProgramManager)programs, "Uncertain", !storageRollback);
                int saves = store.Saves;
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await programs.AddTemplateAsync(Template("Rejected")).ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await programs.RemoveTemplateAsync("Missing").ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await programs.RemoveResultAsync("Missing").ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await device.GetMaintenanceTask(LadsIncubator.MaintenanceName).StartAsync().ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await device.GotoOperateAsync().ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await device.SetOperationModeAsync(MachineryOperationModeValue.Processing).ConfigureAwait(false));
                Assert.Throws<InvalidOperationException>(() => device.IncrementOperationCycles());
                Assert.Throws<InvalidOperationException>(() => device.AddOperationDuration(TimeSpan.FromSeconds(1)));
                IAnalogControlFunction temperature = unit.GetFunction<IAnalogControlFunction>("Temperature");
                double target = temperature.TargetValue;
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await temperature.SetTargetValueAsync(target + 1).ConfigureAwait(false));
                Assert.Throws<InvalidOperationException>(() => temperature.UpdateCurrentValue(30));
                Assert.Throws<InvalidOperationException>(() => temperature.IsEnabled = false);
                IAnalogScalarSensorFunction sensor = unit.GetFunction<IAnalogScalarSensorFunction>("CO2");
                Assert.Throws<InvalidOperationException>(() => sensor.UpdateValue(10));
                Assert.That(temperature.TargetValue, Is.EqualTo(target));
                Assert.That(temperature.CurrentValue, Is.EqualTo(22));
                Assert.That(temperature.IsEnabled, Is.True);
                Assert.That(sensor.SensorValue, Is.EqualTo(5));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await device.SaveStoredDataAsync().ConfigureAwait(false));
                Assert.That(store.Saves, Is.EqualTo(saves));
            }
            await using LadsHostedServer restarted = await StartAsync(
                nameof(FailedCompensationQuarantinesDeviceAndPreservesBothErrorsAsync) + "Restart", store, nodes)
                .ConfigureAwait(false);
            var recovered = (LadsDevice)await restarted.DeviceAsync().ConfigureAwait(false);
            var recoveredPrograms = (LadsProgramManager)recovered.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            Assert.That(recovered.State, Is.EqualTo(LadsDeviceStateValue.Operate));
            Assert.That(recoveredPrograms.Templates.ToList().Any(template => template.Id == "Uncertain"),
                Is.EqualTo(storageRollback));
            AssertTemplateNode(recovered, recoveredPrograms, "Uncertain", storageRollback);
            await recoveredPrograms.AddTemplateAsync(Template("Recovered")).ConfigureAwait(false);
            AssertTemplateNode(recovered, recoveredPrograms, "Recovered", true);
        }

        /// <summary>
        /// Serializes the quarantined ItemState write and notification with queued projections.
        /// </summary>
        [Test]
        public async Task QuarantineSerializesItemStateNotificationAndProjectionAsync()
        {
            const int timeoutMilliseconds = 5000;
            var nodes = new FaultingNodeOperations { FailAfterChange = true, FailRemovalBeforeChange = true };
            await using LadsHostedServer server = await StartAsync(
                nameof(QuarantineSerializesItemStateNotificationAndProjectionAsync),
                new FaultingStore(),
                nodes).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);

            var notificationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Func<MachineryItemStateValue, CancellationToken, ValueTask> handler = async (_, _) =>
            {
                if (device.ItemState == MachineryItemStateValue.OutOfService)
                {
                    notificationEntered.TrySetResult(true);
                    await releaseNotification.Task.WaitAsync(
                        TimeSpan.FromMilliseconds(timeoutMilliseconds), CancellationToken.None)
                        .ConfigureAwait(false);
                }
            };
            device.ItemStateMachine.StateChangedAsync += handler;

            Task? quarantine = null;
            Task? projection = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                quarantine = QuarantineAsync(device, nodes);
                Task notificationStarted = await Task.WhenAny(
                    notificationEntered.Task,
                    Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(notificationStarted, Is.SameAs(notificationEntered.Task));
                Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.OutOfService));

                projection = device.OnFunctionalUnitStateChangedAsync(CancellationToken.None).AsTask();
                Assert.That(projection.IsCompleted, Is.False);
                Assert.That(quarantine.IsCompleted, Is.False);

                releaseNotification.TrySetResult(true);
                await quarantine.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
                await projection.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.OutOfService));
                Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                releaseNotification.TrySetResult(true);
                device.ItemStateMachine.StateChangedAsync -= handler;
                var pending = new List<Task>();
                if (quarantine != null)
                {
                    pending.Add(quarantine);
                }
                if (projection != null)
                {
                    pending.Add(projection);
                }
                if (pending.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                            .ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        if (testFailure == null || !ReferenceEquals(testFailure.SourceException, failure))
                        {
                            cleanupFailures.Add(failure);
                        }
                    }
                }
            }

            if (testFailure != null && cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "The quarantine notification test and its cleanup both failed.",
                    [testFailure.SourceException, .. cleanupFailures]);
            }
            if (testFailure != null)
            {
                testFailure.Throw();
            }
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException("Quarantine notification cleanup failed.", cleanupFailures);
            }
        }

        /// <summary>
        /// Drains admitted unit and program starts before quarantine publishes Shutdown.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task QuarantineAbortsAdmittedStartBeforeShutdownAsync(bool programStart)
        {
            const int timeoutMilliseconds = 5000;
            var nodes = new FaultingNodeOperations
            {
                FailAfterChange = true,
                FailRemovalBeforeChange = true,
                PauseNextRemove = true
            };
            await using LadsHostedServer server = await StartAsync(
                nameof(QuarantineAbortsAdmittedStartBeforeShutdownAsync) + programStart,
                new FaultingStore(),
                nodes,
                extra: device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit("Second");
            if (programStart)
            {
                await unit.Programs!.AddTemplateAsync(Template("Start")).ConfigureAwait(false);
            }
            var notificationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var shutdownObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Func<LadsFunctionalStateValue, CancellationToken, ValueTask> functionalHandler = async (state, _) =>
            {
                if (state == LadsFunctionalStateValue.Running)
                {
                    notificationEntered.TrySetResult(true);
                    await releaseNotification.Task.WaitAsync(
                        TimeSpan.FromMilliseconds(timeoutMilliseconds), CancellationToken.None)
                        .ConfigureAwait(false);
                }
            };
            Func<LadsDeviceStateValue, CancellationToken, ValueTask> deviceHandler = (state, _) =>
            {
                if (state == LadsDeviceStateValue.Shutdown)
                {
                    shutdownObserved.TrySetResult(
                        unit.Functional.Current == LadsFunctionalStateValue.Aborted &&
                        unit.Running.Current == LadsRunningStateValue.Idle &&
                        device.ItemState == MachineryItemStateValue.OutOfService);
                }
                return default;
            };
            unit.Functional.StateChangedAsync += functionalHandler;
            device.DeviceState.StateChangedAsync += deviceHandler;

            Task? start = null;
            Task? quarantine = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                start = Task.Run(async () =>
                {
                    if (programStart)
                    {
                        await unit.Programs!.StartProgramAsync("Start").ConfigureAwait(false);
                    }
                    else
                    {
                        await unit.StartAsync().ConfigureAwait(false);
                    }
                });
                Task notificationStarted = await Task.WhenAny(
                    notificationEntered.Task,
                    Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(notificationStarted, Is.SameAs(notificationEntered.Task));

                quarantine = Task.Run(() => QuarantineAsync(device, nodes));
                await nodes.RemoveEntered.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false);
                nodes.ReleaseRemove.TrySetResult(true);
                await WaitForStorageQuarantineAsync(device, timeoutMilliseconds).ConfigureAwait(false);

                Assert.That(start.IsCompleted, Is.False);
                Assert.That(quarantine.IsCompleted, Is.False);
                Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Operate));
                Assert.That(unit.Functional.Current, Is.EqualTo(LadsFunctionalStateValue.Running));

                releaseNotification.TrySetResult(true);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await start.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false));
                start = null;
                await quarantine.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
                quarantine = null;

                Assert.That(unit.Functional.Current, Is.EqualTo(LadsFunctionalStateValue.Aborted));
                Assert.That(unit.Running.Current, Is.EqualTo(LadsRunningStateValue.Idle));
                Assert.That(await shutdownObserved.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false), Is.True);
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                releaseNotification.TrySetResult(true);
                nodes.ReleaseRemove.TrySetResult(true);
                var pending = new List<Task>();
                if (start != null)
                {
                    pending.Add(start);
                }
                if (quarantine != null)
                {
                    pending.Add(quarantine);
                }
                if (pending.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                            .ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
                unit.Functional.StateChangedAsync -= functionalHandler;
                device.DeviceState.StateChangedAsync -= deviceHandler;
            }

            if (testFailure != null && cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "The quarantine start test and its cleanup both failed.",
                    [testFailure.SourceException, .. cleanupFailures]);
            }
            if (testFailure != null)
            {
                testFailure.Throw();
            }
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException("Quarantine start cleanup failed.", cleanupFailures);
            }
        }

        /// <summary>
        /// Cancels and drains an admitted executor when quarantine interrupts its post-scheduling notification.
        /// </summary>
        [Test]
        public async Task QuarantineDrainsProgramStartAfterSchedulingBeforeShutdownAsync()
        {
            const int timeoutMilliseconds = 5000;
            var nodes = new FaultingNodeOperations
            {
                FailAfterChange = true,
                FailRemovalBeforeChange = true,
                PauseNextRemove = true
            };
            var executorEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executorCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = new Mock<ILadsProgramExecutor>();
            executor.Setup(value => value.ExecuteAsync(
                    It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()))
                .Returns(async (LadsProgramRun _, CancellationToken cancellationToken) =>
                {
                    executorEntered.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        executorCancelled.TrySetResult(true);
                        throw;
                    }
                });
            await using LadsHostedServer server = await StartAsync(
                nameof(QuarantineDrainsProgramStartAfterSchedulingBeforeShutdownAsync),
                new FaultingStore(),
                nodes,
                compose: builder => builder.Services.AddSingleton(executor.Object),
                extra: device => device.FunctionalUnit("Second", unit =>
                    unit.RunningOperations().Programs(_ => { }))).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit("Second");
            var programs = (LadsProgramManager)(unit.Programs ??
                throw new AssertionException("The program manager is missing."));
            await programs.AddTemplateAsync(Template("Start")).ConfigureAwait(false);
            MethodState startProgram = unit.Node.FunctionalUnitState!.StartProgram ??
                throw new AssertionException("The StartProgram method is missing.");
            BaseVariableState activeProgramId = programs.Node.ActiveProgram!.DeviceProgramRunId!;
            NodeStateChangedAsyncHandler? previousActiveProgram = activeProgramId.OnStateChangedAsync;
            activeProgramId.OnStateChangedAsync = (_, _, _, _) =>
            {
                startProgram.UpdateChangeMasks(NodeStateChangeMasks.NonValue);
                return default;
            };
            NodeStateChangedAsyncHandler? previous = startProgram.OnStateChangedAsync;
            var notificationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var shutdownObserved = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            startProgram.OnStateChangedAsync = async (_, _, _, _) =>
            {
                if (programs.ActiveRun == null)
                {
                    return;
                }
                await executorEntered.Task.WaitAsync(
                    TimeSpan.FromMilliseconds(timeoutMilliseconds), CancellationToken.None)
                    .ConfigureAwait(false);
                notificationEntered.TrySetResult(true);
                await releaseNotification.Task.WaitAsync(
                    TimeSpan.FromMilliseconds(timeoutMilliseconds), CancellationToken.None)
                    .ConfigureAwait(false);
            };
            Func<LadsDeviceStateValue, CancellationToken, ValueTask> deviceHandler = (state, _) =>
            {
                if (state == LadsDeviceStateValue.Shutdown)
                {
                    shutdownObserved.TrySetResult(
                        unit.Functional.Current == LadsFunctionalStateValue.Aborted &&
                        unit.Running.Current == LadsRunningStateValue.Idle &&
                        device.ItemState == MachineryItemStateValue.OutOfService);
                }
                return default;
            };
            device.DeviceState.StateChangedAsync += deviceHandler;

            Task? start = null;
            Task? quarantine = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                start = Task.Run(() => programs.StartProgramAsync("Start").AsTask());
                Task notificationStarted = await Task.WhenAny(
                    notificationEntered.Task,
                    Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(notificationStarted, Is.SameAs(notificationEntered.Task));
                await executorEntered.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false);

                quarantine = Task.Run(() => QuarantineAsync(device, nodes));
                await nodes.RemoveEntered.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false);
                nodes.ReleaseRemove.TrySetResult(true);
                await WaitForStorageQuarantineAsync(device, timeoutMilliseconds).ConfigureAwait(false);
                await executorCancelled.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false);

                Assert.That(start.IsCompleted, Is.False);
                Assert.That(quarantine.IsCompleted, Is.False);
                Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Operate));
                Assert.That(unit.Functional.Current, Is.EqualTo(LadsFunctionalStateValue.Running));

                releaseNotification.TrySetResult(true);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await start.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false));
                start = null;
                await quarantine.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
                quarantine = null;

                Assert.That(unit.Functional.Current, Is.EqualTo(LadsFunctionalStateValue.Aborted));
                Assert.That(unit.Running.Current, Is.EqualTo(LadsRunningStateValue.Idle));
                Assert.That(await shutdownObserved.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false), Is.True);
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                releaseNotification.TrySetResult(true);
                nodes.ReleaseRemove.TrySetResult(true);
                if (programs.HasActiveRun)
                {
                    try
                    {
                        await programs.EndActiveRunAsync(
                            LadsProgramOutcome.Aborted, CancellationToken.None).AsTask()
                            .WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
                var pending = new List<Task>();
                if (start != null)
                {
                    pending.Add(start);
                }
                if (quarantine != null)
                {
                    pending.Add(quarantine);
                }
                if (pending.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                            .ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        cleanupFailures.Add(failure);
                    }
                }
                startProgram.OnStateChangedAsync = previous;
                activeProgramId.OnStateChangedAsync = previousActiveProgram;
                device.DeviceState.StateChangedAsync -= deviceHandler;
            }

            if (testFailure != null && cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "The post-scheduling quarantine test and its cleanup both failed.",
                    [testFailure.SourceException, .. cleanupFailures]);
            }
            if (testFailure != null)
            {
                testFailure.Throw();
            }
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException("Post-scheduling quarantine cleanup failed.", cleanupFailures);
            }
        }

        /// <summary>
        /// Rejects both a provider's delayed acknowledgement and the command waiting behind that provider.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task QuarantineRejectsDeviceCommandsWaitingOnProviderAndGateAsync(bool modeFirst)
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true, FailRemovalBeforeChange = true };
            var provider = new Mock<ILadsDeviceProvider>();
            await using LadsHostedServer server = await StartAsync(
                nameof(QuarantineRejectsDeviceCommandsWaitingOnProviderAndGateAsync), new FaultingStore(), nodes,
                extra: device => device.Provider(provider.Object)).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            MachineryOperationModeValue originalMode = device.OperationMode;
            MachineryOperationModeValue target = originalMode == MachineryOperationModeValue.Setup
                ? MachineryOperationModeValue.Maintenance : MachineryOperationModeValue.Setup;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            async ValueTask<ServiceResult> PauseAsync()
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return ServiceResult.Good;
            }
            provider.Setup(value => value.OnOperationModeChangingAsync(
                device, target, It.IsAny<CancellationToken>())).Returns(PauseAsync);
            provider.Setup(value => value.OnDeviceStateChangingAsync(
                device, LadsDeviceStateValue.Sleep, It.IsAny<CancellationToken>())).Returns(PauseAsync);
            Task first = modeFirst
                ? device.SetOperationModeAsync(target).AsTask() : device.GotoSleepAsync().AsTask();
            Task? queued = null;
            Task? quarantine = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                queued = modeFirst
                    ? device.GotoSleepAsync().AsTask() : device.SetOperationModeAsync(target).AsTask();
                Assert.That(queued.IsCompleted, Is.False);
                quarantine = Task.Run(() => QuarantineAsync(device, nodes));
                await WaitForStorageQuarantineAsync(device, 30000).ConfigureAwait(false);
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(queued.IsCompleted, Is.False);
                if (!modeFirst)
                {
                    Assert.That(quarantine.IsCompleted, Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await AssertQuarantinedAsync(first).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (queued != null)
                        {
                            await AssertQuarantinedAsync(queued).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (quarantine != null)
                        {
                            await quarantine.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                        }
                    }
                }
            }
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
            Assert.That(device.OperationMode, Is.EqualTo(originalMode));
        }

        /// <summary>
        /// Rejects Clear after its unit gate becomes available on an already quarantined device.
        /// </summary>
        [Test]
        public async Task QuarantineRejectsClearAlreadyWaitingOnUnitGateAsync()
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true, FailRemovalBeforeChange = true };
            var store = new FaultingStore();
            await using LadsHostedServer server = await StartAsync(
                nameof(QuarantineRejectsClearAlreadyWaitingOnUnitGateAsync), store, nodes,
                extra: device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            await unit.Functional.SetStateAsync(LadsFunctionalStateValue.Aborted).ConfigureAwait(false);
            await unit.Gate.WaitAsync().ConfigureAwait(false);
            Task clear = unit.ClearAsync().AsTask();
            try
            {
                Assert.That(clear.IsCompleted, Is.False);
                await QuarantineAsync(device, nodes, "Second").ConfigureAwait(false);
            }
            finally
            {
                unit.Gate.Release();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await clear.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
        }

        /// <summary>
        /// Rejects retained values and maintenance records after their providers return into quarantine.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task QuarantineRejectsFunctionAndMaintenanceProviderCompletionAsync(bool maintenance)
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true, FailRemovalBeforeChange = true };
            var store = new FaultingStore();
            var provider = new Mock<ILadsDeviceProvider>();
            var functions = new Mock<ILadsFunctionProvider>();
            await using LadsHostedServer server = await StartAsync(
                nameof(QuarantineRejectsFunctionAndMaintenanceProviderCompletionAsync), store, nodes,
                extra: device => device.Provider(provider.Object)).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            unit.SetFunctionProvider(functions.Object);
            IAnalogControlFunction temperature = unit.GetFunction<IAnalogControlFunction>("Temperature");
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            await task.StartAsync().ConfigureAwait(false);
            double originalTarget = temperature.TargetValue;
            DateTimeUtc originalDate = task.LastExecutionDate;
            MaintenanceTaskResultEnum originalResult = task.LastResult;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            async ValueTask<ServiceResult> PauseAsync()
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return ServiceResult.Good;
            }
            provider.Setup(value => value.OnMaintenanceTaskAsync(
                task, LadsMaintenanceCommand.Stop, It.IsAny<CancellationToken>())).Returns(PauseAsync);
            functions.Setup(value => value.OnCommandAsync(
                It.IsAny<LadsFunctionCommand>(), It.IsAny<CancellationToken>())).Returns(PauseAsync);
            Task pending = maintenance ? task.StopAsync().AsTask()
                : temperature.SetTargetValueAsync(originalTarget + 1).AsTask();
            int saves = 0;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await QuarantineAsync(device, nodes).ConfigureAwait(false);
                saves = store.Saves;
            }
            finally
            {
                release.TrySetResult(true);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await pending.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
            }
            Assert.That(temperature.TargetValue, Is.EqualTo(originalTarget));
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));
            Assert.That(task.LastExecutionDate, Is.EqualTo(originalDate));
            Assert.That(task.LastResult, Is.EqualTo(originalResult));
            Assert.That(store.Saves, Is.EqualTo(saves));
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
        }

        /// <summary>
        /// Orders the actual state write after a synchronous transition guard against quarantine.
        /// </summary>
        [TestCase("device")]
        [TestCase("mode")]
        [TestCase("clear")]
        public async Task QuarantineRejectsStateCommitAfterTransitionGuardAsync(string command)
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true, FailRemovalBeforeChange = true };
            await using LadsHostedServer server = await StartAsync(
                nameof(QuarantineRejectsStateCommitAfterTransitionGuardAsync), new FaultingStore(), nodes,
                extra: device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            await unit.Functional.SetStateAsync(LadsFunctionalStateValue.Aborted).ConfigureAwait(false);
            MachineryOperationModeValue originalMode = device.OperationMode;
            MachineryOperationModeValue target = originalMode == MachineryOperationModeValue.Setup
                ? MachineryOperationModeValue.Maintenance : MachineryOperationModeValue.Setup;
            FiniteStateMachineState machine = command switch
            {
                "mode" => device.OperationModeMachine.Driver.StateMachine,
                "clear" => unit.Functional.Driver.StateMachine,
                _ => device.DeviceState.Driver.StateMachine
            };
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            machine.OnBeforeTransition = (_, _, _, _, _, _) =>
            {
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("The transition guard was not released.");
                }
                return ServiceResult.Good;
            };
            Task pending = Task.Run(async () =>
            {
                switch (command)
                {
                    case "mode":
                        await device.SetOperationModeAsync(target).ConfigureAwait(false);
                        break;
                    case "clear":
                        await unit.ClearAsync().ConfigureAwait(false);
                        break;
                    default:
                        await device.GotoSleepAsync().ConfigureAwait(false);
                        break;
                }
            });
            Task? quarantine = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                quarantine = Task.Run(() => QuarantineAsync(device, nodes, "Second"));
                await WaitForStorageQuarantineAsync(device, 30000).ConfigureAwait(false);
                Assert.That(pending.IsCompleted, Is.False);
            }
            finally
            {
                release.Set();
                try
                {
                    await Task.WhenAll(
                        AssertQuarantinedAsync(pending),
                        quarantine?.WaitAsync(TimeSpan.FromSeconds(30)) ?? Task.CompletedTask).ConfigureAwait(false);
                }
                finally
                {
                    machine.OnBeforeTransition = null;
                }
            }
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
            Assert.That(device.OperationMode, Is.EqualTo(originalMode));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
        }

        /// <summary>
        /// Allows a native node notification to coordinate a mutation on another thread.
        /// </summary>
        [TestCase("sensor")]
        [TestCase("current")]
        [TestCase("target")]
        [TestCase("enabled")]
        [TestCase("counter")]
        [TestCase("state")]
        [TestCase("maintenance")]
        [TestCase("maintenance-condition")]
        [TestCase("quarantine")]
        public async Task NativeNotificationsPermitCrossThreadDeviceMutationAsync(string change)
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true, FailRemovalBeforeChange = true };
            await using LadsHostedServer server = await StartAsync(
                nameof(NativeNotificationsPermitCrossThreadDeviceMutationAsync),
                new FaultingStore(), nodes).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            var temperature = (LadsAnalogControlFunction)unit.GetFunction<IAnalogControlFunction>("Temperature");
            var sensor = (LadsAnalogScalarSensorFunction)unit.GetFunction<IAnalogScalarSensorFunction>("CO2");
            var maintenance = (LadsMaintenanceTask)device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            if (change == "maintenance-condition")
            {
                await maintenance.StartAsync().ConfigureAwait(false);
            }
            NodeState observed = change switch
            {
                "sensor" => ((AnalogScalarSensorFunctionState)sensor.Node).SensorValue!,
                "current" => ((AnalogControlFunctionState)temperature.Node).CurrentValue!,
                "target" => ((AnalogControlFunctionState)temperature.Node).TargetValue!,
                "enabled" => temperature.Node.IsEnabled!,
                "counter" => device.Counters.OperationCycleCounter!,
                "maintenance" => maintenance.Machine.Driver.StateMachine.CurrentState!.Id!,
                "maintenance-condition" => maintenance.Node.Severity!,
                _ => device.DeviceState.Driver.StateMachine.CurrentState!.Id!
            };
            using var finished = new ManualResetEventSlim();
            Exception? workerFailure = null;
            bool completedInsideNotification = false;
            int notifications = 0;
            var worker = new Thread(() =>
            {
                try
                {
                    device.AddOperationDuration(TimeSpan.FromMilliseconds(17));
                }
                catch (Exception failure)
                {
                    workerFailure = failure;
                }
                finally
                {
                    finished.Set();
                }
            })
            {
                IsBackground = true
            };
            double originalDuration = device.OperationDuration.TotalMilliseconds;
            observed.OnStateChanged = (_, _, _) =>
            {
                if (Interlocked.Increment(ref notifications) == 1)
                {
                    worker.Start();
                    completedInsideNotification = finished.Wait(TimeSpan.FromSeconds(10));
                }
            };
            try
            {
                switch (change)
                {
                    case "sensor":
                        sensor.UpdateValue(sensor.SensorValue + 1);
                        break;
                    case "current":
                        temperature.UpdateCurrentValue(temperature.CurrentValue + 1);
                        break;
                    case "target":
                        await temperature.SetTargetValueAsync(temperature.TargetValue + 1).ConfigureAwait(false);
                        break;
                    case "enabled":
                        temperature.IsEnabled = false;
                        break;
                    case "counter":
                        device.IncrementOperationCycles();
                        break;
                    case "maintenance":
                        await maintenance.StartAsync().ConfigureAwait(false);
                        break;
                    case "maintenance-condition":
                        await maintenance.StopAsync().ConfigureAwait(false);
                        break;
                    case "quarantine":
                        await QuarantineAsync(device, nodes).ConfigureAwait(false);
                        break;
                    default:
                        await device.GotoSleepAsync().ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                observed.OnStateChanged = null;
                if (Volatile.Read(ref notifications) != 0)
                {
                    Assert.That(worker.Join(TimeSpan.FromSeconds(30)), Is.True);
                }
            }
            Assert.That(notifications, Is.GreaterThan(0));
            Assert.That(completedInsideNotification, Is.True,
                "The callback could not complete a mutation on another thread.");
            if (change == "quarantine")
            {
                Assert.That(workerFailure, Is.TypeOf<InvalidOperationException>());
                Assert.That(workerFailure!.InnerException, Is.Not.Null);
                Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
                Assert.That(device.OperationDuration.TotalMilliseconds, Is.EqualTo(originalDuration));
            }
            else
            {
                Assert.That(workerFailure, Is.Null);
                Assert.That(device.OperationDuration.TotalMilliseconds, Is.EqualTo(originalDuration + 17));
            }
        }

        /// <summary>
        /// Does not rewind totalizer timing when clock samples are committed in a different order.
        /// </summary>
        [Test]
        public async Task ReorderedClockSamplesDoNotSubtractOrDuplicateTotalizedTimeAsync()
        {
            await using LadsHostedServer server = await StartAsync(
                nameof(ReorderedClockSamplesDoNotSubtractOrDuplicateTotalizedTimeAsync),
                new FaultingStore(), new FaultingNodeOperations(),
                extra: device => device.FunctionalUnit("Meter", unit => unit.AnalogControl(
                    "Flow", options => options.Kind = LadsAnalogControlKind.Totalizer))).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit("Meter");
            var function = (LadsAnalogControlFunction)unit.GetFunction<IAnalogControlFunction>("Flow");
            await function.StartAsync().ConfigureAwait(false);
            long start = device.Runtime.TimeProvider.GetTimestamp();
            long frequency = device.Runtime.TimeProvider.TimestampFrequency;
            device.UpdateControlReading(function, 2, start + frequency, frequency);
            device.UpdateControlReading(function, 3, start + (3 * frequency), frequency);
            Assert.That(function.TotalizedValue, Is.EqualTo(4));
            device.UpdateControlReading(function, 5, start + (2 * frequency), frequency);
            Assert.That(function.CurrentValue, Is.EqualTo(5));
            Assert.That(function.TotalizedValue, Is.EqualTo(4));
            device.UpdateControlReading(function, 5, start + (4 * frequency), frequency);
            Assert.That(function.TotalizedValue, Is.EqualTo(9));
            device.ResetTotalizer(function, start + (2 * frequency));
            Assert.That(function.TotalizedValue, Is.Zero);
            device.UpdateControlReading(function, 5, start + (5 * frequency), frequency);
            Assert.That(function.TotalizedValue, Is.EqualTo(5));
            Assert.Throws<InvalidOperationException>(() =>
                device.UpdateControlReading(function, 99, start + (6 * frequency), 0));
            Assert.Throws<InvalidOperationException>(() =>
                device.UpdateControlReading(function, 99, start + (6 * frequency), -1));
            Assert.That(function.CurrentValue, Is.EqualTo(5));
            Assert.That(function.TotalizedValue, Is.EqualTo(5));
        }

        /// <summary>
        /// Drains owned execution during disposal without rewriting a quarantined unit's state.
        /// </summary>
        [Test]
        public async Task QuarantinedShutdownStillCancelsAndDrainsActiveExecutionAsync()
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true, FailRemovalBeforeChange = true };
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = new Mock<ILadsProgramExecutor>();
            executor.Setup(value => value.ExecuteAsync(It.IsAny<LadsProgramRun>(), It.IsAny<CancellationToken>()))
                .Returns(async (LadsProgramRun _, CancellationToken cancellationToken) =>
                {
                    entered.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        cancelled.TrySetResult(true);
                        throw;
                    }
                });
            await using LadsHostedServer server = await StartAsync(
                nameof(QuarantinedShutdownStillCancelsAndDrainsActiveExecutionAsync), new FaultingStore(), nodes,
                compose: builder => builder.Services.AddSingleton(executor.Object),
                extra: device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            string id = await unit.Programs!.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Task<ILadsResult> result = unit.Programs.WaitForResultAsync(id).AsTask();
            await QuarantineAsync(device, nodes, "Second").ConfigureAwait(false);
            await Assert.ThrowsAsync<AggregateException>(async () =>
                await device.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await result.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            ILadsFunctionalUnit[] ownedUnits = device.FunctionalUnits.ToArray() ??
                throw new AssertionException("The device has no functional-unit snapshot.");
            foreach (ILadsFunctionalUnit owned in ownedUnits)
            {
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await ((LadsFunctionalUnit)owned).Gate.WaitAsync().ConfigureAwait(false));
            }
        }

        /// <summary>
        /// Prevents another unit's checkpoint from being overwritten by a delayed rollback.
        /// </summary>
        [Test]
        public async Task CompensationKeepsDeviceStoreExclusiveAcrossOtherUnitsAsync()
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            var store = new FaultingStore();
            await using LadsHostedServer server = await StartAsync(
                nameof(CompensationKeepsDeviceStoreExclusiveAcrossOtherUnitsAsync), store, nodes,
                extra: device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager first = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            ILadsProgramManager second = device.GetFunctionalUnit("Second").Programs!;
            nodes.FailNextAdd = true;
            nodes.PauseNextRemove = true;
            Task rejected = first.AddTemplateAsync(Template("RolledBack")).AsTask();
            Task? accepted = null;
            try
            {
                await nodes.RemoveEntered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                int saves = store.Saves;
                accepted = second.AddTemplateAsync(Template("Retained")).AsTask();
                Assert.That(accepted.IsCompleted, Is.False);
                Assert.That(store.Saves, Is.EqualTo(saves));
                Assert.That(second.Templates.Count, Is.Zero);
            }
            finally
            {
                nodes.ReleaseRemove.TrySetResult(true);
                await Assert.ThrowsAsync<IOException>(async () =>
                    await rejected.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
                if (accepted != null)
                {
                    await accepted.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
            }
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.FunctionalUnits[0].Templates.ToList().Any(template => template.Id == "RolledBack"),
                Is.False);
            Assert.That(saved.FunctionalUnits[1].Templates[0].Id, Is.EqualTo("Retained"));
        }

        /// <summary>
        /// Rejects a queued transaction after failed storage compensation records quarantine.
        /// </summary>
        [Test]
        public async Task FailedStorageCompensationRejectsQueuedProgramTransactionAsync()
        {
            const int timeoutMilliseconds = 5000;
            int observingThread = 0;
            bool templatePrepared = false;
            var clock = new Mock<TimeProvider> { CallBase = true };
            clock.Setup(time => time.GetUtcNow()).Returns(() =>
            {
                if (Volatile.Read(ref observingThread) == Environment.CurrentManagedThreadId)
                {
                    templatePrepared = true;
                }
                return DateTimeOffset.UtcNow;
            });
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            var store = new FaultingStore();
            await using LadsHostedServer server = await StartAsync(
                nameof(FailedStorageCompensationRejectsQueuedProgramTransactionAsync), store, nodes,
                extra: device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })),
                timeProvider: clock.Object)
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            LadsProgramManager first = (LadsProgramManager)device
                .GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            var secondUnit = (LadsFunctionalUnit)device.GetFunctionalUnit("Second");
            var second = (LadsProgramManager)(secondUnit.Programs ??
                throw new AssertionException("The second program manager is missing."));
            nodes.FailNextAdd = true;
            nodes.FailNextRemove = true;
            int failedRestoreSave = store.Saves + 2;
            store.FailSaveNumber = failedRestoreSave;
            store.PauseSaveNumber = failedRestoreSave;

            Task? rejected = null;
            Task? queued = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                rejected = Task.Run(() => first.AddTemplateAsync(Template("RolledBack")).AsTask());
                await store.SaveEntered.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false);
                LadsProgramTemplate template = Template("Rejected");
                ValueTask pending;
                Volatile.Write(ref observingThread, Environment.CurrentManagedThreadId);
                try
                {
                    pending = second.AddTemplateAsync(template);
                }
                finally
                {
                    Volatile.Write(ref observingThread, 0);
                }
                bool completedSynchronously = pending.IsCompleted;
                queued = pending.AsTask();
                Assert.That(templatePrepared, Is.True);
                // After FillTemplate, the store gate is the only await that can suspend this direct call.
                Assert.That(completedSynchronously, Is.False);
                Assert.That(queued.IsCompleted, Is.False);
                Assert.That(store.Saves, Is.EqualTo(failedRestoreSave));
                store.ReleaseSave.TrySetResult(true);

                await Assert.ThrowsAsync<AggregateException>(async () =>
                    await rejected.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false));
                rejected = null;
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await queued.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false));
                queued = null;

                Assert.That(store.Saves, Is.EqualTo(failedRestoreSave));
                Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
                Assert.That(second.Templates.Count, Is.Zero);
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                store.ReleaseSave.TrySetResult(true);
                foreach (Task? pending in new[] { rejected, queued })
                {
                    if (pending != null)
                    {
                        try
                        {
                            await pending.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                                .ConfigureAwait(false);
                        }
                        catch (Exception failure)
                        {
                            cleanupFailures.Add(failure);
                        }
                    }
                }
            }

            if (testFailure != null && cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "The storage quarantine test and its cleanup both failed.",
                    [testFailure.SourceException, .. cleanupFailures]);
            }
            if (testFailure != null)
            {
                testFailure.Throw();
            }
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException("Storage quarantine cleanup failed.", cleanupFailures);
            }
        }

        /// <summary>
        /// Rejects colliding explicit and fallback keys without accessing the provider.
        /// </summary>
        [TestCase("explicit")]
        [TestCase("fallback")]
        [TestCase("reverse-fallback")]
        public async Task CollidingIdentitiesAreRejectedBeforeAnyStoreAccessAsync(string collision)
        {
            var store = new FaultingStore();
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(CollidingIdentitiesAreRejectedBeforeAnyStoreAccessAsync),
                builder => builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                {
                    setup = context;
                    LadsIncubator.Configure(context, LadsIncubator.DeviceName, device =>
                    {
                        if (collision != "fallback")
                        {
                            device.Identification(id => id.ProductInstanceUri = collision == "reverse-fallback"
                                ? "urn:opcua-netstandard:lads:instances#Second" : "urn:tests:lads:shared");
                        }
                    });
                })).ConfigureAwait(false);
            var first = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager programs = first.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            await programs.AddTemplateAsync(Template("OwnedByFirst")).ConfigureAwait(false);
            int loads = store.Loads;
            int saves = store.Saves;
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await setup!.AddDeviceAsync("Second", device =>
                {
                    if (collision != "reverse-fallback")
                    {
                        device.Identification(id => id.ProductInstanceUri = first.StoreIdentity);
                    }
                }).ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(store.Loads, Is.EqualTo(loads));
            Assert.That(store.Saves, Is.EqualTo(saves));
            LadsStoredDeviceData saved = (await store.LoadAsync(first.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.FunctionalUnits[0].Templates.ToList().Any(template => template.Id == "OwnedByFirst"),
                Is.True);
            ILadsDevice replacement = await setup!.AddDeviceAsync("Second", device => device.Identification(id =>
                id.ProductInstanceUri = "urn:tests:lads:second")).ConfigureAwait(false);
            Assert.That(replacement.Name, Is.EqualTo("Second"));
        }

        /// <summary>
        /// Makes failed registration reservations available to a corrected retry.
        /// </summary>
        [TestCase("load")]
        [TestCase("save")]
        [TestCase("add")]
        [TestCase("add-cleanup")]
        public async Task FailedRegistrationReleasesNameAndStorageIdentityAsync(string operation)
        {
            var store = new FaultingStore();
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(FailedRegistrationReleasesNameAndStorageIdentityAsync),
                builder =>
                {
                    builder.Services.AddSingleton<ILadsNodeOperations>(nodes);
                    builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                    {
                        setup = context;
                        LadsIncubator.Configure(context);
                    });
                }).ConfigureAwait(false);
            store.FailNextLoad = operation == "load";
            store.FailSaveNumber = operation == "save" ? store.Saves + 1 : 0;
            nodes.FailNextAdd = operation is "add" or "add-cleanup";
            nodes.FailNextRemove = operation == "add-cleanup";
            Exception? failure = await Assert.CatchAsync<Exception>(async () =>
                await setup!.AddDeviceAsync("Retry", device => device.Identification(id =>
                    id.ProductInstanceUri = "urn:tests:lads:retry")).ConfigureAwait(false));
            string[] messages = failure is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions.Select(error => error.Message).ToArray()
                : [failure!.Message];
            Assert.That(messages, Does.Contain(operation switch
            {
                "load" => "Store load failed.",
                "save" => "Store save failed.",
                _ => "Node add failed."
            }));
            if (operation == "add-cleanup")
            {
                Assert.That(messages, Does.Contain("Node remove failed."));
            }
            ILadsDevice retry = await setup!.AddDeviceAsync("Retry", device => device.Identification(id =>
                id.ProductInstanceUri = "urn:tests:lads:retry")).ConfigureAwait(false);
            Assert.That(retry.Name, Is.EqualTo("Retry"));
            Assert.That(retry.State, Is.EqualTo(LadsDeviceStateValue.Operate));
        }

        /// <summary>
        /// Retains both reservations when add registered nodes but cleanup failed before deleting them.
        /// </summary>
        [Test]
        public async Task FailedRegistrationWithOrphanedNodesRetainsNameAndStorageIdentityAsync()
        {
            var store = new FaultingStore();
            var nodes = new FaultingNodeOperations
            {
                FailAfterChange = true,
                FailRemovalBeforeChange = true
            };
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(FailedRegistrationWithOrphanedNodesRetainsNameAndStorageIdentityAsync),
                builder =>
                {
                    builder.Services.AddSingleton<ILadsNodeOperations>(nodes);
                    builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                    {
                        setup = context;
                        LadsIncubator.Configure(context);
                    });
                }).ConfigureAwait(false);
            nodes.FailNextAdd = true;
            nodes.FailNextRemove = true;
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await setup!.AddDeviceAsync("Orphan", device => device.Identification(id =>
                    id.ProductInstanceUri = "urn:tests:lads:orphan")).ConfigureAwait(false));
            Assert.That(failure!.Flatten().InnerExceptions.Select(error => error.Message),
                Does.Contain("Node add failed.").And.Contain("Node remove failed."));
            Assert.That(nodes.LastAdded, Is.Not.Null);
            var first = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            Assert.That(first.Runtime.Manager.FindPredefinedNode(nodes.LastAdded!.NodeId),
                Is.SameAs(nodes.LastAdded));
            int loads = store.Loads;
            int saves = store.Saves;
            ServiceResultException? duplicateName = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await setup!.AddDeviceAsync("Orphan", _ => { }).ConfigureAwait(false));
            Assert.That(duplicateName!.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            ServiceResultException? duplicateIdentity = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await setup!.AddDeviceAsync("Different", device => device.Identification(id =>
                    id.ProductInstanceUri = "urn:tests:lads:orphan")).ConfigureAwait(false));
            Assert.That(duplicateIdentity!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(store.Loads, Is.EqualTo(loads));
            Assert.That(store.Saves, Is.EqualTo(saves));
            ILadsDevice independent = await setup!.AddDeviceAsync("Different", _ => { }).ConfigureAwait(false);
            Assert.That(independent.Name, Is.EqualTo("Different"));
        }

        /// <summary>
        /// Reports checkpoint and disposal failures while attempting every remaining owner exactly once.
        /// </summary>
        [Test]
        public async Task ShutdownPreservesCheckpointAndBindingFailuresAndReleasesEveryOwnerAsync()
        {
            var store = new FaultingStore();
            var binder = new FailingFileBinder(1, 3, 5, 8);
            await using LadsHostedServer server = await StartAsync(
                nameof(ShutdownPreservesCheckpointAndBindingFailuresAndReleasesEveryOwnerAsync), store,
                new FaultingNodeOperations(),
                builder =>
                {
                    builder.Services.AddSingleton<IFileTypeBinder>(binder);
                    builder.Services.AddSingleton<ILadsProgramExecutor>(new ResultFileExecutor());
                },
                device => device.FunctionalUnit("Second", unit => unit.Programs(programs =>
                    programs.Template("Run", template => template.Step("Step", TimeSpan.Zero)))))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit[] ownedUnits = device.FunctionalUnits.ToArray() ??
                throw new AssertionException("The device has no functional-unit snapshot.");
            foreach (ILadsFunctionalUnit unit in ownedUnits)
            {
                ILadsProgramManager programs = unit.Programs!;
                for (int index = 0; index < (unit.Name == "Second" ? 1 : 2); index++)
                {
                    string id = await programs.StartProgramAsync(
                        unit.Name == "Second" ? "Run" : LadsIncubator.QuickTemplate).ConfigureAwait(false);
                    await programs.WaitForResultAsync(id).ConfigureAwait(false);
                }
            }
            Assert.That(binder.Bindings, Is.EqualTo(9));
            store.FailAllSaves = true;
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await device.DisposeAsync().ConfigureAwait(false));
            string[] messages = failure!.Flatten().InnerExceptions.Select(error => error.Message).ToArray();
            Assert.That(messages, Does.Contain("Store save failed."));
            Assert.That(messages, Does.Contain("Binding 1 cleanup failed."));
            Assert.That(messages, Does.Contain("Binding 3 cleanup failed."));
            Assert.That(messages, Does.Contain("Binding 5 cleanup failed."));
            Assert.That(messages, Does.Contain("Binding 8 cleanup failed."));
            Assert.That(binder.Releases, Is.EqualTo(binder.Bindings));
            foreach (ILadsFunctionalUnit unit in ownedUnits)
            {
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await ((LadsFunctionalUnit)unit).Gate.WaitAsync().ConfigureAwait(false));
            }
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await device.GetMaintenanceTask(LadsIncubator.MaintenanceName).StartAsync().ConfigureAwait(false));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await device.SaveStoredDataAsync().ConfigureAwait(false));
            int saves = store.Saves;
            AggregateException? repeated = await Assert.ThrowsAsync<AggregateException>(async () =>
                await device.DisposeAsync().ConfigureAwait(false));
            Assert.That(repeated, Is.SameAs(failure));
            Assert.That(binder.Releases, Is.EqualTo(binder.Bindings));
            Assert.That(store.Saves, Is.EqualTo(saves));
        }

        /// <summary>
        /// Releases every earlier file binding if a later bind fails, preserving cleanup errors and allowing retry.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ResultBindingFailureReleasesPartialFilesAndAllowsRetryAsync(bool restore, bool failCleanup)
        {
            var store = new FaultingStore();
            var binder = new FailingFileBinder(failCleanup ? [1, 2] : [])
            {
                FailBindingNumber = 3
            };
            await using LadsHostedServer server = await StartAsync(
                nameof(ResultBindingFailureReleasesPartialFilesAndAllowsRetryAsync), store,
                new FaultingNodeOperations(), builder =>
                {
                    builder.Services.AddSingleton<IFileTypeBinder>(binder);
                    builder.Services.AddSingleton<ILadsProgramExecutor>(new ResultFileExecutor());
                }).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            await device.SaveStoredDataAsync().ConfigureAwait(false);
            Exception? failure;
            if (restore)
            {
                var stored = new LadsStoredFunctionalUnitData
                {
                    Name = unit.Name,
                    Results =
                    [
                        new LadsStoredResultData
                        {
                            RunId = "Restored",
                            Template = programs.CaptureStoredData().Templates[0],
                            Outcome = LadsProgramOutcome.Completed,
                            Files =
                            [
                                new LadsResultFile("First.bin", "application/octet-stream", ByteString.From(1)),
                                new LadsResultFile("Second.bin", "application/octet-stream", ByteString.From(2)),
                                new LadsResultFile("Third.bin", "application/octet-stream", ByteString.From(3))
                            ]
                        }
                    ]
                };
                failure = await Assert.CatchAsync<Exception>(async () =>
                    await programs.RestoreStoredDataAsync(stored, CancellationToken.None).ConfigureAwait(false));
            }
            else
            {
                string id = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
                failure = await Assert.CatchAsync<Exception>(async () =>
                    await programs.WaitForResultAsync(id).ConfigureAwait(false));
            }
            Assert.That(failure, Is.Not.Null);
            if (failCleanup)
            {
                Assert.That(failure, Is.TypeOf<AggregateException>());
                string[] messages = ((AggregateException)failure!).Flatten().InnerExceptions
                    .Select(error => error.Message).ToArray();
                Assert.That(messages, Does.Contain("File bind failed."));
                Assert.That(messages, Does.Contain("Binding 1 cleanup failed."));
                Assert.That(messages, Does.Contain("Binding 2 cleanup failed."));
                Assert.That(messages, Has.Length.EqualTo(3));
            }
            else
            {
                Assert.That(failure, Is.TypeOf<IOException>());
                Assert.That(failure!.Message, Is.EqualTo("File bind failed."));
            }
            Assert.That(binder.Bindings, Is.EqualTo(2));
            Assert.That(binder.Releases, Is.EqualTo(2));
            Assert.That(programs.Results.Count, Is.Zero);
            Assert.That(device.OperationCycleCounter, Is.Zero);
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Operate));
            var children = new List<BaseInstanceState>();
            programs.Node.ResultSet!.GetChildren(device.Runtime.Context, children);
            Assert.That(children.OfType<ResultState>(), Is.Empty);
            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That(children[0], Is.SameAs(programs.Node.ResultSet.NodeVersion));
            Assert.That(device.Runtime.Manager.FindPredefinedNode(programs.Node.ResultSet.NodeVersion!.NodeId),
                Is.SameAs(programs.Node.ResultSet.NodeVersion));
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.OperationCycles, Is.Zero);
            Assert.That(saved.FunctionalUnits[0].Results.Count, Is.Zero);
            if (!restore)
            {
                await unit.ClearAsync().ConfigureAwait(false);
            }
            string retry = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(retry).ConfigureAwait(false);
            Assert.That(result.Files.Count, Is.EqualTo(3));
            Assert.That(result.Files[0].Content, Is.EqualTo(ByteString.From(1)));
            Assert.That(result.Files[1].Content, Is.EqualTo(ByteString.From(2)));
            Assert.That(result.Files[2].Content, Is.EqualTo(ByteString.From(3)));
            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
            Assert.That(binder.Bindings, Is.EqualTo(5));
            Assert.That(binder.Releases, Is.EqualTo(2));
        }

        /// <summary>
        /// Reports publication and candidate cleanup errors without losing either cause or preventing retry.
        /// </summary>
        [Test]
        public async Task ResultPublicationAndFileCleanupFailuresPreserveBothErrorsAndAllowRetryAsync()
        {
            var store = new FaultingStore();
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            var binder = new FailingFileBinder(1, 3);
            await using LadsHostedServer server = await StartAsync(
                nameof(ResultPublicationAndFileCleanupFailuresPreserveBothErrorsAndAllowRetryAsync),
                store, nodes, builder =>
                {
                    builder.Services.AddSingleton<IFileTypeBinder>(binder);
                    builder.Services.AddSingleton<ILadsProgramExecutor>(new ResultFileExecutor());
                }).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            nodes.FailNextAdd = true;
            string id = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await programs.WaitForResultAsync(id).ConfigureAwait(false));
            string[] messages = failure!.Flatten().InnerExceptions.Select(error => error.Message).ToArray();
            Assert.That(messages, Does.Contain("Node add failed."));
            Assert.That(messages, Does.Contain("Binding 1 cleanup failed."));
            Assert.That(messages, Does.Contain("Binding 3 cleanup failed."));
            Assert.That(binder.Bindings, Is.EqualTo(3));
            Assert.That(binder.Releases, Is.EqualTo(3));
            Assert.That(device.OperationCycleCounter, Is.Zero);
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Operate));
            Assert.That(programs.Results.Count, Is.Zero);
            var children = new List<BaseInstanceState>();
            programs.Node.ResultSet!.GetChildren(device.Runtime.Context, children);
            Assert.That(children.OfType<ResultState>(), Is.Empty);
            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That(children[0], Is.SameAs(programs.Node.ResultSet.NodeVersion));
            Assert.That(device.Runtime.Manager.FindPredefinedNode(programs.Node.ResultSet.NodeVersion!.NodeId),
                Is.SameAs(programs.Node.ResultSet.NodeVersion));
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.OperationCycles, Is.Zero);
            Assert.That(saved.FunctionalUnits[0].Results.Count, Is.Zero);
            await unit.ClearAsync().ConfigureAwait(false);
            string retry = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await programs.WaitForResultAsync(retry).ConfigureAwait(false);
            Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
            Assert.That(programs.Results.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Creates a zero-duration template without physical side effects.
        /// </summary>
        private static LadsProgramTemplate Template(string id)
        {
            return new LadsProgramTemplate(id).Step("Step", TimeSpan.Zero);
        }

        /// <summary>
        /// Leaves a registered template behind when compensating a real node add failure.
        /// </summary>
        private static async Task QuarantineAsync(
            LadsDevice device, FaultingNodeOperations nodes, string unitName = LadsIncubator.UnitName)
        {
            nodes.FailNextAdd = true;
            nodes.FailNextRemove = true;
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await device.GetFunctionalUnit(unitName).Programs!.AddTemplateAsync(Template("Uncertain"))
                    .ConfigureAwait(false));
            Assert.That(failure!.Flatten().InnerExceptions.Select(error => error.Message),
                Does.Contain("Node add failed.").And.Contain("Node remove failed."));
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
        }

        /// <summary>
        /// Observes a command's quarantine rejection with a bounded wait.
        /// </summary>
        private static async Task AssertQuarantinedAsync(Task command)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await command.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <summary>
        /// Waits until a failed compensation has made the device reject stored-data operations.
        /// </summary>
        private static async Task WaitForStorageQuarantineAsync(LadsDevice device, int timeoutMilliseconds)
        {
            for (int attempt = 0; attempt < timeoutMilliseconds; attempt++)
            {
                try
                {
                    device.RequireStorageHealthy();
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                await Task.Delay(1).ConfigureAwait(false);
            }
            throw new TimeoutException("The failed compensation did not quarantine the device in time.");
        }

        /// <summary>
        /// Checks both browse containment and node-manager registration.
        /// </summary>
        private static void AssertTemplateNode(
            LadsDevice device, LadsProgramManager programs, string id, bool present)
        {
            var children = new List<BaseInstanceState>();
            programs.Node.ProgramTemplateSet!.GetChildren(device.Runtime.Context, children);
            BaseInstanceState[] matches = children.Where(child => child.BrowseName.Name == id).ToArray();
            Assert.That(matches, Has.Length.EqualTo(present ? 1 : 0));
            if (present)
            {
                AssertSubtreeRegistration(device, matches[0], present: true);
            }
        }

        /// <summary>
        /// Checks every generated slot, dynamic child and native node index of the retained handles.
        /// </summary>
        private static void AssertSubtreeRegistration(LadsDevice device, NodeState root, bool present)
        {
            var nodes = new List<NodeState> { root };
            for (int index = 0; index < nodes.Count; index++)
            {
                NodeState node = nodes[index];
                NodeState? indexed = device.Runtime.Manager.FindPredefinedNode(node.NodeId);
                Assert.That(ReferenceEquals(indexed, node), Is.EqualTo(present), node.BrowseName.ToString());
                var children = new List<BaseInstanceState>();
                node.GetChildren(device.Runtime.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    Assert.That(child.Parent, Is.SameAs(node));
                }
                nodes.AddRange(children);
            }
            Assert.That(nodes, Has.Count.GreaterThan(1));
        }

        /// <summary>
        /// Starts a real server with deterministic storage and node-operation failure seams.
        /// </summary>
        private static Task<LadsHostedServer> StartAsync(
            string name,
            FaultingStore store,
            FaultingNodeOperations nodes,
            Action<IOpcUaServerBuilder>? compose = null,
            Action<ILadsDeviceBuilder>? extra = null,
            TimeProvider? timeProvider = null)
        {
            return LadsHostedServer.StartAsync(name, builder =>
            {
                builder.Services.AddSingleton<ILadsNodeOperations>(nodes);
                compose?.Invoke(builder);
                builder.AddLads(options =>
                {
                    options.Store = store;
                    if (timeProvider != null)
                    {
                        options.TimeProvider = timeProvider;
                    }
                }).ConfigureLads(context =>
                    LadsIncubator.Configure(context, LadsIncubator.DeviceName, extra));
            });
        }

        /// <summary>
        /// Produces multiple owned file bindings without performing physical work.
        /// </summary>
        private sealed class ResultFileExecutor : ILadsProgramExecutor
        {
            /// <inheritdoc/>
            public ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                run.AddResultFile("First.bin", "application/octet-stream", ByteString.From(1));
                run.AddResultFile("Second.bin", "application/octet-stream", ByteString.From(2));
                run.AddResultFile("Third.bin", "application/octet-stream", ByteString.From(3));
                return default;
            }
        }

        /// <summary>
        /// Reports selected disposal failures after releasing the real bindings.
        /// </summary>
        private sealed class FailingFileBinder : IFileTypeBinder
        {
            /// <summary>
            /// Creates a binder that rejects the listed one-based disposal calls.
            /// </summary>
            public FailingFileBinder(params int[] failures)
            {
                m_failures = failures;
            }

            /// <summary>
            /// Gets the number of bindings created.
            /// </summary>
            public int Bindings { get; private set; }

            /// <summary>
            /// Gets the number of bindings released.
            /// </summary>
            public int Releases => Volatile.Read(ref m_releases);

            /// <summary>
            /// Gets or sets the one-based next binding to reject before creating it; the fault is consumed once.
            /// </summary>
            public int FailBindingNumber { get; set; }

            /// <inheritdoc/>
            public IFileTypeBinding Bind(
                FileState file,
                ISystemContext context,
                ByteString content,
                Func<ByteString, CancellationToken, ValueTask>? commit = null,
                bool commitOnClose = false,
                NodeId ownerSession = default,
                int maximumBytes = 16 * 1024 * 1024)
            {
                if (Bindings + 1 == FailBindingNumber)
                {
                    FailBindingNumber = 0;
                    throw new IOException("File bind failed.");
                }
                IFileTypeBinding inner = m_inner.Bind(
                    file, context, content, commit, commitOnClose, ownerSession, maximumBytes);
                int number = ++Bindings;
                var binding = new Mock<IFileTypeBinding>();
                binding.SetupGet(value => value.File).Returns(inner.File);
                binding.Setup(value => value.DisposeAsync()).Returns(async () =>
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                    Interlocked.Increment(ref m_releases);
                    if (m_failures.Contains(number))
                    {
                        throw new IOException("Binding " + number + " cleanup failed.");
                    }
                });
                return binding.Object;
            }

            /// <summary>The real file binder used before injecting disposal errors.</summary>
            private readonly FileTypeBinder m_inner = new();

            /// <summary>The selected disposal calls that report errors.</summary>
            private readonly int[] m_failures;

            /// <summary>The number of bindings whose real disposal completed.</summary>
            private int m_releases;
        }

        /// <summary>
        /// Injects failures immediately before or after actual node-manager operations.
        /// </summary>
        private sealed class FaultingNodeOperations : ILadsNodeOperations
        {
            /// <summary>
            /// Gets the last subtree passed to the actual registration path.
            /// </summary>
            public BaseInstanceState? LastAdded { get; private set; }

            /// <summary>
            /// Gets or sets whether to fail the next add.
            /// </summary>
            public bool FailNextAdd { get; set; }

            /// <summary>
            /// Gets or sets whether to fail the next removal.
            /// </summary>
            public bool FailNextRemove { get; set; }

            /// <summary>
            /// Gets or sets whether the real operation completes before the injected error.
            /// </summary>
            public bool FailAfterChange { get; set; }

            /// <summary>
            /// Gets or sets whether a failing removal leaves the real subtree registered.
            /// </summary>
            public bool FailRemovalBeforeChange { get; set; }

            /// <summary>
            /// Gets or sets whether the next removal waits for explicit release.
            /// </summary>
            public bool PauseNextRemove { get; set; }

            /// <summary>
            /// Gets the notification that compensation has entered.
            /// </summary>
            public TaskCompletionSource<bool> RemoveEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the release signal for a paused compensation.
            /// </summary>
            public TaskCompletionSource<bool> ReleaseRemove { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public async ValueTask AddAsync(
                DiNodeManager manager, BaseInstanceState node, CancellationToken cancellationToken)
            {
                LastAdded = node;
                bool fail = FailNextAdd;
                FailNextAdd = false;
                if (!fail || FailAfterChange)
                {
                    await m_inner.AddAsync(manager, node, cancellationToken).ConfigureAwait(false);
                }
                if (fail)
                {
                    throw new IOException("Node add failed.");
                }
            }

            /// <inheritdoc/>
            public async ValueTask RemoveAsync(
                DiNodeManager manager, BaseInstanceState node, CancellationToken cancellationToken)
            {
                if (PauseNextRemove)
                {
                    PauseNextRemove = false;
                    RemoveEntered.TrySetResult(true);
                    await ReleaseRemove.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                bool fail = FailNextRemove;
                FailNextRemove = false;
                if (!fail || (FailAfterChange && !FailRemovalBeforeChange))
                {
                    await m_inner.RemoveAsync(manager, node, cancellationToken).ConfigureAwait(false);
                }
                if (fail)
                {
                    throw new IOException("Node remove failed.");
                }
            }

            /// <summary>The actual node-manager operation dispatcher.</summary>
            private readonly LadsNodeOperations m_inner = new();
        }

        /// <summary>
        /// Counts actual store calls and rejects selected loads or saves.
        /// </summary>
        private sealed class FaultingStore : ILadsStore
        {
            /// <summary>
            /// Gets the number of load calls.
            /// </summary>
            public int Loads { get; private set; }

            /// <summary>
            /// Gets the number of save calls.
            /// </summary>
            public int Saves { get; private set; }

            /// <summary>
            /// Gets or sets whether the next load is rejected.
            /// </summary>
            public bool FailNextLoad { get; set; }

            /// <summary>
            /// Gets or sets the one-based save call to reject.
            /// </summary>
            public int FailSaveNumber { get; set; }

            /// <summary>
            /// Gets or sets the one-based save call to pause before it succeeds or fails.
            /// </summary>
            public int PauseSaveNumber { get; set; }

            /// <summary>
            /// Gets or sets whether every checkpoint is rejected.
            /// </summary>
            public bool FailAllSaves { get; set; }

            /// <summary>
            /// Gets the notification that a selected save has paused.
            /// </summary>
            public TaskCompletionSource<bool> SaveEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the release signal for a selected paused save.
            /// </summary>
            public TaskCompletionSource<bool> ReleaseSave { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public ValueTask<LadsStoredDeviceData?> LoadAsync(
                string deviceIdentity, CancellationToken cancellationToken = default)
            {
                Loads++;
                if (FailNextLoad)
                {
                    FailNextLoad = false;
                    throw new IOException("Store load failed.");
                }
                return m_inner.LoadAsync(deviceIdentity, cancellationToken);
            }

            /// <inheritdoc/>
            public async ValueTask SaveAsync(
                string deviceIdentity, LadsStoredDeviceData data, CancellationToken cancellationToken = default)
            {
                Saves++;
                if (Saves == PauseSaveNumber)
                {
                    SaveEntered.TrySetResult(true);
                    await ReleaseSave.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                if (FailAllSaves || Saves == FailSaveNumber)
                {
                    throw new IOException("Store save failed.");
                }
                await m_inner.SaveAsync(deviceIdentity, data, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public ValueTask DeleteAsync(string deviceIdentity, CancellationToken cancellationToken = default)
            {
                return m_inner.DeleteAsync(deviceIdentity, cancellationToken);
            }

            /// <summary>The retained snapshots underlying injected provider failures.</summary>
            private readonly InMemoryLadsStore m_inner = new();
        }
    }
}
