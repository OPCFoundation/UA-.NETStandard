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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using UaLens.Plugins.PubSub;
using UaLens.StructuredValues;

namespace UaLens.Tests.PubSub
{
    [TestFixture]
    public sealed class PubSubSchemaMappingTests
    {
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.QualifiedName)]
        public void NamespaceBearingMappingsRequireTheAdaptersActualNamespaceContext(BuiltInType type)
        {
            var test = new MappingContext(BuiltInConfiguration(type, 1));
            Assert.That(() => new PubSubSchemaMapping(test.Session.Object, test.Configuration, [new NodeId(100u, 2)],
                test.Values.MessageContext, remote: null, write: true),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("namespace context"));
            test.Session.VerifyNoOtherCalls();
        }

        [TestCase(BuiltInType.NodeId, -1)]
        [TestCase(BuiltInType.NodeId, 1)]
        [TestCase(BuiltInType.NodeId, 2)]
        [TestCase(BuiltInType.ExpandedNodeId, -1)]
        [TestCase(BuiltInType.ExpandedNodeId, 1)]
        [TestCase(BuiltInType.ExpandedNodeId, 2)]
        [TestCase(BuiltInType.QualifiedName, -1)]
        [TestCase(BuiltInType.QualifiedName, 1)]
        [TestCase(BuiltInType.QualifiedName, 2)]
        public async Task BuiltInNamespaceValuesKeepTheirMeaningAcrossAdapterTablesAsync(BuiltInType type, int rank)
        {
            var test = new MappingContext(BuiltInConfiguration(type, rank));
            using PubSubSchemaMapping mapping = test.Create();
            await mapping.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
            Variant local = NamespaceValue(type, rank, 1, test.Values.MessageContext);
            Variant remote = mapping.ToRemote(local);
            Assert.That(remote, Is.EqualTo(NamespaceValue(type, rank, 2, test.RemoteContext)));
            Assert.That(remote.TypeInfo.ValueRank, Is.EqualTo(rank));
            Assert.That(mapping.FromRemote(0, remote), Is.EqualTo(local));
            test.Session.Verify(session => session.DisposeAsync(), Times.Never);
        }

        [TestCase(BuiltInType.NodeId, false)]
        [TestCase(BuiltInType.NodeId, true)]
        [TestCase(BuiltInType.ExpandedNodeId, false)]
        [TestCase(BuiltInType.ExpandedNodeId, true)]
        [TestCase(BuiltInType.QualifiedName, false)]
        [TestCase(BuiltInType.QualifiedName, true)]
        public void ARemoteNamespaceOutsideTheReviewedMappingCannotBecomeANumericIndex(
            BuiltInType type, bool unknownSource)
        {
            var test = new MappingContext(BuiltInConfiguration(type, 1));
            using PubSubSchemaMapping mapping = test.Create();
            Variant remote = NamespaceValue(type, 1, unknownSource ? (ushort)9 : (ushort)1, test.RemoteContext);
            Assert.That(() => mapping.FromRemote(0, remote),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ALocalNamespaceMissingFromTheTargetCannotReachWriteBack()
        {
            PubSubConfiguration configuration = BuiltInConfiguration(BuiltInType.NodeId, 1) with
            {
                Namespaces = [Namespaces.OpcUa, Model, "urn:ualens:test:unmapped"]
            };
            var test = new MappingContext(configuration);
            using PubSubSchemaMapping mapping = test.Create();
            Variant local = NamespaceValue(BuiltInType.NodeId, 1, 2, test.Values.MessageContext);
            Assert.That(() => mapping.ToRemote(local),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ExpandedNodeServerIndexesCannotBeReinterpretedWithoutAServerTableContract()
        {
            var test = new MappingContext(BuiltInConfiguration(BuiltInType.ExpandedNodeId, ValueRanks.Scalar));
            using PubSubSchemaMapping mapping = test.Create();
            Variant value = Variant.From(new ExpandedNodeId(new NodeId(99u, 1), null, 1));
            Assert.That(() => mapping.ToRemote(value),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task AChangedServerNamespaceTableInvalidatesEvenAProviderSnapshotAsync()
        {
            var test = new MappingContext();
            using PubSubSchemaMapping mapping = test.Create();
            await mapping.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
            test.Change = "server-namespaces";
            await Assert.ThatAsync(() => mapping.ValidateAsync(CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);
            Assert.That(test.RemoteContext.NamespaceUris.GetString(1), Is.EqualTo("urn:namespace-padding"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SourceSamplesAreRemappedOnlyWhileTheReviewedSchemaStaysCurrentAsync(bool changes)
        {
            var test = new MappingContext();
            using PubSubSchemaMapping mapping = test.Create();
            NodeId type = new(5001u, 1);
            DataTypeDefinition definition = await test.Values.ResolveAsync(type).ConfigureAwait(false) ??
                throw new AssertionException("Missing type.");
            StructuredValueDraft draft = await test.Values.OpenAsync(type, definition, Variant.Null)
                .ConfigureAwait(false);
            Assert.That(draft.TryCommit([new("Source", Variant.From(new NodeId(99u, 1)))],
                out Variant payload, out string? error), Is.True, error);
            Variant remote = mapping.ToRemote(payload);
            DataSetMetaDataType metadata = PubSubStackConfiguration.CreateMetadata(test.Configuration);
            var source = new Mock<IPublishedDataSetSource>(MockBehavior.Strict);
            source.Setup(value => value.SampleAsync(
                It.IsAny<DataSetMetaDataType>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (changes)
                    {
                        test.Change = "definition";
                    }
                    return new ValueTask<PublishedDataSetSnapshot>(new PublishedDataSetSnapshot(
                        metadata.ConfigurationVersion, [new DataSetField { Name = "Reading", Value = remote }],
                        new DateTimeUtc(2026, 10, 11)));
                });
            var guarded = new PubSubSchemaSource(source.Object, mapping, metadata);
            if (changes)
            {
                await Assert.ThatAsync(() => guarded.SampleAsync(guarded.BuildMetaData()).AsTask(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState))
                    .ConfigureAwait(false);
            }
            else
            {
                PublishedDataSetSnapshot sample = await guarded.SampleAsync(guarded.BuildMetaData())
                    .ConfigureAwait(false);
                Assert.That(sample.Fields[0].Value, Is.EqualTo(payload));
                Assert.That(sample.SampledAt, Is.EqualTo(new DateTimeUtc(2026, 10, 11)));
            }
            source.Verify(value => value.SampleAsync(
                It.IsAny<DataSetMetaDataType>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task MappedStructuresRetainPortableTypeAndNestedNodeIdentityAcrossNamespaceTablesAsync()
        {
            var test = new MappingContext();
            using PubSubSchemaMapping mapping = test.Create();
            await mapping.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
            NodeId type = PubSubStructureSchema.FieldType(test.Configuration.Fields[0],
                test.Values.MessageContext.NamespaceUris);
            DataTypeDefinition definition = await test.Values.ResolveAsync(type).ConfigureAwait(false) ??
                throw new AssertionException("The declared type is missing.");
            StructuredValueDraft draft = await test.Values.OpenAsync(type, definition, Variant.Null)
                .ConfigureAwait(false);
            Assert.That(draft.TryCommit([new("Source", Variant.From(new NodeId(99u, 1)))],
                out Variant payload, out string? error), Is.True, error);
            Variant remote = mapping.ToRemote(payload);
            Assert.That(remote.TryGetValue<IEncodeable>(out IEncodeable? body, test.RemoteContext), Is.True);
            Assert.That(((IStructure)body!)["Source"], Is.EqualTo(Variant.From(new NodeId(99u, 2))));
            Variant returned = mapping.FromRemote(0, remote);
            Assert.That(returned.TryGetValue<IEncodeable>(out IEncodeable? local, test.Values.MessageContext), Is.True);
            Assert.That(((IStructure)local!)["Source"], Is.EqualTo(Variant.From(new NodeId(99u, 1))));
            Assert.That(test.RemoteContext.NamespaceUris.Count, Is.EqualTo(3));
            Assert.That(test.Values.MessageContext.NamespaceUris.Count, Is.EqualTo(2));
        }

        [TestCase("type")]
        [TestCase("rank")]
        [TestCase("dimensions")]
        [TestCase("definition")]
        [TestCase("denied")]
        [TestCase("uncertain")]
        [TestCase("incomplete")]
        [TestCase("model-change")]
        [TestCase("namespace-change")]
        [TestCase("reviewed-namespace-change")]
        public async Task ChangedMappingCannotReachTheTargetWriterAsync(string change)
        {
            var test = new MappingContext();
            using PubSubSchemaMapping mapping = test.Create();
            await mapping.ValidateAsync(CancellationToken.None).ConfigureAwait(false);
            test.Change = change;
            if (change == "model-change")
            {
                test.Session.Raise(session => session.ModelChanged += null, EventArgs.Empty);
            }
            if (change == "namespace-change")
            {
                test.RemoteContext.NamespaceUris.GetIndexOrAppend("urn:changed");
            }
            if (change == "reviewed-namespace-change")
            {
                test.Values.MessageContext.NamespaceUris.GetIndexOrAppend("urn:changed");
            }
            var target = new Mock<ISubscribedDataSetSink>(MockBehavior.Strict);
            var store = new PubSubObservationStore(2, messageContext: test.Values.MessageContext);
            var sink = new PubSubControlledWriteBack(target.Object, store, test.Configuration,
                new PubSubTestClock(), test.Values.MessageContext, mapping);
            await using (sink.ConfigureAwait(false))
            {
                NodeId type = new(5001u, 1);
                DataTypeDefinition definition = await test.Values.ResolveAsync(type).ConfigureAwait(false) ??
                    throw new AssertionException("No reviewed type.");
                StructuredValueDraft draft = await test.Values.OpenAsync(type, definition, Variant.Null)
                    .ConfigureAwait(false);
                Assert.That(draft.TryCommit([new("Source", Variant.From(new NodeId(99u, 1)))],
                    out Variant payload, out string? error), Is.True, error);
                await Assert.ThatAsync(() => sink.WriteAsync(
                    [new DataSetField { Name = "Reading", Value = payload }]).AsTask(),
                    Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
                target.VerifyNoOtherCalls();
                Assert.That(store.Snapshot().AcceptedDataSets, Is.Zero);
            }
        }

        [Test]
        public async Task UnreviewedNamespacesInsideAStructureAreRejectedBeforeRemappingAsync()
        {
            var test = new MappingContext();
            using PubSubSchemaMapping mapping = test.Create();
            var type = new NodeId(5001u, 1);
            DataTypeDefinition definition = await test.Values.ResolveAsync(type).ConfigureAwait(false) ??
                throw new AssertionException("Missing reviewed structure.");
            StructuredValueDraft draft = await test.Values.OpenAsync(type, definition, Variant.Null)
                .ConfigureAwait(false);
            Assert.That(draft.TryCommit([new("Source", Variant.From(new NodeId(99u, 9)))],
                out Variant payload, out string? error), Is.True, error);
            Assert.That(() => mapping.ToRemote(payload),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        private static PubSubConfiguration BuiltInConfiguration(BuiltInType type, int rank)
        {
            return PubSubTestRuntime.Configuration with
            {
                Namespaces = [Namespaces.OpcUa, Model],
                Fields = [new()
                {
                    Name = "Reading", Type = type, ValueRank = rank,
                    ArrayDimensions = rank == ValueRanks.Scalar ? [] : rank == 1 ? [1u] : [1u, 1u]
                }]
            };
        }

        private static Variant NamespaceValue(
            BuiltInType type, int rank, ushort namespaceIndex, IServiceMessageContext context)
        {
            Variant scalar = type switch
            {
                BuiltInType.NodeId => Variant.From(new NodeId(99u, namespaceIndex)),
                BuiltInType.ExpandedNodeId => Variant.From(new ExpandedNodeId(new NodeId(99u, namespaceIndex))),
                BuiltInType.QualifiedName => Variant.From(new QualifiedName("Source", namespaceIndex)),
                _ => throw new AssertionException("Unexpected namespace-bearing type.")
            };
            return rank == ValueRanks.Scalar ? scalar :
                new StructuredArrayValue(type, [scalar], rank == 2 ? [1, 1] : []).ToVariant(context);
        }

        private sealed class MappingContext
        {
            public MappingContext(PubSubConfiguration? configuration = null)
            {
                Configuration = configuration ?? Configuration;
                ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
                RemoteContext = ServiceMessageContext.Create(telemetry);
                RemoteContext.NamespaceUris.GetIndexOrAppend("urn:namespace-padding");
                RemoteContext.NamespaceUris.GetIndexOrAppend(Model);
                Values = new PubSubConfiguredValueService(Configuration, telemetry);
                Session.SetupGet(session => session.IsConnected).Returns(true);
                Session.Setup(session => session.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                    .Returns((ArrayOf<ReadValueId> reads, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (Change == "incomplete")
                        {
                            return new ValueTask<ArrayOf<DataValue>>([]);
                        }
                        var values = new List<DataValue>();
                        foreach (ReadValueId read in reads)
                        {
                            Variant value = read.AttributeId switch
                            {
                                Attributes.Value when read.NodeId == VariableIds.Server_NamespaceArray =>
                                    Variant.From(Change == "server-namespaces"
                                        ? (ArrayOf<string>)[Namespaces.OpcUa, "urn:changed", Model]
                                        : RemoteContext.NamespaceUris.ToArray().ToArrayOf()),
                                Attributes.DataType => Variant.From(Change == "type" ? DataTypeIds.String :
                                    PubSubStructureSchema.FieldType(
                                        Configuration.Fields[0], RemoteContext.NamespaceUris)),
                                Attributes.ValueRank =>
                                    Variant.From(Change == "rank" ? 1 : Configuration.Fields[0].ValueRank),
                                Attributes.ArrayDimensions => Variant.From(Change == "dimensions"
                                    ? (ArrayOf<uint>)[1u] : Configuration.Fields[0].ArrayDimensions),
                                Attributes.UserAccessLevel => Variant.From(AccessLevels.CurrentReadOrWrite),
                                Attributes.DataTypeDefinition => Definition(),
                                _ => throw new AssertionException("Unexpected read")
                            };
                            values.Add(new DataValue(value, Change == "denied" ? StatusCodes.BadUserAccessDenied :
                                Change == "uncertain" ? StatusCodes.Uncertain : StatusCodes.Good));
                        }
                        return new ValueTask<ArrayOf<DataValue>>([.. values]);
                    });
            }

            public PubSubConfiguration Configuration { get; } = PubSubTestRuntime.Configuration with
            {
                Namespaces = [Namespaces.OpcUa, Model],
                Fields = [new() { Name = "Reading", Type = BuiltInType.ExtensionObject,
                    DataTypeId = "nsu=" + Model + ";i=5001" }],
                Structures = [new()
                {
                    Name = "ReadingType", DataTypeId = "nsu=" + Model + ";i=5001",
                    BinaryEncodingId = "nsu=" + Model + ";i=5002",
                    Fields = [new() { Name = "Source", DataTypeId = "i=17" }]
                }]
            };

            public PubSubConfiguredValueService Values { get; }
            public ServiceMessageContext RemoteContext { get; }
            public Mock<IServerSession> Session { get; } = new(MockBehavior.Strict);
            public string Change { get; set; } = string.Empty;

            public PubSubSchemaMapping Create()
            {
                return new(Session.Object, Configuration, [new NodeId(100u, 2)],
                    Values.MessageContext, RemoteContext, write: true);
            }

            private Variant Definition()
            {
                StructureDefinition definition = PubSubStructureSchema.Definition(Configuration.Structures[0],
                    RemoteContext.NamespaceUris);
                if (Change == "definition")
                {
                    definition.Fields[0].Name = "Changed";
                }
                return Variant.FromStructure(definition);
            }
        }

        private const string Model = "urn:ualens:test:mapped-model";
    }
}
