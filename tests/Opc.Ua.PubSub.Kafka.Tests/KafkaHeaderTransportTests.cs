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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Tests for promoting DataSet fields into Kafka record headers through
    /// the <see cref="IPubSubHeaderTransport"/> capability on
    /// <see cref="KafkaBrokerTransport"/>.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka field promotion to record headers")]
    [CancelAfter(10000)]
    public sealed class KafkaHeaderTransportTests
    {
        [Test]
        public async Task KafkaBrokerTransport_ImplementsHeaderCapability()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            Assert.That(transport, Is.InstanceOf<IPubSubHeaderTransport>());
        }

        [Test]
        public async Task SendAsync_WithProperties_EmitsRecordHeaders()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            PubSubMessageProperty[] properties =
            [
                new("Temperature", "21.5"),
                new("Unit", "C")
            ];

            await transport.SendAsync(new byte[] { 1, 2, 3 }, KafkaTestHelper.JsonTopic, properties)
                .ConfigureAwait(false);

            KafkaMessage message = KafkaTestHelper.FirstProduced(factory.Adapter);
            Assert.That(message.Headers, Is.Not.Null);
            Assert.That(message.Headers!, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(message.Headers!["Temperature"], Is.EqualTo("21.5"));
                Assert.That(message.Headers!["Unit"], Is.EqualTo("C"));
            });
        }

        [Test]
        public async Task SendAsync_NoProperties_EmitsNoRecordHeaders()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport
                .SendAsync(new byte[] { 1 }, KafkaTestHelper.JsonTopic,
                    default(ArrayOf<PubSubMessageProperty>))
                .ConfigureAwait(false);

            KafkaMessage message = KafkaTestHelper.FirstProduced(factory.Adapter);
            Assert.That(message.Headers, Is.Null);
        }

        [Test]
        public async Task PlainSendAsync_EmitsNoRecordHeaders()
        {
            var factory = new FakeKafkaClientFactory();
            await using KafkaBrokerTransport transport = KafkaTestHelper.NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport.SendAsync(new byte[] { 1 }, KafkaTestHelper.JsonTopic)
                .ConfigureAwait(false);

            KafkaMessage message = KafkaTestHelper.FirstProduced(factory.Adapter);
            Assert.That(message.Headers, Is.Null);
        }
    }
}
