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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Hosting;
using Opc.Ua.AMB.Server.Structure;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Gives registered assets their version information, locations,
    /// classification, requirements, capabilities and relations
    /// (OPC 10000-110 §10, §11, §13, §14) in a hosted server.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AssetStructureTests
    {
        private static readonly QualifiedName[] s_locationUnits =
        [
            new("AMB Hierarchical Location Property"),
            new("AMB Hierarchical Location Objects"),
            new("AMB Operational Location Property"),
            new("AMB Operational Location Objects"),
            new("AMB Digital Location")
        ];

        private static readonly QualifiedName[] s_classificationUnits =
        [
            new("AMB Requirements"),
            new("AMB Capabilities"),
            new("AMB Local Time")
        ];

        private static readonly QualifiedName[] s_structureUnits =
        [
            new("AMB Sub-assets"),
            new("AMB Asset relations"),
            new("AMB Operation Counters")
        ];

        [TestCase(false, TestName = "LocationsWithDeviceIntegrationRegisteredFirst")]
        [TestCase(true, TestName = "LocationsWithAssetManagementRegisteredFirst")]
        public async Task LocationsContainTheAssetInBothDirectionsAsync(bool assetManagementFirst)
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(LocationsContainTheAssetInBothDirectionsAsync) + assetManagementFirst,
                    builder => builder
                        .LocatedIn(AssetLocationKind.Hierarchical, "Plant1/Hall3/Line2")
                        .LocatedIn(AssetLocationKind.Operational, "Packaging")
                        .WithLocation(AssetLocationKind.Hierarchical, "Plant1/Hall3/Line2")
                        .WithLocation(AssetLocationKind.Operational, "Packaging")
                        .WithLocation(AssetLocationKind.Digital, "https://plant1.example/sensor"),
                    assetManagementFirst: assetManagementFirst)
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            ushort instances = server.Manager.InstanceNamespaceIndex;
            NodeId hierarchicalContains = server.ToNodeId(ReferenceTypeIds.HierarchicalContains);
            NodeId operationalContains = server.ToNodeId(ReferenceTypeIds.OperationalContains);

            IReadOnlyList<ReferenceDescription> roots = await server
                .BrowseAsync(server.ToNodeId(ObjectIds.HierarchicalLocations), Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);
            NodeId plant = server.ToNodeId(roots.Single(reference => reference.BrowseName.Name == "Plant1").NodeId);
            NodeId hall = await ChildAsync(server, plant, "Hall3").ConfigureAwait(false);
            NodeId line = await ChildAsync(server, hall, "Line2").ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> contained = await server
                .BrowseAsync(line, hierarchicalContains)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> locatedIn = await server
                .BrowseAsync(device.NodeId, hierarchicalContains, BrowseDirection.Inverse)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> operational = await server
                .BrowseAsync(device.NodeId, operationalContains, BrowseDirection.Inverse)
                .ConfigureAwait(false);
            NodeId again = await server.AssetManagement
                .DefineLocationAsync(AssetLocationKind.Hierarchical, " Plant1 / Hall3 / Line2 ")
                .ConfigureAwait(false);
            DataValue digital = await server
                .ReadAsync(Child(server, device, Amb(server, "DigitalLocation")))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    roots.Single(reference => reference.BrowseName.Name == "Plant1").BrowseName.NamespaceIndex,
                    Is.EqualTo(instances));
                Assert.That(
                    contained.Select(reference => reference.NodeId),
                    Does.Contain((ExpandedNodeId)device.NodeId));
                Assert.That(locatedIn.Select(reference => reference.NodeId), Does.Contain((ExpandedNodeId)line));
                Assert.That(operational, Has.Count.EqualTo(1));
                Assert.That(again, Is.EqualTo(line), "an existing path is found again");
                Assert.That(digital.WrappedValue.TryGetValue(out string uri) ? uri : null, Is.EqualTo(
                    "https://plant1.example/sensor"));
                Assert.That(server.Manager.ConformanceUnits.ToArray(), Is.SupersetOf(s_locationUnits));
            });
        }

        [Test]
        public async Task AnUnregisteredAssetLeavesItsLocationsAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(AnUnregisteredAssetLeavesItsLocationsAsync),
                    builder => builder
                        .LocatedIn(AssetLocationKind.Hierarchical, "Plant1/Hall3")
                        .LocatedIn(AssetLocationKind.Operational, "Cooling"))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            NodeId hierarchicalContains = server.ToNodeId(ReferenceTypeIds.HierarchicalContains);
            NodeId operationalContains = server.ToNodeId(ReferenceTypeIds.OperationalContains);
            NodeId hall = await server.AssetManagement
                .DefineLocationAsync(AssetLocationKind.Hierarchical, "Plant1/Hall3")
                .ConfigureAwait(false);
            NodeId cooling = await server.AssetManagement
                .DefineLocationAsync(AssetLocationKind.Operational, "Cooling")
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> before = await server
                .BrowseAsync(hall, hierarchicalContains)
                .ConfigureAwait(false);

            await asset.UnregisterAsync().ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> hierarchical = await server
                .BrowseAsync(hall, hierarchicalContains)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> operational = await server
                .BrowseAsync(cooling, operationalContains)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> locatedIn = await server
                .BrowseAsync(device.NodeId, hierarchicalContains, BrowseDirection.Inverse)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(before, Has.Count.EqualTo(1));
                Assert.That(hierarchical, Is.Empty, "the location no longer contains the asset");
                Assert.That(operational, Is.Empty);
                Assert.That(locatedIn, Is.Empty, "nor is the object located in it");
            });
        }

        [Test]
        public async Task VersionInformationUsesTheDeviceSlotsAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(VersionInformationUsesTheDeviceSlotsAsync),
                    builder => builder.WithVersionInformation("HW-1", "SW-2", 3))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;

            int next = await asset.IncrementRevisionCounterAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(device.HardwareRevision?.Value, Is.EqualTo("HW-1"));
                Assert.That(device.SoftwareRevision?.Value, Is.EqualTo("SW-2"));
                Assert.That(device.RevisionCounter?.Value, Is.EqualTo(4));
                Assert.That(next, Is.EqualTo(4));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Version Information")));
            });
        }

        [Test]
        public async Task TheDeviceIntegrationDefaultsAreNoVersionInformationAsync()
        {
            (AmbHostedServer withoutVersion, DeviceState? first) = await StartWithRevisionsAsync(
                nameof(TheDeviceIntegrationDefaultsAreNoVersionInformationAsync) + "Without",
                askForVersionInformation: false).ConfigureAwait(false);
            await using AmbHostedServer withoutScope = withoutVersion;
            (AmbHostedServer withVersion, DeviceState? second) = await StartWithRevisionsAsync(
                nameof(TheDeviceIntegrationDefaultsAreNoVersionInformationAsync) + "With",
                askForVersionInformation: true).ConfigureAwait(false);
            await using AmbHostedServer withScope = withVersion;

            Assert.Multiple(() =>
            {
                Assert.That(first!.RevisionCounter?.Value, Is.EqualTo(-1));
                Assert.That(
                    withoutVersion.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Version Information")),
                    "a RevisionCounter of -1 says the asset does not support it");
                Assert.That(second!.RevisionCounter?.Value, Is.Zero, "asking for version information starts it");
                Assert.That(
                    withVersion.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Version Information")));
            });
        }

        private static async Task<(AmbHostedServer Server, DeviceState? Device)> StartWithRevisionsAsync(
            string name,
            bool askForVersionInformation)
        {
            DeviceState? device = null;
            AmbHostedServer server = await AmbHostedServer.StartAsync(
                name,
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> sensor = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            "urn:acme:sensor").ConfigureAwait(false);
                        sensor.WithIdentification(identification =>
                        {
                            identification.HardwareRevision = "C";
                            identification.RevisionCounter = -1;
                        });
                        device = sensor.Device;
                        await sensor.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            asset =>
                            {
                                if (askForVersionInformation)
                                {
                                    asset.WithVersionInformation();
                                }
                            }).ConfigureAwait(false);
                    })).ConfigureAwait(false);
            return (server, device);
        }

        [Test]
        public async Task VersionInformationIsCreatedOnAnyObjectAsync()
        {
            IAssetHandle? bare = null;
            IAssetHandle? software = null;
            INodeBuilder<BaseObjectState>? softwareNode = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(VersionInformationIsCreatedOnAnyObjectAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureAssetManagement(async context =>
                    {
                        bare = await context.Assets.RegisterAssetAsync(
                            AssetRegistrationTests.CreateMachine(context, "urn:acme:press:1"),
                            null,
                            context.CancellationToken).ConfigureAwait(false);
                        softwareNode = CreatePart(context, null, "Firmware", "urn:acme:firmware:3");
                        software = await context.Assets.RegisterAssetAsync(
                            softwareNode,
                            asset => asset.WithVersionInformation(softwareRevision: "3.1.4"),
                            context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            ServiceResultException refused = Assert.ThrowsAsync<ServiceResultException>(
                async () => await bare!.IncrementRevisionCounterAsync().ConfigureAwait(false))!;
            int next = await software!.IncrementRevisionCounterAsync().ConfigureAwait(false);
            ushort di = (ushort)server.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
            BaseObjectState firmware = softwareNode!.Node;

            Assert.Multiple(() =>
            {
                Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(next, Is.EqualTo(1), "a created counter starts at 0");
                Assert.That(
                    ((BaseVariableState)firmware.FindChildWithQualifiedName(
                        server.Manager.SystemContext,
                        new QualifiedName("SoftwareRevision", di))!).WrappedValue.TryGetValue(out string revision)
                        ? revision
                        : null,
                    Is.EqualTo("3.1.4"));
                Assert.That(
                    firmware.FindChildWithQualifiedName(
                        server.Manager.SystemContext,
                        new QualifiedName("HardwareRevision", di)),
                    Is.Null,
                    "§10.2: a software asset has no hardware revision");
                Assert.That(
                    firmware.ReferenceExists(
                        Ua.ReferenceTypeIds.HasInterface,
                        false,
                        server.ToNodeId(Opc.Ua.Di.ObjectTypeIds.IVendorNameplateType)),
                    Is.True);
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Version Information")),
                    "the bare machine has none");
            });
        }

        [Test]
        public async Task WritableLocationsAndLocalTimeSurviveARestartAsync()
        {
            string state = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AssetStructureTests),
                Guid.NewGuid().ToString("N"));
            const string name = nameof(WritableLocationsAndLocalTimeSurviveARestartAsync);
            await using (AmbHostedServer first = await StartWithStoreAsync(name, state).ConfigureAwait(false))
            {
                BaseObjectState sensor = await first
                    .FindNodeAsync<BaseObjectState>(first.AssetManagement.Assets[0].NodeId)
                    .ConfigureAwait(false);
                NodeId location = Child(first, sensor, Amb(first, "HierarchicalLocation"));
                NodeId localTime = Child(first, sensor, new QualifiedName("LocalTime"));

                StatusCode written = await first.WriteAsync(location, Variant.From("Plant2/Hall1"))
                    .ConfigureAwait(false);
                StatusCode wrongType = await first.WriteAsync(location, Variant.From(7)).ConfigureAwait(false);
                StatusCode tooLong = await first.WriteAsync(location, Variant.From(new string('p', 1025)))
                    .ConfigureAwait(false);
                StatusCode timeZone = await first
                    .WriteAsync(localTime, Variant.FromStructure(new TimeZoneDataType { Offset = 120 }))
                    .ConfigureAwait(false);
                StatusCode notATimeZone = await first.WriteAsync(localTime, Variant.From("UTC+2"))
                    .ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(written, Is.EqualTo(StatusCodes.Good));
                    Assert.That(wrongType, Is.EqualTo(StatusCodes.BadTypeMismatch));
                    Assert.That(tooLong, Is.EqualTo(StatusCodes.BadOutOfRange), "MaxLocationLength");
                    Assert.That(timeZone, Is.EqualTo(StatusCodes.Good));
                    Assert.That(notATimeZone, Is.EqualTo(StatusCodes.BadTypeMismatch));
                });
            }

            await using AmbHostedServer second = await StartWithStoreAsync(name, state).ConfigureAwait(false);
            BaseObjectState restarted = await second
                .FindNodeAsync<BaseObjectState>(second.AssetManagement.Assets[0].NodeId)
                .ConfigureAwait(false);
            DataValue restoredLocation = await second
                .ReadAsync(Child(second, restarted, Amb(second, "HierarchicalLocation")))
                .ConfigureAwait(false);
            DataValue restoredTime = await second
                .ReadAsync(Child(second, restarted, new QualifiedName("LocalTime")))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    restoredLocation.WrappedValue.TryGetValue(out string text) ? text : null,
                    Is.EqualTo("Plant2/Hall1"));
                Assert.That(TimeZoneOf(restoredTime)?.Offset, Is.EqualTo((short)120));
                Assert.That(TimeZoneOf(restoredTime)?.DaylightSavingInOffset, Is.False);
            });
        }

        [Test]
        public async Task ClassificationRequirementsAndCapabilitiesAsync()
        {
            var eclass = new ExpandedNodeId("0173-1#01-ADN228#012", "urn:example:eclass");
            var voltage = new ExpandedNodeId("0173-1#02-AAC967#005", "urn:example:eclass");
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(ClassificationRequirementsAndCapabilitiesAsync),
                    builder => builder
                        .ClassifiedAs(eclass)
                        .WithLocalTime(60, daylightSavingInOffset: true)
                        .WithRequirements(requirements => requirements
                            .Add("SupplyVoltage", Variant.From(24.0), voltage))
                        .WithCapabilities(capabilities => capabilities
                            .Add("MaxPressure", Variant.From(16.0))
                            .Add("Medium", Variant.From("Water"))))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            ushort amb = server.Manager.AmbNamespaceIndex;
            var requirements = (FolderState)device.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("Requirements", amb))!;
            var capabilities = (FolderState)device.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("Capabilities", amb))!;
            IReadOnlyList<ReferenceDescription> entries = await server
                .BrowseAsync(requirements.NodeId, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);
            NodeId supplyVoltage = server.ToNodeId(entries.Single().NodeId);
            BaseVariableState entry = await server.FindNodeAsync<BaseVariableState>(supplyVoltage)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> classified = await server
                .BrowseAsync(device.NodeId, Ua.ReferenceTypeIds.HasDictionaryEntry)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> capabilityEntries = await server
                .BrowseAsync(capabilities.NodeId, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(classified.Select(reference => reference.NodeId), Does.Contain(eclass));
                Assert.That(entries.Single().BrowseName.Name, Is.EqualTo("SupplyVoltage"));
                Assert.That(entry.DataType, Is.EqualTo(Ua.DataTypeIds.Double));
                Assert.That(entry.WrappedValue.TryGetValue(out double value) ? value : 0, Is.EqualTo(24.0));
                Assert.That(entry.ReferenceExists(Ua.ReferenceTypeIds.HasDictionaryEntry, false, voltage), Is.True);
                Assert.That(capabilityEntries, Has.Count.EqualTo(2));
                Assert.That(
                    device.FindChildWithQualifiedName(server.Manager.SystemContext, new QualifiedName("LocalTime")),
                    Is.Not.Null);
                Assert.That(server.Manager.ConformanceUnits.ToArray(), Is.SupersetOf(s_classificationUnits));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Classification")),
                    "Part 19: the target is no dictionary entry object of the server");
            });
        }

        [Test]
        public async Task AClassificationNeedsADictionaryEntryObjectAsync()
        {
            const string irdi = "0173-1#01-AKE798#019";
            var classification = new ExpandedNodeId(irdi, AmbServerOptions.IrdiNamespaceUri);
            NodeId entry = NodeId.Null;
            DeviceState? device = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AClassificationNeedsADictionaryEntryObjectAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureAssetManagement(async context =>
                    {
                        entry = await context.Assets
                            .DefineDictionaryEntryAsync(irdi, new LocalizedText("Centrifugal pump"))
                            .ConfigureAwait(false);
                        NodeId again = await context.Assets.DefineDictionaryEntryAsync(irdi).ConfigureAwait(false);
                        Assert.That(again, Is.EqualTo(entry), "an entry is defined once");
                    })
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> pump = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Pump",
                            "urn:acme:pump:1").ConfigureAwait(false);
                        device = pump.Device;
                        await pump.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            asset => asset.ClassifiedAs(classification)).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> dictionaries = await server
                .BrowseAsync(Ua.ObjectIds.Dictionaries, Ua.ReferenceTypeIds.HasComponent)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> classified = await server
                .BrowseAsync(device!.NodeId, Ua.ReferenceTypeIds.HasDictionaryEntry)
                .ConfigureAwait(false);
            ReferenceDescription? listed = dictionaries.FirstOrDefault(
                reference => server.ToNodeId(reference.NodeId) == entry);

            Assert.Multiple(() =>
            {
                Assert.That(entry.IdType, Is.EqualTo(IdType.String));
                Assert.That(
                    server.Server.NamespaceUris.GetString(entry.NamespaceIndex),
                    Is.EqualTo(AmbServerOptions.IrdiNamespaceUri));
                Assert.That(listed, Is.Not.Null, "the entry is a component of Server/Dictionaries");
                Assert.That(
                    listed?.TypeDefinition,
                    Is.EqualTo((ExpandedNodeId)Ua.ObjectTypeIds.IrdiDictionaryEntryType));
                Assert.That(classified.Select(reference => server.ToNodeId(reference.NodeId)), Does.Contain(entry));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Classification")));
            });
        }

        [Test]
        public async Task SubAssetsRelationsAndOperationCountersAsync()
        {
            IAssetHandle? press = null;
            IAssetHandle? drive = null;
            IAssetHandle? sensor = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(SubAssetsRelationsAndOperationCountersAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            "urn:acme:sensor:4711").ConfigureAwait(false);
                        sensor = await device.RegisterAsAssetAsync(context.GetRequiredService<IAssetManagement>()).ConfigureAwait(false);
                    })
                    .ConfigureAssetManagement(async context =>
                    {
                        INodeBuilder<BaseObjectState> machine = AssetRegistrationTests.CreateMachine(
                            context,
                            "urn:acme:press:1");
                        INodeBuilder<BaseObjectState> motor = CreatePart(
                            context,
                            machine.Node,
                            "Drive",
                            "urn:acme:drive:7");
                        AddOperationCounter(context, machine.Node);
                        drive = await context.Assets.RegisterAssetAsync(motor, null, context.CancellationToken).ConfigureAwait(false);
                        press = await context.Assets.RegisterAssetAsync(
                            machine,
                            asset => asset.RelatesTo(Ua.ReferenceTypeIds.Utilizes, sensor!.NodeId),
                            context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);
            DeviceState sensorNode = await server.FindNodeAsync<DeviceState>(sensor!.NodeId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(drive, Is.Not.Null);
                Assert.That(
                    sensorNode.ReferenceExists(Ua.ReferenceTypeIds.Utilizes, true, press!.NodeId),
                    Is.True,
                    "the related asset gets the inverse reference");
                Assert.That(server.Manager.ConformanceUnits.ToArray(), Is.SupersetOf(s_structureUnits));
            });
        }

        [Test]
        public async Task AHierarchicalReferenceMakesASubAssetAsync()
        {
            IAssetHandle? pump = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AHierarchicalReferenceMakesASubAssetAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IAssetManagement assets = context.GetRequiredService<IAssetManagement>();
                        IDeviceBuilder<DeviceState> motor = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Motor",
                            "urn:acme:motor:1").ConfigureAwait(false);
                        IAssetHandle motorAsset = await motor.RegisterAsAssetAsync(assets).ConfigureAwait(false);
                        IDeviceBuilder<DeviceState> device = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Pump",
                            "urn:acme:pump:1").ConfigureAwait(false);
                        pump = await device.RegisterAsAssetAsync(
                            assets,
                            asset => asset.RelatesTo(Ua.ReferenceTypeIds.HasComponent, motorAsset.NodeId)).ConfigureAwait(false);
                    })).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pump, Is.Not.Null);
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Sub-assets")));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Asset relations")),
                    "a hierarchical reference is no relation");
            });
        }

        [Test]
        public async Task InvalidLocationsAreRefusedAsync()
        {
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(InvalidLocationsAreRefusedAsync),
                builder => builder.AddOpcUaDi().AddAssetManagement()).ConfigureAwait(false);
            var builder = new AssetBuilder();

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(async () => await server.AssetManagement
                    .DefineLocationAsync(AssetLocationKind.Digital, "Somewhere")
                    .ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () => await server.AssetManagement
                    .DefineLocationAsync(AssetLocationKind.Hierarchical, "Plant1//Line2")
                    .ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () => await server.AssetManagement
                    .DefineLocationAsync(AssetLocationKind.Operational, " ")
                    .ConfigureAwait(false));
                Assert.Throws<ArgumentException>(() => builder.LocatedIn(AssetLocationKind.Digital, "Somewhere"));
                Assert.Throws<ArgumentException>(() => builder.LocatedIn(AssetLocationKind.Hierarchical, string.Empty));
                Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithLocation((AssetLocationKind)42));
                Assert.Throws<ArgumentException>(() => builder.ClassifiedAs(ExpandedNodeId.Null));
                Assert.Throws<ArgumentException>(() => builder.RelatesTo(NodeId.Null, new NodeId(1u)));
                Assert.Throws<ArgumentException>(() => builder.RelatesTo(Ua.ReferenceTypeIds.Utilizes, NodeId.Null));
                Assert.Throws<ArgumentNullException>(() => builder.WithRequirements(null!));
                Assert.Throws<ArgumentNullException>(() => builder.WithCapabilities(null!));
                Assert.Throws<ArgumentException>(() => builder.WithRequirements(entries => entries
                    .Add("A", Variant.From(1))
                    .Add("A", Variant.From(2))));
                Assert.Throws<ArgumentException>(() => builder.WithCapabilities(entries => entries
                    .Add(string.Empty, Variant.From(1))));
                Assert.ThrowsAsync<ServiceResultException>(async () => await new AssetManagement()
                    .DefineLocationAsync(AssetLocationKind.Hierarchical, "Plant1")
                    .ConfigureAwait(false));
            });
        }

        private static async Task<NodeId> ChildAsync(AmbHostedServer server, NodeId parent, string name)
        {
            IReadOnlyList<ReferenceDescription> children = await server
                .BrowseAsync(parent, Ua.ReferenceTypeIds.HasComponent)
                .ConfigureAwait(false);
            return server.ToNodeId(children.Single(reference => reference.BrowseName.Name == name).NodeId);
        }

        private static QualifiedName Amb(AmbHostedServer server, string name)
        {
            return new QualifiedName(name, server.Manager.AmbNamespaceIndex);
        }

        private static NodeId Child(AmbHostedServer server, NodeState parent, QualifiedName name)
        {
            NodeState? child = parent.FindChildWithQualifiedName(server.Manager.SystemContext, name);
            Assert.That(child, Is.Not.Null, name.ToString());
            return child!.NodeId;
        }

        private static Task<AmbHostedServer> StartWithStoreAsync(string name, string state)
        {
            return AmbHostedServer.StartAsync(
                name,
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement(options => options.UseFileSystemStores(state))
                    .ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> device = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            "urn:acme:sensor:4711").ConfigureAwait(false);
                        await device.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            asset => asset
                                .WithLocation(AssetLocationKind.Hierarchical, "Plant1/Hall3", writable: true)
                                .WithLocalTime(60, writable: true),
                            context.CancellationToken).ConfigureAwait(false);
                    }));
        }

        /// <summary>
        /// Builds a part of a machine that is an asset of its own: an object
        /// below the machine with its own <c>ProductInstanceUri</c>.
        /// </summary>
        private static INodeBuilder<BaseObjectState> CreatePart(
            IAssetManagementSetupContext context,
            BaseObjectState? machine,
            string name,
            string productInstanceUri)
        {
            ushort di = (ushort)context.Manager.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
            if (machine == null)
            {
                INodeBuilder<BaseObjectState> standalone = context.Builder.AddObject(
                    new QualifiedName(name, context.Manager.InstanceNamespaceIndex),
                    Ua.ObjectIds.ObjectsFolder);
                PropertyState standaloneUri = standalone.Node.AddProperty<string, VariantBuilder>(
                    "ProductInstanceUri",
                    Ua.DataTypeIds.String,
                    ValueRanks.Scalar);
                standaloneUri.BrowseName = new QualifiedName("ProductInstanceUri", di);
                standaloneUri.NodeId = context.Manager.New(context.Manager.SystemContext, standaloneUri);
                standaloneUri.WrappedValue = Variant.From(productInstanceUri);
                context.Manager.AddNode(standaloneUri);
                return standalone;
            }
            var part = new BaseObjectState(machine)
            {
                BrowseName = new QualifiedName(name, context.Manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType,
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
            };
            part.NodeId = context.Manager.New(context.Manager.SystemContext, part);
            machine.AddChild(part);
            PropertyState uri = part.AddProperty<string, VariantBuilder>(
                "ProductInstanceUri",
                Ua.DataTypeIds.String,
                ValueRanks.Scalar);
            uri.BrowseName = new QualifiedName("ProductInstanceUri", di);
            uri.NodeId = context.Manager.New(context.Manager.SystemContext, uri);
            uri.WrappedValue = Variant.From(productInstanceUri);
            context.Manager.AddNode(part);
            return context.Builder.Node<BaseObjectState>(part.NodeId);
        }

        /// <summary>
        /// Adds the <c>2:OperationCounters</c> group with one counter, nested
        /// one group deep as §10.3 allows.
        /// </summary>
        private static void AddOperationCounter(IAssetManagementSetupContext context, BaseObjectState machine)
        {
            ushort di = (ushort)context.Manager.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
            var counters = new BaseObjectState(machine)
            {
                BrowseName = new QualifiedName("OperationCounters", di),
                DisplayName = new LocalizedText("OperationCounters"),
                TypeDefinitionId = FunctionalGroup(context),
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
            };
            counters.NodeId = context.Manager.New(context.Manager.SystemContext, counters);
            machine.AddChild(counters);
            var group = new BaseObjectState(counters)
            {
                BrowseName = new QualifiedName("Spindle", context.Manager.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Spindle"),
                TypeDefinitionId = FunctionalGroup(context),
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
            };
            group.NodeId = context.Manager.New(context.Manager.SystemContext, group);
            counters.AddChild(group);
            PropertyState hours = group.AddProperty<double, VariantBuilder>(
                "OperationDuration",
                Ua.DataTypeIds.Duration,
                ValueRanks.Scalar);
            hours.BrowseName = new QualifiedName("OperationDuration", di);
            hours.NodeId = context.Manager.New(context.Manager.SystemContext, hours);
            hours.WrappedValue = Variant.From(3_600_000d);
            context.Manager.AddNode(counters);
        }

        private static NodeId FunctionalGroup(IAssetManagementSetupContext context)
        {
            return ExpandedNodeId.ToNodeId(
                Opc.Ua.Di.ObjectTypeIds.FunctionalGroupType,
                context.Manager.Server.NamespaceUris);
        }

        private static TimeZoneDataType? TimeZoneOf(DataValue value)
        {
            TimeZoneDataType zone;
            return value.WrappedValue.TryGetStructure(out zone!) ? zone : null;
        }
    }
}
