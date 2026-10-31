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

        [Test]
        public void TemplateContentRoundTrips()
        {
            LadsProgramTemplate template = new LadsProgramTemplate("Shake")
                .WithDescription(new LocalizedText("Shake it"))
                .Step(
                    "Spin",
                    TimeSpan.FromSeconds(90),
                    new LadsSetpoint("ShakerSpeed", Variant.From(300.0)),
                    new LadsSetpoint("Door", Variant.From(true)))
                .Step("Rest", TimeSpan.FromSeconds(30));

            LadsProgramTemplate parsed = LadsProgramTemplate.Parse("Shake", template.ToContent());

            Assert.That(parsed.Id, Is.EqualTo("Shake"));
            Assert.That(parsed.Steps.Count, Is.EqualTo(2));
            Assert.That(parsed.Steps[0].Name, Is.EqualTo("Spin"));
            Assert.That(parsed.Steps[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(parsed.Steps[0].Setpoints.Count, Is.EqualTo(2));
            Assert.That(parsed.Steps[0].Setpoints[0].FunctionName, Is.EqualTo("ShakerSpeed"));
            Assert.That(parsed.Steps[0].Setpoints[0].Value.TryGetValue(out double speed), Is.True);
            Assert.That(speed, Is.EqualTo(300));
            Assert.That(parsed.EstimatedRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(template.EstimatedRuntime, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(
                () => LadsProgramTemplate.Parse("Bad", ByteString.From(Encoding.UTF8.GetBytes("{not json"))),
                Throws.InstanceOf<ServiceResultException>());
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
