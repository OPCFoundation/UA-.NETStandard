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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.LADS.Client;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;
using LadsDevice = Opc.Ua.LADS.Client.LadsDevice;
using LadsFunction = Opc.Ua.LADS.Client.LadsFunction;
using LadsFunctionalUnit = Opc.Ua.LADS.Client.LadsFunctionalUnit;
using LadsMaintenanceTask = Opc.Ua.LADS.Client.LadsMaintenanceTask;
using LadsProgramTemplate = Opc.Ua.LADS.Client.LadsProgramTemplate;
using LadsResult = Opc.Ua.LADS.Client.LadsResult;
using LadsResultFile = Opc.Ua.LADS.Client.LadsResultFile;

namespace Opc.Ua.LADS.Tests.Integration
{
    /// <summary>
    /// End-to-end tests of the LADS client against a LADS server over a real session.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class LadsClientServerTests
    {

        [Test]
        public async Task ClientDiscoversDeviceAndReadsSnapshotAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(ClientDiscoversDeviceAndReadsSnapshotAsync)).ConfigureAwait(false);
            LadsClient client = connection.Client;

            Assert.That(client.IsSupported, Is.True);
            ArrayOf<LadsDevice> devices = await client.GetDevicesAsync().ConfigureAwait(false);
            Assert.That(devices.Count, Is.EqualTo(1));
            LadsDevice device = await client.GetFirstDeviceAsync().ConfigureAwait(false);
            Assert.That(device.Name, Is.EqualTo(LadsIncubator.DeviceName));
            Assert.That(await client.FindDeviceAsync("Missing").ConfigureAwait(false), Is.Null);

            LadsDeviceSnapshot snapshot = await device.ReadAsync().ConfigureAwait(false);
            Assert.That(snapshot.Identification.Manufacturer, Is.EqualTo("OPC Foundation"));
            Assert.That(snapshot.Identification.Model, Is.EqualTo("Shaking incubator"));
            Assert.That(snapshot.Identification.SerialNumber, Is.EqualTo("SN-0001"));
            Assert.That(snapshot.FunctionalUnitNames.ToList(), Does.Contain(LadsIncubator.UnitName));
            Assert.That(snapshot.DeviceState, Is.Not.Null);
            Assert.That(snapshot.MachineryItemState, Is.Not.Null);

            LadsFunctionalUnit unit = await device.GetFunctionalUnitAsync(LadsIncubator.UnitName)
                .ConfigureAwait(false);
            LadsFunctionalUnitSnapshot unitSnapshot = await unit.ReadAsync().ConfigureAwait(false);
            Assert.That(unitSnapshot.HasProgramManager, Is.True);
            Assert.That(unitSnapshot.State!.CurrentState, Is.EqualTo("Stopped"));
            Assert.That(unitSnapshot.ActiveProgram, Is.Not.Null);
            string[] names = [.. unitSnapshot.Functions.ToList().Select(f => f.DisplayName)];
            Assert.That(names, Is.EquivalentTo(s_chamberFunctions));

            LadsFunctionSnapshot temperature = unitSnapshot.Functions.ToList()
                .Single(f => f.DisplayName == "Temperature");
            Assert.That(temperature.Kind, Is.EqualTo(LadsFunctionKind.AnalogControl));
            Assert.That(temperature.TargetValue.TryGetValue(out double target), Is.True);
            Assert.That(target, Is.EqualTo(37));
            Assert.That(temperature.CurrentValue.TryGetValue(out double current), Is.True);
            Assert.That(current, Is.EqualTo(22));
            Assert.That(temperature.State, Is.EqualTo("Stopped"));

            LadsFunction co2 = await unit.GetFunctionAsync("CO2").ConfigureAwait(false);
            Assert.That(co2.IsSensor, Is.True);
            LadsFunctionSnapshot co2Snapshot = await co2.ReadAsync().ConfigureAwait(false);
            Assert.That(co2Snapshot.SensorValue.TryGetValue(out double co2Value), Is.True);
            Assert.That(co2Value, Is.EqualTo(5));

            LadsFunction door = await unit.GetFunctionAsync("Door").ConfigureAwait(false);
            Assert.That(door.Kind, Is.EqualTo(LadsFunctionKind.Cover));
            LadsFunction timer = await unit.GetFunctionAsync("Timer").ConfigureAwait(false);
            Assert.That(timer.Kind, Is.EqualTo(LadsFunctionKind.TimerControl));
        }

        [Test]
        public async Task ClientDeduplicatesDeviceComposedIntoMachineryAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ClientDeduplicatesDeviceComposedIntoMachineryAsync),
                builder => builder
                    .AddMachinery(o => o.Parts = MachineryParts.BuildingBlocks)
                    .AddAssetManagement()
                    .AddLadsToMachinery()
                    .ConfigureLads(LadsIncubator.Configure)).ConfigureAwait(false);

            ArrayOf<LadsDevice> devices = await connection.Client.GetDevicesAsync().ConfigureAwait(false);
            Assert.That(devices.Count, Is.EqualTo(1));
            Assert.That(devices[0].Name, Is.EqualTo(LadsIncubator.DeviceName));
            LadsDeviceSnapshot snapshot = await devices[0].ReadAsync().ConfigureAwait(false);
            Assert.That(snapshot.Identification.Manufacturer, Is.EqualTo("OPC Foundation"));
            LadsFunctionalUnit unit = await connection.ChamberAsync().ConfigureAwait(false);
            ArrayOf<LadsFunction> functions = await unit.GetFunctionsAsync().ConfigureAwait(false);
            Assert.That(functions.Count, Is.EqualTo(5));
        }

        [Test]
        public async Task ClientReadsEveryFunctionKindAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ClientReadsEveryFunctionKindAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit("Aux", unit => unit
                        .AnalogControl("Relative", o =>
                        {
                            o.Kind = LadsAnalogControlKind.Relative;
                            o.Range = new Range(100, 0);
                            o.InitialTargetValue = 10;
                        })
                        .AnalogControl("Totalizer", o => o.Kind = LadsAnalogControlKind.Totalizer)
                        .AnalogControl("Composed", o =>
                        {
                            o.Kind = LadsAnalogControlKind.Composed;
                            o.ComposedTargetValues = s_composedNames.ToArrayOf();
                        })
                        .MultiModeAnalogControl("Mode", o => o.Modes = s_modes.ToArrayOf())
                        .TwoStateDiscreteControl("Valve")
                        .MultiStateDiscreteControl("Level", o => o.States = new[]
                        {
                            new LocalizedText("Low"),
                            new LocalizedText("High")
                        }.ToArrayOf())
                        .AnalogArraySensor("Spectrum", o => o.InitialValues = s_spectrum.ToArrayOf())
                        .AnalogScalarSensor("Compensated", o => o.WithCompensation = true)
                        .TwoStateDiscreteSensor("Presence")
                        .MultiStateDiscreteSensor("Phase", o => o.States = new[]
                        {
                            new LocalizedText("A"),
                            new LocalizedText("B")
                        }.ToArrayOf())
                        .MultiSensor("Multi", m => m.AnalogScalarSensor("Inner")))))).ConfigureAwait(false);

            LadsDevice device = await connection.Client.GetDeviceAsync(LadsIncubator.DeviceName)
                .ConfigureAwait(false);
            LadsFunctionalUnit aux = await device.GetFunctionalUnitAsync("Aux").ConfigureAwait(false);
            LadsFunctionalUnitSnapshot snapshot = await aux.ReadAsync().ConfigureAwait(false);
            Dictionary<string, LadsFunctionSnapshot> functions = snapshot.Functions.ToList()
                .ToDictionary(f => f.DisplayName, StringComparer.Ordinal);

            Assert.That(functions["Relative"].Kind, Is.EqualTo(LadsFunctionKind.AnalogControlWithRelativeTargetValue));
            Assert.That(functions["Totalizer"].Kind, Is.EqualTo(LadsFunctionKind.AnalogControlWithTotalizer));
            Assert.That(
                functions["Composed"].Kind,
                Is.EqualTo(LadsFunctionKind.AnalogControlWithComposedTargetValue));
            Assert.That(functions["Mode"].Kind, Is.EqualTo(LadsFunctionKind.MultiModeAnalogControl));
            Assert.That(functions["Valve"].Kind, Is.EqualTo(LadsFunctionKind.TwoStateDiscreteControl));
            Assert.That(functions["Level"].Kind, Is.EqualTo(LadsFunctionKind.MultiStateDiscreteControl));
            Assert.That(functions["Spectrum"].Kind, Is.EqualTo(LadsFunctionKind.AnalogArraySensor));
            Assert.That(
                functions["Compensated"].Kind,
                Is.EqualTo(LadsFunctionKind.AnalogScalarSensorWithCompensation));
            Assert.That(functions["Presence"].Kind, Is.EqualTo(LadsFunctionKind.TwoStateDiscreteSensor));
            Assert.That(functions["Phase"].Kind, Is.EqualTo(LadsFunctionKind.MultiStateDiscreteSensor));
            Assert.That(functions["Multi"].Kind, Is.EqualTo(LadsFunctionKind.MultiSensor));

            Assert.That(functions["Relative"].TargetValue.TryGetValue(out double relative), Is.True);
            Assert.That(relative, Is.EqualTo(10));
            Assert.That(functions["Spectrum"].SensorValue.TryGetValue(out ArrayOf<double> spectrum), Is.True);
            Assert.That(spectrum.ToArray(), Is.EqualTo(s_spectrum));
            Assert.That(functions["Valve"].State, Is.EqualTo("Stopped"));

            LadsFunction multi = await aux.GetFunctionAsync("Multi").ConfigureAwait(false);
            ArrayOf<LadsFunction> inner = await multi.GetFunctionsAsync().ConfigureAwait(false);
            Assert.That(inner.Count, Is.EqualTo(1));
            Assert.That(inner[0].Kind, Is.EqualTo(LadsFunctionKind.AnalogScalarSensor));
        }

        [Test]
        public async Task ClientControlsFunctionsAndCoverAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(ClientControlsFunctionsAndCoverAsync)).ConfigureAwait(false);
            ILadsDevice serverDevice = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit serverUnit = serverDevice.GetFunctionalUnit(LadsIncubator.UnitName);
            IAnalogControlFunction serverTemperature =
                serverUnit.GetFunction<IAnalogControlFunction>("Temperature");
            ICoverFunction serverDoor = serverUnit.GetFunction<ICoverFunction>("Door");
            LadsFunctionalUnit unit = await connection.ChamberAsync().ConfigureAwait(false);

            LadsFunction temperature = await unit.GetFunctionAsync("Temperature").ConfigureAwait(false);
            Assert.That(temperature.IsControl, Is.True);
            await temperature.StartAsync(Variant.From(42.0)).ConfigureAwait(false);
            Assert.That(serverTemperature.TargetValue, Is.EqualTo(42));
            Assert.That(serverTemperature.State, Is.EqualTo(LadsFunctionalStateValue.Running));
            LadsFunctionSnapshot running = await temperature.ReadAsync().ConfigureAwait(false);
            Assert.That(running.State, Is.EqualTo("Running"));
            Assert.That(running.TargetValue.TryGetValue(out double target), Is.True);
            Assert.That(target, Is.EqualTo(42));
            await temperature.StopAsync().ConfigureAwait(false);
            Assert.That(serverTemperature.State, Is.EqualTo(LadsFunctionalStateValue.Stopped));

            LadsFunction door = await unit.GetFunctionAsync("Door").ConfigureAwait(false);
            Assert.That(serverDoor.State, Is.EqualTo(LadsCoverStateValue.Closed));
            await door.OpenAsync().ConfigureAwait(false);
            Assert.That(serverDoor.State, Is.EqualTo(LadsCoverStateValue.Opened));
            Assert.That((await door.ReadAsync().ConfigureAwait(false)).State, Is.EqualTo("Opened"));
            await door.CloseAsync().ConfigureAwait(false);
            Assert.That(serverDoor.State, Is.EqualTo(LadsCoverStateValue.Closed));
            await door.LockAsync().ConfigureAwait(false);
            Assert.That(serverDoor.State, Is.EqualTo(LadsCoverStateValue.Locked));
            Assert.That((await door.ReadAsync().ConfigureAwait(false)).State, Is.EqualTo("Locked"));
            await door.UnlockAsync().ConfigureAwait(false);
            Assert.That(serverDoor.State, Is.EqualTo(LadsCoverStateValue.Closed));
            await door.OpenAsync().ConfigureAwait(false);
            Assert.That(serverDoor.State, Is.EqualTo(LadsCoverStateValue.Opened));
        }

        [Test]
        public async Task ClientObservesFunctionValueChangesAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(ClientObservesFunctionValueChangesAsync)).ConfigureAwait(false);
            ILadsDevice serverDevice = await connection.Server.DeviceAsync().ConfigureAwait(false);
            IAnalogControlFunction serverTemperature = serverDevice
                .GetFunctionalUnit(LadsIncubator.UnitName)
                .GetFunction<IAnalogControlFunction>("Temperature");
            LadsFunctionalUnit unit = await connection.ChamberAsync().ConfigureAwait(false);
            LadsFunction temperature = await unit.GetFunctionAsync("Temperature").ConfigureAwait(false);

            using var cts = new CancellationTokenSource(s_timeout);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool updated = false;
            await foreach (LadsValueChange change in temperature.ObserveAsync(cancellationToken: cts.Token)
                .ConfigureAwait(false))
            {
                Assert.That(change.SourceId, Is.EqualTo(temperature.NodeId));
                Assert.That(LadsFunctionMembers.All.ToList(), Does.Contain(change.Name));
                seen.Add(change.Name);
                if (!updated &&
                    seen.Contains(LadsFunctionMembers.CurrentValue) &&
                    seen.Contains(LadsFunctionMembers.TargetValue))
                {
                    updated = true;
                    serverTemperature.UpdateCurrentValue(30.5);
                    continue;
                }
                if (updated &&
                    change.Name == LadsFunctionMembers.CurrentValue &&
                    change.Value.WrappedValue.TryGetValue(out double value) &&
                    value == 30.5)
                {
                    break;
                }
            }
            Assert.That(cts.IsCancellationRequested, Is.False, "The value change was not observed.");
            Assert.That(seen, Does.Contain(LadsFunctionMembers.ControlFunctionState));
        }

        [Test]
        public async Task ClientRunsProgramAndDownloadsResultFileAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(ClientRunsProgramAndDownloadsResultFileAsync)).ConfigureAwait(false);
            ILadsDevice serverDevice = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager serverPrograms = serverDevice.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            LadsFunctionalUnit unit = await connection.ChamberAsync().ConfigureAwait(false);

            ArrayOf<LadsProgramTemplate> templates = await unit.GetProgramTemplatesAsync().ConfigureAwait(false);
            Assert.That(
                templates.ToList().Select(t => t.Name),
                Is.EquivalentTo(new[] { LadsIncubator.QuickTemplate, LadsIncubator.LongTemplate }));
            LadsProgramTemplate quick = templates.ToList().Single(t => t.Name == LadsIncubator.QuickTemplate);
            Assert.That(quick.Description, Is.EqualTo("A quick incubation."));

            string runId = await unit.StartProgramAsync(LadsProgramRequest
                .For(LadsIncubator.QuickTemplate)
                .WithProperty("Volume", "250")
                .WithSample("Flask-1", "S-1", "A1")
                .WithJob("Job-1")
                .WithTask("Task-1")).ConfigureAwait(false);
            Assert.That(runId, Is.Not.Empty);
            ILadsResult serverResult = await serverPrograms.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(serverResult.Outcome, Is.EqualTo(LadsProgramOutcome.Completed));

            LadsResult? result = await unit.FindResultAsync(runId).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.DeviceProgramRunId, Is.EqualTo(runId));
            Assert.That(result.SupervisoryJobId, Is.EqualTo("Job-1"));
            Assert.That(result.SupervisoryTaskId, Is.EqualTo("Task-1"));
            Assert.That(
                result.Properties.ToList().Any(p => p.Key == "Volume" &&
                    p.Value == "250"),
                Is.True);
            Assert.That(result.Samples.Count, Is.EqualTo(1));
            Assert.That(result.Samples[0].SampleId, Is.EqualTo("S-1"));
            Assert.That(result.Files.Count, Is.EqualTo(1));
            LadsResultFile file = result.Files[0];
            Assert.That(file.CanDownload, Is.True);
            Assert.That(file.MimeType, Is.EqualTo("text/csv"));

            using var stream = new MemoryStream();
            long length = await connection.Client.DownloadResultFileAsync(file, stream).ConfigureAwait(false);
            byte[] expected = serverResult.Files[0].Content.ToArray();
            Assert.That(length, Is.EqualTo(expected.Length));
            Assert.That(stream.ToArray(), Is.EqualTo(expected));
            ByteString content = await connection.Client.ReadResultFileAsync(file).ConfigureAwait(false);
            Assert.That(Encoding.UTF8.GetString(content.ToArray()), Does.Contain("Hold"));

            ArrayOf<LadsResult> results = await unit.GetResultsAsync().ConfigureAwait(false);
            Assert.That(results.Count, Is.EqualTo(1));
            LadsFunction shaker = await unit.GetFunctionAsync("ShakerSpeed").ConfigureAwait(false);
            LadsFunctionSnapshot shakerSnapshot = await shaker.ReadAsync().ConfigureAwait(false);
            Assert.That(shakerSnapshot.TargetValue.TryGetValue(out double speed), Is.True);
            Assert.That(speed, Is.EqualTo(200));

            LadsOperationCounters? counters = await (await connection.Client
                .GetDeviceAsync(LadsIncubator.DeviceName).ConfigureAwait(false))
                .ReadOperationCountersAsync().ConfigureAwait(false);
            Assert.That(counters, Is.Not.Null);
            Assert.That(counters!.NodeId.IsNull, Is.False);
            Assert.That(counters.OperationCycleCounter, Is.EqualTo(1));
            Assert.That(counters.OperationDuration, Is.Not.Null);
        }

        [Test]
        public async Task ClientMonitorsAndStopsActiveProgramAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(ClientMonitorsAndStopsActiveProgramAsync)).ConfigureAwait(false);
            ILadsDevice serverDevice = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager serverPrograms = serverDevice.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            LadsFunctionalUnit unit = await connection.ChamberAsync().ConfigureAwait(false);

            string runId = await unit.StartProgramAsync(LadsIncubator.LongTemplate).ConfigureAwait(false);
            LadsStateSnapshot running = await unit.WaitForStateAsync("Running", s_timeout).ConfigureAwait(false);
            Assert.That(running.CurrentState, Is.EqualTo("Running"));
            LadsActiveProgram? active = await unit.ReadActiveProgramAsync().ConfigureAwait(false);
            Assert.That(active, Is.Not.Null);
            Assert.That(active!.DeviceProgramRunId, Is.EqualTo(runId));
            Assert.That(active.CurrentStepName, Is.EqualTo("Hold"));

            await unit.StopAsync().ConfigureAwait(false);
            ILadsResult stopped = await serverPrograms.WaitForResultAsync(runId).ConfigureAwait(false);
            Assert.That(stopped.Outcome, Is.EqualTo(LadsProgramOutcome.Stopped));
            await unit.WaitForStateAsync("Stopped", s_timeout).ConfigureAwait(false);

            string abortedRun = await unit.StartProgramAsync(LadsIncubator.LongTemplate).ConfigureAwait(false);
            await unit.AbortAsync().ConfigureAwait(false);
            ILadsResult aborted = await serverPrograms.WaitForResultAsync(abortedRun).ConfigureAwait(false);
            Assert.That(aborted.Outcome, Is.EqualTo(LadsProgramOutcome.Aborted));
            Assert.That((await unit.ReadStateAsync().ConfigureAwait(false))!.CurrentState, Is.EqualTo("Aborted"));
            await unit.ClearAsync().ConfigureAwait(false);
            Assert.That((await unit.ReadStateAsync().ConfigureAwait(false))!.CurrentState, Is.EqualTo("Stopped"));

            ArrayOf<LadsResult> results = await unit.GetResultsAsync().ConfigureAwait(false);
            Assert.That(results.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ClientDownloadsUploadsAndRemovesProgramTemplatesAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(ClientDownloadsUploadsAndRemovesProgramTemplatesAsync)).ConfigureAwait(false);
            ILadsDevice serverDevice = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager serverPrograms = serverDevice.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            LadsFunctionalUnit unit = await connection.ChamberAsync().ConfigureAwait(false);

            (ArrayOf<KeyValueType> _, ByteString data) = await unit
                .DownloadProgramTemplateAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            Assert.That(data.IsEmpty, Is.False);
            Assert.That(Encoding.UTF8.GetString(data.ToArray()), Does.Contain("Heat"));

            string id = await unit.UploadProgramTemplateAsync(
                data,
                new[] { new KeyValueType { Key = "TemplateId", Value = "Copy" } }.ToArrayOf())
                .ConfigureAwait(false);
            Assert.That(id, Is.EqualTo("Copy"));
            Assert.That(serverPrograms.Templates.ToList().Any(t => t.Id == "Copy"), Is.True);
            ArrayOf<LadsProgramTemplate> templates = await unit.GetProgramTemplatesAsync().ConfigureAwait(false);
            Assert.That(templates.ToList().Select(t => t.Name), Does.Contain("Copy"));

            await unit.RemoveProgramTemplateAsync("Copy").ConfigureAwait(false);
            Assert.That(serverPrograms.Templates.ToList().Any(t => t.Id == "Copy"), Is.False);

            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotFound,
                () => unit.RemoveProgramTemplateAsync("Copy")).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadNotFound,
                () => unit.DownloadProgramTemplateAsync("Copy")).ConfigureAwait(false);
            await LadsAssert.ThrowsStatusAsync(
                StatusCodes.BadInvalidArgument,
                () => unit.UploadProgramTemplateAsync(ByteString.From(Encoding.UTF8.GetBytes("not a template"))))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ClientRunsMaintenanceTaskAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(ClientRunsMaintenanceTaskAsync)).ConfigureAwait(false);
            ILadsDevice serverDevice = await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsMaintenanceTask serverTask = serverDevice.GetMaintenanceTask(LadsIncubator.MaintenanceName);
            LadsDevice device = await connection.Client.GetDeviceAsync(LadsIncubator.DeviceName)
                .ConfigureAwait(false);

            ArrayOf<LadsMaintenanceTask> tasks = await device.GetMaintenanceTasksAsync().ConfigureAwait(false);
            Assert.That(tasks.Count, Is.EqualTo(1));
            LadsMaintenanceTask task = tasks[0];
            Assert.That(task.Name, Is.EqualTo(LadsIncubator.MaintenanceName));
            Assert.That(task.RecurrencePeriod, Is.EqualTo(TimeSpan.FromDays(30)));
            Assert.That(task.State!.CurrentState, Is.EqualTo("Planned"));

            await connection.Client.StartMaintenanceTaskAsync(task.NodeId).ConfigureAwait(false);
            Assert.That(serverTask.State, Is.EqualTo(LadsMaintenanceStateValue.Executing));
            await connection.Client.StopMaintenanceTaskAsync(
                task.NodeId,
                MaintenanceTaskResultEnum.Success,
                "Calibrated.").ConfigureAwait(false);
            Assert.That(serverTask.State, Is.EqualTo(LadsMaintenanceStateValue.Finished));
            Assert.That(serverTask.LastResult, Is.EqualTo(MaintenanceTaskResultEnum.Success));
            tasks = await device.GetMaintenanceTasksAsync().ConfigureAwait(false);
            Assert.That(tasks[0].State!.CurrentState, Is.EqualTo("Finished"));
            Assert.That(tasks[0].LastExecutionDate.IsNull, Is.False);

            await connection.Client.ResetMaintenanceTaskAsync(task.NodeId).ConfigureAwait(false);
            Assert.That(serverTask.State, Is.EqualTo(LadsMaintenanceStateValue.Planned));
        }

        [Test]
        public async Task ClientReadsAndObservesMultiModeControllerValuesAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ClientReadsAndObservesMultiModeControllerValuesAsync),
                builder => builder.AddLads().ConfigureLads(context => LadsIncubator.Configure(
                    context,
                    LadsIncubator.DeviceName,
                    device => device.FunctionalUnit(
                        "Aux",
                        unit => unit.MultiModeAnalogControl(
                            "Mode",
                            options => options.Modes = s_modes.ToArrayOf()))))).ConfigureAwait(false);
            ILadsDevice serverDevice = await connection.Server.DeviceAsync().ConfigureAwait(false);
            IMultiModeAnalogControlFunction serverMode = serverDevice
                .GetFunctionalUnit("Aux")
                .GetFunction<IMultiModeAnalogControlFunction>("Mode");
            serverMode.SetTargetValue("Heat", 10.5);
            serverMode.UpdateCurrentValue("Heat", 3.25);
            serverMode.SetTargetValue("Cool", 20.25);
            serverMode.UpdateCurrentValue("Cool", 6.75);

            LadsDevice clientDevice = await connection.Client.GetDeviceAsync(LadsIncubator.DeviceName)
                .ConfigureAwait(false);
            LadsFunctionalUnit clientUnit = await clientDevice.GetFunctionalUnitAsync("Aux").ConfigureAwait(false);
            LadsFunction clientMode = await clientUnit.GetFunctionAsync("Mode").ConfigureAwait(false);
            LadsFunctionSnapshot snapshot = await clientMode.ReadAsync().ConfigureAwait(false);
            string[] modeValuePaths =
            [
                "ControllerModeSet/Heat/CurrentValue",
                "ControllerModeSet/Heat/TargetValue",
                "ControllerModeSet/Cool/CurrentValue",
                "ControllerModeSet/Cool/TargetValue"
            ];
            double[] expectedSnapshotValues = [3.25, 10.5, 6.75, 20.25];
            for (int ii = 0; ii < modeValuePaths.Length; ii++)
            {
                Assert.That(snapshot.TryGetValue(modeValuePaths[ii], out DataValue value), Is.True, modeValuePaths[ii]);
                Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True, modeValuePaths[ii]);
                Assert.That(actual, Is.EqualTo(expectedSnapshotValues[ii]), modeValuePaths[ii]);
            }

            using var cts = new CancellationTokenSource(s_timeout);
            var initialNames = new HashSet<string>(StringComparer.Ordinal);
            var changedValues = new Dictionary<string, double>(StringComparer.Ordinal);
            bool valuesChanged = false;
            await foreach (LadsValueChange change in clientMode.ObserveAsync(cancellationToken: cts.Token)
                .ConfigureAwait(false))
            {
                if (!valuesChanged)
                {
                    initialNames.Add(change.Name);
                    if (modeValuePaths.All(initialNames.Contains))
                    {
                        valuesChanged = true;
                        serverMode.UpdateCurrentValue("Cool", 7.25);
                        serverMode.SetTargetValue("Cool", 21.25);
                    }
                    continue;
                }

                if ((change.Name == modeValuePaths[2] || change.Name == modeValuePaths[3]) &&
                    change.Value.WrappedValue.TryGetValue(out double changedValue) &&
                    changedValue == (change.Name == modeValuePaths[2] ? 7.25 : 21.25))
                {
                    changedValues[change.Name] = changedValue;
                    if (changedValues.Count == 2)
                    {
                        break;
                    }
                }
            }

            Assert.That(cts.IsCancellationRequested, Is.False, "The mode-specific value changes were not observed.");
            Assert.That(initialNames, Is.SupersetOf(modeValuePaths));
            Assert.That(changedValues, Is.EquivalentTo(new Dictionary<string, double>
            {
                [modeValuePaths[2]] = 7.25,
                [modeValuePaths[3]] = 21.25
            }));
        }

        [Test]
        public async Task ClientFileDownloadDoesNotSwitchGenerationAfterReconnectAsync()
        {
            await using LadsClientConnection connection = await LadsClientConnection.StartIncubatorAsync(
                nameof(ClientFileDownloadDoesNotSwitchGenerationAfterReconnectAsync)).ConfigureAwait(false);
            LadsFunctionalUnit unit = await connection.ChamberAsync().ConfigureAwait(false);
            string runId = await unit.StartProgramAsync(LadsProgramRequest.For(LadsIncubator.QuickTemplate))
                .ConfigureAwait(false);
            ILadsDevice serverDevice = await connection.Server.DeviceAsync().ConfigureAwait(false);
            await serverDevice.GetFunctionalUnit(LadsIncubator.UnitName).Programs!.WaitForResultAsync(runId)
                .ConfigureAwait(false);
            LadsResult? result = await unit.FindResultAsync(runId).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
            LadsResultFile file = result!.Files[0];
            var client = new LadsClient(
                connection.Session,
                connection.Telemetry,
                new LadsClientOptions { FileChunkSize = 16 });
            using var destination = new ReconnectOnFirstWriteStream(
                () => Opc.Ua.Client.SessionExtensions.ReconnectAsync(connection.Session));

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.DownloadResultFileAsync(file, destination).ConfigureAwait(false)))!;

            Assert.Multiple(() =>
            {
                Assert.That(destination.Reconnected, Is.True);
                Assert.That(destination.Length, Is.EqualTo(16));
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            });
            Assert.That(
                await connection.Client.GetDeviceAsync(LadsIncubator.DeviceName).ConfigureAwait(false),
                Is.Not.Null,
                "The reconnected ManagedSession should remain usable for a fresh LADS request.");
        }

        private sealed class ReconnectOnFirstWriteStream(Func<Task> reconnect) : MemoryStream
        {
            public bool Reconnected { get; private set; }

#if NET6_0_OR_GREATER
            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                await ReconnectOnceAsync().ConfigureAwait(false);
            }
#else
            public override async Task WriteAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                await base.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                await ReconnectOnceAsync().ConfigureAwait(false);
            }
#endif

            private async Task ReconnectOnceAsync()
            {
                if (!Reconnected)
                {
                    await reconnect().ConfigureAwait(false);
                    Reconnected = true;
                }
            }
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);
        private static readonly string[] s_chamberFunctions = ["Temperature", "ShakerSpeed", "CO2", "Door", "Timer"];
        private static readonly string[] s_composedNames = ["A", "B"];
        private static readonly string[] s_modes = ["Heat", "Cool"];
        private static readonly double[] s_spectrum = [1.0, 2.0];    }
}
