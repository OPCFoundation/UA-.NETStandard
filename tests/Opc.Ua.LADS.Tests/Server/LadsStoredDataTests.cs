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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Integration;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;
using LadsClientModel = Opc.Ua.LADS.Client;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests retained device data independently from physical execution recovery.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsStoredDataTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RestartRestoresTemplatesResultsFilesMaintenanceAndCountersAsync(bool registerStore)
        {
            const string stepName = "Uploaded; \"quoted\"\r\nstep";
            var store = new InMemoryLadsStore();
            string runId;
            ByteString bytes;
            DateTimeUtc executed;
            TimeSpan duration;
            await using (LadsHostedServer first = await LadsHostedServer.StartIncubatorAsync(
                nameof(RestartRestoresTemplatesResultsFilesMaintenanceAndCountersAsync),
                builder =>
                {
                    if (registerStore)
                    {
                        builder.Services.AddSingleton<ILadsStore>(store);
                    }
                },
                options =>
                {
                    options.TimeProvider = new SteppingClock();
                    if (!registerStore)
                    {
                        options.Store = store;
                    }
                }).ConfigureAwait(false))
            {
                ILadsDevice device = await first.DeviceAsync().ConfigureAwait(false);
                ILadsProgramManager programs = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
                await programs.AddTemplateAsync(new LadsProgramTemplate("Uploaded")
                    .Step(stepName, TimeSpan.Zero)).ConfigureAwait(false);
                runId = await programs.StartProgramAsync("Uploaded").ConfigureAwait(false);
                ILadsResult result = await programs.WaitForResultAsync(runId).ConfigureAwait(false);
                bytes = result.Files[0].Content;
                ILadsMaintenanceTask maintenance = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
                await maintenance.StartAsync().ConfigureAwait(false);
                await maintenance.StopAsync(MaintenanceTaskResultEnum.Success, new LocalizedText("Calibrated"))
                    .ConfigureAwait(false);
                executed = maintenance.LastExecutionDate;
                Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
                duration = device.OperationDuration;
                Assert.That(duration, Is.GreaterThan(TimeSpan.Zero));
            }
            await using LadsClientConnection second = await LadsClientConnection.StartAsync(
                nameof(RestartRestoresTemplatesResultsFilesMaintenanceAndCountersAsync) + "Restart",
                builder =>
                {
                    builder.AddLads(options =>
                    {
                        if (!registerStore)
                        {
                            options.Store = store;
                        }
                    }).ConfigureLads(LadsIncubator.Configure);
                    if (registerStore)
                    {
                        builder.Services.AddSingleton<ILadsStore>(store);
                    }
                })
                .ConfigureAwait(false);
            ILadsDevice restored = await second.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = restored.GetFunctionalUnit(LadsIncubator.UnitName);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(unit.Programs!.ActiveRun, Is.Null);
            Assert.That(unit.Programs.Templates.ToList().Select(template => template.Id), Does.Contain("Uploaded"));
            Assert.That(unit.Programs.Templates.ToList().Single(template => template.Id == "Uploaded").Steps[0].Name,
                Is.EqualTo(stepName));
            Assert.That(unit.Programs.Results.Count, Is.EqualTo(1));
            Assert.That(unit.Programs.Results[0].RunId, Is.EqualTo(runId));
            Assert.That(unit.Programs.Results[0].Files[0].Content.ToArray(), Is.EqualTo(bytes.ToArray()));
            Assert.That(restored.OperationCycleCounter, Is.EqualTo(1));
            Assert.That(restored.OperationDuration, Is.EqualTo(duration));
            Assert.That(restored.PowerOnDuration, Is.GreaterThan(TimeSpan.Zero));
            ILadsMaintenanceTask task = restored.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            Assert.That(task.LastExecutionDate, Is.EqualTo(executed));
            Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Success));
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Finished));
            LadsClientModel.LadsFunctionalUnit chamber = await second.ChamberAsync().ConfigureAwait(false);
            LadsClientModel.LadsResult? remote = await chamber.FindResultAsync(runId).ConfigureAwait(false);
            Assert.That(remote, Is.Not.Null);
            ArrayOf<LadsClientModel.LadsResultFile> files = remote!.Files;
            Assert.That(files.Count, Is.EqualTo(1));
            ByteString downloaded = await second.Client.ReadResultFileAsync(files[0]).ConfigureAwait(false);
            Assert.That(downloaded.ToArray(), Is.EqualTo(bytes.ToArray()));
        }

        [Test]
        public async Task InterruptedExecutionIsRecordedButNeverResumedAsync()
        {
            var store = new InMemoryLadsStore();
            var executor = new CountingExecutor();
            string runId;
            await using (LadsHostedServer first = await LadsHostedServer.StartIncubatorAsync(
                nameof(InterruptedExecutionIsRecordedButNeverResumedAsync),
                builder => builder.Services.AddSingleton<ILadsProgramExecutor>(executor),
                options => options.Store = store).ConfigureAwait(false))
            {
                ILadsDevice device = await first.DeviceAsync().ConfigureAwait(false);
                runId = await device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!
                    .StartProgramAsync(LadsIncubator.LongTemplate).ConfigureAwait(false);
            }
            await using LadsHostedServer second = await LadsHostedServer.StartIncubatorAsync(
                nameof(InterruptedExecutionIsRecordedButNeverResumedAsync) + "Restart",
                builder => builder.Services.AddSingleton<ILadsProgramExecutor>(executor),
                options => options.Store = store).ConfigureAwait(false);
            ILadsDevice restored = await second.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = restored.GetFunctionalUnit(LadsIncubator.UnitName);
            Assert.That(executor.Executions, Is.EqualTo(1));
            Assert.That(unit.Programs!.InterruptedRunId, Is.EqualTo(runId));
            Assert.That(unit.Programs.ActiveRun, Is.Null);
            Assert.That(unit.Programs.Results.Count, Is.Zero);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));
            Assert.That(restored.OperationCycleCounter, Is.Zero);

            executor.CompleteImmediately = true;
            string next = await unit.Programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await unit.Programs.WaitForResultAsync(next).ConfigureAwait(false);
            LadsStoredDeviceData? committed = await store.LoadAsync(
                "urn:opcua-netstandard:lads:instances#" + LadsIncubator.DeviceName).ConfigureAwait(false);
            Assert.That(committed, Is.Not.Null);
            Assert.That(committed!.FunctionalUnits[0].InterruptedRunId, Is.Null);
            Assert.That(committed.FunctionalUnits[0].LastInterruptedRunId, Is.EqualTo(runId));
            Assert.That(executor.Executions, Is.EqualTo(2));
        }

        [Test]
        public async Task ExplicitTemplateDeletionAndResultRetentionSurviveRestartAsync()
        {
            var store = new InMemoryLadsStore();
            string last;
            await using (LadsHostedServer first = await LadsHostedServer.StartIncubatorAsync(
                nameof(ExplicitTemplateDeletionAndResultRetentionSurviveRestartAsync),
                options: options =>
                {
                    options.Store = store;
                    options.MaxRetainedResults = 1;
                }).ConfigureAwait(false))
            {
                ILadsProgramManager programs = (await first.DeviceAsync().ConfigureAwait(false))
                    .GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
                string old = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
                await programs.WaitForResultAsync(old).ConfigureAwait(false);
                last = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
                await programs.WaitForResultAsync(last).ConfigureAwait(false);
                Assert.That(programs.Results.Count, Is.EqualTo(1));
                Assert.That(await programs.RemoveTemplateAsync(LadsIncubator.LongTemplate).ConfigureAwait(false),
                    Is.True);
            }
            await using (LadsHostedServer second = await LadsHostedServer.StartIncubatorAsync(
                nameof(ExplicitTemplateDeletionAndResultRetentionSurviveRestartAsync) + "Restart",
                options: options => options.Store = store).ConfigureAwait(false))
            {
                ILadsProgramManager programs = (await second.DeviceAsync().ConfigureAwait(false))
                    .GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
                Assert.That(programs.Templates.ToList().Select(template => template.Id),
                    Does.Not.Contain(LadsIncubator.LongTemplate));
                Assert.That(programs.Results.Count, Is.EqualTo(1));
                Assert.That(programs.Results[0].RunId, Is.EqualTo(last));
                Assert.That(await programs.RemoveResultAsync(last).ConfigureAwait(false), Is.True);
            }
            await using LadsHostedServer third = await LadsHostedServer.StartIncubatorAsync(
                nameof(ExplicitTemplateDeletionAndResultRetentionSurviveRestartAsync) + "Deleted",
                options: options => options.Store = store).ConfigureAwait(false);
            ILadsProgramManager empty = (await third.DeviceAsync().ConfigureAwait(false))
                .GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            Assert.That(empty.Results.Count, Is.Zero);
        }

        [Test]
        public async Task CompletionWriteFailureDoesNotPublishResultOrIncrementCyclesAsync()
        {
            var store = new RejectResultsStore();
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(CompletionWriteFailureDoesNotPublishResultOrIncrementCyclesAsync),
                options: options => options.Store = store).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;
            string id = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await Assert.ThrowsAsync<IOException>(async () =>
                await programs.WaitForResultAsync(id).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(programs.Results.Count, Is.Zero);
            Assert.That(device.OperationCycleCounter, Is.Zero);
            Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
            NodeId set = await server.ChildAsync(unit.NodeId, "ProgramManager", "ResultSet").ConfigureAwait(false);
            Assert.That(await server.ChildNamesAsync(set).ConfigureAwait(false), Does.Not.Contain(id));
        }

        [Test]
        public async Task ConfiguredTemplatesWinAndExecutingMaintenanceReturnsToPlannedAsync()
        {
            const string identity = "urn:opcua-netstandard:lads:instances#" + LadsIncubator.DeviceName;
            var store = new InMemoryLadsStore();
            DateTimeUtc created = (DateTimeUtc)new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            await store.SaveAsync(identity, new LadsStoredDeviceData
            {
                Identity = identity,
                FunctionalUnits = new LadsStoredFunctionalUnitData[]
                {
                    new()
                    {
                        Name = LadsIncubator.UnitName,
                        Templates = new LadsStoredTemplateData[]
                        {
                            new()
                            {
                                Id = LadsIncubator.QuickTemplate,
                                Author = "Stored author",
                                Created = created,
                                Modified = created,
                                Content = new LadsProgramTemplate(LadsIncubator.QuickTemplate)
                                    .Step("Replaced", TimeSpan.FromHours(1)).ToContent()
                            },
                            new()
                            {
                                Id = "Retained",
                                Author = "Retained author",
                                Created = created,
                                Modified = created,
                                Content = new LadsProgramTemplate("Retained").Step("Retained step", TimeSpan.Zero)
                                    .ToContent()
                            }
                        }
                    }
                },
                MaintenanceTasks = new LadsStoredMaintenanceData[]
                {
                    new()
                    {
                        Name = LadsIncubator.MaintenanceName,
                        State = LadsMaintenanceStateValue.Executing,
                        LastExecutionDate = created,
                        LastResult = MaintenanceTaskResultEnum.Success
                    }
                }
            }).ConfigureAwait(false);
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ConfiguredTemplatesWinAndExecutingMaintenanceReturnsToPlannedAsync),
                options: options => options.Store = store).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ArrayOf<LadsProgramTemplate> templates = device.GetFunctionalUnit(LadsIncubator.UnitName)
                .Programs!.Templates;
            LadsProgramTemplate configured = templates.ToList()
                .Single(template => template.Id == LadsIncubator.QuickTemplate);
            Assert.That(configured.Steps.Count, Is.EqualTo(2));
            Assert.That(configured.Author, Is.Empty);
            Assert.That(configured.Created, Is.EqualTo(created));
            LadsProgramTemplate retained = templates.ToList().Single(template => template.Id == "Retained");
            Assert.That(retained.Author, Is.EqualTo("Retained author"));
            Assert.That(retained.Created, Is.EqualTo(created));
            ILadsMaintenanceTask task = device.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            Assert.That(task.State, Is.EqualTo(LadsMaintenanceStateValue.Planned));
            Assert.That(task.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Success));
            Assert.That(task.LastExecutionDate, Is.EqualTo(created));
        }

        [Test]
        public async Task CompletedSnapshotDoesNotFollowLateExecutorMutationsAsync()
        {
            var store = new InMemoryLadsStore();
            var executor = new CapturingExecutor();
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(CompletedSnapshotDoesNotFollowLateExecutorMutationsAsync),
                builder => builder.Services.AddSingleton<ILadsProgramExecutor>(executor),
                options => options.Store = store).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager programs = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            string id = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(id).ConfigureAwait(false);
            Assert.That(result.Files[0].Content.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            executor.Run!.Template.Author = "Late mutation";
            LadsAssert.ThrowsStatus(StatusCodes.BadInvalidState,
                () => executor.Run.SetResultVariable("Late", "Mutation"));
            await device.SaveStoredDataAsync().ConfigureAwait(false);
            LadsStoredDeviceData? snapshot = await store.LoadAsync(
                "urn:opcua-netstandard:lads:instances#" + LadsIncubator.DeviceName).ConfigureAwait(false);
            Assert.That(snapshot, Is.Not.Null);
            LadsStoredResultData saved = snapshot!.FunctionalUnits[0].Results[0];
            Assert.That(saved.RunId, Is.EqualTo(id));
            Assert.That(saved.Template.Author, Is.Empty);
            Assert.That(saved.Files[0].Content.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(saved.Variables.ToList().Select(variable => variable.Key), Does.Not.Contain("Late"));
        }

        [Test]
        public async Task FailedStartCheckpointDoesNotInvokeExecutorOrPublishAnActiveRunAsync()
        {
            var store = new RejectResultsStore();
            var executor = new CountingExecutor { CompleteImmediately = true };
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(FailedStartCheckpointDoesNotInvokeExecutorOrPublishAnActiveRunAsync),
                builder => builder.Services.AddSingleton<ILadsProgramExecutor>(executor),
                options: options => options.Store = store).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            store.RejectAllSaves = true;
            try
            {
                await Assert.ThrowsAsync<IOException>(async () =>
                    await unit.Programs!.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(executor.Executions, Is.Zero);
                Assert.That(unit.Programs!.ActiveRun, Is.Null);
                Assert.That(unit.Programs.Results.Count, Is.Zero);
                Assert.That(unit.State, Is.EqualTo(LadsFunctionalStateValue.Aborted));
                Assert.That(device.OperationCycleCounter, Is.Zero);
                DataValue activeId = await server.ReadAsync(await server.ChildAsync(
                    unit.NodeId, "ProgramManager", "ActiveProgram", "DeviceProgramRunId").ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(activeId.WrappedValue.TryGetValue(out string value), Is.True);
                Assert.That(value, Is.Empty);
            }
            finally
            {
                store.RejectAllSaves = false;
            }
            await device.SaveStoredDataAsync().ConfigureAwait(false);
            LadsStoredDeviceData? saved = await store.LoadAsync(
                "urn:opcua-netstandard:lads:instances#" + LadsIncubator.DeviceName).ConfigureAwait(false);
            Assert.That(saved!.FunctionalUnits[0].InterruptedRunId, Is.Null);
        }

        [Test]
        public async Task FailedStoreMutationsPreserveTemplatesResultsAndFileBindingsAsync()
        {
            var store = new RejectResultsStore { RejectResults = false };
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(FailedStoreMutationsPreserveTemplatesResultsAndFileBindingsAsync),
                options: options => options.Store = store).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;
            string id = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult result = await programs.WaitForResultAsync(id).ConfigureAwait(false);
            string[] templates = programs.Templates.ToList().Select(template => template.Id).ToArray();
            store.RejectAllSaves = true;
            try
            {
                await Assert.ThrowsAsync<IOException>(async () =>
                    await programs.AddTemplateAsync(new LadsProgramTemplate("Rejected")
                        .Step("Step", TimeSpan.Zero)).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(programs.Templates.ToList().Select(template => template.Id),
                    Is.EquivalentTo(templates));
                await Assert.ThrowsAsync<IOException>(async () =>
                    await programs.RemoveTemplateAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(programs.Templates.ToList().Select(template => template.Id),
                    Is.EquivalentTo(templates));
                await Assert.ThrowsAsync<IOException>(async () =>
                    await programs.RemoveResultAsync(id).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(programs.Results.Count, Is.EqualTo(1));
                Assert.That(programs.Results[0], Is.SameAs(result));
                NodeId results = await server.ChildAsync(unit.NodeId, "ProgramManager", "ResultSet")
                    .ConfigureAwait(false);
                Assert.That(await server.ChildNamesAsync(results).ConfigureAwait(false), Does.Contain(id));
                NodeId fileSet = await server.ChildAsync(result.NodeId, "FileSet").ConfigureAwait(false);
                Assert.That(await server.ChildNamesAsync(fileSet).ConfigureAwait(false),
                    Does.Contain(result.Files[0].Name));
            }
            finally
            {
                store.RejectAllSaves = false;
            }
            await device.SaveStoredDataAsync().ConfigureAwait(false);
            LadsStoredDeviceData? saved = await store.LoadAsync(
                "urn:opcua-netstandard:lads:instances#" + LadsIncubator.DeviceName).ConfigureAwait(false);
            Assert.That(saved!.FunctionalUnits[0].Templates.ToList().Select(template => template.Id),
                Is.EquivalentTo(templates));
            Assert.That(saved.FunctionalUnits[0].Results[0].RunId, Is.EqualTo(id));
            Assert.That(saved.FunctionalUnits[0].RemovedTemplateIds.Count, Is.Zero);
        }

        [TestCase("add")]
        [TestCase("remove-template")]
        [TestCase("remove-result")]
        public async Task ProgramDataMutationsRemainUnpublishedWhileAnotherCheckpointHoldsTheStoreAsync(
            string operation)
        {
            var store = new RejectResultsStore { RejectResults = false };
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(ProgramDataMutationsRemainUnpublishedWhileAnotherCheckpointHoldsTheStoreAsync) + operation,
                options: options => options.Store = store).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager programs = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            string id = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await programs.WaitForResultAsync(id).ConfigureAwait(false);
            store.PauseNextSave = true;
            Task checkpoint = device.SaveStoredDataAsync().AsTask();
            await store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Task mutation = operation switch
            {
                "add" => programs.AddTemplateAsync(new LadsProgramTemplate("Pending")
                    .Step("Step", TimeSpan.Zero)).AsTask(),
                "remove-template" => programs.RemoveTemplateAsync(LadsIncubator.QuickTemplate).AsTask(),
                _ => programs.RemoveResultAsync(id).AsTask()
            };
            try
            {
                Assert.That(mutation.IsCompleted, Is.False);
                string[] templates = programs.Templates.ToList().Select(template => template.Id).ToArray();
                Assert.That(templates, Does.Contain(LadsIncubator.QuickTemplate));
                Assert.That(templates, Does.Not.Contain("Pending"));
                Assert.That(programs.Results.Count, Is.EqualTo(1));
                Assert.That(programs.Results[0].RunId, Is.EqualTo(id));
            }
            finally
            {
                store.ReleaseSave.TrySetResult(true);
                await Task.WhenAll(checkpoint, mutation).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            LadsStoredDeviceData? saved = await store.LoadAsync(
                "urn:opcua-netstandard:lads:instances#" + LadsIncubator.DeviceName).ConfigureAwait(false);
            LadsStoredFunctionalUnitData stored = saved!.FunctionalUnits[0];
            string[] committed = stored.Templates.ToList().Select(template => template.Id).ToArray();
            Assert.That(committed.Contains("Pending"), Is.EqualTo(operation == "add"));
            Assert.That(committed.Contains(LadsIncubator.QuickTemplate),
                Is.EqualTo(operation != "remove-template"));
            Assert.That(stored.Results.Count, Is.EqualTo(operation == "remove-result" ? 0 : 1));
            Assert.That(stored.RemovedTemplateIds.ToList().Contains(LadsIncubator.QuickTemplate),
                Is.EqualTo(operation == "remove-template"));
        }

        [Test]
        public async Task StoredResultTimingIsRestoredWithoutRecomputingFromUtcAsync()
        {
            const string identity = "urn:opcua-netstandard:lads:instances#" + LadsIncubator.DeviceName;
            var store = new InMemoryLadsStore();
            await store.SaveAsync(identity, new LadsStoredDeviceData
            {
                Identity = identity,
                FunctionalUnits = new LadsStoredFunctionalUnitData[]
                {
                    new()
                    {
                        Name = LadsIncubator.UnitName,
                        Results = new LadsStoredResultData[]
                        {
                            new()
                            {
                                RunId = "StoredTiming",
                                Template = new LadsStoredTemplateData
                                {
                                    Id = "Retained",
                                    Content = new LadsProgramTemplate("Retained").Step("Step", TimeSpan.Zero)
                                        .ToContent()
                                },
                                Started = new DateTimeUtc(2024, 1, 2, 0, 0, 0),
                                Stopped = new DateTimeUtc(2024, 1, 2, 0, 0, 10),
                                TotalRuntimeMilliseconds = 7500.5,
                                TotalPauseTimeMilliseconds = 1250.25
                            }
                        }
                    }
                }
            }).ConfigureAwait(false);
            await using LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(
                nameof(StoredResultTimingIsRestoredWithoutRecomputingFromUtcAsync),
                options: options => options.Store = store).ConfigureAwait(false);
            ILadsDevice device = await server.DeviceAsync().ConfigureAwait(false);
            ILadsResult result = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!.Results[0];
            DataValue runtime = await server.ReadAsync(
                await server.ChildAsync(result.NodeId, "TotalRuntime").ConfigureAwait(false)).ConfigureAwait(false);
            DataValue pause = await server.ReadAsync(
                await server.ChildAsync(result.NodeId, "TotalPauseTime").ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(runtime.WrappedValue.TryGetValue(out double runtimeMilliseconds), Is.True);
            Assert.That(runtimeMilliseconds, Is.EqualTo(7500.5));
            Assert.That(pause.WrappedValue.TryGetValue(out double pauseMilliseconds), Is.True);
            Assert.That(pauseMilliseconds, Is.EqualTo(1250.25));
            await device.SaveStoredDataAsync().ConfigureAwait(false);
            LadsStoredDeviceData? saved = await store.LoadAsync(identity).ConfigureAwait(false);
            Assert.That(saved!.FunctionalUnits[0].Results[0].TotalRuntimeMilliseconds, Is.EqualTo(7500.5));
            Assert.That(saved.FunctionalUnits[0].Results[0].TotalPauseTimeMilliseconds, Is.EqualTo(1250.25));
        }

        [Test]
        public async Task MachineryOwnerNamespaceRemainsTheStorageIdentityWhenIndexesChangeAsync()
        {
            const string ownerUri = "urn:tests:lads:machinery-owner";
            var store = new InMemoryLadsStore();
            ushort firstIndex;
            string runId;
            await using (LadsHostedServer first = await LadsHostedServer.StartAsync(
                nameof(MachineryOwnerNamespaceRemainsTheStorageIdentityWhenIndexesChangeAsync),
                builder => builder.AddMachinery(options =>
                {
                    options.Parts = MachineryParts.BuildingBlocks;
                    options.InstanceNamespaceUri = ownerUri;
                }).AddLadsToMachinery(options => options.Store = store)
                    .ConfigureLads(LadsIncubator.Configure)).ConfigureAwait(false))
            {
                ILadsDevice device = await first.DeviceAsync().ConfigureAwait(false);
                firstIndex = device.NodeId.NamespaceIndex;
                ILadsProgramManager programs = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
                runId = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
                await programs.WaitForResultAsync(runId).ConfigureAwait(false);
                Assert.That(await store.LoadAsync(ownerUri + "#" + LadsIncubator.DeviceName).ConfigureAwait(false),
                    Is.Not.Null);
            }
            await using LadsHostedServer second = await LadsHostedServer.StartAsync(
                nameof(MachineryOwnerNamespaceRemainsTheStorageIdentityWhenIndexesChangeAsync) + "Restart",
                builder =>
                {
                    builder.AddMachinery(options =>
                    {
                        options.Parts = MachineryParts.BuildingBlocks;
                        options.InstanceNamespaceUri = ownerUri;
                    }).AddLadsToMachinery(options => options.Store = store)
                        .ConfigureLads(LadsIncubator.Configure);
                    builder.Services.AddSingleton<IMachineryModelProvider>(new ExtraNamespaceProvider());
                }).ConfigureAwait(false);
            ILadsDevice restored = await second.DeviceAsync().ConfigureAwait(false);
            Assert.That(restored.NodeId.NamespaceIndex, Is.Not.EqualTo(firstIndex));
            Assert.That(second.Server.NamespaceUris.GetString(restored.NodeId.NamespaceIndex), Is.EqualTo(ownerUri));
            Assert.That(restored.GetFunctionalUnit(LadsIncubator.UnitName).Programs!.Results[0].RunId,
                Is.EqualTo(runId));
            Assert.That(restored.OperationCycleCounter, Is.EqualTo(1));
        }

        /// <summary>
        /// Advances the monotonic clock on each observation, including zero-duration programs.
        /// </summary>
        private sealed class SteppingClock : TimeProvider
        {
            /// <inheritdoc/>
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            /// <inheritdoc/>
            public override long GetTimestamp()
            {
                return Interlocked.Add(ref m_timestamp, TimeSpan.TicksPerMillisecond);
            }

            /// <summary>The deterministic monotonic tick count.</summary>
            private long m_timestamp;
        }

        /// <summary>
        /// Adds a namespace ahead of the owner's instance namespace without adding equipment.
        /// </summary>
        private sealed class ExtraNamespaceProvider : IMachineryModelProvider
        {
            /// <inheritdoc/>
            public int Order => 200;

            /// <inheritdoc/>
            public ArrayOf<string> NamespaceUris => ["urn:tests:lads:extra-model"];

            /// <inheritdoc/>
            public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
            {
            }
        }

        /// <summary>
        /// Retains an executor handle to verify that completed storage no longer follows it.
        /// </summary>
        private sealed class CapturingExecutor : ILadsProgramExecutor
        {
            /// <summary>
            /// Gets the last run supplied to the executor.
            /// </summary>
            public LadsProgramRun? Run { get; private set; }

            /// <inheritdoc/>
            public ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Run = run;
                byte[] source = [1, 2, 3];
                run.AddResultFile("Data.bin", "application/octet-stream", ByteString.From(source));
                source[0] = 9;
                return default;
            }
        }

        /// <summary>
        /// Counts physical executor calls independently from server startup.
        /// </summary>
        private sealed class CountingExecutor : ILadsProgramExecutor
        {
            /// <summary>
            /// Gets the number of execution requests.
            /// </summary>
            public int Executions { get; private set; }

            /// <summary>
            /// Gets or sets whether newly requested programs finish without physical work.
            /// </summary>
            public bool CompleteImmediately { get; set; }

            /// <inheritdoc/>
            public async ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                Executions++;
                if (!CompleteImmediately)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Rejects completion snapshots while retaining the previously committed interrupted marker.
        /// </summary>
        private sealed class RejectResultsStore : ILadsStore
        {
            /// <summary>
            /// Gets or sets whether completion snapshots are rejected.
            /// </summary>
            public bool RejectResults { get; set; } = true;

            /// <summary>
            /// Gets or sets whether every checkpoint is rejected.
            /// </summary>
            public bool RejectAllSaves { get; set; }

            /// <summary>
            /// Gets or sets whether the next save waits for explicit release.
            /// </summary>
            public bool PauseNextSave { get; set; }

            /// <summary>
            /// Gets the notification that the paused checkpoint owns the device-store gate.
            /// </summary>
            public TaskCompletionSource<bool> SaveEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the signal that releases a paused checkpoint.
            /// </summary>
            public TaskCompletionSource<bool> ReleaseSave { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public ValueTask<LadsStoredDeviceData?> LoadAsync(
                string deviceIdentity, CancellationToken cancellationToken = default)
            {
                return m_store.LoadAsync(deviceIdentity, cancellationToken);
            }

            /// <inheritdoc/>
            public async ValueTask SaveAsync(
                string deviceIdentity, LadsStoredDeviceData data, CancellationToken cancellationToken = default)
            {
                if (PauseNextSave)
                {
                    PauseNextSave = false;
                    SaveEntered.TrySetResult(true);
                    await ReleaseSave.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                if (RejectAllSaves)
                {
                    throw new IOException("Storage is unavailable.");
                }
                for (int index = 0; index < data.FunctionalUnits.Count; index++)
                {
                    if (RejectResults && data.FunctionalUnits[index].Results.Count > 0)
                    {
                        throw new IOException("Completion storage is unavailable.");
                    }
                }
                await m_store.SaveAsync(deviceIdentity, data, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public ValueTask DeleteAsync(string deviceIdentity, CancellationToken cancellationToken = default)
            {
                return m_store.DeleteAsync(deviceIdentity, cancellationToken);
            }

            private readonly InMemoryLadsStore m_store = new();
        }
    }
}
