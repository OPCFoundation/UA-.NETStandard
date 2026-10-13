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
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.ISA95.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tests;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Covers common-model reads, independent job roles and lossless version-specific payload contracts.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class Isa95CoverageTests
    {
        /// <summary>
        /// A property page excludes duplicate and remote references and reads only the selected value.
        /// </summary>
        [Test]
        public async Task ReadCommonObjectPagesLocalPropertiesWithQualityAndTimestampsAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId person = wire.Node("operator-12");
            NodeId vendorType = wire.Node("CertifiedOperatorType");
            wire.Subtype(vendorType, ISA95.ObjectTypeIds.PersonType);
            wire.Browse(person, ReferenceTypeIds.HasTypeDefinition, NodeClass.ObjectType,
                [wire.Reference(vendorType, "CertifiedOperatorType", ObjectTypeIds.BaseObjectType,
                    NodeClass.ObjectType)]);
            NodeId certification = wire.Node("certification");
            NodeId shift = wire.Node("shift");
            ReferenceDescription first = wire.Reference(certification, "Certification", VariableTypeIds.PropertyType,
                NodeClass.Variable, ReferenceTypeIds.HasProperty);
            ReferenceDescription remote = wire.Reference(wire.Node("remote"), "Remote", VariableTypeIds.PropertyType,
                NodeClass.Variable, ReferenceTypeIds.HasProperty);
            remote.NodeId = new ExpandedNodeId(wire.Node("remote"), null, 1);
            wire.Browse(person, NodeId.Null, NodeClass.Variable,
            [
                first, first, remote,
                wire.Reference(shift, "Shift", VariableTypeIds.PropertyType, NodeClass.Variable,
                    ReferenceTypeIds.HasProperty)
            ]);
            wire.Value(person, Variant.From(new QualifiedName("Operator12", wire.VendorIndex)), Attributes.BrowseName);
            wire.Value(person, Variant.From(new LocalizedText("en", "Operator twelve")), Attributes.DisplayName);
            wire.Value(person, Variant.From(new LocalizedText("de", "Schichtführer")), Attributes.Description);
            wire.Value(shift, new DataValue(Variant.From((ushort)2), StatusCodes.Uncertain,
                new DateTimeUtc(2026, 6, 1, 8, 9, 10)));
            await manager.RegisterExistingSessionAsync("jobs", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement json = Success(await new Isa95Tools(manager).ReadCommonObjectAsync(
                person.ToString(), 1, 1, "jobs").ConfigureAwait(false));

            Assert.That(json.GetProperty("kind").GetString(), Is.EqualTo("Person"));
            Assert.That(json.GetProperty("typeDefinitionId").GetString(), Is.EqualTo(vendorType.ToString()));
            Assert.That(json.GetProperty("displayName").GetProperty("Value").GetProperty("Text").GetString(),
                Is.EqualTo("Operator twelve"));
            JsonElement page = json.GetProperty("properties");
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(page.GetProperty("offset").GetInt32(), Is.EqualTo(1));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.False);
            JsonElement item = page.GetProperty("items")[0];
            Assert.That(item.GetProperty("nodeId").GetString(), Is.EqualTo(shift.ToString()));
            Assert.That(item.GetProperty("referenceTypeId").GetString(), Is.EqualTo("i=46"));
            Assert.That(item.GetProperty("value").GetProperty("UaType").GetInt32(),
                Is.EqualTo((int)BuiltInType.UInt16));
            Assert.That(item.GetProperty("value").GetProperty("Value").GetUInt16(), Is.EqualTo(2));
            Assert.That(item.GetProperty("value").GetProperty("Status").GetProperty("Code").GetUInt32(),
                Is.EqualTo(StatusCodes.Uncertain.Code));
            Assert.That(item.GetProperty("value").GetProperty("SourceTimestamp").GetString(),
                Does.StartWith("2026-06-01T08:09:10"));
            Assert.That(wire.Reads.Select(read => read.NodeId), Is.EqualTo([person, person, person, shift]));
            Assert.That(wire.TimestampRequests, Is.EqualTo([TimestampsToReturn.Both]));
        }

        /// <summary>
        /// Empty property pages retain object identity without reading a fabricated variable.
        /// </summary>
        [Test]
        public async Task ReadCommonObjectBeyondLastPropertyReturnsIdentityAndEmptyPageAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId lot = wire.Node("lot-A");
            NodeId type = wire.Local(ISA95.ObjectTypeIds.MaterialLotType);
            wire.Browse(lot, ReferenceTypeIds.HasTypeDefinition, NodeClass.ObjectType,
                [wire.Reference(type, "MaterialLotType", ObjectTypeIds.BaseObjectType, NodeClass.ObjectType)]);
            wire.Value(lot, Variant.From(new QualifiedName("LotA", wire.VendorIndex)), Attributes.BrowseName);
            wire.Value(lot, Variant.From(new LocalizedText("Lot A")), Attributes.DisplayName);
            wire.Value(lot, Variant.From(LocalizedText.Null), Attributes.Description);
            await manager.RegisterExistingSessionAsync("jobs", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement json = Success(await new Isa95Tools(manager).ReadCommonObjectAsync(
                lot.ToString(), 3, 2, "jobs").ConfigureAwait(false));

            Assert.That(json.GetProperty("kind").GetString(), Is.EqualTo("MaterialLot"));
            Assert.That(json.GetProperty("nodeId").GetString(), Is.EqualTo(lot.ToString()));
            Assert.That(json.GetProperty("properties").GetProperty("items").GetArrayLength(), Is.Zero);
            Assert.That(json.GetProperty("properties").GetProperty("hasMore").GetBoolean(), Is.False);
            Assert.That(wire.Reads, Has.Count.EqualTo(3));
        }

        /// <summary>
        /// Type validation and browse errors remain actionable service statuses instead of empty success.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadCommonObjectPreservesTypeAndBrowseRefusalsAsync(bool browseFailure)
        {
            StatusCode expected = browseFailure ? StatusCodes.BadUserAccessDenied : StatusCodes.BadTypeMismatch;
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId node = wire.Node("not-readable-person");
            wire.Browse(node, ReferenceTypeIds.HasTypeDefinition, NodeClass.ObjectType,
                [wire.Reference(ObjectTypeIds.FolderType, "FolderType", ObjectTypeIds.BaseObjectType,
                    NodeClass.ObjectType)],
                browseFailure ? StatusCodes.BadUserAccessDenied : StatusCodes.Good);
            await manager.RegisterExistingSessionAsync("jobs", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            CallToolResult result = await new Isa95Tools(manager).ReadCommonObjectAsync(
                node.ToString(), sessionName: "jobs").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent!.Value.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(expected.Code));
            Assert.That(wire.Reads, Is.Empty);
        }

        /// <summary>
        /// Common discovery traverses actual folders but only returns supported common object families.
        /// </summary>
        [Test]
        public async Task ListCommonObjectsFindsNestedVendorEquipmentWithoutReturningFoldersAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId cell = wire.Node("cell");
            NodeId press = wire.Node("press");
            NodeId type = wire.Node("VendorEquipmentType");
            wire.Subtype(type, ISA95.ObjectTypeIds.EquipmentType);
            wire.Browse(ObjectIds.ObjectsFolder, NodeId.Null, NodeClass.Object,
                [wire.Reference(cell, "Cell", ObjectTypeIds.FolderType)]);
            wire.Browse(cell, NodeId.Null, NodeClass.Object, [wire.Reference(press, "Press", type)]);
            await manager.RegisterExistingSessionAsync("jobs", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement json = Success(await new Isa95Tools(manager).ListCommonObjectsAsync(
                sessionName: "jobs").ConfigureAwait(false));

            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            JsonElement entry = json.GetProperty("items")[0];
            Assert.That(entry.GetProperty("kind").GetString(), Is.EqualTo("Equipment"));
            Assert.That(entry.GetProperty("nodeId").GetString(), Is.EqualTo(press.ToString()));
            Assert.That(entry.GetProperty("typeDefinitionId").GetString(), Is.EqualTo(type.ToString()));
            Assert.That(entry.GetProperty("browseName").GetString(), Is.EqualTo("Press"));
            Assert.That(wire.Browses.Select(browse => browse.NodeId),
                Is.EqualTo([ObjectIds.ObjectsFolder, cell, press]));
        }

        /// <summary>
        /// Version selection preserves the independent order and response endpoint identities.
        /// </summary>
        [TestCase(Isa95JobVersion.All, 3)]
        [TestCase(Isa95JobVersion.V1, 1)]
        [TestCase(Isa95JobVersion.V2, 2)]
        public async Task DiscoverJobEndpointsKeepsVersionedRolesAndVendorTypesAsync(Isa95JobVersion version, int count)
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            NodeId root = wire.Node("job-endpoints");
            NodeId v1Receiver = wire.Node("v1-response-receiver");
            NodeId v2Receiver = wire.Node("v2-order-receiver");
            NodeId v2Provider = wire.Node("v2-response-provider");
            NodeId vendorType = wire.Node("VendorResponseProviderType");
            wire.Subtype(vendorType, V2.ObjectTypeIds.ISA95JobResponseProviderObjectType);
            wire.Browse(root, NodeId.Null, NodeClass.Object,
            [
                wire.Reference(v1Receiver, "LegacyResponses",
                    wire.Local(V1.ObjectTypeIds.ISA95JobResponseReceiverObjectType)),
                wire.Reference(v2Receiver, "Orders", wire.Local(V2.ObjectTypeIds.ISA95JobOrderReceiverSubStatesType)),
                wire.Reference(v2Provider, "Responses", vendorType),
                wire.Reference(wire.Node("folder"), "Folder", ObjectTypeIds.FolderType)
            ]);
            await manager.RegisterExistingSessionAsync("jobs", wire.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonElement page = Success(await new Isa95Tools(manager).DiscoverJobEndpointsAsync(
                root.ToString(), version, sessionName: "jobs").ConfigureAwait(false));
            JsonElement[] entries = [.. page.GetProperty("items").EnumerateArray()];

            Assert.That(entries, Has.Length.EqualTo(count));
            Assert.That(page.GetProperty("hasMore").GetBoolean(), Is.False);
            if (version != Isa95JobVersion.V2)
            {
                Assert.That(entries[0].GetProperty("version").GetString(), Is.EqualTo("V1"));
                Assert.That(entries[0].GetProperty("role").GetString(), Is.EqualTo("JobResponseReceiver"));
                Assert.That(entries[0].GetProperty("nodeId").GetString(), Is.EqualTo(v1Receiver.ToString()));
            }
            if (version != Isa95JobVersion.V1)
            {
                Assert.That(entries[^2].GetProperty("version").GetString(), Is.EqualTo("V2"));
                Assert.That(entries[^2].GetProperty("role").GetString(), Is.EqualTo("JobOrderReceiver"));
                Assert.That(entries[^2].GetProperty("nodeId").GetString(), Is.EqualTo(v2Receiver.ToString()));
                Assert.That(entries[^1].GetProperty("role").GetString(), Is.EqualTo("JobResponseProvider"));
                Assert.That(entries[^1].GetProperty("nodeId").GetString(), Is.EqualTo(v2Provider.ToString()));
                Assert.That(entries[^1].GetProperty("typeDefinitionId").GetString(), Is.EqualTo(vendorType.ToString()));
            }
            Assert.That(wire.Browses, Has.Count.EqualTo(1), "An explicit discovery root is not recursively expanded.");
        }

        /// <summary>
        /// V1 order and response commands carry recursive resources with exact string quantities and units.
        /// </summary>
        [Test]
        public async Task V1OrderAndResponseDispatchCompleteRecursiveResourcePayloadsAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            var wire = new JobsCoverageWireSession(manager.Telemetry);
            var calls = new List<CallMethodRequest>();
            JobMcpTestSession.SetupCall(wire.Session, calls, [Variant.From(1UL)]);
            await manager.RegisterExistingSessionAsync("jobs", wire.Session.Object, "Anonymous").ConfigureAwait(false);
            Isa95JobOrderInput order = JsonSerializer.Deserialize(
                """
                {
                  "jobOrderId":"batch-17","description":[{"text":"Castings"}],
                  "startTime":"2026-06-01T08:00:00Z","endTime":"2026-06-01T09:00:00Z","priority":3,
                  "workMasters":[{"id":"recipe-2","description":{"text":"Casting recipe"},
                    "parameters":[{"id":"temperature","value":{"dataType":"Double","value":720},"unitOfMeasure":"C"}]}],
                  "parameters":[{"id":"count","value":{"dataType":"UInt32","value":2},"children":[
                    {"id":"verified","value":{"dataType":"Boolean","value":false}}]}],
                  "personnel":[{"id":"operator-7","use":"pouring","quantity":"1","unitOfMeasure":"person",
                    "properties":[{"id":"skill","value":{"dataType":"String","value":"casting"},"children":[
                      {"id":"level","value":{"dataType":"Int16","value":4},"unitOfMeasure":"grade"}]}]}],
                  "equipment":[{"id":"furnace-2","use":"melting","quantity":"1","unitOfMeasure":"unit",
                    "properties":[{"id":"power","value":{"dataType":"Double","value":22.5},"unitOfMeasure":"kW"}]}],
                  "physicalAssets":[{"id":"crucible-9","use":"holding","quantity":"0.125","unitOfMeasure":"m3",
                    "properties":[{"id":"reusable","value":{"dataType":"Boolean","value":false}}]}],
                  "materials":[{"materialClassId":"alloy","materialDefinitionId":"AlSi",
                    "materialLotId":"lot-3","materialSublotId":"sub-1","use":"consumed",
                    "quantity":"12345678901234567890.0001","unitOfMeasure":"kg","description":[{"text":"Aluminium"}],
                    "properties":[{"id":"purity","value":{"dataType":"Double","value":99.5},"unitOfMeasure":"%"}]}]
                }
                """, Isa95InputJsonContext.Default.Isa95JobOrderInput)!;
            var response = new Isa95JobResponseInput
            {
                JobResponseId = "response-17",
                JobOrderId = "batch-17",
                Description = new Isa95TextInput { Text = "Finished casting" },
                StartTime = order.StartTime,
                EndTime = order.EndTime,
                V1State = Isa95V1State.Completed,
                Parameters = order.Parameters,
                Personnel = order.Personnel,
                Equipment = order.Equipment,
                PhysicalAssets = order.PhysicalAssets,
                Materials = order.Materials
            };
            var tools = new Isa95Tools(manager);
            NodeId orderReceiver = wire.Node("v1-order-receiver");
            NodeId responseReceiver = wire.Node("v1-response-receiver");

            Success(await tools.ReceiveV1OrderAsync(
                orderReceiver.ToString(), Isa95V1Command.Store, order, "jobs").ConfigureAwait(false));
            Success(await tools.ReceiveV1ResponseAsync(
                responseReceiver.ToString(), response, "jobs").ConfigureAwait(false));

            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(calls[0].ObjectId, Is.EqualTo(orderReceiver));
            Assert.That(calls[1].ObjectId, Is.EqualTo(responseReceiver));
            V1.ISA95JobOrderDataType sent = null!;
            Assert.That(calls[0].InputArguments[1].TryGetValue(out sent!, wire.Context), Is.True);
            Assert.That(sent.ID, Is.EqualTo("batch-17"));
            Assert.That(sent.Priority, Is.EqualTo(3));
            Assert.That(sent.StartTime, Is.EqualTo(new DateTimeUtc(2026, 6, 1, 8, 0, 0)));
            Assert.That(sent.EndTime, Is.EqualTo(new DateTimeUtc(2026, 6, 1, 9, 0, 0)));
            Assert.That(sent.WorkMasterID[0].ID, Is.EqualTo("recipe-2"));
            Assert.That(sent.WorkMasterID[0].Parameters[0].UoM, Is.EqualTo("C"));
            Assert.That(sent.PersonnelRequirements[0].PersonnelUse, Is.EqualTo("pouring"));
            Assert.That(sent.PersonnelRequirements[0].Properties[0].Subproperties[0].Value.TryGetValue(out short level),
                Is.True);
            Assert.That(level, Is.EqualTo(4));
            Assert.That(sent.PersonnelRequirements[0].Properties[0].Subproperties[0].UoM, Is.EqualTo("grade"));
            Assert.That(sent.EquipmentRequirements[0].EquipmentUse, Is.EqualTo("melting"));
            Assert.That(sent.EquipmentRequirements[0].Properties[0].Value.TryGetValue(out double power), Is.True);
            Assert.That(power, Is.EqualTo(22.5));
            Assert.That(sent.PhysicalAssetRequirements[0].PhysicalAssetUse, Is.EqualTo("holding"));
            Assert.That(sent.PhysicalAssetRequirements[0].Quantity, Is.EqualTo("0.125"));
            Assert.That(sent.MaterialRequirements[0].MaterialLotID, Is.EqualTo("lot-3"));
            Assert.That(sent.MaterialRequirements[0].MaterialSublotID, Is.EqualTo("sub-1"));
            Assert.That(sent.MaterialRequirements[0].Quantity, Is.EqualTo("12345678901234567890.0001"));
            Assert.That(sent.MaterialRequirements[0].UoM, Is.EqualTo("kg"));
            V1.ISA95JobResponseDataType actual = null!;
            Assert.That(calls[1].InputArguments[0].TryGetValue(out actual!, wire.Context), Is.True);
            Assert.That(actual.ID, Is.EqualTo("response-17"));
            Assert.That(actual.JobOrderID, Is.EqualTo("batch-17"));
            Assert.That(actual.Description, Is.EqualTo("Finished casting"));
            Assert.That(actual.JobResponseData[0].Subparameters[0].Value.TryGetValue(out bool verified), Is.True);
            Assert.That(verified, Is.False);
            Assert.That(actual.PersonnelActuals[0].ID, Is.EqualTo("operator-7"));
            Assert.That(actual.EquipmentActuals[0].ID, Is.EqualTo("furnace-2"));
            Assert.That(actual.PhysicalAssetActuals[0].ID, Is.EqualTo("crucible-9"));
            Assert.That(actual.MaterialActuals[0].Properties[0].Value.TryGetValue(out double purity), Is.True);
            Assert.That(purity, Is.EqualTo(99.5));
        }

        /// <summary>
        /// V2 response actuals keep optional masks, localized units and independently scoped states.
        /// </summary>
        [Test]
        public void ResponseV2PreservesRichActualsAndExplicitEmptyProperties()
        {
            Isa95JobResponseInput input = JsonSerializer.Deserialize(
                """
                {
                  "jobResponseId":"response-9","jobOrderId":"job-9",
                  "description":{"text":"Fertig","locale":"de"},
                  "startTime":"2026-06-01T08:00:00Z","endTime":"2026-06-01T08:30:00Z",
                  "states":[{"stateNumber":4,"browsePath":["3:Execution"],"stateText":{"text":"Ended","locale":"en"}}],
                  "parameters":[{"id":"good","value":{"dataType":"Boolean","value":false}}],
                  "personnel":[{"id":"operator","properties":[]}],
                  "equipment":[{"id":"press","description":[],"use":"","quantity":"0",
                    "engineeringUnits":{"namespaceUri":"urn:units","unitId":7,
                      "displayName":{"text":"h","locale":"en"},"description":{"text":"hour","locale":"en"}},
                    "properties":[{"id":"hours","value":{"dataType":"Double","value":0.5}}]}],
                  "physicalAssets":[{"id":"tool",
                    "properties":[{"id":"wear","value":{"dataType":"Float","value":1.25}}]}],
                  "materials":[{"materialDefinitionId":"steel","materialSublotId":"","use":"produced",
                    "quantity":"0.0000000000000001","description":[],
                    "engineeringUnits":{"namespaceUri":"urn:units","unitId":8},"properties":[]}]
                }
                """, Isa95InputJsonContext.Default.Isa95JobResponseInput)!;

            V2.ISA95JobResponseDataType response = Isa95InputMapper.ResponseV2(
                input, ServiceMessageContext.Create(NUnitTelemetryContext.Create()));

            Assert.That(response.Description.Locale, Is.EqualTo("de"));
            Assert.That(response.EndTime, Is.EqualTo(new DateTimeUtc(2026, 6, 1, 8, 30, 0)));
            Assert.That(response.EncodingMask, Is.EqualTo((uint)(
                V2.ISA95JobResponseDataTypeFields.Description |
                V2.ISA95JobResponseDataTypeFields.StartTime |
                V2.ISA95JobResponseDataTypeFields.EndTime |
                V2.ISA95JobResponseDataTypeFields.JobResponseData |
                V2.ISA95JobResponseDataTypeFields.PersonnelActuals |
                V2.ISA95JobResponseDataTypeFields.EquipmentActuals |
                V2.ISA95JobResponseDataTypeFields.PhysicalAssetActuals |
                V2.ISA95JobResponseDataTypeFields.MaterialActuals)));
            Assert.That(response.JobState[0].BrowsePath.Elements[0].TargetName,
                Is.EqualTo(new QualifiedName("Execution", 3)));
            Assert.That(response.JobState[0].StateText.Text, Is.EqualTo("Ended"));
            Assert.That(response.PersonnelActuals[0].Properties.IsNull, Is.False);
            Assert.That(response.PersonnelActuals[0].Properties.Count, Is.Zero);
            Assert.That(response.EquipmentActuals[0].EngineeringUnits.UnitId, Is.EqualTo(7));
            Assert.That(response.EquipmentActuals[0].EngineeringUnits.DisplayName.Text, Is.EqualTo("h"));
            Assert.That(response.EquipmentActuals[0].EngineeringUnits.Description.Text, Is.EqualTo("hour"));
            Assert.That(response.EquipmentActuals[0].Quantity, Is.EqualTo("0"));
            Assert.That(response.EquipmentActuals[0].Properties[0].Value.TryGetValue(out double hours), Is.True);
            Assert.That(hours, Is.EqualTo(0.5));
            Assert.That(response.PhysicalAssetActuals[0].Properties[0].Value.TryGetValue(out float wear), Is.True);
            Assert.That(wear, Is.EqualTo(1.25f));
            Assert.That(response.MaterialActuals[0].Quantity, Is.EqualTo("0.0000000000000001"));
            Assert.That(response.MaterialActuals[0].EncodingMask, Is.EqualTo((uint)(
                V2.ISA95MaterialDataTypeFields.MaterialDefinitionID |
                V2.ISA95MaterialDataTypeFields.MaterialSublotID |
                V2.ISA95MaterialDataTypeFields.MaterialUse |
                V2.ISA95MaterialDataTypeFields.Quantity |
                V2.ISA95MaterialDataTypeFields.Description |
                V2.ISA95MaterialDataTypeFields.EngineeringUnits |
                V2.ISA95MaterialDataTypeFields.Properties)));
        }

        /// <summary>
        /// The source-generated serializer writes nested input arrays and preserves null versus empty.
        /// </summary>
        [Test]
        public void InputSerializationPreservesTypedChildrenAndArrayPresence()
        {
            var input = new Isa95JobOrderInput
            {
                JobOrderId = "job-serializer",
                Equipment = [],
                Description = [new Isa95TextInput { Locale = "fr", Text = "Ordre" }],
                Parameters =
                [
                    new Isa95ParameterInput
                    {
                        Id = "enabled",
                        Value = Typed(BuiltInType.Boolean, "false"),
                        Children =
                        [
                            new Isa95ParameterInput { Id = "count", Value = Typed(BuiltInType.UInt16, "17") }
                        ]
                    }
                ]
            };

            string text = JsonSerializer.Serialize(input, Isa95InputJsonContext.Default.Isa95JobOrderInput);
            using var document = JsonDocument.Parse(text);
            JsonElement json = document.RootElement;

            Assert.That(json.GetProperty("jobOrderId").GetString(), Is.EqualTo("job-serializer"));
            Assert.That(json.GetProperty("equipment").GetArrayLength(), Is.Zero);
            Assert.That(json.GetProperty("personnel").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.GetProperty("description")[0].GetProperty("locale").GetString(), Is.EqualTo("fr"));
            JsonElement parameter = json.GetProperty("parameters")[0];
            Assert.That(parameter.GetProperty("value").GetProperty("value").GetBoolean(), Is.False);
            Assert.That(parameter.GetProperty("children")[0].GetProperty("id").GetString(), Is.EqualTo("count"));
            Assert.That(parameter.GetProperty("children")[0].GetProperty("value").GetProperty("value").GetUInt16(),
                Is.EqualTo(17));
            Assert.That(parameter.GetProperty("children")[0].GetProperty("children").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
        }

        /// <summary>
        /// Input shape and size guards reject invalid values without coercion.
        /// </summary>
        [Test]
        public void TypedValueAndVersionGuardsRejectUnrepresentableInput()
        {
            IServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            Assert.That(() => Isa95InputMapper.Value(
                new Isa95TypedValueInput { DataType = BuiltInType.Int32, Value = default }, context),
                Throws.ArgumentException.With.Message.Contains("explicitly supply"));
            Assert.That(() => Isa95InputMapper.Value(Typed(BuiltInType.UInt32,
                "[" + string.Join(",", Enumerable.Repeat("0", 501)) + "]", true), context),
                Throws.ArgumentException.With.Message.Contains("500"));
            Assert.That(() => Isa95InputMapper.Value(Typed(BuiltInType.String,
                "\"" + new string('x', 262143) + "\""), context),
                Throws.ArgumentException.With.Message.Contains("262144"));
            Assert.That(Isa95InputMapper.Value(Typed(BuiltInType.Null, "null"), context).IsNull, Is.True);
            var v1 = new Isa95JobOrderInput
            {
                JobOrderId = "j",
                Description = [new Isa95TextInput { Text = "one" }, new Isa95TextInput { Text = "two" }]
            };
            Assert.That(() => Isa95InputMapper.OrderV1(v1, context),
                Throws.ArgumentException.With.Message.Contains("only one"));
            v1.Description = [];
            v1.StartTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Unspecified);
            Assert.That(() => Isa95InputMapper.OrderV1(v1, context),
                Throws.ArgumentException.With.Message.Contains("UTC or offset"));
            v1.StartTime = null;
            v1.Parameters =
            [
                new Isa95ParameterInput
                {
                    Id = "mass",
                    Value = Typed(BuiltInType.Double, "1"),
                    EngineeringUnits = new Isa95UnitInput { NamespaceUri = "urn:units", UnitId = 0 }
                }
            ];
            Assert.That(() => Isa95InputMapper.OrderV1(v1, context),
                Throws.ArgumentException.With.Message.Contains("unitOfMeasure"));
        }

        /// <summary>
        /// The array converter rejects an oversized wire array at its documented boundary.
        /// </summary>
        [Test]
        public void InputArrayConverterRejectsMoreThanFiveHundredEntries()
        {
            string entries = string.Join(",", Enumerable.Repeat(/*lang=json,strict*/ "{\"text\":\"x\"}", 501));
            string json = "{\"texts\":[" + entries + "]}";

            Assert.That(() => JsonSerializer.Deserialize(json, Isa95InputJsonContext.Default.Isa95CommentInput),
                Throws.TypeOf<JsonException>().With.Message.Contains("500 items"));
        }

        /// <summary>
        /// Normalized response-query schemas require exactly one selector and keep unrelated arguments.
        /// </summary>
        [Test]
        public void QuerySchemaRequiresExclusiveSelectorsAndBoundedStatePaths()
        {
            using var input = JsonDocument.Parse(
                """
                {"type":"object","properties":{"query":{"description":"Select a response"},
                  "sessionName":{"type":"string"}},"$defs":{"unrelated":{"type":"integer"}}}
                """);

            JsonElement schema = Isa95McpFilters.Normalize(input.RootElement);
            JsonElement query = schema.GetProperty("properties").GetProperty("query");

            Assert.That(query.GetProperty("description").GetString(), Is.EqualTo("Select a response"));
            Assert.That(query.GetProperty("oneOf").GetArrayLength(), Is.EqualTo(2));
            Assert.That(query.GetProperty("oneOf")[0].GetProperty("required")[0].GetString(), Is.EqualTo("jobOrderId"));
            Assert.That(query.GetProperty("oneOf")[0].GetProperty("not").GetProperty("required")[0].GetString(),
                Is.EqualTo("states"));
            Assert.That(query.GetProperty("oneOf")[1].GetProperty("required")[0].GetString(), Is.EqualTo("states"));
            Assert.That(query.GetProperty("oneOf")[1].GetProperty("not").GetProperty("required")[0].GetString(),
                Is.EqualTo("jobOrderId"));
            Assert.That(query.GetProperty("properties").GetProperty("states").GetProperty("maxItems").GetInt32(),
                Is.EqualTo(500));
            Assert.That(schema.GetProperty("properties").GetProperty("sessionName").GetProperty("type").GetString(),
                Is.EqualTo("string"));
            Assert.That(schema.GetProperty("$defs").GetProperty("unrelated").GetProperty("type").GetString(),
                Is.EqualTo("integer"));
            Assert.That(schema.GetProperty("$defs").TryGetProperty("isa95State", out _), Is.True);
            Assert.That(schema.GetProperty("$defs").TryGetProperty("isa95Parameter", out _), Is.False);
        }

        /// <summary>
        /// Generated status decoding handles known and vendor event types and releases only its enumeration.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StatusEventsPreserveTypedJobPayloadAndReleaseEnumeratorAsync(bool vendorSubtype)
        {
            var wire = new JobsCoverageWireSession(NUnitTelemetryContext.Create());
            var client = new Isa95Client(wire.Session.Object, NUnitTelemetryContext.Create());
            NodeId declared = wire.Local(V2.ObjectTypeIds.ISA95JobOrderStatusEventType);
            NodeId eventType = vendorSubtype ? wire.Node("VendorJobEventType") : declared;
            if (vendorSubtype)
            {
                wire.Subtype(eventType, V2.ObjectTypeIds.ISA95JobOrderStatusEventType);
            }
            var streaming = new Mock<IStreamingSubscription>(MockBehavior.Strict);
            bool disposed = false;
            EventFilter? requestedFilter = null;
            NodeId notifier = wire.Node("job-status-notifier");
            streaming.Setup(value => value.SubscribeEventsAsync(
                    notifier, It.IsAny<EventFilter>(), It.IsAny<MonitoringOptions?>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId _, EventFilter filter, MonitoringOptions? _, CancellationToken ct) =>
                {
                    requestedFilter = filter;
                    return NotificationsAsync(filter, ct);
                });

            System.Text.Json.Nodes.JsonObject observed = await McpCompanionTools.ObserveAsync(
                ct => Isa95Reads.StatusEventsAsync(client, streaming.Object, notifier, ct),
                record => Isa95Json.StatusEvent(record, wire.Context), 30000, 1).ConfigureAwait(false);

            Assert.That(observed["stoppedBy"]!.GetValue<string>(), Is.EqualTo("itemLimit"));
            Assert.That(observed["complete"]!.GetValue<bool>(), Is.False);
            Assert.That(disposed, Is.True);
            Assert.That(requestedFilter!.SelectClauses.ToArray()!.Any(clause =>
                clause.BrowsePath.Count > 0 && clause.BrowsePath[^1].Name == "JobOrder"), Is.True);
            System.Text.Json.Nodes.JsonNode item = observed["items"]![0]!;
            Assert.That(item["eventType"]!.GetValue<string>(), Is.EqualTo(eventType.ToString()));
            Assert.That(item["eventId"]!.GetValue<string>(), Is.EqualTo("BAUG"));
            Assert.That(item["sourceNode"]!.GetValue<string>(), Is.EqualTo(notifier.ToString()));
            Assert.That(item["message"]!["locale"]!.GetValue<string>(), Is.EqualTo("de"));
            Assert.That(item["time"]!.GetValue<string>(), Is.EqualTo("2026-06-01T08:30:00.0000000Z"));
            Assert.That(item["jobOrder"]!["JobOrderID"]!.GetValue<string>(), Is.EqualTo("job-17"));
            Assert.That(item["jobResponse"]!["JobResponseID"]!.GetValue<string>(), Is.EqualTo("response-17"));
            Assert.That(item["jobState"]![0]!["StateNumber"]!.GetValue<uint>(), Is.EqualTo(4));
            streaming.Verify(value => value.DisposeAsync(), Times.Never);

            async IAsyncEnumerable<EventNotification> NotificationsAsync(
                EventFilter filter,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.Yield();
                try
                {
                    var values = new Variant[filter.SelectClauses.Count];
                    for (int index = 0; index < values.Length; index++)
                    {
                        ArrayOf<QualifiedName> path = filter.SelectClauses[index].BrowsePath;
                        string? name = path.Count == 0 ? null : path[^1].Name;
                        values[index] = name switch
                        {
                            "EventType" => Variant.From(eventType),
                            "EventId" => Variant.From(new ByteString(new byte[] { 4, 5, 6 })),
                            "SourceNode" => Variant.From(notifier),
                            "SourceName" => Variant.From("Job control"),
                            "Time" => Variant.From(new DateTimeUtc(2026, 6, 1, 8, 30, 0)),
                            "Message" => Variant.From(new LocalizedText("de", "Auftrag beendet")),
                            "Severity" => Variant.From((ushort)100),
                            "JobOrder" => Variant.FromStructure(new V2.ISA95JobOrderDataType { JobOrderID = "job-17" }),
                            "JobResponse" => Variant.FromStructure(new V2.ISA95JobResponseDataType
                            {
                                JobOrderID = "job-17",
                                JobResponseID = "response-17"
                            }),
                            "JobState" => Variant.FromStructure(
                            [
                                new V2.ISA95StateDataType
                                {
                                    StateNumber = 4,
                                    StateText = new LocalizedText("en", "Ended"),
                                    BrowsePath = new RelativePath()
                                }
                            ]),
                            _ => Variant.Null
                        };
                    }
                    ct.ThrowIfCancellationRequested();
                    yield return new EventNotification(null, values.ToArrayOf());
                    Assert.Fail("The item limit must stop before requesting a second notification.");
                }
                finally
                {
                    disposed = true;
                }
            }
        }

        private static Isa95TypedValueInput Typed(BuiltInType type, string json, bool isArray = false)
        {
            using var document = JsonDocument.Parse(json);
            return new Isa95TypedValueInput
            {
                DataType = type,
                IsArray = isArray,
                Value = document.RootElement.Clone()
            };
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
