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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Integration;
using Opc.Ua.Machinery.Server;
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
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                queued = modeFirst
                    ? device.GotoSleepAsync().AsTask() : device.SetOperationModeAsync(target).AsTask();
                Assert.That(queued.IsCompleted, Is.False);
                await QuarantineAsync(device, nodes).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await first.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
                if (queued != null)
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await queued.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
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
            unit.Functional.SetState(LadsFunctionalStateValue.Aborted);
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
            unit.Functional.SetState(LadsFunctionalStateValue.Aborted);
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
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await QuarantineAsync(device, nodes, "Second").ConfigureAwait(false);
            }
            finally
            {
                release.Set();
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await pending.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
                machine.OnBeforeTransition = null;
            }
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
            Assert.That(device.OperationMode, Is.EqualTo(originalMode));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
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
            foreach (ILadsFunctionalUnit owned in device.FunctionalUnits.ToArray())
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
            foreach (ILadsFunctionalUnit unit in device.FunctionalUnits.ToArray())
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
            foreach (ILadsFunctionalUnit unit in device.FunctionalUnits.ToArray())
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
            Assert.That(children, Is.Empty);
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
        /// Checks both browse containment and node-manager registration.
        /// </summary>
        private static void AssertTemplateNode(
            LadsDevice device, LadsProgramManager programs, string id, bool present)
        {
            var children = new List<BaseInstanceState>();
            programs.Node.ProgramTemplateSet!.GetChildren(device.Runtime.Context, children);
            BaseInstanceState[] matches = children.Where(child => child.BrowseName.Name == id).ToArray();
            Assert.That(matches.Length, Is.EqualTo(present ? 1 : 0));
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
            Assert.That(nodes.Count, Is.GreaterThan(1));
        }

        /// <summary>
        /// Starts a real server with deterministic storage and node-operation failure seams.
        /// </summary>
        private static Task<LadsHostedServer> StartAsync(
            string name,
            FaultingStore store,
            FaultingNodeOperations nodes,
            Action<IOpcUaServerBuilder>? compose = null,
            Action<ILadsDeviceBuilder>? extra = null)
        {
            return LadsHostedServer.StartAsync(name, builder =>
            {
                builder.Services.AddSingleton<ILadsNodeOperations>(nodes);
                compose?.Invoke(builder);
                builder.AddLads(options => options.Store = store).ConfigureLads(context =>
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
            /// Gets or sets whether every checkpoint is rejected.
            /// </summary>
            public bool FailAllSaves { get; set; }

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
            public ValueTask SaveAsync(
                string deviceIdentity, LadsStoredDeviceData data, CancellationToken cancellationToken = default)
            {
                Saves++;
                if (FailAllSaves || Saves == FailSaveNumber)
                {
                    throw new IOException("Store save failed.");
                }
                return m_inner.SaveAsync(deviceIdentity, data, cancellationToken);
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
