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

using System.Collections.Generic;
using System.Text.Json;
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
                DataTypeIds.Double,
                ValueRanks.Scalar,
                readOnly: false,
                observable: true,
                form: (JsonElement?)null);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_provider.DisposeAsync();
        }

        [Test]
        public async Task ReadReturnsSeededDefaultBeforeFirstWrite()
        {
            (ServiceResult status, Variant value) = await m_provider
                .ReadAsync(m_voltageTag, CancellationToken.None);

            Assert.That(ServiceResult.IsGood(status), Is.True);
            Assert.That(value.AsBoxedObject(), Is.EqualTo(0.0));
        }

        [Test]
        public async Task WriteUpdatesSubsequentRead()
        {
            await m_provider.WriteAsync(m_voltageTag, new Variant(24.5), CancellationToken.None);

            (ServiceResult _, Variant value) = await m_provider
                .ReadAsync(m_voltageTag, CancellationToken.None);

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
                CancellationToken.None);

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
                CancellationToken.None);
            await m_provider.UnsubscribeAsync(m_voltageTag, subscriberId: 7, CancellationToken.None);

            m_provider.SetValue("Voltage", new Variant(1.0));

            Assert.That(notifications, Is.EqualTo(0));
        }

        [Test]
        public async Task InvokeActionEchoesInputsToOutputs()
        {
            var actionTag = new WotActionTag(
                "Echo",
                new NodeId(2u, 2),
                new[] { new Argument { Name = "in1", DataType = DataTypeIds.Int64 } },
                new[] { new Argument { Name = "out1", DataType = DataTypeIds.Int64 } },
                form: (JsonElement?)null);
            Variant[] outputs = new Variant[1];

            ServiceResult result = await m_provider.InvokeActionAsync(
                actionTag,
                new[] { new Variant(42L) },
                outputs,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(outputs[0].AsBoxedObject(), Is.EqualTo(42L));
            Assert.That(m_provider.Invocations, Has.Count.EqualTo(1));
            Assert.That(m_provider.Invocations[0].Name, Is.EqualTo("Echo"));
        }
    }
}
