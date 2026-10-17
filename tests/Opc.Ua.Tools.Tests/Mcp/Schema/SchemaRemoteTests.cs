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
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Schema;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Deterministic remote-service tests beneath the real current-session manager and SDK type loader.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class SchemaRemoteTests
    {
        /// <summary>
        /// Inspection separates NodeId and BrowseName namespaces and preserves the declared array rank.
        /// </summary>
        [Test]
        public async Task RemoteInspectionPreservesIdentityAndRankAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            NodeId id = remote.Id("Record");
            ushort nameNamespace = remote.Context.NamespaceUris.GetIndexOrAppend("urn:other:labels");
            SetType(remote, id, new QualifiedName("Record", nameNamespace),
                SchemaTestContext.Structure(SchemaTestContext.Field("Values", DataTypeIds.Int32, 3)));

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).InspectTypeAsync(
                new SchemaInspectInput { TypeId = id.ToString(), Source = SchemaTypeSource.Session }, "selected")
                .ConfigureAwait(false));

            Assert.That(result["typeId"]!.GetValue<string>(), Is.EqualTo(id.ToString()));
            Assert.That(result["namespaceUri"]!.GetValue<string>(), Is.EqualTo("urn:test:remote-model"));
            Assert.That(result["browseName"]!.GetValue<string>(), Is.EqualTo(nameNamespace + ":Record"));
            Assert.That(result["definition"]!["Fields"]![0]!["ValueRank"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(remote.Reads.Select(read => read.AttributeId), Is.EqualTo(
            [
                Attributes.BrowseName, Attributes.DataTypeDefinition, Attributes.IsAbstract
            ]));
            Assert.That(remote.ReadTimestamps, Is.All.EqualTo(TimestampsToReturn.Both));
            Assert.That(remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Both bad and uncertain DataTypeDefinition quality remain explicit inspection errors.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InspectionPreservesNonGoodAttributeStatusAsync(bool uncertain)
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            NodeId id = remote.Id("Record");
            SetType(remote, id, new QualifiedName("Record"), SchemaTestContext.Structure());
            StatusCode status = uncertain ? StatusCodes.Uncertain : StatusCodes.BadAttributeIdInvalid;
            remote.Values[(id, Attributes.DataTypeDefinition)] = DataValue.FromStatusCode(status);

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).InspectTypeAsync(
                new SchemaInspectInput { TypeId = id.ToString(), Source = SchemaTypeSource.Session }, "selected")
                .ConfigureAwait(false), error: true);

            Assert.That(result["attributes"]!["dataTypeDefinition"]!["Status"]!["Code"]!.GetValue<uint>(),
                Is.EqualTo(status.Code));
            Assert.That(result.ContainsKey("definition"), Is.False);
        }

        /// <summary>
        /// The actual supertype, rather than IsOptionSet alone, determines structure versus integer wire shape.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RemoteOptionSetClassificationUsesBoundedSupertypeAsync(bool structure)
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            NodeId id = remote.Id("Flags");
            SetType(remote, id, new QualifiedName("Flags", remote.InstanceNamespace), new EnumDefinition
            {
                IsOptionSet = true,
                Fields = [new EnumField { Name = "Enabled", Value = 0 }]
            });
            remote.Browse(id, ReferenceTypeIds.HasSubtype,
                [Reference(structure ? DataTypeIds.OptionSet : DataTypeIds.UInt64)], BrowseDirection.Inverse);

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).InspectTypeAsync(
                new SchemaInspectInput { TypeId = id.ToString(), Source = SchemaTypeSource.Session }, "selected")
                .ConfigureAwait(false));

            Assert.That(result["isStructureOptionSet"]!.GetValue<bool>(), Is.EqualTo(structure));
            Assert.That(remote.Browses, Has.Count.EqualTo(1));
            Assert.That(remote.Browses[0].BrowseDirection, Is.EqualTo(BrowseDirection.Inverse));
            Assert.That(remote.Browses[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasSubtype));
        }

        /// <summary>
        /// Destination policy is applied before session type reads, even for an otherwise valid selected session.
        /// </summary>
        [Test]
        public async Task OutputPreflightPrecedesRemoteReadsAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(local.Root, "taken.json"), "keep").ConfigureAwait(false);
            var input = new SchemaGenerateInput
            {
                TypeId = remote.Id("Record").ToString(),
                Source = SchemaTypeSource.Session
            };

            SchemaTestContext.Payload(await Tools(local, remote).GenerateAsync(input, "taken.json", "selected")
                .ConfigureAwait(false), error: true);

            Assert.That(remote.Reads, Is.Empty);
            Assert.That(remote.Browses, Is.Empty);
            Assert.That(await File.ReadAllTextAsync(Path.Combine(local.Root, "taken.json")).ConfigureAwait(false),
                Is.EqualTo("keep"));
        }

        /// <summary>
        /// List lookahead releases a live browse continuation point rather than leaking server resources.
        /// </summary>
        [Test]
        public async Task TypeListPagesAndReleasesContinuationAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            remote.Browse(DataTypeIds.BaseDataType, ReferenceTypeIds.HasSubtype,
                [Reference(remote.Id("First")), Reference(remote.Id("Second"))], continuation: ByteString.From([42]));

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).ListTypesAsync(
                new SchemaListInput { Source = SchemaTypeSource.Session, MaxResults = 1 }, "selected")
                .ConfigureAwait(false));

            Assert.That(result["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(result["items"]![0]!["nodeId"]!.GetValue<string>(), Is.EqualTo(remote.Id("First").ToString()));
            Assert.That(result["nextOffset"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(result["hasMore"]!.GetValue<bool>(), Is.True);
            Assert.That(remote.Continuations, Has.Count.EqualTo(1));
            Assert.That(remote.Continuations[0].Release, Is.True);
            Assert.That(remote.Continuations[0].Point, Is.EqualTo(ByteString.From([42])));
        }

        /// <summary>
        /// Oversized namespace enumeration fails before definitions or schema artifacts are expanded.
        /// </summary>
        [Test]
        public async Task NamespaceCountIsCheckedBeforeDefinitionReadsAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            NodeId id = remote.Id("First");
            remote.Browse(DataTypeIds.BaseDataType, ReferenceTypeIds.HasSubtype,
                [Reference(id), Reference(remote.Id("Second"))]);

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote,
                new McpSchemaOptions { MaxTypes = 1 }).GenerateAsync(new SchemaGenerateInput
                {
                    TypeId = id.ToString(),
                    Source = SchemaTypeSource.Session,
                    Scope = UaSchemaScope.Namespace
                }, "namespace.json", "selected").ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(remote.Reads, Is.Empty);
            Assert.That(Directory.GetFiles(local.Root), Is.Empty);
        }

        /// <summary>
        /// Client loading uses the real AOT-safe factory and never browses the BaseDataType tree or dictionaries.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitLoadingUsesFiniteDefaultFactoryAsync(bool includeEnumeration)
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            remote.Session.SetupGet(session => session.Factory).Returns(remote.Context.Factory);
            NodeId id = remote.Id("Record");
            NodeId encoding = remote.Id("RecordBinary");
            StructureDefinition definition = SchemaTestContext.Structure(
                SchemaTestContext.Field("Count", DataTypeIds.Int32));
            if (includeEnumeration)
            {
                NodeId enumeration = remote.Id("Mode");
                SetType(remote, enumeration, new QualifiedName("Mode", remote.InstanceNamespace), new EnumDefinition
                {
                    Fields =
                    [
                        new EnumField { Name = "Stopped", Value = 0 },
                        new EnumField { Name = "Running", Value = 1 }
                    ]
                });
                definition.Fields = definition.Fields.AddItem(SchemaTestContext.Field("Mode", enumeration));
            }
            definition.DefaultEncodingId = encoding;
            SetType(remote, id, new QualifiedName("Record", remote.InstanceNamespace), definition);
            remote.Browse(id, ReferenceTypeIds.HasEncoding,
            [
                new ReferenceDescription
                {
                    NodeId = encoding,
                    BrowseName = new QualifiedName(BrowseNames.DefaultBinary),
                    NodeClass = NodeClass.Object
                }
            ]);

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).LoadTypesAsync(
                new SchemaLoadInput { TypeIds = [id.ToString()] }, "selected").ConfigureAwait(false));

            Assert.That(result["results"]![0]!["loaded"]!.GetValue<bool>(), Is.True);
            Assert.That(result["factory"]!.GetValue<string>(), Is.EqualTo(nameof(DefaultComplexTypeFactory)));
            Assert.That(remote.Context.Factory.TryGetEncodeableType(
                NodeId.ToExpandedNodeId(id, remote.Context.NamespaceUris), out IEncodeableType? type), Is.True);
            Assert.That(type!.Type.Assembly.IsDynamic, Is.False);
            Assert.That(type.CreateInstance().TypeId,
                Is.EqualTo(NodeId.ToExpandedNodeId(id, remote.Context.NamespaceUris)));
            Assert.That(remote.Browses.Select(browse => browse.ReferenceTypeId),
                Is.All.EqualTo(ReferenceTypeIds.HasEncoding));
            Assert.That(remote.Browses.Select(browse => browse.NodeId), Is.All.EqualTo(id));
        }

        /// <summary>
        /// A partial client-side outcome retains the successfully loaded root and reports an unsupported sibling.
        /// </summary>
        [Test]
        public async Task LoadingReportsPartialRegistrationWithoutInventingAtomicityAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            remote.Session.SetupGet(session => session.Factory).Returns(remote.Context.Factory);
            NodeId good = remote.Id("Good");
            NodeId unsupported = remote.Id("Unsupported");
            foreach (NodeId id in new[] { good, unsupported })
            {
                NodeId encoding = remote.Id(id == good ? "GoodBinary" : "UnsupportedBinary");
                StructureDefinition definition = SchemaTestContext.Structure(
                    SchemaTestContext.Field("Count", DataTypeIds.Int32,
                        id == good ? ValueRanks.Scalar : ValueRanks.ScalarOrOneDimension));
                definition.DefaultEncodingId = encoding;
                SetType(remote, id, new QualifiedName(id == good ? "Good" : "Unsupported", remote.InstanceNamespace),
                    definition);
                remote.Browse(id, ReferenceTypeIds.HasEncoding,
                [
                    new ReferenceDescription
                    {
                        NodeId = encoding,
                        BrowseName = new QualifiedName(BrowseNames.DefaultBinary),
                        NodeClass = NodeClass.Object
                    }
                ]);
            }

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).LoadTypesAsync(
                new SchemaLoadInput { TypeIds = [good.ToString(), unsupported.ToString()] }, "selected")
                .ConfigureAwait(false), error: true);

            Assert.That(result["results"]![0]!["loaded"]!.GetValue<bool>(), Is.True);
            Assert.That(result["results"]![1]!["loaded"]!.GetValue<bool>(), Is.False);
            Assert.That(result["results"]![1]!["statusCode"]!.GetValue<string>(), Is.EqualTo("BadNotSupported"));
            Assert.That(remote.Context.Factory.TryGetEncodeableType(
                NodeId.ToExpandedNodeId(good, remote.Context.NamespaceUris), out _), Is.True);
            Assert.That(remote.Context.Factory.TryGetEncodeableType(
                NodeId.ToExpandedNodeId(unsupported, remote.Context.NamespaceUris), out _), Is.False);
        }

        /// <summary>
        /// Missing binary encodings fail preflight and do not register even the selected root.
        /// </summary>
        [Test]
        public async Task LoadingWithoutEncodingDoesNotMutateFactoryAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            remote.Session.SetupGet(session => session.Factory).Returns(remote.Context.Factory);
            NodeId id = remote.Id("Record");
            SetType(remote, id, new QualifiedName("Record"), SchemaTestContext.Structure());

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).LoadTypesAsync(
                new SchemaLoadInput { TypeIds = [id.ToString()] }, "selected").ConfigureAwait(false), error: true);

            Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(remote.Context.Factory.TryGetEncodeableType(
                NodeId.ToExpandedNodeId(id, remote.Context.NamespaceUris), out _), Is.False);
            Assert.That(remote.Calls, Is.Empty);
        }

        /// <summary>
        /// A tool instance resolves the new session object after the same name is replaced.
        /// </summary>
        [Test]
        public async Task ReplacedSessionDoesNotReuseOldDefinitionProxyAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession first =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            await using RemoteModelTestSession second =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            NodeId id = first.Id("Record");
            SetType(first, id, new QualifiedName("First"), SchemaTestContext.Structure());
            SetType(second, id, new QualifiedName("Second"), SchemaTestContext.Structure());
            SchemaTools tools = Tools(local, first);
            var input = new SchemaInspectInput { TypeId = id.ToString(), Source = SchemaTypeSource.Session };
            JsonObject before = SchemaTestContext.Payload(await tools.InspectTypeAsync(input, "selected")
                .ConfigureAwait(false));
            await first.Manager.RegisterExistingSessionAsync("selected", second.Session.Object, "Anonymous")
                .ConfigureAwait(false);

            JsonObject after = SchemaTestContext.Payload(await tools.InspectTypeAsync(input, "selected")
                .ConfigureAwait(false));

            Assert.That(before["name"]!.GetValue<string>(), Is.EqualTo("First"));
            Assert.That(after["name"]!.GetValue<string>(), Is.EqualTo("Second"));
            Assert.That(first.Reads, Has.Count.EqualTo(3));
            Assert.That(second.Reads, Has.Count.EqualTo(3));
        }

        /// <summary>
        /// A stalled mocked read receives the operation deadline and leaves no artifact.
        /// </summary>
        [Test]
        public async Task HostDeadlineCancelsRemoteReadWithoutPublishingAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            remote.Session.Setup(session => session.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> _,
                    CancellationToken token) => WaitForCancellationAsync(token));

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote,
                new McpSchemaOptions { OperationTimeout = TimeSpan.FromMilliseconds(20) }).GenerateAsync(
                new SchemaGenerateInput
                {
                    TypeId = remote.Id("Record").ToString(),
                    Source = SchemaTypeSource.Session
                }, "timeout.json", "selected").ConfigureAwait(false), error: true);

            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo(nameof(TimeoutException)));
            Assert.That(Directory.GetFiles(local.Root), Is.Empty);
        }

        /// <summary>
        /// Remote metadata quality is preserved rather than allowing stale fields to produce a successful artifact.
        /// </summary>
        [Test]
        public async Task NonGoodPubSubMetadataIsNotGeneratedAsync()
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            NodeId id = remote.Id("Metadata");
            remote.Values[(id, Attributes.Value)] = DataValue.FromStatusCode(StatusCodes.BadNotReadable);

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).GeneratePubSubAsync(
                new SchemaPubSubInput
                {
                    Source = SchemaTypeSource.Session,
                    MetadataNodeId = id.ToString(),
                    FieldContentMask = 0,
                    MessageContentMask = 0,
                    NetworkContentMask = 0
                }, "metadata.json", "selected").ConfigureAwait(false), error: true);

            Assert.That(result["dataValue"]!["Status"]!["Code"]!.GetValue<uint>(),
                Is.EqualTo(StatusCodes.BadNotReadable));
            Assert.That(Directory.GetFiles(local.Root), Is.Empty);
        }

        /// <summary>
        /// Actual DataSetMetaData values preserve array rank and reject contradictory built-in hints.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RemotePubSubMetadataPreservesRanksAndChecksHintsAsync(bool contradictory)
        {
            await using var local = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            NodeId id = remote.Id("Metadata");
            var metadata = new DataSetMetaDataType
            {
                Name = "Readings",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Samples",
                        DataType = DataTypeIds.Double,
                        BuiltInType = (byte)(contradictory ? BuiltInType.Int32 : BuiltInType.Double),
                        ValueRank = 2
                    }
                ]
            };
            remote.Values[(id, Attributes.Value)] = new DataValue(Variant.From(new ExtensionObject(metadata)));

            JsonObject result = SchemaTestContext.Payload(await Tools(local, remote).GeneratePubSubAsync(
                new SchemaPubSubInput
                {
                    Source = SchemaTypeSource.Session,
                    MetadataNodeId = id.ToString(),
                    FieldContentMask = 0,
                    MessageContentMask = 0,
                    NetworkContentMask = 0
                }, "metadata.json", "selected").ConfigureAwait(false), error: contradictory);

            Assert.That(remote.Reads.Select(read => read.AttributeId), Is.EqualTo([Attributes.Value]));
            if (contradictory)
            {
                Assert.That(result["statusCodeValue"]!.GetValue<uint>(), Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(Directory.GetFiles(local.Root), Is.Empty);
            }
            else
            {
                JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(
                    Path.Combine(local.Root, "metadata.json")).ConfigureAwait(false))!;
                Assert.That(document["properties"]!["Samples"]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
                Assert.That(document["properties"]!["Samples"]!["items"]!["type"]!.GetValue<string>(),
                    Is.EqualTo("array"));
                Assert.That(result["definitionCount"]!.GetValue<int>(), Is.Zero);
            }
        }

        /// <summary>
        /// Creates tools over the actual selected manager while reusing the local test's artifact policy.
        /// </summary>
        private static SchemaTools Tools(
            SchemaTestContext local, RemoteModelTestSession remote, McpSchemaOptions? options = null)
        {
            return new SchemaTools(remote.Manager, new McpFileTransfers(local.Shared), local.Shared,
                options ?? new McpSchemaOptions(), local.Context, local.Registry);
        }

        /// <summary>
        /// Supplies the three concrete DataType attributes used by bounded discovery.
        /// </summary>
        private static void SetType(
            RemoteModelTestSession remote, NodeId id, QualifiedName name, DataTypeDefinition definition)
        {
            remote.Values[(id, Attributes.BrowseName)] = new DataValue(Variant.From(name));
            remote.Values[(id, Attributes.DataTypeDefinition)] =
                new DataValue(Variant.From(new ExtensionObject(definition)));
            remote.Values[(id, Attributes.IsAbstract)] = new DataValue(Variant.From(false));
        }

        /// <summary>
        /// Creates one server-advertised DataType reference.
        /// </summary>
        private static ReferenceDescription Reference(NodeId id)
        {
            return new ReferenceDescription
            {
                NodeId = id,
                BrowseName = new QualifiedName(id.ToString()),
                NodeClass = NodeClass.DataType
            };
        }

        /// <summary>
        /// Models a stalled server without timers, sleeps or public-network access of its own.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static async ValueTask<ReadResponse> WaitForCancellationAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            throw new InvalidOperationException("A cancellation-only read cannot complete successfully.");
        }
    }
}
#endif
