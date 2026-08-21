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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Address-space compliance regression coverage for the pump device
    /// integration server. Each test pins one requirement that a live
    /// compliance audit of the sample found violated, so the behaviour
    /// cannot silently regress.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("Compliance")]
    [NonParallelizable]
    public sealed class PumpAddressSpaceComplianceTests
    {
        /// <summary>
        /// OPC 10000-5 requires the <c>Namespaces</c> Object to describe the
        /// namespaces a Server provides, and every companion specification
        /// repeats the requirement for its own namespace. Clients use the
        /// published version and publication date to validate cached models,
        /// so every entry of the NamespaceArray must be described.
        /// </summary>
        [Test]
        public async Task EveryNamespaceIsDescribedByNamespaceMetadataAsync()
        {
            await RunServerAsync(async server =>
            {
                IReadOnlyList<ReferenceDescription> references = await BrowseAsync(
                    server,
                    Opc.Ua.ObjectIds.Server_Namespaces).ConfigureAwait(false);

                var described = new HashSet<string>(StringComparer.Ordinal);
                foreach (ReferenceDescription reference in references)
                {
                    if (reference.NodeClass == NodeClass.Object)
                    {
                        described.Add(reference.BrowseName.Name ?? string.Empty);
                    }
                }

                string[] namespaceUris = server.NamespaceUris.ToArray();
                var missing = namespaceUris.Where(uri => !described.Contains(uri)).ToList();

                Assert.That(
                    missing,
                    Is.Empty,
                    "NamespaceMetadata is missing for: " + string.Join(", ", missing));
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task NamespaceMetadataPublishesModelVersionAndPublicationDateAsync()
        {
            await RunServerAsync(async server =>
            {
                string[] expectedModelNamespaces =
                [
                    Opc.Ua.Di.Namespaces.OpcUaDi,
                    global::Opc.Ua.Machinery.Namespaces.Machinery,
                    global::Opc.Ua.Pumps.Namespaces.Pumps
                ];

                foreach (string namespaceUri in expectedModelNamespaces)
                {
                    NodeId metadata = await ResolveAsync(
                        server,
                        Opc.Ua.ObjectIds.Server_Namespaces,
                        [namespaceUri]).ConfigureAwait(false);
                    Assert.That(metadata.IsNull, Is.False, "NamespaceMetadata was not found for " + namespaceUri);

                    DataValue version = await ReadChildValueAsync(
                        server,
                        metadata,
                        "NamespaceVersion").ConfigureAwait(false);
                    DataValue publicationDate = await ReadChildValueAsync(
                        server,
                        metadata,
                        "NamespacePublicationDate").ConfigureAwait(false);

                    Assert.That(ValueToText(version.WrappedValue), Is.Not.Empty);
                    Assert.That(
                        publicationDate.WrappedValue.TryGetValue(out DateTimeUtc published),
                        Is.True,
                        "NamespacePublicationDate was not a DateTime for " + namespaceUri);
                    Assert.That(
                        published.ToDateTime(),
                        Is.GreaterThan(DateTime.MinValue),
                        "NamespacePublicationDate was not populated for " + namespaceUri);
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task OverTempAlarmIsBrowsableFromPumpEventsObjectAsync()
        {
            await RunServerAsync(async server =>
            {
                NodeId events = await ResolveAsync(
                    server,
                    PumpNodeId(server, "Pump_1"),
                    ["Events"]).ConfigureAwait(false);
                Assert.That(events.IsNull, Is.False, "Events object was not found.");

                IReadOnlyList<ReferenceDescription> references = await BrowseAsync(
                    server,
                    events,
                    Opc.Ua.Types.ReferenceTypeIds.References,
                    BrowseDirection.Both).ConfigureAwait(false);

                Assert.That(
                    references,
                    Has.Some.Matches<ReferenceDescription>(reference =>
                        reference.BrowseName.Name == "OverTempAlarm" &&
                        ExpandedNodeId.ToNodeId(reference.NodeId, server.NamespaceUris).NamespaceIndex ==
                        server.NamespaceUris.GetIndex("urn:localhost:" + nameof(PumpAddressSpaceComplianceTests))),
                    "OverTempAlarm was not browsable from Pump_1/Events through References/Both.");
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task AlarmSourceIsEventSubscribableFromServerNotifierPathAsync()
        {
            await RunServerAsync(async server =>
            {
                NodeId pump = PumpNodeId(server, "Pump_1");
                NodeId events = await ResolveAsync(server, pump, ["Events"]).ConfigureAwait(false);
                NodeId alarm = await ResolveAsync(server, events, ["OverTempAlarm"]).ConfigureAwait(false);
                Assert.That(events.IsNull, Is.False, "Events object was not found.");
                Assert.That(alarm.IsNull, Is.False, "OverTempAlarm was not found.");

                await AssertSubscribeToEventsAsync(server, pump).ConfigureAwait(false);
                await AssertSubscribeToEventsAsync(server, events).ConfigureAwait(false);

                IReadOnlyList<ReferenceDescription> notifiers = await BrowseAsync(
                    server,
                    Opc.Ua.ObjectIds.Server,
                    Opc.Ua.Types.ReferenceTypeIds.HasNotifier).ConfigureAwait(false);
                Assert.That(
                    notifiers.Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, server.NamespaceUris)),
                    Does.Contain(events),
                    "Server did not expose a HasNotifier path to the pump Events object.");

                await AssertAlarmEventReceivedThroughClientSubscriptionAsync(server, alarm).ConfigureAwait(false);
            }, simulationInterval: TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        }

        /// <summary>
        /// The Pumps NodeSet declares <c>AccessLevel="5"</c>
        /// (CurrentRead | HistoryRead) on the supervision booleans. The
        /// source generator must preserve the combination instead of
        /// collapsing it onto the single-valued ModelDesign AccessLevel
        /// enumeration.
        /// </summary>
        [Test]
        public async Task SupervisionBooleansKeepHistoryReadAccessLevelAsync()
        {
            await RunServerAsync(async server =>
            {
                NodeId cavitation = await ResolveAsync(
                    server,
                    PumpNodeId(server, "Pump_1"),
                    ["Events", "SupervisionProcessFluid", "Cavitation"]).ConfigureAwait(false);

                Assert.That(cavitation.IsNull, Is.False, "Cavitation was not found.");

                DataValue accessLevel = await ReadAttributeAsync(
                    server,
                    cavitation,
                    Attributes.AccessLevel).ConfigureAwait(false);

                Assert.That(accessLevel.WrappedValue.TryGetValue(out byte level), Is.True);
                Assert.That(
                    level & AccessLevels.HistoryRead,
                    Is.EqualTo(AccessLevels.HistoryRead),
                    "The HistoryRead bit declared by the Pumps NodeSet was dropped.");
                Assert.That(
                    level & AccessLevels.CurrentRead,
                    Is.EqualTo(AccessLevels.CurrentRead));
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task DiConformanceFacetsMergeWithStandardProfileAsync()
        {
            await RunServerAsync(async server =>
            {
                DataValue profilesValue = await ReadAttributeAsync(
                    server,
                    global::Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray,
                    Attributes.Value).ConfigureAwait(false);
                DataValue unitsValue = await ReadAttributeAsync(
                    server,
                    global::Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits,
                    Attributes.Value).ConfigureAwait(false);

                Assert.That(TryGetStringArray(profilesValue.WrappedValue, out List<string> profiles), Is.True);
                Assert.That(
                    profiles,
                    Does.Contain("http://opcfoundation.org/UA-Profile/Server/StandardUA2017"));
                Assert.That(
                    profiles,
                    Does.Contain("http://opcfoundation.org/UA-Profile/DI/Server/DeviceIntegrationHost"));

                Assert.That(
                    TryGetQualifiedNameArray(unitsValue.WrappedValue, out List<QualifiedName> units),
                    Is.True);
                Assert.That(units.Select(unit => unit.Name), Does.Contain("DI DeviceTopology"));
                Assert.That(units.Select(unit => unit.Name), Does.Contain("DI Offline"));
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task RuntimeInstancesUseServerNamespaceAndSpecBrowseNamesStayStandardAsync()
        {
            await RunServerAsync(async server =>
            {
                ushort instanceNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                    "urn:localhost:" + nameof(PumpAddressSpaceComplianceTests));
                ushort diNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                    Opc.Ua.Di.Namespaces.OpcUaDi);
                ushort pumpsNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                    global::Opc.Ua.Pumps.Namespaces.Pumps);
                ushort machineryNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                    global::Opc.Ua.Machinery.Namespaces.Machinery);

                List<ReferenceDescription> reachable = await BrowseReachableAsync(
                    server,
                    DeviceSetNodeId(server)).ConfigureAwait(false);

                Assert.That(
                    reachable,
                    Has.None.Matches<ReferenceDescription>(reference =>
                    {
                        NodeId nodeId = ExpandedNodeId.ToNodeId(
                            reference.NodeId,
                            server.NamespaceUris);
                        return nodeId.IdType == IdType.String &&
                            (nodeId.NamespaceIndex == diNamespaceIndex ||
                            nodeId.NamespaceIndex == pumpsNamespaceIndex ||
                            nodeId.NamespaceIndex == machineryNamespaceIndex);
                    }),
                    "Runtime-created instances under DeviceSet must not use companion-spec NodeId namespaces.");

                NodeId pump1 = PumpNodeId(server, "Pump_1");
                NodeId pump2 = PumpNodeId(server, "Pump_2");
                Assert.That(pump1.NamespaceIndex, Is.EqualTo(instanceNamespaceIndex));
                Assert.That(pump2.NamespaceIndex, Is.EqualTo(instanceNamespaceIndex));

                await AssertBrowseNameAsync(
                    server,
                    pump1,
                    ["Identification"],
                    new QualifiedName("Identification", diNamespaceIndex)).ConfigureAwait(false);
                await AssertBrowseNameAsync(
                    server,
                    pump1,
                    ["Operational"],
                    new QualifiedName("Operational", diNamespaceIndex)).ConfigureAwait(false);
                await AssertBrowseNameAsync(
                    server,
                    pump1,
                    ["Events"],
                    new QualifiedName("Events", pumpsNamespaceIndex)).ConfigureAwait(false);

                await AssertBrowseNameAsync(
                    server,
                    pump2,
                    ["Diagnostics"],
                    new QualifiedName("Diagnostics", instanceNamespaceIndex)).ConfigureAwait(false);
                await AssertBrowseNameAsync(
                    server,
                    pump2,
                    ["Diagnostics", "LastError"],
                    new QualifiedName("LastError", instanceNamespaceIndex)).ConfigureAwait(false);
                await AssertBrowseNameAsync(
                    server,
                    pump2,
                    ["Diagnostics", "ErrorCount"],
                    new QualifiedName("ErrorCount", instanceNamespaceIndex)).ConfigureAwait(false);
                await AssertBrowseNameAsync(
                    server,
                    pump2,
                    ["Diagnostics", "LastSelfTest"],
                    new QualifiedName("LastSelfTest", instanceNamespaceIndex)).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// OPC 10000-3 defines <c>Historizing</c> as "the Server is actively
        /// collecting data for the history of the Node". Advertising history
        /// that the server cannot serve is what the audit found: the pump
        /// exposed <c>Historizing = true</c> while <c>HistoryRead</c> returned
        /// <c>BadHistoryOperationUnsupported</c>. Whatever route is taken —
        /// clearing the advertisement or wiring a historian — the advertised
        /// capability and the actual behaviour must agree.
        /// </summary>
        [Test]
        public async Task HistoryAdvertisementMatchesActualHistorySupportAsync()
        {
            await RunServerAsync(async server =>
            {
                NodeId cavitation = await ResolveAsync(
                    server,
                    PumpNodeId(server, "Pump_1"),
                    ["Events", "SupervisionProcessFluid", "Cavitation"]).ConfigureAwait(false);

                Assert.That(cavitation.IsNull, Is.False, "Cavitation was not found.");

                DataValue accessLevelValue = await ReadAttributeAsync(
                    server,
                    cavitation,
                    Attributes.AccessLevel).ConfigureAwait(false);
                DataValue historizingValue = await ReadAttributeAsync(
                    server,
                    cavitation,
                    Attributes.Historizing).ConfigureAwait(false);

                Assert.That(accessLevelValue.WrappedValue.TryGetValue(out byte accessLevel), Is.True);
                Assert.That(historizingValue.WrappedValue.TryGetValue(out bool historizing), Is.True);

                bool advertisesHistory =
                    historizing || (accessLevel & AccessLevels.HistoryRead) != 0;

                StatusCode historyStatus = await HistoryReadAsync(server, cavitation)
                    .ConfigureAwait(false);

                if (advertisesHistory)
                {
                    Assert.That(
                        historyStatus.Code,
                        Is.Not.EqualTo(StatusCodes.BadHistoryOperationUnsupported),
                        "Node " + cavitation + " advertises history (Historizing=" + historizing +
                        ", AccessLevel=0x" + accessLevel.ToString("X2", CultureInfo.InvariantCulture) +
                        ") but HistoryRead reports the operation is unsupported.");
                }
                else
                {
                    Assert.That(
                        historizing,
                        Is.False,
                        "Historizing must be false when no history is served.");
                }
            }).ConfigureAwait(false);
        }

        [Test]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public async Task ConfiguredPumpCountCreatesDistinctLivePumpsAsync(int pumpCount)
        {
            await RunServerAsync(async server =>
            {
                Assert.That(
                    await WaitForAsync(
                        () => AllPumpMeasurementsAreGoodAsync(server, pumpCount),
                        TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                    Is.True,
                    "Pump measurements did not become live.");

                IReadOnlyList<ReferenceDescription> deviceSetChildren = await BrowseAsync(
                    server,
                    DeviceSetNodeId(server),
                    Opc.Ua.Types.ReferenceTypeIds.Organizes).ConfigureAwait(false);
                List<ReferenceDescription> pumps = deviceSetChildren
                    .Where(reference => reference.BrowseName.Name != null &&
                        reference.BrowseName.Name.StartsWith("Pump_", StringComparison.Ordinal))
                    .ToList();

                Assert.That(pumps, Has.Count.EqualTo(pumpCount));

                var pressureValues = new List<double>();
                for (int pumpNumber = 1; pumpNumber <= pumpCount; pumpNumber++)
                {
                    DataValue value = await ReadPumpMeasurementAsync(
                        server,
                        pumpNumber,
                        "DifferentialPressure").ConfigureAwait(false);

                    Assert.That(value.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.TryGetValue(out double pressure), Is.True);
                    pressureValues.Add(pressure);
                }

                if (pumpCount > 1)
                {
                    Assert.That(
                        pressureValues.Distinct().Count(),
                        Is.GreaterThan(1),
                        "Per-pump phase offsets should make live values distinct.");
                }
            }, pumpCount: pumpCount, simulationInterval: TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        [Test]
        public async Task EveryPumpHasMandatoryIdentificationValuesAsync()
        {
            await RunServerAsync(async server =>
            {
                for (int pumpNumber = 1; pumpNumber <= 4; pumpNumber++)
                {
                    NodeId identification = await ResolveAsync(
                        server,
                        PumpNodeId(server, PumpBrowseName(pumpNumber)),
                        ["Identification"]).ConfigureAwait(false);

                    await AssertNonEmptyValueAsync(server, identification, "Manufacturer")
                        .ConfigureAwait(false);
                    await AssertNonEmptyValueAsync(server, identification, "SerialNumber")
                        .ConfigureAwait(false);
                    await AssertNonEmptyValueAsync(server, identification, "ProductInstanceUri")
                        .ConfigureAwait(false);
                }
            }, pumpCount: 4).ConfigureAwait(false);
        }

        [Test]
        public async Task SupervisionBooleansExposeStateLabelsAsync()
        {
            await RunServerAsync(async server =>
            {
                for (int pumpNumber = 1; pumpNumber <= 2; pumpNumber++)
                {
                    NodeId pump = PumpNodeId(server, PumpBrowseName(pumpNumber));
                    await AssertStateLabelsAsync(
                        server,
                        pump,
                        ["Events", "SupervisionProcessFluid", "Cavitation"]).ConfigureAwait(false);
                    await AssertStateLabelsAsync(
                        server,
                        pump,
                        ["Events", "SupervisionPumpOperation", "MotorOverheat"]).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task PumpsAreReachableFromMachinesFolderByBrowseAsync()
        {
            await RunServerAsync(async server =>
            {
                NodeId machines = await ResolveAsync(
                    server,
                    Opc.Ua.ObjectIds.ObjectsFolder,
                    ["Machines"]).ConfigureAwait(false);
                Assert.That(machines.IsNull, Is.False, "Machines folder was not found.");

                IReadOnlyList<ReferenceDescription> references = await BrowseAsync(
                    server,
                    machines,
                    Opc.Ua.Types.ReferenceTypeIds.Organizes).ConfigureAwait(false);
                var organized = new HashSet<NodeId>(references.Select(
                    reference => ExpandedNodeId.ToNodeId(reference.NodeId, server.NamespaceUris)));

                Assert.That(organized, Does.Contain(PumpNodeId(server, "Pump_1")));
                Assert.That(organized, Does.Contain(PumpNodeId(server, "Pump_2")));
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task PumpBrowseNamesAvoidRelativePathEscapingCharactersAsync()
        {
            await RunServerAsync(async server =>
            {
                IReadOnlyList<ReferenceDescription> references = await BrowseAsync(
                    server,
                    DeviceSetNodeId(server),
                    Opc.Ua.Types.ReferenceTypeIds.Organizes).ConfigureAwait(false);

                foreach (ReferenceDescription reference in references.Where(
                    reference => reference.BrowseName.Name?.StartsWith("Pump", StringComparison.Ordinal) == true))
                {
                    Assert.That(reference.BrowseName.Name, Does.Not.Contain(" "));
                    Assert.That(reference.BrowseName.Name, Does.Not.Contain("#"));

                    NodeId pump = ExpandedNodeId.ToNodeId(reference.NodeId, server.NamespaceUris);
                    DataValue displayName = await ReadAttributeAsync(
                        server,
                        pump,
                        Attributes.DisplayName).ConfigureAwait(false);

                    Assert.That(displayName.WrappedValue.TryGetValue(out LocalizedText label), Is.True);
                    Assert.That(label.Text, Does.Contain("Pump #"));
                }
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task MaintenanceGroupIsPopulatedWhenMaterialisedAsync()
        {
            await RunServerAsync(async server =>
            {
                NodeId maintenance = await ResolveAsync(
                    server,
                    PumpNodeId(server, "Pump_1"),
                    ["Maintenance"]).ConfigureAwait(false);

                if (maintenance.IsNull)
                {
                    return;
                }

                IReadOnlyList<ReferenceDescription> references = await BrowseHierarchicalAsync(
                    server,
                    maintenance).ConfigureAwait(false);
                Assert.That(
                    references,
                    Has.Some.Matches<ReferenceDescription>(reference =>
                        reference.BrowseName.Name == "GeneralMaintenance"),
                    "Maintenance was materialised but did not expose a concrete maintenance child.");
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task MeasurementsReportInitialDataStatusUntilFirstSimulationTickAsync()
        {
            await RunServerAsync(async server =>
            {
                DataValue initial = await ReadPumpMeasurementAsync(
                    server,
                    1,
                    "DifferentialPressure").ConfigureAwait(false);

                Assert.That(
                    initial.StatusCode.Code,
                    Is.AnyOf(StatusCodes.BadWaitingForInitialData, StatusCodes.UncertainInitialValue));
            }, simulationInterval: TimeSpan.FromDays(1)).ConfigureAwait(false);

            await RunServerAsync(async server =>
            {
                Assert.That(
                    await WaitForAsync(
                        async () =>
                        {
                            DataValue value = await ReadPumpMeasurementAsync(
                                server,
                                1,
                                "DifferentialPressure").ConfigureAwait(false);
                            return value.StatusCode.Code == StatusCodes.Good;
                        },
                        TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                    Is.True,
                    "Measurement did not become Good after the first simulation tick.");
            }, simulationInterval: TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        private static async Task<StatusCode> HistoryReadAsync(
            IServerInternal server,
            NodeId nodeId)
        {
            var details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                StartTime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow,
                NumValuesPerNode = 10,
                ReturnBounds = false
            };

            ArrayOf<HistoryReadValueId> nodesToRead =
            [
                new HistoryReadValueId { NodeId = nodeId }
            ];

            using var context = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryRead,
                RequestLifetime.None);

            (ArrayOf<HistoryReadResult> results, _) = await server.NodeManager.HistoryReadAsync(
                context,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
                false,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            return results[0].StatusCode;
        }

        private static NodeId PumpNodeId(IServerInternal server, string pumpName)
        {
            NodeId deviceSetId = DeviceSetNodeId(server);
            ushort instanceNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                "urn:localhost:" + nameof(PumpAddressSpaceComplianceTests));
            return new NodeId(
                deviceSetId.IdentifierAsString + "_" + pumpName,
                instanceNamespaceIndex);
        }

        private static string PumpBrowseName(int pumpNumber)
        {
            return "Pump_" + pumpNumber.ToString(CultureInfo.InvariantCulture);
        }

        private static NodeId DeviceSetNodeId(IServerInternal server)
        {
            ushort diNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                Opc.Ua.Di.Namespaces.OpcUaDi);
            return new NodeId(Opc.Ua.Di.Objects.DeviceSet, diNamespaceIndex);
        }

        /// <summary>
        /// Walks the supplied browse-name path from <paramref name="startNodeId"/>
        /// matching on browse name only, so the test does not have to spell
        /// out the namespace of every segment.
        /// </summary>
        private static async Task<NodeId> ResolveAsync(
            IServerInternal server,
            NodeId startNodeId,
            IReadOnlyList<string> path)
        {
            NodeId current = startNodeId;
            foreach (string segment in path)
            {
                IReadOnlyList<ReferenceDescription> references = await BrowseAsync(
                    server,
                    current).ConfigureAwait(false);

                ReferenceDescription? match = references.FirstOrDefault(
                    reference => reference.BrowseName.Name == segment);
                if (match == null)
                {
                    return NodeId.Null;
                }

                current = ExpandedNodeId.ToNodeId(match.NodeId, server.NamespaceUris);
            }
            return current;
        }

        private static Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
            IServerInternal server,
            NodeId nodeId)
        {
            return BrowseAsync(
                server,
                nodeId,
                Opc.Ua.Types.ReferenceTypeIds.References);
        }

        private static Task<IReadOnlyList<ReferenceDescription>> BrowseHierarchicalAsync(
            IServerInternal server,
            NodeId nodeId)
        {
            return BrowseAsync(
                server,
                nodeId,
                Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences);
        }

        private static Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
            IServerInternal server,
            NodeId nodeId,
            NodeId referenceTypeId)
        {
            return BrowseAsync(
                server,
                nodeId,
                referenceTypeId,
                BrowseDirection.Forward);
        }

        private static async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
            IServerInternal server,
            NodeId nodeId,
            NodeId referenceTypeId,
            BrowseDirection browseDirection)
        {
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = browseDirection,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];

            using var context = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Browse,
                RequestLifetime.None);

            (ArrayOf<BrowseResult> results, _) = await server.NodeManager.BrowseAsync(
                context,
                new ViewDescription(),
                0,
                nodesToBrowse,
                CancellationToken.None).ConfigureAwait(false);

            var references = new List<ReferenceDescription>();
            for (int ii = 0; ii < results[0].References.Count; ii++)
            {
                references.Add(results[0].References[ii]);
            }
            return references;
        }

        private static async Task<DataValue> ReadChildValueAsync(
            IServerInternal server,
            NodeId parent,
            string browseName)
        {
            NodeId child = await ResolveAsync(server, parent, [browseName]).ConfigureAwait(false);
            Assert.That(child.IsNull, Is.False, browseName + " was not found.");
            return await ReadAttributeAsync(server, child, Attributes.Value).ConfigureAwait(false);
        }

        private static async Task AssertSubscribeToEventsAsync(
            IServerInternal server,
            NodeId nodeId)
        {
            DataValue value = await ReadAttributeAsync(
                server,
                nodeId,
                Attributes.EventNotifier).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out byte notifier), Is.True);
            Assert.That(
                notifier & EventNotifiers.SubscribeToEvents,
                Is.EqualTo(EventNotifiers.SubscribeToEvents),
                nodeId + " does not allow event subscriptions.");
        }

        private static async Task AssertAlarmEventReceivedThroughClientSubscriptionAsync(
            IServerInternal server,
            NodeId alarmNodeId)
        {
            await using var clientFixture = new ClientFixture(server.Telemetry);
            string clientRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(PumpAddressSpaceComplianceTests),
                "client",
                Guid.NewGuid().ToString("N"));
            await clientFixture.LoadClientConfigurationAsync(clientRoot).ConfigureAwait(false);
            clientFixture.OperationTimeout = 15_000;

            Opc.Ua.Client.ISession session = await clientFixture.ConnectAsync(
                server.EndpointAddresses.First().ToString()).ConfigureAwait(false);
            try
            {
                CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                    null,
                    50,
                    1000,
                    100,
                    0,
                    true,
                    0,
                    CancellationToken.None).ConfigureAwait(false);

                try
                {
                    CreateMonitoredItemsResponse monitoredItems = await session.CreateMonitoredItemsAsync(
                        null,
                        subscription.SubscriptionId,
                        TimestampsToReturn.Both,
                        new MonitoredItemCreateRequest[]
                        {
                            new()
                            {
                                ItemToMonitor = new ReadValueId
                                {
                                    NodeId = Opc.Ua.ObjectIds.Server,
                                    AttributeId = Attributes.EventNotifier
                                },
                                MonitoringMode = MonitoringMode.Reporting,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 1,
                                    SamplingInterval = 0,
                                    Filter = new ExtensionObject(CreateAlarmEventFilter()),
                                    QueueSize = 100,
                                    DiscardOldest = true
                                }
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(monitoredItems.Results, Has.Count.EqualTo(1));
                    Assert.That(
                        StatusCode.IsGood(monitoredItems.Results[0].StatusCode),
                        Is.True,
                        "Event monitored item creation failed: " + monitoredItems.Results[0].StatusCode);

                    AlarmClient alarmClient = session.GetAlarmClient(server.Telemetry);
                    await alarmClient.ConditionRefreshAsync(
                        subscription.SubscriptionId,
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(
                        await WaitForAlarmEventAsync(
                            session,
                            subscription.SubscriptionId,
                            alarmNodeId,
                            TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                        Is.True,
                        "No OverTempAlarm event was received through a client event subscription.");
                }
                finally
                {
                    await session.DeleteSubscriptionsAsync(
                        null,
                        new uint[] { subscription.SubscriptionId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                session.Dispose();
            }
        }

        private static EventFilter CreateAlarmEventFilter()
        {
            return new EventFilter
            {
                SelectClauses =
                [
                    SelectEventField(global::Opc.Ua.ObjectTypeIds.ConditionType, Attributes.NodeId),
                    SelectEventField(global::Opc.Ua.ObjectTypeIds.BaseEventType, Attributes.Value, "SourceName"),
                    SelectEventField(
                        global::Opc.Ua.ObjectTypeIds.AlarmConditionType,
                        Attributes.Value,
                        "ActiveState",
                        "Id")
                ],
                WhereClause = new ContentFilter()
            };
        }

        private static SimpleAttributeOperand SelectEventField(
            NodeId typeDefinitionId,
            uint attributeId,
            params string[] browseNames)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = typeDefinitionId,
                BrowsePath = browseNames.Select(name => new QualifiedName(name)).ToArrayOf(),
                AttributeId = attributeId
            };
        }

        private static async Task<bool> WaitForAlarmEventAsync(
            Opc.Ua.Client.ISession session,
            uint subscriptionId,
            NodeId alarmNodeId,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = Array.Empty<SubscriptionAcknowledgement>().ToArrayOf();
            while (DateTime.UtcNow < deadline)
            {
                PublishResponse publish;
                try
                {
                    publish = acknowledgements.Count == 0
                        ? await session.PublishWithTimeoutAsync(1000).ConfigureAwait(false)
                        : await session.PublishWithTimeoutAsync(acknowledgements, 1000).ConfigureAwait(false);
                    acknowledgements = Array.Empty<SubscriptionAcknowledgement>().ToArrayOf();
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadRequestTimeout)
                {
                    continue;
                }

                if (publish.SubscriptionId == subscriptionId &&
                    publish.NotificationMessage.SequenceNumber != 0)
                {
                    acknowledgements = new SubscriptionAcknowledgement[]
                    {
                        new()
                        {
                            SubscriptionId = publish.SubscriptionId,
                            SequenceNumber = publish.NotificationMessage.SequenceNumber
                        }
                    }.ToArrayOf();
                }

                if (ContainsAlarmEvent(publish.NotificationMessage, alarmNodeId))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsAlarmEvent(
            NotificationMessage notificationMessage,
            NodeId alarmNodeId)
        {
            foreach (ExtensionObject notification in notificationMessage.NotificationData)
            {
                if (!notification.TryGetValue(out EventNotificationList? events) ||
                    events == null)
                {
                    continue;
                }

                foreach (EventFieldList eventFields in events.Events)
                {
                    if (eventFields.EventFields.Count > 0 &&
                        eventFields.EventFields[0].TryGetValue(out NodeId conditionId) &&
                        conditionId == alarmNodeId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TryGetStringArray(
            Variant value,
            out List<string> values)
        {
            if (value.TryGetValue(out ArrayOf<string> arrayOf))
            {
                values = [.. arrayOf];
                return true;
            }

            values = [];
            return false;
        }

        private static bool TryGetQualifiedNameArray(
            Variant value,
            out List<QualifiedName> values)
        {
            if (value.TryGetValue(out ArrayOf<QualifiedName> arrayOf))
            {
                values = [.. arrayOf];
                return true;
            }

            values = [];
            return false;
        }

        private static async Task<List<ReferenceDescription>> BrowseReachableAsync(
            IServerInternal server,
            NodeId startNodeId)
        {
            var reachable = new List<ReferenceDescription>();
            var visited = new HashSet<NodeId> { startNodeId };
            var pending = new Queue<NodeId>();
            pending.Enqueue(startNodeId);

            while (pending.Count > 0)
            {
                NodeId current = pending.Dequeue();
                IReadOnlyList<ReferenceDescription> references = await BrowseHierarchicalAsync(
                    server,
                    current).ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                    NodeId target = ExpandedNodeId.ToNodeId(reference.NodeId, server.NamespaceUris);
                    if (target.IsNull)
                    {
                        continue;
                    }
                    reachable.Add(reference);
                    if (visited.Add(target))
                    {
                        pending.Enqueue(target);
                    }
                }
            }
            return reachable;
        }

        private static async Task<bool> AllPumpMeasurementsAreGoodAsync(
            IServerInternal server,
            int pumpCount)
        {
            for (int pumpNumber = 1; pumpNumber <= pumpCount; pumpNumber++)
            {
                DataValue value = await ReadPumpMeasurementAsync(
                    server,
                    pumpNumber,
                    "DifferentialPressure").ConfigureAwait(false);
                if (value.StatusCode.Code != StatusCodes.Good)
                {
                    return false;
                }
            }
            return true;
        }

        private static async Task<DataValue> ReadPumpMeasurementAsync(
            IServerInternal server,
            int pumpNumber,
            string measurementName)
        {
            NodeId nodeId = await ResolveAsync(
                server,
                PumpNodeId(server, PumpBrowseName(pumpNumber)),
                ["Operational", "Measurements", measurementName]).ConfigureAwait(false);
            Assert.That(nodeId.IsNull, Is.False, measurementName + " was not found.");
            return await ReadAttributeAsync(server, nodeId, Attributes.Value).ConfigureAwait(false);
        }

        private static async Task AssertNonEmptyValueAsync(
            IServerInternal server,
            NodeId parent,
            string browseName)
        {
            NodeId nodeId = await ResolveAsync(server, parent, [browseName]).ConfigureAwait(false);
            Assert.That(nodeId.IsNull, Is.False, browseName + " was not found.");

            DataValue value = await ReadAttributeAsync(server, nodeId, Attributes.Value)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.IsNull, Is.False, browseName + " was null.");
            string text = ValueToText(value.WrappedValue);
            Assert.That(text, Is.Not.Empty, browseName + " was empty.");
        }

        private static async Task AssertStateLabelsAsync(
            IServerInternal server,
            NodeId pump,
            IReadOnlyList<string> booleanPath)
        {
            NodeId booleanNode = await ResolveAsync(server, pump, booleanPath)
                .ConfigureAwait(false);
            Assert.That(booleanNode.IsNull, Is.False, string.Join("/", booleanPath) + " was not found.");

            NodeId trueState = await ResolveAsync(server, booleanNode, ["TrueState"])
                .ConfigureAwait(false);
            NodeId falseState = await ResolveAsync(server, booleanNode, ["FalseState"])
                .ConfigureAwait(false);
            Assert.That(trueState.IsNull, Is.False, "TrueState was not found.");
            Assert.That(falseState.IsNull, Is.False, "FalseState was not found.");

            DataValue trueValue = await ReadAttributeAsync(server, trueState, Attributes.Value)
                .ConfigureAwait(false);
            DataValue falseValue = await ReadAttributeAsync(server, falseState, Attributes.Value)
                .ConfigureAwait(false);

            Assert.That(ValueToText(trueValue.WrappedValue), Is.Not.Empty);
            Assert.That(ValueToText(falseValue.WrappedValue), Is.Not.Empty);
        }

        private static string ValueToText(Variant value)
        {
            if (value.TryGetValue(out string text))
            {
                return text;
            }
            if (value.TryGetValue(out LocalizedText localizedText))
            {
                return localizedText.Text ?? string.Empty;
            }
            return value.ToString();
        }

        private static async Task AssertBrowseNameAsync(
            IServerInternal server,
            NodeId startNodeId,
            IReadOnlyList<string> path,
            QualifiedName expected)
        {
            NodeId nodeId = await ResolveAsync(server, startNodeId, path)
                .ConfigureAwait(false);
            Assert.That(nodeId.IsNull, Is.False, string.Join("/", path) + " was not found.");

            DataValue browseName = await ReadAttributeAsync(
                server,
                nodeId,
                Attributes.BrowseName).ConfigureAwait(false);

            Assert.That(browseName.WrappedValue.TryGetValue(out QualifiedName actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static async Task<DataValue> ReadAttributeAsync(
            IServerInternal server,
            NodeId nodeId,
            uint attributeId)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = attributeId }
            ];

            using var context = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Read,
                RequestLifetime.None);

            (ArrayOf<DataValue> values, _) = await server.NodeManager.ReadAsync(
                context,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                CancellationToken.None).ConfigureAwait(false);

            return values[0];
        }

        private static async Task RunServerAsync(
            Func<IServerInternal, Task> assertions,
            int pumpCount = 2,
            TimeSpan? simulationInterval = null)
        {
            ComplianceCaptureServer.Reset();
            var services = new ServiceCollection();
            services.AddLogging();
            services.Configure<PumpDeviceIntegrationOptions>(options =>
            {
                options.PumpCount = pumpCount;
                if (simulationInterval.HasValue)
                {
                    options.SimulationInterval = simulationInterval.Value;
                }
            });
            services.AddOpcUa()
                .AddServer<ComplianceCaptureServer>(ConfigureServer)
                .AddNodeManager<PumpNodeManagerFactory>()
                .ConfigureDevicesFor<PumpNodeManager>(context =>
                {
                    var manager = (PumpNodeManager)context.Manager;
                    foreach (NodeId pumpNodeId in manager.PumpNodeIds)
                    {
                        ITopologyElementBuilder<PumpState> pump =
                            context.TopologyElement<PumpState>(pumpNodeId);

                        pump.WithFunctionalGroup(
                            new QualifiedName("Diagnostics", manager.InstanceNamespaceIndex),
                            fg => fg.Configure(node =>
                                node.WithProperty("LastError", Variant.From(string.Empty), p => p.Writable())
                                    .WithProperty("ErrorCount", 0)
                                    .WithProperty("LastSelfTest", (DateTimeUtc)DateTime.UtcNow)));
                    }

                    return new ValueTask();
                });

            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();
            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.That(
                    await WaitForAsync(
                        () => ComplianceCaptureServer.StartedInstance != null,
                        TimeSpan.FromSeconds(30)).ConfigureAwait(false),
                    Is.True,
                    "The server did not start.");

                await assertions(ComplianceCaptureServer.StartedInstance!).ConfigureAwait(false);
            }
            finally
            {
                await hostedService.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static void ConfigureServer(OpcUaServerOptions options)
        {
            string applicationName = nameof(PumpAddressSpaceComplianceTests);
            string testRoot = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                applicationName,
                Guid.NewGuid().ToString("N"));
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:" + applicationName;
            options.ProductUri = "urn:localhost:" + applicationName + ":product";
            options.PkiRoot = System.IO.Path.Combine(testRoot, "pki");
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            options.EndpointUrls.Add(
                "opc.tcp://localhost:" +
                GetAvailablePort().ToString(CultureInfo.InvariantCulture) +
                "/" +
                applicationName);
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<bool> WaitForAsync(
            Func<Task<bool>> condition,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await condition().ConfigureAwait(false))
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return await condition().ConfigureAwait(false);
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            return condition();
        }

        public sealed class ComplianceCaptureServer : DependencyInjectionStandardServer
        {
            public ComplianceCaptureServer(
                IServiceProvider services,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                : base(services, telemetry, timeProvider)
            {
            }

            public static IServerInternal? StartedInstance => Volatile.Read(ref s_startedInstance);

            public static void Reset()
            {
                Volatile.Write(ref s_startedInstance, null);
            }

            protected override void OnServerStarted(IServerInternal server)
            {
                Volatile.Write(ref s_startedInstance, server);
                base.OnServerStarted(server);
            }

            private static IServerInternal? s_startedInstance;
        }
    }
}
