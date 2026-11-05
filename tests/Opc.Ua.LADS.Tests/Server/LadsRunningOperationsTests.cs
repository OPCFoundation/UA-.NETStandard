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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.LADS.Client;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Integration;
using Opc.Ua.Server.Hosting;
using ClientUnit = Opc.Ua.LADS.Client.LadsFunctionalUnit;
using LadsFunction = Opc.Ua.LADS.Client.LadsFunction;
using LadsFunctionalUnit = Opc.Ua.LADS.Server.LadsFunctionalUnit;
using LadsNotificationTestSupport = Opc.Ua.LADS.Tests.Server.LadsMaintenanceTaskTests.LadsNotificationTestSupport;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests acknowledged running operations and cooperative program execution.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class LadsRunningOperationsTests
    {
        /// <summary>
        /// Measures pauses at committed states, independently of delayed or failed node notifications.
        /// </summary>
        [TestCase(LadsRunningStateValue.Held, "None")]
        [TestCase(LadsRunningStateValue.Held, "Pause")]
        [TestCase(LadsRunningStateValue.Held, "Resume")]
        [TestCase(LadsRunningStateValue.Suspended, "None")]
        [TestCase(LadsRunningStateValue.Suspended, "Pause")]
        [TestCase(LadsRunningStateValue.Suspended, "Resume")]
        public async Task PauseTimingPrecedesStateNotificationsAsync(
            LadsRunningStateValue pausedState,
            string failureStage)
        {
            var clock = new ManualClock();
            await using LadsClientConnection connection = await StartAsync(
                nameof(PauseTimingPrecedesStateNotificationsAsync), clock: clock).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            await client.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await LadsAssert.EventuallyAsync(
                () => clock.HasDelay(TimeSpan.FromMinutes(10)),
                "The first step did not enter its delay.").ConfigureAwait(false);
            LadsProgramRun run = unit.Programs!.ActiveRun!;
            var stateNode = unit.Node.FunctionalUnitState!.RunningStateMachine!.CurrentState!;
            NodeStateChangedAsyncHandler? previous = stateNode.OnStateChangedAsync;
            var failure = new InvalidOperationException("Running-state notification failed.");
            var paused = new LadsNotificationTestSupport.Gate
            {
                Failure = failureStage == "Pause" ? failure : null
            };
            var resumed = new LadsNotificationTestSupport.Gate
            {
                Failure = failureStage == "Resume" ? failure : null
            };
            var publicStates = new List<LadsRunningStateValue>();
            unit.Running.StateChanged += publicStates.Add;
            stateNode.OnStateChangedAsync = async (context, node, masks, token) =>
            {
                if (unit.RunningState == pausedState)
                {
                    await paused.WaitAsync(token).ConfigureAwait(false);
                }
                else if (unit.RunningState == LadsRunningStateValue.Execute)
                {
                    await resumed.WaitAsync(token).ConfigureAwait(false);
                }
                if (previous != null)
                {
                    await previous(context, node, masks, token).ConfigureAwait(false);
                }
            };
            Task? pause = null;
            Task? resume = null;
            try
            {
                clock.Advance(TimeSpan.FromMinutes(2));
                pause = pausedState == LadsRunningStateValue.Held
                    ? unit.RunningOperations!.HoldAsync().AsTask()
                    : unit.RunningOperations!.SuspendAsync().AsTask();
                await paused.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromMinutes(3));
                Assert.That(pause.IsCompleted, Is.False);
                Assert.That(unit.RunningState, Is.EqualTo(pausedState));
                Assert.That(publicStates, Does.Not.Contain(pausedState));
                Assert.That(run.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
                Assert.That(run.CurrentStepRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
                Assert.That(run.TotalPauseTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                paused.Release.TrySetResult(true);
                if (failureStage == "Pause")
                {
                    await Assert.ThatAsync(
                        () => pause.WaitAsync(LadsNotificationTestSupport.Timeout),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                }
                else
                {
                    await pause.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                }
                Assert.That(publicStates.Contains(pausedState), Is.EqualTo(failureStage != "Pause"));

                resume = pausedState == LadsRunningStateValue.Held
                    ? unit.RunningOperations!.UnholdAsync().AsTask()
                    : unit.RunningOperations!.UnsuspendAsync().AsTask();
                await resumed.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromMinutes(1));
                Assert.That(resume.IsCompleted, Is.False);
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
                Assert.That(publicStates, Does.Not.Contain(LadsRunningStateValue.Execute));
                Assert.That(run.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                Assert.That(run.CurrentStepRuntime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                Assert.That(run.TotalPauseTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                resumed.Release.TrySetResult(true);
                if (failureStage == "Resume")
                {
                    await Assert.ThatAsync(
                        () => resume.WaitAsync(LadsNotificationTestSupport.Timeout),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                }
                else
                {
                    await resume.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                }
                Assert.That(
                    publicStates.Contains(LadsRunningStateValue.Execute),
                    Is.EqualTo(failureStage != "Resume"));
                Assert.That(run.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                Assert.That(run.TotalPauseTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
            }
            finally
            {
                paused.Release.TrySetResult(true);
                resumed.Release.TrySetResult(true);
                if (pause != null)
                {
                    await LadsNotificationTestSupport.DrainAsync(pause).ConfigureAwait(false);
                }
                if (resume != null)
                {
                    await LadsNotificationTestSupport.DrainAsync(resume).ConfigureAwait(false);
                }
                stateNode.OnStateChangedAsync = previous;
                unit.Running.StateChanged -= publicStates.Add;
            }
        }

        [Test]
        public async Task LegacyUnitsOmitRunningOperationsAndStillStopOnCompletionAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(LegacyUnitsOmitRunningOperationsAndStillStopOnCompletionAsync)).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ClientUnit client = await connection.ChamberAsync().ConfigureAwait(false);

            Assert.That(unit.RunningOperations, Is.Null);
            Assert.That((await client.GetRunningMethodsAsync().ConfigureAwait(false)).Count, Is.Zero);
            string runId = await client.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult result = await unit.Programs!.WaitForResultAsync(runId).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Complete));
            Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
            LadsFunctionalUnitSnapshot snapshot = await client.ReadAsync(false).ConfigureAwait(false);
            Assert.That(snapshot.State!.CurrentState, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task ClientHoldAndSuspendFreezeStepsSetpointsAndProcessingTimeAsync()
        {
            var clock = new ManualClock();
            await using LadsClientConnection connection = await StartAsync(
                nameof(ClientHoldAndSuspendFreezeStepsSetpointsAndProcessingTimeAsync), clock: clock)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            ILadsProgramManager programs = unit.Programs!;
            IAnalogControlFunction temperature = unit.GetFunction<IAnalogControlFunction>("Temperature");

            Assert.That(unit.RunningOperations, Is.Not.Null);
            await AssertMethodsAsync(client).ConfigureAwait(false);
            string runId = await client.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await LadsAssert.EventuallyAsync(
                () => clock.HasDelay(TimeSpan.FromMinutes(10)),
                "The first step did not enter its delay.").ConfigureAwait(false);
            LadsProgramRun run = programs.ActiveRun!;
            Assert.That(run.CurrentStepNumber, Is.EqualTo(1));
            Assert.That(temperature.TargetValue, Is.EqualTo(10));
            await AssertMethodsAsync(client, "Hold", "Suspend", "ToComplete").ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState, () => unit.RunningOperations!.ResetAsync()).ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMinutes(2));
            await client.HoldAsync().ConfigureAwait(false);
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Held));
            await AssertMethodsAsync(client, "Unhold").ConfigureAwait(false);
            using (var cancellation = new CancellationTokenSource())
            {
                Task<bool> checkpoint = run.WaitForExecutionAsync(cancellation.Token).AsTask();
                Task setpoint = run.ApplySetpointAsync(
                    new LadsSetpoint("Temperature", Variant.From(99.0)), cancellation.Token).AsTask();
                Assert.That(checkpoint.IsCompleted, Is.False);
                Assert.That(setpoint.IsCompleted, Is.False);
                clock.Advance(TimeSpan.FromMinutes(3));
                Assert.That(run.CurrentStepNumber, Is.EqualTo(1));
                Assert.That(temperature.TargetValue, Is.EqualTo(10));
                Assert.That(run.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
                Assert.That(run.CurrentStepRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
                Assert.That(run.CurrentPauseTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                Assert.That(run.TotalPauseTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                LadsActiveProgram? held = await client.ReadActiveProgramAsync().ConfigureAwait(false);
                Assert.That(held, Is.Not.Null);
                Assert.That(held!.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
                Assert.That(held.CurrentStepRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
                Assert.That(held.CurrentPauseTime, Is.EqualTo(TimeSpan.FromMinutes(3)));
                cancellation.Cancel();
                await Assert.ThatAsync(() => checkpoint, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
                await Assert.ThatAsync(() => setpoint, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
            }
            await client.UnholdAsync().ConfigureAwait(false);
            await LadsAssert.EventuallyAsync(
                () => clock.HasDelay(TimeSpan.FromMinutes(8)),
                "Unhold did not resume the unconsumed delay.").ConfigureAwait(false);
            Assert.That(await run.WaitForExecutionAsync().ConfigureAwait(false), Is.True);
            clock.Advance(TimeSpan.FromMinutes(1));

            await client.SuspendAsync().ConfigureAwait(false);
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Suspended));
            await AssertMethodsAsync(client, "Hold", "Unsuspend").ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMinutes(5));
            Assert.That(run.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(3)));
            Assert.That(run.CurrentStepRuntime, Is.EqualTo(TimeSpan.FromMinutes(3)));
            Assert.That(run.CurrentPauseTime, Is.EqualTo(TimeSpan.FromMinutes(8)));
            Assert.That(run.TotalPauseTime, Is.EqualTo(TimeSpan.FromMinutes(8)));
            Assert.That(run.CurrentStepNumber, Is.EqualTo(1));
            Assert.That(temperature.TargetValue, Is.EqualTo(10));
            Assert.That(programs.Results.Count, Is.Zero);

            await client.UnsuspendAsync().ConfigureAwait(false);
            await LadsAssert.EventuallyAsync(
                () => clock.HasDelay(TimeSpan.FromMinutes(7)),
                "Unsuspend did not resume the unconsumed delay.").ConfigureAwait(false);
            LadsActiveProgram? active = await client.ReadActiveProgramAsync().ConfigureAwait(false);
            Assert.That(active, Is.Not.Null);
            Assert.That(active!.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(3)));
            Assert.That(active.CurrentStepRuntime, Is.EqualTo(TimeSpan.FromMinutes(3)));
            Assert.That(active.CurrentPauseTime, Is.EqualTo(TimeSpan.FromMinutes(8)));
            await AssertMethodsAsync(client, "Hold", "Suspend", "ToComplete").ConfigureAwait(false);

            clock.AdjustUtc(TimeSpan.FromHours(1));
            clock.Advance(TimeSpan.FromMinutes(7) - TimeSpan.FromTicks(1));
            Assert.That(run.CurrentStepNumber, Is.EqualTo(1));
            Assert.That(temperature.TargetValue, Is.EqualTo(10));
            Assert.That(programs.Results.Count, Is.Zero);
            clock.Advance(TimeSpan.FromTicks(1));
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That(run.CurrentStepNumber, Is.EqualTo(2));
            Assert.That(temperature.TargetValue, Is.EqualTo(20));
            Assert.That(run.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(10)));
            Assert.That(run.CurrentStepRuntime, Is.EqualTo(TimeSpan.Zero));
            Assert.That(run.TotalPauseTime, Is.EqualTo(TimeSpan.FromMinutes(8)));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Complete));
            Assert.That(device.OperationDuration, Is.EqualTo(TimeSpan.FromMinutes(18)));
            Opc.Ua.LADS.Client.LadsResult? published = await client.FindResultAsync(runId).ConfigureAwait(false);
            Assert.That(published, Is.Not.Null);
            Assert.That(published!.TotalRuntime, Is.EqualTo(TimeSpan.FromMinutes(18)));
            Assert.That(published.TotalPauseTime, Is.EqualTo(TimeSpan.FromMinutes(8)));
            await AssertMethodsAsync(client, "Reset").ConfigureAwait(false);
            clock.Advance(TimeSpan.FromHours(1));
            Assert.That(run.CurrentRuntime, Is.EqualTo(TimeSpan.FromMinutes(10)));
            Assert.That(run.TotalPauseTime, Is.EqualTo(TimeSpan.FromMinutes(8)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OrderlyCompletionPublishesOneResultAndRequiresResetBeforeRepeatAsync(bool paused)
        {
            await using LadsClientConnection connection = await StartAsync(
                nameof(OrderlyCompletionPublishesOneResultAndRequiresResetBeforeRepeatAsync)).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ILadsProgramManager programs = unit.Programs!;
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            int resultEvents = 0;
            programs.ResultAdded += _ => Interlocked.Increment(ref resultEvents);

            for (int cycle = 1; cycle <= 2; cycle++)
            {
                string runId = await client.StartProgramAsync(TemplateName).ConfigureAwait(false);
                await LadsAssert.EventuallyAsync(
                    () => unit.GetFunction<IAnalogControlFunction>("Temperature").State ==
                        LadsFunctionalStateValue.Running,
                    "The executor did not apply the first setpoint.").ConfigureAwait(false);
                LadsProgramRun run = programs.ActiveRun!;
                if (paused)
                {
                    await client.HoldAsync().ConfigureAwait(false);
                    await client.UnholdAsync().ConfigureAwait(false);
                }
                await client.ToCompleteAsync().ConfigureAwait(false);
                ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);

                Assert.That(run.IsCompletionRequested, Is.True);
                Assert.That(await run.WaitForExecutionAsync().ConfigureAwait(false), Is.False);
                Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
                Assert.That(result.Files.Count, Is.EqualTo(1));
                Assert.That(programs.Results.Count, Is.EqualTo(cycle));
                Assert.That(await programs.WaitForResultAsync(runId).ConfigureAwait(false), Is.SameAs(result));
                Assert.That(programs.ActiveRun, Is.Null);
                Assert.That(device.OperationCycleCounter, Is.EqualTo(cycle));
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Complete));
                Assert.That(
                    unit.GetFunction<IAnalogControlFunction>("Temperature").State,
                    Is.EqualTo(LadsFunctionalStateValue.Stopped));
                Assert.That(
                    unit.GetFunction<IAnalogControlFunction>("Temperature").TargetValue,
                    Is.EqualTo(10));
                await LadsAssert.EventuallyAsync(
                    () => Volatile.Read(ref resultEvents) == cycle,
                    "Completion did not publish exactly one result event.").ConfigureAwait(false);
                LadsFunctionalUnitSnapshot snapshot = await client.ReadAsync(false).ConfigureAwait(false);
                Assert.That(snapshot.State!.CurrentState, Is.EqualTo("Running"));
                Assert.That(snapshot.RunningState!.CurrentState, Is.EqualTo("Complete"));
                Assert.That((await client.GetResultsAsync().ConfigureAwait(false)).Count, Is.EqualTo(cycle));
                await AssertMethodsAsync(client, "Reset").ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadInvalidState,
                    () => programs.StartProgramAsync(TemplateName)).ConfigureAwait(false);

                await client.ResetAsync().ConfigureAwait(false);
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
                Assert.That(programs.ActiveRun, Is.Null);
                Assert.That(programs.Results.Count, Is.EqualTo(cycle));
                Assert.That(device.OperationCycleCounter, Is.EqualTo(cycle));
                await AssertMethodsAsync(client).ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadInvalidState, () => unit.RunningOperations!.ResetAsync()).ConfigureAwait(false);
            }
            Assert.That(resultEvents, Is.EqualTo(2));
            Assert.That(programs.Results[0].RunId, Is.Not.EqualTo(programs.Results[1].RunId));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task StopAndAbortCancelPausedExecutionWithoutCountingACompletedCycleAsync(
            bool abort,
            bool suspended)
        {
            await using LadsClientConnection connection = await StartAsync(
                nameof(StopAndAbortCancelPausedExecutionWithoutCountingACompletedCycleAsync)).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            string runId = await client.StartProgramAsync(TemplateName).ConfigureAwait(false);
            if (suspended)
            {
                await client.SuspendAsync().ConfigureAwait(false);
            }
            else
            {
                await client.HoldAsync().ConfigureAwait(false);
            }
            Assert.That(unit.RunningState, Is.EqualTo(
                suspended ? LadsRunningStateValue.Suspended : LadsRunningStateValue.Held));
            if (abort)
            {
                await client.AbortAsync().ConfigureAwait(false);
            }
            else
            {
                await client.StopAsync().ConfigureAwait(false);
            }
            ILadsResult result = await unit.Programs!.WaitForResultAsync(runId).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(
                abort ? LadsProgramOutcome.Aborted : LadsProgramOutcome.Stopped));
            Assert.That(unit.State, Is.EqualTo(
                abort ? LadsFunctionalStateValue.Aborted : LadsFunctionalStateValue.Stopped));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            Assert.That(unit.Programs.ActiveRun, Is.Null);
            Assert.That(unit.Programs.Results.Count, Is.EqualTo(1));
            Assert.That(device.OperationCycleCounter, Is.Zero);
            await AssertMethodsAsync(client).ConfigureAwait(false);
        }

        [Test]
        public async Task LegacyExecutorDisablesOptionalMethodsAndRejectsControlAsync()
        {
            await using LadsClientConnection connection = await StartAsync(
                nameof(LegacyExecutorDisablesOptionalMethodsAndRejectsControlAsync),
                new LegacyExecutor()).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            string runId = await client.StartProgramAsync(TemplateName).ConfigureAwait(false);

            Assert.That(unit.RunningOperations, Is.Not.Null);
            await AssertMethodsAsync(client).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported, () => unit.RunningOperations!.HoldAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported, () => unit.RunningOperations!.SuspendAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported, () => unit.RunningOperations!.ToCompleteAsync()).ConfigureAwait(false);
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            Assert.That(unit.Programs!.ActiveRun!.RunId, Is.EqualTo(runId));
            await client.StopAsync().ConfigureAwait(false);
            Assert.That(
                (await unit.Programs.WaitForResultAsync(runId).ConfigureAwait(false)).Outcome,
                Is.EqualTo(LadsProgramOutcome.Stopped));
        }

        [TestCase("Hold", false)]
        [TestCase("Hold", true)]
        [TestCase("Suspend", false)]
        [TestCase("Suspend", true)]
        [TestCase("ToComplete", false)]
        [TestCase("ToComplete", true)]
        public async Task FunctionalUnitStopOrAbortCancelsPendingExecutorAcknowledgmentAsync(
            string operation,
            bool abort)
        {
            var entered = NewSignal();
            var cancelled = NewSignal();
            var release = NewSignal();
            LadsProgramRun? callbackRun = null;
            LadsRunningStateValue callbackTarget = default;
            CancellationToken callbackToken = default;
            var executor = new ControlledExecutor(async (run, target, cancellationToken) =>
            {
                callbackRun = run;
                callbackTarget = target;
                callbackToken = cancellationToken;
                entered.TrySetResult(true);
                try
                {
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled.TrySetResult(true);
                    throw;
                }
                return ServiceResult.Good;
            });
            await using LadsClientConnection connection = await StartAsync(
                nameof(FunctionalUnitStopOrAbortCancelsPendingExecutorAcknowledgmentAsync),
                executor).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ILadsProgramManager programs = unit.Programs!;
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            int resultEvents = 0;
            programs.ResultAdded += _ => Interlocked.Increment(ref resultEvents);
            string runId = await client.StartProgramAsync(TemplateName).ConfigureAwait(false);
            LadsProgramRun activeRun = programs.ActiveRun!;
            LadsRunningStateValue acting = operation switch
            {
                "Hold" => LadsRunningStateValue.Holding,
                "Suspend" => LadsRunningStateValue.Suspending,
                _ => LadsRunningStateValue.Completing
            };
            LadsRunningStateValue target = operation switch
            {
                "Hold" => LadsRunningStateValue.Held,
                "Suspend" => LadsRunningStateValue.Suspended,
                _ => LadsRunningStateValue.Completing
            };
            Task command = operation switch
            {
                "Hold" => unit.RunningOperations!.HoldAsync().AsTask(),
                "Suspend" => unit.RunningOperations!.SuspendAsync().AsTask(),
                _ => unit.RunningOperations!.ToCompleteAsync().AsTask()
            };
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(command.IsCompleted, Is.False);
                Assert.That(callbackRun, Is.SameAs(activeRun));
                Assert.That(callbackTarget, Is.EqualTo(target));
                Assert.That(callbackToken.IsCancellationRequested, Is.False);
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
                Assert.That(unit.RunningState, Is.EqualTo(acting));
                Assert.That(activeRun.IsCompletionRequested, Is.False);
                Assert.That(programs.Results.Count, Is.Zero);
                LadsFunctionalUnitSnapshot pending = await client.ReadAsync(false).ConfigureAwait(false);
                Assert.That(pending.RunningState!.CurrentState, Is.EqualTo(acting.ToString()));

                await (abort ? client.AbortAsync().AsTask() : client.StopAsync().AsTask()).WaitAsync(s_timeout)
                    .ConfigureAwait(false);
                await cancelled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => command.WaitAsync(s_timeout),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);

                Assert.That(callbackToken.IsCancellationRequested, Is.True);
                Assert.That(result.RunId, Is.EqualTo(runId));
                Assert.That(result.Outcome, Is.EqualTo(
                    abort ? LadsProgramOutcome.Aborted : LadsProgramOutcome.Stopped));
                Assert.That(result.Files.Count, Is.Zero);
                Assert.That(unit.State, Is.EqualTo(
                    abort ? LadsFunctionalStateValue.Aborted : LadsFunctionalStateValue.Stopped));
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
                Assert.That(activeRun.IsCompletionRequested, Is.False);
                Assert.That(activeRun.TotalPauseTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(programs.ActiveRun, Is.Null);
                Assert.That(programs.Results.Count, Is.EqualTo(1));
                Assert.That(await programs.WaitForResultAsync(runId).ConfigureAwait(false), Is.SameAs(result));
                Assert.That(device.OperationCycleCounter, Is.Zero);
                await LadsAssert.EventuallyAsync(
                    () => Volatile.Read(ref resultEvents) == 1,
                    "Cancellation did not publish exactly one result event.").ConfigureAwait(false);
                LadsFunctionalUnitSnapshot ended = await client.ReadAsync(false).ConfigureAwait(false);
                Assert.That(ended.State!.CurrentState, Is.EqualTo(abort ? "Aborted" : "Stopped"));
                Assert.That(ended.RunningState!.CurrentState, Is.Empty);
                Assert.That((await client.GetResultsAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
                await AssertMethodsAsync(client).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExecutorCompletionDuringPendingPauseAbortsAndBlocksRestartUntilCallbackDrainsAsync(
            bool suspended)
        {
            var executionStarted = NewSignal();
            var finishExecution = NewSignal();
            var entered = NewSignal();
            var cancelled = NewSignal();
            var release = NewSignal();
            CancellationToken callbackToken = default;
            var executor = new ControlledExecutor(
                async (_, target, cancellationToken) =>
                {
                    Assert.That(target, Is.EqualTo(
                        suspended ? LadsRunningStateValue.Suspended : LadsRunningStateValue.Held));
                    callbackToken = cancellationToken;
                    using CancellationTokenRegistration registration = cancellationToken.Register(
                        () => cancelled.TrySetResult(true));
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return ServiceResult.Good;
                },
                async (run, cancellationToken) =>
                {
                    run.ReportStep(1, "Finishing");
                    executionStarted.TrySetResult(true);
                    await finishExecution.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(ExecutorCompletionDuringPendingPauseAbortsAndBlocksRestartUntilCallbackDrainsAsync),
                executor).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ILadsProgramManager programs = unit.Programs!;
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            int resultEvents = 0;
            programs.ResultAdded += _ => Interlocked.Increment(ref resultEvents);
            string runId = await client.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await executionStarted.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            LadsProgramRun run = programs.ActiveRun!;
            Task command = suspended
                ? unit.RunningOperations!.SuspendAsync().AsTask()
                : unit.RunningOperations!.HoldAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(command.IsCompleted, Is.False);
                Assert.That(unit.RunningState, Is.EqualTo(
                    suspended ? LadsRunningStateValue.Suspending : LadsRunningStateValue.Holding));
                finishExecution.TrySetResult(true);
                ILadsResult result = await programs.WaitForResultAsync(runId).AsTask().WaitAsync(s_timeout)
                    .ConfigureAwait(false);
                await cancelled.Task.WaitAsync(s_timeout).ConfigureAwait(false);

                Assert.That(callbackToken.IsCancellationRequested, Is.True);
                Assert.That(command.IsCompleted, Is.False);
                Assert.That(result.RunId, Is.EqualTo(runId));
                Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Aborted));
                Assert.That(result.Files.Count, Is.Zero);
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
                Assert.That(run.IsCompletionRequested, Is.False);
                Assert.That(run.TotalPauseTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(programs.ActiveRun, Is.Null);
                Assert.That(programs.Results.Count, Is.EqualTo(1));
                Assert.That(device.OperationCycleCounter, Is.Zero);
                await LadsAssert.EventuallyAsync(
                    () => Volatile.Read(ref resultEvents) == 1,
                    "Interrupted completion did not publish exactly one result event.").ConfigureAwait(false);
                await AssertMethodsAsync(client).ConfigureAwait(false);

                await client.ClearAsync().ConfigureAwait(false);
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadInvalidState, () => programs.StartProgramAsync(TemplateName)).ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadInvalidState, () => unit.RunningOperations!.ResetAsync()).ConfigureAwait(false);
                NodeId start = await connection.Server.ChildAsync(
                    unit.NodeId, "FunctionalUnitState", "StartProgram").ConfigureAwait(false);
                DataValue disabled = await connection.Server.ReadAsync(start, Attributes.Executable)
                    .ConfigureAwait(false);
                Assert.That(disabled.WrappedValue.TryGetValue(out bool canStart), Is.True);
                Assert.That(canStart, Is.False);

                release.TrySetResult(true);
                await Assert.ThatAsync(
                    () => command.WaitAsync(s_timeout),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
                Assert.That(programs.ActiveRun, Is.Null);
                Assert.That(programs.Results.Count, Is.EqualTo(1));
                Assert.That(await programs.WaitForResultAsync(runId).ConfigureAwait(false), Is.SameAs(result));
                Assert.That(resultEvents, Is.EqualTo(1));
                Assert.That(device.OperationCycleCounter, Is.Zero);
                DataValue enabled = await connection.Server.ReadAsync(start, Attributes.Executable)
                    .ConfigureAwait(false);
                Assert.That(enabled.WrappedValue.TryGetValue(out bool canRestart), Is.True);
                Assert.That(canRestart, Is.True);
                await AssertMethodsAsync(client).ConfigureAwait(false);
            }
            finally
            {
                finishExecution.TrySetResult(true);
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Preserves requested outcomes when a completed false acknowledgment wins over cancellation.
        /// </summary>
        [TestCase("Stop", LadsProgramOutcome.Stopped)]
        [TestCase("Abort", LadsProgramOutcome.Aborted)]
        [TestCase("Reject", LadsProgramOutcome.Aborted)]
        public async Task CompletedFalseAcknowledgmentPreservesRequestedEndOutcomeAsync(
            string action,
            LadsProgramOutcome expected)
        {
            var run = new LadsProgramRun(
                "completion-order",
                Mock.Of<ILadsFunctionalUnit>(),
                new Opc.Ua.LADS.Server.LadsProgramTemplate(TemplateName),
                [],
                string.Empty,
                string.Empty,
                [],
                DateTimeUtc.MinValue,
                TimeProvider.System,
                static (_, _) => { },
                static (_, _, _) => default);
            using var cancellation = new CancellationTokenSource();
            var active = new LadsProgramManager.ActiveRunEntry(run, cancellation);
            var acknowledgment = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellation.Token.Register(
                () => acknowledgment.TrySetResult(false));
            if (action == "Reject")
            {
                acknowledgment.SetResult(false);
            }
            else
            {
                active.RequestEnd(expected);
            }

            Assert.That(acknowledgment.Task.IsCompleted, Is.True);
            Assert.That(cancellation.IsCancellationRequested, Is.EqualTo(action != "Reject"));
            bool acknowledged = await acknowledgment.Task.WaitAsync(cancellation.Token).ConfigureAwait(false);

            Assert.That(acknowledged, Is.False);
            Assert.That(active.GetCompletionOutcome(acknowledged), Is.EqualTo(expected));
        }

        [TestCase("Accept")]
        [TestCase("Reject")]
        [TestCase("Stop")]
        [TestCase("Abort")]
        public async Task OrderlyResultWaitsForProviderAcknowledgmentAndHandlesRejectionOrCancellationAsync(
            string action)
        {
            bool reject = action == "Reject";
            bool cancel = action is "Stop" or "Abort";
            var entered = NewSignal();
            var executorFinished = NewSignal();
            var release = NewSignal();
            var executor = new ControlledExecutor(
                async (run, target, cancellationToken) =>
                {
                    Assert.That(target, Is.EqualTo(LadsRunningStateValue.Completing));
                    run.RequestCompletion();
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return reject ? StatusCodes.BadInvalidState : ServiceResult.Good;
                },
                async (run, cancellationToken) =>
                {
                    await run.DelayAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                    executorFinished.TrySetResult(true);
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(OrderlyResultWaitsForProviderAcknowledgmentAndHandlesRejectionOrCancellationAsync), executor)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ILadsProgramManager programs = unit.Programs!;
            string runId = await programs.StartProgramAsync(TemplateName).ConfigureAwait(false);
            Task command = unit.RunningOperations!.ToCompleteAsync().AsTask();
            Task<ILadsResult> resultTask = programs.WaitForResultAsync(runId).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                await executorFinished.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(command.IsCompleted, Is.False);
                Assert.That(resultTask.IsCompleted, Is.False);
                Assert.That(programs.Results.Count, Is.Zero);
                Assert.That(device.OperationCycleCounter, Is.Zero);
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Completing));
                if (cancel)
                {
                    if (action == "Abort")
                    {
                        await unit.AbortAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await unit.StopAsync().ConfigureAwait(false);
                    }
                    await Assert.ThatAsync(() => command, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                }
                else if (reject)
                {
                    release.TrySetResult(true);
                    await Assert.ThatAsync(() => command, Throws.InstanceOf<ServiceResultException>())
                        .ConfigureAwait(false);
                }
                else
                {
                    release.TrySetResult(true);
                    await command.WaitAsync(s_timeout).ConfigureAwait(false);
                }
                ILadsResult result = await resultTask.WaitAsync(s_timeout).ConfigureAwait(false);
                LadsProgramOutcome expected = action switch
                {
                    "Accept" => LadsProgramOutcome.Completed,
                    "Stop" => LadsProgramOutcome.Stopped,
                    _ => LadsProgramOutcome.Aborted
                };
                Assert.That(result.Outcome, Is.EqualTo(expected));
                Assert.That(programs.Results.Count, Is.EqualTo(1));
                Assert.That(device.OperationCycleCounter, Is.EqualTo(action == "Accept" ? 1 : 0));
                LadsFunctionalStateValue state = action switch
                {
                    "Accept" => LadsFunctionalStateValue.Running,
                    "Stop" => LadsFunctionalStateValue.Stopped,
                    _ => LadsFunctionalStateValue.Aborted
                };
                Assert.That(unit.State, Is.EqualTo(state));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Reports finalization failure to end and disposal callers after execution and resource cleanup.
        /// </summary>
        [TestCase("Stop")]
        [TestCase("Abort")]
        [TestCase("Dispose")]
        public async Task ResultStoreFailureFaultsAcceptedEndAndResultWaiterAfterCleanupAsync(string action)
        {
            var entered = NewSignal();
            var saving = NewSignal();
            var release = NewSignal();
            var executorExited = NewSignal();
            var failure = new InvalidOperationException("The result snapshot could not be saved.");
            var store = new Mock<ILadsStore>();
            store.Setup(value => value.SaveAsync(
                    It.IsAny<string>(), It.IsAny<LadsStoredDeviceData>(), It.IsAny<CancellationToken>()))
                .Returns((string _, LadsStoredDeviceData data, CancellationToken _) =>
                {
                    if (data.FunctionalUnits.ToList().Any(unit => unit.Results.Count != 0))
                    {
                        return new ValueTask(FailSaveAsync());
                    }
                    return default;
                });
            async Task FailSaveAsync()
            {
                saving.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                throw failure;
            }
            var executor = new ControlledExecutor(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good),
                async (run, cancellationToken) =>
                {
                    run.AddResultFile("failure-evidence.txt", "text/plain", ByteString.From("evidence"u8));
                    entered.TrySetResult(true);
                    try
                    {
                        await NewSignal().Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        executorExited.TrySetResult(true);
                    }
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(ResultStoreFailureFaultsAcceptedEndAndResultWaiterAfterCleanupAsync),
                executor,
                store: store.Object).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (Opc.Ua.LADS.Server.LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            string runId = await programs.StartProgramAsync(TemplateName).ConfigureAwait(false);
            Task<ILadsResult> result = programs.WaitForResultAsync(runId).AsTask();
            Task? end = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                end = action switch
                {
                    "Dispose" => programs.DisposeAsync().AsTask(),
                    "Abort" => unit.AbortAsync().AsTask(),
                    _ => unit.StopAsync().AsTask()
                };
                await saving.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(executorExited.Task.IsCompleted, Is.True);
                Assert.That(end.IsCompleted, Is.False);
                Assert.That(result.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
            if (action == "Dispose")
            {
                AggregateException? disposalFailure = await Assert.ThrowsAsync<AggregateException>(
                    async () => await end!.WaitAsync(s_timeout).ConfigureAwait(false));
                Assert.That(disposalFailure!.Flatten().InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(disposalFailure.Flatten().InnerExceptions[0], Is.SameAs(failure));
                await programs.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(
                    async () => await end!.WaitAsync(s_timeout).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message))
                    .ConfigureAwait(false);
            }
            await Assert.ThatAsync(
                () => result.WaitAsync(s_timeout),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message)).ConfigureAwait(false);
            Assert.That(programs.ActiveRun, Is.Null);
            Assert.That(programs.Results.Count, Is.Zero);
            var children = new List<BaseInstanceState>();
            programs.Node.ResultSet!.GetChildren(
                ((Opc.Ua.LADS.Server.LadsDevice)device).Runtime.Context, children);
            Assert.That(children.OfType<ResultState>(), Is.Empty);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            Assert.That(device.OperationCycleCounter, Is.Zero);
            Assert.That(await unit.Gate.WaitAsync(0).ConfigureAwait(false), Is.True);
            unit.Gate.Release();
            await device.SaveStoredDataAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Distinguishes active-run cancellation from rejection before execution finishes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ActiveRunCancellationPublishesCancelledAcknowledgmentBeforeExecutionEndsAsync(bool abort)
        {
            var entered = NewSignal();
            var release = NewSignal();
            var executor = new ControlledExecutor(
                async (_, _, cancellationToken) =>
                {
                    entered.TrySetResult(true);
                    await NewSignal().Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return ServiceResult.Good;
                },
                async (_, cancellationToken) =>
                {
                    await release.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(ActiveRunCancellationPublishesCancelledAcknowledgmentBeforeExecutionEndsAsync), executor)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (Opc.Ua.LADS.Server.LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            ILadsProgramManager programs = unit.Programs!;
            string runId = await programs.StartProgramAsync(TemplateName).ConfigureAwait(false);
            Task command = unit.RunningOperations!.ToCompleteAsync().AsTask();
            Task? end = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                end = abort ? unit.AbortAsync().AsTask() : unit.StopAsync().AsTask();
                await Assert.ThatAsync(
                    async () => await command.WaitAsync(s_timeout).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                await Assert.ThatAsync(
                    async () => await unit.WaitForCompletionAcknowledgmentAsync(CancellationToken.None)
                        .ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(end.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await end!.WaitAsync(s_timeout).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(abort ? LadsProgramOutcome.Aborted : LadsProgramOutcome.Stopped));
            Assert.That(device.OperationCycleCounter, Is.Zero);
        }

        /// <summary>
        /// Retains a provider rejection when Stop subsequently cancels execution.
        /// </summary>
        [Test]
        public async Task ProviderRejectionRetainsFailureWhenStopCancelsExecutionAsync()
        {
            var entered = NewSignal();
            var cancelled = NewSignal();
            var release = NewSignal();
            var executor = new ControlledExecutor(
                (_, _, _) => new ValueTask<ServiceResult>(StatusCodes.BadInvalidState),
                async (_, cancellationToken) =>
                {
                    using CancellationTokenRegistration registration = cancellationToken.Register(
                        () => cancelled.TrySetResult(true));
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(ProviderRejectionRetainsFailureWhenStopCancelsExecutionAsync), executor).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            string runId = await programs.StartProgramAsync(TemplateName).ConfigureAwait(false);
            Task? end = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                ServiceResult rejected = await programs.ChangeExecutionAsync(
                    programs.ActiveRun!, LadsRunningStateValue.Completing, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                end = unit.StopAsync().AsTask();
                await cancelled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(end.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await end!.WaitAsync(s_timeout).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Aborted));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            Assert.That(device.OperationCycleCounter, Is.Zero);
        }

        /// <summary>
        /// Releases the admitted command after availability publication fails.
        /// </summary>
        [Test]
        public async Task AdmissionNotificationFailureReleasesGateAndCleansCommandAsync()
        {
            TaskCompletionSource<bool> executionStarted = NewSignal();
            int commandCount = 0;
            var executor = new ControlledExecutor(
                (_, _, _) =>
                {
                    Interlocked.Increment(ref commandCount);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                },
                async (run, cancellationToken) =>
                {
                    run.ReportStep(1, "Wait");
                    executionStarted.TrySetResult(true);
                    await run.DelayAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(AdmissionNotificationFailureReleasesGateAndCleansCommandAsync), executor)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            await unit.Programs!.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await executionStarted.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            MethodState hold = unit.Node.FunctionalUnitState!.RunningStateMachine!.Hold!;
            NodeStateChangedAsyncHandler? previous = hold.OnStateChangedAsync;
            int notifications = 0;
            hold.OnStateChangedAsync = (_, _, _, _) =>
            {
                int notification = Interlocked.Increment(ref notifications);
                if (notification == 1)
                {
                    hold.Executable = true;
                    return default;
                }
                return notification == 2
                    ? new ValueTask(Task.FromException(new InvalidOperationException("admission notification failed")))
                    : default;
            };
            try
            {
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await unit.RunningOperations!.HoldAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.Message, Is.EqualTo("admission notification failed"));
            }
            finally
            {
                hold.OnStateChangedAsync = previous;
            }

            Assert.That(commandCount, Is.Zero);
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Holding));
            Assert.That(unit.HasPendingExecutionCommands, Is.False);
            Assert.That(notifications, Is.GreaterThanOrEqualTo(2));
            Assert.That(await unit.Gate.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            unit.Gate.Release();
        }

        /// <summary>
        /// Recomputes availability after a committed transition fails before command admission.
        /// </summary>
        [Test]
        public async Task PreAdmissionTransitionNotificationFailureRecomputesAvailabilityAsync()
        {
            TaskCompletionSource<bool> executionStarted = NewSignal();
            int commandCount = 0;
            var executor = new ControlledExecutor(
                (_, _, _) =>
                {
                    Interlocked.Increment(ref commandCount);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                },
                async (run, cancellationToken) =>
                {
                    run.ReportStep(1, "Wait");
                    executionStarted.TrySetResult(true);
                    await run.DelayAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(PreAdmissionTransitionNotificationFailureRecomputesAvailabilityAsync), executor)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            await unit.Programs!.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await executionStarted.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            NodeState currentState = unit.Node.FunctionalUnitState!.RunningStateMachine!.CurrentState!;
            NodeStateChangedAsyncHandler? previous = currentState.OnStateChangedAsync;
            int notifications = 0;
            currentState.OnStateChangedAsync = (_, _, _, _) =>
            {
                if (Interlocked.Increment(ref notifications) == 1)
                {
                    return new ValueTask(Task.FromException(
                        new InvalidOperationException("transition notification failed")));
                }
                return default;
            };
            try
            {
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await unit.RunningOperations!.HoldAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.Message, Is.EqualTo("transition notification failed"));
            }
            finally
            {
                currentState.OnStateChangedAsync = previous;
            }

            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Holding));
            Assert.That(commandCount, Is.Zero);
            Assert.That(unit.HasPendingExecutionCommands, Is.False);
            Assert.That(notifications, Is.EqualTo(2));
            Assert.That(await unit.Gate.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            unit.Gate.Release();
        }

        /// <summary>
        /// Recomputes availability after a precondition rejects an operation before admission.
        /// </summary>
        [Test]
        public async Task PreconditionFailureRecomputesAvailabilityAsync()
        {
            await using LadsClientConnection connection = await StartAsync(
                nameof(PreconditionFailureRecomputesAvailabilityAsync)).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            NodeState currentState = unit.Node.FunctionalUnitState!.RunningStateMachine!.CurrentState!;
            NodeStateChangedAsyncHandler? previous = currentState.OnStateChangedAsync;
            int notifications = 0;
            currentState.OnStateChangedAsync = (_, _, _, _) =>
            {
                Interlocked.Increment(ref notifications);
                return default;
            };
            try
            {
                currentState.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadInvalidState,
                    () => unit.RunningOperations!.HoldAsync()).ConfigureAwait(false);
            }
            finally
            {
                currentState.OnStateChangedAsync = previous;
            }

            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            Assert.That(notifications, Is.GreaterThan(0));
            Assert.That(await unit.Gate.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            unit.Gate.Release();
        }

        /// <summary>
        /// Releases the gate when precondition availability publication throws.
        /// </summary>
        [Test]
        public async Task PreconditionAvailabilityFailureReleasesGateAsync()
        {
            await using LadsClientConnection connection = await StartAsync(
                nameof(PreconditionAvailabilityFailureReleasesGateAsync)).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            NodeState currentState = unit.Node.FunctionalUnitState!.RunningStateMachine!.CurrentState!;
            NodeStateChangedAsyncHandler? previous = currentState.OnStateChangedAsync;
            currentState.OnStateChangedAsync = (_, _, _, _) =>
                new ValueTask(Task.FromException(new InvalidOperationException("availability publication failed")));
            try
            {
                currentState.UpdateChangeMasks(NodeStateChangeMasks.Value);
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await unit.RunningOperations!.HoldAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.Message, Is.EqualTo("availability publication failed"));
            }
            finally
            {
                currentState.OnStateChangedAsync = previous;
            }

            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            Assert.That(await unit.Gate.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            unit.Gate.Release();
        }

        /// <summary>
        /// Recomputes availability when a transition guard rejects before state commit.
        /// </summary>
        [Test]
        public async Task PreCommitTransitionFailureRecomputesAvailabilityAsync()
        {
            TaskCompletionSource<bool> executionStarted = NewSignal();
            var executor = new ControlledExecutor(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good),
                async (run, cancellationToken) =>
                {
                    run.ReportStep(1, "Wait");
                    executionStarted.TrySetResult(true);
                    await run.DelayAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(PreCommitTransitionFailureRecomputesAvailabilityAsync), executor).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            await unit.Programs!.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await executionStarted.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            FiniteStateMachineState machine = unit.Node.FunctionalUnitState!.RunningStateMachine!;
            NodeState currentState = machine.CurrentState!;
            NodeStateChangedAsyncHandler? previousStateChanged = currentState.OnStateChangedAsync;
            StateMachineTransitionHandler? previousGuard = machine.OnBeforeTransition;
            int notifications = 0;
            currentState.OnStateChangedAsync = (_, _, _, _) =>
            {
                Interlocked.Increment(ref notifications);
                return default;
            };
            machine.OnBeforeTransition = (_, _, _, _, _, _) =>
                ServiceResult.Create(StatusCodes.BadInvalidState, "transition rejected");
            try
            {
                currentState.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadInvalidState,
                    () => unit.RunningOperations!.HoldAsync()).ConfigureAwait(false);
            }
            finally
            {
                currentState.OnStateChangedAsync = previousStateChanged;
                machine.OnBeforeTransition = previousGuard;
            }

            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            Assert.That(unit.HasPendingExecutionCommands, Is.False);
            Assert.That(notifications, Is.GreaterThan(0));
            Assert.That(await unit.Gate.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            unit.Gate.Release();
        }

        /// <summary>
        /// Verifies raw and typed observers see running-operation availability after sync and async state changes.
        /// </summary>
        [Test]
        public async Task RunningStateObserversSeeRecomputedAvailabilityAsync()
        {
            TaskCompletionSource<bool> executionStarted = NewSignal();
            var executor = new ControlledExecutor(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good),
                async (run, cancellationToken) =>
                {
                    run.ReportStep(1, "Wait");
                    executionStarted.TrySetResult(true);
                    await run.DelayAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(RunningStateObserversSeeRecomputedAvailabilityAsync), executor).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            await unit.Programs!.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await executionStarted.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            var runningNode = unit.Node.FunctionalUnitState!.RunningStateMachine!;
            MethodState hold = runningNode.Hold!;
            MethodState unhold = runningNode.Unhold!;
            var observations = new List<string>();
            void Observe(string observer, LadsRunningStateValue state)
            {
                if (state == LadsRunningStateValue.Holding)
                {
                    Assert.That(hold.Executable, Is.False);
                    Assert.That(unhold.Executable, Is.False);
                }
                else if (state == LadsRunningStateValue.Held)
                {
                    Assert.That(hold.Executable, Is.False);
                    Assert.That(unhold.Executable, Is.True);
                }
                else
                {
                    Assert.That(state, Is.EqualTo(LadsRunningStateValue.Execute));
                    Assert.That(hold.Executable, Is.True);
                    Assert.That(unhold.Executable, Is.False);
                }
                observations.Add($"{observer}:{state}");
            }
            unit.Running.StateChanged += state => Observe("typed", state);
            unit.Running.Driver.StateChanged += _ => Observe("raw", unit.Running.Current);

            Assert.That(
                await unit.Running.TransitionToAsync(LadsRunningStateValue.Holding).ConfigureAwait(false),
                Is.True);
            Assert.That(
                await unit.Running.TransitionToAsync(LadsRunningStateValue.Held).ConfigureAwait(false),
                Is.True);
            await unit.Running.SetStateAsync(LadsRunningStateValue.Execute).ConfigureAwait(false);

            Assert.That(
                observations,
                Is.EqualTo(new[]
                {
                    "typed:Holding",
                    "raw:Holding",
                    "typed:Held",
                    "raw:Held",
                    "typed:Execute",
                    "raw:Execute"
                }));
        }

        /// <summary>
        /// Verifies control-function events precede running-method availability updates on async transitions.
        /// </summary>
        [Test]
        public async Task ControlFunctionEventPrecedesAvailabilityUpdateAsync()
        {
            var provider = new ControlledFunctionProvider(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good));
            await using LadsClientConnection connection = await StartFunctionsAsync(
                nameof(ControlFunctionEventPrecedesAvailabilityUpdateAsync), provider).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsControlFunction function = device.GetFunctionalUnit(UnitName)
                .GetFunction<ILadsControlFunction>("Analog");
            MethodState hold = ((LadsControlFunction)function).Node.ControlFunctionState!.RunningStateMachine!.Hold!;
            var observedAvailability = new List<bool>();
            function.StateChanged += state =>
            {
                if (state is LadsFunctionalStateValue.Running or LadsFunctionalStateValue.Stopping)
                {
                    observedAvailability.Add(hold.Executable);
                }
            };

            await function.StartAsync().ConfigureAwait(false);
            Assert.That(hold.Executable, Is.True);
            await function.StopAsync().ConfigureAwait(false);

            Assert.That(observedAvailability, Is.EqualTo(new[] { false, true }));
            Assert.That(hold.Executable, Is.False);
        }

        /// <summary>
        /// Verifies a throwing control-function event prevents its later availability update.
        /// </summary>
        [Test]
        public async Task ThrowingControlFunctionEventPreventsAvailabilityUpdateAsync()
        {
            var provider = new ControlledFunctionProvider(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good));
            await using LadsClientConnection connection = await StartFunctionsAsync(
                nameof(ThrowingControlFunctionEventPreventsAvailabilityUpdateAsync), provider)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsControlFunction function = device.GetFunctionalUnit(UnitName)
                .GetFunction<ILadsControlFunction>("Analog");
            MethodState hold = ((LadsControlFunction)function).Node.ControlFunctionState!.RunningStateMachine!.Hold!;
            await function.StartAsync().ConfigureAwait(false);
            Assert.That(hold.Executable, Is.True);
            function.StateChanged += state =>
            {
                if (state == LadsFunctionalStateValue.Stopping)
                {
                    throw new InvalidOperationException("control event failed");
                }
            };

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await function.StopAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure!.Message, Is.EqualTo("control event failed"));
            Assert.That(function.State, Is.EqualTo(LadsFunctionalStateValue.Stopping));
            Assert.That(hold.Executable, Is.True);
        }

        /// <summary>
        /// Preserves the acknowledged state and reports both notification failures.
        /// </summary>
        [Test]
        public async Task PostAcknowledgmentNotificationFailuresPreserveStateAndReportBothFailuresAsync()
        {
            TaskCompletionSource<bool> executionStarted = NewSignal();
            MethodState? hold = null;
            bool failNotifications = false;
            int commandCount = 0;
            var executor = new ControlledExecutor(
                (_, target, _) =>
                {
                    Interlocked.Increment(ref commandCount);
                    if (target == LadsRunningStateValue.Completing)
                    {
                        hold!.Executable = true;
                        failNotifications = true;
                    }
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                },
                async (run, cancellationToken) =>
                {
                    run.ReportStep(1, "Wait");
                    executionStarted.TrySetResult(true);
                    await run.DelayAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(PostAcknowledgmentNotificationFailuresPreserveStateAndReportBothFailuresAsync), executor)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            await unit.Programs!.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await executionStarted.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            hold = unit.Node.FunctionalUnitState!.RunningStateMachine!.Hold!;
            NodeStateChangedAsyncHandler? previous = hold.OnStateChangedAsync;
            int notifications = 0;
            hold.OnStateChangedAsync = (_, _, _, _) =>
            {
                if (!failNotifications)
                {
                    return default;
                }
                int notification = Interlocked.Increment(ref notifications);
                return new ValueTask(Task.FromException(
                    new InvalidOperationException($"availability notification {notification} failed")));
            };
            try
            {
                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await unit.RunningOperations!.ToCompleteAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(
                    failure!.Flatten().InnerExceptions.Select(exception => exception.Message),
                    Is.EqualTo(new[]
                    {
                        "availability notification 1 failed",
                        "availability notification 2 failed"
                    }));
            }
            finally
            {
                hold.OnStateChangedAsync = previous;
            }

            Assert.That(commandCount, Is.EqualTo(1));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Completing));
            Assert.That(unit.HasPendingExecutionCommands, Is.False);
            Assert.That(notifications, Is.EqualTo(2));
            Assert.That(await unit.Gate.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            unit.Gate.Release();
        }

        /// <summary>
        /// Waits for run cleanup when an observer ends or disposes a newly published active run.
        /// </summary>
        [TestCase("Stop")]
        [TestCase("Abort")]
        [TestCase("Dispose")]
        public async Task EarlyEndOrDisposalWaitsForPublishedRunLifetimeAsync(string action)
        {
            var entered = NewSignal();
            var release = NewSignal();
            var executor = new ControlledExecutor(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good),
                async (_, cancellationToken) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(EarlyEndOrDisposalWaitsForPublishedRunLifetimeAsync), executor).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (Opc.Ua.LADS.Server.LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            Task? end = null;
            bool pendingInNotification = false;
            BaseVariableState observed = programs.Node.ActiveProgram!.DeviceProgramRunId!;
            observed.OnStateChanged = (_, _, _) =>
            {
                observed.OnStateChanged = null;
                end = action switch
                {
                    "Dispose" => programs.DisposeAsync().AsTask(),
                    "Abort" => unit.AbortAsync().AsTask(),
                    _ => unit.StopAsync().AsTask()
                };
                pendingInNotification = !end.IsCompleted;
            };
            string runId;
            try
            {
                runId = await programs.StartProgramAsync(TemplateName).ConfigureAwait(false);
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(pendingInNotification, Is.True);
                Assert.That(end, Is.Not.Null);
                Assert.That(end!.IsCompleted, Is.False);
            }
            finally
            {
                observed.OnStateChanged = null;
                release.TrySetResult(true);
            }
            await end!.WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(programs.ActiveRun, Is.Null);
            Assert.That(device.OperationCycleCounter, Is.Zero);
            if (action != "Dispose")
            {
                ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
                Assert.That(result.Outcome, Is.EqualTo(
                    action == "Abort" ? LadsProgramOutcome.Aborted : LadsProgramOutcome.Stopped));
                Assert.That(unit.State, Is.EqualTo(
                    action == "Abort" ? LadsFunctionalStateValue.Aborted : LadsFunctionalStateValue.Stopped));
            }
        }

        /// <summary>
        /// Faults early lifetime and result waiters when startup publication fails.
        /// </summary>
        [Test]
        public async Task StartupPublicationFailureCompletesEarlyEndWaiterAsync()
        {
            await using LadsClientConnection connection = await StartAsync(
                nameof(StartupPublicationFailureCompletesEarlyEndWaiterAsync)).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (Opc.Ua.LADS.Server.LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            Task? end = null;
            Task<ILadsResult>? resultWaiter = null;
            BaseVariableState observed = programs.Node.ActiveProgram!.DeviceProgramRunId!;
            observed.OnStateChanged = (_, _, _) =>
            {
                observed.OnStateChanged = null;
                resultWaiter = programs.WaitForResultAsync(programs.ActiveRun!.RunId).AsTask();
                end = unit.StopAsync().AsTask();
                Assert.That(end.IsCompleted, Is.False);
                throw new InvalidOperationException("The startup observer failed.");
            };
            try
            {
                await Assert.ThatAsync(
                    async () => await programs.StartProgramAsync(TemplateName).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(end, Is.Not.Null);
                await Assert.ThatAsync(
                    async () => await end!.WaitAsync(s_timeout).ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(resultWaiter, Is.Not.Null);
                await Assert.ThatAsync(
                    () => resultWaiter!.WaitAsync(s_timeout),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(programs.ActiveRun, Is.Null);
                Assert.That(programs.Results.Count, Is.Zero);
                Assert.That(device.OperationCycleCounter, Is.Zero);
            }
            finally
            {
                observed.OnStateChanged = null;
            }
        }

        /// <summary>
        /// Preserves genuine executor failures despite an overlapping accepted end request.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExecutorFailureOverridesOverlappingEndRequestAsync(bool abort)
        {
            var entered = NewSignal();
            var cancelled = NewSignal();
            var release = NewSignal();
            var executor = new ControlledExecutor(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good),
                async (_, cancellationToken) =>
                {
                    using CancellationTokenRegistration registration = cancellationToken.Register(
                        () => cancelled.TrySetResult(true));
                    entered.SetResult(true);
                    await release.Task.ConfigureAwait(false);
                    throw new InvalidOperationException("The equipment failed after the end request.");
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(ExecutorFailureOverridesOverlappingEndRequestAsync), executor).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ILadsProgramManager programs = unit.Programs!;
            string runId = await programs.StartProgramAsync(TemplateName).ConfigureAwait(false);
            Task? end = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                end = abort ? unit.AbortAsync().AsTask() : unit.StopAsync().AsTask();
                await cancelled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(end.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await end!.WaitAsync(s_timeout).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Aborted));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(programs.ActiveRun, Is.Null);
            Assert.That(device.OperationCycleCounter, Is.Zero);
        }

        /// <summary>
        /// Publishes an accepted end outcome when finalization has not yet frozen the candidate.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EndRequestBeforeFinalizationOverridesCompletedCandidateAsync(bool abort)
        {
            var finish = NewSignal();
            var executorReturned = NewSignal();
            var executor = new ControlledExecutor(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good),
                async (_, _) =>
                {
                    await finish.Task.ConfigureAwait(false);
                    executorReturned.SetResult(true);
                });
            await using LadsClientConnection connection = await StartAsync(
                nameof(EndRequestBeforeFinalizationOverridesCompletedCandidateAsync), executor).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            var unit = (Opc.Ua.LADS.Server.LadsFunctionalUnit)device.GetFunctionalUnit(UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            string runId = await programs.StartProgramAsync(TemplateName).ConfigureAwait(false);
            await unit.Gate.WaitAsync().ConfigureAwait(false);
            Task<bool>? end = null;
            try
            {
                finish.SetResult(true);
                await executorReturned.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                end = programs.EndActiveRunAsync(
                    abort ? LadsProgramOutcome.Aborted : LadsProgramOutcome.Stopped,
                    CancellationToken.None).AsTask();
                Assert.That(end.IsCompleted, Is.False);
            }
            finally
            {
                finish.TrySetResult(true);
                unit.Gate.Release();
            }
            Assert.That(await end!.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(abort ? LadsProgramOutcome.Aborted : LadsProgramOutcome.Stopped));
            Assert.That(unit.State, Is.EqualTo(
                abort ? LadsFunctionalStateValue.Aborted : LadsFunctionalStateValue.Stopped));
            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(device.OperationCycleCounter, Is.Zero);
        }

        /// <summary>
        /// Enforces first-request precedence and rejects stale requests after finalization starts.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FinalizationArbitratesCandidateAndStaleEndRequests(bool abort)
        {
            using var cancellation = new CancellationTokenSource();
            var run = new LadsProgramRun(
                "finalization-order",
                Mock.Of<ILadsFunctionalUnit>(),
                new Opc.Ua.LADS.Server.LadsProgramTemplate(TemplateName),
                [],
                string.Empty,
                string.Empty,
                [],
                DateTimeUtc.MinValue,
                TimeProvider.System,
                static (_, _) => { },
                static (_, _, _) => default);
            var active = new LadsProgramManager.ActiveRunEntry(run, cancellation);
            LadsProgramOutcome candidate = active.GetCompletionOutcome(true);
            Assert.That(candidate, Is.EqualTo(LadsProgramOutcome.Completed));
            LadsProgramOutcome requested = abort ? LadsProgramOutcome.Aborted : LadsProgramOutcome.Stopped;
            using var requestedCancellation = new CancellationTokenSource();
            var requestedActive = new LadsProgramManager.ActiveRunEntry(run, requestedCancellation);
            Assert.That(requestedActive.RequestEnd(requested), Is.True);
            Assert.That(requestedActive.RequestEnd(
                abort ? LadsProgramOutcome.Stopped : LadsProgramOutcome.Aborted), Is.True);
            Assert.That(requestedActive.BeginFinalization(candidate), Is.EqualTo(requested));
            Assert.That(active.BeginFinalization(candidate), Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That(active.RequestEnd(
                abort ? LadsProgramOutcome.Aborted : LadsProgramOutcome.Stopped), Is.False);
            Assert.That(cancellation.IsCancellationRequested, Is.False);
            cancellation.Dispose();
            Assert.That(active.RequestEnd(LadsProgramOutcome.Stopped), Is.False);
        }

        /// <summary>
        /// Waits for frozen-result publication before applying a standalone end transition.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EndDuringResultPublicationWaitsThenTransitionsStandaloneAsync(bool abort)
        {
            var finish = NewSignal();
            var saving = NewSignal();
            var release = NewSignal();
            var store = new Mock<ILadsStore>();
            store.Setup(value => value.SaveAsync(
                    It.IsAny<string>(), It.IsAny<LadsStoredDeviceData>(), It.IsAny<CancellationToken>()))
                .Returns((string _, LadsStoredDeviceData data, CancellationToken _) =>
                {
                    if (data.FunctionalUnits.ToList().Any(unit => unit.Results.Count != 0))
                    {
                        saving.TrySetResult(true);
                        return new ValueTask(release.Task);
                    }
                    return default;
                });
            var executor = new ControlledExecutor(
                (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good),
                async (_, _) => await finish.Task.ConfigureAwait(false));
            await using LadsClientConnection connection = await StartAsync(
                nameof(EndDuringResultPublicationWaitsThenTransitionsStandaloneAsync),
                executor,
                store: store.Object).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ILadsProgramManager programs = unit.Programs!;
            string runId = await programs.StartProgramAsync(TemplateName).ConfigureAwait(false);
            Task? end = null;
            try
            {
                finish.SetResult(true);
                await saving.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(programs.ActiveRun, Is.Not.Null);
                end = abort ? unit.AbortAsync().AsTask() : unit.StopAsync().AsTask();
                Assert.That(end.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await end!.WaitAsync(s_timeout).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That(unit.State, Is.EqualTo(
                abort ? LadsFunctionalStateValue.Aborted : LadsFunctionalStateValue.Stopped));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            Assert.That(programs.ActiveRun, Is.Null);
            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
        }

        [TestCase("Reject")]
        [TestCase("Throw")]
        [TestCase("Cancel")]
        public async Task FailedAcknowledgmentNeverPublishesHeldOrCompletesTheRunAsync(string failure)
        {
            var entered = NewSignal();
            var release = NewSignal();
            var executor = new ControlledExecutor(async (_, _, cancellationToken) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (failure == "Throw")
                {
                    throw new InvalidOperationException("Hardware rejected the pause.");
                }
                return StatusCodes.BadInvalidState;
            });
            await using LadsClientConnection connection = await StartAsync(
                nameof(FailedAcknowledgmentNeverPublishesHeldOrCompletesTheRunAsync), executor)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            string runId = await client.StartProgramAsync(TemplateName).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task hold = unit.RunningOperations!.HoldAsync(cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(hold.IsCompleted, Is.False);
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Holding));
                LadsFunctionalUnitSnapshot snapshot = await client.ReadAsync(false).ConfigureAwait(false);
                Assert.That(snapshot.RunningState!.CurrentState, Is.EqualTo("Holding"));
                if (failure == "Cancel")
                {
                    cancellation.Cancel();
                    await Assert.ThatAsync(() => hold, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                }
                else
                {
                    release.TrySetResult(true);
                    if (failure == "Throw")
                    {
                        await Assert.ThatAsync(() => hold, Throws.TypeOf<InvalidOperationException>())
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await LadsAssert.ThrowsStatusAsync(
                            StatusCodes.BadInvalidState, () => new ValueTask(hold)).ConfigureAwait(false);
                    }
                }
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Holding));
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
                Assert.That(unit.Programs!.ActiveRun!.RunId, Is.EqualTo(runId));
                Assert.That(unit.Programs.ActiveRun.TotalPauseTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(unit.Programs.Results.Count, Is.Zero);
                Assert.That(device.OperationCycleCounter, Is.Zero);
                await client.StopAsync().ConfigureAwait(false);
                Assert.That(
                    (await unit.Programs.WaitForResultAsync(runId).ConfigureAwait(false)).Outcome,
                    Is.EqualTo(LadsProgramOutcome.Stopped));
            }
            finally
            {
                release.TrySetResult(true);
                cancellation.Cancel();
            }
        }

        [Test]
        public async Task HoldSupersedesPendingSuspendWithoutOverlappingHardwareCallbacksAsync()
        {
            var suspending = NewSignal();
            var cancelled = NewSignal();
            var release = NewSignal();
            var holding = NewSignal();
            var executor = new ControlledExecutor(async (run, target, cancellationToken) =>
            {
                if (target == LadsRunningStateValue.Suspended)
                {
                    using CancellationTokenRegistration registration = cancellationToken.Register(
                        () => cancelled.TrySetResult(true));
                    suspending.TrySetResult(true);
                    // Hardware cleanup can outlive cancellation, but must not overlap the next command.
                    await release.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                holding.TrySetResult(true);
                return await new InMemoryLadsProgramExecutor().ChangeExecutionAsync(
                    run, target, cancellationToken).ConfigureAwait(false);
            });
            await using LadsClientConnection connection = await StartAsync(
                nameof(HoldSupersedesPendingSuspendWithoutOverlappingHardwareCallbacksAsync), executor)
                .ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ClientUnit client = await ClientUnitAsync(connection).ConfigureAwait(false);
            string runId = await client.StartProgramAsync(TemplateName).ConfigureAwait(false);
            Task suspend = unit.RunningOperations!.SuspendAsync().AsTask();
            try
            {
                await suspending.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Suspending));
                await AssertMethodsAsync(client, "Hold").ConfigureAwait(false);
                Task hold = unit.RunningOperations.HoldAsync().AsTask();
                await cancelled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Holding));
                Assert.That(holding.Task.IsCompleted, Is.False);
                Assert.That(hold.IsCompleted, Is.False);
                Assert.That(executor.MaximumConcurrentCallbacks, Is.EqualTo(1));
                release.TrySetResult(true);
                await Assert.ThatAsync(() => suspend, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
                await hold.WaitAsync(s_timeout).ConfigureAwait(false);

                Assert.That(holding.Task.IsCompleted, Is.True);
                Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Held));
                Assert.That(executor.MaximumConcurrentCallbacks, Is.EqualTo(1));
                Assert.That(unit.Programs!.ActiveRun!.RunId, Is.EqualTo(runId));
                Assert.That(unit.Programs.Results.Count, Is.Zero);
                await AssertMethodsAsync(client, "Unhold").ConfigureAwait(false);
                await client.StopAsync().ConfigureAwait(false);
                Assert.That(
                    (await unit.Programs.WaitForResultAsync(runId).ConfigureAwait(false)).Outcome,
                    Is.EqualTo(LadsProgramOutcome.Stopped));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [TestCase("Analog", false)]
        [TestCase("Analog", true)]
        [TestCase("Timer", false)]
        [TestCase("Timer", true)]
        public async Task ControlFunctionsAcknowledgeRunningLifecycleAndResetThroughClientOrDirectAsync(
            string functionName,
            bool useClient)
        {
            var completing = NewSignal();
            var release = NewSignal();
            var provider = new ControlledFunctionProvider(async (_, target, cancellationToken) =>
            {
                if (target == LadsRunningStateValue.Completing)
                {
                    completing.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                return ServiceResult.Good;
            });
            await using LadsClientConnection connection = await StartFunctionsAsync(
                nameof(ControlFunctionsAcknowledgeRunningLifecycleAndResetThroughClientOrDirectAsync),
                provider).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ILadsControlFunction function = unit.GetFunction<ILadsControlFunction>(functionName);
            ClientUnit clientUnit = await ClientUnitAsync(connection).ConfigureAwait(false);
            LadsFunction client = await clientUnit.GetFunctionAsync(functionName).ConfigureAwait(false);
            ILadsRunningOperations operations = function.RunningOperations!;

            Assert.That(client.IsControl, Is.True);
            Assert.That(function.RunningOperations, Is.Not.Null);
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState, () => operations.ResetAsync()).ConfigureAwait(false);
            await (useClient ? client.StartAsync().AsTask() : function.StartAsync().AsTask()).ConfigureAwait(false);
            Assert.That(function.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            await AssertFunctionMethodsAsync(client, "Hold", "Suspend", "ToComplete").ConfigureAwait(false);

            await (useClient ? client.HoldAsync().AsTask() : operations.HoldAsync().AsTask()).ConfigureAwait(false);
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Held));
            Assert.That((await client.ReadAsync().ConfigureAwait(false)).RunningState, Is.EqualTo("Held"));
            LadsStateSnapshot held = await client.WaitForRunningStateAsync("Held", s_timeout).ConfigureAwait(false);
            Assert.That(held.CurrentState, Is.EqualTo("Held"));
            await AssertFunctionMethodsAsync(client, "Unhold").ConfigureAwait(false);
            await (useClient ? client.UnholdAsync().AsTask() : operations.UnholdAsync().AsTask())
                .ConfigureAwait(false);
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            await (useClient ? client.SuspendAsync().AsTask() : operations.SuspendAsync().AsTask())
                .ConfigureAwait(false);
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Suspended));
            await AssertFunctionMethodsAsync(client, "Hold", "Unsuspend").ConfigureAwait(false);
            await (useClient ? client.UnsuspendAsync().AsTask() : operations.UnsuspendAsync().AsTask())
                .ConfigureAwait(false);
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            Task complete = useClient ? client.ToCompleteAsync().AsTask() : operations.ToCompleteAsync().AsTask();
            try
            {
                await completing.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(complete.IsCompleted, Is.False);
                Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Completing));
                await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadInvalidState, () => operations.ResetAsync()).ConfigureAwait(false);
                release.TrySetResult(true);
                await complete.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            Assert.That(function.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Complete));
            LadsFunctionSnapshot completed = await client.ReadAsync().ConfigureAwait(false);
            Assert.That(completed.State, Is.EqualTo("Running"));
            Assert.That(completed.RunningState, Is.EqualTo("Complete"));
            await AssertFunctionMethodsAsync(client, "Reset").ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState, () => function.StartAsync()).ConfigureAwait(false);
            await (useClient ? client.ResetAsync().AsTask() : operations.ResetAsync().AsTask()).ConfigureAwait(false);
            Assert.That(function.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState, () => operations.ResetAsync()).ConfigureAwait(false);
            await (useClient ? client.StartAsync().AsTask() : function.StartAsync().AsTask()).ConfigureAwait(false);
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            await (useClient ? client.StopAsync().AsTask() : function.StopAsync().AsTask()).ConfigureAwait(false);
            Assert.That(function.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
            Assert.That(provider.Targets, Is.EqualTo(new[]
            {
                LadsRunningStateValue.Held,
                LadsRunningStateValue.Execute,
                LadsRunningStateValue.Suspended,
                LadsRunningStateValue.Execute,
                LadsRunningStateValue.Completing
            }));
            Assert.That(provider.Functions, Is.All.SameAs(function));
            Assert.That(provider.Commands, Is.EqualTo(new[]
            {
                LadsFunctionCommandKind.Start,
                LadsFunctionCommandKind.Start,
                LadsFunctionCommandKind.Stop
            }));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(device.OperationCycleCounter, Is.Zero);
        }

        [TestCase("Analog")]
        [TestCase("Timer")]
        public async Task ControlFunctionsWithoutRunningProviderAdvertiseNoExecutableRunningMethodsAsync(
            string functionName)
        {
            await using LadsClientConnection connection = await StartFunctionsAsync(
                nameof(ControlFunctionsWithoutRunningProviderAdvertiseNoExecutableRunningMethodsAsync),
                new InMemoryLadsFunctionProvider()).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(UnitName);
            ILadsControlFunction function = unit.GetFunction<ILadsControlFunction>(functionName);
            ClientUnit clientUnit = await ClientUnitAsync(connection).ConfigureAwait(false);
            LadsFunction client = await clientUnit.GetFunctionAsync(functionName).ConfigureAwait(false);
            ILadsControlFunction legacy = unit.GetFunction<ILadsControlFunction>("Legacy");
            LadsFunction legacyClient = await clientUnit.GetFunctionAsync("Legacy").ConfigureAwait(false);

            Assert.That(legacy.RunningOperations, Is.Null);
            Assert.That(legacy.RunningState, Is.Null);
            Assert.That((await legacyClient.GetRunningMethodsAsync().ConfigureAwait(false)).Count, Is.Zero);
            Assert.That(function.RunningOperations, Is.Not.Null);
            await client.StartAsync().ConfigureAwait(false);
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported, () => function.RunningOperations!.HoldAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported, () => function.RunningOperations!.SuspendAsync()).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotSupported, () => function.RunningOperations!.ToCompleteAsync()).ConfigureAwait(false);
            Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            await client.StopAsync().ConfigureAwait(false);
            Assert.That(function.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
        }

        [TestCase("Analog", "Reject", false)]
        [TestCase("Analog", "Throw", false)]
        [TestCase("Analog", "Cancel", false)]
        [TestCase("Timer", "Reject", false)]
        [TestCase("Timer", "Throw", false)]
        [TestCase("Timer", "Cancel", false)]
        [TestCase("Analog", "Reject", true)]
        [TestCase("Analog", "Throw", true)]
        [TestCase("Analog", "Cancel", true)]
        [TestCase("Timer", "Reject", true)]
        [TestCase("Timer", "Throw", true)]
        [TestCase("Timer", "Cancel", true)]
        public async Task ControlFunctionFailedAcknowledgmentLeavesActingStateAndAllowsStopAsync(
            string functionName,
            string failure,
            bool completing)
        {
            var entered = NewSignal();
            var release = NewSignal();
            var provider = new ControlledFunctionProvider(async (_, _, cancellationToken) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (failure == "Throw")
                {
                    throw new InvalidOperationException("Hardware pause failed.");
                }
                return StatusCodes.BadInvalidState;
            });
            await using LadsClientConnection connection = await StartFunctionsAsync(
                nameof(ControlFunctionFailedAcknowledgmentLeavesActingStateAndAllowsStopAsync),
                provider).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsControlFunction function = device.GetFunctionalUnit(UnitName)
                .GetFunction<ILadsControlFunction>(functionName);
            ClientUnit clientUnit = await ClientUnitAsync(connection).ConfigureAwait(false);
            LadsFunction client = await clientUnit.GetFunctionAsync(functionName).ConfigureAwait(false);
            await client.StartAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            LadsRunningStateValue acting = completing
                ? LadsRunningStateValue.Completing : LadsRunningStateValue.Holding;
            Task command = completing
                ? function.RunningOperations!.ToCompleteAsync(cancellation.Token).AsTask()
                : function.RunningOperations!.HoldAsync(cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(command.IsCompleted, Is.False);
                Assert.That(function.RunningState, Is.EqualTo(acting));
                await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
                if (failure == "Cancel")
                {
                    cancellation.Cancel();
                    await Assert.ThatAsync(() => command, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                }
                else
                {
                    release.TrySetResult(true);
                    if (failure == "Throw")
                    {
                        await Assert.ThatAsync(() => command, Throws.TypeOf<InvalidOperationException>())
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await LadsAssert.ThrowsStatusAsync(
                            StatusCodes.BadInvalidState, () => new ValueTask(command)).ConfigureAwait(false);
                    }
                }
                Assert.That(function.State, Is.EqualTo(LadsFunctionalStateValue.Running));
                Assert.That(function.RunningState, Is.EqualTo(acting));
                await LadsAssert.ThrowsStatusAsync(
                    StatusCodes.BadInvalidState, () => function.RunningOperations!.ResetAsync()).ConfigureAwait(false);
                await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
                await client.StopAsync().ConfigureAwait(false);
                Assert.That(function.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
                Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
                Assert.That(provider.Targets, Is.EqualTo(new[]
                {
                    completing ? LadsRunningStateValue.Completing : LadsRunningStateValue.Held
                }));
                Assert.That(provider.Functions, Is.All.SameAs(function));
                Assert.That(provider.Commands, Is.EqualTo(new[]
                {
                    LadsFunctionCommandKind.Start,
                    LadsFunctionCommandKind.Stop
                }));
                await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                cancellation.Cancel();
            }
        }

        [TestCase("Analog", false, false)]
        [TestCase("Analog", true, false)]
        [TestCase("Timer", false, false)]
        [TestCase("Timer", true, false)]
        [TestCase("Analog", false, true)]
        [TestCase("Analog", true, true)]
        [TestCase("Timer", false, true)]
        [TestCase("Timer", true, true)]
        public async Task ControlFunctionStopOrAbortCancelsPendingProviderAcknowledgmentAsync(
            string functionName,
            bool abort,
            bool completing)
        {
            var entered = NewSignal();
            var cancelled = NewSignal();
            var release = NewSignal();
            var provider = new ControlledFunctionProvider(async (_, _, cancellationToken) =>
            {
                entered.TrySetResult(true);
                try
                {
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled.TrySetResult(true);
                    throw;
                }
                return ServiceResult.Good;
            });
            await using LadsClientConnection connection = await StartFunctionsAsync(
                nameof(ControlFunctionStopOrAbortCancelsPendingProviderAcknowledgmentAsync),
                provider).ConfigureAwait(false);
            ILadsDevice device = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsControlFunction function = device.GetFunctionalUnit(UnitName)
                .GetFunction<ILadsControlFunction>(functionName);
            ClientUnit clientUnit = await ClientUnitAsync(connection).ConfigureAwait(false);
            LadsFunction client = await clientUnit.GetFunctionAsync(functionName).ConfigureAwait(false);
            await client.StartAsync().ConfigureAwait(false);
            Task command = completing
                ? function.RunningOperations!.ToCompleteAsync().AsTask()
                : function.RunningOperations!.SuspendAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(function.RunningState, Is.EqualTo(completing
                    ? LadsRunningStateValue.Completing : LadsRunningStateValue.Suspending));
                Assert.That(command.IsCompleted, Is.False);
                await AssertFunctionMethodsAsync(client, completing ? [] : ["Hold"]).ConfigureAwait(false);
                await (abort ? client.AbortAsync().AsTask() : client.StopAsync().AsTask()).WaitAsync(s_timeout)
                    .ConfigureAwait(false);
                await cancelled.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                await Assert.ThatAsync(() => command, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);

                Assert.That(function.State, Is.EqualTo(
                    abort ? LadsFunctionalStateValue.Aborted : LadsFunctionalStateValue.Stopped));
                Assert.That(function.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
                Assert.That(
                    (await client.ReadAsync().ConfigureAwait(false)).State,
                    Is.EqualTo(abort ? "Aborted" : "Stopped"));
                Assert.That(provider.Targets, Is.EqualTo(new[]
                {
                    completing ? LadsRunningStateValue.Completing : LadsRunningStateValue.Suspended
                }));
                Assert.That(provider.Functions, Is.All.SameAs(function));
                Assert.That(provider.Commands, Is.EqualTo(new[]
                {
                    LadsFunctionCommandKind.Start,
                    abort ? LadsFunctionCommandKind.Abort : LadsFunctionCommandKind.Stop
                }));
                await AssertFunctionMethodsAsync(client).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Composes opt-in analog and timer controls alongside a legacy control.
        /// </summary>
        private static Task<LadsClientConnection> StartFunctionsAsync(string name, ILadsFunctionProvider provider)
        {
            return LadsClientConnection.StartAsync(name, builder => builder.AddLads().ConfigureLads(
                context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit(UnitName, unit => unit
                        .FunctionProvider(provider)
                        .AnalogControl("Analog", options => options.EnableRunningOperations = true)
                        .Timer("Timer", options => options.EnableRunningOperations = true)
                        .AnalogControl("Legacy")))));
        }

        /// <summary>
        /// Checks control-function capability discovery and both executable attributes.
        /// </summary>
        private static async Task AssertFunctionMethodsAsync(LadsFunction function, params string[] executable)
        {
            ArrayOf<LadsRunningMethod> methods = await function.GetRunningMethodsAsync().ConfigureAwait(false);
            Assert.That(methods.ToList().Select(method => method.Name), Is.EquivalentTo(s_methods));
            Assert.That(
                methods.ToList().Where(method => method.Executable).Select(method => method.Name),
                Is.EquivalentTo(executable));
            Assert.That(
                methods.ToList().Where(method => method.UserExecutable).Select(method => method.Name),
                Is.EquivalentTo(executable));
        }

        /// <summary>
        /// Composes an opt-in unit with two distinguishable processing steps.
        /// </summary>
        private static Task<LadsClientConnection> StartAsync(
            string name,
            ILadsProgramExecutor? executor = null,
            ManualClock? clock = null,
            ILadsStore? store = null)
        {
            return LadsClientConnection.StartAsync(name, builder =>
            {
                builder.AddLads(options =>
                {
                    options.TimeProvider = clock;
                    options.Store = store;
                })
                    .ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit(UnitName, unit => unit
                        .RunningOperations()
                        .AnalogControl("Temperature")
                        .Programs(programs =>
                        {
                            if (executor != null)
                            {
                                programs.Executor(executor);
                            }
                            programs.Template(TemplateName, template => template
                                .Step(
                                    "First",
                                    TimeSpan.FromMinutes(10),
                                    new LadsSetpoint("Temperature", Variant.From(10.0)))
                                .Step(
                                    "Second",
                                    TimeSpan.Zero,
                                    new LadsSetpoint("Temperature", Variant.From(20.0))));
                        }))));
            });
        }

        /// <summary>
        /// Discovers the opt-in unit over the real client session.
        /// </summary>
        private static async Task<ClientUnit> ClientUnitAsync(LadsClientConnection connection)
        {
            Opc.Ua.LADS.Client.LadsDevice device = await connection.Client.GetDeviceAsync(LadsIncubator.DeviceName)
                .ConfigureAwait(false);
            return await device.GetFunctionalUnitAsync(UnitName).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks capability discovery, including both executable attributes.
        /// </summary>
        private static async Task AssertMethodsAsync(ClientUnit unit, params string[] executable)
        {
            ArrayOf<LadsRunningMethod> methods = await unit.GetRunningMethodsAsync().ConfigureAwait(false);
            Assert.That(methods.ToList().Select(method => method.Name), Is.EquivalentTo(s_methods));
            Assert.That(
                methods.ToList().Where(method => method.Executable).Select(method => method.Name),
                Is.EquivalentTo(executable));
            Assert.That(
                methods.ToList().Where(method => method.UserExecutable).Select(method => method.Name),
                Is.EquivalentTo(executable));
        }

        /// <summary>
        /// Creates a continuation-safe gate.
        /// </summary>
        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Records physical function commands and delegates gated running acknowledgments.
        /// </summary>
        private sealed class ControlledFunctionProvider(
            Func<ILadsControlFunction, LadsRunningStateValue, CancellationToken, ValueTask<ServiceResult>> change)
            : ILadsControlFunctionRunningProvider
        {
            /// <summary>
            /// Gets the acknowledged target requests.
            /// </summary>
            public List<LadsRunningStateValue> Targets { get; } = [];

            /// <summary>
            /// Gets the functions passed to running callbacks.
            /// </summary>
            public List<ILadsControlFunction> Functions { get; } = [];

            /// <summary>
            /// Gets the physical commands issued to the provider.
            /// </summary>
            public List<LadsFunctionCommandKind> Commands { get; } = [];

            /// <inheritdoc/>
            public ValueTask<ServiceResult> OnCommandAsync(
                LadsFunctionCommand command,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Commands.Add(command.Kind);
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            /// <inheritdoc/>
            public ValueTask<ServiceResult> ChangeExecutionAsync(
                ILadsControlFunction function,
                LadsRunningStateValue target,
                CancellationToken cancellationToken)
            {
                Targets.Add(target);
                Functions.Add(function);
                return change(function, target, cancellationToken);
            }
        }

        /// <summary>
        /// Executes cooperatively without advertising hardware-control capability.
        /// </summary>
        private sealed class LegacyExecutor : ILadsProgramExecutor
        {
            /// <inheritdoc/>
            public async ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                run.ReportStep(1, "Wait");
                await run.DelayAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Records callback concurrency while allowing tests to gate hardware acknowledgments.
        /// </summary>
        private sealed class ControlledExecutor(
            Func<LadsProgramRun, LadsRunningStateValue, CancellationToken, ValueTask<ServiceResult>> change,
            Func<LadsProgramRun, CancellationToken, ValueTask>? execute = null)
            : ILadsControlledProgramExecutor
        {
            /// <summary>
            /// Gets the largest number of simultaneous callbacks.
            /// </summary>
            public int MaximumConcurrentCallbacks => Volatile.Read(ref m_maximum);

            /// <inheritdoc/>
            public async ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                if (execute != null)
                {
                    await execute(run, cancellationToken).ConfigureAwait(false);
                    return;
                }
                run.ReportStep(1, "Wait");
                await run.DelayAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask<ServiceResult> ChangeExecutionAsync(
                LadsProgramRun run,
                LadsRunningStateValue target,
                CancellationToken cancellationToken)
            {
                int concurrent = Interlocked.Increment(ref m_concurrent);
                int previous;
                do
                {
                    previous = Volatile.Read(ref m_maximum);
                }
                while (concurrent > previous &&
                    Interlocked.CompareExchange(ref m_maximum, concurrent, previous) != previous);
                try
                {
                    return await change(run, target, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref m_concurrent);
                }
            }

            private int m_concurrent;
            private int m_maximum;
        }

        /// <summary>
        /// Advances program timers explicitly without waiting for wall-clock time.
        /// </summary>
        private sealed class ManualClock : TimeProvider
        {
            /// <inheritdoc/>
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                lock (m_lock)
                {
                    return m_now + m_utcOffset;
                }
            }

            /// <inheritdoc/>
            public override long GetTimestamp()
            {
                lock (m_lock)
                {
                    return m_now.UtcDateTime.Ticks;
                }
            }

            /// <summary>
            /// Changes wall-clock time without changing elapsed time or timer scheduling.
            /// </summary>
            public void AdjustUtc(TimeSpan offset)
            {
                lock (m_lock)
                {
                    m_utcOffset += offset;
                }
            }

            /// <inheritdoc/>
            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                var timer = new ScheduledTimer(this, () => callback(state));
                lock (m_lock)
                {
                    m_timers.Add(timer);
                    timer.Change(dueTime, period);
                }
                return timer;
            }

            /// <summary>
            /// Gets whether a live timer has the specified remaining duration.
            /// </summary>
            public bool HasDelay(TimeSpan remaining)
            {
                lock (m_lock)
                {
                    return m_timers.Any(timer => timer.Due == m_now + remaining);
                }
            }

            /// <summary>
            /// Advances time and fires due callbacks outside the clock lock.
            /// </summary>
            public void Advance(TimeSpan elapsed)
            {
                var callbacks = new List<Action>();
                lock (m_lock)
                {
                    m_now += elapsed;
                    foreach (ScheduledTimer timer in m_timers)
                    {
                        if (timer.Due.HasValue && timer.Due <= m_now)
                        {
                            timer.Due = timer.Period > TimeSpan.Zero ? m_now + timer.Period : null;
                            callbacks.Add(timer.Callback);
                        }
                    }
                }
                foreach (Action callback in callbacks)
                {
                    callback();
                }
            }

            /// <summary>
            /// Represents a timer owned by the manual clock.
            /// </summary>
            private sealed class ScheduledTimer(ManualClock clock, Action callback) : ITimer
            {
                /// <summary>
                /// Gets the timer callback.
                /// </summary>
                public Action Callback { get; } = callback;

                /// <summary>
                /// Gets or sets the next firing time.
                /// </summary>
                public DateTimeOffset? Due { get; set; }

                /// <summary>
                /// Gets the repeat interval.
                /// </summary>
                public TimeSpan Period { get; private set; }

                /// <inheritdoc/>
                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    lock (clock.m_lock)
                    {
                        if (m_disposed)
                        {
                            return false;
                        }
                        Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.m_now + dueTime;
                        Period = period;
                        return true;
                    }
                }

                /// <inheritdoc/>
                public void Dispose()
                {
                    lock (clock.m_lock)
                    {
                        m_disposed = true;
                        Due = null;
                    }
                }

                /// <inheritdoc/>
                public ValueTask DisposeAsync()
                {
                    Dispose();
                    return default;
                }

                private bool m_disposed;
            }

            private readonly Lock m_lock = new();
            private readonly List<ScheduledTimer> m_timers = [];
            private DateTimeOffset m_now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
            private TimeSpan m_utcOffset;
        }

        private const string UnitName = "Controlled";
        private const string TemplateName = "TwoSteps";
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);
        private static readonly string[] s_methods =
            ["Hold", "Unhold", "Suspend", "Unsuspend", "Reset", "ToComplete"];
    }
}
