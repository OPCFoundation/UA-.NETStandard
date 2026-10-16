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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Verifies native geometry, calibration and monitoring as one coherent provider observation.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GmsCalibrationTests
    {
        /// <summary>
        /// Actual workspace structures and calibration fields survive a rejected inherited or native update.
        /// </summary>
        [Test]
        public async Task WorkspaceLoadingAndCalibrationCommitTogetherAndRejectInvalidGeometryAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSystemSnapshot initial = Example();
            var provider = new InMemoryGmsProvider(initial);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "CalibratedGms", provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.Identification | GmsFeatures.Sensors | GmsFeatures.Loading |
                        GmsFeatures.ToolMonitoring | GmsFeatures.Calibration
                })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            Assert.That(handle.State.Identification!.Workspace, Is.Not.Null);
            Assert.That(handle.State.Monitoring!.LoadingMonitoring, Is.TypeOf<LoadingMonitoringState>());
            Assert.That(handle.State.Monitoring.ToolMonitoring, Is.TypeOf<ToolMonitoringState>());
            var calibration = handle.State.Notification!.Prognoses!.FindChild(context.Context,
                new QualifiedName("Calibration", (ushort)context.Context.NamespaceUris.GetIndex(GmsModel.NamespaceUri)))
                as CalibrationPrognosisState;
            Assert.That(calibration, Is.Not.Null);
            Assert.That(calibration!.Calibrated!.Value, Is.True);
            Assert.That(handle.TryGetSensor("Probe", out BaseToolState? sensor), Is.True);
            Assert.That(handle.State.Monitoring.ToolMonitoring!.ActiveTool!.Value,
                Is.EqualTo((ArrayOf<NodeId>)[sensor!.NodeId]));
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            DataValue geometry = await session.ReadValueAsync(handle.State.Identification.Workspace!.NodeId)
                .ConfigureAwait(false);
            Assert.That(geometry.WrappedValue.TryGetStructure<CartesianWorkspaceType>(out CartesianWorkspaceType? box),
                Is.True);
            Assert.That(box!.Height, Is.EqualTo(80.0));
            Assert.That(calibration.CalibrationInterval!.Value, Is.EqualTo(31_536_000_000.0));
            Assert.That(calibration.CalibrationCertificate!.Value,
                Is.EqualTo((ArrayOf<string>)["https://metrology.example/certificates/2026-001"]));
            await provider.SetSnapshotAsync(initial with
            {
                Identification = initial.Identification! with
                {
                    Workspace = GmsEquipmentTests.Sample(Variant.FromStructure(
                        new CylindricalWorkspaceType { Length = 50, Radius = -1 }))
                },
                Loading = initial.Loading! with { LoadStatus = GmsEquipmentTests.Sample(Variant.From(1u)) }
            }).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () => await handle.RefreshAsync().ConfigureAwait(false));
            Assert.That(handle.Observation, Is.SameAs(initial));
            Assert.That(handle.State.Monitoring.LoadingMonitoring!.LoadStatus!.Value.TryGetValue(out uint load),
                Is.True);
            Assert.That(load, Is.EqualTo(2u));
            await provider.SetSnapshotAsync(initial with
            {
                MachineTool = initial.MachineTool with
                {
                    Operation = new MachineToolOperation { Mode = (MachineOperationMode)99 }
                },
                Calibration = initial.Calibration! with { Calibrated = GmsEquipmentTests.Sample(Variant.From(false)) }
            }).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () => await handle.RefreshAsync().ConfigureAwait(false));
            Assert.That(calibration.Calibrated.Value, Is.True);
            await provider.SetSnapshotAsync(initial with
            {
                Identification = initial.Identification! with
                {
                    Workspace = GmsEquipmentTests.Sample(Variant.FromStructure(
                        new CylindricalWorkspaceType { Length = 50, Radius = 25 }))
                },
                Calibration = initial.Calibration! with { Calibrated = GmsEquipmentTests.Sample(Variant.From(false)) }
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            Assert.That(calibration.Calibrated.Value, Is.False);
            DataValue changed = await session.ReadValueAsync(handle.State.Identification.Workspace.NodeId)
                .ConfigureAwait(false);
            Assert.That(changed.WrappedValue.TryGetStructure<CylindricalWorkspaceType>(
                out CylindricalWorkspaceType? cylinder), Is.True);
            Assert.That(cylinder!.Radius, Is.EqualTo(25));
        }

        /// <summary>
        /// Native calibration coexists with all ten shared forecasts and their actual prospective GMS job/part targets.
        /// </summary>
        [Test]
        public async Task CalibrationComposesAllGenericForecastsAndNativeProductionReferencesAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSystemSnapshot initial = Example();
            GmsSystemSnapshot production = GmsSystemProductionTests.Example();
            var entries = new MachineToolPrognosis[10];
            var kinds = new MachineToolPrognosisKind[10];
            for (int i = 0; i < entries.Length; i++)
            {
                MachineToolPrognosisKind kind = (MachineToolPrognosisKind)i;
                kinds[i] = kind;
                var value = new MachineToolPrognosis
                {
                    Key = kind.ToString(), Kind = kind, PredictedTime = initial.Calibration!.PredictedTime,
                    Sample = new MachineToolSampleInfo(GmsEquipmentTests.AcquisitionTime, StatusCodes.Good)
                };
                entries[i] = kind switch
                {
                    MachineToolPrognosisKind.Maintenance or MachineToolPrognosisKind.ManualActivity =>
                        value with { Activity = new LocalizedText("Inspect") },
                    MachineToolPrognosisKind.ProcessChangeover => value with
                    {
                        Activity = new LocalizedText("Replace fixture"),
                        Location = new LocalizedText("Measurement area")
                    },
                    MachineToolPrognosisKind.PartLoad or MachineToolPrognosisKind.PartUnload => value with
                    {
                        PartIdentifier = "P-1", PartName = "Workpiece",
                        Location = new LocalizedText("Measurement area"),
                        Target = new MachineToolPrognosisTarget
                        {
                            JobKey = "JobSlot", PartSetKey = "Set", PartKey = "Part"
                        }
                    },
                    MachineToolPrognosisKind.ProductionJobEnd => value with
                    {
                        SourceIdentifier = "J-1", Target = new MachineToolPrognosisTarget { JobKey = "JobSlot" }
                    },
                    MachineToolPrognosisKind.UtilityChange => value with { UtilityName = "Compressed air" },
                    _ => value with { Location = new LocalizedText("Magazine"), ToolName = "Replacement probe" }
                };
            }
            initial = initial with
            {
                MachineTool = production.MachineTool with
                {
                    Prognoses = new MachineToolPrognosisObservation { Entries = entries.ToArrayOf() }
                },
                Jobs = production.Jobs, Parts = production.Parts
            };
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "SharedForecasts",
                new InMemoryGmsProvider(initial))
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.Identification | GmsFeatures.Sensors | GmsFeatures.Loading |
                        GmsFeatures.ToolMonitoring | GmsFeatures.Calibration | GmsFeatures.Production
                }).WithMachineToolFeatures(MachineToolFeatures.ProductionStatistics)
                .WithPrognoses(new MachineToolPrognosisOptions
                {
                    Features = MachineToolFeatures.Prognoses | MachineToolFeatures.DynamicPrognoses,
                    SupportedKinds = kinds.ToArrayOf()
                }).WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            var nodes = new List<BaseInstanceState>();
            handle.State.Notification!.Prognoses!.GetChildren(context.Context, nodes);
            Assert.That(nodes.FindAll(value => value is PrognosisState), Has.Count.EqualTo(11));
            var job = (GMSJobState)handle.State.Production!.ProductionPlan!.FindChild(context.Context,
                new QualifiedName("JobSlot", context.InstanceNamespaceIndex))!;
            var set = (ProductionPartSetState)job.PartSets!.FindChild(context.Context,
                new QualifiedName("Set", context.InstanceNamespaceIndex))!;
            var part = (GMSPartState)set.PartsPerRun!.FindChild(context.Context,
                new QualifiedName("Part", context.InstanceNamespaceIndex))!;
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            await using var inherited = new MachineToolClient(session, NUnitTelemetryContext.Create());
            MachineToolPrognosisReadSnapshot read = await inherited.ReadPrognosesAsync(handle.NodeId)
                .ConfigureAwait(false);
            Assert.That(read.Entries.Count, Is.EqualTo(11));
            MachineToolPrognosisReadEntry loaded = read.Entries[2];
            Assert.That(loaded.PartNodeId.WrappedValue.TryGetValue(out NodeId partReference), Is.True);
            Assert.That(partReference, Is.EqualTo(part.NodeId));
            Assert.That(read.Entries[5].JobNodeId.WrappedValue.TryGetValue(out NodeId jobReference), Is.True);
            Assert.That(jobReference, Is.EqualTo(job.NodeId));
            await using var gms = new GmsClient(session, NUnitTelemetryContext.Create());
            Assert.That(await gms.ReadCalibrationAsync(handle.NodeId).ConfigureAwait(false), Is.Not.Null);
            Assert.That(handle.ServerProfiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/GMS/Server/Prognoses"));
        }

        /// <summary>
        /// Supplies actual equipment-reported geometry, qualification and calibration metadata.
        /// </summary>
        internal static GmsSystemSnapshot Example()
        {
            return new GmsSystemSnapshot
            {
                Sensors = [GmsEquipmentTests.Sensor("Probe")],
                Identification = new GmsIdentificationObservation
                {
                    SubDeviceClass = GmsEquipmentTests.Sample(Variant.From("CoordinateMeasuringMachine")),
                    Workspace = GmsEquipmentTests.Sample(Variant.FromStructure(
                        new CartesianWorkspaceType { Length = 150, Width = 100, Height = 80 }))
                },
                Loading = new GmsLoadingObservation
                {
                    LoadStatus = GmsEquipmentTests.Sample(Variant.From(2u)),
                    IsInLoadingPosition = GmsEquipmentTests.Sample(Variant.From(false))
                },
                ToolMonitoring = new GmsToolMonitoringObservation
                {
                    ActiveSensorKeys = ["Probe"],
                    Sample = new MachineToolSampleInfo(GmsEquipmentTests.AcquisitionTime, StatusCodes.Good)
                },
                Calibration = new GmsCalibrationObservation
                {
                    Calibrated = GmsEquipmentTests.Sample(Variant.From(true)),
                    DateOfCalibration = GmsEquipmentTests.Sample(Variant.From(
                        new DateTimeUtc(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)))),
                    PredictedTime = GmsEquipmentTests.Sample(Variant.From(
                        new DateTimeUtc(new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc)))),
                    CalibrationInterval = GmsEquipmentTests.Sample(Variant.From(31_536_000_000.0)),
                    CalibrationPreptime = GmsEquipmentTests.Sample(Variant.From(86_400_000.0)),
                    CalibrationCertificate = GmsEquipmentTests.Sample(Variant.From(
                        (ArrayOf<string>)["https://metrology.example/certificates/2026-001"]))
                }
            };
        }
    }
}
