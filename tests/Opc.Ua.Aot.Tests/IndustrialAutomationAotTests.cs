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
        /// Copies supported decoded values and deferred text, and rejects registered custom bodies before publication.
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
            BaseDataVariableState localized = Quantity(context, owner, ids, "Localized");
            object[] formatting = ["Original"];
            var localizedText = new LocalizedText("key", "en-US", "Value: {0}", formatting);

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
            context.EncodeableFactory.Builder.AddEncodeableType(new MutableStructureActivator()).Commit();
            DateTimeUtc timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            DataValue[] readings =
            [
                new DataValue(new Variant(new ExtensionObject(argument)), StatusCodes.Good, timestamp),
                new DataValue(Variant.From(12u), StatusCodes.Good, timestamp),
                new DataValue(new Variant(localizedText), StatusCodes.Good, timestamp)
            ];
            var provider = new SnapshotProvider(new IaStatisticsSnapshot
            {
                StartTime = timestamp,
                Values = new ArrayOf<DataValue>(readings.AsMemory())
            });
            using var binding = new IaStatistics(context, owner, [registered, custom, localized], provider);
            await binding.RefreshAsync().ConfigureAwait(false);
            dimensions[0] = 99;
            values[0] = 99;
            payload[0] = 99;
            nested[0] = 99;
            formatting[0] = "Changed";
            await Assert.That(localized.WrappedValue.TryGetValue(out LocalizedText localizedCopy)).IsTrue();
            await Assert.That(localizedCopy.TranslationInfo.Text).IsEqualTo("Value: {0}");
            await Assert.That(localizedCopy.FilterByPreferredLocales(["en-US"]).Text).IsEqualTo("Value: Original");
            await Assert.That(localizedCopy.TranslationInfo.Key).IsEqualTo("key");

            await Assert.That(registered.WrappedValue.TryGetValue(out ExtensionObject first)).IsTrue();
            await Assert.That(first.TryGetValue(out Argument? argumentCopy)).IsTrue();
            await Assert.That(ReferenceEquals(argumentCopy, argument)).IsFalse();
            await Assert.That(argumentCopy!.Name).IsEqualTo("Input");
            await Assert.That(argumentCopy.ArrayDimensions.Span.SequenceEqual(s_dimensions)).IsTrue();

            readings[1] = new DataValue(new Variant(new ExtensionObject(structure)), StatusCodes.Good, timestamp);
            ServiceResultException error = await Assert.That(
                async () => await binding.RefreshAsync().ConfigureAwait(false)).Throws<ServiceResultException>() ??
                throw new InvalidOperationException("The exception assertion returned no exception.");
            await Assert.That(error.StatusCode).IsEqualTo((StatusCode)StatusCodes.BadNotSupported);
            await Assert.That(custom.WrappedValue.TryGetValue(out uint retained)).IsTrue();
            await Assert.That(retained).IsEqualTo(12u);
            await Assert.That(registered.WrappedValue.TryGetValue(out ExtensionObject retainedArgument)).IsTrue();
            await Assert.That(retainedArgument.TryGetValue(out Argument? argumentAfterRejection)).IsTrue();
            await Assert.That(ReferenceEquals(argumentAfterRejection, argumentCopy)).IsTrue();
            await Assert.That(argumentAfterRejection!.ArrayDimensions.Span.SequenceEqual(s_dimensions)).IsTrue();
            await Assert.That(localized.WrappedValue.TryGetValue(out LocalizedText textAfterRejection)).IsTrue();
            await Assert.That(textAfterRejection.FilterByPreferredLocales(["en-US"]).Text).IsEqualTo("Value: Original");
            await Assert.That(binding.StartTime.WrappedValue.TryGetValue(out DateTimeUtc retainedStart)).IsTrue();
            await Assert.That(retainedStart).IsEqualTo(timestamp);
        }

        /// <summary>
        /// Roots both first-party decoded ECM structures and their generated publication copies in NativeAOT.
        /// </summary>
        [Test]
        public async Task StatisticsCombinedEcmRetainsDecodedValuesAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            var namespaces = new NamespaceTable();
            var ids = new InstanceIds(namespaces.GetIndexOrAppend("urn:tests:ia:aot:ecm"));
            namespaces.GetIndexOrAppend(DiNodeManager.DiNamespaceUri);
            namespaces.GetIndexOrAppend(IA.Namespaces.IA);
            namespaces.GetIndexOrAppend(ECM.Namespaces.ECM);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = namespaces,
                NodeIdFactory = ids,
                TypeTable = new TypeTable(namespaces)
            };
            var owner = new BaseObjectState(null);
            owner.Create(context, NodeId.Null, new QualifiedName("Statistics", ids.Namespace),
                new LocalizedText("Statistics"), true);
            BaseDataVariableState statistic = Quantity(context, owner, ids, "Statistic");
            BaseDataVariableState companion = Quantity(context, owner, ids, "Instantaneous");
            var acPe = new ECM.AcPeDataType { L1 = 1, L2 = 2, L3 = 3 };
            var acPp = new ECM.AcPpDataType { L1L2 = 4, L2L3 = 5, L3L1 = 6 };
            DateTimeUtc timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var provider = new SnapshotProvider(new IaStatisticsSnapshot
            {
                StartTime = timestamp,
                Values = [new DataValue(Variant.FromStructure(acPe), StatusCodes.Good, timestamp)],
                CompanionValues = [new DataValue(Variant.FromStructure(acPp),
                    StatusCodes.UncertainLastUsableValue, timestamp)]
            });
            using var binding = new IaStatisticsBuilder().AddComponent(statistic)
                .WithCombinedPublication([companion]).Build(context, owner, provider);
            await binding.RefreshAsync().ConfigureAwait(false);
            acPe.L1 = 99;
            acPe.L2 = 99;
            acPe.L3 = 99;
            acPp.L1L2 = 99;
            acPp.L2L3 = 99;
            acPp.L3L1 = 99;
            await Assert.That(statistic.WrappedValue.TryGetStructure<ECM.AcPeDataType>(out ECM.AcPeDataType? copiedPe)).IsTrue();
            await Assert.That(companion.WrappedValue.TryGetStructure<ECM.AcPpDataType>(out ECM.AcPpDataType? copiedPp)).IsTrue();
            await Assert.That(copiedPe!.GetType()).IsEqualTo(typeof(ECM.AcPeDataType));
            await Assert.That(copiedPp!.GetType()).IsEqualTo(typeof(ECM.AcPpDataType));
            await Assert.That(ReferenceEquals(copiedPe, acPe)).IsFalse();
            await Assert.That(ReferenceEquals(copiedPp, acPp)).IsFalse();
            await Assert.That(copiedPe.L1).IsEqualTo(1f);
            await Assert.That(copiedPe.L2).IsEqualTo(2f);
            await Assert.That(copiedPe.L3).IsEqualTo(3f);
            await Assert.That(copiedPp.L1L2).IsEqualTo(4f);
            await Assert.That(copiedPp.L2L3).IsEqualTo(5f);
            await Assert.That(copiedPp.L3L1).IsEqualTo(6f);
            await Assert.That(companion.StatusCode).IsEqualTo((StatusCode)StatusCodes.UncertainLastUsableValue);
            await Assert.That(companion.Timestamp).IsEqualTo(timestamp);
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

        /// <summary>
        /// Creates independent custom decoded receivers without reflection.
        /// </summary>
        private sealed class MutableStructureActivator : IEncodeableType
        {
            /// <inheritdoc/>
            public Type Type => typeof(MutableStructure);

            /// <inheritdoc/>
            public System.Xml.XmlQualifiedName XmlName => new("MutableStructure", "urn:tests:ia:aot");

            /// <inheritdoc/>
            public IEncodeable CreateInstance()
            {
                return new MutableStructure();
            }
        }
    }
}
