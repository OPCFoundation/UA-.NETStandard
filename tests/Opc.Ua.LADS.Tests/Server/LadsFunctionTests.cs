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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the functional unit state machine and the function handles.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsFunctionTests
    {
        private static readonly string[] s_composedNames = ["A", "B"];
        private static readonly string[] s_modes = ["Heat", "Cool"];
        private static readonly double[] s_initialSpectrum = [1.0, 2.0];
        private static readonly double[] s_spectrum = [3.0, 4.0, 5.0];

        [Test]
        public async Task FunctionalUnitMovesThroughItsStatesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(FunctionalUnitMovesThroughItsStatesAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            var states = new List<LadsFunctionalStateValue>();
            unit.StateChanged += states.Add;

            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(unit.Device, Is.SameAs(device));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.StopAsync())
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.AbortAsync())
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.ClearAsync())
                .ConfigureAwait(false);

            await unit.StartAsync().ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.StartAsync())
                .ConfigureAwait(false);
            await unit.StopAsync().ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));

            await unit.StartAsync().ConfigureAwait(false);
            await unit.AbortAsync().ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.StartAsync())
                .ConfigureAwait(false);
            await unit.ClearAsync().ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));

            Assert.That(
                states,
                Is.EqualTo(new[]
                {
                    LadsFunctionalStateValue.Running,
                    LadsFunctionalStateValue.Stopping,
                    LadsFunctionalStateValue.Stopped,
                    LadsFunctionalStateValue.Running,
                    LadsFunctionalStateValue.Aborting,
                    LadsFunctionalStateValue.Aborted,
                    LadsFunctionalStateValue.Clearing,
                    LadsFunctionalStateValue.Stopped
                }));
        }

        /// <summary>
        /// Verifies async functional-state writes update the Machinery item state and raise one unit event.
        /// </summary>
        [Test]
        public async Task AsyncStateWriteRaisesOnePublicEventAndRecomputesItemStateAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(AsyncStateWriteRaisesOnePublicEventAndRecomputesItemStateAsync)).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            var states = new List<LadsFunctionalStateValue>();
            int synchronousItemStateChanges = 0;
            int asynchronousItemStateChanges = 0;
            unit.StateChanged += states.Add;
            device.ItemStateMachine.StateChanged += _ => synchronousItemStateChanges++;
            device.ItemStateMachine.StateChangedAsync += (_, _) =>
            {
                asynchronousItemStateChanges++;
                return default;
            };

            await unit.Functional.SetStateAsync(LadsFunctionalStateValue.Running).ConfigureAwait(false);

            Assert.That(states, Is.EqualTo(new[] { LadsFunctionalStateValue.Running }));
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.Executing));
            Assert.That(synchronousItemStateChanges, Is.EqualTo(1));
            Assert.That(asynchronousItemStateChanges, Is.EqualTo(1));
        }

        /// <summary>
        /// Serializes cross-unit item-state writes with each pending item-state notification.
        /// </summary>
        [Test]
        public async Task CrossUnitItemStateNotificationsAreSerializedAsync()
        {
            const int timeoutMilliseconds = 5000;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(CrossUnitItemStateNotificationsAreSerializedAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var first = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            var second = (LadsFunctionalUnit)device.GetFunctionalUnit("Second");
            await first.StartAsync().ConfigureAwait(false);

            var notificationEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var observedState = new TaskCompletionSource<MachineryItemStateValue>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Func<MachineryItemStateValue, CancellationToken, ValueTask> handler = async (_, _) =>
            {
                if (device.ItemState == MachineryItemStateValue.NotExecuting)
                {
                    notificationEntered.TrySetResult(true);
                    await releaseNotification.Task.ConfigureAwait(false);
                    observedState.TrySetResult(device.ItemState);
                }
            };
            device.ItemStateMachine.StateChangedAsync += handler;

            Task? firstStop = null;
            Task? secondStart = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                firstStop = Task.Run(async () => await first.StopAsync().ConfigureAwait(false));
                Task notificationStarted = await Task.WhenAny(
                    notificationEntered.Task,
                    Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(notificationStarted, Is.SameAs(notificationEntered.Task));
                Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.NotExecuting));

                secondStart = Task.Run(async () => await second.StartAsync().ConfigureAwait(false));
                Task secondCompleted = await Task.WhenAny(
                    secondStart,
                    Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(secondCompleted, Is.Not.SameAs(secondStart));
                Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.NotExecuting));

                releaseNotification.TrySetResult(true);
                Task notificationCompleted = await Task.WhenAny(
                    observedState.Task,
                    Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(notificationCompleted, Is.SameAs(observedState.Task));
                Assert.That(await observedState.Task.ConfigureAwait(false),
                    Is.EqualTo(MachineryItemStateValue.NotExecuting));
                await secondStart.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.Executing));
                await firstStop.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
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
                if (firstStop != null)
                {
                    pending.Add(firstStop);
                }
                if (secondStart != null)
                {
                    pending.Add(secondStart);
                }
                if (pending.Count > 0)
                {
                    Task allPending = Task.WhenAll(pending);
                    try
                    {
                        Task drained = await Task.WhenAny(
                            allPending,
                            Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                        Assert.That(drained, Is.SameAs(allPending),
                            "Functional-unit state operations did not drain after notification release.");
                        await allPending.ConfigureAwait(false);
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
                    "The cross-unit notification test and its cleanup both failed.",
                    [testFailure.SourceException, .. cleanupFailures]);
            }
            if (testFailure != null)
            {
                testFailure.Throw();
            }
            if (cleanupFailures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
            }
            if (cleanupFailures.Count > 1)
            {
                throw new AggregateException("Cross-unit notification cleanup failed.", cleanupFailures);
            }
        }

        /// <summary>
        /// Prevents a functional unit from starting while Sleep or Shutdown is awaiting its device provider.
        /// </summary>
        [TestCase(LadsDeviceStateValue.Sleep)]
        [TestCase(LadsDeviceStateValue.Shutdown)]
        public async Task DeviceSleepOrShutdownAdmissionSerializesUnitStartAsync(LadsDeviceStateValue target)
        {
            const int timeoutMilliseconds = 5000;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(DeviceSleepOrShutdownAdmissionSerializesUnitStartAsync) + target,
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit("Second");
            var provider = new Mock<ILadsDeviceProvider>();
            device.SetProvider(provider.Object);

            var providerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseProvider = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startInvoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startStateChanged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            unit.StateChanged += _ => startStateChanged.TrySetResult(true);
            async ValueTask<ServiceResult> PauseAsync()
            {
                providerEntered.TrySetResult(true);
                await releaseProvider.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false);
                return ServiceResult.Good;
            }
            provider.Setup(value => value.OnDeviceStateChangingAsync(
                device, target, It.IsAny<CancellationToken>())).Returns(PauseAsync);

            Task? stateChange = null;
            Task? start = null;
            ExceptionDispatchInfo? testFailure = null;
            var cleanupFailures = new List<Exception>();
            try
            {
                stateChange = target == LadsDeviceStateValue.Sleep
                    ? device.GotoSleepAsync().AsTask()
                    : device.GotoShutdownAsync().AsTask();
                Task providerStarted = await Task.WhenAny(
                    providerEntered.Task,
                    Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(providerStarted, Is.SameAs(providerEntered.Task));

                start = Task.Run(async () =>
                {
                    startInvoked.TrySetResult(true);
                    await unit.StartAsync().ConfigureAwait(false);
                });
                await startInvoked.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
                    .ConfigureAwait(false);
                Task unitGateHeld = WaitForUnitGateAsync();
                Task admissionObserved = await Task.WhenAny(
                    unitGateHeld,
                    startStateChanged.Task,
                    start,
                    Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                Assert.That(admissionObserved, Is.SameAs(unitGateHeld));
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                Assert.That(start.IsCompleted, Is.False);

                releaseProvider.TrySetResult(true);
                await stateChange.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, async () =>
                    await start.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                Assert.That(device.State, Is.EqualTo(target));
            }
            catch (Exception failure)
            {
                testFailure = ExceptionDispatchInfo.Capture(failure);
            }
            finally
            {
                releaseProvider.TrySetResult(true);
                if (stateChange != null)
                {
                    try
                    {
                        await stateChange.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds))
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
                if (start != null)
                {
                    try
                    {
                        await start.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds)).ConfigureAwait(false);
                        cleanupFailures.Add(new AssertionException(
                            "The unit start completed although the device transition committed first."));
                    }
                    catch (ServiceResultException failure) when (failure.StatusCode == StatusCodes.BadInvalidState)
                    {
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
                    "The device-admission test and its cleanup both failed.",
                    [testFailure.SourceException, .. cleanupFailures]);
            }
            if (testFailure != null)
            {
                testFailure.Throw();
            }
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException("Device-admission cleanup failed.", cleanupFailures);
            }

            async Task WaitForUnitGateAsync()
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
                while (unit.Gate.CurrentCount != 0)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException("The unit start did not acquire its command gate.");
                    }
                    await Task.Delay(1).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Rejects synchronous functional-state writes after publication before they mutate the state.
        /// </summary>
        [Test]
        public async Task SynchronousFunctionalStateWritesAreRejectedAfterPublicationAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(SynchronousFunctionalStateWritesAreRejectedAfterPublicationAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit("Second");

            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
                () => unit.Functional.SetState(LadsFunctionalStateValue.Running));
            InvalidOperationException? itemStateFailure = Assert.Throws<InvalidOperationException>(
                device.OnFunctionalUnitStateChanged);

            Assert.That(failure!.Message, Is.EqualTo(
                "Synchronous LADS state writes are only supported before device publication."));
            Assert.That(itemStateFailure!.Message, Is.EqualTo(
                "Synchronous LADS state writes are only supported before device publication."));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.NotExecuting));
        }

        /// <summary>
        /// Preserves the functional unit event before the device item-state update on asynchronous writes.
        /// </summary>
        [Test]
        public async Task FunctionalUnitEventPrecedesItemStateUpdateAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(FunctionalUnitEventPrecedesItemStateUpdateAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            var observedItemStates = new List<MachineryItemStateValue>();
            MachineryItemStateValue initialItemState = device.ItemState;
            unit.StateChanged += state =>
            {
                observedItemStates.Add(device.ItemState);
                if (state == LadsFunctionalStateValue.Stopped)
                {
                    throw new InvalidOperationException("unit event failed");
                }
            };

            await unit.Functional.SetStateAsync(LadsFunctionalStateValue.Running).ConfigureAwait(false);

            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.Executing));
            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await unit.Functional.SetStateAsync(LadsFunctionalStateValue.Stopped).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(failure!.Message, Is.EqualTo("unit event failed"));
            Assert.That(observedItemStates, Is.EqualTo(new[]
            {
                initialItemState,
                MachineryItemStateValue.Executing
            }));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.Executing));
        }

        /// <summary>
        /// Preserves the raw and typed state-machine events on asynchronous writes and transitions.
        /// </summary>
        [Test]
        public async Task AsyncStateChangesRaiseRawAndTypedMachineEventsOnceAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(AsyncStateChangesRaiseRawAndTypedMachineEventsOnceAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            var rawStates = new List<string>();
            var typedStates = new List<LadsFunctionalStateValue>();
            var rawAsyncStates = new List<string>();
            var typedAsyncStates = new List<LadsFunctionalStateValue>();
            var rawItemStates = new List<MachineryItemStateValue>();
            var typedItemStates = new List<MachineryItemStateValue>();
            var rawAsyncItemStates = new List<MachineryItemStateValue>();
            var typedAsyncItemStates = new List<MachineryItemStateValue>();
            var order = new List<string>();
            unit.Functional.Driver.StateChanged += id =>
            {
                string state = unit.Functional.Driver.Table.FindState(id).Name;
                rawStates.Add(state);
                rawItemStates.Add(device.ItemState);
                order.Add($"raw-sync:{state}");
            };
            unit.Functional.StateChanged += state =>
            {
                typedStates.Add(state);
                typedItemStates.Add(device.ItemState);
                order.Add($"typed-sync:{state}");
            };
            unit.Functional.Driver.StateChangedAsync += (id, _) =>
            {
                string state = unit.Functional.Driver.Table.FindState(id).Name;
                rawAsyncStates.Add(state);
                rawAsyncItemStates.Add(device.ItemState);
                order.Add($"raw-async:{state}");
                return default;
            };
            unit.Functional.StateChangedAsync += (state, _) =>
            {
                typedAsyncStates.Add(state);
                typedAsyncItemStates.Add(device.ItemState);
                order.Add($"typed-async:{state}");
                return default;
            };

            await unit.Functional.TransitionToAsync(LadsFunctionalStateValue.Running).ConfigureAwait(false);
            await unit.Functional.SetStateAsync(LadsFunctionalStateValue.Stopped).ConfigureAwait(false);

            Assert.That(rawStates, Is.EqualTo(new[] { "Running", "Stopped" }));
            Assert.That(
                typedStates,
                Is.EqualTo(new[]
                {
                    LadsFunctionalStateValue.Running,
                    LadsFunctionalStateValue.Stopped
                }));
            Assert.That(rawAsyncStates, Is.EqualTo(new[] { "Running", "Stopped" }));
            Assert.That(
                typedAsyncStates,
                Is.EqualTo(new[] { LadsFunctionalStateValue.Running, LadsFunctionalStateValue.Stopped }));
            var expectedItemStates = new[]
            {
                MachineryItemStateValue.Executing,
                MachineryItemStateValue.NotExecuting
            };
            var expectedAsyncItemStates = new[]
            {
                MachineryItemStateValue.Executing,
                MachineryItemStateValue.NotExecuting
            };
            Assert.That(rawItemStates, Is.EqualTo(expectedItemStates));
            Assert.That(typedItemStates, Is.EqualTo(expectedItemStates));
            Assert.That(rawAsyncItemStates, Is.EqualTo(expectedAsyncItemStates));
            Assert.That(typedAsyncItemStates, Is.EqualTo(expectedAsyncItemStates));
            Assert.That(
                order,
                Is.EqualTo(new[]
                {
                    "typed-sync:Running",
                    "raw-sync:Running",
                    "typed-async:Running",
                    "raw-async:Running",
                    "typed-sync:Stopped",
                    "raw-sync:Stopped",
                    "typed-async:Stopped",
                    "raw-async:Stopped"
                }));
        }

        /// <summary>
        /// Verifies synchronous machine events run before a public asynchronous observer can fail.
        /// </summary>
        [Test]
        public async Task PublicAsyncObserverFailureFollowsSynchronousMachineEventsAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(PublicAsyncObserverFailureFollowsSynchronousMachineEventsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            var states = new List<string>();
            var itemStates = new List<MachineryItemStateValue>();
            unit.Functional.Driver.StateChanged += id =>
            {
                states.Add($"raw:{unit.Functional.Driver.Table.FindState(id).Name}");
                itemStates.Add(device.ItemState);
            };
            unit.Functional.StateChanged += state => states.Add($"typed:{state}");
            unit.Functional.StateChangedAsync += (_, _) =>
                new ValueTask(Task.FromException(new InvalidOperationException("public async observer failed")));

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await unit.Functional.SetStateAsync(LadsFunctionalStateValue.Running).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.That(failure!.Message, Is.EqualTo("public async observer failed"));
            Assert.That(states, Is.EqualTo(new[] { "typed:Running", "raw:Running" }));
            Assert.That(itemStates, Is.EqualTo(new[] { MachineryItemStateValue.Executing }));
            Assert.That(device.ItemState, Is.EqualTo(MachineryItemStateValue.Executing));
        }

        [Test]
        public async Task FunctionalUnitMethodsAreCallableAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(FunctionalUnitMethodsAreCallableAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            NodeId state = await server.ChildAsync(unit.NodeId, "FunctionalUnitState").ConfigureAwait(false);

            Variant properties = Variant.From(default(ArrayOf<ExtensionObject>));
            CallMethodResult start = await server.CallByNameAsync(state, "Start", properties).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(start.StatusCode), Is.True, start.StatusCode.ToString());
            CallMethodResult again = await server.CallByNameAsync(state, "Start", properties).ConfigureAwait(false);
            Assert.That(again.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            CallMethodResult abort = await server.CallByNameAsync(state, "Abort").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(abort.StatusCode), Is.True, abort.StatusCode.ToString());
            CallMethodResult clear = await server.CallByNameAsync(state, "Clear").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(clear.StatusCode), Is.True, clear.StatusCode.ToString());
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));

            NodeId current = await server.ChildAsync(state, "CurrentState").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(current).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Text, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task AnalogControlFunctionValidatesItsCommandsAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(AnalogControlFunctionValidatesItsCommandsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            IAnalogControlFunction temperature = unit.GetFunction<IAnalogControlFunction>("Temperature");

            Assert.That(temperature.Kind, Is.EqualTo(LadsAnalogControlKind.Plain));
            Assert.That(temperature.TargetValue, Is.EqualTo(37));
            Assert.That(temperature.CurrentValue, Is.EqualTo(22));
            Assert.That(temperature.FunctionalUnit, Is.SameAs(unit));
            LadsAssert.ThrowsStatus(StatusCodes.BadNotFound, () => unit.GetFunction<ICoverFunction>("Temperature"));
            Assert.That(unit.TryGetFunction("Missing", out IAnalogControlFunction? _), Is.False);

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadOutOfRange,
                () => temperature.SetTargetValueAsync(70)).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported,
                () => temperature.ModifyTargetValueByAsync(1)).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => temperature.StopAsync()).ConfigureAwait(false);

            temperature.IsEnabled = false;
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => temperature.StartAsync()).ConfigureAwait(false);
            temperature.IsEnabled = true;

            await temperature.StartWithTargetValueAsync(40).ConfigureAwait(false);
            Assert.That(temperature.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(temperature.TargetValue, Is.EqualTo(40));
            await temperature.SetTargetValueAsync(42).ConfigureAwait(false);
            temperature.UpdateCurrentValue(41.5);
            Assert.That(temperature.CurrentValue, Is.EqualTo(41.5));
            await temperature.AbortAsync().ConfigureAwait(false);
            Assert.That(temperature.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            await temperature.ClearAsync().ConfigureAwait(false);
            await temperature.StartAsync().ConfigureAwait(false);
            await temperature.StopAsync().ConfigureAwait(false);
            Assert.That(temperature.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));

            NodeId target = await server.ChildAsync(temperature.NodeId, "TargetValue").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(target).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out double read), Is.True, value.WrappedValue.ToString());
            Assert.That(read, Is.EqualTo(42));

            NodeId state = await server.ChildAsync(temperature.NodeId, "ControlFunctionState")
                .ConfigureAwait(false);
            CallMethodResult start = await server.CallByNameAsync(
                state,
                "StartWithTargetValue",
                Variant.From(30.0)).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(start.StatusCode), Is.True, start.StatusCode.ToString());
            Assert.That(temperature.TargetValue, Is.EqualTo(30));
            CallMethodResult stop = await server.CallByNameAsync(state, "Stop").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(stop.StatusCode), Is.True, stop.StatusCode.ToString());
        }

        [Test]
        public async Task CoverMovesThroughItsStatesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(CoverMovesThroughItsStatesAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ICoverFunction door = device.GetFunctionalUnit(LadsIncubator.UnitName).GetFunction<ICoverFunction>("Door");
            var states = new List<LadsCoverStateValue>();
            door.StateChanged += states.Add;

            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Closed));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.CloseAsync())
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.UnlockAsync())
                .ConfigureAwait(false);
            await door.OpenAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Opened));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.LockAsync())
                .ConfigureAwait(false);
            await door.CloseAsync().ConfigureAwait(false);
            await door.LockAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Locked));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.OpenAsync())
                .ConfigureAwait(false);
            await door.UnlockAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Closed));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => door.ResetAsync())
                .ConfigureAwait(false);
            await door.ReportErrorAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Error));
            await door.ReportErrorAsync().ConfigureAwait(false);
            await door.ResetAsync().ConfigureAwait(false);
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Opened));

            Assert.That(
                states,
                Is.EqualTo(new[]
                {
                    LadsCoverStateValue.Opening,
                    LadsCoverStateValue.Opened,
                    LadsCoverStateValue.Closing,
                    LadsCoverStateValue.Closed,
                    LadsCoverStateValue.Locking,
                    LadsCoverStateValue.Locked,
                    LadsCoverStateValue.Unlocking,
                    LadsCoverStateValue.Closed,
                    LadsCoverStateValue.Error,
                    LadsCoverStateValue.Opened
                }));

            NodeId coverState = await server.ChildAsync(door.NodeId, "CoverState").ConfigureAwait(false);
            CallMethodResult close = await server.CallByNameAsync(coverState, "Close").ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(close.StatusCode), Is.True, close.StatusCode.ToString());
            Assert.That(door.State, Is.EqualTo(LadsCoverStateValue.Closed));
        }

        [Test]
        public async Task NonLockableCoverRejectsLockingAsync()
        {
            ICoverFunction? lid = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(NonLockableCoverRejectsLockingAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit.Cover(
                        "Lid",
                        options => options.Lockable = false,
                        out lid))))).ConfigureAwait(false);
            await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(lid, Is.Not.Null);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadNotSupported, () => lid!.LockAsync())
                .ConfigureAwait(false);
            NodeId coverState = await server.ChildAsync(lid!.NodeId, "CoverState").ConfigureAwait(false);
            string[] children = await server.ChildNamesAsync(coverState).ConfigureAwait(false);
            Assert.That(children, Does.Contain("Open"));
            Assert.That(children, Does.Not.Contain("Lock"));
        }

        [Test]
        public async Task SensorsPublishTheirValuesAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(SensorsPublishTheirValuesAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            IAnalogScalarSensorFunction co2 = device.GetFunctionalUnit(LadsIncubator.UnitName)
                .GetFunction<IAnalogScalarSensorFunction>("CO2");

            Assert.That(co2.SensorValue, Is.EqualTo(5));
            Assert.That(co2.HasCompensation, Is.False);
            co2.UpdateValue(6.5, 6.4);
            Assert.That(co2.SensorValue, Is.EqualTo(6.5));
            Assert.That(co2.RawValue, Is.EqualTo(6.4));
            co2.UpdateValue(7);
            Assert.That(co2.RawValue, Is.EqualTo(7));
            LadsAssert.ThrowsStatus(StatusCodes.BadNotSupported, () => co2.UpdateCompensationValue(1));

            NodeId sensor = await server.ChildAsync(co2.NodeId, "SensorValue").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(sensor).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out double read), Is.True, value.WrappedValue.ToString());
            Assert.That(read, Is.EqualTo(7));
        }

        [Test]
        public async Task TimerTracksTheRemainingTimeAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(TimerTracksTheRemainingTimeAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ITimerFunction timer = device.GetFunctionalUnit(LadsIncubator.UnitName)
                .GetFunction<ITimerFunction>("Timer");

            await timer.SetTargetValueAsync(60).ConfigureAwait(false);
            timer.UpdateCurrentValue(10);
            Assert.That(timer.TargetValue, Is.EqualTo(60));
            Assert.That(timer.CurrentValue, Is.EqualTo(10));
            Assert.That(timer.DifferenceValue, Is.EqualTo(50));
            await timer.StartAsync().ConfigureAwait(false);
            Assert.That(timer.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            await timer.StopAsync().ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadOutOfRange, () => timer.SetTargetValueAsync(-1))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Non-finite targets are rejected by ranged and unrestricted controls.
        /// </summary>
        [Test]
        public async Task ControlsRejectNonFiniteTargetsAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ControlsRejectNonFiniteTargetsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            IAnalogControlFunction temperature = unit.GetFunction<IAnalogControlFunction>("Temperature");
            ITimerFunction timer = unit.GetFunction<ITimerFunction>("Timer");
            double[] invalid = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];

            foreach (double value in invalid)
            {
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadOutOfRange, () => temperature.SetTargetValueAsync(value)).ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadOutOfRange, () => temperature.StartWithTargetValueAsync(value))
                    .ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadOutOfRange, () => timer.SetTargetValueAsync(value)).ConfigureAwait(false);
            }

            NodeId state = await server.ChildAsync(temperature.NodeId, "ControlFunctionState").ConfigureAwait(false);
            CallMethodResult result = await server.CallByNameAsync(
                state, "StartWithTargetValue", Variant.From(double.NaN)).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(temperature.TargetValue, Is.EqualTo(37));
            Assert.That(temperature.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(timer.TargetValue, Is.Zero);
        }

        /// <summary>
        /// Timer values publish millisecond units when none are configured.
        /// </summary>
        [Test]
        public async Task TimerDefaultsToMillisecondUnitsAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(TimerDefaultsToMillisecondUnitsAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ITimerFunction timer = device.GetFunctionalUnit(LadsIncubator.UnitName)
                .GetFunction<ITimerFunction>("Timer");
            NodeId current = await server.ChildAsync(timer.NodeId, "CurrentValue").ConfigureAwait(false);
            NodeId unitsNode = await server.ChildAsync(current, "EngineeringUnits").ConfigureAwait(false);
            DataValue units = await server.ReadAsync(unitsNode).ConfigureAwait(false);

            Assert.That(units.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out EUInformation? information), Is.True);
            Assert.That(information?.UnitId, Is.EqualTo(LadsUnits.Millisecond.UnitId));
        }

        [Test]
        public async Task EveryFunctionTypeIsCreatedAsync()
        {
            IAnalogControlFunction? relative = null;
            IAnalogControlFunction? totalizer = null;
            IAnalogControlFunction? composed = null;
            IMultiModeAnalogControlFunction? multiMode = null;
            ITwoStateDiscreteControlFunction? valve = null;
            IMultiStateDiscreteControlFunction? level = null;
            IAnalogArraySensorFunction? spectrum = null;
            ITwoStateDiscreteSensorFunction? presence = null;
            IMultiStateDiscreteSensorFunction? phase = null;
            IMultiSensorFunction? multi = null;
            IAnalogScalarSensorFunction? compensated = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(EveryFunctionTypeIsCreatedAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit
                        .AnalogControl(
                            "Relative",
                            o =>
                            {
                                o.Kind = LadsAnalogControlKind.Relative;
                                o.Range = new Range(100, 0);
                                o.InitialTargetValue = 10;
                                o.IncreaseRate = 1;
                                o.DecreaseRate = 1;
                            },
                            out relative)
                        .AnalogControl("Totalizer", o => o.Kind = LadsAnalogControlKind.Totalizer, out totalizer)
                        .AnalogControl(
                            "Composed",
                            o =>
                            {
                                o.Kind = LadsAnalogControlKind.Composed;
                                o.ComposedTargetValues = s_composedNames.ToArrayOf();
                            },
                            out composed)
                        .MultiModeAnalogControl(
                            "Mode",
                            o => o.Modes = s_modes.ToArrayOf(),
                            out multiMode)
                        .TwoStateDiscreteControl("Valve", null, out valve)
                        .MultiStateDiscreteControl(
                            "Level",
                            o => o.States = new[]
                            {
                                new LocalizedText("Low"),
                                new LocalizedText("High")
                            }.ToArrayOf(),
                            out level)
                        .AnalogArraySensor(
                            "Spectrum",
                            o => o.InitialValues = s_initialSpectrum.ToArrayOf(),
                            out spectrum)
                        .AnalogScalarSensor("Compensated", o => o.WithCompensation = true, out compensated)
                        .TwoStateDiscreteSensor("Presence", null, out presence)
                        .MultiStateDiscreteSensor(
                            "Phase",
                            o => o.States = new[]
                            {
                                new LocalizedText("A"),
                                new LocalizedText("B")
                            }.ToArrayOf(),
                            out phase)
                        .MultiSensor("Multi", m => m.AnalogScalarSensor("Inner"), out multi))))).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit aux = device.GetFunctionalUnit("Aux");

            Assert.That(aux.Functions.Count, Is.EqualTo(11));
            await relative!.ModifyTargetValueByAsync(5).ConfigureAwait(false);
            Assert.That(relative.TargetValue, Is.EqualTo(15));
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadOutOfRange,
                () => relative.ModifyTargetValueByAsync(1000)).ConfigureAwait(false);
            await totalizer!.ResetTotalizerAsync().ConfigureAwait(false);
            Assert.That(totalizer.TotalizedValue, Is.Zero);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported,
                () => relative.ResetTotalizerAsync()).ConfigureAwait(false);
            composed!.SetComposedTargetValue("A", 3);
            LadsAssert.ThrowsStatus(StatusCodes.BadNotFound, () => composed.SetComposedTargetValue("C", 3));
            Assert.That(multiMode!.Modes.Count, Is.EqualTo(2));
            await multiMode.SetModeAsync(1).ConfigureAwait(false);
            Assert.That(multiMode.CurrentMode, Is.EqualTo(1));
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadOutOfRange, () => multiMode.SetModeAsync(5))
                .ConfigureAwait(false);
            multiMode.SetTargetValue("Cool", 4);
            Assert.That(multiMode.GetTargetValue("Cool"), Is.EqualTo(4));
            await valve!.StartWithTargetValueAsync(true).ConfigureAwait(false);
            Assert.That(valve.TargetValue, Is.True);
            valve.UpdateCurrentValue(true);
            Assert.That(valve.CurrentValue, Is.True);
            await level!.SetTargetValueAsync(1).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadOutOfRange, () => level.SetTargetValueAsync(7))
                .ConfigureAwait(false);
            level.UpdateCurrentValue(1);
            Assert.That(level.CurrentValue, Is.EqualTo(1));
            spectrum!.UpdateValue(s_spectrum.ToArrayOf());
            Assert.That(spectrum.SensorValue.Count, Is.EqualTo(3));
            compensated!.UpdateCompensationValue(2);
            Assert.That(compensated.HasCompensation, Is.True);
            presence!.UpdateValue(true);
            Assert.That(presence.SensorValue, Is.True);
            phase!.UpdateValue(1);
            Assert.That(phase.SensorValue, Is.EqualTo(1));
            Assert.That(multi!.Functions.Count, Is.EqualTo(1));
            Assert.That(aux.TryGetFunction("Multi/Inner", out IAnalogScalarSensorFunction? _), Is.True);
        }

        [Test]
        public async Task FunctionProviderCanVetoCommandsAsync()
        {
            var provider = new Mock<ILadsFunctionProvider>();
            var commands = new List<LadsFunctionCommandKind>();
            provider
                .Setup(p => p.OnCommandAsync(It.IsAny<LadsFunctionCommand>(), It.IsAny<CancellationToken>()))
                .Returns((LadsFunctionCommand command, CancellationToken _) =>
                {
                    commands.Add(command.Kind);
                    return new ValueTask<ServiceResult>(command.Kind == LadsFunctionCommandKind.Start
                        ? new ServiceResult(StatusCodes.BadDeviceFailure)
                        : ServiceResult.Good);
                });
            IAnalogControlFunction? heater = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(FunctionProviderCanVetoCommandsAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit
                        .FunctionProvider(provider.Object)
                        .AnalogControl("Heater", null, out heater))))).ConfigureAwait(false);
            await server.DeviceAsync().ConfigureAwait(false);

            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadDeviceFailure, () => heater!.StartAsync())
                .ConfigureAwait(false);
            Assert.That(heater!.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            await heater.StartWithTargetValueAsync(5).ConfigureAwait(false);
            Assert.That(heater.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(
                commands,
                Is.EqualTo(new[] { LadsFunctionCommandKind.Start, LadsFunctionCommandKind.StartWithTargetValue }));
        }

        [Test]
        public async Task RelativeTargetDeltaUpdatesCurrentValueFromThePreviousTargetAsync()
        {
            IAnalogControlFunction? relative = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(RelativeTargetDeltaUpdatesCurrentValueFromThePreviousTargetAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit.AnalogControl(
                        "Relative",
                        options =>
                        {
                            options.Kind = LadsAnalogControlKind.Relative;
                            options.Range = new Range(100, 0);
                            options.InitialTargetValue = 10;
                            options.InitialCurrentValue = 2;
                        },
                        out relative))))).ConfigureAwait(false);
            await server.DeviceAsync().ConfigureAwait(false);

            Assert.That(relative!.TargetValue, Is.EqualTo(10));
            Assert.That(relative.CurrentValue, Is.EqualTo(2));
            await relative.ModifyTargetValueByAsync(5).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(relative.TargetValue, Is.EqualTo(15));
                Assert.That(relative.CurrentValue, Is.EqualTo(15));
            });
        }

        [Test]
        public async Task MultiModeStartWithTargetValueUpdatesSelectedModeCurrentValueAsync()
        {
            IMultiModeAnalogControlFunction? multiMode = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(MultiModeStartWithTargetValueUpdatesSelectedModeCurrentValueAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit.MultiModeAnalogControl(
                        "Mode",
                        options => options.Modes = s_modes.ToArrayOf(),
                        out multiMode))))).ConfigureAwait(false);
            await server.DeviceAsync().ConfigureAwait(false);

            multiMode!.SetTargetValue("Heat", 3.5);
            multiMode.UpdateCurrentValue("Heat", 2.5);
            multiMode.SetTargetValue("Cool", 12);
            multiMode.UpdateCurrentValue("Cool", 1.5);
            await multiMode.SetModeAsync(1).ConfigureAwait(false);
            NodeId controlState = await server.ChildAsync(multiMode.NodeId, "ControlFunctionState")
                .ConfigureAwait(false);
            CallMethodResult start = await server.CallByNameAsync(
                controlState, "StartWithTargetValue", Variant.From(8.5)).ConfigureAwait(false);

            DataValue coolCurrent = await server.ReadAsync(await server.ChildAsync(
                multiMode.NodeId,
                "ControllerModeSet",
                "Cool",
                "CurrentValue").ConfigureAwait(false)).ConfigureAwait(false);
            DataValue coolTarget = await server.ReadAsync(await server.ChildAsync(
                multiMode.NodeId,
                "ControllerModeSet",
                "Cool",
                "TargetValue").ConfigureAwait(false)).ConfigureAwait(false);
            DataValue heatCurrent = await server.ReadAsync(await server.ChildAsync(
                multiMode.NodeId,
                "ControllerModeSet",
                "Heat",
                "CurrentValue").ConfigureAwait(false)).ConfigureAwait(false);
            DataValue heatTarget = await server.ReadAsync(await server.ChildAsync(
                multiMode.NodeId,
                "ControllerModeSet",
                "Heat",
                "TargetValue").ConfigureAwait(false)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(start.StatusCode), Is.True, start.StatusCode.ToString());
                Assert.That(multiMode.CurrentMode, Is.EqualTo(1));
                Assert.That(multiMode.GetTargetValue("Cool"), Is.EqualTo(8.5));
                Assert.That(coolCurrent.WrappedValue.TryGetValue(out double coolCurrentValue), Is.True);
                Assert.That(coolCurrentValue, Is.EqualTo(8.5));
                Assert.That(coolTarget.WrappedValue.TryGetValue(out double coolTargetValue), Is.True);
                Assert.That(coolTargetValue, Is.EqualTo(8.5));
                Assert.That(multiMode.GetTargetValue("Heat"), Is.EqualTo(3.5));
                Assert.That(heatCurrent.WrappedValue.TryGetValue(out double heatCurrentValue), Is.True);
                Assert.That(heatCurrentValue, Is.EqualTo(2.5));
                Assert.That(heatTarget.WrappedValue.TryGetValue(out double heatTargetValue), Is.True);
                Assert.That(heatTargetValue, Is.EqualTo(3.5));
            });
        }
    }
}
