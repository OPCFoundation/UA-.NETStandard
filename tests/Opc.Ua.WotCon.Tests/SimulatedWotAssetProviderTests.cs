// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    public class SimulatedWotAssetProviderTests
    {
        private SimulatedWotAssetProvider m_provider = null!;
        private WotPropertyTag m_voltageTag = null!;

        [SetUp]
        public void SetUp()
        {
            var td = new ThingDescription
            {
                Name = "asset-001",
                Base = "sim://opcua.test/wot/asset-001",
                Properties = new Dictionary<string, WotProperty>
                {
                    ["Voltage"] = new WotProperty { Type = "number", Observable = true }
                }
            };
            m_provider = new SimulatedWotAssetProvider(td);
            m_voltageTag = new WotPropertyTag(
                "Voltage",
                new NodeId(1u, 2),
                Ua.DataTypeIds.Double,
                ValueRanks.Scalar,
                readOnly: false,
                observable: true,
                form: null);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_provider.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ReadReturnsSeededDefaultBeforeFirstWrite()
        {
            (ServiceResult status, Variant value) = await m_provider
                .ReadAsync(m_voltageTag, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(value.AsBoxedObject(), Is.Zero);
        }

        [Test]
        public async Task WriteUpdatesSubsequentRead()
        {
            await m_provider.WriteAsync(m_voltageTag, new Variant(24.5), CancellationToken.None).ConfigureAwait(false);

            (ServiceResult _, Variant value) = await m_provider
                .ReadAsync(m_voltageTag, CancellationToken.None).ConfigureAwait(false);

            Assert.That(value.AsBoxedObject(), Is.EqualTo(24.5));
        }

        [Test]
        public async Task SetValueNotifiesSubscriber()
        {
            int notifications = 0;
            Variant lastValue = Variant.Null;
            await m_provider.SubscribeAsync(
                m_voltageTag,
                subscriberId: 42,
                (tag, value, status, timestamp) =>
                {
                    Interlocked.Increment(ref notifications);
                    lastValue = value;
                },
                CancellationToken.None).ConfigureAwait(false);

            m_provider.SetValue("Voltage", new Variant(12.3));

            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(lastValue.AsBoxedObject(), Is.EqualTo(12.3));
        }

        [Test]
        public async Task UnsubscribeStopsNotifications()
        {
            int notifications = 0;
            await m_provider.SubscribeAsync(
                m_voltageTag,
                subscriberId: 7,
                (_, _, _, _) => Interlocked.Increment(ref notifications),
                CancellationToken.None).ConfigureAwait(false);
            await m_provider.UnsubscribeAsync(m_voltageTag, subscriberId: 7, CancellationToken.None).ConfigureAwait(false);

            m_provider.SetValue("Voltage", new Variant(1.0));

            Assert.That(notifications, Is.Zero);
        }

        [Test]
        public async Task InvokeActionEchoesInputsToOutputs()
        {
            var actionTag = new WotActionTag(
                "Echo",
                new NodeId(2u, 2),
                [new Argument { Name = "in1", DataType = Ua.DataTypeIds.Int64 }],
                [new Argument { Name = "out1", DataType = Ua.DataTypeIds.Int64 }],
                form: null);
            var outputs = new Variant[1];

            ServiceResult result = await m_provider.InvokeActionAsync(
                actionTag,
                [new Variant(42L)],
                outputs,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(outputs[0].AsBoxedObject(), Is.EqualTo(42L));
            Assert.That(m_provider.Invocations, Has.Count.EqualTo(1));
            Assert.That(m_provider.Invocations[0].Name, Is.EqualTo("Echo"));
        }
    }
}
