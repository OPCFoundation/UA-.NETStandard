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
using NUnit.Framework;

namespace Opc.Ua.Fuzzing
{
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class JsonRoundTripSystemicRegressionTests
    {
        [TestCaseSource(nameof(AwkwardJsonRoundTripCases))]
        public void AwkwardMessagesRoundTripThroughAllJsonModes(string name, IEncodeable message)
        {
            Assert.That(message, Is.Not.Null, name);

            foreach (JsonEncoderOptions options in FuzzableCode.JsonEncodingModes)
            {
                Assert.DoesNotThrow(
                    () => FuzzableCode.FuzzJsonRoundTripCore(message, options),
                    $"{name} failed JSON source mode {options.Name}.");
            }
        }

        [TestCaseSource(nameof(AwkwardBinaryJsonRoundTripCases))]
        public void BinarySourcedAwkwardMessagesRoundTripThroughAllJsonModes(string name, IEncodeable message)
        {
            Assert.That(message, Is.Not.Null, name);
            byte[] input = BinaryEncoder.EncodeMessage(message, FuzzableCode.MessageContext);

            Assert.DoesNotThrow(
                () => FuzzableCode.LibfuzzBinaryJsonEncoder(input),
                $"{name} failed binary-to-Verbose JSON.");
            Assert.DoesNotThrow(
                () => FuzzableCode.LibfuzzBinaryJsonEncoderCompact(input),
                $"{name} failed binary-to-Compact JSON.");
            Assert.DoesNotThrow(
                () => FuzzableCode.LibfuzzBinaryJsonEncoderRawData(input),
                $"{name} failed binary-to-RawData JSON.");
            Assert.DoesNotThrow(
                () => FuzzableCode.LibfuzzBinaryJsonEncoderLegacyReversible(input),
                $"{name} failed binary-to-LegacyReversible JSON.");
            Assert.DoesNotThrow(
                () => FuzzableCode.LibfuzzBinaryJsonEncoderLegacyNonReversible(input),
                $"{name} failed binary-to-LegacyNonReversible JSON.");
        }

        /// <summary>
        /// The legacy inline JSON body form makes a bodyless ExtensionObject and a default
        /// constructed instance of the same type serialize identically, so the decoder can only
        /// keep the envelope bodyless when the TypeId does not resolve to a registered type.
        /// Pin both halves of that deliberate asymmetry.
        /// </summary>
        [Test]
        public void BodylessExtensionObjectDecodesByTypeRegistration()
        {
            ExpandedNodeId registeredId = new ObjectAttributes().BinaryEncodingId;
            var unregisteredId = new ExpandedNodeId(new NodeId(9999, 1), Namespaces.OpcUa);

            var message = new BrowseRequest
            {
                RequestHeader = new RequestHeader
                {
                    AdditionalHeader = new ExtensionObject(registeredId)
                },
                View = new ViewDescription(),
                NodesToBrowse = ArrayOf<BrowseDescription>.Empty
            };

            string json = FuzzableCode.EncodeJsonMessage(
                message,
                FuzzableCode.LegacyReversibleOptions,
                FuzzableCode.MessageContext);

            var decoded = (BrowseRequest)FuzzableCode.FuzzJsonDecoderCore(json, true);
            ExtensionObject decodedHeader = decoded.RequestHeader.AdditionalHeader;

            Assert.That(
                decodedHeader.TryGetValue(out IEncodeable materialized),
                Is.True,
                "A registered TypeId must materialize a default instance.");
            Assert.That(materialized, Is.InstanceOf<ObjectAttributes>());
            Assert.That(Utils.IsEqual(materialized, new ObjectAttributes()), Is.True);

            var unregisteredMessage = new BrowseRequest
            {
                RequestHeader = new RequestHeader
                {
                    AdditionalHeader = new ExtensionObject(unregisteredId)
                },
                View = new ViewDescription(),
                NodesToBrowse = ArrayOf<BrowseDescription>.Empty
            };

            string unregisteredJson = FuzzableCode.EncodeJsonMessage(
                unregisteredMessage,
                FuzzableCode.LegacyReversibleOptions,
                FuzzableCode.MessageContext);

            var unregisteredDecoded =
                (BrowseRequest)FuzzableCode.FuzzJsonDecoderCore(unregisteredJson, true);
            ExtensionObject unregisteredHeader = unregisteredDecoded.RequestHeader.AdditionalHeader;

            Assert.That(
                unregisteredHeader.Encoding,
                Is.EqualTo(ExtensionObjectEncoding.None),
                "An unregistered TypeId must stay bodyless.");
            Assert.That(unregisteredHeader.TypeId.IsNull, Is.False);
        }

        /// <summary>
        /// The tolerance for the materialization above must not swallow a real loss: an
        /// ExtensionObject that carries a populated body has to survive the round-trip intact.
        /// </summary>
        [Test]
        public void PopulatedExtensionObjectBodySurvivesLegacyReversibleRoundTrip()
        {
            var message = new BrowseRequest
            {
                RequestHeader = new RequestHeader
                {
                    AdditionalHeader = new ExtensionObject(new ObjectAttributes
                    {
                        SpecifiedAttributes = 7,
                        WriteMask = 3,
                        UserWriteMask = 1,
                        EventNotifier = 5
                    })
                },
                View = new ViewDescription(),
                NodesToBrowse = ArrayOf<BrowseDescription>.Empty
            };

            string json = FuzzableCode.EncodeJsonMessage(
                message,
                FuzzableCode.LegacyReversibleOptions,
                FuzzableCode.MessageContext);

            var decoded = (BrowseRequest)FuzzableCode.FuzzJsonDecoderCore(json, true);

            Assert.That(
                decoded.RequestHeader.AdditionalHeader.TryGetValue(out IEncodeable body),
                Is.True);
            Assert.That(body, Is.InstanceOf<ObjectAttributes>());
            Assert.That(((ObjectAttributes)body).EventNotifier, Is.EqualTo(5));
            Assert.That(Utils.IsEqual(body, new ObjectAttributes()), Is.False);
        }

        private static IEnumerable<TestCaseData> AwkwardJsonRoundTripCases()
        {
            yield return new TestCaseData(
                "RegisteredServerWithNullOptionalCollections",
                new RegisteredServer
                {
                    ServerUri = "urn:fuzz:registered-server",
                    ProductUri = null,
                    ServerNames = ArrayOf<LocalizedText>.Null,
                    ServerType = ApplicationType.Server,
                    GatewayServerUri = null,
                    DiscoveryUrls = ArrayOf<string>.Null,
                    SemaphoreFilePath = null,
                    IsOnline = false
                });
            yield return new TestCaseData(
                "RegisteredServerWithEmptyAndNullCollectionElements",
                new RegisteredServer
                {
                    ServerUri = "urn:fuzz:registered-server",
                    ProductUri = string.Empty,
                    ServerNames =
                    [
                        LocalizedText.Null,
                        new LocalizedText((string)null, (string)null),
                        new LocalizedText("en", string.Empty),
                        new LocalizedText("de", "Server")
                    ],
                    ServerType = ApplicationType.ClientAndServer,
                    GatewayServerUri = string.Empty,
                    DiscoveryUrls = [null, string.Empty, "opc.tcp://localhost:4840"],
                    SemaphoreFilePath = string.Empty,
                    IsOnline = true
                });
            yield return new TestCaseData(
                "QueryDataSetWithNullVariantAndNestedArrays",
                new QueryDataSet
                {
                    NodeId = ExpandedNodeId.Null,
                    TypeDefinitionNode = ObjectTypeIds.BaseObjectType,
                    Values =
                    [
                        Variant.Null,
                        Variant.From(new ArrayOf<string>(s_stringValues)),
                        Variant.From(new ArrayOf<int>(s_matrixValues).ToMatrix(2, 2)),
                        Variant.From(new ExtensionObject(new Argument
                        {
                            Name = "Nested",
                            DataType = DataTypeIds.String,
                            ValueRank = ValueRanks.Scalar,
                            Description = LocalizedText.Null
                        }))
                    ]
                });
            yield return new TestCaseData(
                "QueryDataSetWithBodylessExtensionObjectVariant",
                new QueryDataSet
                {
                    NodeId = new ExpandedNodeId(new NodeId("query", 1), Namespaces.OpcUa),
                    TypeDefinitionNode = ObjectTypeIds.BaseObjectType,
                    Values =
                    [
                        Variant.From(new ExtensionObject(
                            new ExpandedNodeId(new NodeId(9999, 1), Namespaces.OpcUa)))
                    ]
                });
            yield return new TestCaseData(
                "QueryDataSetWithNamespaceUriExpandedNodeId",
                new QueryDataSet
                {
                    NodeId = new ExpandedNodeId(
                        new NodeId("query", 1),
                        "urn:opcfoundation:fuzzing:application"),
                    TypeDefinitionNode = ObjectTypeIds.BaseObjectType,
                    Values =
                    [
                        Variant.From(new ExtensionObject(new ExpandedNodeId(
                            new NodeId(9999, 1),
                            "urn:opcfoundation:fuzzing:application")))
                    ]
                });
            yield return new TestCaseData(
                "QueryDataSetWithNullValuesCollection",
                new QueryDataSet
                {
                    NodeId = new ExpandedNodeId(new NodeId("query", 1), Namespaces.OpcUa),
                    TypeDefinitionNode = ExpandedNodeId.Null,
                    Values = ArrayOf<Variant>.Null
                });
            yield return new TestCaseData(
                "VariableTypeNodeWithNullValueAndOptionalNodeFields",
                new VariableTypeNode
                {
                    NodeId = NodeId.Null,
                    NodeClass = NodeClass.VariableType,
                    BrowseName = new QualifiedName(string.Empty),
                    DisplayName = LocalizedText.Null,
                    Description = LocalizedText.Null,
                    Value = Variant.Null,
                    DataType = NodeId.Null,
                    ValueRank = ValueRanks.Any,
                    ArrayDimensions = ArrayOf<uint>.Null,
                    IsAbstract = true
                });
            yield return new TestCaseData(
                "VariableTypeNodeWithExtensionObjectValueAndEmptyArrays",
                new VariableTypeNode
                {
                    NodeId = new NodeId(1234, 1),
                    NodeClass = NodeClass.VariableType,
                    BrowseName = new QualifiedName("AwkwardVariableType", 1),
                    DisplayName = new LocalizedText("en", "Awkward"),
                    Description = new LocalizedText(null, string.Empty),
                    Value = Variant.From(new ExtensionObject(new Argument
                    {
                        Name = null,
                        DataType = NodeId.Null,
                        ValueRank = ValueRanks.Any,
                        ArrayDimensions = ArrayOf<uint>.Null,
                        Description = new LocalizedText((string)null, (string)null)
                    })),
                    DataType = DataTypeIds.BaseDataType,
                    ValueRank = ValueRanks.OneDimension,
                    ArrayDimensions = [],
                    IsAbstract = false,
                    References = ArrayOf<ReferenceNode>.Null,
                    RolePermissions = [],
                    UserRolePermissions = ArrayOf<RolePermissionType>.Null
                });
            yield return new TestCaseData(
                "VariableTypeNodeWithBodylessExtensionObjectValue",
                new VariableTypeNode
                {
                    NodeId = new NodeId(4321, 1),
                    NodeClass = NodeClass.VariableType,
                    BrowseName = new QualifiedName("BodylessExtensionObjectVariableType", 1),
                    DisplayName = new LocalizedText("en", "Bodyless"),
                    Description = LocalizedText.Null,
                    Value = Variant.From(new ExtensionObject(
                        new ExpandedNodeId(new NodeId(9999, 1), Namespaces.OpcUa))),
                    DataType = DataTypeIds.BaseDataType,
                    ValueRank = ValueRanks.Scalar,
                    ArrayDimensions = ArrayOf<uint>.Null,
                    IsAbstract = false
                });
            yield return new TestCaseData(
                "DatagramTransportWithNullAndBodylessExtensionObjects",
                new DatagramDataSetReaderTransportDataType
                {
                    Address = new ExtensionObject(new ExpandedNodeId(new NodeId(9999, 1), Namespaces.OpcUa)),
                    QosCategory = null,
                    DatagramQos =
                    [
                        ExtensionObject.Null,
                        new ExtensionObject(new ExpandedNodeId(new NodeId(8888, 1), Namespaces.OpcUa)),
                        new ExtensionObject(new Argument
                        {
                            Name = "Qos",
                            DataType = DataTypeIds.ByteString,
                            ValueRank = ValueRanks.Scalar,
                            Description = LocalizedText.Null
                        })
                    ],
                    Topic = string.Empty
                });
            yield return new TestCaseData(
                "BrowseRequestWithBodylessRegisteredTypeAdditionalHeader",
                new BrowseRequest
                {
                    RequestHeader = new RequestHeader
                    {
                        RequestHandle = 42,
                        AdditionalHeader = new ExtensionObject(
                            new ObjectAttributes().BinaryEncodingId)
                    },
                    View = new ViewDescription(),
                    RequestedMaxReferencesPerNode = ushort.MaxValue,
                    NodesToBrowse = ArrayOf<BrowseDescription>.Empty
                });
        }

        private static IEnumerable<TestCaseData> AwkwardBinaryJsonRoundTripCases()
        {
            foreach (TestCaseData testCase in AwkwardJsonRoundTripCases())
            {
                yield return testCase;
            }
        }

        private static readonly int[] s_matrixValues = [1, 2, 3, 4];
        private static readonly string[] s_stringValues = [string.Empty, "value"];
    }
}
