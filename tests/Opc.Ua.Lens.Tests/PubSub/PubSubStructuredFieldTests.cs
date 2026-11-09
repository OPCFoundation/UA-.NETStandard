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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using UaLens.Plugins.PubSub;
using UaLens.StructuredValues;
using JsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;

namespace UaLens.Tests.PubSub
{
    [TestFixture]
    public sealed class PubSubStructuredFieldTests
    {
        [Test]
        public void TypedNullCollectionIsNormalizedToTheReviewedMatrixType()
        {
            var context = PubSubFieldSchema.CreateContext(DefaultTelemetry.Create(static _ => { }));
            var field = new PubSubFieldConfiguration
            {
                Name = "Matrix", Type = BuiltInType.Int32, ValueRank = 2, ArrayDimensions = [2u, 3u]
            };
            Variant captured = PubSubFieldSchema.ValidateAndSnapshot(field, Variant.From(MatrixOf<int>.Null), context);
            Assert.That(captured.TryGetValue(out MatrixOf<int> matrix), Is.True);
            Assert.That(matrix.IsNull, Is.True);
            Assert.That(captured.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(captured.IsNull, Is.False);
            Assert.That(field.ValueRank, Is.EqualTo(2));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void TheEncodedFieldBudgetIsExactRatherThanAnElementCountApproximation(int excess)
        {
            var context = PubSubFieldSchema.CreateContext(DefaultTelemetry.Create(static _ => { }));
            var field = new PubSubFieldConfiguration
            {
                Name = "Text", Type = BuiltInType.String, ValueRank = 1, ArrayDimensions = [32u]
            };
            string[] elements = [.. Enumerable.Repeat(new string('x', 508), 32)];
            elements[^1] = new string('x', 503 + excess);
            Variant payload = Variant.From(elements.ToArrayOf());
            if (excess == 0)
            {
                Variant captured = PubSubFieldSchema.ValidateAndSnapshot(field, payload, context);
                Assert.That(PubSubFieldSchema.Encode(captured, context).Length,
                    Is.EqualTo(PubSubFieldSchema.MaximumValueBytes));
                Assert.That(captured, Is.EqualTo(payload));
            }
            else
            {
                Assert.That(() => PubSubFieldSchema.ValidateAndSnapshot(field, payload, context),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NestedStructuresAndStructureArraysPreserveTheirExactValuesAsync(bool json)
        {
            PubSubConfiguration configuration = StructureConfiguration();
            PubSubStructureConfiguration child = configuration.Structures[0] with
            {
                Name = "Child", DataTypeId = "nsu=urn:ualens:test:pubsub-model;i=6001",
                BinaryEncodingId = "nsu=urn:ualens:test:pubsub-model;i=6002"
            };
            configuration = configuration with
            {
                Structures =
                [
                    configuration.Structures[0] with
                    {
                        Fields =
                        [
                            new() { Name = "Child", DataTypeId = child.DataTypeId },
                            new() { Name = "Children", DataTypeId = child.DataTypeId,
                                ValueRank = 1, ArrayDimensions = [2u] }
                        ]
                    },
                    child
                ]
            };
            var values = new PubSubConfiguredValueService(configuration, DefaultTelemetry.Create(static _ => { }));
            var childType = new NodeId(6001u, 1);
            DataTypeDefinition childDefinition = await values.ResolveAsync(childType).ConfigureAwait(false) ??
                throw new AssertionException("Missing child definition.");
            StructuredValueDraft childDraft = await values.OpenAsync(childType, childDefinition, Variant.Null)
                .ConfigureAwait(false);
            Assert.That(childDraft.TryCommit([new("Temperature", Variant.From(10.5))],
                out Variant first, out string? error), Is.True, error);
            Assert.That(childDraft.TryCommit([new("Temperature", Variant.From(21.25))],
                out Variant second, out error), Is.True, error);
            Variant array = new StructuredArrayValue(BuiltInType.ExtensionObject, [first, second])
                .ToVariant(values.MessageContext);
            var rootType = new NodeId(5001u, 1);
            DataTypeDefinition rootDefinition = await values.ResolveAsync(rootType).ConfigureAwait(false) ??
                throw new AssertionException("Missing root definition.");
            StructuredValueDraft rootDraft = await values.OpenAsync(rootType, rootDefinition, Variant.Null)
                .ConfigureAwait(false);
            Assert.That(rootDraft.TryCommit([new("Child", first), new("Children", array)],
                out Variant payload, out error), Is.True, error);
            Variant received = await RoundTripFieldAsync(configuration, values, payload, json).ConfigureAwait(false);
            Assert.That(received.TryGetValue<IEncodeable>(out IEncodeable? body, values.MessageContext), Is.True);
            var root = (IStructure)body!;
            Assert.That(root["Child"].TryGetValue<IEncodeable>(
                out IEncodeable? nested, values.MessageContext), Is.True);
            Assert.That(((IStructure)nested!)["Temperature"], Is.EqualTo(Variant.From(10.5)));
            var children = StructuredArrayValue.Read(root["Children"], values.MessageContext);
            Assert.That(children.Elements.Count, Is.EqualTo(2));
            Assert.That(children.Elements[1].TryGetValue<IEncodeable>(
                out IEncodeable? last, values.MessageContext), Is.True);
            Assert.That(((IStructure)last!)["Temperature"], Is.EqualTo(Variant.From(21.25)));
            Assert.That(rootDraft.TryCommit([new("Child", Variant.From(10)), new("Children", array)],
                out Variant rejected, out error), Is.False);
            Assert.That(rejected.IsNull, Is.True);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task OptionalStructurePresenceAndTypedNullSurviveBothWireCodecsAsync(bool json, bool include)
        {
            PubSubConfiguration configuration = StructureConfiguration();
            configuration = configuration with
            {
                Structures = [configuration.Structures[0] with
                {
                    StructureType = StructureType.StructureWithOptionalFields,
                    Fields = [new() { Name = "Temperature", DataTypeId = "i=11" },
                        new() { Name = "Bytes", DataTypeId = "i=15", IsOptional = true }]
                }]
            };
            var values = new PubSubConfiguredValueService(configuration, DefaultTelemetry.Create(static _ => { }));
            var type = new NodeId(5001u, 1);
            DataTypeDefinition definition = await values.ResolveAsync(type).ConfigureAwait(false) ??
                throw new AssertionException("Missing optional structure.");
            StructuredValueDraft draft = await values.OpenAsync(type, definition, Variant.Null).ConfigureAwait(false);
            Assert.That(draft.TryCommit([new("Temperature", Variant.From(12.5)),
                new("Bytes", Variant.From(default(ByteString)), IsIncluded: include)],
                out Variant payload, out string? error), Is.True, error);
            Variant received = await RoundTripFieldAsync(configuration, values, payload, json).ConfigureAwait(false);
            Assert.That(received.TryGetValue<Opc.Ua.Encoders.StructureWithOptionalFields>(
                out Opc.Ua.Encoders.StructureWithOptionalFields? body, values.MessageContext), Is.True);
            Assert.That(body!.EncodingMask, Is.EqualTo(include ? 1u : 0u));
            Assert.That(body["Temperature"], Is.EqualTo(Variant.From(12.5)));
            if (include)
            {
                Assert.That(body["Bytes"].TryGetValue(out ByteString bytes), Is.True);
                Assert.That(bytes.IsNull, Is.True);
                Assert.That(body["Bytes"].IsNull, Is.False);
            }
            else
            {
                Assert.That(body["Bytes"].IsNull, Is.True);
            }
        }

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        public async Task UnionSelectionAndEmptyUnionSurviveBothWireCodecsAsync(bool json, int selected)
        {
            PubSubConfiguration configuration = StructureConfiguration();
            configuration = configuration with
            {
                Structures = [configuration.Structures[0] with
                {
                    StructureType = StructureType.Union,
                    BaseDataTypeId = "i=12756",
                    Fields = [new() { Name = "Number", DataTypeId = "i=6" },
                        new() { Name = "Text", DataTypeId = "i=12" }]
                }]
            };
            var values = new PubSubConfiguredValueService(configuration, DefaultTelemetry.Create(static _ => { }));
            var type = new NodeId(5001u, 1);
            DataTypeDefinition definition = await values.ResolveAsync(type).ConfigureAwait(false) ??
                throw new AssertionException("Missing union.");
            StructuredValueDraft draft = await values.OpenAsync(type, definition, Variant.Null).ConfigureAwait(false);
            Assert.That(draft.TryCommit([new("Number", Variant.From(31), IsIncluded: selected == 1),
                new("Text", Variant.From("selected"), IsIncluded: selected == 2)],
                out Variant payload, out string? error), Is.True, error);
            Variant received = await RoundTripFieldAsync(configuration, values, payload, json).ConfigureAwait(false);
            Assert.That(received.TryGetValue<Opc.Ua.Encoders.Union>(
                out Opc.Ua.Encoders.Union? body, values.MessageContext), Is.True);
            Assert.That(body!.SwitchField, Is.EqualTo((uint)selected));
            Assert.That(body["Number"], Is.EqualTo(selected == 1 ? Variant.From(31) : Variant.Null));
            Assert.That(body["Text"], Is.EqualTo(selected == 2 ? Variant.From("selected") : Variant.Null));
            Assert.That(draft.TryCommit([new("Number", Variant.From(1)), new("Text", Variant.From("ambiguous"))],
                out Variant rejected, out error), Is.False);
            Assert.That(rejected.IsNull, Is.True);
            Assert.That(body.SwitchField, Is.EqualTo((uint)selected));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void NullCollectionsStayNullAcrossLocalSamplePersistence(int rank)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
            {
                Fields = [new() { Name = "Samples", Type = BuiltInType.Int32, ValueRank = rank,
                    ArrayDimensions = rank == 1 ? [3u] : [2u, 3u] }]
            };
            var values = new PubSubConfiguredValueService(configuration, telemetry);
            Variant original = rank == 1 ? Variant.From(ArrayOf<int>.Null) : Variant.From(MatrixOf<int>.Null);
            configuration = configuration with { Fields = [values.CaptureSample(configuration.Fields[0], original)] };
            PubSubConfiguration restored = PubSubStateCodec.Restore(PubSubStateCodec.Capture(configuration));
            var current = new PubSubConfiguredValueService(restored, telemetry);
            Variant sample = current.ReadSample(restored.Fields[0]);
            Assert.That(restored.Fields[0].ValueRank, Is.EqualTo(rank));
            Assert.That(restored.Fields[0].ArrayDimensions, Is.EqualTo(configuration.Fields[0].ArrayDimensions));
            Assert.That(sample.IsNull, Is.False);
            if (rank == 1)
            {
                Assert.That(sample.TryGetValue(out ArrayOf<int> array), Is.True);
                Assert.That(array.IsNull, Is.True);
                Assert.That(array, Is.Not.EqualTo(ArrayOf<int>.Empty));
            }
            else
            {
                Assert.That(sample.TryGetValue(out MatrixOf<int> matrix), Is.True);
                Assert.That(matrix.IsNull, Is.True);
                Assert.That(matrix, Is.Not.EqualTo(MatrixOf<int>.Empty));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [NonParallelizable]
        public async Task OwnedUdpRuntimePublishesAndReceivesTheExactReviewedStructuredPayloadAsync(bool custom)
        {
            int port;
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                port = ((IPEndPoint)socket.LocalEndPoint!).Port;
            }
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            PubSubConfiguration configuration =
                (custom ? StructureConfiguration() : PubSubTestRuntime.Configuration) with
            {
                Endpoint = $"opc.udp://127.0.0.1:{port}",
                NetworkInterface = "127.0.0.1",
                LocalPublisherId = 2,
                PublisherFilter = 2,
                DurationSeconds = 5,
                PublishingIntervalMs = 150,
                MaxPublishedMessages = 4,
                FieldContentMask = custom ? DataSetFieldContentMask.None : DataSetFieldContentMask.RawData
            };
            if (custom)
            {
                var editor = new PubSubConfiguredValueService(configuration, telemetry);
                NodeId type = new(5001u, 1);
                DataTypeDefinition definition = await editor.ResolveAsync(type).ConfigureAwait(false) ??
                    throw new AssertionException("Missing schema.");
                StructuredValueDraft draft = await editor.OpenAsync(type, definition, Variant.Null)
                    .ConfigureAwait(false);
                Assert.That(draft.TryCommit([new("Temperature", Variant.From(23.5))],
                    out Variant value, out string? error), Is.True, error);
                configuration = configuration with { Fields = [editor.CaptureSample(configuration.Fields[0], value)] };
            }
            else
            {
                configuration = configuration with
                {
                    Fields = [new() { Name = "Samples", Type = BuiltInType.Int32,
                        ValueRank = 2, ArrayDimensions = [2u, 3u] }]
                };
            }
            configuration = configuration with { Publication = PubSubPublication.Synthetic };
            var workspace = new PubSubWorkspace(new PubSubRuntimeFactory(telemetry), telemetry);
            await using (workspace.ConfigureAwait(false))
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await workspace.ConfigureAsync(configuration, deadline.Token).ConfigureAwait(false);
                await workspace.StartAsync(
                    new PubSubStartAuthorization(AllowUnsecured: true, AllowPublication: true), deadline.Token)
                    .ConfigureAwait(false);
                PubSubWorkspaceSnapshot snapshot;
                do
                {
                    await Task.Delay(25, deadline.Token).ConfigureAwait(false);
                    snapshot = workspace.Snapshot();
                }
                while (snapshot.Phase is PubSubDocumentPhase.Running or PubSubDocumentPhase.Starting or
                    PubSubDocumentPhase.Stopping);
                await workspace.StopAsync().ConfigureAwait(false);
                snapshot = workspace.Snapshot();
                Assert.That(snapshot.Phase, Is.EqualTo(PubSubDocumentPhase.Offline), snapshot.Status);
                Assert.That(snapshot.Observations.AcceptedDataSets, Is.EqualTo(4), snapshot.Status);
                Assert.That(snapshot.SourceSamples, Is.EqualTo(4));
                Assert.That(snapshot.Observations.TruncatedValues, Is.Zero);
                Variant last = snapshot.Observations.Values[0].Value;
                if (custom)
                {
                    var values = new PubSubConfiguredValueService(configuration, telemetry);
                    Assert.That(last.TryGetValue<IEncodeable>(out IEncodeable? body, values.MessageContext), Is.True);
                    Assert.That(((IStructure)body!)["Temperature"], Is.EqualTo(Variant.From(23.5)));
                }
                else
                {
                    Assert.That(last.TryGetValue(out MatrixOf<int> matrix), Is.True);
                    Assert.That(matrix.Dimensions, Is.EqualTo([2, 3]));
                    Assert.That(matrix.Span.ToArray(), Is.All.EqualTo(4));
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(1024)]
        public void ArrayValuesWithinTheElementLimitKeepTheirExactCount(int count)
        {
            ServiceMessageContext context = PubSubFieldSchema.CreateContext(DefaultTelemetry.Create(static _ => { }));
            int[] values = [.. Enumerable.Range(0, count)];
            var field = new PubSubFieldConfiguration { Name = "Values", Type = BuiltInType.Int32, ValueRank = 1 };
            Variant captured = PubSubFieldSchema.ValidateAndSnapshot(field, Variant.From(values.ToArrayOf()), context);
            Assert.That(captured.TryGetValue(out ArrayOf<int> array), Is.True);
            Assert.That(array.Count, Is.EqualTo(count));
            Assert.That(array.ToList(), Is.EqualTo(values));
        }

        [TestCase("count")]
        [TestCase("dimension")]
        [TestCase("rank")]
        [TestCase("type")]
        [TestCase("nan")]
        [TestCase("infinity")]
        public void InvalidArrayValuesCannotPassTheReviewedSchema(string fault)
        {
            ServiceMessageContext context = PubSubFieldSchema.CreateContext(DefaultTelemetry.Create(static _ => { }));
            double[] values = fault == "count" ? new double[1025] : [1, 2, 3];
            if (fault is "nan" or "infinity")
            {
                values[1] = fault == "nan" ? double.NaN : double.PositiveInfinity;
            }
            var field = new PubSubFieldConfiguration
            {
                Name = "Values",
                Type = fault == "type" ? BuiltInType.Int32 : BuiltInType.Double,
                ValueRank = fault == "rank" ? 2 : 1,
                ArrayDimensions = fault == "dimension" ? [2u] : []
            };
            Assert.That(() => PubSubFieldSchema.ValidateAndSnapshot(field, Variant.From(values.ToArrayOf()), context),
                Throws.TypeOf<ServiceResultException>());
        }

        [TestCase("cycle")]
        [TestCase("missing-type")]
        [TestCase("missing-namespace")]
        [TestCase("duplicate")]
        [TestCase("encoding-collision")]
        [TestCase("field-limit")]
        public void InvalidStructureGraphsCannotCreateNativeAdapters(string fault)
        {
            PubSubConfiguration configuration = StructureConfiguration();
            PubSubStructureConfiguration type = configuration.Structures[0];
            type = fault switch
            {
                "cycle" => type with { Fields = [new() { Name = "Recursive", DataTypeId = type.DataTypeId }] },
                "missing-type" => type with
                {
                    Fields = [new() { Name = "Missing", DataTypeId = "nsu=urn:missing;i=1" }]
                },
                "encoding-collision" => type with { BinaryEncodingId = type.DataTypeId },
                "field-limit" => type with
                {
                    Fields = [.. Enumerable.Range(0, 65)
                    .Select(index => new PubSubStructureFieldConfiguration { Name = "F" + index, DataTypeId = "i=6" })]
                },
                _ => type
            };
            configuration = configuration with { Structures = fault == "duplicate" ? [type, type] : [type] };
            if (fault == "missing-namespace")
            {
                configuration = configuration with { Namespaces = [Namespaces.OpcUa] };
            }
            Assert.That(() => new PubSubConfiguredValueService(configuration, DefaultTelemetry.Create(static _ => { })),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task LocalStructuredSampleSurvivesPersistenceButRejectsAChangedSchemaAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            PubSubConfiguration configuration = StructureConfiguration();
            var values = new PubSubConfiguredValueService(configuration, telemetry);
            NodeId type = PubSubStructureSchema.FieldType(configuration.Fields[0], values.MessageContext.NamespaceUris);
            DataTypeDefinition definition = await values.ResolveAsync(type).ConfigureAwait(false) ??
                throw new AssertionException("Missing reviewed structure.");
            StructuredValueDraft draft = await values.OpenAsync(type, definition, Variant.Null).ConfigureAwait(false);
            Assert.That(draft.TryCommit([new("Temperature", Variant.From(17.25))],
                out Variant payload, out string? error), Is.True, error);
            configuration = configuration with
            {
                Fields = [values.CaptureSample(configuration.Fields[0], payload)],
                Publication = PubSubPublication.Synthetic
            };
            PubSubConfiguration restored = PubSubStateCodec.Restore(PubSubStateCodec.Capture(configuration));
            var current = new PubSubConfiguredValueService(restored, telemetry);
            var source = new PubSubSyntheticSource(restored, new PubSubTestClock(), current);
            PublishedDataSetSnapshot sample = await source.SampleAsync(source.BuildMetaData()).ConfigureAwait(false);
            Assert.That(sample.Fields[0].Value.TryGetValue<IEncodeable>(
                out IEncodeable? body, current.MessageContext), Is.True);
            Assert.That(((IStructure)body!)["Temperature"], Is.EqualTo(Variant.From(17.25)));
            var changed = new PubSubConfiguredValueService(
                restored with { MetadataMajorVersion = restored.MetadataMajorVersion + 1 }, telemetry);
            Assert.That(() => changed.ReadSample(restored.Fields[0]),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState));
        }

        [TestCase("dimensions")]
        [TestCase("missing-version")]
        [TestCase("wrong-version")]
        public async Task ChangedOrUnversionedReceivedSchemaCannotReachTheConfiguredDataSetSinkAsync(string change)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
            {
                Fields = [new() { Name = "Samples", Type = BuiltInType.Int32, ValueRank = 1, ArrayDimensions = [4u] }]
            };
            DataSetMetaDataType metadata = PubSubStackConfiguration.CreateMetadata(configuration);
            var registry = new DataSetMetaDataRegistry();
            var publisher = PublisherId.FromUInt16(1);
            var key = new DataSetMetaDataKey(publisher, 100, 1, Uuid.Empty, 1);
            registry.Register(key, metadata);
            var context = new PubSubNetworkMessageContext(PubSubFieldSchema.CreateContext(telemetry),
                registry, new PubSubDiagnostics(PubSubDiagnosticsLevel.High), TimeProvider.System);
            var message = new UadpNetworkMessage
            {
                PublisherId = publisher,
                WriterGroupId = 100,
                ContentMask = PubSubContentMasks.DefaultUadpNetwork,
                DataSetMessages = [new UadpDataSetMessage
                {
                    DataSetWriterId = 1, MetaDataVersion = metadata.ConfigurationVersion,
                    ContentMask = PubSubContentMasks.DefaultUadpDataSet,
                    Fields = [new DataSetField { Name = "Samples", Value = Variant.From([1, 2]) }]
                }]
            };
            ReadOnlyMemory<byte> encoded = await new UadpEncoder().EncodeAsync(message, context).ConfigureAwait(false);
            var store = new PubSubObservationStore(2);
            var observed = new PubSubObservedDecoder(new UadpDecoder(), store, 1500,
                context.MessageContext, configuration);
            Assert.That(await observed.TryDecodeAsync(encoded, context).ConfigureAwait(false), Is.Not.Null);
            if (change == "dimensions")
            {
                metadata.Fields[0].ArrayDimensions = [16u];
                registry.Register(key, metadata);
            }
            else
            {
                var dataSet = (UadpDataSetMessage)message.DataSetMessages[0];
                dataSet = change == "missing-version" ? dataSet with
                {
                    ContentMask = dataSet.ContentMask &
                        ~(UadpDataSetMessageContentMask.MajorVersion |
                            UadpDataSetMessageContentMask.MinorVersion)
                } : dataSet with
                {
                    MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 99 }
                };
                message = message with { DataSetMessages = [dataSet] };
                encoded = await new UadpEncoder().EncodeAsync(message, context).ConfigureAwait(false);
            }
            Assert.That(await observed.TryDecodeAsync(encoded, context).ConfigureAwait(false), Is.Null);
            Assert.That(store.Snapshot().Evidence.ToList().Any(evidence =>
                evidence.Area == "Schema" && evidence.Status == StatusCodes.BadInvalidState), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReviewedCustomStructureUsesNativeAdaptersAndExistingWireCodecsAsync(bool json)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            PubSubConfiguration configuration = StructureConfiguration();
            var values = new PubSubConfiguredValueService(configuration, telemetry);
            NodeId type = PubSubStructureSchema.FieldType(configuration.Fields[0], values.MessageContext.NamespaceUris);
            DataTypeDefinition definition = await values.ResolveAsync(type).ConfigureAwait(false) ??
                throw new AssertionException("The configured structure definition is missing.");
            StructuredValueDraft draft = await values.OpenAsync(type, definition, Variant.Null).ConfigureAwait(false);
            Assert.That(draft.TryCommit([new("Temperature", Variant.From(42.5))],
                out Variant payload, out string? error), Is.True, error);
            Variant result = await RoundTripFieldAsync(configuration, values, payload, json).ConfigureAwait(false);
            Assert.That(result.TryGetValue<IEncodeable>(out IEncodeable? body, values.MessageContext), Is.True);
            Assert.That(body, Is.InstanceOf<IStructure>());
            Assert.That(((IStructure)body!)["Temperature"], Is.EqualTo(Variant.From(42.5)));
            values.Refresh();
            Assert.That(draft.TryCommit([new("Temperature", Variant.From(1.0))], out _, out error), Is.False);
            Assert.That(error, Does.Contain("schema changed"));
        }

        [TestCase(false, 1, PubSubFieldEncoding.Variant)]
        [TestCase(false, 2, PubSubFieldEncoding.Variant)]
        [TestCase(false, 1, PubSubFieldEncoding.RawData)]
        [TestCase(false, 2, PubSubFieldEncoding.RawData)]
        [TestCase(false, 2, PubSubFieldEncoding.DataValue)]
        [TestCase(true, 1, PubSubFieldEncoding.Variant)]
        [TestCase(true, 2, PubSubFieldEncoding.Variant)]
        public async Task ExistingWireCodecsRetainReviewedArrayDimensionsAsync(
            bool json, int rank, PubSubFieldEncoding encoding)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            IServiceMessageContext values = PubSubFieldSchema.CreateContext(telemetry);
            PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
            {
                Fields = [new() { Name = "Samples", Type = BuiltInType.Int32, ValueRank = rank,
                    ArrayDimensions = rank == 1 ? [6u] : [2u, 3u] }]
            };
            DataSetMetaDataType metadata = PubSubStackConfiguration.CreateMetadata(configuration);
            var publisher = PublisherId.FromByte(1);
            var registry = new DataSetMetaDataRegistry();
            registry.Register(new DataSetMetaDataKey(publisher, json ? (ushort)0 : (ushort)100, 1, Uuid.Empty, 1),
                metadata);
            var context = new PubSubNetworkMessageContext(values, registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High), TimeProvider.System);
            int[] numbers = [1, 2, 3, 4, 5, 6];
            Variant value = rank == 1 ? Variant.From(numbers.ToArrayOf()) : Variant.From(numbers.ToMatrixOf([2, 3]));
            var field = new DataSetField { Name = "Samples", Value = value, Encoding = encoding };
            PubSubNetworkMessage message = json
                ? new JsonNetworkMessage
                {
                    PublisherId = publisher,
                    WriterGroupId = 100,
                    DataSetMessages = [new JsonDataSetMessage
                    {
                        DataSetWriterId = 1, MetaDataVersion = metadata.ConfigurationVersion, Fields = [field]
                    }]
                }
                : new UadpNetworkMessage
                {
                    PublisherId = publisher,
                    WriterGroupId = 100,
                    ContentMask = PubSubContentMasks.DefaultUadpNetwork,
                    DataSetMessages = [new UadpDataSetMessage
                    {
                        DataSetWriterId = 1, MetaDataVersion = metadata.ConfigurationVersion,
                        ContentMask = PubSubContentMasks.DefaultUadpDataSet, FieldEncoding = encoding, Fields = [field]
                    }]
                };
            INetworkMessageEncoder encoder = json
                ? new Opc.Ua.PubSub.Encoding.Json.JsonEncoder() : new UadpEncoder();
            INetworkMessageDecoder decoder = json
                ? new Opc.Ua.PubSub.Encoding.Json.JsonDecoder() : new UadpDecoder();
            ReadOnlyMemory<byte> encoded = await encoder.EncodeAsync(message, context).ConfigureAwait(false);
            var store = new PubSubObservationStore(2, messageContext: values);
            PubSubNetworkMessage? decoded = await new PubSubObservedDecoder(decoder, store, 1500)
                .TryDecodeAsync(encoded, context).ConfigureAwait(false);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.DataSetMessages, Has.Count.EqualTo(1));
            Assert.That(decoded.DataSetMessages[0].Fields[0].Value, Is.EqualTo(value));
            await store.WriteAsync(decoded.DataSetMessages[0].Fields.ToList()).ConfigureAwait(false);
            Assert.That(store.Snapshot().Values[0].Value, Is.EqualTo(value));
            Assert.That(store.Snapshot().Values[0].Truncated, Is.False);
        }

        [TestCase(1)]
        [TestCase(2)]
        public async Task SyntheticArraysRetainTheirReviewedShapeAndSampleSequenceAsync(int rank)
        {
            PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
            {
                Publication = PubSubPublication.Synthetic,
                Fields =
                [
                    new()
                    {
                        Name = "Samples", Type = BuiltInType.Int32, ValueRank = rank,
                        ArrayDimensions = rank == 1 ? [6u] : [2u, 3u]
                    }
                ]
            };
            var source = new PubSubSyntheticSource(configuration, new PubSubTestClock());
            for (int sequence = 1; sequence <= 2; sequence++)
            {
                PublishedDataSetSnapshot sample = await source.SampleAsync(source.BuildMetaData())
                    .ConfigureAwait(false);
                Variant value = sample.Fields[0].Value;
                Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(rank));
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
                var array = StructuredArrayValue.Read(
                    value, ServiceMessageContext.Create(DefaultTelemetry.Create(static _ => { })));
                Assert.That(array.Elements, Has.Count.EqualTo(6));
                Assert.That(array.Elements.ToList(), Is.All.EqualTo(Variant.From(sequence)));
                if (rank == 2)
                {
                    Assert.That(array.Dimensions, Is.EqualTo((ArrayOf<int>)[2, 3]));
                }
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public async Task CompleteArrayWriteBackPreservesItsShapeAndDoesNotRetainCallerStorageAsync(int rank)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
            {
                Fields = [new() { Name = "Samples", Type = BuiltInType.Int32,
                        ValueRank = rank, ArrayDimensions = rank == 1 ? [6u] : [2u, 3u] }]
            };
            int[] elements = [1, 2, 3, 4, 5, 6];
            Variant value = rank == 1 ? Variant.From(elements.ToArrayOf())
                : Variant.From(elements.ToMatrixOf([2, 3]));
            var target = new Mock<ISubscribedDataSetSink>(MockBehavior.Strict);
            target.Setup(sink => sink.WriteAsync(
                It.IsAny<IReadOnlyList<DataSetField>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            var store = new PubSubObservationStore(2, messageContext: context);
            var sink = new PubSubControlledWriteBack(
                target.Object, store, configuration, new PubSubTestClock(), context);
            await using (sink.ConfigureAwait(false))
            {
                await sink.WriteAsync([new DataSetField { Name = "Samples", Value = value }]).ConfigureAwait(false);
                elements[0] = 99;
                PubSubFieldValue observed = store.Snapshot().Values[0];
                Assert.That(observed.Truncated, Is.False);
                var retained = StructuredArrayValue.Read(observed.Value, context);
                Assert.That(retained.Elements[0], Is.EqualTo(Variant.From(1)));
                Assert.That(observed.Value.TypeInfo.ValueRank, Is.EqualTo(rank));
                target.Verify(writer => writer.WriteAsync(
                    It.Is<IReadOnlyList<DataSetField>>(fields => fields[0].Value.TypeInfo.ValueRank == rank),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        private static async Task<Variant> RoundTripFieldAsync(
            PubSubConfiguration configuration, PubSubConfiguredValueService values, Variant payload, bool json)
        {
            var registry = new DataSetMetaDataRegistry();
            DataSetMetaDataType metadata = PubSubStackConfiguration.CreateMetadata(configuration);
            var publisher = PublisherId.FromByte(1);
            registry.Register(new DataSetMetaDataKey(publisher, json ? (ushort)0 : (ushort)100, 1, Uuid.Empty, 1),
                metadata);
            var context = new PubSubNetworkMessageContext(ServiceMessageContext.Create(values.MessageContext.Telemetry),
                registry, new PubSubDiagnostics(PubSubDiagnosticsLevel.High), TimeProvider.System);
            var field = new DataSetField { Name = "Reading", Value = payload };
            PubSubNetworkMessage source = json
                ? new JsonNetworkMessage
                {
                    PublisherId = publisher,
                    WriterGroupId = 100,
                    DataSetMessages = [new JsonDataSetMessage
                    {
                        DataSetWriterId = 1, MetaDataVersion = metadata.ConfigurationVersion, Fields = [field]
                    }]
                }
                : new UadpNetworkMessage
                {
                    PublisherId = publisher,
                    WriterGroupId = 100,
                    ContentMask = PubSubContentMasks.DefaultUadpNetwork,
                    DataSetMessages = [new UadpDataSetMessage
                    {
                        DataSetWriterId = 1, MetaDataVersion = metadata.ConfigurationVersion,
                        ContentMask = PubSubContentMasks.DefaultUadpDataSet, Fields = [field]
                    }]
                };
            INetworkMessageEncoder innerEncoder = json
                ? new Opc.Ua.PubSub.Encoding.Json.JsonEncoder() : new UadpEncoder();
            INetworkMessageDecoder innerDecoder = json
                ? new Opc.Ua.PubSub.Encoding.Json.JsonDecoder() : new UadpDecoder();
            ReadOnlyMemory<byte> encoded = await new PubSubConfiguredEncoder(innerEncoder, values.MessageContext)
                .EncodeAsync(source, context).ConfigureAwait(false);
            var store = new PubSubObservationStore(2, messageContext: values.MessageContext);
            PubSubConfiguration receiver = configuration with
            {
                PublisherFilter = 1,
                PublisherFilterType = PublisherIdType.Byte,
                Profile = json ? PubSubProfile.MqttJson : PubSubProfile.UdpUadp
            };
            PubSubNetworkMessage? decoded = await new PubSubObservedDecoder(
                innerDecoder, store, 1500, values.MessageContext, receiver).TryDecodeAsync(encoded, context)
                .ConfigureAwait(false);
            Assert.That(decoded, Is.Not.Null);
            Variant result = decoded!.DataSetMessages[0].Fields[0].Value;
            Variant captured = PubSubFieldSchema.ValidateAndSnapshot(
                configuration.Fields[0], result, values.MessageContext);
            await store.WriteAsync(decoded.DataSetMessages[0].Fields.ToList()).ConfigureAwait(false);
            Assert.That(store.Snapshot().Values[0].Truncated, Is.False);
            return captured;
        }

        private static PubSubConfiguration StructureConfiguration()
        {
            const string model = "urn:ualens:test:pubsub-model";
            return PubSubTestRuntime.Configuration with
            {
                Namespaces = [Namespaces.OpcUa, model],
                Fields = [new() { Name = "Reading", Type = BuiltInType.ExtensionObject,
                    DataTypeId = "nsu=" + model + ";i=5001" }],
                Structures = [new()
                {
                    Name = "ReadingType", DataTypeId = "nsu=" + model + ";i=5001",
                    BinaryEncodingId = "nsu=" + model + ";i=5002",
                    Fields = [new() { Name = "Temperature", DataTypeId = "i=11" }]
                }]
            };
        }
    }
}
