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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Validates typed native workflows over real sessions, including namespace mapping and continuation ownership.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GmsSystemClientTests
    {
        /// <summary>
        /// The facade composes all defined native scalar and retained-production reads without losing data quality.
        /// </summary>
        [Test]
        public async Task TypedClientReadsNativeGeometryQualificationCalibrationAndProductionAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSystemSnapshot initial = FullObservation();
            await using GmsHandle handle = await GmsSystemTests.Builder(
                context, "NativeClient", new InMemoryGmsProvider(initial))
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.Sensors | GmsFeatures.DynamicSensors | GmsFeatures.Identification |
                        GmsFeatures.Loading | GmsFeatures.ToolMonitoring | GmsFeatures.Calibration |
                        GmsFeatures.Production
                }).WithMachineToolFeatures(MachineToolFeatures.ProductionStatistics)
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            await using GmsHandle mandatory = await GmsSystemTests.Builder(
                context, "NativeMandatory", new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            ISession session = await fixture.ConnectAsync(managed: true).ConfigureAwait(false);
            Assert.That(session.NamespaceUris.GetIndex(GmsModel.NamespaceUri), Is.Not.EqualTo(1),
                "The live namespace order differs from the released NodeSet's GMS=1 index.");
            await using var client = session.CreateGmsClient(NUnitTelemetryContext.Create());
            ArrayOf<GmsDeviceEntry> found = await client.DiscoverAsync().ConfigureAwait(false);
            Assert.That(found.ToArray()!.Select(value => value.NodeId),
                Is.EquivalentTo(new[] { handle.NodeId, mandatory.NodeId }));
            GmsSystemReadSnapshot snapshot = await client.ReadSnapshotAsync(handle.NodeId).ConfigureAwait(false);
            Assert.That(snapshot.Identification.Workspace.IsPresent, Is.True);
            Assert.That(snapshot.Identification.Workspace.Value.WrappedValue.TryGetStructure<CartesianWorkspaceType>(
                out CartesianWorkspaceType? workspace), Is.True);
            Assert.That(workspace!.Width, Is.EqualTo(100));
            Assert.That(snapshot.Loading!.LoadStatus.Value.WrappedValue.TryGetValue(out uint load), Is.True);
            Assert.That(load, Is.EqualTo(2));
            GmsEquipmentReadSnapshot equipment = await client.ReadEquipmentAsync(handle.NodeId).ConfigureAwait(false);
            Assert.That(equipment.Sensors.Count, Is.EqualTo(1));
            GmsSensorReadSnapshot multi = equipment.Sensors[0];
            Assert.That(multi.Inherited.TypeDefinitionId,
                Is.EqualTo(NodeId.Create(1012u, GmsModel.NamespaceUri, session.NamespaceUris)));
            Assert.That(multi.Class.IsPresent, Is.False);
            GmsSensorReadSnapshot sensor = multi.Sensors[0];
            Assert.That(sensor.Class.Value.WrappedValue.TryGetValue(out ushort classification), Is.True);
            Assert.That(classification, Is.EqualTo((ushort)3));
            Assert.That(sensor.Class.Value.SourceTimestamp, Is.EqualTo(GmsEquipmentTests.AcquisitionTime));
            Assert.That(sensor.Capabilities.Value.WrappedValue.TryGetValue(out ArrayOf<ushort> capabilities), Is.True);
            Assert.That(capabilities, Is.EqualTo((ArrayOf<ushort>)[1, 4]));
            Assert.That(sensor.EngineeringUnit.IsPresent, Is.True);
            Assert.That(sensor.MeasuringRange.Value.WrappedValue.TryGetValue(out double range), Is.True);
            Assert.That(range, Is.EqualTo(50));
            Assert.That(sensor.Inherited.Life[0].IsCountingUp.WrappedValue.TryGetValue(out bool countingUp), Is.True);
            Assert.That(countingUp, Is.False);
            Assert.That(sensor.Inherited.Life[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Double));
            GmsCalibrationReadSnapshot? calibration = await client.ReadCalibrationAsync(handle.NodeId)
                .ConfigureAwait(false);
            Assert.That(calibration, Is.Not.Null);
            Assert.That(calibration!.Calibrated.Value.WrappedValue.TryGetValue(out bool calibrated), Is.True);
            Assert.That(calibrated, Is.True);
            Assert.That(calibration.CalibrationCertificate.Value.WrappedValue.TryGetValue(
                out ArrayOf<string> certificates), Is.True);
            Assert.That(certificates.Count, Is.EqualTo(1));
            GmsProductionReadSnapshot production = await client.ReadProductionAsync(handle.NodeId)
                .ConfigureAwait(false);
            Assert.That(production.Jobs[0].MeasurementReason.Value.WrappedValue.TryGetValue(
                out MeasurementReasonEnum reason), Is.True);
            Assert.That(reason, Is.EqualTo(MeasurementReasonEnum.AuditMeasurement));
            GmsPartReadSnapshot part = production.Jobs[0].Parts[0];
            Assert.That(part.NestIdentifier!.CatalogEntry.Value.WrappedValue.TryGetValue(out string entry), Is.True);
            Assert.That(entry, Is.EqualTo("Nest-42"));
            Assert.That(part.ProcessParameter!.Value.WrappedValue.TryGetValue(out ArrayOf<double> parameters), Is.True);
            Assert.That(parameters, Is.EqualTo((ArrayOf<double>)[1, 2.5]));
            MachineToolDataObservation observed = await client.ObserveDataAsync(handle.NodeId,
                new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(5), MaximumNotifications = 100 })
                .ConfigureAwait(false);
            Assert.That(observed.Changes.ToArray()!.Any(value => value.NodeId == sensor.Inherited.Life[0].NodeId &&
                value.Value.WrappedValue.TryGetValue(out double hours) && hours == 2.5), Is.True);
            Assert.That(observed.DroppedNotifications, Is.Zero);
            Assert.That(await client.GetResults(handle.NodeId).QueryAsync(1).ConfigureAwait(false), Is.Empty);
            GmsSystemReadSnapshot minimal = await client.ReadSnapshotAsync(mandatory.NodeId).ConfigureAwait(false);
            Assert.That(minimal.Identification.Workspace.IsPresent, Is.False);
            Assert.That(minimal.Inherited.OperationMode.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(minimal.Loading, Is.Null);
            Assert.That((await client.ReadEquipmentAsync(mandatory.NodeId).ConfigureAwait(false)).Sensors, Is.Empty);
            Assert.That(await client.ReadCalibrationAsync(mandatory.NodeId).ConfigureAwait(false), Is.Null);
            Assert.That((await client.ReadProductionAsync(mandatory.NodeId).ConfigureAwait(false)).Jobs, Is.Empty);
            Assert.That(handle.ServerProfiles.ToArray(),
                Does.Not.Contain("http://opcfoundation.org/UA-Profile/GMS/Server/Production"));
            Assert.That(handle.Capabilities.Limitations[0], Does.Contain("GMS_ProductionStatisticsType"));
            Assert.That(handle.ConformanceUnits.ToArray()!.Any(unit => unit.Name == "GMS GMSJobType"), Is.True);
            Assert.That(mandatory.ConformanceUnits.ToArray()!.Any(unit => unit.Name == "GMS SensorType"), Is.False);
            await client.DisposeAsync().ConfigureAwait(false);
            Assert.That(session.Connected, Is.True);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await client.ReadSnapshotAsync(handle.NodeId).ConfigureAwait(false));
        }

        /// <summary>
        /// Early bound failures release real continuation points instead of gradually exhausting the session.
        /// </summary>
        [Test]
        public async Task NativeClientLimitsReleaseBrowseContinuationsAndCancelObservationsAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            var values = new GmsAdditionalSensor[65];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = new GmsAdditionalSensor
                {
                    Key = "Sensor" + i, Identifier = "S-" + i, Name = "Environmental sensor",
                    Value = GmsEquipmentTests.Sample(Variant.From(20.0f)),
                    Class = GmsEquipmentTests.Sample(Variant.From((byte)1)), EngineeringUnits = Unit()
                };
            }
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "ManyNative",
                new InMemoryGmsProvider(new GmsSystemSnapshot { AdditionalSensors = values.ToArrayOf() }))
                .WithOptions(new GmsSystemOptions { Features = GmsFeatures.AdditionalSensors })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            ISession session = await fixture.ConnectAsync(managed: true).ConfigureAwait(false);
            await using var bounded = new GmsClient(
                session, NUnitTelemetryContext.Create(), new GmsClientOptions { MaximumElements = 1 });
            for (int i = 0; i < 12; i++)
            {
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await bounded.ReadEquipmentAsync(handle.NodeId).ConfigureAwait(false)))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            await using var smallPayload = new GmsClient(
                session, NUnitTelemetryContext.Create(), new GmsClientOptions { MaximumPayloadBytes = 1 });
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await smallPayload.ReadSnapshotAsync(handle.NodeId).ConfigureAwait(false));
            await using var client = new GmsClient(session, NUnitTelemetryContext.Create());
            GmsEquipmentReadSnapshot all = await client.ReadEquipmentAsync(handle.NodeId).ConfigureAwait(false);
            Assert.That(all.AdditionalSensors.Count, Is.EqualTo(65));
            Assert.That(all.AdditionalSensors[0].Value.WrappedValue.TryGetValue(out float temperature), Is.True);
            Assert.That(temperature, Is.EqualTo(20));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(async () => await client.DiscoverAsync(cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Task<MachineToolEventObservation> pending = client.ObserveEventsAsync(handle.NodeId,
                new MachineToolObservationOptions { Window = TimeSpan.FromMinutes(1) }).AsTask();
            await client.DisposeAsync().ConfigureAwait(false);
            Assert.That(() => pending, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(session.Connected, Is.True);
        }

        /// <summary>
        /// Discovery accepts a real vendor subtype and rejects unrelated nodes in the same instance namespace.
        /// </summary>
        [Test]
        public async Task DiscoveryUsesActualSubtypeHierarchyRatherThanNamespacePresenceAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            var vendor = new BaseObjectTypeState
            {
                NodeId = new NodeId(Guid.NewGuid(), context.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("VendorGmsType", context.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Vendor GMS"),
                SuperTypeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.GMSType, context.Context.NamespaceUris),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            await fixture.Manager.AddPredefinedNodeAsync(vendor, CancellationToken.None).ConfigureAwait(false);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "VendorInstance",
                new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .WithMachinery(builder => builder.State.TypeDefinitionId = vendor.NodeId)
                .BuildAsync().ConfigureAwait(false);
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            await using var client = new GmsClient(session, NUnitTelemetryContext.Create());
            ArrayOf<GmsDeviceEntry> devices = await client.DiscoverAsync().ConfigureAwait(false);
            Assert.That(devices.Count, Is.EqualTo(1));
            Assert.That(devices[0].TypeDefinitionId, Is.EqualTo(vendor.NodeId));
            Assert.That((await client.ReadSnapshotAsync(handle.NodeId).ConfigureAwait(false)).NodeId,
                Is.EqualTo(handle.NodeId));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadSnapshotAsync(handle.State.Equipment!.NodeId).ConfigureAwait(false));
            Assert.Throws<ArgumentException>(() => client.GetProxy(NodeId.Null));
            await handle.DisposeAsync().ConfigureAwait(false);
            await fixture.Manager.DeleteNodeAsync(fixture.Manager.SystemContext, vendor.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Supplies all native sensor properties and all defined part catalogs on the public provider boundary.
        /// </summary>
        internal static GmsSystemSnapshot FullObservation()
        {
            GmsSystemSnapshot initial = GmsCalibrationTests.Example();
            GmsSystemSnapshot production = GmsSystemProductionTests.Example();
            GmsSensor sensor = initial.Sensors[0];
            GmsCatalogObservation catalog = production.Parts[0].NestIdentifier!;
            return initial with
            {
                MachineTool = production.MachineTool,
                Jobs = production.Jobs,
                Parts =
                [
                    production.Parts[0] with
                    {
                        PartAmendmentStatus = GmsEquipmentTests.Sample(Variant.From("A")),
                        ProductionNumber = GmsEquipmentTests.Sample(Variant.From("PN-1")),
                        Operator = catalog with { Value = GmsEquipmentTests.Sample(Variant.From(42u)) },
                        PartCarrierIdentifier = catalog with { Value = GmsEquipmentTests.Sample(Variant.From(42L)) },
                        ProcessingMachineIdentifier = catalog with
                        {
                            Value = GmsEquipmentTests.Sample(Variant.From(42.0))
                        },
                        ProcessParameter = catalog with
                        {
                            Value = GmsEquipmentTests.Sample(Variant.From((ArrayOf<double>)[1, 2.5]))
                        }
                    }
                ],
                Sensors =
                [
                    new GmsSensor
                    {
                        Tool = new MachineToolTool
                        {
                            Key = "Head", Kind = MachineToolToolKind.MultiTool,
                            Sample = new MachineToolSampleInfo(GmsEquipmentTests.AcquisitionTime, StatusCodes.Good)
                        },
                        Alignment = GmsEquipmentTests.Sample(Variant.From(ToolAlignmentState.Continuous)),
                        Axes = GmsEquipmentTests.Sample(Variant.From((ArrayOf<string>)["A", "B"])),
                        Sensors =
                        [
                            sensor with
                            {
                                Capabilities = GmsEquipmentTests.Sample(Variant.From((ArrayOf<ushort>)[1, 4])),
                                Axes = GmsEquipmentTests.Sample(Variant.From((ArrayOf<string>)["X", "Y", "Z"])),
                                AbsoluteProbe = GmsEquipmentTests.Sample(Variant.From(true)),
                                EngineeringUnit = GmsEquipmentTests.Sample(Variant.FromStructure(new EUInformation
                                {
                                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                                    UnitId = 0x4D4D54, DisplayName = new LocalizedText("mm"),
                                    Description = new LocalizedText("millimetre")
                                })),
                                MeasuringRange = GmsEquipmentTests.Sample(Variant.From(50.0)),
                                WorkingRange = GmsEquipmentTests.Sample(Variant.From(100.0)),
                                Resolution = GmsEquipmentTests.Sample(Variant.From(0.001)),
                                Qualified = sensor.Qualified! with
                                {
                                    StartHours = GmsEquipmentTests.Sample(Variant.From(8.0)),
                                    WarningHours = GmsEquipmentTests.Sample(Variant.From(1.0)),
                                    LimitHours = GmsEquipmentTests.Sample(Variant.From(0.0))
                                }
                            }
                        ]
                    }
                ]
            };
        }

        /// <summary>
        /// Supplies explicit dimension metadata rather than inferring it from classification.
        /// </summary>
        internal static EUInformation Unit() => new()
        {
            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
            UnitId = 4408652, DisplayName = new LocalizedText("°C"), Description = new LocalizedText("degree Celsius")
        };
    }
}
