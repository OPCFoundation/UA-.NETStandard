/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Verifies data channel source lookup and server-offer redemption.
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class DataChannelSourceRegistryTests
    {
        [Test]
        public void RegisterAddsAndReplacesSourcesByNodeId()
        {
            var registry = new DataChannelSourceRegistry();
            var nodeId = new NodeId(42u);
            var first = new TestSource(nodeId, activeChannelCount: 1);
            var replacement = new TestSource(nodeId, activeChannelCount: 2);

            registry.Register(first);
            registry.Register(replacement);

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGet(nodeId, out IDataChannelSource? resolved), Is.True);
                Assert.That(resolved, Is.SameAs(replacement));
                Assert.That(registry.Sources, Has.Count.EqualTo(1));
                Assert.That(registry.Sources, Does.Contain(replacement));
                Assert.That(resolved!.ActiveChannelCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void RegisterRejectsNullSource()
        {
            var registry = new DataChannelSourceRegistry();

            Assert.That(
                () => registry.Register(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("source"));
        }

        [Test]
        public void UnregisterRemovesOnlyTheNamedSource()
        {
            var registry = new DataChannelSourceRegistry();
            var kept = new TestSource(new NodeId(100u));
            var removed = new TestSource(new NodeId(101u));
            registry.Register(kept);
            registry.Register(removed);

            bool firstRemove = registry.Unregister(removed.NodeId);
            bool secondRemove = registry.Unregister(removed.NodeId);

            Assert.Multiple(() =>
            {
                Assert.That(firstRemove, Is.True);
                Assert.That(secondRemove, Is.False);
                Assert.That(registry.TryGet(removed.NodeId, out _), Is.False);
                Assert.That(registry.TryGet(kept.NodeId, out IDataChannelSource? resolved), Is.True);
                Assert.That(resolved, Is.SameAs(kept));
                Assert.That(registry.Sources, Is.EquivalentTo(new[] { kept }));
            });
        }

        [Test]
        public void TryGetReturnsFalseForUnknownSource()
        {
            var registry = new DataChannelSourceRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGet(new NodeId(999u), out IDataChannelSource? source), Is.False);
                Assert.That(source, Is.Null);
                Assert.That(registry.Sources, Is.Empty);
            });
        }

        [Test]
        public void CreateReturnsRedeemableOfferWithExpirationAndParameters()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            var registry = new DataChannelOfferRegistry(clock);
            var source = new NodeId(700u);
            var parameters = new DataChannelParametersDataType
            {
                Direction = DataChannelDirection.SinkToSource,
                DeliveryMode = DataChannelDeliveryMode.ReliableUnordered,
                ContentType = "application/cbor",
                MaxFrameSize = 2048,
                InitialCredit = 4096,
                Priority = 6
            };

            DataChannelOfferDataType created = registry.Create(
                source,
                parameters,
                TimeSpan.FromSeconds(30));

            bool redeemed = registry.TryRedeem(created.OfferId, source, out DataChannelOfferDataType? offer);

            Assert.Multiple(() =>
            {
                Assert.That(created.OfferId, Is.EqualTo(1u));
                Assert.That(created.SourceNodeId, Is.EqualTo(source));
                Assert.That(created.Parameters, Is.SameAs(parameters));
                Assert.That(created.ExpirationTime, Is.EqualTo(new DateTime(2026, 1, 1, 12, 0, 30, DateTimeKind.Utc)));
                Assert.That(redeemed, Is.True);
                Assert.That(offer, Is.SameAs(created));
                Assert.That(offer!.Parameters.Direction, Is.EqualTo(DataChannelDirection.SinkToSource));
                Assert.That(offer.Parameters.DeliveryMode, Is.EqualTo(DataChannelDeliveryMode.ReliableUnordered));
                Assert.That(offer.Parameters.ContentType, Is.EqualTo("application/cbor"));
                Assert.That(offer.Parameters.MaxFrameSize, Is.EqualTo(2048u));
                Assert.That(offer.Parameters.InitialCredit, Is.EqualTo(4096u));
                Assert.That(offer.Parameters.Priority, Is.EqualTo((byte)6));
                Assert.That(registry.Count, Is.Zero);
            });
        }

        [Test]
        public void OfferIdsAreSingleUse()
        {
            var registry = new DataChannelOfferRegistry();
            var source = new NodeId(701u);
            DataChannelOfferDataType offer = registry.Create(
                source,
                new DataChannelParametersDataType(),
                TimeSpan.FromMinutes(1));

            bool firstRedeem = registry.TryRedeem(offer.OfferId, source, out DataChannelOfferDataType? redeemed);
            bool secondRedeem = registry.TryRedeem(offer.OfferId, source, out DataChannelOfferDataType? second);

            Assert.Multiple(() =>
            {
                Assert.That(firstRedeem, Is.True);
                Assert.That(redeemed, Is.SameAs(offer));
                Assert.That(secondRedeem, Is.False);
                Assert.That(second, Is.Null);
                Assert.That(registry.Count, Is.Zero);
            });
        }

        [Test]
        public void ExpiredOfferCannotBeRedeemed()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var registry = new DataChannelOfferRegistry(clock);
            var source = new NodeId(702u);
            DataChannelOfferDataType offer = registry.Create(
                source,
                new DataChannelParametersDataType(),
                TimeSpan.FromSeconds(10));

            clock.Advance(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryRedeem(offer.OfferId, source, out DataChannelOfferDataType? redeemed), Is.False);
                Assert.That(redeemed, Is.Null);
                Assert.That(registry.Count, Is.Zero);
            });
        }

        [Test]
        public void OfferCannotBeRedeemedFromDifferentSecureChannelRegistry()
        {
            var source = new NodeId(703u);
            var issuingRegistry = new DataChannelOfferRegistry();
            var differentSessionRegistry = new DataChannelOfferRegistry();

            DataChannelOfferDataType offer = issuingRegistry.Create(
                source,
                new DataChannelParametersDataType(),
                TimeSpan.FromMinutes(1));

            bool redeemed = differentSessionRegistry.TryRedeem(
                offer.OfferId,
                source,
                out DataChannelOfferDataType? rejected);

            Assert.Multiple(() =>
            {
                Assert.That(redeemed, Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(differentSessionRegistry.Count, Is.Zero);
                Assert.That(issuingRegistry.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void OfferWithWrongSourceIsRejectedAndConsumed()
        {
            var registry = new DataChannelOfferRegistry();
            DataChannelOfferDataType offer = registry.Create(
                new NodeId(704u),
                new DataChannelParametersDataType(),
                TimeSpan.FromMinutes(1));

            bool rejected = registry.TryRedeem(
                offer.OfferId,
                new NodeId(705u),
                out DataChannelOfferDataType? wrongSource);
            bool retry = registry.TryRedeem(
                offer.OfferId,
                new NodeId(704u),
                out DataChannelOfferDataType? retryOffer);

            Assert.Multiple(() =>
            {
                Assert.That(rejected, Is.False);
                Assert.That(wrongSource, Is.Null);
                Assert.That(retry, Is.False);
                Assert.That(retryOffer, Is.Null);
                Assert.That(registry.Count, Is.Zero);
            });
        }

        [Test]
        public void TamperedUnknownOfferIdIsRejectedWithoutConsumingKnownOffer()
        {
            var registry = new DataChannelOfferRegistry();
            var source = new NodeId(706u);
            DataChannelOfferDataType offer = registry.Create(
                source,
                new DataChannelParametersDataType(),
                TimeSpan.FromMinutes(1));

            bool tampered = registry.TryRedeem(
                offer.OfferId + 1,
                source,
                out DataChannelOfferDataType? tamperedOffer);
            bool redeemed = registry.TryRedeem(
                offer.OfferId,
                source,
                out DataChannelOfferDataType? realOffer);

            Assert.Multiple(() =>
            {
                Assert.That(tampered, Is.False);
                Assert.That(tamperedOffer, Is.Null);
                Assert.That(redeemed, Is.True);
                Assert.That(realOffer, Is.SameAs(offer));
                Assert.That(registry.Count, Is.Zero);
            });
        }

        [Test]
        public void PurgeExpiredRemovesOnlyExpiredOffers()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var registry = new DataChannelOfferRegistry(clock);
            registry.Create(new NodeId(800u), new DataChannelParametersDataType(), TimeSpan.FromSeconds(5));
            DataChannelOfferDataType live = registry.Create(
                new NodeId(801u),
                new DataChannelParametersDataType(),
                TimeSpan.FromSeconds(20));

            clock.Advance(TimeSpan.FromSeconds(6));

            Assert.Multiple(() =>
            {
                Assert.That(registry.PurgeExpired(), Is.EqualTo(1));
                Assert.That(registry.Count, Is.EqualTo(1));
                Assert.That(registry.TryRedeem(live.OfferId, live.SourceNodeId, out DataChannelOfferDataType? redeemed), Is.True);
                Assert.That(redeemed, Is.SameAs(live));
            });
        }

        private sealed class TestSource : IDataChannelSource
        {
            public TestSource(NodeId nodeId, int activeChannelCount = 0)
            {
                NodeId = nodeId;
                ActiveChannelCount = activeChannelCount;
                Capabilities = new DataChannelSourceCapabilities
                {
                    Direction = DataChannelDirection.Bidirectional,
                    ContentType = "application/octet-stream",
                    MaxFrameSize = 4096
                };
            }

            public NodeId NodeId { get; }

            public DataChannelSourceCapabilities Capabilities { get; }

            public int ActiveChannelCount { get; }

            public void OnChannelOpened(DataChannel channel)
            {
            }

            public void OnChannelClosed(DataChannel channel, StatusCode reason)
            {
            }
        }
    }
}
