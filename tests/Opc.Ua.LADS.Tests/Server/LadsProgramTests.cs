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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using LadsNotificationTestSupport = Opc.Ua.LADS.Tests.Server.LadsMaintenanceTaskTests.LadsNotificationTestSupport;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the program manager, the program coordinator and the results.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsProgramTests
    {
        /// <summary>
        /// Verifies program start waits for notification of the committed active-program state.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task StartProgramAwaitsCommittedActiveProgramNotificationAsync(
            bool cancelAfterCommit,
            bool failNotification)
        {
            var notification = new LadsNotificationTestSupport.Gate();
            var executor = new NotificationExecutor();
            var store = new InMemoryLadsStore();
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(StartProgramAwaitsCommittedActiveProgramNotificationAsync),
                builder =>
                {
                    builder.Services.AddSingleton<ILadsProgramExecutor>(executor);
                    builder.Services.AddSingleton<ILadsStore>(store);
                }).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var unit = (LadsFunctionalUnit)device.GetFunctionalUnit(LadsIncubator.UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            var runIdNode = programs.Node.ActiveProgram!.DeviceProgramRunId!;
            NodeStateChangedHandler? previousSync = runIdNode.OnStateChanged;
            NodeStateChangedAsyncHandler? previous = runIdNode.OnStateChangedAsync;
            var syncNotified = LadsNotificationTestSupport.NewSignal();
            runIdNode.OnStateChanged = (context, node, masks) =>
            {
                previousSync?.Invoke(context, node, masks);
                syncNotified.TrySetResult(true);
            };
            var failure = new InvalidOperationException("Active-program notification failed.");
            notification.Failure = failNotification ? failure : null;
            string? notifiedRunId = null;
            runIdNode.OnStateChangedAsync = async (context, node, masks, token) =>
            {
                // Re-enter read-only public handles from the sink, after the device mutation lock.
                if (notification.Calls == 0)
                {
                    Assert.That(syncNotified.Task.IsCompleted, Is.True,
                        "The synchronous state sink must still run before the asynchronous sink.");
                    notifiedRunId = programs.ActiveRun?.RunId;
                    Assert.That(runIdNode.Value, Is.EqualTo(notifiedRunId));
                    Assert.That(programs.ActiveRun!.Template.Id, Is.EqualTo(LadsIncubator.QuickTemplate));
                    Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
                    Assert.That(masks & NodeStateChangeMasks.Value, Is.Not.EqualTo(NodeStateChangeMasks.None));
                }
                await notification.WaitAsync(token).ConfigureAwait(false);
                if (previous != null)
                {
                    await previous(context, node, masks, token).ConfigureAwait(false);
                }
            };

            using var cancellation = new CancellationTokenSource();
            var returned = LadsNotificationTestSupport.NewSignal();
            Task<string>? start = null;
            Task<ILadsResult>? result = null;
            try
            {
                // Do not unwrap Task.Run around the call alone: that would hide a blocking bridge.
                start = Task.Run(() =>
                {
                    Task<string> pending = programs.StartProgramAsync(
                        LadsIncubator.QuickTemplate, cancellationToken: cancellation.Token).AsTask();
                    returned.TrySetResult(true);
                    return pending;
                });
                await notification.Entered.Task.WaitAsync(LadsNotificationTestSupport.Timeout)
                    .ConfigureAwait(false);
                await returned.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                Assert.That(start.IsCompleted, Is.False);
                Assert.That(executor.Started.Task.IsCompleted, Is.False);
                Assert.That(unit.Gate.CurrentCount, Is.Zero);
                Assert.That(notifiedRunId, Is.Not.Null.And.Not.Empty);
                result = programs.WaitForResultAsync(notifiedRunId!).AsTask();
                Assert.That(result.IsCompleted, Is.False);
                LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
                Assert.That(
                    saved.FunctionalUnits.ToList().Single(value => value.Name == unit.Name).InterruptedRunId,
                    Is.EqualTo(notifiedRunId));

                if (cancelAfterCommit)
                {
                    cancellation.Cancel();
                }
                Assert.That(notification.Token.CanBeCanceled, Is.False,
                    "The committed active program must notify independently of the caller token.");
                Assert.That(start.IsCompleted, Is.False);
                notification.Release.TrySetResult(true);
                if (failNotification)
                {
                    await Assert.ThatAsync(
                        () => start.WaitAsync(LadsNotificationTestSupport.Timeout),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                    await Assert.ThatAsync(
                        () => result.WaitAsync(LadsNotificationTestSupport.Timeout),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                    Assert.That(programs.ActiveRun, Is.Null);
                    Assert.That(runIdNode.Value, Is.Empty);
                    Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
                    Assert.That(executor.Started.Task.IsCompleted, Is.False);
                }
                else
                {
                    Assert.That(
                        await start.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false),
                        Is.EqualTo(notifiedRunId));
                    await executor.Started.Task.WaitAsync(LadsNotificationTestSupport.Timeout).ConfigureAwait(false);
                    Assert.That(programs.ActiveRun!.RunId, Is.EqualTo(notifiedRunId));
                    Assert.That(programs.Results.Count, Is.Zero);
                    Assert.That(result.IsCompleted, Is.False);
                }
                Assert.That(unit.Gate.CurrentCount, Is.EqualTo(1));
            }
            finally
            {
                // Remove the sink before cleanup/reset notifications, including failure cleanup.
                runIdNode.OnStateChanged = previousSync;
                runIdNode.OnStateChangedAsync = previous;
                notification.Release.TrySetResult(true);
                executor.Release.TrySetResult(true);
                if (start != null)
                {
                    await LadsNotificationTestSupport.DrainAsync(start).ConfigureAwait(false);
                }
                if (result != null)
                {
                    await LadsNotificationTestSupport.DrainAsync(result).ConfigureAwait(false);
                }
            }
            if (!failNotification)
            {
                ILadsResult completed = await result!.WaitAsync(LadsNotificationTestSupport.Timeout)
                    .ConfigureAwait(false);
                Assert.That(completed.RunId, Is.EqualTo(notifiedRunId));
                Assert.That(completed.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
                Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task QuickProgramCompletesWithAResultFileAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(QuickProgramCompletesWithAResultFileAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;
            var added = new List<ILadsResult>();
            programs.ResultAdded += added.Add;

            Assert.That(programs.FunctionalUnit, Is.SameAs(unit));
            Assert.That(programs.Templates.Count, Is.EqualTo(2));
            string runId = await programs.StartProgramAsync(
                LadsIncubator.QuickTemplate,
                supervisoryJobId: "Job",
                supervisoryTaskId: "Task").ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);

            Assert.That(result.RunId, Is.EqualTo(runId));
            Assert.That(result.Name, Is.EqualTo(runId));
            Assert.That(result.TemplateId, Is.EqualTo(LadsIncubator.QuickTemplate));
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That((DateTime)result.Stopped, Is.GreaterThanOrEqualTo((DateTime)result.Started));
            Assert.That(result.Files.Count, Is.EqualTo(1));
            Assert.That(result.Files[0].Name, Is.EqualTo("Protocol.csv"));
            Assert.That(result.Files[0].MimeType, Is.EqualTo("text/csv"));
            Assert.That(await programs.WaitForResultAsync(runId).ConfigureAwait(false), Is.SameAs(result));
            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(programs.ActiveRun, Is.Null);
            await LadsAssert.EventuallyAsync(() => added.Count == 1, "The result event was not raised.")
                .ConfigureAwait(false);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Complete));
            Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
            Assert.That(
                unit.GetFunction<IAnalogControlFunction>("ShakerSpeed").TargetValue,
                Is.EqualTo(200));

            NodeId resultNode = await server.ChildAsync(unit.NodeId, "ProgramManager", "ResultSet", runId)
                .ConfigureAwait(false);
            Assert.That(resultNode, Is.EqualTo(result.NodeId));
            DataValue job = await server.ReadAsync(await server.ChildAsync(resultNode, "SupervisoryJobId")
                .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(job.WrappedValue.TryGetValue(out string jobId), Is.True);
            Assert.That(jobId, Is.EqualTo("Job"));
            NodeId steps = await server.ChildAsync(resultNode, "VariableSet", "Steps").ConfigureAwait(false);
            DataValue stepCount = await server.ReadAsync(steps).ConfigureAwait(false);
            Assert.That(stepCount.WrappedValue.TryGetValue(out string count), Is.True);
            Assert.That(count, Is.EqualTo("2"));

            NodeId fileNode = await server.ChildAsync(resultNode, "FileSet", "Protocol.csv").ConfigureAwait(false);
            DataValue mimeType = await server.ReadAsync(await server.ChildAsync(fileNode, "MimeType")
                .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(mimeType.WrappedValue.TryGetValue(out string mime), Is.True);
            Assert.That(mime, Is.EqualTo("text/csv"));
            NodeId file = await server.ChildAsync(fileNode, "File").ConfigureAwait(false);
            DataValue size = await server.ReadAsync(await server.ChildAsync(file, "Size").ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(size.WrappedValue.TryGetValue(out ulong length), Is.True, size.WrappedValue.ToString());
            Assert.That(length, Is.EqualTo((ulong)result.Files[0].Content.Length));
            string protocol = Encoding.UTF8.GetString(result.Files[0].Content.ToArray());
            Assert.That(protocol, Does.Contain("Hold"));
        }

        [Test]
        public async Task OnlyOneProgramRunsPerFunctionalUnitAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(OnlyOneProgramRunsPerFunctionalUnitAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotFound,
                () => programs.StartProgramAsync("Missing")).ConfigureAwait(false);
            string runId = await programs.StartProgramAsync(LadsIncubator.LongTemplate).ConfigureAwait(false);
            Assert.That(programs.ActiveRun, Is.Not.Null);
            Assert.That(programs.ActiveRun!.RunId, Is.EqualTo(runId));
            Assert.That(programs.ActiveRun.Template.Id, Is.EqualTo(LadsIncubator.LongTemplate));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Execute));
            Assert.That(device.ItemState, Is.EqualTo(Machinery.Server.StateMachines.MachineryItemStateValue.Executing));

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => programs.StartProgramAsync(LadsIncubator.QuickTemplate)).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(StatusCodes.BadInvalidState, () => unit.StartAsync())
                .ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => programs.RemoveTemplateAsync(LadsIncubator.LongTemplate)).ConfigureAwait(false);

            NodeId active = await server.ChildAsync(unit.NodeId, "ProgramManager", "ActiveProgram")
                .ConfigureAwait(false);
            string[] activeChildren = await server.ChildNamesAsync(active).ConfigureAwait(false);
            Assert.That(activeChildren, Is.Not.Empty);

            await unit.StopAsync().ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Stopped));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(unit.RunningState, Is.EqualTo(LadsRunningStateValue.Idle));
            Assert.That(device.OperationCycleCounter, Is.Zero);
            Assert.That(programs.ActiveRun, Is.Null);
        }

        [Test]
        public async Task AbortEndsTheRunUntilClearedAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(AbortEndsTheRunUntilClearedAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;

            string runId = await programs.StartProgramAsync(LadsIncubator.LongTemplate).ConfigureAwait(false);
            await unit.AbortAsync().ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Aborted));
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidState,
                () => programs.StartProgramAsync(LadsIncubator.QuickTemplate)).ConfigureAwait(false);

            await unit.ClearAsync().ConfigureAwait(false);
            string next = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult completed = await programs.WaitForResultAsync(next).ConfigureAwait(false);
            Assert.That(completed.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That(programs.Results.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ExecutorFailureAbortsTheRunAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(ExecutorFailureAbortsTheRunAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit.Programs(p => p
                        .Executor(new FailingExecutor())
                        .Template("Fail", t => t.Step("Boom", TimeSpan.Zero))))))).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit aux = device.GetFunctionalUnit("Aux");

            string runId = await aux.Programs!.StartProgramAsync("Fail").ConfigureAwait(false);
            ILadsResult result = await aux.Programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Aborted));
            Assert.That(aux.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            Assert.That(result.Files.Count, Is.Zero);
        }

        [Test]
        public async Task CustomExecutorReportsStepsVariablesAndFilesAsync()
        {
            var executor = new GatedExecutor();
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(CustomExecutorReportsStepsVariablesAndFilesAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit.Programs(p => p
                        .Executor(executor)
                        .Template(new LadsProgramTemplate("Gated").Step("Wait", TimeSpan.FromSeconds(1))))))))
                .ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager programs = device.GetFunctionalUnit("Aux").Programs!;

            string runId = await programs.StartProgramAsync("Gated").ConfigureAwait(false);
            await executor.Started.Task.ConfigureAwait(false);
            Assert.That(programs.ActiveRun!.CurrentStepNumber, Is.EqualTo(1));
            Assert.That(programs.ActiveRun.Variables.Count, Is.EqualTo(1));
            executor.Release.SetResult(true);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            Assert.That(result.Files.Count, Is.EqualTo(1));
            Assert.That(result.Files[0].Name, Is.EqualTo("data.json"));
            NodeId value = await server.ChildAsync(result.NodeId, "VariableSet", "Yield").ConfigureAwait(false);
            DataValue read = await server.ReadAsync(value).ConfigureAwait(false);
            Assert.That(read.WrappedValue.TryGetValue(out string yield), Is.True);
            Assert.That(yield, Is.EqualTo("98"));
        }

        [Test]
        public async Task ConcurrentStartsAllowExactlyOneRunAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ConcurrentStartsAllowExactlyOneRunAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;

            Task<string>[] starts = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => programs.StartProgramAsync(LadsIncubator.LongTemplate).AsTask()))
                .ToArray();
            int succeeded = 0;
            int rejected = 0;
            foreach (Task<string> start in starts)
            {
                try
                {
                    await start.ConfigureAwait(false);
                    succeeded++;
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadInvalidState)
                {
                    rejected++;
                }
            }

            Assert.That(succeeded, Is.EqualTo(1));
            Assert.That(rejected, Is.EqualTo(7));
            await unit.StopAsync().ConfigureAwait(false);
            Assert.That(programs.ActiveRun, Is.Null);
        }

        [Test]
        public async Task ConcurrentStopAndAbortProduceASingleResultAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ConcurrentStopAndAbortProduceASingleResultAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;

            string runId = await programs.StartProgramAsync(LadsIncubator.LongTemplate).ConfigureAwait(false);
            Task stop = Task.Run(async () => await IgnoreInvalidStateAsync(unit.StopAsync()).ConfigureAwait(false));
            Task abort = Task.Run(async () => await IgnoreInvalidStateAsync(unit.AbortAsync()).ConfigureAwait(false));
            await Task.WhenAll(stop, abort).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);

            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(
                result.Outcome,
                Is.EqualTo(LadsProgramOutcome.Stopped).Or.EqualTo(LadsProgramOutcome.Aborted));
            Assert.That(
                unit.State,
                Is.EqualTo(result.Outcome == LadsProgramOutcome.Stopped
                    ? LadsFunctionalStateValue.Stopped
                    : LadsFunctionalStateValue.Aborted));
        }

        [Test]
        public async Task ResultsArePrunedAndRemovableAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ResultsArePrunedAndRemovableAsync),
                options: o => o.MaxRetainedResults = 1).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;
            NodeId resultSet = await server.ChildAsync(unit.NodeId, "ProgramManager", "ResultSet")
                .ConfigureAwait(false);

            string first = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await programs.WaitForResultAsync(first).ConfigureAwait(false);
            string second = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await programs.WaitForResultAsync(second).ConfigureAwait(false);

            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(programs.Results[0].RunId, Is.EqualTo(second));
            string[] names = await server.ChildNamesAsync(resultSet).ConfigureAwait(false);
            Assert.That(names, Does.Contain(second));
            Assert.That(names, Does.Not.Contain(first));
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotFound,
                () => programs.WaitForResultAsync(first)).ConfigureAwait(false);

            Assert.That(await programs.RemoveResultAsync(second).ConfigureAwait(false), Is.True);
            Assert.That(await programs.RemoveResultAsync(second).ConfigureAwait(false), Is.False);
            Assert.That(programs.Results.Count, Is.Zero);
            names = await server.ChildNamesAsync(resultSet).ConfigureAwait(false);
            Assert.That(names, Does.Not.Contain(second));
        }

        [Test]
        public async Task TemplatesCanBeAddedAndRemovedAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(TemplatesCanBeAddedAndRemovedAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;
            NodeId templateSet = await server.ChildAsync(unit.NodeId, "ProgramManager", "ProgramTemplateSet")
                .ConfigureAwait(false);

            LadsProgramTemplate template = new LadsProgramTemplate("Overnight")
                .WithDescription(new LocalizedText("Overnight incubation"))
                .WithAuthor("Lab", "2.0")
                .Step("Heat", TimeSpan.FromMinutes(5), new LadsSetpoint("Temperature", Variant.From(37.0)))
                .Step("Hold", TimeSpan.FromHours(8));
            await programs.AddTemplateAsync(template).ConfigureAwait(false);
            Assert.That(programs.Templates.ToArray()!.Any(t => t.Id == "Overnight"), Is.True);
            string[] names = await server.ChildNamesAsync(templateSet).ConfigureAwait(false);
            Assert.That(names, Does.Contain("Overnight"));
            NodeId author = await server.ChildAsync(templateSet, "Overnight", "Author").ConfigureAwait(false);
            DataValue value = await server.ReadAsync(author).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out string text), Is.True);
            Assert.That(text, Is.EqualTo("Lab"));

            Assert.That(await programs.RemoveTemplateAsync("Overnight").ConfigureAwait(false), Is.True);
            Assert.That(await programs.RemoveTemplateAsync("Overnight").ConfigureAwait(false), Is.False);
            names = await server.ChildNamesAsync(templateSet).ConfigureAwait(false);
            Assert.That(names, Does.Not.Contain("Overnight"));
        }

        [TestCase("Spin", "ShakerSpeed")]
        [TestCase("Ramp; \"fast\"\r\nthen hold", "Speed; \"target\"\nvalue")]
        [TestCase("\"Quoted\"", "\"Function\"")]
        [TestCase(" Leading and trailing ", " Spaced ")]
        public void TemplateContentRoundTrips(string stepName, string functionName)
        {
            LadsProgramTemplate template = new LadsProgramTemplate("Shake")
                .WithDescription(new LocalizedText("Shake it"))
                .Step(
                    stepName,
                    TimeSpan.FromSeconds(90),
                    new LadsSetpoint(functionName, Variant.From(300.0)),
                    new LadsSetpoint("Door", Variant.From(true)),
                    new LadsSetpoint("Mode", Variant.From(2u)))
                .Step("Rest", TimeSpan.FromSeconds(30));

            LadsProgramTemplate parsed = LadsProgramTemplate.Parse("Shake", template.ToContent());

            Assert.That(parsed.Id, Is.EqualTo("Shake"));
            Assert.That(parsed.Steps.Count, Is.EqualTo(2));
            Assert.That(parsed.Steps[0].Name, Is.EqualTo(stepName));
            Assert.That(parsed.Steps[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(parsed.Steps[0].Setpoints.Count, Is.EqualTo(3));
            Assert.That(parsed.Steps[0].Setpoints[0].FunctionName, Is.EqualTo(functionName));
            Assert.That(parsed.Steps[0].Setpoints[0].Value.TryGetValue(out double speed), Is.True);
            Assert.That(speed, Is.EqualTo(300));
            Assert.That(parsed.Steps[0].Setpoints[1].Value.TryGetValue(out bool door), Is.True);
            Assert.That(door, Is.True);
            Assert.That(parsed.Steps[0].Setpoints[2].Value.TryGetValue(out uint mode), Is.True);
            Assert.That(mode, Is.EqualTo(2u));
            Assert.That(parsed.EstimatedRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(template.EstimatedRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(
                () => LadsProgramTemplate.Parse("Bad", ByteString.From(Encoding.UTF8.GetBytes("{not json"))),
                Throws.InstanceOf<ServiceResultException>());
        }

        [TestCase("step;\"Unclosed;1")]
        [TestCase("step;\"Closed\"suffix;1")]
        [TestCase("step;Too;Many;1")]
        [TestCase("step;Missing")]
        [TestCase("set;BeforeStep;true")]
        public void TemplateContentRejectsMalformedRecords(string text)
        {
            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => LadsProgramTemplate.Parse("Bad", ByteString.From(Encoding.UTF8.GetBytes(text))));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void TemplateContentPreservesLegacyUnquotedRecords()
        {
            var template = new LadsProgramTemplate("Legacy")
                .Step("Spin", TimeSpan.FromMilliseconds(125), new LadsSetpoint("Mode", Variant.From(2u)));
            Assert.That(Encoding.UTF8.GetString(template.ToContent().ToArray()),
                Is.EqualTo("step;Spin;125\nset;Mode;u2\n"));
            LadsProgramTemplate parsed = LadsProgramTemplate.Parse(
                "Legacy", ByteString.From(Encoding.UTF8.GetBytes("\r\nstep;Spin;125\r\nset;Mode;u2\r\n")));
            Assert.That(parsed.Steps.Count, Is.EqualTo(1));
            Assert.That(parsed.Steps[0].Name, Is.EqualTo("Spin"));
            Assert.That(parsed.Steps[0].Duration, Is.EqualTo(TimeSpan.FromMilliseconds(125)));
            Assert.That(parsed.Steps[0].Setpoints[0].Value.TryGetValue(out uint mode), Is.True);
            Assert.That(mode, Is.EqualTo(2u));
        }

        /// <summary>
        /// Accepts legacy trailing record whitespace without changing step or function names.
        /// </summary>
        [TestCase(" \n")]
        [TestCase("\t\r\n")]
        [TestCase(" \t")]
        public void TemplateContentPreservesTrailingRecordWhitespace(string ending)
        {
            string text = "step; Spin ;1" + ending + "\n"
                + "set; Door ;true" + ending + "\n"
                + "set;Other;false" + ending + "\n"
                + "set;Mode;u2" + ending + "\n"
                + "set;Speed;1.5" + ending;
            LadsProgramTemplate parsed = LadsProgramTemplate.Parse(
                "Legacy", ByteString.From(Encoding.UTF8.GetBytes(text)));

            Assert.That(parsed.Steps.Count, Is.EqualTo(1));
            Assert.That(parsed.Steps[0].Name, Is.EqualTo(" Spin "));
            Assert.That(parsed.Steps[0].Duration, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
            ArrayOf<LadsSetpoint> setpoints = parsed.Steps[0].Setpoints;
            Assert.That(setpoints.Count, Is.EqualTo(4));
            Assert.That(setpoints[0].FunctionName, Is.EqualTo(" Door "));
            Assert.That(setpoints[0].Value.TryGetValue(out bool opened), Is.True);
            Assert.That(opened, Is.True);
            Assert.That(setpoints[1].Value.TryGetValue(out bool closed), Is.True);
            Assert.That(closed, Is.False);
            Assert.That(setpoints[2].Value.TryGetValue(out uint mode), Is.True);
            Assert.That(mode, Is.EqualTo(2u));
            Assert.That(setpoints[3].Value.TryGetValue(out double speed), Is.True);
            Assert.That(speed, Is.EqualTo(1.5));
        }

        [Test]
        public async Task ActiveRecipeKeepsFullDurationAndIsDetachedFromCallerMutationsAsync()
        {
            var executor = new GatedExecutor();
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ActiveRecipeKeepsFullDurationAndIsDetachedFromCallerMutationsAsync),
                builder => builder.Services.AddSingleton<ILadsProgramExecutor>(executor)).ConfigureAwait(false);
            ILadsProgramManager programs = (await server.DeviceAsync().ConfigureAwait(false))
                .GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            LadsSetpoint[] setpoints = [new("Temperature", Variant.From(20.0))];
            var template = new LadsProgramTemplate("Detached")
                .WithAuthor("Original")
                .Step(new LadsProgramStep("Submillisecond", TimeSpan.FromTicks(12345), setpoints));
            await programs.AddTemplateAsync(template).ConfigureAwait(false);
            string runId = await programs.StartProgramAsync(template.Id).ConfigureAwait(false);
            try
            {
                await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                template.Author = "Changed";
                template.Step("Later", TimeSpan.FromHours(1));
                setpoints[0] = new LadsSetpoint("Temperature", Variant.From(99.0));
                LadsProgramTemplate active = programs.ActiveRun!.Template;
                Assert.That(active, Is.Not.SameAs(template));
                Assert.That(active.Author, Is.EqualTo("Original"));
                Assert.That(active.Steps.Count, Is.EqualTo(1));
                Assert.That(active.Steps[0].Duration, Is.EqualTo(TimeSpan.FromTicks(12345)));
                Assert.That(active.Steps[0].Setpoints[0].Value.TryGetValue(out double target), Is.True);
                Assert.That(target, Is.EqualTo(20.0));
            }
            finally
            {
                executor.Release.TrySetResult(true);
            }
            await programs.WaitForResultAsync(runId).ConfigureAwait(false);
        }

        [Test]
        public async Task StartProgramMethodReturnsTheRunIdAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(StartProgramMethodReturnsTheRunIdAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            NodeId state = await server.ChildAsync(unit.NodeId, "FunctionalUnitState").ConfigureAwait(false);

            CallMethodResult missing = await server.CallByNameAsync(state, "StartProgram").ConfigureAwait(false);
            Assert.That(StatusCode.IsBad(missing.StatusCode), Is.True);
            CallMethodResult call = await server.CallByNameAsync(
                state,
                "StartProgram",
                Variant.From(LadsIncubator.QuickTemplate),
                Variant.From(default(ArrayOf<ExtensionObject>)),
                Variant.From("Job"),
                Variant.From("Task"),
                Variant.From(default(ArrayOf<ExtensionObject>))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(call.StatusCode), Is.True, call.StatusCode.ToString());
            Assert.That(call.OutputArguments[0].TryGetValue(out string runId), Is.True);

            ILadsResult result = await unit.Programs!.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));
            CallMethodResult unknown = await server.CallByNameAsync(
                state,
                "StartProgram",
                Variant.From("Missing"),
                Variant.From(default(ArrayOf<ExtensionObject>)),
                Variant.From(string.Empty),
                Variant.From(string.Empty),
                Variant.From(default(ArrayOf<ExtensionObject>))).ConfigureAwait(false);
            Assert.That(unknown.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task StoppingTheServerEndsTheActiveRunAsync()
        {
            LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(StoppingTheServerEndsTheActiveRunAsync)).ConfigureAwait(false);
            Task<ILadsResult> waiter;
            try
            {
                ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
                ILadsProgramManager programs = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
                string runId = await programs.StartProgramAsync(LadsIncubator.LongTemplate).ConfigureAwait(false);
                waiter = programs.WaitForResultAsync(runId).AsTask();
            }
            finally
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(
                () => waiter,
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task WaitForResultHonorsCancellationAsync()
        {
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(WaitForResultHonorsCancellationAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotFound,
                () => programs.WaitForResultAsync("unknown")).ConfigureAwait(false);
            string runId = await programs.StartProgramAsync(LadsIncubator.LongTemplate).ConfigureAwait(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            Assert.That(
                async () => await programs.WaitForResultAsync(runId, cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            await unit.StopAsync().ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(result.Outcome, Is.EqualTo(LadsProgramOutcome.Stopped));
        }

        [Test]
        public async Task ProtocolCsvKeepsSpecialStepNameInOneFieldAndRecordAsync()
        {
            const string stepName = "Ramp; \"fast\"\nthen hold";
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ProtocolCsvKeepsSpecialStepNameInOneFieldAndRecordAsync)).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager programs = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            var template = new LadsProgramTemplate("CsvEscaping")
                .Step(stepName, TimeSpan.Zero);
            await programs.AddTemplateAsync(template).ConfigureAwait(false);

            string runId = await programs.StartProgramAsync(template.Id).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
            string protocol = Encoding.UTF8.GetString(result.Files[0].Content.ToArray());
            List<string[]> records = ParseSemicolonCsv(protocol);

            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records[0], Has.Length.EqualTo(4));
            Assert.That(records[0][0], Is.EqualTo("step"));
            Assert.That(records[0][1], Is.EqualTo("name"));
            Assert.That(records[0][2], Is.EqualTo("started"));
            Assert.That(records[0][3], Is.EqualTo("durationMs"));
            Assert.That(records[1], Has.Length.EqualTo(4));
            Assert.That(records[1][0], Is.EqualTo("1"));
            Assert.That(records[1][1], Is.EqualTo(stepName));
            Assert.That(records[1][2], Does.EndWith("Z"));
            Assert.That(records[1][3], Is.EqualTo("0"));
        }

        private static List<string[]> ParseSemicolonCsv(string text)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int ii = 0; ii < text.Length; ii++)
            {
                char character = text[ii];
                if (quoted)
                {
                    if (character == '"' && ii + 1 < text.Length && text[ii + 1] == '"')
                    {
                        field.Append('"');
                        ii++;
                    }
                    else if (character == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        field.Append(character);
                    }
                }
                else if (character == '"' && field.Length == 0)
                {
                    quoted = true;
                }
                else if (character == ';')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (character is '\r' or '\n')
                {
                    if (character == '\r' && ii + 1 < text.Length && text[ii + 1] == '\n')
                    {
                        ii++;
                    }
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(fields.ToArray());
                    fields.Clear();
                }
                else
                {
                    field.Append(character);
                }
            }

            if (quoted)
            {
                throw new FormatException("The CSV contains an unterminated quoted field.");
            }
            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                records.Add(fields.ToArray());
            }
            return records;
        }

        private static async Task IgnoreInvalidStateAsync(ValueTask command)
        {
            try
            {
                await command.ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadInvalidState)
            {
            }
        }

        private sealed class FailingExecutor : ILadsProgramExecutor
        {
            public ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("The device failed.");
            }
        }

        private sealed class NotificationExecutor : ILadsProgramExecutor
        {
            public TaskCompletionSource<bool> Started { get; } = LadsNotificationTestSupport.NewSignal();

            public TaskCompletionSource<bool> Release { get; } = LadsNotificationTestSupport.NewSignal();

            public async ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                await Release.Task.WaitAsync(LadsNotificationTestSupport.Timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private sealed class GatedExecutor : ILadsProgramExecutor
        {
            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                run.ReportStep(1, "Wait");
                run.SetResultVariable("Yield", "98");
                run.AddResultFile("data.json", "application/json", ByteString.From(Encoding.UTF8.GetBytes("{}")));
                Started.SetResult(true);
                await Release.Task.ConfigureAwait(false);
            }
        }
    }
}
