/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.IA;
using Opc.Ua.IA.Client;
using Opc.Ua.IA.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Roots IA model loading, encoding and public DI registrations for native execution.
    /// </summary>
    public sealed class IndustrialAutomationAotTests
    {
        /// <summary>
        /// Loads all IA concepts and resolves client registration without opening a connection.
        /// </summary>
        [Test]
        public async Task IndustrialAutomationModelEncodingAndHostingAreReachableAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            ServiceMessageContext messages = ServiceMessageContext.Create(telemetry);
            messages.NamespaceUris.GetIndexOrAppend(DiNodeManager.DiNamespaceUri);
            messages.NamespaceUris.GetIndexOrAppend(IA.Namespaces.IA);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = messages.NamespaceUris,
                EncodeableFactory = messages.Factory
            };
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            var model = new IaModelProvider();
            model.AddPredefinedNodes(nodes, context);
            int count = nodes.Count;
            model.AddPredefinedNodes(nodes, context);

            var color = new RGBWDataType
            {
                Red = 200,
                Green = 10,
                Blue = 20,
                White = 30,
                EncodingMask = (uint)RGBWDataTypeFields.White
            };
            byte[] encoded = BinaryEncoder.EncodeMessage(color, messages);
            RGBWDataType decoded = BinaryDecoder.DecodeMessage<RGBWDataType>(encoded, messages);

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "IndustrialAutomationAotServer")
                .AddIndustrialAutomation();
            services.AddOpcUa().AddClient(options =>
            {
                options.Configuration = new ApplicationConfiguration(telemetry)
                {
                    ApplicationName = "IndustrialAutomationAotClient",
                    ApplicationType = ApplicationType.Client,
                    ClientConfiguration = new ClientConfiguration(),
                    SecurityConfiguration = new SecurityConfiguration(),
                    TransportQuotas = new TransportQuotas()
                };
            }).AddIndustrialAutomationClient();
            using ServiceProvider provider = services.BuildServiceProvider();
            IndustrialAutomationClientFactory factory =
                provider.GetRequiredService<IndustrialAutomationClientFactory>();
            int registrations = provider.GetServices<IDiModelProvider>().OfType<IaModelProvider>().Count();

            await Assert.That(count > 0).IsTrue();
            await Assert.That(nodes.Count).IsEqualTo(count);
            await Assert.That(registrations).IsEqualTo(1);
            await Assert.That(factory).IsNotNull();
            await Assert.That(decoded.IsEqual(color)).IsTrue();
            await Assert.That(decoded.Red).IsEqualTo((byte)200);
            await Assert.That(decoded.Green).IsEqualTo((byte)10);
            await Assert.That(decoded.Blue).IsEqualTo((byte)20);
            await Assert.That(decoded.White).IsEqualTo((byte)30);
            await Assert.That(decoded.EncodingMask).IsEqualTo((uint)RGBWDataTypeFields.White);
            await Assert.That(nodes.Any(node => node.NodeId == ExpandedNodeId.ToNodeId(
                IA.ObjectTypeIds.BasicStacklightType, messages.NamespaceUris))).IsTrue();
            await Assert.That(nodes.Any(node => node.NodeId == ExpandedNodeId.ToNodeId(
                IA.ObjectTypeIds.IStatisticsType, messages.NamespaceUris))).IsTrue();
            await Assert.That(nodes.Any(node => node.NodeId == ExpandedNodeId.ToNodeId(
                IA.ObjectTypeIds.CalibrationTargetType, messages.NamespaceUris))).IsTrue();
        }

        /// <summary>
        /// Publishes decoded structures through the public statistics binding and verifies that
        /// the published copies stay detached after the provider changes its source buffers.
        /// </summary>
        [Test]
        public async Task StatisticsPublishDetachedDecodedStructuresAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            ServiceMessageContext messages = ServiceMessageContext.Create(telemetry);
            var namespaces = new NamespaceTable();
            var ids = new InstanceIds(namespaces.GetIndexOrAppend("urn:tests:ia:aot:instances"));
            namespaces.GetIndexOrAppend(DiNodeManager.DiNamespaceUri);
            namespaces.GetIndexOrAppend(IA.Namespaces.IA);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = namespaces,
                NodeIdFactory = ids,
                TypeTable = new TypeTable(namespaces),
                EncodeableFactory = messages.Factory
            };
            context.EncodeableFactory.Builder.AddOpcUaIA().Commit();

            var owner = new BaseObjectState(null);
            owner.Create(context, NodeId.Null, new QualifiedName("Statistics", ids.Namespace),
                new LocalizedText("Statistics"), true);
            BaseDataVariableState registered = Quantity(context, owner, ids, "Registered");
            BaseDataVariableState custom = Quantity(context, owner, ids, "Custom");

            uint[] dimensions = [2, 3];
            double[] values = [1, 2];
            byte[] payload = [3, 4];
            uint[] nested = [5, 6];
            var argument = new Argument
            {
                Name = "Input",
                ArrayDimensions = new ArrayOf<uint>(dimensions.AsMemory())
            };
            var structure = new MutableStructure
            {
                Values = new ArrayOf<double>(values.AsMemory()),
                Payload = new ByteString(payload.AsMemory()),
                Nested = Variant.From(new ArrayOf<uint>(nested.AsMemory()))
            };
            DateTimeUtc timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var provider = new SnapshotProvider(new IaStatisticsSnapshot
            {
                StartTime = timestamp,
                Values =
                [
                    new DataValue(new Variant(new ExtensionObject(argument)), StatusCodes.Good, timestamp),
                    new DataValue(new Variant(new ExtensionObject(structure)), StatusCodes.Good, timestamp)
                ]
            });
            using (var binding = new IaStatistics(context, owner, [registered, custom], provider))
            {
                await binding.RefreshAsync().ConfigureAwait(false);
            }
            dimensions[0] = 99;
            values[0] = 99;
            payload[0] = 99;
            nested[0] = 99;

            await Assert.That(registered.WrappedValue.TryGetValue(out ExtensionObject first)).IsTrue();
            await Assert.That(first.TryGetValue(out Argument? argumentCopy)).IsTrue();
            await Assert.That(ReferenceEquals(argumentCopy, argument)).IsFalse();
            await Assert.That(argumentCopy!.Name).IsEqualTo("Input");
            await Assert.That(argumentCopy.ArrayDimensions.Span.SequenceEqual(s_dimensions)).IsTrue();

            await Assert.That(custom.WrappedValue.TryGetValue(out ExtensionObject second)).IsTrue();
            await Assert.That(second.TryGetValue(out MutableStructure? structureCopy)).IsTrue();
            await Assert.That(ReferenceEquals(structureCopy, structure)).IsFalse();
            await Assert.That(structureCopy!.Values.Span.SequenceEqual(s_values)).IsTrue();
            await Assert.That(structureCopy.Payload.Span.SequenceEqual(s_payload)).IsTrue();
            await Assert.That(structureCopy.Nested.TryGetValue(out ArrayOf<uint> nestedCopy)).IsTrue();
            await Assert.That(nestedCopy.Span.SequenceEqual(s_nested)).IsTrue();
        }

        /// <summary>
        /// Creates an application-defined statistic that accepts any structure.
        /// </summary>
        private static BaseDataVariableState Quantity(
            SystemContext context, BaseObjectState owner, InstanceIds ids, string name)
        {
            var value = new BaseDataVariableState(owner);
            value.Create(context, NodeId.Null, new QualifiedName(name, ids.Namespace), new LocalizedText(name), true);
            value.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            value.ValueRank = ValueRanks.Scalar;
            owner.AddChild(value);
            return value;
        }

        /// <summary>The published argument dimensions.</summary>
        private static readonly uint[] s_dimensions = [2, 3];

        /// <summary>The published primitive-array field.</summary>
        private static readonly double[] s_values = [1, 2];

        /// <summary>The published byte string field.</summary>
        private static readonly byte[] s_payload = [3, 4];

        /// <summary>The published nested variant elements.</summary>
        private static readonly uint[] s_nested = [5, 6];

        /// <summary>
        /// Allocates unique instance identities in a test-local namespace.
        /// </summary>
        private sealed class InstanceIds : INodeIdFactory
        {
            /// <summary>
            /// Creates the factory for one instance namespace.
            /// </summary>
            public InstanceIds(ushort namespaceIndex)
            {
                Namespace = namespaceIndex;
            }

            /// <summary>
            /// Gets the instance namespace index.
            /// </summary>
            public ushort Namespace { get; }

            /// <inheritdoc/>
            public NodeId New(ISystemContext context, NodeState node)
            {
                return node.NodeId.IsNull
                    ? new NodeId((uint)Interlocked.Increment(ref m_nextId), Namespace)
                    : node.NodeId;
            }

            /// <summary>The next instance identity.</summary>
            private int m_nextId;
        }

        /// <summary>
        /// Returns one fixed snapshot.
        /// </summary>
        private sealed class SnapshotProvider : IIaStatisticsProvider
        {
            /// <summary>
            /// Creates the provider for a snapshot.
            /// </summary>
            public SnapshotProvider(IaStatisticsSnapshot snapshot)
            {
                m_snapshot = snapshot;
            }

            /// <inheritdoc/>
            public ValueTask<IaStatisticsSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<IaStatisticsSnapshot>(m_snapshot);
            }

            /// <inheritdoc/>
            public ValueTask<ServiceResult> AuthorizeResetAsync(
                ISystemContext requestContext,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied));
            }

            /// <inheritdoc/>
            public ValueTask<IaStatisticsSnapshot> ResetAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<IaStatisticsSnapshot>(m_snapshot);
            }

            /// <summary>The fixed snapshot.</summary>
            private readonly IaStatisticsSnapshot m_snapshot;
        }

        /// <summary>
        /// A structure unknown to the encodeable factory whose member-wise clone shares a
        /// primitive array, a byte string and a nested variant with the provider.
        /// </summary>
        private sealed class MutableStructure : IEncodeable
        {
            /// <summary>
            /// The primitive-array field.
            /// </summary>
            public ArrayOf<double> Values { get; set; }

            /// <summary>
            /// The byte string field.
            /// </summary>
            public ByteString Payload { get; set; }

            /// <summary>
            /// The nested variant field.
            /// </summary>
            public Variant Nested { get; set; }

            /// <inheritdoc/>
            public ExpandedNodeId TypeId => new(9101u, 1);

            /// <inheritdoc/>
            public ExpandedNodeId BinaryEncodingId => new(9102u, 1);

            /// <inheritdoc/>
            public ExpandedNodeId XmlEncodingId => new(9103u, 1);

            /// <inheritdoc/>
            public void Encode(IEncoder encoder)
            {
                encoder.WriteDoubleArray(nameof(Values), Values);
                encoder.WriteByteString(nameof(Payload), Payload);
                encoder.WriteVariant(nameof(Nested), Nested);
            }

            /// <inheritdoc/>
            public void Decode(IDecoder decoder)
            {
                Values = decoder.ReadDoubleArray(nameof(Values));
                Payload = decoder.ReadByteString(nameof(Payload));
                Nested = decoder.ReadVariant(nameof(Nested));
            }

            /// <inheritdoc/>
            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is MutableStructure other &&
                    Values.Span.SequenceEqual(other.Values.Span) &&
                    Payload.Span.SequenceEqual(other.Payload.Span) &&
                    Nested.Equals(other.Nested);
            }

            /// <inheritdoc/>
            public object Clone()
            {
                return MemberwiseClone();
            }
        }
    }
}
