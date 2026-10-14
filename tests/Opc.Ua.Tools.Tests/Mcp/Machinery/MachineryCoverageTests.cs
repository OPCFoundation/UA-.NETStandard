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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Behavioral coverage of Machinery discovery, optional facets, projections and calibration calls.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class MachineryCoverageTests
    {
        /// <summary>
        /// Identification retains localized identity, dates and deliberately absent optional properties.
        /// </summary>
        [Test]
        public async Task ReadIdentificationPreservesLocaleDateAndAbsentOptionalsAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId machine = wire.Node("press");
            NodeId identification = wire.Node("press-identity");
            wire.Path(machine, identification, wire.Name("Identification", Di.Namespaces.OpcUaDi));
            wire.Child(identification, wire.Name("Manufacturer", Di.Namespaces.OpcUaDi),
                Variant.From(new LocalizedText("de", "Pressenbau")));
            wire.Child(identification, wire.Name("SerialNumber", Di.Namespaces.OpcUaDi), Variant.From("P-142"));
            wire.Child(identification, wire.Name("Model", Di.Namespaces.OpcUaDi),
                Variant.From(new LocalizedText("en", "Hydraulic press")));
            wire.Child(identification, wire.Name("ProductInstanceUri", Di.Namespaces.OpcUaDi),
                Variant.From("urn:press:P-142"));
            wire.Child(identification, wire.Name("SoftwareRevision", Di.Namespaces.OpcUaDi), Variant.From("2.4"));
            wire.Child(identification, wire.Name("Location", Machinery.Namespaces.Machinery),
                Variant.From("Hall 7"));
            wire.Child(identification, wire.Name("InitialOperationDate", Machinery.Namespaces.Machinery),
                Variant.From(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)));
            wire.Child(identification, wire.Name("YearOfConstruction", Machinery.Namespaces.Machinery),
                Variant.From((ushort)2025));
            wire.Child(identification, wire.Name("MonthOfConstruction", Machinery.Namespaces.Machinery),
                Variant.From((byte)11));
            await manager.RegisterExistingSessionAsync("press", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement json = Success(await services.GetRequiredService<MachineryTools>().ReadAsync(
                machine.ToString(), MachineryReadFacet.Identification, "press").ConfigureAwait(false));

            Assert.That(json.GetProperty("manufacturer").GetProperty("text").GetString(), Is.EqualTo("Pressenbau"));
            Assert.That(json.GetProperty("manufacturer").GetProperty("locale").GetString(), Is.EqualTo("de"));
            Assert.That(json.GetProperty("serialNumber").GetString(), Is.EqualTo("P-142"));
            Assert.That(json.GetProperty("model").GetProperty("text").GetString(), Is.EqualTo("Hydraulic press"));
            Assert.That(json.GetProperty("productInstanceUri").GetString(), Is.EqualTo("urn:press:P-142"));
            Assert.That(json.GetProperty("softwareRevision").GetString(), Is.EqualTo("2.4"));
            Assert.That(json.GetProperty("location").GetString(), Is.EqualTo("Hall 7"));
            Assert.That(json.GetProperty("initialOperationDate").GetString(),
                Is.EqualTo("2026-03-04T05:06:07.0000000Z"));
            Assert.That(json.GetProperty("yearOfConstruction").GetUInt16(), Is.EqualTo(2025));
            Assert.That(json.GetProperty("monthOfConstruction").GetByte(), Is.EqualTo(11));
            Assert.That(json.GetProperty("assetId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.GetProperty("hardwareRevision").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(wire.Reads, Has.Count.EqualTo(9));
            Assert.That(wire.Paths.Skip(1).All(path =>
                path.RelativePath.Elements[0].ReferenceTypeId == ReferenceTypeIds.HasProperty), Is.True);
        }

        /// <summary>
        /// A connected machine without an optional facet is different from a failed read.
        /// </summary>
        [TestCase(MachineryReadFacet.Identification)]
        [TestCase(MachineryReadFacet.OperationCounters)]
        [TestCase(MachineryReadFacet.ItemState)]
        [TestCase(MachineryReadFacet.OperationMode)]
        public async Task ReadAbsentFacetReturnsExplicitNullWithoutReadingValuesAsync(MachineryReadFacet facet)
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            await manager.RegisterExistingSessionAsync("bare", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement json = Success(await services.GetRequiredService<MachineryTools>().ReadAsync(
                wire.Node("bare-machine").ToString(), facet, "bare").ConfigureAwait(false));

            Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(wire.Paths, Has.Count.EqualTo(1));
            Assert.That(wire.Reads, Is.Empty);
        }

        /// <summary>
        /// Health and open-typed counters preserve zero, non-finite values and UInt64 precision.
        /// </summary>
        [Test]
        public async Task ReadHealthAndCountersRetainTypedValuesAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId machine = wire.Node("press");
            NodeId health = wire.Node("health");
            NodeId counters = wire.Node("counters");
            wire.Path(machine, health, wire.Name("Monitoring", Machinery.Namespaces.Machinery),
                wire.Name("Health", Machinery.Namespaces.Machinery));
            wire.Child(health, wire.Name("DeviceHealth", Di.Namespaces.OpcUaDi), Variant.From(0));
            wire.Path(machine, counters, wire.Name("OperationCounters", Di.Namespaces.OpcUaDi));
            wire.Child(counters, wire.Name("PowerOnDuration", Di.Namespaces.OpcUaDi), Variant.From(0.0));
            wire.Child(counters, wire.Name("OperationDuration", Di.Namespaces.OpcUaDi),
                Variant.From(double.PositiveInfinity));
            wire.Child(counters, wire.Name("OperationCycleCounter", Di.Namespaces.OpcUaDi),
                Variant.From(ulong.MaxValue));
            await manager.RegisterExistingSessionAsync("press", wire.Session.Object, "Anonymous").ConfigureAwait(false);
            MachineryTools tools = services.GetRequiredService<MachineryTools>();

            JsonElement healthJson = Success(await tools.ReadAsync(
                machine.ToString(), MachineryReadFacet.Health, "press").ConfigureAwait(false));
            JsonElement countersJson = Success(await tools.ReadAsync(
                machine.ToString(), MachineryReadFacet.OperationCounters, "press").ConfigureAwait(false));
            JsonElement absentHealth = Success(await tools.ReadAsync(
                wire.Node("bare").ToString(), MachineryReadFacet.Health, "press").ConfigureAwait(false));

            Assert.That(healthJson.GetProperty("deviceHealth").GetInt32(), Is.Zero);
            Assert.That(healthJson.GetProperty("name").GetString(), Is.EqualTo("NORMAL"));
            Assert.That(absentHealth.GetProperty("deviceHealth").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(countersJson.GetProperty("nodeId").GetString(), Is.EqualTo(counters.ToString()));
            Assert.That(countersJson.GetProperty("powerOnDuration").GetDouble(), Is.Zero);
            Assert.That(countersJson.GetProperty("operationDuration").GetString(), Is.EqualTo("Infinity"));
            JsonElement count = countersJson.GetProperty("operationCycleCounter");
            Assert.That(count.GetProperty("UaType").GetInt32(), Is.EqualTo((int)BuiltInType.UInt64));
            Assert.That(count.GetProperty("Value").GetString(), Is.EqualTo("18446744073709551615"));
        }

        /// <summary>
        /// Machine discovery uses Machines rather than DeviceSet, deduplicates and reports a live page.
        /// </summary>
        [Test]
        public async Task ListMachinesPagesDistinctVendorInstancesFromMachinesAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            var folder = NodeId.Create(
                Machinery.Objects.Machines, Machinery.Namespaces.Machinery, wire.Context.NamespaceUris);
            NodeId type = wire.Node("VendorPressType");
            ReferenceDescription first = wire.Reference(wire.Node("press-a"), "Presse A", type);
            wire.Browse(folder, ReferenceTypeIds.HierarchicalReferences, NodeClass.Object,
            [
                first, first,
                wire.Reference(wire.Node("press-b"), "Presse B", type),
                wire.Reference(wire.Node("press-c"), "Presse C", type)
            ]);
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement page = Success(await services.GetRequiredService<MachineryTools>().ListMachinesAsync(
                1, 1, "shop").ConfigureAwait(false));

            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(wire.Node("press-b").ToString()));
            Assert.That(page.GetProperty("items")[0].GetProperty("typeDefinitionId").GetString(),
                Is.EqualTo(type.ToString()));
            Assert.That(page.GetProperty("items")[0].GetProperty("displayName").GetProperty("locale").GetString(),
                Is.EqualTo("de"));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(page.GetProperty("nextOffset").GetInt32(), Is.EqualTo(2));
            Assert.That(page.GetProperty("consistency").GetString(), Is.EqualTo("live"));
            Assert.That(wire.Browses.Single().NodeId, Is.EqualTo(folder));
            Assert.That(wire.Browses[0].IncludeSubtypes, Is.True);
        }

        /// <summary>
        /// Each organizer is resolved by its actual qualified name without assuming a machine type.
        /// </summary>
        [TestCase(true, "Components")]
        [TestCase(false, "MachineryBuildingBlocks")]
        public async Task ListItemChildrenUsesPublishedOrganizerAsync(bool components, string organizer)
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId machine = wire.Node("press");
            NodeId folder = wire.Node("published-organizer");
            NodeId child = wire.Node("hydraulic-unit");
            wire.Path(machine, folder, wire.Name(organizer, Machinery.Namespaces.Machinery));
            wire.Browse(folder, ReferenceTypeIds.HierarchicalReferences, NodeClass.Object,
                [wire.Reference(child, "Hydraulik", wire.Node("HydraulicUnitType"))]);
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);
            MachineryTools tools = services.GetRequiredService<MachineryTools>();

            JsonElement page = Success(components
                ? await tools.ListComponentsAsync(machine.ToString(), sessionName: "shop").ConfigureAwait(false)
                : await tools.ListBuildingBlocksAsync(machine.ToString(), sessionName: "shop").ConfigureAwait(false));

            Assert.That(page.GetProperty("items")[0].GetProperty("nodeId").GetString(), Is.EqualTo(child.ToString()));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.False);
            Assert.That(page.GetProperty("nextOffset").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(wire.Paths.Single().RelativePath.Elements[0].TargetName,
                Is.EqualTo(wire.Name(organizer, Machinery.Namespaces.Machinery)));
        }

        /// <summary>
        /// Lifetime and equipment reads retain independent limits and nested lifetime identity.
        /// </summary>
        [Test]
        public async Task ListLifetimeAndEquipmentPreserveThresholdsAndNestedIdentityAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId machine = wire.Node("press");
            NodeId counters = wire.Node("lifetimes");
            NodeId remaining = wire.Node("remaining-seal-life");
            wire.Path(machine, counters, wire.Name("LifetimeCounters", Machinery.Namespaces.Machinery));
            QualifiedName remainingName = new("SealLife", wire.VendorIndex);
            wire.Path(counters, remaining, remainingName);
            wire.Value(remaining, Variant.From(12.5));
            wire.Browse(counters, ReferenceTypeIds.HierarchicalReferences, NodeClass.Variable,
                [wire.Reference(remaining, "SealLife", VariableTypeIds.BaseDataVariableType, NodeClass.Variable)]);
            wire.Child(remaining, wire.Name("StartValue", Di.Namespaces.OpcUaDi), Variant.From(100.0));
            wire.Child(remaining, wire.Name("LimitValue", Di.Namespaces.OpcUaDi), Variant.From(0.0));
            wire.Child(remaining, wire.Name("WarningValues", Di.Namespaces.OpcUaDi),
                Variant.From([20.0, double.NaN, double.NegativeInfinity]));
            NodeId equipment = wire.Node("equipment");
            NodeId seal = wire.Node("seal");
            wire.Path(machine, equipment, wire.Name("MachineryEquipment", Machinery.Namespaces.Machinery));
            wire.Browse(equipment, ReferenceTypeIds.HierarchicalReferences, NodeClass.Object,
                [wire.Reference(seal, "Dichtung", ObjectTypeIds.BaseObjectType)]);
            wire.Child(seal, wire.Name("MachineryEquipmentTypeId", Machinery.Namespaces.Machinery),
                Variant.From("urn:equipment:hydraulic-seal"));
            wire.Child(seal, wire.Name("Description", Machinery.Namespaces.Machinery),
                Variant.From(new LocalizedText("de", "Kolbendichtung")));
            wire.Child(seal, wire.Name("SerialNumber", Di.Namespaces.OpcUaDi), Variant.From("SEAL-42"));
            NodeId life = wire.Child(seal, wire.Name("EquipmentLife", Machinery.Namespaces.Machinery),
                Variant.From(8.0));
            wire.Child(life, wire.Name("StartValue", Di.Namespaces.OpcUaDi), Variant.From(80.0));
            wire.Child(life, wire.Name("LimitValue", Di.Namespaces.OpcUaDi), Variant.From(2.0));
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);
            MachineryTools tools = services.GetRequiredService<MachineryTools>();

            JsonElement lifetime = Success(await tools.ListLifetimeCountersAsync(
                machine.ToString(), sessionName: "shop").ConfigureAwait(false)).GetProperty("items")[0];
            JsonElement item = Success(await tools.ListEquipmentAsync(
                machine.ToString(), sessionName: "shop").ConfigureAwait(false)).GetProperty("items")[0];

            Assert.That(lifetime.GetProperty("remaining").GetDouble(), Is.EqualTo(12.5));
            Assert.That(lifetime.GetProperty("startValue").GetDouble(), Is.EqualTo(100));
            Assert.That(lifetime.GetProperty("limitValue").GetDouble(), Is.Zero);
            Assert.That(lifetime.GetProperty("warningValues")[0].GetDouble(), Is.EqualTo(20));
            Assert.That(lifetime.GetProperty("warningValues")[1].GetString(), Is.EqualTo("NaN"));
            Assert.That(lifetime.GetProperty("warningValues")[2].GetString(), Is.EqualTo("-Infinity"));
            Assert.That(item.GetProperty("equipmentTypeId").GetString(), Is.EqualTo("urn:equipment:hydraulic-seal"));
            Assert.That(item.GetProperty("serialNumber").GetString(), Is.EqualTo("SEAL-42"));
            Assert.That(item.GetProperty("description").GetProperty("text").GetString(), Is.EqualTo("Kolbendichtung"));
            Assert.That(item.GetProperty("equipmentLife").GetProperty("nodeId").GetString(),
                Is.EqualTo(life.ToString()));
            Assert.That(item.GetProperty("equipmentLife").GetProperty("remaining").GetDouble(), Is.EqualTo(8));
            Assert.That(item.GetProperty("equipmentLife").GetProperty("startValue").GetDouble(), Is.EqualTo(80));
            Assert.That(item.GetProperty("equipmentLife").GetProperty("limitValue").GetDouble(), Is.EqualTo(2));
        }

        /// <summary>
        /// Process-value discovery accepts vendor subtypes and the read retains units and distinct limits.
        /// </summary>
        [Test]
        public async Task DiscoverAndReadProcessValuePreservesUnitsLimitsAndSignalIdAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId machine = wire.Node("press");
            NodeId process = wire.Node("pressure");
            NodeId type = wire.Node("PressureType");
            wire.Subtype(type, Machinery.ProcessValues.ObjectTypeIds.ProcessValueType);
            wire.Browse(machine, ReferenceTypeIds.HierarchicalReferences, NodeClass.Object,
                [wire.Reference(process, "Pressure", type),
                    wire.Reference(wire.Node("unrelated"), "Folder", ObjectTypeIds.FolderType)]);
            NodeId signal = wire.Child(process, wire.Name("AnalogSignal", PADIM.Namespaces.PADIM), Variant.From(6.25));
            wire.Child(process,
                wire.Name("ProcessValueSetpoint", Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                Variant.From(7.5));
            wire.Child(process, wire.Name("Status", Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                Variant.From((ushort)3));
            wire.Child(process,
                wire.Name("AlarmSuppression", Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                Variant.From((ushort)2));
            wire.Child(signal, wire.Name("LowLowLimit", Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                Variant.From(-2.0));
            wire.Child(signal, wire.Name("LowLimit", Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                Variant.From(-1.0));
            wire.Child(signal, wire.Name("HighLimit", Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                Variant.From(8.0));
            wire.Child(signal, wire.Name("HighHighLimit", Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                Variant.From(9.0));
            wire.Child(signal, wire.Name("PercentageValue", Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                Variant.From(62.5));
            wire.Child(signal, new QualifiedName("EngineeringUnits"), Variant.FromStructure(new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 4342098,
                DisplayName = new LocalizedText("en", "bar"),
                Description = new LocalizedText("en", "bar pressure")
            }));
            wire.Child(signal, new QualifiedName("EURange"),
                Variant.FromStructure(new Range { Low = -5, High = 10 }));
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);
            MachineryTools tools = services.GetRequiredService<MachineryTools>();

            JsonElement page = Success(await tools.ListProcessValuesAsync(
                machine.ToString(), sessionName: "shop").ConfigureAwait(false));
            JsonElement value = Success(await tools.ReadProcessValueAsync(
                process.ToString(), "shop").ConfigureAwait(false));

            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("items")[0].GetProperty("typeDefinitionId").GetString(),
                Is.EqualTo(type.ToString()));
            Assert.That(value.GetProperty("signalNodeId").GetString(), Is.EqualTo(signal.ToString()));
            Assert.That(value.GetProperty("value").GetDouble(), Is.EqualTo(6.25));
            Assert.That(value.GetProperty("setpoint").GetDouble(), Is.EqualTo(7.5));
            Assert.That(value.GetProperty("lowLowLimit").GetDouble(), Is.EqualTo(-2));
            Assert.That(value.GetProperty("lowLimit").GetDouble(), Is.EqualTo(-1));
            Assert.That(value.GetProperty("highLimit").GetDouble(), Is.EqualTo(8));
            Assert.That(value.GetProperty("highHighLimit").GetDouble(), Is.EqualTo(9));
            Assert.That(value.GetProperty("percentageValue").GetDouble(), Is.EqualTo(62.5));
            Assert.That(value.GetProperty("status").GetUInt16(), Is.EqualTo(3));
            Assert.That(value.GetProperty("alarmSuppression").GetUInt16(), Is.EqualTo(2));
            Assert.That(value.GetProperty("engineeringUnits").GetProperty("UnitId").GetInt32(), Is.EqualTo(4342098));
            Assert.That(value.GetProperty("engineeringUnits")
                .GetProperty("DisplayName").GetProperty("Text").GetString(),
                Is.EqualTo("bar"));
            Assert.That(value.GetProperty("euRange").GetProperty("Low").GetDouble(), Is.EqualTo(-5));
            Assert.That(value.GetProperty("euRange").GetProperty("High").GetDouble(), Is.EqualTo(10));
        }

        /// <summary>
        /// The calibration method uses the discovered method ID and retains the actual server refusal.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ZeroPointAdjustmentCallsActualMethodAndPreservesStatusAsync(bool isError)
        {
            StatusCode status = isError ? StatusCodes.BadUserAccessDenied : StatusCodes.Good;
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId process = wire.Node("pressure");
            NodeId method = wire.Node("vendor-adjust-pressure");
            wire.Path(process, method, wire.Name("ZeroPointAdjustment", PADIM.Namespaces.PADIM));
            wire.Session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader?>(), It.Is<ArrayOf<CallMethodRequest>>(requests =>
                        requests.Count == 1 &&
                        requests[0].ObjectId == process &&
                        requests[0].MethodId == method &&
                        requests[0].InputArguments.Count == 0),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = [new CallMethodResult { StatusCode = status }]
                });
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            CallToolResult result = await services.GetRequiredService<MachineryTools>().ZeroPointAdjustmentAsync(
                process.ToString(), "shop").ConfigureAwait(false);

            Assert.That(result.IsError, Is.EqualTo(isError));
            Assert.That(result.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(status.Code));
            wire.Session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// The three energy scopes browse their prescribed parent and reference, not an inferred hierarchy.
        /// </summary>
        [TestCase(MachineryEnergyScope.Resources)]
        [TestCase(MachineryEnergyScope.MeteringPoints)]
        [TestCase(MachineryEnergyScope.SubMeters)]
        public async Task ListEnergyUsesScopeSpecificParentAndReferenceAsync(MachineryEnergyScope scope)
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId target = wire.Node("energy-owner");
            NodeId parent = target;
            NodeId reference = ReferenceTypeIds.HierarchicalReferences;
            if (scope == MachineryEnergyScope.Resources)
            {
                parent = wire.Node("consumption");
                wire.Path(target, parent, wire.Name("Monitoring", Machinery.Namespaces.Machinery),
                    wire.Name("Consumption", Machinery.Namespaces.Machinery));
            }
            else if (scope == MachineryEnergyScope.SubMeters)
            {
                reference = NodeId.Create(Machinery.Energy.ReferenceTypes.Contains,
                    Machinery.Energy.Namespaces.MachineryEnergy, wire.Context.NamespaceUris);
            }
            wire.Browse(parent, reference, NodeClass.Object,
                [wire.Reference(wire.Node("electricity"), "Electricity", ObjectTypeIds.FolderType)]);
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement page = Success(await services.GetRequiredService<MachineryTools>().ListEnergyAsync(
                target.ToString(), scope, sessionName: "shop").ConfigureAwait(false));

            Assert.That(page.GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(wire.Node("electricity").ToString()));
            Assert.That(wire.Browses.Single().NodeId, Is.EqualTo(parent));
            Assert.That(wire.Browses[0].ReferenceTypeId, Is.EqualTo(reference));
            Assert.That(wire.Browses[0].BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
        }

        /// <summary>
        /// Meter reads retain measurement variable IDs and do not substitute bad measurements with zero.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadMeterPreservesTagAndGoodMeasurementOnlyAsync(bool mainOfResource)
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId resource = wire.Node("electricity");
            NodeId meter = wire.Node("main-electricity-meter");
            NodeId power = wire.Node("active-power");
            NodeId unavailable = wire.Node("unavailable-energy");
            wire.Path(resource, meter, wire.Name("Main", Machinery.Energy.Namespaces.MachineryEnergy));
            wire.Child(meter, wire.Name("ApplicationTag", ECM.Namespaces.ECM), Variant.From("Press supply"));
            wire.Browse(meter, ReferenceTypeIds.HierarchicalReferences, NodeClass.Variable,
            [
                wire.Reference(power, "ActivePower", VariableTypeIds.BaseDataVariableType, NodeClass.Variable),
                wire.Reference(unavailable, "Energy", VariableTypeIds.BaseDataVariableType, NodeClass.Variable)
            ]);
            wire.Value(power, Variant.From(12.75));
            wire.Value(unavailable, DataValue.FromStatusCode(StatusCodes.BadOutOfService));
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement json = Success(await services.GetRequiredService<MachineryTools>().ReadMeterAsync(
                (mainOfResource ? resource : meter).ToString(), mainOfResource, "shop").ConfigureAwait(false));

            Assert.That(json.GetProperty("nodeId").GetString(), Is.EqualTo(meter.ToString()));
            Assert.That(json.GetProperty("applicationTag").GetString(), Is.EqualTo("Press supply"));
            Assert.That(json.GetProperty("measurements").GetArrayLength(), Is.EqualTo(1));
            JsonElement measurement = json.GetProperty("measurements")[0];
            Assert.That(measurement.GetProperty("nodeId").GetString(), Is.EqualTo(power.ToString()));
            Assert.That(measurement.GetProperty("value").GetProperty("UaType").GetInt32(),
                Is.EqualTo((int)BuiltInType.Double));
            Assert.That(measurement.GetProperty("value").GetProperty("Value").GetDouble(), Is.EqualTo(12.75));
        }

        /// <summary>
        /// Response queries target the actual response provider and retain a non-success model bitmap.
        /// </summary>
        [Test]
        public async Task ReadJobResponseParametersKeepsActualProviderAndModelRefusalAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId machine = wire.Node("press");
            NodeId management = wire.Node("job-management");
            NodeId receiver = wire.Node("actual-order-control");
            NodeId provider = wire.Node("actual-response-provider");
            wire.Path(machine, management, wire.Name("JobManagement", Machinery.Jobs.Namespaces.MachineryJobs));
            wire.Path(management, receiver, wire.Name("JobOrderControl", Machinery.Jobs.Namespaces.MachineryJobs));
            wire.Path(management, provider, wire.Name("JobOrderResults", Machinery.Jobs.Namespaces.MachineryJobs));
            var calls = new List<CallMethodRequest>();
            JobMcpTestSession.SetupCall(wire.Session, calls,
            [
                Variant.FromStructure(new V2.ISA95JobResponseDataType
                {
                    JobResponseID = "response-42",
                    JobOrderID = "job-42",
                    EncodingMask = (uint)V2.ISA95JobResponseDataTypeFields.JobResponseData,
                    JobResponseData =
                    [
                        new V2.ISA95ParameterDataType { ID = "RunsCompleted", Value = Variant.From(4u) },
                        new V2.ISA95ParameterDataType { ID = "ProducedQuantity", Value = Variant.From(37.5) }
                    ]
                }),
                Variant.From(1UL << 40)
            ]);
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            CallToolResult result = await services.GetRequiredService<MachineryTools>().ReadJobParametersAsync(
                machine.ToString(), "job-42", MachineryJobList.Responses, "shop").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            JsonElement json = result.StructuredContent!.Value;
            Assert.That(json.GetProperty("returnStatus").GetUInt64(), Is.EqualTo(1UL << 40));
            Assert.That(json.GetProperty("parameters").GetProperty("runsCompleted").GetUInt32(), Is.EqualTo(4));
            Assert.That(json.GetProperty("parameters").GetProperty("producedQuantity").GetDouble(), Is.EqualTo(37.5));
            Assert.That(calls.Single().ObjectId, Is.EqualTo(provider));
            Assert.That(calls[0].MethodId, Is.EqualTo(new NodeId(
                V2.Methods.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderID,
                wire.Context.NamespaceUris.GetIndexOrAppend(V2.Namespaces.ISA95JobControlV2))));
            Assert.That(calls[0].InputArguments[0].TryGetValue(out string jobId), Is.True);
            Assert.That(jobId, Is.EqualTo("job-42"));
        }

        /// <summary>
        /// Response projections preserve distinct quantities, non-finite durations and complete custom parameters.
        /// </summary>
        [Test]
        public void ResponseProjectionPreservesQuantitiesAndCustomParameterStructure()
        {
            IServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var value = new MachineryJobResponseParameters
            {
                JobName = [new LocalizedText("fr", "Lot terminé")],
                OrderNumbers = ["ERP-4"],
                Customers = ["Factory"],
                CustomerOrderNumbers = ["PO-8"],
                JobExecutionMode = JobExecutionMode.SimulationMode,
                ReasonForStateChange = new LocalizedText("en", "Completed"),
                RunsStarted = 5,
                RunsCompleted = 4,
                ActualQuantityCurrentRun = 2.5,
                ActualUnitBusyTime = 125,
                ActualUnitSetupTime = 10,
                ActualUnitDelayTime = 15,
                ActualProductionTime = 100,
                ProducedQuantity = 42,
                EstimatedRemainingTime = double.NaN,
                JobResult = JobResult.Unsuccessful,
                GoodQuantity = 40,
                Parameters =
                [
                    new V2.ISA95ParameterDataType
                    {
                        ID = "vendor-quality",
                        Value = Variant.From((short)7),
                        EncodingMask = (uint)V2.ISA95ParameterDataTypeFields.Subparameters,
                        Subparameters =
                        [
                            new V2.ISA95ParameterDataType { ID = "accepted", Value = Variant.From(false) }
                        ]
                    }
                ]
            };

            JsonObject json = MachineryJson.ResponseParameters(value, context);

            Assert.That(json["jobName"]![0]!["locale"]!.GetValue<string>(), Is.EqualTo("fr"));
            Assert.That(json["orderNumbers"]![0]!.GetValue<string>(), Is.EqualTo("ERP-4"));
            Assert.That(json["customers"]![0]!.GetValue<string>(), Is.EqualTo("Factory"));
            Assert.That(json["customerOrderNumbers"]![0]!.GetValue<string>(), Is.EqualTo("PO-8"));
            Assert.That(json["jobExecutionMode"]!.GetValue<int>(), Is.EqualTo((int)JobExecutionMode.SimulationMode));
            Assert.That(json["reasonForStateChange"]!["text"]!.GetValue<string>(), Is.EqualTo("Completed"));
            Assert.That(json["runsStarted"]!.GetValue<uint>(), Is.EqualTo(5));
            Assert.That(json["runsCompleted"]!.GetValue<uint>(), Is.EqualTo(4));
            Assert.That(json["actualQuantityCurrentRun"]!.GetValue<double>(), Is.EqualTo(2.5));
            Assert.That(json["actualUnitBusyTime"]!.GetValue<double>(), Is.EqualTo(125));
            Assert.That(json["actualUnitSetupTime"]!.GetValue<double>(), Is.EqualTo(10));
            Assert.That(json["actualUnitDelayTime"]!.GetValue<double>(), Is.EqualTo(15));
            Assert.That(json["actualProductionTime"]!.GetValue<double>(), Is.EqualTo(100));
            Assert.That(json["producedQuantity"]!.GetValue<double>(), Is.EqualTo(42));
            Assert.That(json["estimatedRemainingTime"]!.GetValue<string>(), Is.EqualTo("NaN"));
            Assert.That(json["jobResult"]!.GetValue<int>(), Is.EqualTo((int)JobResult.Unsuccessful));
            Assert.That(json["goodQuantity"]!.GetValue<double>(), Is.EqualTo(40));
            Assert.That(json["parameters"]![0]!["ID"]!.GetValue<string>(), Is.EqualTo("vendor-quality"));
            Assert.That(json["parameters"]![0]!["Value"]!["UaType"]!.GetValue<int>(),
                Is.EqualTo((int)BuiltInType.Int16));
            Assert.That(json["parameters"]![0]!["Value"]!["Value"]!.GetValue<short>(), Is.EqualTo(7));
            Assert.That(json["parameters"]![0]!["Subparameters"]![0]!["Value"]!["Value"]!.GetValue<bool>(), Is.False);
        }

        /// <summary>
        /// Nested finite-state snapshots keep each machine's own state, quality and transition identity.
        /// </summary>
        [Test]
        public void StateProjectionPreservesNestedMachineAndMissingOptionals()
        {
            var child = new FiniteStateSnapshot(new NodeId("sub", 2), new LocalizedText("en", "Paused"),
                new NodeId("paused", 2), LocalizedText.Null, NodeId.Null,
                new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc), StatusCodes.Uncertain);
            var parent = new FiniteStateSnapshot(new NodeId("outer", 2), new LocalizedText("en", "Processing"),
                new NodeId("processing", 2), new LocalizedText("en", "Start"), new NodeId("started", 2),
                new DateTime(2026, 5, 6, 7, 8, 0, DateTimeKind.Utc), StatusCodes.Good)
            {
                SubMachine = child
            };

            JsonObject json = MachineryJson.State(parent)!;

            Assert.That(json["stateMachineId"]!.GetValue<string>(), Is.EqualTo("ns=2;s=outer"));
            Assert.That(json["lastTransitionId"]!.GetValue<string>(), Is.EqualTo("ns=2;s=started"));
            Assert.That(json["subMachine"]!["stateMachineId"]!.GetValue<string>(), Is.EqualTo("ns=2;s=sub"));
            Assert.That(json["subMachine"]!["currentState"]!["text"]!.GetValue<string>(), Is.EqualTo("Paused"));
            Assert.That(json["subMachine"]!["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.Uncertain.Code));
            Assert.That(json["subMachine"]!["timestamp"]!.GetValue<string>(),
                Is.EqualTo("2026-05-06T07:08:09.0000000Z"));
            Assert.That(json["subMachine"]!["lastTransition"], Is.Null);
            Assert.That(json["subMachine"]!["subMachine"], Is.Null);
            Assert.That(MachineryJson.Identification(null), Is.Null);
            Assert.That(MachineryJson.Lifetime(null), Is.Null);
        }

        /// <summary>
        /// Event projections retain the event identity and the model-specific payload rather than a summary.
        /// </summary>
        [Test]
        public void EventProjectionsPreservePayloadStatusAndTimestamps()
        {
            IServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var result = new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType
                {
                    ResultId = "inspection-42",
                    JobId = "job-42",
                    EncodingMask = (uint)ResultMetaDataTypeFields.JobId
                },
                ResultContent = [Variant.From(12.5)]
            };
            var notification = new ResultReadyEventTypeRecord
            {
                EventId = new ByteString(new byte[] { 1, 2, 3 }),
                EventType = new NodeId("result-event-type", 2),
                SourceNode = new NodeId("inspection", 2),
                SourceName = "Inspection",
                Time = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc),
                ReceiveTime = new DateTime(2026, 4, 5, 6, 7, 9, DateTimeKind.Utc),
                Message = new LocalizedText("de", "Ergebnis bereit"),
                Severity = 240,
                Result = result
            };

            JsonObject json = MachineryJson.ResultEvent(notification, context);
            JsonObject adjustment = MachineryJson.AdjustmentEvent(new ZeroPointAdjustmentEventTypeRecord
            {
                SourceNode = new NodeId("pressure", 2),
                ZeroPointAdjustmentResult = StatusCodes.BadUserAccessDenied
            });

            Assert.That(json["eventId"]!.GetValue<string>(), Is.EqualTo("AQID"));
            Assert.That(json["sourceNode"]!.GetValue<string>(), Is.EqualTo("ns=2;s=inspection"));
            Assert.That(json["sourceName"]!.GetValue<string>(), Is.EqualTo("Inspection"));
            Assert.That(json["eventType"]!.GetValue<string>(), Is.EqualTo("ns=2;s=result-event-type"));
            Assert.That(json["time"]!.GetValue<string>(), Is.EqualTo("2026-04-05T06:07:08.0000000Z"));
            Assert.That(json["receiveTime"]!.GetValue<string>(), Is.EqualTo("2026-04-05T06:07:09.0000000Z"));
            Assert.That(json["message"]!["locale"]!.GetValue<string>(), Is.EqualTo("de"));
            Assert.That(json["severity"]!.GetValue<ushort>(), Is.EqualTo(240));
            Assert.That(json["result"]!["ResultMetaData"]!["ResultId"]!.GetValue<string>(),
                Is.EqualTo("inspection-42"));
            Assert.That(json["result"]!["ResultContent"]![0]!["Value"]!.GetValue<double>(), Is.EqualTo(12.5));
            Assert.That(adjustment["statusCodeValue"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(adjustment["eventId"], Is.Null);
            Assert.That(adjustment["message"], Is.Null);
            Assert.That(MachineryJson.Result(null, true, context), Is.Null);
            JsonNode metadata = MachineryJson.Result(result, true, context)!;
            Assert.That(metadata["JobId"]!.GetValue<string>(), Is.EqualTo("job-42"));
            Assert.That(metadata.AsObject().ContainsKey("ResultContent"), Is.False);
        }

        /// <summary>
        /// Finite discriminators are rejected on a connected session before any control call.
        /// </summary>
        [Test]
        public async Task UnsupportedFacetsAndMissingCalibrationDoNotInvokeMethodsAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            await manager.RegisterExistingSessionAsync("shop", wire.Session.Object, "Anonymous").ConfigureAwait(false);
            MachineryTools tools = services.GetRequiredService<MachineryTools>();
            string node = wire.Node("press").ToString();

            CallToolResult facet = await tools.ReadAsync(node, (MachineryReadFacet)99, "shop").ConfigureAwait(false);
            CallToolResult energy = await tools.ListEnergyAsync(
                node, (MachineryEnergyScope)99, sessionName: "shop").ConfigureAwait(false);
            CallToolResult calibration = await tools.ZeroPointAdjustmentAsync(node, "shop").ConfigureAwait(false);

            Assert.That(facet.IsError, Is.True);
            Assert.That(facet.StructuredContent!.Value.GetProperty("errorType").GetString(),
                Is.EqualTo(nameof(ArgumentOutOfRangeException)));
            Assert.That(energy.IsError, Is.True);
            Assert.That(energy.StructuredContent!.Value.GetProperty("errorType").GetString(),
                Is.EqualTo(nameof(ArgumentOutOfRangeException)));
            Assert.That(calibration.IsError, Is.True);
            Assert.That(calibration.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadNotFound.Code));
            wire.Session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static JsonElement Success(CallToolResult result)
        {
            Assert.That(result.IsError, Is.False, result.StructuredContent?.ToString());
            Assert.That(result.StructuredContent.HasValue, Is.True);
            return result.StructuredContent!.Value;
        }
    }
}
#endif
