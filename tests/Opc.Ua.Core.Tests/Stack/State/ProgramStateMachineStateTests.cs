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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests that <see cref="ProgramStateMachineState"/> wires the cause
    /// methods it ships handlers for. Part 3 §5.7 defines
    /// <c>Executable</c> / <c>UserExecutable</c> as "may this Method be
    /// called right now", which for a Part 16 cause is
    /// <see cref="FiniteStateMachineState.IsCausePermitted"/>; left
    /// unwired the attributes stay at the <c>true</c> the node was
    /// constructed with and a client can only discover which causes
    /// apply by calling one and being refused.
    /// </summary>
    [TestFixture]
    [Category("ProgramStateMachineState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ProgramStateMachineStateTests
    {
        private const ushort kNs = 2;

        private ISystemContext m_context;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(m_telemetry);
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

        [Test]
        public void CauseMethodsReportExecutableFromTheCurrentState()
        {
            ProgramStateMachineState machine = CreateMachine(
                out MethodState start, out MethodState halt);

            // The machine starts in Ready: Start applies, Halt applies,
            // Reset (Halted only) does not.
            Assert.Multiple(() =>
            {
                Assert.That(ReadAttribute(start, Attributes.Executable), Is.True);
                Assert.That(ReadAttribute(start, Attributes.UserExecutable), Is.True);
                Assert.That(ReadAttribute(halt, Attributes.Executable), Is.True);
            });

            machine.SetState(m_context, Objects.ProgramStateMachineType_Halted);

            Assert.Multiple(() =>
            {
                Assert.That(ReadAttribute(start, Attributes.Executable), Is.False);
                Assert.That(ReadAttribute(start, Attributes.UserExecutable), Is.False);
                Assert.That(ReadAttribute(halt, Attributes.Executable), Is.False);
            });
        }

        [Test]
        public void CauseMethodsAreBoundToTheMatchingCause()
        {
            ProgramStateMachineState machine = CreateMachine(
                out MethodState start, out _);

            Assert.That(start.OnCallMethod, Is.Not.Null);

            ServiceResult result = start.OnCallMethod!(
                m_context, start, inputArguments: default, outputArguments: []);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"Start must succeed: {result}");
            Assert.That(CurrentStateId(machine),
                Is.EqualTo(Objects.ProgramStateMachineType_Running));
        }

        [Test]
        public void MethodsAddedAfterCreateAreWiredOnDemand()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);

            // Create rebuilds the child list from the type template, so
            // a method materialized afterwards has to be wired by the
            // server — the public entry point exists for exactly that.
            MethodState reset = AddMethod(machine, BrowseNames.Reset, "Machine_Reset");
            Assert.That(reset.OnReadExecutable, Is.Null);

            machine.WireCauseMethods(m_context);

            // The machine is in Ready, and Reset is a cause of Halted.
            Assert.Multiple(() =>
            {
                Assert.That(reset.OnCallMethod, Is.Not.Null);
                Assert.That(ReadAttribute(reset, Attributes.Executable), Is.False);
            });

            machine.SetState(m_context, Objects.ProgramStateMachineType_Halted);

            Assert.That(ReadAttribute(reset, Attributes.Executable), Is.True);
        }

        /// <summary>
        /// Part 5 6.4.3: a cause refused in the current state is audited with Status FALSE
        /// and is not reported as a program transition.
        /// </summary>
        [Test]
        public void RefusedCauseIsAuditedAsFailed()
        {
            ProgramStateMachineState machine = CreateMachine(out MethodState start, out _);
            machine.SetState(m_context, Objects.ProgramStateMachineType_Halted);

            var events = new List<IFilterTarget>();
            machine.SetAreEventsMonitored(m_context, true, false);
            machine.OnReportEvent = (_, _, e) => events.Add(e);

            ServiceResult result = machine.DoCause(
                m_context, start, Methods.ProgramStateMachineType_Start, default, []);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0], Is.InstanceOf<AuditUpdateStateEventState>());
            Assert.That(((AuditUpdateStateEventState)events[0]).Status!.Value, Is.False);
        }

        /// <summary>
        /// DoCauseAsync reports the same events in the same order as DoCause,
        /// but awaits an asynchronous sink instead of blocking on it.
        /// </summary>
        [Test]
        public async Task DoCauseAsyncAwaitsEventSinkAndKeepsSyncEventOrderAsync()
        {
            ProgramStateMachineState syncMachine = CreateMachine(out MethodState syncStart, out _);
            var syncEvents = new List<Type>();
            syncMachine.SetAreEventsMonitored(m_context, true, false);
            syncMachine.OnReportEvent = (_, _, e) => syncEvents.Add(e.GetType());
            ServiceResult syncResult = syncMachine.DoCause(
                m_context, syncStart, Methods.ProgramStateMachineType_Start, default, []);

            ProgramStateMachineState machine = CreateMachine(out MethodState start, out _);
            var events = new List<Type>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            machine.SetAreEventsMonitored(m_context, true, false);
            machine.OnReportEventAsync = async (_, _, e, _) =>
            {
                events.Add(e.GetType());
                if (events.Count == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                }
            };

            Task<ServiceResult> cause;
            bool pendingWhileGated;
            uint stateWhileGated;
            try
            {
                // A blocking implementation would not return from the call, so
                // the call runs on a bounded task.
                Task<Task<ServiceResult>> call = Task.Factory.StartNew(
                    () => machine.DoCauseAsync(
                        m_context, start, Methods.ProgramStateMachineType_Start, default, []).AsTask(),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                cause = await call.WaitAsync(s_timeout).ConfigureAwait(false);
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                pendingWhileGated = !cause.IsCompleted;
                stateWhileGated = CurrentStateId(machine);
            }
            finally
            {
                release.TrySetResult(true);
            }
            ServiceResult result = await cause.WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(syncResult), Is.True, $"DoCause must succeed: {syncResult}");
            Assert.That(ServiceResult.IsGood(result), Is.True, $"DoCauseAsync must succeed: {result}");
            Assert.That(pendingWhileGated, Is.True);
            Assert.That(stateWhileGated, Is.EqualTo(Objects.ProgramStateMachineType_Running));
            Assert.That(events, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(events, Is.EqualTo(syncEvents));
        }

        /// <summary>
        /// Native counterparts use subclass cause mapping and virtual async transitions,
        /// without invoking synchronous overrides.
        /// </summary>
        [Test]
        public async Task DoCauseAsyncUsesProtectedMappingAndVirtualAsyncTransitionAsync()
        {
            var machine = new RecordingProgramStateMachineState
            {
                NodeId = new NodeId("Recording", kNs),
                BrowseName = new QualifiedName("Recording", kNs),
                DisplayName = new LocalizedText("Recording")
            };
            machine.Create(
                m_context,
                machine.NodeId,
                machine.BrowseName,
                displayName: machine.DisplayName,
                assignNodeIds: false);

            ServiceResult result = await machine.DoCauseAsync(
                m_context, null, RecordingProgramStateMachineState.CustomCauseId, default, [])
                .AsTask().WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"DoTransitionAsync must succeed: {result}");
            Assert.That(machine.SyncTransitions, Is.Zero);
            Assert.That(machine.SyncCauses, Is.Zero);
            Assert.That(machine.AsyncCauses, Is.EqualTo(1));
            Assert.That(machine.AsyncTransitions, Is.EqualTo(1));
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Running));
        }

        [Test]
        public async Task DoCauseAsyncObservesCancellationBeforeTheCauseAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out MethodState start, out _);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.CatchAsync<OperationCanceledException>(
                async () => await machine.DoCauseAsync(
                    m_context, start, Methods.ProgramStateMachineType_Start, default, [], cts.Token)
                    .ConfigureAwait(false));
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Ready));
        }

        [Test]
        public async Task NativeCauseRetainsProcessingAndEveryNotificationFailureAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out MethodState start, out _);
            machine.ClearChangeMasks(m_context, true);
            var original = new InvalidOperationException("after transition");
            var transition = new InvalidOperationException("transition notification");
            var state = new InvalidOperationException("current state notification");
            var audit = new InvalidOperationException("cause audit");
            var program = new InvalidOperationException("program audit");
            var delivered = new List<string>();
            machine.SetAreEventsMonitored(m_context, true, false);
            machine.OnAfterTransition = (_, _, _, _, _, _) => throw original;
            machine.OnReportEventAsync = (_, _, e, _) =>
            {
                if (e is ProgramTransitionEventState)
                {
                    delivered.Add("transition");
                    return new ValueTask(Task.FromException(transition));
                }
                if (e is AuditProgramTransitionEventState)
                {
                    delivered.Add("program");
                    return new ValueTask(Task.FromException(program));
                }
                delivered.Add("audit");
                return new ValueTask(Task.FromException(audit));
            };
            machine.CurrentState!.OnStateChangedAsync = (_, _, _, _) =>
            {
                delivered.Add("state");
                return new ValueTask(Task.FromException(state));
            };
            machine.LastTransition!.OnStateChangedAsync = (_, _, _, _) =>
            {
                delivered.Add("last transition");
                return default;
            };

            AggregateException? error = null;
            try
            {
                await machine.DoCauseAsync(
                    m_context, start, Methods.ProgramStateMachineType_Start, default, [])
                    .AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            }
            catch (AggregateException e)
            {
                error = e;
            }

            Assert.That(error, Is.Not.Null);
            Assert.That(error!.InnerExceptions,
                Is.EqualTo(new Exception[] { original, transition, state, audit, program }));
            Assert.That(delivered, Is.EqualTo(s_expectedNotificationOrder));
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Running));
            machine.OnReportEventAsync = null;
            machine.CurrentState!.OnStateChangedAsync = null;
            Assert.That(ServiceResult.IsGood(machine.DoTransition(
                m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, [])), Is.True);
        }

        [Test]
        public async Task NativeTransitionRethrowsOriginalFailureAndStillDeliversStateAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);
            machine.ClearChangeMasks(m_context, true);
            var failure = new InvalidOperationException("only");
            bool stateDelivered = false;
            machine.SetAreEventsMonitored(m_context, true, false);
            machine.OnReportEventAsync = (_, _, _, _) => new ValueTask(Task.FromException(failure));
            machine.CurrentState!.OnStateChangedAsync = (_, _, _, _) =>
            {
                stateDelivered = true;
                return default;
            };

            InvalidOperationException? error = null;
            try
            {
                await machine.DoTransitionAsync(
                    m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, [])
                    .AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            }
            catch (InvalidOperationException e)
            {
                error = e;
            }

            Assert.That(error, Is.SameAs(failure));
            Assert.That(stateDelivered, Is.True);
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Running));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task NativeCauseRetainsPreMutationFailureWhenAuditAlsoFailsAsync(bool permissionFailure)
        {
            ProgramStateMachineState machine = CreateMachine(out MethodState start, out _);
            var original = new InvalidOperationException("callback");
            var audit = new InvalidOperationException("audit");
            StateMachineTransitionHandler callback = (_, _, _, _, _, _) => throw original;
            if (permissionFailure)
            {
                machine.OnCheckUserPermission = callback;
            }
            else
            {
                machine.OnBeforeTransition = callback;
            }
            machine.SetAreEventsMonitored(m_context, true, false);
            int auditAttempts = 0;
            machine.OnReportEventAsync = (_, _, _, _) =>
            {
                auditAttempts++;
                return new ValueTask(Task.FromException(audit));
            };
            AggregateException? failure = null;
            try
            {
                await machine.DoCauseAsync(
                    m_context, start, Methods.ProgramStateMachineType_Start, default, [])
                    .AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            }
            catch (AggregateException ex)
            {
                failure = ex;
            }
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.InnerExceptions, Is.EqualTo(new Exception[] { original, audit }));
            Assert.That(auditAttempts, Is.EqualTo(1));
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Ready));
        }

        [Test]
        public async Task NativeCauseFinishesDeliveryWhenCanceledAfterMutationAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out MethodState start, out _);
            machine.ClearChangeMasks(m_context, true);
            var delivered = new List<Type>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            bool receivedCancelableToken = false;
            bool stateDelivered = false;
            machine.SetAreEventsMonitored(m_context, true, false);
            machine.OnReportEventAsync = async (_, _, e, ct) =>
            {
                receivedCancelableToken |= ct.CanBeCanceled;
                delivered.Add(e.GetType());
                if (delivered.Count == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                }
            };
            machine.CurrentState!.OnStateChangedAsync = (_, _, _, ct) =>
            {
                receivedCancelableToken |= ct.CanBeCanceled;
                stateDelivered = true;
                return default;
            };
            Task<ServiceResult> cause = machine.DoCauseAsync(
                m_context, start, Methods.ProgramStateMachineType_Start, default, [], cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                cancellation.Cancel();
            }
            finally
            {
                release.TrySetResult(true);
            }
            ServiceResult result = await cause.WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(stateDelivered, Is.True);
            Assert.That(receivedCancelableToken, Is.False);
            Assert.That(delivered, Is.EqualTo(new[]
            {
                typeof(ProgramTransitionEventState),
                typeof(ProgramTransitionAuditEventState),
                typeof(AuditProgramTransitionEventState)
            }));
        }

        [Test]
        public async Task NativeTransitionsSerializeLiveStateAndRejectSynchronousOverlapAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out MethodState halt);
            machine.ClearChangeMasks(m_context, true);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var liveEvents = new List<uint>();
            var liveChanges = new List<uint>();
            machine.SetAreEventsMonitored(m_context, true, false);
            machine.OnReportEventAsync = async (_, _, _, _) =>
            {
                liveEvents.Add(CurrentStateId(machine));
                if (liveEvents.Count == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                    liveEvents.Add(CurrentStateId(machine));
                }
            };
            machine.CurrentState!.OnStateChangedAsync = (_, _, _, _) =>
            {
                liveChanges.Add(CurrentStateId(machine));
                return default;
            };
            Task<ServiceResult> first = machine.DoTransitionAsync(
                m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, []).AsTask();
            Task<ServiceResult>? second = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                second = machine.DoTransitionAsync(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, []).AsTask();
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(machine.DoCause(
                    m_context, halt, Methods.ProgramStateMachineType_Halt, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(machine.DoTransition(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException? error = Assert.Throws<ServiceResultException>(
                    () => machine.SetState(m_context, Objects.ProgramStateMachineType_Halted));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Running));
            }
            finally
            {
                release.TrySetResult(true);
                await first.WaitAsync(s_timeout).ConfigureAwait(false);
                if (second != null)
                {
                    await second.WaitAsync(s_timeout).ConfigureAwait(false);
                }
            }
            Assert.That(liveEvents, Is.EqualTo(new[]
            {
                Objects.ProgramStateMachineType_Running,
                Objects.ProgramStateMachineType_Running,
                Objects.ProgramStateMachineType_Halted
            }));
            Assert.That(liveChanges, Is.EqualTo(new[]
            {
                Objects.ProgramStateMachineType_Running,
                Objects.ProgramStateMachineType_Halted
            }));
            machine.SetState(m_context, Objects.ProgramStateMachineType_Ready);
            Assert.That(ServiceResult.IsGood(machine.DoCause(
                m_context, null, Methods.ProgramStateMachineType_Start, default, [])), Is.True);
        }

        [Test]
        public async Task NativeNotificationCallbackRejectsSelfReentrancyWithoutWaitingAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);
            machine.SetAreEventsMonitored(m_context, true, false);
            ServiceResult? callbackResult = null;
            machine.OnReportEventAsync = async (_, _, _, _) =>
            {
                callbackResult = await machine.DoTransitionAsync(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, [], CancellationToken.None)
                    .AsTask().WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
            };
            ServiceResult result = await machine.DoTransitionAsync(
                m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, [])
                .AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(callbackResult, Is.Not.Null);
            Assert.That(callbackResult!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Running));
        }

        [Test]
        public async Task NativeStateSinkKeepsAdmissionAndCanceledWaiterCannotMutateAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);
            machine.ClearChangeMasks(m_context, true);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            ServiceResult? reentrant = null;
            machine.CurrentState!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                reentrant = await machine.DoCauseAsync(
                    m_context, null, Methods.ProgramStateMachineType_Halt, default, [], CancellationToken.None)
                    .AsTask().WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
            };
            Task<ServiceResult> first = machine.DoTransitionAsync(
                m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, []).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Task<ServiceResult> waiting = machine.DoTransitionAsync(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, [], cancellation.Token)
                    .AsTask();
                Assert.That(waiting.IsCompleted, Is.False);
                cancellation.Cancel();
                await Assert.CatchAsync<OperationCanceledException>(
                    async () => await waiting.WaitAsync(s_timeout).ConfigureAwait(false));
                Assert.That(reentrant!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(machine.DoTransition(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Running));
                Assert.That(machine.GetTransitionId(machine.LastTransition!.Id!.Value),
                    Is.EqualTo(Objects.ProgramStateMachineType_ReadyToRunning));
            }
            finally
            {
                release.TrySetResult(true);
                await first.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            machine.CurrentState!.OnStateChangedAsync = null;
            ServiceResult next = await machine.DoTransitionAsync(
                m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, [])
                .AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(next), Is.True);
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Halted));
        }

        [Test]
        public void LegacySynchronousTransitionAllowsCallbackTransitionsOutsideAsyncAdmission()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);
            ServiceResult? nested = null;
            machine.OnAfterTransition = (_, _, transition, _, _, _) =>
            {
                if (transition == Objects.ProgramStateMachineType_ReadyToRunning)
                {
                    nested = machine.DoTransition(
                        m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, []);
                }
                return ServiceResult.Good;
            };
            ServiceResult result = machine.DoTransition(
                m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, []);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(nested, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(nested), Is.True);
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Halted));
        }

        [Test]
        public async Task NativeCauseAuditsRefusalWithoutProgramTransitionAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out MethodState start, out _);
            machine.SetState(m_context, Objects.ProgramStateMachineType_Halted);
            var events = new List<IFilterTarget>();
            machine.SetAreEventsMonitored(m_context, true, false);
            machine.OnReportEventAsync = (_, _, e, _) =>
            {
                events.Add(e);
                return default;
            };
            ServiceResult result = await machine.DoCauseAsync(
                m_context, start, Methods.ProgramStateMachineType_Start, default, [])
                .AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(((AuditUpdateStateEventState)events[0]).Status!.Value, Is.False);
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Halted));
        }

        [Test]
        public async Task NativeHierarchyCompletionAwaitsChildBeforeObserverAndRetainsAdmissionAsync()
        {
            ProgramStateMachineState parent = CreateMachine(out _, out _);
            ProgramStateMachineState child = CreateMachine(out _, out _);
            ProgramStateMachineState grandchild = CreateMachine(out _, out _);
            child.AddChild(grandchild);
            parent.AddChild(child);
            parent.ClearChangeMasks(m_context, true);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var order = new List<string>();
            int syncAfterCalls = 0;
            parent.TransitionCallbackFactory = (_, _, before, _) =>
                (before, (_, _, _, _, _, _) =>
                {
                    syncAfterCalls++;
                    return ServiceResult.Good;
                });
            child.InitialStateSynchronizationAsync += async (context, _) =>
                await grandchild.SetStateAndActiveAsync(
                    context, Objects.ProgramStateMachineType_Halted, active: false).ConfigureAwait(false);
            grandchild.CurrentState!.OnStateChangedAsync = (_, _, _, _) =>
            {
                order.Add("grandchild");
                return default;
            };
            child.CurrentState!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                order.Add("child");
                entered.TrySetResult(true);
                await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
            };
            parent.AsyncTransitionCallbackFactory = (_, _, before, _, _) =>
                (before, (_, _, _, _, _, _, _) =>
                {
                    parent.ScheduleAsyncTransitionCompletion(child.NodeId, () => child.SetStateAndActiveAsync(
                        m_context, Objects.ProgramStateMachineType_Halted, active: false));
                    parent.ScheduleAsyncTransitionObserver(() =>
                    {
                        order.Add("observer");
                        Assert.That(child.CurrentState!.StatusCode, Is.EqualTo(StatusCodes.BadStateNotActive));
                        Assert.That(grandchild.CurrentState!.StatusCode, Is.EqualTo(StatusCodes.BadStateNotActive));
                        return default;
                    });
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            Task<ServiceResult> transition = parent.DoTransitionAsync(
                m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, []).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(transition.IsCompleted, Is.False);
                Assert.That(parent.DoTransition(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(child.DoTransition(
                    m_context, Objects.ProgramStateMachineType_HaltedToReady, 0, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(order, Is.EqualTo(s_expectedPendingHierarchyOrder));
            }
            finally
            {
                release.TrySetResult(true);
                await transition.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            Assert.That(syncAfterCalls, Is.Zero);
            Assert.That(order, Is.EqualTo(s_expectedHierarchyOrder));
        }

        [Test]
        public async Task NativeCompletionAndObserverFailuresDoNotSkipLaterWorkAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);
            machine.ClearChangeMasks(m_context, true);
            var completionFailure = new InvalidOperationException("completion");
            var observerFailure = new InvalidOperationException("observer");
            var stateFailure = new InvalidOperationException("state");
            var order = new List<string>();
            machine.AsyncTransitionCallbackFactory = (_, _, before, _, _) =>
                (before, (_, _, _, _, _, _, _) =>
                {
                    machine.ScheduleAsyncTransitionCompletion(new NodeId("first", 0), () =>
                    {
                        order.Add("completion");
                        return new ValueTask(Task.FromException(completionFailure));
                    });
                    machine.ScheduleAsyncTransitionCompletion(new NodeId("second", 0), () =>
                    {
                        order.Add("next completion");
                        return default;
                    });
                    machine.ScheduleAsyncTransitionObserver(() =>
                    {
                        order.Add("observer");
                        return new ValueTask(Task.FromException(observerFailure));
                    });
                    machine.ScheduleAsyncTransitionObserver(() =>
                    {
                        order.Add("next observer");
                        return default;
                    });
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            machine.CurrentState!.OnStateChangedAsync = (_, _, _, _) =>
            {
                order.Add("state");
                return new ValueTask(Task.FromException(stateFailure));
            };
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await machine.DoTransitionAsync(
                    m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, [])
                    .AsTask().WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(failure!.InnerExceptions,
                Is.EqualTo(new Exception[] { completionFailure, observerFailure, stateFailure }));
            Assert.That(order,
                Is.EqualTo(s_expectedCompletionOrder));
            machine.CurrentState!.OnStateChangedAsync = null;
            machine.AsyncTransitionCallbackFactory = null;
            Assert.That(ServiceResult.IsGood(machine.DoTransition(
                m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, [])), Is.True);
        }

        [Test]
        public async Task NativeDeliveryPreservesPerNodeShortCircuitAndContinuesToSiblingAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);
            machine.ClearChangeMasks(m_context, true);
            var original = new InvalidOperationException("first node sink");
            bool laterSameNodeSink = false;
            bool siblingSink = false;
            machine.CurrentState!.OnStateChangedAsync = (_, _, _, _) =>
                new ValueTask(Task.FromException(original));
            machine.CurrentState.StateChangedAsync += (_, _, _, _) =>
            {
                laterSameNodeSink = true;
                return default;
            };
            machine.LastTransition!.OnStateChangedAsync = (_, _, _, _) =>
            {
                siblingSink = true;
                return default;
            };
            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await machine.DoTransitionAsync(
                    m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, [])
                    .AsTask().WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(observed, Is.SameAs(original));
            Assert.That(laterSameNodeSink, Is.False);
            Assert.That(siblingSink, Is.True);
        }

        [Test]
        public async Task NativeCompositePublicationRejectsDirectWritesAndCallbackReentrancyAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);
            machine.ClearChangeMasks(m_context, true);
            machine.SetState(m_context, Objects.ProgramStateMachineType_Running);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ServiceResultException? reentrant = null;
            machine.CurrentState!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                reentrant = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await machine.PublishStateChangesAsync(m_context, true, CancellationToken.None)
                        .ConfigureAwait(false));
                entered.TrySetResult(true);
                await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
            };
            Task publishing = machine.PublishStateChangesAsync(m_context, true).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(publishing.IsCompleted, Is.False);
                Assert.That(reentrant!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(machine.DoTransition(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException? error = Assert.Throws<ServiceResultException>(
                    () => machine.SetState(m_context, Objects.ProgramStateMachineType_Halted));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            finally
            {
                release.TrySetResult(true);
                await publishing.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            machine.CurrentState!.OnStateChangedAsync = null;
            machine.SetState(m_context, Objects.ProgramStateMachineType_Halted);
            Assert.That(CurrentStateId(machine), Is.EqualTo(Objects.ProgramStateMachineType_Halted));
        }

        [Test]
        public async Task NativeAfterCallbackIsAwaitedAndReplacesOnlyLegacyAsyncPathAsync()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int synchronousCalls = 0;
            int asynchronousCalls = 0;
            machine.OnAfterTransition = (_, _, _, _, _, _) =>
            {
                synchronousCalls++;
                return ServiceResult.Good;
            };
            machine.OnAfterTransitionAsync = async (_, _, _, _, _, _, ct) =>
            {
                asynchronousCalls++;
                Assert.That(ct.CanBeCanceled, Is.False);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                return ServiceResult.Good;
            };
            Task<ServiceResult> transition = machine.DoTransitionAsync(
                m_context, Objects.ProgramStateMachineType_ReadyToRunning, 0, default, []).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(transition.IsCompleted, Is.False);
                Assert.That(synchronousCalls, Is.Zero);
                Assert.That(machine.DoTransition(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
            }
            finally
            {
                release.TrySetResult(true);
                await transition.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            Assert.That(ServiceResult.IsGood(machine.DoTransition(
                m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, [])), Is.True);
            Assert.That(synchronousCalls, Is.EqualTo(1));
            Assert.That(asynchronousCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task NativeCompositePublicationReservesChildUntilRootDeliveryCompletesAsync()
        {
            ProgramStateMachineState parent = CreateMachine(out _, out _);
            ProgramStateMachineState child = CreateMachine(out _, out _);
            parent.AddChild(child);
            parent.ClearChangeMasks(m_context, true);
            parent.SetState(m_context, Objects.ProgramStateMachineType_Running);
            child.SetState(m_context, Objects.ProgramStateMachineType_Running);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ServiceResult? reentrant = null;
            child.CurrentState!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                reentrant = await child.DoTransitionAsync(
                    m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, [], CancellationToken.None)
                    .AsTask().WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
            };
            parent.CurrentState!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
            };
            Task publishing = parent.PublishStateChangesAsync(m_context, true).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(publishing.IsCompleted, Is.False);
                Assert.That(child.DoCause(
                    m_context, null, Methods.ProgramStateMachineType_Halt, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException? failure = Assert.Throws<ServiceResultException>(
                    () => child.SetState(m_context, Objects.ProgramStateMachineType_Halted));
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(CurrentStateId(child), Is.EqualTo(Objects.ProgramStateMachineType_Running));
            }
            finally
            {
                release.TrySetResult(true);
                await publishing.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            Assert.That(reentrant, Is.Not.Null);
            Assert.That(reentrant!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            child.CurrentState!.OnStateChangedAsync = null;
            Assert.That(ServiceResult.IsGood(child.DoTransition(
                m_context, Objects.ProgramStateMachineType_RunningToHalted, 0, default, [])), Is.True);
        }

        [Test]
        public async Task CompositeAdmissionReservesRootAndSiblingsDuringChildCauseNotificationAsync()
        {
            ProgramStateMachineState root = CreateMachine(out _, out _);
            ProgramStateMachineState execute = CreateMachine(out MethodState start, out _);
            ProgramStateMachineState suspended = CreateMachine(out _, out _);
            root.AddChild(execute);
            root.AddChild(suspended);
            root.ClearChangeMasks(m_context, true);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ServiceResult? callbackCause = null;
            ServiceResult? callbackTransition = null;
            ServiceResult? callbackComposite = null;
            ServiceResultException? callbackState = null;
            execute.SetAreEventsMonitored(m_context, true, false);
            execute.OnReportEventAsync = async (_, _, e, _) =>
            {
                if (e is not ProgramTransitionEventState)
                {
                    return;
                }
                callbackCause = await root.DoCauseAsync(
                    m_context, null, Methods.ProgramStateMachineType_Halt, default, [], CancellationToken.None)
                    .AsTask().WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                callbackTransition = await suspended.DoTransitionAsync(
                    m_context, Objects.ProgramStateMachineType_ReadyToHalted, 0, default, [], CancellationToken.None)
                    .AsTask().WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                callbackComposite = await root.ExecuteWithStateAdmissionAsync(
                    m_context, _ => new ValueTask<ServiceResult>(ServiceResult.Good), CancellationToken.None)
                    .AsTask().WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                callbackState = Assert.Throws<ServiceResultException>(
                    () => root.SetState(m_context, Objects.ProgramStateMachineType_Halted));
                entered.TrySetResult(true);
                await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
            };
            Task<ServiceResult> operation = root.ExecuteWithStateAdmissionAsync(m_context, async ct =>
            {
                root.SetState(m_context, Objects.ProgramStateMachineType_Running);
                ServiceResult result = await execute.DoCauseAsync(
                    m_context, start, Methods.ProgramStateMachineType_Start, default, [], ct).ConfigureAwait(false);
                suspended.SetState(m_context, Objects.ProgramStateMachineType_Halted);
                await root.PublishStateChangesAsync(m_context, true, CancellationToken.None).ConfigureAwait(false);
                return result;
            }).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(root.DoCause(
                    m_context, null, Methods.ProgramStateMachineType_Halt, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(suspended.DoTransition(
                    m_context, Objects.ProgramStateMachineType_ReadyToHalted, 0, default, []).StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException? externalState = Assert.Throws<ServiceResultException>(
                    () => root.SetState(m_context, Objects.ProgramStateMachineType_Halted));
                Assert.That(externalState!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(callbackCause!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(callbackTransition!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(callbackComposite!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(callbackState!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(CurrentStateId(root), Is.EqualTo(Objects.ProgramStateMachineType_Running));
                Assert.That(CurrentStateId(suspended), Is.EqualTo(Objects.ProgramStateMachineType_Ready));
            }
            finally
            {
                release.TrySetResult(true);
                await operation.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            Assert.That(CurrentStateId(execute), Is.EqualTo(Objects.ProgramStateMachineType_Running));
            Assert.That(CurrentStateId(suspended), Is.EqualTo(Objects.ProgramStateMachineType_Halted));
            root.SetState(m_context, Objects.ProgramStateMachineType_Ready);
            Assert.That(CurrentStateId(root), Is.EqualTo(Objects.ProgramStateMachineType_Ready));
        }

        [Test]
        public async Task CompositeAdmissionReleasesEveryMachineWhenOperationThrowsAsync()
        {
            ProgramStateMachineState root = CreateMachine(out _, out _);
            ProgramStateMachineState child = CreateMachine(out _, out _);
            root.AddChild(child);
            var original = new InvalidOperationException("composite operation");
            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await root.ExecuteWithStateAdmissionAsync(
                    m_context, _ => new ValueTask<ServiceResult>(Task.FromException<ServiceResult>(original)))
                    .AsTask().WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(failure, Is.SameAs(original));
            root.SetState(m_context, Objects.ProgramStateMachineType_Running);
            child.SetState(m_context, Objects.ProgramStateMachineType_Running);
            Assert.That(CurrentStateId(root), Is.EqualTo(Objects.ProgramStateMachineType_Running));
            Assert.That(CurrentStateId(child), Is.EqualTo(Objects.ProgramStateMachineType_Running));
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        private static readonly string[] s_expectedNotificationOrder =
            ["transition", "state", "last transition", "audit", "program"];

        private static readonly string[] s_expectedPendingHierarchyOrder = ["grandchild", "child"];
        private static readonly string[] s_expectedHierarchyOrder = ["grandchild", "child", "observer"];
        private static readonly string[] s_expectedCompletionOrder =
            ["completion", "next completion", "observer", "next observer", "state"];

        /// <summary>
        /// Builds a machine with two of the five optional cause methods
        /// declared, so the test also covers the "only the materialized
        /// ones are wired" half.
        /// </summary>
        private TestProgramStateMachineState CreateMachine(
            out MethodState start,
            out MethodState halt)
        {
            var machine = new TestProgramStateMachineState
            {
                NodeId = new NodeId("Machine", kNs),
                BrowseName = new QualifiedName("Machine", kNs),
                DisplayName = new LocalizedText("Machine")
            };

            machine.Create(
                m_context,
                machine.NodeId,
                machine.BrowseName,
                displayName: machine.DisplayName,
                assignNodeIds: false);

            start = machine.Start;
            halt = machine.Halt;
            return machine;
        }

        private static MethodState AddMethod(
            ProgramStateMachineState machine,
            string browseName,
            string nodeIdentifier)
        {
            // Executable / UserExecutable default to true on a freshly
            // constructed node — the very value the fix has to replace.
            var method = new MethodState(machine)
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(nodeIdentifier, kNs),
                BrowseName = new QualifiedName(browseName, kNs),
                DisplayName = new LocalizedText(browseName),
                Executable = true,
                UserExecutable = true
            };
            machine.AddChild(method);
            return method;
        }

        /// <summary>
        /// Stands in for a generator-emitted subclass whose model
        /// declares two of the optional cause methods. They have to be
        /// materialized in <see cref="InitializeOptionalChildren"/>
        /// because <see cref="NodeState.Create(ISystemContext, NodeId,
        /// QualifiedName, LocalizedText, bool)"/> rebuilds the child
        /// list from the type template.
        /// </summary>
        private sealed class TestProgramStateMachineState : ProgramStateMachineState
        {
            public TestProgramStateMachineState()
                : base(parent: null)
            {
            }

            public MethodState Start { get; private set; } = null!;

            public MethodState Halt { get; private set; } = null!;

            protected override void InitializeOptionalChildren(ISystemContext context)
            {
                base.InitializeOptionalChildren(context);

                Start = AddMethod(this, BrowseNames.Start, "Machine_Start");
                Halt = AddMethod(this, BrowseNames.Halt, "Machine_Halt");
            }
        }

        /// <summary>
        /// Customizes cause mapping and records native versus legacy virtual dispatch.
        /// </summary>
        private sealed class RecordingProgramStateMachineState : ProgramStateMachineState
        {
            public const uint CustomCauseId = 777;

            public RecordingProgramStateMachineState()
                : base(parent: null)
            {
            }

            public int SyncTransitions { get; private set; }

            public int SyncCauses { get; private set; }

            public int AsyncCauses { get; private set; }

            public int AsyncTransitions { get; private set; }

            public override ServiceResult DoCause(
                ISystemContext context,
                MethodState? causeMethod,
                uint causeId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments)
            {
                SyncCauses++;
                return base.DoCause(context, causeMethod, causeId, inputArguments, outputArguments);
            }

            public override ValueTask<ServiceResult> DoCauseAsync(
                ISystemContext context,
                MethodState? causeMethod,
                uint causeId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments,
                CancellationToken cancellationToken = default)
            {
                AsyncCauses++;
                return base.DoCauseAsync(
                    context, causeMethod, causeId, inputArguments, outputArguments, cancellationToken);
            }

            protected override uint GetTransitionForCause(ISystemContext context, uint causeId)
            {
                return causeId == CustomCauseId && CurrentStateId(this) == Objects.ProgramStateMachineType_Ready
                    ? Objects.ProgramStateMachineType_ReadyToRunning
                    : base.GetTransitionForCause(context, causeId);
            }

            public override ServiceResult DoTransition(
                ISystemContext context,
                uint transitionId,
                uint causeId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments)
            {
                SyncTransitions++;
                return base.DoTransition(context, transitionId, causeId, inputArguments, outputArguments);
            }

            public override ValueTask<ServiceResult> DoTransitionAsync(
                ISystemContext context,
                uint transitionId,
                uint causeId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments,
                CancellationToken cancellationToken = default)
            {
                AsyncTransitions++;
                return base.DoTransitionAsync(
                    context, transitionId, causeId, inputArguments, outputArguments, cancellationToken);
            }
        }

        private bool ReadAttribute(MethodState method, uint attributeId)
        {
            var value = new DataValue();
            ServiceResult result = method.ReadAttribute(
                m_context, attributeId, default, default, ref value);

            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"read of {Attributes.GetBrowseName(attributeId)} failed: {result}");
            Assert.That(value.WrappedValue.TryGetValue(out bool flag), Is.True,
                $"{Attributes.GetBrowseName(attributeId)} must read as a Boolean.");
            return flag;
        }

        private static uint CurrentStateId(ProgramStateMachineState machine)
        {
            NodeId stateNodeId = machine.CurrentState!.Id!.Value;
            return stateNodeId.TryGetValue(out uint id) ? id : 0;
        }
    }
}
