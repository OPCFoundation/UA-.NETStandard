/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;

namespace Opc.Ua.PackML.Tests
{
    /// <summary>
    /// Tests that the controller honours a supplied machine subclass.
    /// </summary>
    [TestFixture]
    [Category("PackML")]
    [Parallelizable]
    public sealed class PackMLStateMachineControllerTests
    {
        [Test]
        public async Task AsyncCommandHonoursMachineCauseOverridesLikeSyncCommandAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState generated = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var machine = new AbortRejectingStateMachine(null)
            {
                NodeId = generated.NodeId,
                BrowseName = generated.BrowseName,
                DisplayName = generated.DisplayName
            };
            machine.CreateOrReplaceCurrentState(context, generated.CurrentState, assignInstanceNodeIds: false);
            machine.CreateOrReplaceMachineState(context, generated.MachineState, assignInstanceNodeIds: false);
            var controller = new PackMLStateMachineController(context, machine);
            controller.Initialize(PackMLInitialState.Idle);

            ServiceResult syncResult = controller.Execute(PackMLCommand.Abort);
            ServiceResult asyncResult = await controller.ExecuteAsync(PackMLCommand.Abort);

            Assert.That(syncResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(asyncResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(machine.Causes, Is.EqualTo(new uint[]
            {
                Methods.PackMLBaseStateMachineType_Abort,
                Methods.PackMLBaseStateMachineType_Abort
            }));
            Assert.That(machine.Paths, Is.EqualTo(s_expectedPaths));
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));

            machine.RejectMapping = true;
            Assert.That(controller.Execute(PackMLCommand.Abort).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That((await controller.ExecuteAsync(PackMLCommand.Abort)).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(machine.Paths, Is.EqualTo(s_expectedPaths));
        }

        [Test]
        public async Task AsyncCommandAwaitsTransitionEventSinkWithoutBlockingAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Idle);
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            var events = new List<IFilterTarget>();
            machine.SetAreEventsMonitored(context, true, includeChildren: true);
            NodeStateReportEventAsyncHandler sink = async (_, _, e, _) =>
            {
                events.Add(e);
                if (events.Count == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                }
            };
            machine.OnReportEventAsync = sink;

            Task<ServiceResult> start;
            bool pendingWhileGated;
            uint stateWhileGated;
            try
            {
                // A blocking implementation would not return from the call, so
                // the call runs on a bounded task.
                Task<Task<ServiceResult>> call = Task.Factory.StartNew(
                    () => controller.ExecuteAsync(PackMLCommand.Start).AsTask(),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                start = await call.WaitAsync(s_timeout).ConfigureAwait(false);
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                pendingWhileGated = !start.IsCompleted;
                stateWhileGated = controller.CurrentState;
            }
            finally
            {
                release.TrySetResult(true);
            }
            ServiceResult result = await start.WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"Start must succeed: {result}");
            Assert.That(pendingWhileGated, Is.True);
            Assert.That(stateWhileGated, Is.EqualTo(PackMLStateNumbers.Starting));
            Assert.That(events, Is.Not.Empty);
            Assert.That(events[0], Is.InstanceOf<TransitionEventState>());
        }

        [Test]
        public async Task ConcurrentSyncWritesAreRejectedWhileAsyncNotificationIsPendingAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Idle);
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            int sinkCalls = 0;
            machine.MachineState.Required().ExecuteState.Required().CurrentState.Required().OnStateChangedAsync =
                async (_, _, _, _) =>
                {
                    if (Interlocked.Increment(ref sinkCalls) == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                    }
                };
            var changes = new List<(uint Previous, uint Current)>();
            controller.StateChanged += (_, change) =>
            {
                changes.Add((change.PreviousState, change.CurrentState));
            };

            Task<ServiceResult> start = Task.Run(() => controller.ExecuteAsync(PackMLCommand.Start).AsTask());
            ServiceResult? rejected = null;
            bool startPendingAfterAbort;
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Task<ServiceResult> abort = Task.Run(() => controller.Execute(PackMLCommand.Abort));
                rejected = await abort.WaitAsync(s_timeout).ConfigureAwait(false);
                await Task.Run(() =>
                {
                    Assert.That(controller.CompleteActingState().StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                    Assert.That(controller.SetMode(controller.CurrentMode).StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                    Assert.That(controller.SetSupportedStates(controller.SupportedStates).StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                    Assert.That(() => controller.Initialize(), Throws.TypeOf<ServiceResultException>());
                    Assert.That(() => controller.ConfigureStates(controller.SupportedStates, PackMLInitialState.Idle),
                        Throws.TypeOf<ServiceResultException>());
                    Assert.That(machine.DoCause(
                        context, null, Methods.PackMLBaseStateMachineType_Abort, default, []).StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                    Assert.That(() => machine.SetState(context, Objects.PackMLBaseStateMachineType_Aborted),
                        Throws.TypeOf<ServiceResultException>());
                    Assert.That(() => machine.MachineState.Required().SetState(
                        context, Objects.PackMLMachineStateMachineType_Stopped),
                        Throws.TypeOf<ServiceResultException>());
                }).WaitAsync(s_timeout).ConfigureAwait(false);
                startPendingAfterAbort = !start.IsCompleted;
            }
            finally
            {
                release.TrySetResult(true);
            }
            ServiceResult startResult = await start.WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(rejected!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(startPendingAfterAbort, Is.True);
            Assert.That(ServiceResult.IsGood(startResult), Is.True);
            Assert.That(changes, Is.EqualTo(new[]
            {
                (PackMLStateNumbers.Idle, PackMLStateNumbers.Starting)
            }));
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Starting));
            Assert.That(ServiceResult.IsGood(controller.Execute(PackMLCommand.Abort)), Is.True);
        }

        [Test]
        public async Task AsyncCommandRetainsPublishedStateWhenAsyncCustomizationThrowsAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState generated = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var machine = new ThrowingAfterCauseStateMachine(null)
            {
                NodeId = generated.NodeId,
                BrowseName = generated.BrowseName,
                DisplayName = generated.DisplayName
            };
            machine.CreateOrReplaceCurrentState(context, generated.CurrentState, assignInstanceNodeIds: false);
            machine.CreateOrReplaceMachineState(context, generated.MachineState, assignInstanceNodeIds: false);
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Idle);
            var changes = new List<(uint Previous, uint Current)>();
            controller.StateChanged += (_, change) => changes.Add((change.PreviousState, change.CurrentState));
            machine.SetAreEventsMonitored(context, true, includeChildren: true);
            var events = new List<IFilterTarget>();
            machine.OnReportEventAsync = (_, _, e, _) =>
            {
                events.Add(e);
                return default;
            };

            InvalidOperationException? error = null;
            try
            {
                await controller.ExecuteAsync(PackMLCommand.Abort).ConfigureAwait(false);
            }
            catch (InvalidOperationException e)
            {
                error = e;
            }

            Assert.That(error, Is.SameAs(machine.Failure));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0], Is.InstanceOf<TransitionEventState>());
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Aborting));
            Assert.That(machine.MachineState.Required().CurrentState.Required().StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            Assert.That(machine.MachineState.Required().ExecuteState.Required().CurrentState.Required().StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            Assert.That(changes, Is.EqualTo(new[] { (PackMLStateNumbers.Idle, PackMLStateNumbers.Aborting) }));
            machine.OnReportEventAsync = null;
            Assert.That(ServiceResult.IsGood(await controller.CompleteActingStateAsync()), Is.True);
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Aborted));
        }

        [Test]
        public async Task CommittedTransitionReturningBadReconcilesNestedStatesAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Idle);
            var changes = new List<(uint Previous, uint Current)>();
            controller.StateChanged += (_, change) => changes.Add((change.PreviousState, change.CurrentState));
            machine.OnAfterTransitionAsync = (_, _, _, _, _, _, _) =>
                new ValueTask<ServiceResult>(
                    ServiceResult.Create(StatusCodes.BadUnexpectedError, "After transition."));

            ServiceResult result = await controller.ExecuteAsync(PackMLCommand.Abort);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUnexpectedError));
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Aborting));
            Assert.That(machine.MachineState.Required().CurrentState.Required().StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            Assert.That(changes, Is.EqualTo(new[] { (PackMLStateNumbers.Idle, PackMLStateNumbers.Aborting) }));
            machine.OnAfterTransitionAsync = null;
            Assert.That(ServiceResult.IsGood(await controller.CompleteActingStateAsync()), Is.True);
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Aborted));
        }

        [Test]
        public async Task CommittedCompletionReconcilesStateWhenNotificationThrowsAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Aborted);
            Assert.That(ServiceResult.IsGood(await controller.ExecuteAsync(PackMLCommand.Clear)), Is.True);
            var nested = machine.MachineState.Required();
            nested.SetAreEventsMonitored(context, true, includeChildren: true);
            var failure = new InvalidOperationException("The transition event failed.");
            nested.OnReportEventAsync = (_, _, _, _) => throw failure;
            var changes = new List<(uint Previous, uint Current)>();
            controller.StateChanged += (_, change) => changes.Add((change.PreviousState, change.CurrentState));

            InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await controller.CompleteActingStateAsync()))!;

            Assert.That(error, Is.SameAs(failure));
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            Assert.That(changes, Is.EqualTo(new[] { (PackMLStateNumbers.Clearing, PackMLStateNumbers.Stopped) }));
            Assert.That(controller.CanExecute(PackMLCommand.Reset), Is.True);
        }

        [Test]
        public async Task CommittedStartRetainsParametersWhenNotificationThrowsAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Idle);
            var execute = machine.MachineState.Required().ExecuteState.Required();
            execute.SetAreEventsMonitored(context, true, includeChildren: true);
            var failure = new InvalidOperationException("The transition event failed.");
            execute.OnReportEventAsync = (_, _, _, _) => throw failure;
            ArrayOf<PackMLDescriptorDataType> parameters =
                [new PackMLDescriptorDataType { ID = 9, Name = "Temperature", Value = 23.5f }];

            InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await controller.ExecuteAsync(PackMLCommand.Start, parameters)))!;

            Assert.That(error, Is.SameAs(failure));
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Starting));
            Assert.That(controller.StartParameters.Count, Is.EqualTo(1));
            Assert.That(controller.StartParameters[0].ID, Is.EqualTo(9));
        }

        [Test]
        public async Task FailedAutoCompletionAfterCommitReportsOneStateChangeAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine);
            controller.Initialize(PackMLInitialState.Aborted);
            machine.MachineState.Required().OnAfterTransitionAsync = (_, _, _, _, _, _, _) =>
                new ValueTask<ServiceResult>(ServiceResult.Create(StatusCodes.BadUnexpectedError, "After completion."));
            var changes = new List<(uint Previous, uint Current)>();
            controller.StateChanged += (_, change) => changes.Add((change.PreviousState, change.CurrentState));

            ServiceResult result = await controller.ExecuteAsync(PackMLCommand.Clear);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUnexpectedError));
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Stopped));
            Assert.That(changes, Is.EqualTo(new[] { (PackMLStateNumbers.Aborted, PackMLStateNumbers.Stopped) }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TransitionAndRecoveryFailuresAreBothReportedAsync(bool returnBad)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Idle);
            machine.SetAreEventsMonitored(context, true, includeChildren: true);
            var transitionError = new InvalidOperationException("The transition event failed.");
            var recoveryError = new InvalidOperationException("The nested state notification failed.");
            if (returnBad)
            {
                machine.OnAfterTransitionAsync = (_, _, _, _, _, _, _) =>
                    new ValueTask<ServiceResult>(
                        ServiceResult.Create(StatusCodes.BadUnexpectedError, "After transition."));
            }
            else
            {
                machine.OnReportEventAsync = (_, _, _, _) => throw transitionError;
            }
            machine.MachineState.Required().CurrentState.Required().OnStateChangedAsync =
                (_, _, _, _) => throw recoveryError;

            AggregateException error = (await Assert.ThrowsAsync<AggregateException>(
                async () => await controller.ExecuteAsync(PackMLCommand.Abort)))!;

            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            if (returnBad)
            {
                Assert.That(error.InnerExceptions[0], Is.InstanceOf<ServiceResultException>());
                Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUnexpectedError));
            }
            else
            {
                Assert.That(error.InnerExceptions[0], Is.SameAs(transitionError));
            }
            Assert.That(error.InnerExceptions[1], Is.SameAs(recoveryError));
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Aborting));
        }

        [Test]
        public async Task AutoCompletedTransitionsPublishTheirOwnLiveStateBeforeAdvancingAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine);
            controller.Initialize(PackMLInitialState.Aborted);
            machine.SetAreEventsMonitored(context, true, includeChildren: true);
            var states = new List<uint>();
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            NodeStateReportEventAsyncHandler sink = async (_, _, e, _) =>
            {
                if (e is TransitionEventState)
                {
                    states.Add(controller.CurrentState);
                    if (states.Count == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                        Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Cleared));
                    }
                }
            };
            machine.OnReportEventAsync = sink;
            Task<ServiceResult> clear = Task.Run(() => controller.ExecuteAsync(PackMLCommand.Clear).AsTask());
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(states, Is.EqualTo(new[] { PackMLStateNumbers.Cleared }));
                Assert.That(clear.IsCompleted, Is.False);
                ServiceResult rejected = await Task.Run(() => controller.Execute(PackMLCommand.Abort))
                    .WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            }
            finally
            {
                release.TrySetResult(true);
            }
            Assert.That(ServiceResult.IsGood(await clear.WaitAsync(s_timeout).ConfigureAwait(false)), Is.True);
            Assert.That(states, Is.EqualTo(new[] { PackMLStateNumbers.Cleared, PackMLStateNumbers.Stopped }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigureStatesKeepsAdmissionUntilPublicationFinishesAsync(bool failPublication)
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine);
            controller.Initialize();
            var failure = new InvalidOperationException("Configuration notification failed.");
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            var notificationNode = machine.MachineState.Required().CurrentState.Required();
            notificationNode.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                if (failPublication)
                {
                    throw failure;
                }
            };
            using var cancellation = new CancellationTokenSource();
            Task configure = Task.Run(() => controller.ConfigureStatesAsync(
                controller.SupportedStates, PackMLInitialState.Idle, cancellation.Token).AsTask());
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                cancellation.Cancel();
                ServiceResult result = await Task.Run(() => controller.Execute(PackMLCommand.Start))
                    .WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(configure.IsCompleted, Is.False);
                Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
                using var waiterCancellation = new CancellationTokenSource();
                Task<ServiceResult> waiter = controller.ExecuteAsync(
                    PackMLCommand.Abort, cancellationToken: waiterCancellation.Token).AsTask();
                Assert.That(waiter.IsCompleted, Is.False);
                waiterCancellation.Cancel();
                await Assert.ThatAsync(
                    () => waiter.WaitAsync(s_timeout),
                    Throws.InstanceOf<OperationCanceledException>());
                Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
            }
            finally
            {
                release.TrySetResult(true);
            }
            if (failPublication)
            {
                InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await configure.WaitAsync(s_timeout).ConfigureAwait(false));
                Assert.That(error, Is.SameAs(failure));
            }
            else
            {
                await configure.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            notificationNode.OnStateChangedAsync = null;
            Assert.That(ServiceResult.IsGood(controller.Execute(PackMLCommand.Start)), Is.True);
        }

        [Test]
        public async Task ActingStateCompletionRejectsSyncWritesUntilItsLiveEventFinishesAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Idle);
            Assert.That(ServiceResult.IsGood(await controller.ExecuteAsync(PackMLCommand.Start)), Is.True);
            var execute = machine.MachineState.Required().ExecuteState.Required();
            execute.SetAreEventsMonitored(context, true, includeChildren: true);
            TaskCompletionSource<bool> entered = PackMLTestSupport.Signal();
            TaskCompletionSource<bool> release = PackMLTestSupport.Signal();
            execute.OnReportEventAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Execute));
            };
            Task<ServiceResult> complete = Task.Run(() => controller.CompleteActingStateAsync().AsTask());
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(complete.IsCompleted, Is.False);
                ServiceResult rejected = await Task.Run(() => controller.Execute(PackMLCommand.Stop))
                    .WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            }
            finally
            {
                release.TrySetResult(true);
            }
            Assert.That(ServiceResult.IsGood(await complete.WaitAsync(s_timeout).ConfigureAwait(false)), Is.True);
            execute.OnReportEventAsync = null;
            Assert.That(ServiceResult.IsGood(controller.Execute(PackMLCommand.Stop)), Is.True);
        }

        [Test]
        public async Task ControllerNotificationRejectsAsyncSelfReentryWithoutWaitingAsync()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseStateMachineState machine = context
                .CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context))
                .BaseStateMachine
                .Required();
            var controller = new PackMLStateMachineController(context, machine)
            {
                AutoCompleteActingStates = false
            };
            controller.Initialize(PackMLInitialState.Idle);
            var execute = machine.MachineState.Required().ExecuteState.Required();
            ServiceResultException? rejection = null;
            execute.CurrentState.Required().OnStateChangedAsync = async (_, _, _, _) =>
            {
                rejection = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await controller.ExecuteAsync(PackMLCommand.Abort).AsTask()
                        .WaitAsync(s_timeout).ConfigureAwait(false));
            };
            ServiceResult result = await Task.Run(() => controller.ExecuteAsync(PackMLCommand.Start).AsTask())
                .WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(rejection, Is.Not.Null);
            Assert.That(rejection!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(controller.CurrentState, Is.EqualTo(PackMLStateNumbers.Starting));
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);
        private static readonly string[] s_expectedPaths = ["sync", "async"];

        /// <summary>
        /// A base machine whose async customization throws after publication.
        /// </summary>
        private sealed class ThrowingAfterCauseStateMachine : PackMLBaseStateMachineState
        {
            /// <summary>
            /// Creates the machine.
            /// </summary>
            public ThrowingAfterCauseStateMachine(NodeState? parent)
                : base(parent)
            {
            }

            /// <summary>
            /// Gets the exception the async cause throws.
            /// </summary>
            public InvalidOperationException Failure { get; } = new("The cause failed after publication.");

            /// <inheritdoc/>
            public override async ValueTask<ServiceResult> DoCauseAsync(
                ISystemContext context,
                MethodState? causeMethod,
                uint causeId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments,
                CancellationToken cancellationToken = default)
            {
                await base.DoCauseAsync(
                    context, causeMethod, causeId, inputArguments, outputArguments, cancellationToken)
                    .ConfigureAwait(false);
                throw Failure;
            }
        }

        /// <summary>
        /// A base machine whose cause mapping rejects Abort and records every cause.
        /// </summary>
        private sealed class AbortRejectingStateMachine : PackMLBaseStateMachineState
        {
            /// <summary>
            /// Creates the machine.
            /// </summary>
            public AbortRejectingStateMachine(NodeState? parent)
                : base(parent)
            {
            }

            /// <summary>
            /// Gets the causes passed to the synchronous and async counterparts.
            /// </summary>
            public List<uint> Causes { get; } = [];

            /// <summary>
            /// Gets which cause counterpart ran for each cause: "sync"
            /// or "async".
            /// </summary>
            public List<string> Paths { get; } = [];

            /// <summary>
            /// Gets or sets whether the cause mapping rejects Abort.
            /// </summary>
            public bool RejectMapping { get; set; }

            /// <inheritdoc/>
            public override ValueTask<ServiceResult> DoCauseAsync(
                ISystemContext context,
                MethodState? causeMethod,
                uint causeId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments,
                CancellationToken cancellationToken = default)
            {
                Causes.Add(causeId);
                Paths.Add("async");
                if (causeId == Methods.PackMLBaseStateMachineType_Abort)
                {
                    return new ValueTask<ServiceResult>(
                        ServiceResult.Create(StatusCodes.BadNotSupported, "Abort is rejected by the machine."));
                }
                return base.DoCauseAsync(
                    context, causeMethod, causeId, inputArguments, outputArguments, cancellationToken);
            }

            /// <inheritdoc/>
            public override ServiceResult DoCause(
                ISystemContext context,
                MethodState? causeMethod,
                uint causeId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments)
            {
                Causes.Add(causeId);
                Paths.Add("sync");
                if (causeId == Methods.PackMLBaseStateMachineType_Abort)
                {
                    return ServiceResult.Create(StatusCodes.BadNotSupported, "Abort is rejected by the machine.");
                }
                return base.DoCause(
                    context, causeMethod, causeId, inputArguments, outputArguments);
            }

            /// <inheritdoc/>
            protected override uint GetTransitionForCause(ISystemContext context, uint causeId)
            {
                return RejectMapping && causeId == Methods.PackMLBaseStateMachineType_Abort
                    ? 0
                    : base.GetTransitionForCause(context, causeId);
            }
        }
    }
}
