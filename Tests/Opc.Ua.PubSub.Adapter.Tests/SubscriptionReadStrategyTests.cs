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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Publisher;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SubscriptionReadStrategy"/>: the latest-value
    /// cache, attribute normalization, subscription-driven updates and lifetime.
    /// </summary>
    [TestFixture]
    public sealed class SubscriptionReadStrategyTests
    {
        [Test]
        public void ConstructorNullTelemetryThrows()
        {
            Assert.That(
                () => new SubscriptionReadStrategy(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public async Task ReadUnprimedKeyReturnsUncertainInitialValue()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = new NodeId(1u), AttributeId = Attributes.Value }
            ];

            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(values.Count, Is.EqualTo(1));
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
        }

        [Test]
        public async Task SeedThenReadReturnsCachedValue()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            var nodeId = new NodeId(5u);
            strategy.Seed(nodeId, Attributes.Value, new DataValue(new Variant(42)));

            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(42)));
        }

        [Test]
        public async Task ReadNormalizesZeroAttributeToValue()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            var nodeId = new NodeId(7u);
            strategy.Seed(nodeId, Attributes.Value, new DataValue(new Variant(11)));

            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = 0 }
            ];
            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(11)));
        }

        [Test]
        public async Task CacheKeyDistinguishesAttributes()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            var nodeId = new NodeId(9u);
            strategy.Seed(nodeId, Attributes.Value, new DataValue(new Variant(1)));

            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Description }
            ];
            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(
                values[0].StatusCode,
                Is.EqualTo(StatusCodes.UncertainInitialValue));
        }

        [Test]
        public async Task RegisterMonitoredItemSeedsUncertainPlaceholder()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            var nodeId = new NodeId(13u);
            strategy.RegisterMonitoredItem(100, nodeId, Attributes.Value);

            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(
                values[0].StatusCode,
                Is.EqualTo(StatusCodes.UncertainInitialValue));
        }

        [Test]
        public async Task DataChangedUpdatesCacheByClientHandle()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            var subscription = new FakeDataChangeSubscription();
            strategy.Attach(subscription);

            var nodeId = new NodeId(21u);
            strategy.RegisterMonitoredItem(55, nodeId, Attributes.Value);
            subscription.Raise(55, nodeId, new DataValue(new Variant(99)));

            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(99)));
        }

        [Test]
        public async Task DataChangedFallsBackToNodeIdWhenHandleUnknown()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            var subscription = new FakeDataChangeSubscription();
            strategy.Attach(subscription);

            var nodeId = new NodeId(23u);
            subscription.Raise(999, nodeId, new DataValue(new Variant(7)));

            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(7)));
        }

        [Test]
        public void ReadAfterDisposeThrows()
        {
            var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            strategy.Dispose();

            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = new NodeId(1u), AttributeId = Attributes.Value }
            ];

            Assert.That(
                async () => await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void ReadCanceledThrows()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = new NodeId(1u), AttributeId = Attributes.Value }
            ];

            Assert.That(
                async () => await strategy.ReadAsync(reads.ToArrayOf(), cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task ReadEmptyInputReturnsEmpty()
        {
            using var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());

            ArrayOf<DataValue> values = await strategy.ReadAsync([]).ConfigureAwait(false);

            Assert.That(values.Count, Is.Zero);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var strategy = new SubscriptionReadStrategy(AdapterTestHelpers.Telemetry());
            strategy.Dispose();

            Assert.That(strategy.Dispose, Throws.Nothing);
        }
    }
}
