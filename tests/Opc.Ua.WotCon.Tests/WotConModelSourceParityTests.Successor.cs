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
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotConModelSourceParityTests
    {
        [TestCase(RegistryNodeSet, "f27ee860a6cb7847a808541997afb3f0f79ca259a6c7986fd359c659c9879629")]
        [TestCase(ConnectivityNodeSet, "6d68ce861f2e98e2e4e968e23cadca5f8b3f596beeda65559c11cd4a3b2f1fa5")]
        [TestCase(ConnectivityCsv, "66603011dbb48b0be6b160ed423a98a6eeb5aa57ed690a9f6529a34f52cbd2b1")]
        public void TheSuccessorInputsMatchTheReviewedGeneratedArtifacts(string fileName, string digest)
        {
            string text = System.IO.File.ReadAllText(FindModel(fileName))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            ByteString hash = WotContentDigest.Compute(System.Text.Encoding.UTF8.GetBytes(text));
            Assert.That(WotContentDigest.ToHex(hash), Is.EqualTo(digest));
        }

        [TestCase(RegistryNodeSet, RegistryNamespace)]
        [TestCase(ConnectivityNodeSet, ConnectivityNamespace)]
        public void TheSuccessorCoreDependencyMatchesThePinnedLegacyInput(string fileName, string modelUri)
        {
            XElement core = ReadModel(fileName, modelUri).Elements(UaNodeSet + "RequiredModel")
                .Single(model => model.Attribute("ModelUri")?.Value == Ua.Namespaces.OpcUa);

            Assert.That(core.Attribute("Version")?.Value, Is.EqualTo("1.05.04"));
            Assert.That(core.Attribute("PublicationDate")?.Value, Is.EqualTo("2025-01-08T00:00:00Z"));
        }

        [TestCase("ns=2;i=64609", "1:<Version>", "UAObject")]
        [TestCase("ns=2;i=64610", "1:Versions", "UAObject")]
        [TestCase("ns=2;i=64611", "1:<Version>", "UAObject")]
        [TestCase("ns=2;i=64612", "1:Versions", "UAObject")]
        [TestCase("ns=2;i=64053", "2:WoTProjectionGroupType", "UAObjectType")]
        [TestCase("ns=2;i=64661", "2:ProjectionRoot", "UAVariable")]
        [TestCase("ns=2;i=64662", "2:ProjectionMembershipDigest", "UAVariable")]
        public void SuccessorDeclarationsRetainTheirAssignedIdentityAndQualifiedName(
            string nodeId, string browseName, string nodeClass)
        {
            XElement node = XDocument.Load(FindModel(ConnectivityNodeSet)).Root!.Elements()
                .Single(element => element.Attribute("NodeId")?.Value == nodeId);

            Assert.That(node.Name.LocalName, Is.EqualTo(nodeClass));
            Assert.That(node.Attribute("BrowseName")?.Value, Is.EqualTo(browseName));
        }

        [Test]
        public void GeneratedSuccessorIdentitiesAndSelectorValuesMatchTheModel()
        {
            Assert.That((int)WoTDocumentKindEnum.ThingDescription, Is.Zero);
            Assert.That((int)WoTDocumentKindEnum.ThingModel, Is.EqualTo(1));
            Assert.That((int)WoTDocumentKindEnum.All, Is.EqualTo(2));
            Assert.That((int)WoTEventIdentityModeEnum.LocalReEmission, Is.Zero);
            Assert.That((int)WoTEventIdentityModeEnum.TransparentForwarding, Is.EqualTo(1));
            Assert.That(ObjectTypes.WoTEventBindingType, Is.EqualTo(64052u));
            Assert.That(ObjectTypes.WoTProjectionGroupType, Is.EqualTo(64053u));
            Assert.That(Methods.WoTRegistryType_CreateDocumentGroup, Is.EqualTo(64613u));
            Assert.That(Methods.WoTRegistryType_GetOrCreateDocumentGroup, Is.EqualTo(64616u));
            Assert.That(Methods.WoTEventBindingType_GetEventProvenance, Is.EqualTo(64656u));
            Assert.That(Variables.WoTProjectionGroupType_ProjectionRoot, Is.EqualTo(64661u));
            Assert.That(Variables.WoTDocumentType_ProjectionMembershipDigest, Is.EqualTo(64662u));
        }

        [Test]
        public void CanonicalCapabilitiesRetainOptionalPresenceInNativeEncoding()
        {
            var value = new RegistryCapabilitiesSnapshotDataType
            {
                EncodingMask = (uint)(RegistryCapabilitiesSnapshotDataTypeFields.Pagination |
                    RegistryCapabilitiesSnapshotDataTypeFields.Flags),
                Pagination = false,
                Flags = ["pagination"]
            };
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);
            byte[] bytes;
            using (var encoder = new BinaryEncoder(context))
            {
                value.Encode(encoder);
                bytes = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(bytes, context);
            var decoded = new RegistryCapabilitiesSnapshotDataType();
            decoded.Decode(decoder);

            Assert.That(decoded.TypeId, Is.EqualTo(new ExpandedNodeId(63029, RegistryNamespace)));
            Assert.That(decoded.BinaryEncodingId, Is.EqualTo(new ExpandedNodeId(63598, RegistryNamespace)));
            Assert.That(decoded.XmlEncodingId.IsNull, Is.True);
            Assert.That(decoded.EncodingMask, Is.EqualTo(value.EncodingMask));
            Assert.That(decoded.Pagination, Is.False);
            Assert.That(decoded.Flags, Is.EqualTo(value.Flags));
            Assert.That(decoded.EncodingMask & (uint)RegistryCapabilitiesSnapshotDataTypeFields.ShortSelf, Is.Zero);
        }

        [Test]
        public void EventProvenanceRetainsPresentNullSourceFactsInNativeEncoding()
        {
            var value = new WoTEventOriginDataType
            {
                EncodingMask = (uint)(WoTEventOriginDataTypeFields.SourceEventId |
                    WoTEventOriginDataTypeFields.SourceBranchId),
                IdentityMode = WoTEventIdentityModeEnum.TransparentForwarding,
                BindingId = "binding",
                Generation = 7,
                SourceDocumentId = "urn:test:source",
                SourceEventId = default,
                SourceBranchId = ExpandedNodeId.Null,
                ReceiveTime = new DateTimeUtc(new DateTime(2026, 9, 12, 1, 2, 3, DateTimeKind.Utc))
            };
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);
            byte[] bytes;
            using (var encoder = new BinaryEncoder(context))
            {
                value.Encode(encoder);
                bytes = encoder.CloseAndReturnBuffer()!;
            }
            using var decoder = new BinaryDecoder(bytes, context);
            var decoded = new WoTEventOriginDataType();
            decoded.Decode(decoder);

            Assert.That(decoded.TypeId, Is.EqualTo(new ExpandedNodeId(64051, ConnectivityNamespace)));
            Assert.That(decoded.BinaryEncodingId, Is.EqualTo(new ExpandedNodeId(64648, ConnectivityNamespace)));
            Assert.That(decoded.XmlEncodingId.IsNull, Is.True);
            Assert.That(decoded.EncodingMask, Is.EqualTo(value.EncodingMask));
            Assert.That(decoded.IdentityMode, Is.EqualTo(WoTEventIdentityModeEnum.TransparentForwarding));
            Assert.That(decoded.BindingId, Is.EqualTo("binding"));
            Assert.That(decoded.Generation, Is.EqualTo(7));
            Assert.That(decoded.SourceDocumentId, Is.EqualTo("urn:test:source"));
            Assert.That(decoded.SourceEventId.IsNull, Is.True);
            Assert.That(decoded.SourceBranchId.IsNull, Is.True);
            Assert.That(decoded.ReceiveTime, Is.EqualTo(value.ReceiveTime));
            Assert.That(decoded.EncodingMask & (uint)WoTEventOriginDataTypeFields.SourceTime, Is.Zero);
        }

        [TestCase("createGroup", "Kind,CatalogUri", "64020,23751", "GroupNodeId,AssignedGroupId", "17,12")]
        [TestCase("getGroup", "Kind,CatalogUri", "64020,23751", "GroupNodeId,AssignedGroupId,Created", "17,12,1")]
        [TestCase("createTd", "ThingId,VersionId,RequestFileOpen", "23751,12,1",
            "LogicalResourceNodeId,VersionNodeId,AssignedResourceId,AssignedVersionId,FileHandle", "17,17,12,12,7")]
        [TestCase("createTm", "ModelId,VersionId,RequestFileOpen", "23751,12,1",
            "LogicalResourceNodeId,VersionNodeId,AssignedResourceId,AssignedVersionId,FileHandle", "17,17,12,12,7")]
        [TestCase("getTd", "ThingId,VersionId,RequestFileOpen", "23751,12,1",
            "LogicalResourceNodeId,VersionNodeId,AssignedResourceId,AssignedVersionId,FileHandle," +
            "CreatedResource,CreatedVersion",
            "17,17,12,12,7,1,1")]
        [TestCase("getTm", "ModelId,VersionId,RequestFileOpen", "23751,12,1",
            "LogicalResourceNodeId,VersionNodeId,AssignedResourceId,AssignedVersionId,FileHandle," +
            "CreatedResource,CreatedVersion",
            "17,17,12,12,7,1,1")]
        [TestCase("provenance", "EventId", "15", "Origin", "64051")]
        public void GeneratedSuccessorMethodStatesRetainEveryOrderedNativeArgument(
            string name, string inputNames, string inputTypes, string outputNames, string outputTypes)
        {
            var context = new SystemContext(null!) { NamespaceUris = new NamespaceTable() };
            context.NamespaceUris.GetIndexOrAppend("urn:test:unrelated");
            context.NamespaceUris.GetIndexOrAppend(ConnectivityNamespace);
            context.NamespaceUris.GetIndexOrAppend(RegistryNamespace);
            MethodState method = name switch
            {
                "createGroup" => context.CreateInstanceOfCreateDocumentGroupMethodType(),
                "getGroup" => context.CreateInstanceOfGetOrCreateDocumentGroupMethodType(),
                "createTd" => context.CreateInstanceOfCreateThingDescriptionResourceMethodType(),
                "createTm" => context.CreateInstanceOfCreateThingModelResourceMethodType(),
                "getTd" => context.CreateInstanceOfGetOrCreateThingDescriptionResourceMethodType(),
                "getTm" => context.CreateInstanceOfGetOrCreateThingModelResourceMethodType(),
                "provenance" => context.CreateInstanceOfGetEventProvenanceMethodType(),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };

            Assert.That(method.InputArguments, Is.Not.Null);
            Assert.That(method.OutputArguments, Is.Not.Null);
            AssertSignature(method.InputArguments!.Value, inputNames, inputTypes, context.NamespaceUris);
            AssertSignature(method.OutputArguments!.Value, outputNames, outputTypes, context.NamespaceUris);
        }

        [Test]
        public void GeneratedProjectionAndEventBindingNodesRetainRequiredTypedMembers()
        {
            var context = new SystemContext(null!) { NamespaceUris = new NamespaceTable() };
            context.NamespaceUris.GetIndexOrAppend("urn:test:unrelated");
            ushort ns = context.NamespaceUris.GetIndexOrAppend(ConnectivityNamespace);
            context.NamespaceUris.GetIndexOrAppend(RegistryNamespace);
            WoTProjectionGroupState group = context.CreateInstanceOfWoTProjectionGroupType();
            WoTEventBindingState binding = context.CreateInstanceOfWoTEventBindingType();

            Assert.That(group.TypeDefinitionId, Is.EqualTo(new NodeId(64053, ns)));
            Assert.That(group.ProjectionRoot, Is.Not.Null);
            Assert.That(group.ProjectionRoot!.DataType, Is.EqualTo(Ua.DataTypeIds.NodeId));
            Assert.That(group.ProjectionRoot.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(group.ProjectionRoot.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(group.ProjectionRoot.ModellingRuleId.IsNull, Is.True,
                "An instantiated child does not retain the declaration-only modelling rule.");
            Assert.That(binding.TypeDefinitionId, Is.EqualTo(new NodeId(64052, ns)));
            Assert.That(binding.IdentityMode!.DataType, Is.EqualTo(new NodeId(64050, ns)));
            Assert.That(binding.Availability!.DataType, Is.EqualTo(Ua.DataTypeIds.StatusCode));
            Assert.That(binding.SourceServerUri, Is.Null);
            Assert.That(binding.GetEventProvenance, Is.Not.Null);
            AssertSignature(binding.GetEventProvenance!.InputArguments!.Value, "EventId", "15", context.NamespaceUris);
            AssertSignature(
                binding.GetEventProvenance.OutputArguments!.Value, "Origin", "64051", context.NamespaceUris);
        }

        private static void AssertSignature(
            ArrayOf<Argument> arguments, string names, string types, NamespaceTable namespaces)
        {
            string[] expectedNames = names.Split(',');
            string[] expectedTypes = types.Split(',');
            Assert.That(arguments.Count, Is.EqualTo(expectedNames.Length));
            for (int index = 0; index < arguments.Count; index++)
            {
                uint id = uint.Parse(expectedTypes[index], CultureInfo.InvariantCulture);
                var dataType = new NodeId(
                    id, id >= 64000 ? namespaces.GetIndexOrAppend(ConnectivityNamespace) : (ushort)0);
                Assert.That(arguments[index].Name, Is.EqualTo(expectedNames[index]));
                Assert.That(arguments[index].DataType, Is.EqualTo(dataType));
                Assert.That(arguments[index].ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(arguments[index].ArrayDimensions.Count, Is.Zero);
            }
        }
    }
}
