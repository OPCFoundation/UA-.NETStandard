/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable]
    public sealed class HistorianContinuationPointCodecTests
    {
        [Test]
        public async Task RawContinuationRoundTripsAsync()
        {
            NodeId nodeId = new("Historized", 1);
            PortableProvider provider = new("shared-historian");
            (HistorianContinuationPointCodec codec, HistorianProviderRegistry registry) =
                CreateCodec();
            registry.RegisterForNode(nodeId, provider);
            HistorianContinuationState original = CreateRawState(nodeId, provider);

            HistoryContinuationPointEnvelope? envelope = await codec.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                original,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(envelope, Is.Not.Null);

            IHistoryContinuationPoint? decoded = await codec.DecodeAsync(
                envelope!,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(decoded, Is.TypeOf<HistorianContinuationState>());
            var state = (HistorianContinuationState)decoded!;
            Assert.That(state.Id, Is.EqualTo(original.Id));
            Assert.That(state.NodeId, Is.EqualTo(nodeId));
            Assert.That(state.Provider, Is.SameAs(provider));
            Assert.That(state.Kind, Is.EqualTo(HistorianReadKind.Raw));
            Assert.That(state.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Both));
            Assert.That(state.ResumeToken.State.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(state.RawRequest, Is.Not.Null);
            Assert.That(state.RawRequest!.StartTime, Is.EqualTo(original.RawRequest!.StartTime));
            Assert.That(state.RawRequest.EndTime, Is.EqualTo(original.RawRequest.EndTime));
            Assert.That(state.RawRequest.MaxValues, Is.EqualTo(17));
            Assert.That(state.RawRequest.IsForward, Is.True);
            Assert.That(state.RawRequest.ReturnBounds, Is.True);
        }

        [Test]
        public async Task NonPortableContinuationIsNotEncodedAsync()
        {
            NodeId nodeId = new("Historized", 1);
            var provider = new PortableProvider("local-historian", portable: false);
            (HistorianContinuationPointCodec codec, _) = CreateCodec();

            HistoryContinuationPointEnvelope? envelope = await codec.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                CreateRawState(nodeId, provider),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(envelope, Is.Null);
        }

        [Test]
        public async Task NonPortableInvalidStateFallsBackToLocalContinuationAsync()
        {
            NodeId nodeId = new("Historized", 1);
            var provider = new PortableProvider(string.Empty, portable: false);
            (HistorianContinuationPointCodec codec, _) = CreateCodec();
            HistorianContinuationState state = CreateRawState(nodeId, provider);
            state.ResumeToken = new HistorianResumeToken(
                ByteString.From(new byte[128 * 1024]));

            HistoryContinuationPointEnvelope? envelope = await codec.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                state,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(envelope, Is.Null);
        }

        [Test]
        public async Task MultidimensionalIndexRangeRoundTripsAsync()
        {
            NodeId nodeId = new("Historized", 1);
            PortableProvider provider = new("shared-historian");
            (HistorianContinuationPointCodec codec, HistorianProviderRegistry registry) =
                CreateCodec();
            registry.RegisterForNode(nodeId, provider);
            HistorianContinuationState original = CreateRawState(
                nodeId,
                provider,
                NumericRange.Parse("1:2,0:3"));

            HistoryContinuationPointEnvelope? envelope = await codec.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                original,
                CancellationToken.None).ConfigureAwait(false);
            var decoded = (HistorianContinuationState?)await codec.DecodeAsync(
                envelope!,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.IndexRange.Dimensions, Is.EqualTo(2));
            Assert.That(decoded.IndexRange.SubRanges, Has.Length.EqualTo(2));
            Assert.That(decoded.IndexRange.SubRanges![0].Begin, Is.EqualTo(1));
            Assert.That(decoded.IndexRange.SubRanges[0].End, Is.EqualTo(2));
            Assert.That(decoded.IndexRange.SubRanges[1].Begin, Is.Zero);
            Assert.That(decoded.IndexRange.SubRanges[1].End, Is.EqualTo(3));
        }

        [Test]
        public async Task NamespaceUrisRemapAcrossReplicaTablesAsync()
        {
            (HistorianContinuationPointCodec encoder, _) = CreateCodec(
                "urn:test:other",
                "urn:test:historian");
            NodeId sourceNodeId = new("Historized", 2);
            PortableProvider sourceProvider = new("shared-historian");
            HistorianContinuationState original = CreateRawState(
                sourceNodeId,
                sourceProvider,
                dataEncoding: new QualifiedName(
                    BrowseNames.DefaultBinary,
                    1));
            HistoryContinuationPointEnvelope? envelope = await encoder.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                original,
                CancellationToken.None).ConfigureAwait(false);

            (HistorianContinuationPointCodec decoder, HistorianProviderRegistry registry) =
                CreateCodec(
                    "urn:test:historian",
                    "urn:test:other");
            NodeId remappedNodeId = new("Historized", 1);
            PortableProvider targetProvider = new("shared-historian");
            registry.RegisterForNode(remappedNodeId, targetProvider);
            var decoded = (HistorianContinuationState?)await decoder.DecodeAsync(
                envelope!,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.NodeId, Is.EqualTo(remappedNodeId));
            Assert.That(decoded.Provider, Is.SameAs(targetProvider));
            Assert.That(decoded.DataEncoding.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public async Task ProviderIdentityMismatchIsRejectedAsync()
        {
            NodeId nodeId = new("Historized", 1);
            PortableProvider originalProvider = new("provider-a");
            (HistorianContinuationPointCodec codec, HistorianProviderRegistry registry) =
                CreateCodec();
            registry.RegisterForNode(nodeId, originalProvider);
            HistoryContinuationPointEnvelope? envelope = await codec.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                CreateRawState(nodeId, originalProvider),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(envelope, Is.Not.Null);

            registry.RegisterForNode(nodeId, new PortableProvider("provider-b"));
            IHistoryContinuationPoint? decoded = await codec.DecodeAsync(
                envelope!,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task MalformedPayloadIsRejectedAsync()
        {
            (HistorianContinuationPointCodec codec, _) = CreateCodec();
            var envelope = new HistoryContinuationPointEnvelope
            {
                Id = Guid.NewGuid(),
                OwnerSessionId = new NodeId(Guid.NewGuid()),
                CodecId = "opcua-historian",
                CodecVersion = 1,
                Payload = ByteString.From(new byte[] { 1, 2, 3 })
            };

            IHistoryContinuationPoint? decoded = await codec.DecodeAsync(
                envelope,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded, Is.Null);
        }

        [Test]
        public async Task BufferedProcessedContinuationIsNotEncodedAsync()
        {
            NodeId nodeId = new("Historized", 1);
            PortableProvider provider = new("shared-historian");
            (HistorianContinuationPointCodec codec, _) = CreateCodec();
            HistorianContinuationState state = CreateRawState(nodeId, provider);
            state.BufferedProcessedOutputs = [];

            HistoryContinuationPointEnvelope? envelope = await codec.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                state,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(envelope, Is.Null);
        }

        [Test]
        public async Task EventContinuationWithNeitherTimestampsRoundTripsAsync()
        {
            NodeId nodeId = new("HistorizedEvents", 1);
            PortableProvider provider = new("shared-historian");
            (HistorianContinuationPointCodec codec, HistorianProviderRegistry registry) =
                CreateCodec();
            registry.RegisterForNode(nodeId, provider);
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventId,
                Attributes.Value);
            DateTimeUtc startTime = DateTime.UtcNow.AddHours(-1);
            var original = new HistorianContinuationState
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                Kind = HistorianReadKind.Events,
                NodeId = nodeId,
                ResumeToken = new HistorianResumeToken(
                    ByteString.From([4, 5, 6])),
                TimestampsToReturn = TimestampsToReturn.Neither,
                EventRequest = new HistorianEventReadRequest
                {
                    NodeId = nodeId,
                    StartTime = startTime,
                    EndTime = startTime.ToDateTime().AddMinutes(30),
                    MaxValues = 11,
                    IsForward = true,
                    Filter = filter
                }
            };

            HistoryContinuationPointEnvelope? envelope = await codec.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                original,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(envelope, Is.Not.Null);

            IHistoryContinuationPoint? decoded = await codec.DecodeAsync(
                envelope!,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded, Is.TypeOf<HistorianContinuationState>());
            var state = (HistorianContinuationState)decoded!;
            Assert.That(state.Kind, Is.EqualTo(HistorianReadKind.Events));
            Assert.That(
                state.TimestampsToReturn,
                Is.EqualTo(TimestampsToReturn.Neither));
            Assert.That(state.EventRequest, Is.Not.Null);
            Assert.That(state.EventRequest!.Filter.SelectClauses, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task PayloadWithTrailingBytesIsRejectedAsync()
        {
            NodeId nodeId = new("Historized", 1);
            PortableProvider provider = new("shared-historian");
            (HistorianContinuationPointCodec codec, HistorianProviderRegistry registry) =
                CreateCodec();
            registry.RegisterForNode(nodeId, provider);
            HistoryContinuationPointEnvelope? envelope = await codec.EncodeAsync(
                new NodeId(Guid.NewGuid()),
                CreateRawState(nodeId, provider),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(envelope, Is.Not.Null);
            byte[] payload = envelope!.Payload.ToArray();
            Array.Resize(ref payload, payload.Length + 1);

            IHistoryContinuationPoint? decoded = await codec.DecodeAsync(
                envelope with
                {
                    Payload = ByteString.From(payload)
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(decoded, Is.Null);
        }

        private static HistorianContinuationState CreateRawState(
            NodeId nodeId,
            IHistorianProvider provider,
            NumericRange indexRange = default,
            QualifiedName dataEncoding = default)
        {
            DateTimeUtc startTime = DateTime.UtcNow.AddHours(-1);
            return new HistorianContinuationState
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                Kind = HistorianReadKind.Raw,
                NodeId = nodeId,
                ResumeToken = new HistorianResumeToken(
                    ByteString.From([1, 2, 3])),
                TimestampsToReturn = TimestampsToReturn.Both,
                IndexRange = indexRange.IsNull
                    ? NumericRange.Parse("1:2")
                    : indexRange,
                DataEncoding = dataEncoding.IsNull
                    ? new QualifiedName(BrowseNames.DefaultBinary)
                    : dataEncoding,
                RawRequest = new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = startTime,
                    EndTime = startTime.ToDateTime().AddMinutes(30),
                    MaxValues = 17,
                    IsForward = true,
                    ReturnBounds = true
                }
            };
        }

        private static (
            HistorianContinuationPointCodec Codec,
            HistorianProviderRegistry Registry) CreateCodec()
        {
            return CreateCodec("urn:test:historian");
        }

        private static (
            HistorianContinuationPointCodec Codec,
            HistorianProviderRegistry Registry) CreateCodec(
                params string[] customNamespaceUris)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            for (int i = 0; i < customNamespaceUris.Length; i++)
            {
                namespaceUris.Append(customNamespaceUris[i]);
            }
            var serverUris = new StringTable();
            var messageContext = new ServiceMessageContext(
                telemetry,
                EncodeableFactory.Create())
            {
                NamespaceUris = namespaceUris,
                ServerUris = serverUris
            };
            var registry = new HistorianProviderRegistry(namespaceUris);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.MessageContext).Returns(messageContext);
            server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(value => value.ServerUris).Returns(serverUris);
            server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry)
                .Returns(registry);
            return (new HistorianContinuationPointCodec(server.Object), registry);
        }

        private sealed class PortableProvider :
            IHistorianProvider,
            IHistorianProviderIdentity
        {
            public PortableProvider(string providerId, bool portable = true)
            {
                ProviderId = providerId;
                m_capabilities = new HistorianNodeCapabilities
                {
                    PortableResumeTokens = portable
                };
            }

            public string ProviderId { get; }

            public ValueTask<bool> IsHistorizingAsync(
                NodeId nodeId,
                CancellationToken ct)
            {
                return new ValueTask<bool>(!nodeId.IsNull);
            }

            public ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(
                NodeId nodeId,
                CancellationToken ct)
            {
                return new ValueTask<HistorianNodeCapabilities>(
                    m_capabilities);
            }

            private readonly HistorianNodeCapabilities m_capabilities;
        }
    }
}
