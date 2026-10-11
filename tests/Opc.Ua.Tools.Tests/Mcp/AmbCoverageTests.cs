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
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.AMB;
using Opc.Ua.AMB.Client;
using Opc.Ua.Di;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;
using static Opc.Ua.Tools.Tests.McpCompanion.CompanionCoverageSession;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies AMB MCP value fidelity, finite reads and exact mutations using the actual companion client.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class AmbCoverageTests
    {
        /// <summary>
        /// Alarm JSON preserves the acknowledgement token, classification, inactive flags and structured causes.
        /// </summary>
        [Test]
        public void AlarmPreservesEveryFieldAndStructuredRootCause()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var alarm = new AssetAlarmRecord
            {
                ConditionId = Id("condition"),
                EventId = ByteString.FromBase64("AAH/"),
                EventType = Id("event-type"),
                SourceNode = Id("asset"),
                SourceName = "Motor",
                ConditionName = "Connection",
                Time = s_timestamp,
                Message = new LocalizedText("de-DE", "Verbindung"),
                Severity = 801,
                ConditionClassId = Id("condition-class"),
                ConditionClassName = new LocalizedText("en-US", "Connection failure"),
                ConditionClass = AmbConditionClass.ConnectionFailure,
                IsActive = false,
                IsAcknowledged = true,
                Retain = false,
                Comment = new LocalizedText("fr-FR", "Vérifié"),
                PotentialRootCauses =
                [
                    new RootCauseDataType
                    {
                        RootCauseId = new NodeId(42),
                        RootCause = new LocalizedText("en-US", "Cable disconnected")
                    }
                ]
            };

            AssertJson(AmbJson.Alarm(alarm, wire.Context), /*lang=json,strict*/ """
                {
                  "conditionId":"ns=1;s=condition","eventId":"AAH/","eventType":"ns=1;s=event-type",
                  "sourceNode":"ns=1;s=asset","sourceName":"Motor","conditionName":"Connection",
                  "time":"2026-02-03T04:05:06.0000000Z","message":{"text":"Verbindung","locale":"de-DE"},
                  "severity":801,"faultCategory":"CriticalFault","conditionClassId":"ns=1;s=condition-class",
                  "conditionClassName":{"text":"Connection failure","locale":"en-US"},
                  "conditionClass":"ConnectionFailureConditionClassType",
                  "isActive":false,"isAcknowledged":true,"retain":false,
                  "comment":{"text":"Vérifié","locale":"fr-FR"},
                  "potentialRootCauses":[
                    {"RootCauseId":"i=42","RootCause":{"Locale":"en-US","Text":"Cable disconnected"}}]
                }
                """);
            JsonObject absent = AmbJson.Alarm(new AssetAlarmRecord(), wire.Context);
            Assert.That(absent["eventId"], Is.Null);
            Assert.That(absent["time"], Is.Null);
            Assert.That(absent["message"], Is.Null);
            Assert.That(absent["conditionClass"], Is.Null);
            Assert.That(absent["faultCategory"], Is.Null);
            Assert.That(absent["potentialRootCauses"]!.AsArray(), Is.Empty);
        }

        /// <summary>
        /// Maintenance JSON retains planned work, units of downtime, typed personnel and unchanged configuration.
        /// </summary>
        [Test]
        public void MaintenancePreservesPlannedWorkAndOptionalAbsence()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var activity = new MaintenanceActivityRecord
            {
                ConditionId = Id("maintenance"),
                EventId = ByteString.FromBase64("AQI="),
                EventType = Id("maintenance-type"),
                SourceNode = Id("asset"),
                ConditionName = "Inspection",
                Time = s_timestamp,
                Message = new LocalizedText("en-US", "Inspection finished"),
                ConditionClassId = Id("inspection-class"),
                ConditionClass = AmbConditionClass.Inspection,
                IsActive = false,
                IsAcknowledged = false,
                Retain = true,
                Comment = new LocalizedText("en-US", "No changes"),
                State = MaintenanceStateKind.Finished,
                PlannedDate = s_timestamp.AddDays(1),
                EstimatedDowntime = TimeSpan.FromMilliseconds(1250),
                MaintenanceSupplier = new NameNodeIdDataType
                {
                    Name = new LocalizedText("en-US", "Service"),
                    NodeId = new NodeId(501)
                },
                QualificationOfPersonnel = new NameNodeIdDataType
                {
                    Name = new LocalizedText("en-US", "Level 2"),
                    NodeId = new NodeId(502)
                },
                PartsOfAssetReplaced =
                [
                    new NameNodeIdDataType { Name = new LocalizedText("en-US", "Seal"), NodeId = new NodeId(503) }
                ],
                PartsOfAssetServiced =
                [
                    new NameNodeIdDataType { Name = new LocalizedText("en-US", "Motor"), NodeId = new NodeId(504) }
                ],
                MaintenanceMethod = MaintenanceMethodEnum.Remote,
                ConfigurationChanged = false
            };

            AssertJson(AmbJson.Maintenance(activity, wire.Context), /*lang=json,strict*/ """
                {
                  "conditionId":"ns=1;s=maintenance","eventId":"AQI=","eventType":"ns=1;s=maintenance-type",
                  "sourceNode":"ns=1;s=asset","conditionName":"Inspection","time":"2026-02-03T04:05:06.0000000Z",
                  "message":{"text":"Inspection finished","locale":"en-US"},
                  "conditionClassId":"ns=1;s=inspection-class","conditionClass":"InspectionConditionClassType",
                  "isActive":false,"isAcknowledged":false,"retain":true,
                  "comment":{"text":"No changes","locale":"en-US"},"state":"Finished",
                  "plannedDate":"2026-02-04T04:05:06.0000000Z","estimatedDowntimeSeconds":1.25,
                  "maintenanceSupplier":{"Name":{"Locale":"en-US","Text":"Service"},"NodeId":"i=501"},
                  "qualificationOfPersonnel":{"Name":{"Locale":"en-US","Text":"Level 2"},"NodeId":"i=502"},
                  "partsOfAssetReplaced":[{"Name":{"Locale":"en-US","Text":"Seal"},"NodeId":"i=503"}],
                  "partsOfAssetServiced":[{"Name":{"Locale":"en-US","Text":"Motor"},"NodeId":"i=504"}],
                  "maintenanceMethod":"Remote","configurationChanged":false
                }
                """);
            JsonObject missing = AmbJson.Maintenance(new MaintenanceActivityRecord(), wire.Context);
            foreach (string property in new[]
            {
                "eventId", "time", "message", "comment", "conditionClass", "state", "plannedDate",
                "estimatedDowntimeSeconds", "maintenanceSupplier", "qualificationOfPersonnel",
                "maintenanceMethod", "configurationChanged"
            })
            {
                Assert.That(missing.ContainsKey(property), Is.True, property);
                Assert.That(missing[property], Is.Null, property);
            }
            Assert.That(missing["partsOfAssetReplaced"]!.AsArray(), Is.Empty);
            Assert.That(missing["partsOfAssetServiced"]!.AsArray(), Is.Empty);
        }

        /// <summary>
        /// Snapshot collection pages are independent rather than sharing a cursor or truncating scalar facets.
        /// </summary>
        [Test]
        public void SnapshotPagesCollectionsIndependentlyAndRetainsContext()
        {
            var wire = new CompanionCoverageSession(NUnitTelemetryContext.Create());
            var snapshot = new AssetSnapshot
            {
                Asset = Id("asset"),
                Identification = new AssetIdentificationRecord
                {
                    Asset = Id("asset"),
                    ProductInstanceUri = "urn:asset:1",
                    AssetId = "A1",
                    Manufacturer = new LocalizedText("en-US", "Factory"),
                    ManufacturerUri = "urn:factory",
                    Model = new LocalizedText("de-DE", "Motor"),
                    ProductCode = "P1",
                    SerialNumber = "S1",
                    HardwareRevision = "H1",
                    SoftwareRevision = "S2",
                    RevisionCounter = 0
                },
                DeviceHealth = DeviceHealthEnumeration.FAILURE,
                HealthAlarms =
                [
                    new AssetAlarmRecord { ConditionId = Id("alarm-1") },
                    new AssetAlarmRecord { ConditionId = Id("alarm-2") },
                    new AssetAlarmRecord { ConditionId = Id("alarm-3") }
                ],
                MaintenanceActivities =
                [
                    new MaintenanceActivityRecord { ConditionId = Id("maintenance-1") },
                    new MaintenanceActivityRecord { ConditionId = Id("maintenance-2") }
                ],
                DocumentationLinks =
                [
                    new DocumentationLinkRecord(
                        Id("manual"), new QualifiedName("Manual", 1), LocalizedText.Null, "urn:manual", false)
                ],
                Context = new AssetContextRecord
                {
                    HierarchicalLocation = "Plant/Line",
                    OperationalLocation = "Mixing",
                    DigitalLocation = "urn:digital:asset",
                    LocalTime = new TimeZoneDataType { Offset = 60, DaylightSavingInOffset = false },
                    Classifications = [RemoteTarget()]
                },
                Locations =
                [
                    new AssetLocationRecord(AssetLocationKind.Hierarchical, Id("plant")),
                    new AssetLocationRecord(AssetLocationKind.Operational, Id("line")),
                    new AssetLocationRecord(AssetLocationKind.Hierarchical, Id("cell"))
                ]
            };

            JsonObject json = AmbJson.Snapshot(snapshot, wire.Context, 1, 1);

            AssertJson(json["identification"], /*lang=json,strict*/ """
                {
                  "assetNodeId":"ns=1;s=asset","productInstanceUri":"urn:asset:1","assetId":"A1",
                  "manufacturer":{"text":"Factory","locale":"en-US"},"manufacturerUri":"urn:factory",
                  "model":{"text":"Motor","locale":"de-DE"},"productCode":"P1","serialNumber":"S1",
                  "hardwareRevision":"H1","softwareRevision":"S2","revisionCounter":0
                }
                """);
            Assert.That(json["deviceHealth"]!.GetValue<string>(), Is.EqualTo("FAILURE"));
            Assert.That(json["deviceHealthValue"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(json["healthAlarms"]!["items"]![0]!["conditionId"]!.GetValue<string>(),
                Is.EqualTo("ns=1;s=alarm-2"));
            Assert.That(json["healthAlarms"]!["nextOffset"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(json["maintenanceActivities"]!["items"]![0]!["conditionId"]!.GetValue<string>(),
                Is.EqualTo("ns=1;s=maintenance-2"));
            Assert.That(json["maintenanceActivities"]!["hasMore"]!.GetValue<bool>(), Is.False);
            Assert.That(json["documentationLinks"]!["items"]!.AsArray(), Is.Empty);
            Assert.That(json["documentationLinks"]!["nextOffset"], Is.Null);
            Assert.That(json["locations"]!["items"]![0]!["kind"]!.GetValue<string>(), Is.EqualTo("Operational"));
            Assert.That(json["locations"]!["items"]![0]!["nodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=line"));
            Assert.That(json["locations"]!["hasMore"]!.GetValue<bool>(), Is.True);
            AssertJson(json["context"], /*lang=json,strict*/ """
                {
                  "hierarchicalLocation":"Plant/Line","operationalLocation":"Mixing",
                  "digitalLocation":"urn:digital:asset","localTime":{"Offset":60,"DaylightSavingInOffset":false},
                  "classifications":["svr=2;nsu=urn:remote;s=asset"]
                }
                """);
        }

        /// <summary>
        /// FindAlias receives the selected category and original pattern and preserves remote aliases in a page.
        /// </summary>
        [TestCase(AmbAssetCategory.Assets, AMB.Methods.Assets_FindAlias, AMB.Objects.Assets)]
        [TestCase(AmbAssetCategory.ByAssetId,
            AMB.Methods.AssetsByAssetId_FindAlias, AMB.Objects.AssetsByAssetId)]
        [TestCase(AmbAssetCategory.ByProductInstanceUri,
            AMB.Methods.AssetsByProductInstanceUri_FindAlias, AMB.Objects.AssetsByProductInstanceUri)]
        public async Task FindAssetsUsesSelectedCategoryAndPreservesRemoteTargetsAsync(
            AmbAssetCategory category,
            uint methodId,
            uint categoryId)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            wire.ReplyToCalls(AliasOutput(
                new AliasNameDataType { AliasName = new QualifiedName("first"), ReferencedNodes = [Id("first")] },
                new AliasNameDataType { AliasName = new QualifiedName("remote"), ReferencedNodes = [RemoteTarget()] }));

            JsonObject json = Result(await Reads(provider).FindAssetsAsync(
                "urn:line:%", category, 1, 1, "selected").ConfigureAwait(false));

            Assert.That(json["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(json["items"]![0]!["category"]!.GetValue<string>(), Is.EqualTo(category.ToString()));
            Assert.That(json["items"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("remote"));
            AssertJson(json["items"]![0]!["referencedNodes"], """["svr=2;nsu=urn:remote;s=asset"]""");
            Assert.That(json["hasMore"]!.GetValue<bool>(), Is.False);
            Assert.That(json["nextOffset"], Is.Null);
            Assert.That(wire.Calls, Has.Count.EqualTo(1));
            CallMethodRequest call = wire.Calls[0];
            Assert.That(call.ObjectId, Is.EqualTo(new NodeId(categoryId, wire.NamespaceIndex(AMB.Namespaces.AMB))));
            Assert.That(call.MethodId, Is.EqualTo(new NodeId(methodId, wire.NamespaceIndex(AMB.Namespaces.AMB))));
            Assert.That(call.InputArguments.Count, Is.EqualTo(2));
            Assert.That(call.InputArguments[0].TryGetValue(out string pattern), Is.True);
            Assert.That(pattern, Is.EqualTo("urn:line:%"));
            Assert.That(call.InputArguments[1].TryGetValue(out NodeId referenceType), Is.True);
            Assert.That(referenceType.IsNull, Is.True);
            Assert.That(wire.Browses, Is.Empty);
            Assert.That(wire.Reads, Is.Empty);
        }

        /// <summary>
        /// Browsing aliases reports remote identities without invoking FindAlias or following remote nodes.
        /// </summary>
        [Test]
        public async Task ListAliasesPagesBrowseResultsWithoutCallingFindAliasAsync()
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            NodeId category = new(AMB.Objects.AssetsByAssetId, wire.NamespaceIndex(AMB.Namespaces.AMB));
            wire.SetBrowse(category,
                [Reference(Id("alias-1"), "A1"), Reference(Id("alias-2"), "A2")], ReferenceTypeIds.Organizes);
            wire.SetBrowse(Id("alias-1"), [Reference(Id("asset"), "Asset")], ReferenceTypeIds.AliasFor);
            wire.SetBrowse(Id("alias-2"),
                [new ReferenceDescription { NodeId = RemoteTarget() }], ReferenceTypeIds.AliasFor);

            JsonObject json = Result(await Reads(provider).ListAliasesAsync(
                AmbAssetCategory.ByAssetId, 1, 1, "selected").ConfigureAwait(false));

            AssertJson(json["items"], /*lang=json,strict*/ """
                [{"category":"ByAssetId","name":"A2","referencedNodes":["svr=2;nsu=urn:remote;s=asset"]}]
                """);
            Assert.That(json["offset"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(json["hasMore"]!.GetValue<bool>(), Is.False);
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Browses.Select(request => request.NodeId),
                Is.EqualTo([category, Id("alias-1"), Id("alias-2")]));
        }

        /// <summary>
        /// Category support and NodeVersion are data; absence of the AMB namespace does not invent an identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadCategoryDistinguishesUnsupportedServerAndPublishedVersionAsync(bool supported)
        {
            await using ServiceProvider provider = Services();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            var wire = new CompanionCoverageSession(manager.Telemetry, supported);
            await manager.RegisterExistingSessionAsync("selected", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            NodeId category = supported
                ? new NodeId(AMB.Objects.Assets, wire.NamespaceIndex(AMB.Namespaces.AMB))
                : NodeId.Null;
            if (supported)
            {
                wire.Publish(category, "version", Variant.From("v17"), new QualifiedName("NodeVersion"));
            }

            JsonObject json = Result(await Reads(provider).ReadCategoryAsync(
                sessionName: "selected").ConfigureAwait(false));

            Assert.That(json["supported"]!.GetValue<bool>(), Is.EqualTo(supported));
            Assert.That(json["category"]!.GetValue<string>(), Is.EqualTo("Assets"));
            Assert.That(json["nodeId"]?.GetValue<string>(), Is.EqualTo(supported ? category.ToString() : null));
            Assert.That(json["nodeVersion"]?.GetValue<string>(), Is.EqualTo(supported ? "v17" : null));
            Assert.That(wire.Reads, Has.Count.EqualTo(supported ? 1 : 0));
            if (!supported)
            {
                JsonObject unsupported = Result(await Reads(provider).ReadAssetAsync(
                    Id("asset").ToString(), AmbAssetFacet.Health, sessionName: "selected").ConfigureAwait(false), true);
                Assert.That(unsupported["statusCodeValue"]!.GetValue<uint>(),
                    Is.EqualTo(StatusCodes.BadNotSupported.Code));
                Assert.That(wire.Paths, Is.Empty);
            }
        }

        /// <summary>
        /// Both location trees retain parent-first paths and contained identities without following assets.
        /// </summary>
        [TestCase(AmbLocationKind.Hierarchical)]
        [TestCase(AmbLocationKind.Operational)]
        public async Task BrowseLocationsPreservesTreePathsAndAssetIdentitiesAsync(AmbLocationKind kind)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            ushort ns = wire.NamespaceIndex(AMB.Namespaces.AMB);
            NodeId root = new(kind == AmbLocationKind.Hierarchical
                ? AMB.Objects.HierarchicalLocations : AMB.Objects.OperationalLocations, ns);
            NodeId contains = new(kind == AmbLocationKind.Hierarchical
                ? AMB.ReferenceTypes.HierarchicalContains : AMB.ReferenceTypes.OperationalContains, ns);
            wire.SetBrowse(root, [Reference(Id("plant"), "Plant")], ReferenceTypeIds.Organizes);
            wire.SetBrowse(Id("plant"), [Reference(Id("cell"), "Cell")], ReferenceTypeIds.HasComponent);
            wire.SetBrowse(Id("plant"), [Reference(Id("asset"), "Asset")], contains);
            wire.SetBrowse(Id("cell"), [new ReferenceDescription { NodeId = RemoteTarget() }], contains);

            JsonObject json = Result(await Reads(provider).BrowseLocationsAsync(
                kind, sessionName: "selected").ConfigureAwait(false));

            AssertJson(json["items"], /*lang=json,strict*/ """
                [
                  {"nodeId":"ns=1;s=plant","path":"Plant","assets":["ns=1;s=asset"]},
                  {"nodeId":"ns=1;s=cell","path":"Plant/Cell","assets":["svr=2;nsu=urn:remote;s=asset"]}
                ]
                """);
            Assert.That(json["hasMore"]!.GetValue<bool>(), Is.False);
            Assert.That(wire.Browses.Select(request => request.NodeId), Does.Not.Contain(Id("asset")));
            Assert.That(wire.Calls, Is.Empty);
        }

        /// <summary>
        /// Retained alarms exclude cleared unretained alarms and all maintenance-class conditions.
        /// </summary>
        [TestCase(AmbAssetFacet.HealthAlarms, 2, "alarm-1")]
        [TestCase(AmbAssetFacet.RetainedAlarms, 1, "alarm-2")]
        [TestCase(AmbAssetFacet.Maintenance, 1, "maintenance")]
        public async Task ReadConditionsSeparatesMaintenanceAndRetainedAlarmsAsync(
            AmbAssetFacet facet,
            int count,
            string first)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            PublishConditions(wire);

            JsonObject json = Result(await Reads(provider).ReadAssetAsync(
                Id("asset").ToString(), facet, sessionName: "selected").ConfigureAwait(false));

            Assert.That(json["facet"]!.GetValue<string>(), Is.EqualTo(facet.ToString()));
            JsonArray items = json["data"]!["items"]!.AsArray();
            Assert.That(items, Has.Count.EqualTo(count));
            Assert.That(items[0]!["conditionId"]!.GetValue<string>(), Is.EqualTo(Id(first).ToString()));
            Assert.That(items[0]!["sourceNode"]!.GetValue<string>(), Is.EqualTo("ns=1;s=asset"));
            Assert.That(items[0]!["eventId"]!.GetValue<string>(), Is.EqualTo("AQI="));
            Assert.That(items[0]!["retain"]!.GetValue<bool>(), Is.EqualTo(first != "alarm-1"));
            if (facet == AmbAssetFacet.HealthAlarms)
            {
                Assert.That(items[1]!["conditionId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=alarm-2"));
                Assert.That(items[0]!["isActive"]!.GetValue<bool>(), Is.False);
            }
            if (facet == AmbAssetFacet.Maintenance)
            {
                Assert.That(items[0]!["conditionClass"]!.GetValue<string>(),
                    Is.EqualTo("InspectionConditionClassType"));
            }
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// Semantic facets route to the published context, entries, relations and documentation, not raw reads.
        /// </summary>
        [TestCase(AmbAssetFacet.Health)]
        [TestCase(AmbAssetFacet.Documentation)]
        [TestCase(AmbAssetFacet.Context)]
        [TestCase(AmbAssetFacet.Locations)]
        [TestCase(AmbAssetFacet.Requirements)]
        [TestCase(AmbAssetFacet.Capabilities)]
        [TestCase(AmbAssetFacet.SubAssets)]
        [TestCase(AmbAssetFacet.Relations)]
        [TestCase(AmbAssetFacet.Snapshot)]
        public async Task ReadAssetReturnsSelectedSemanticFacetWithoutMutationsAsync(AmbAssetFacet facet)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            PublishAsset(wire);

            JsonObject json = Result(await Reads(provider).ReadAssetAsync(
                Id("asset").ToString(), facet, maxResults: 1, sessionName: "selected").ConfigureAwait(false));

            Assert.That(json["assetNodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=asset"));
            Assert.That(json["facet"]!.GetValue<string>(), Is.EqualTo(facet.ToString()));
            JsonNode data = json["data"]!;
            switch (facet)
            {
                case AmbAssetFacet.Health:
                    Assert.That(data["deviceHealthValue"]!.GetValue<int>(), Is.EqualTo(1));
                    break;
                case AmbAssetFacet.Documentation:
                    Assert.That(data["documentationLinksNodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=links"));
                    AssertLink(data["links"]!["items"]![0]!);
                    Assert.That(data["links"]!["hasMore"]!.GetValue<bool>(), Is.True);
                    break;
                case AmbAssetFacet.Context:
                    Assert.That(data["hierarchicalLocation"]!.GetValue<string>(), Is.EqualTo("Plant/Line"));
                    Assert.That(data["operationalLocation"]!.GetValue<string>(), Is.EqualTo("Mixing"));
                    Assert.That(data["digitalLocation"]!.GetValue<string>(), Is.EqualTo("urn:digital"));
                    AssertJson(data["classifications"], """["svr=2;nsu=urn:remote;s=asset"]""");
                    break;
                case AmbAssetFacet.Locations:
                    AssertJson(data["items"],
                        /*lang=json,strict*/ """[{"kind":"Hierarchical","nodeId":"ns=1;s=plant"}]""");
                    Assert.That(data["hasMore"]!.GetValue<bool>(), Is.True);
                    break;
                case AmbAssetFacet.Requirements:
                case AmbAssetFacet.Capabilities:
                    Assert.That(data["items"]![0]!["browseName"]!["name"]!.GetValue<string>(),
                        Is.EqualTo(facet == AmbAssetFacet.Requirements ? "Required" : "Capable"));
                    AssertJson(data["items"]![0]!["value"], /*lang=json,strict*/ """{"UaType":1,"Value":false}""");
                    AssertJson(data["items"]![0]!["dictionaryEntries"], """["svr=2;nsu=urn:remote;s=asset"]""");
                    break;
                case AmbAssetFacet.SubAssets:
                    AssertJson(data["items"], """["ns=1;s=subasset"]""");
                    Assert.That(data["hasMore"]!.GetValue<bool>(), Is.True);
                    break;
                case AmbAssetFacet.Relations:
                    AssertJson(data["items"], /*lang=json,strict*/ """
                        [{"referenceTypeId":"i=53","isInverse":true,"target":"svr=2;nsu=urn:remote;s=asset"}]
                        """);
                    break;
                case AmbAssetFacet.Snapshot:
                    Assert.That(data["identification"]!["serialNumber"]!.GetValue<string>(), Is.EqualTo("S-9"));
                    Assert.That(data["deviceHealthValue"]!.GetValue<int>(), Is.EqualTo(1));
                    Assert.That(data["healthAlarms"]!["items"]![0]!["conditionId"]!.GetValue<string>(),
                        Is.EqualTo("ns=1;s=alarm-1"));
                    Assert.That(data["healthAlarms"]!["hasMore"]!.GetValue<bool>(), Is.True);
                    Assert.That(data["maintenanceActivities"]!["items"]![0]!["conditionId"]!.GetValue<string>(),
                        Is.EqualTo("ns=1;s=maintenance"));
                    Assert.That(data["maintenanceActivities"]!["hasMore"]!.GetValue<bool>(), Is.False);
                    AssertLink(data["documentationLinks"]!["items"]![0]!);
                    Assert.That(data["context"]!["operationalLocation"]!.GetValue<string>(), Is.EqualTo("Mixing"));
                    Assert.That(data["locations"]!["items"]![0]!["nodeId"]!.GetValue<string>(),
                        Is.EqualTo("ns=1;s=plant"));
                    break;
            }
            Assert.That(wire.Writes, Is.Empty);
            Assert.That(wire.Calls, Has.Count.EqualTo(
                facet is AmbAssetFacet.SubAssets or AmbAssetFacet.Relations ? 1 : 0));
        }

        /// <summary>
        /// Acknowledgement forwards the exact event token and localized comment and does not retry refusals.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AcknowledgePreservesEventTokenCommentAndRawRefusalAsync(bool refused)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            StatusCode status = refused ? StatusCodes.BadEventIdUnknown : StatusCodes.Good;
            wire.ReplyToCalls(status: status);

            JsonObject json = Result(await Commands(provider).AcknowledgeAsync(
                Id("condition").ToString(), "AAH/", "Vérifié", "fr-FR", "selected").ConfigureAwait(false),
                status != StatusCodes.Good);

            Assert.That(wire.Calls, Has.Count.EqualTo(1));
            CallMethodRequest call = wire.Calls[0];
            Assert.That(call.ObjectId, Is.EqualTo(Id("condition")));
            Assert.That(call.MethodId, Is.EqualTo(MethodIds.AcknowledgeableConditionType_Acknowledge));
            Assert.That(call.InputArguments.Count, Is.EqualTo(2));
            Assert.That(call.InputArguments[0].TryGetValue(out ByteString eventId), Is.True);
            Assert.That(eventId.ToBase64(), Is.EqualTo("AAH/"));
            Assert.That(call.InputArguments[1].TryGetValue(out LocalizedText comment), Is.True);
            Assert.That(comment.Text, Is.EqualTo("Vérifié"));
            Assert.That(comment.Locale, Is.EqualTo("fr-FR"));
            if (status == StatusCodes.Good)
            {
                AssertJson(json, /*lang=json,strict*/ """{"succeeded":true,"conditionId":"ns=1;s=condition"}""");
            }
            else
            {
                Assert.That(json["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadEventIdUnknown.Code));
                Assert.That(json.ContainsKey("succeeded"), Is.False);
            }
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// Adding a link uses the actual AddIn owner and all four generated method arguments.
        /// </summary>
        [Test]
        public async Task AddDocumentationLinkPreservesQualifiedAndLocalizedArgumentsAsync()
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            wire.MapPath(Id("asset"), Id("links"), AmbName(wire, "DocumentationLinks"));
            wire.ReplyToCalls([Variant.From(Id("new-link"))]);

            JsonObject json = Result(await Commands(provider).AddDocumentationLinkAsync(
                Id("asset").ToString(), "https://example.invalid/manual", "Manual", 1,
                "Manuel", "Instructions", "fr-FR", "selected").ConfigureAwait(false));

            AssertJson(json, /*lang=json,strict*/ """
                {"succeeded":true,"assetNodeId":"ns=1;s=asset","linkNodeId":"ns=1;s=new-link"}
                """);
            Assert.That(wire.Calls, Has.Count.EqualTo(1));
            CallMethodRequest call = wire.Calls[0];
            Assert.That(call.ObjectId, Is.EqualTo(Id("links")));
            Assert.That(call.MethodId, Is.EqualTo(new NodeId(
                AMB.Methods.DocumentationLinksType_AddLink, wire.NamespaceIndex(AMB.Namespaces.AMB))));
            Assert.That(call.InputArguments.Count, Is.EqualTo(4));
            Assert.That(call.InputArguments[0].TryGetValue(out string uri), Is.True);
            Assert.That(uri, Is.EqualTo("https://example.invalid/manual"));
            Assert.That(call.InputArguments[1].TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name, Is.EqualTo(new QualifiedName("Manual", 1)));
            Assert.That(call.InputArguments[2].TryGetValue(out LocalizedText display), Is.True);
            Assert.That(display, Is.EqualTo(new LocalizedText("fr-FR", "Manuel")));
            Assert.That(call.InputArguments[3].TryGetValue(out LocalizedText description), Is.True);
            Assert.That(description, Is.EqualTo(new LocalizedText("fr-FR", "Instructions")));
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// Removing a link targets its asset's AddIn and preserves an authorization refusal unchanged.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RemoveDocumentationLinkUsesAddInAndRetainsRawStatusAsync(bool refused)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            wire.MapPath(Id("asset"), Id("links"), AmbName(wire, "DocumentationLinks"));
            StatusCode status = refused ? StatusCodes.BadUserAccessDenied : StatusCodes.Good;
            wire.ReplyToCalls(status: status);

            JsonObject json = Result(await Commands(provider).RemoveDocumentationLinkAsync(
                Id("asset").ToString(), Id("link").ToString(), "selected").ConfigureAwait(false),
                status != StatusCodes.Good);

            Assert.That(wire.Calls, Has.Count.EqualTo(1));
            CallMethodRequest call = wire.Calls[0];
            Assert.That(call.ObjectId, Is.EqualTo(Id("links")));
            Assert.That(call.MethodId, Is.EqualTo(new NodeId(
                AMB.Methods.DocumentationLinksType_RemoveLink, wire.NamespaceIndex(AMB.Namespaces.AMB))));
            Assert.That(call.InputArguments.Count, Is.EqualTo(1));
            Assert.That(call.InputArguments[0].TryGetValue(out NodeId link), Is.True);
            Assert.That(link, Is.EqualTo(Id("link")));
            if (status == StatusCodes.Good)
            {
                AssertJson(json, /*lang=json,strict*/ """{"succeeded":true,"linkNodeId":"ns=1;s=link"}""");
            }
            else
            {
                Assert.That(json["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(status.Code));
            }
            Assert.That(wire.Writes, Is.Empty);
        }

        /// <summary>
        /// URI writes permit explicit clearing and touch only the chosen link's Value attribute.
        /// </summary>
        [TestCase("")]
        [TestCase("https://example.invalid/revised")]
        public async Task WriteDocumentationLinkWritesOnlySelectedValueAsync(string uri)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            wire.ReplyToWrites();

            JsonObject json = Result(await Commands(provider).WriteDocumentationLinkAsync(
                Id("link").ToString(), uri, "selected").ConfigureAwait(false));

            Assert.That(json["succeeded"]!.GetValue<bool>(), Is.True);
            Assert.That(json["linkNodeId"]!.GetValue<string>(), Is.EqualTo("ns=1;s=link"));
            Assert.That(json["uri"]!.GetValue<string>(), Is.EqualTo(uri));
            Assert.That(wire.Writes, Has.Count.EqualTo(1));
            WriteValue write = wire.Writes[0];
            Assert.That(write.NodeId, Is.EqualTo(Id("link")));
            Assert.That(write.AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(write.Value.WrappedValue.TryGetValue(out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(uri));
            Assert.That(wire.Paths, Is.Empty);
            Assert.That(wire.Calls, Is.Empty);
        }

        /// <summary>
        /// Invalid selectors, null NodeIds and malformed acknowledgement tokens never send a wire operation.
        /// </summary>
        [TestCase("category", "ArgumentOutOfRangeException")]
        [TestCase("facet", "ArgumentOutOfRangeException")]
        [TestCase("location", "ArgumentOutOfRangeException")]
        [TestCase("observation", "ArgumentOutOfRangeException")]
        [TestCase("notifier", "ArgumentException")]
        [TestCase("node", "ArgumentException")]
        [TestCase("token", "FormatException")]
        public async Task InvalidSelectorsAndTokensRejectBeforeDispatchAsync(string input, string errorType)
        {
            await using ServiceProvider provider = Services();
            CompanionCoverageSession wire = await RegisterAsync(provider).ConfigureAwait(false);
            AmbReadTools reads = Reads(provider);
            CallToolResult result = input switch
            {
                "category" => await reads.FindAssetsAsync(
                    "%", (AmbAssetCategory)999, sessionName: "selected").ConfigureAwait(false),
                "facet" => await reads.ReadAssetAsync(
                    Id("asset").ToString(), (AmbAssetFacet)999, sessionName: "selected").ConfigureAwait(false),
                "location" => await reads.BrowseLocationsAsync(
                    (AmbLocationKind)999, sessionName: "selected").ConfigureAwait(false),
                "observation" => await reads.ObserveAsync(
                    (AmbObservationKind)999, sessionName: "selected").ConfigureAwait(false),
                "notifier" => await reads.ObserveAsync(
                    AmbObservationKind.AssetSetChanges, Id("asset").ToString(), sessionName: "selected")
                    .ConfigureAwait(false),
                "node" => await reads.ReadAssetAsync(
                    "i=0", AmbAssetFacet.Health, sessionName: "selected").ConfigureAwait(false),
                _ => await Commands(provider).AcknowledgeAsync(
                    Id("condition").ToString(), "not-base64", "comment", sessionName: "selected").ConfigureAwait(false)
            };

            Assert.That(Result(result, true)["errorType"]!.GetValue<string>(), Is.EqualTo(errorType));
            Assert.That(wire.Paths, Is.Empty);
            Assert.That(wire.Browses, Is.Empty);
            Assert.That(wire.Reads, Is.Empty);
            Assert.That(wire.Calls, Is.Empty);
            Assert.That(wire.Writes, Is.Empty);
        }

        private static async Task<CompanionCoverageSession> RegisterAsync(ServiceProvider provider)
        {
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();
            var wire = new CompanionCoverageSession(manager.Telemetry);
            await manager.RegisterExistingSessionAsync("selected", wire.Session.Object, "Anonymous")
                .ConfigureAwait(false);
            return wire;
        }

        private static AmbReadTools Reads(ServiceProvider provider)
        {
            return new AmbReadTools(provider.GetRequiredService<AmbClientAccessor>());
        }

        private static AmbCommandTools Commands(ServiceProvider provider)
        {
            return new AmbCommandTools(provider.GetRequiredService<AmbClientAccessor>());
        }

        private static QualifiedName AmbName(CompanionCoverageSession wire, string name)
        {
            return wire.Name(name, AMB.Namespaces.AMB);
        }

        private static ExpandedNodeId RemoteTarget()
        {
            return ExpandedNodeId.Parse("svr=2;nsu=urn:remote;s=asset");
        }

        private static ArrayOf<Variant> AliasOutput(params AliasNameDataType[] aliases)
        {
            return [Variant.From(aliases.Select(alias => new ExtensionObject(alias)).ToArrayOf())];
        }

        private static void AssertLink(JsonNode link)
        {
            AssertJson(link, /*lang=json,strict*/ """
                {
                  "nodeId":"ns=1;s=manual","browseName":{"name":"Manual","namespaceIndex":1},
                  "displayName":{"text":"Manual","locale":"de-DE"},"uri":"urn:manual",
                  "isWritable":true,"isUserLink":true
                }
                """);
        }

        private static void PublishConditions(CompanionCoverageSession wire)
        {
            wire.MapPath(Id("asset"), Id("alarms"), wire.Name("DeviceHealthAlarms", Di.Namespaces.OpcUaDi));
            wire.SetBrowse(Id("alarms"),
            [
                Reference(Id("alarm-1"), "First", typeDefinition: ObjectTypeIds.AlarmConditionType),
                Reference(Id("alarm-2"), "Second", typeDefinition: ObjectTypeIds.AlarmConditionType),
                Reference(Id("maintenance"), "Inspection", typeDefinition: ObjectTypeIds.AlarmConditionType)
            ]);
            foreach (string name in new[] { "alarm-1", "alarm-2", "maintenance" })
            {
                NodeId condition = Id(name);
                AmbConditionClass conditionClass = name == "maintenance"
                    ? AmbConditionClass.Inspection
                    : AmbConditionClass.ConnectionFailure;
                wire.Publish(condition, name + "-class", Variant.From(
                    conditionClass.GetTypeId(wire.Context.NamespaceUris)), new QualifiedName("ConditionClassId"));
                wire.Publish(condition, name + "-source", Variant.From(Id("asset")), new QualifiedName("SourceNode"));
                wire.Publish(condition, name + "-event", Variant.From(ByteString.FromBase64("AQI=")),
                    new QualifiedName("EventId"));
                wire.Publish(condition, name + "-retain", Variant.From(name != "alarm-1"), new QualifiedName("Retain"));
                wire.Publish(condition, name + "-active", Variant.From(name != "alarm-1"),
                    new QualifiedName("ActiveState"), new QualifiedName("Id"));
            }
        }

        private static void PublishAsset(CompanionCoverageSession wire)
        {
            NodeId asset = Id("asset");
            PublishConditions(wire);
            wire.Publish(asset, "serial", Variant.From("S-9"), wire.Name("SerialNumber", Di.Namespaces.OpcUaDi));
            wire.Publish(asset, "health", Variant.From(1), wire.Name("DeviceHealth", Di.Namespaces.OpcUaDi));
            wire.Publish(asset, "hierarchy", Variant.From("Plant/Line"), AmbName(wire, "HierarchicalLocation"));
            wire.Publish(asset, "operation", Variant.From("Mixing"), AmbName(wire, "OperationalLocation"));
            wire.Publish(asset, "digital", Variant.From("urn:digital"), AmbName(wire, "DigitalLocation"));
            wire.SetBrowse(asset,
                [new ReferenceDescription { NodeId = RemoteTarget() }], ReferenceTypeIds.HasDictionaryEntry);
            wire.MapPath(asset, Id("links"), AmbName(wire, "DocumentationLinks"));
            wire.SetBrowse(Id("links"),
            [
                Reference(Id("manual"), "Manual", NodeClass.Variable),
                Reference(Id("drawing"), "Drawing", NodeClass.Variable)
            ], ReferenceTypeIds.HasComponent);
            wire.SetValue(Id("manual"), Variant.From("urn:manual"));
            wire.SetValue(Id("drawing"), Variant.From("urn:drawing"));
            wire.SetValue(Id("manual"), Variant.From((byte)(AccessLevels.CurrentRead | AccessLevels.CurrentWrite)),
                Attributes.UserAccessLevel);
            wire.SetValue(Id("drawing"), Variant.From(AccessLevels.CurrentRead), Attributes.UserAccessLevel);
            wire.SetBrowse(Id("manual"),
                [Reference(Id("user-link"), DocumentationLinkProperties.UserLink, NodeClass.Variable)],
                ReferenceTypeIds.HasProperty);
            wire.SetValue(Id("user-link"), Variant.From(true));
            ushort ns = wire.NamespaceIndex(AMB.Namespaces.AMB);
            wire.SetBrowse(asset, [Reference(Id("plant"), "Plant")],
                new NodeId(AMB.ReferenceTypes.HierarchicalContains, ns), BrowseDirection.Inverse);
            wire.SetBrowse(asset, [Reference(Id("operation-location"), "Mixing")],
                new NodeId(AMB.ReferenceTypes.OperationalContains, ns), BrowseDirection.Inverse);
            foreach ((string folder, string entry) in new[]
            {
                ("Requirements", "Required"), ("Capabilities", "Capable")
            })
            {
                wire.MapPath(asset, Id(folder), AmbName(wire, folder));
                wire.SetBrowse(Id(folder), [Reference(Id(entry), entry, NodeClass.Variable)]);
                wire.SetValue(Id(entry), Variant.From(false));
                wire.SetBrowse(Id(entry),
                    [new ReferenceDescription { NodeId = RemoteTarget() }], ReferenceTypeIds.HasDictionaryEntry);
            }
            wire.ReplyToCalls(AliasOutput(new AliasNameDataType
            {
                AliasName = new QualifiedName("local"),
                ReferencedNodes = [asset, Id("subasset")]
            }));
            wire.SetBrowse(asset,
            [
                Reference(Id("subasset"), "Subasset"),
                new ReferenceDescription { NodeId = RemoteTarget(), NodeClass = NodeClass.Object }
            ]);
            wire.SetBrowse(asset,
            [
                new ReferenceDescription
                {
                    NodeId = RemoteTarget(),
                    ReferenceTypeId = ReferenceTypeIds.HasCause,
                    IsForward = false
                }
            ], ReferenceTypeIds.NonHierarchicalReferences, BrowseDirection.Both);
        }

        private static readonly DateTime s_timestamp = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
    }
}
#endif
