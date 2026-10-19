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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using MQTTnet;
using MQTTnet.Channel;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Mqtt;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Exercises MQTT activation and its actual transport boundary without a broker or listening socket.
    /// </summary>
    [TestFixture]
    public sealed class SecurityF28MqttEndpointTests
    {
        /// <summary>
        /// Every operation rejects a missing actual host even if BaseUri names an allowed broker.
        /// </summary>
        [TestCase("mqtt", null, false)]
        [TestCase("mqtt", "", false)]
        [TestCase("mqtt", " \t", false)]
        [TestCase("mqtts", null, false)]
        [TestCase("mqtts", "", false)]
        [TestCase("mqtts", " \t", false)]
        [TestCase("mqtt", null, true)]
        [TestCase("mqtts", " \t", true)]
        [Category("SecurityRegression")]
        public void SecurityF28HostlessMqttNeverResolvesCredentialsOrConnects(
            string scheme, string? host, bool allowLoopback)
        {
            var attempts = new RejectedMqttConnection(new WotEndpointPolicy { AllowLoopback = allowLoopback });
            WotProtocolBinderRegistry registry = attempts.Registry();
            ImmutableArray<WotCompiledForm> forms = Plan(registry, scheme + "://broker.example/private/status", true);
            Assert.That(forms.Select(form => form.Operation), Is.EquivalentTo(
            [
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty,
                WoTBindingCapabilityEnum.InvokeAction
            ]));
            Assert.That(forms.All(form => form.Security.Any(
                reference => reference.Scheme == WotSecurityScheme.Basic)), Is.True);

            foreach (WotCompiledForm planned in forms)
            {
                WotCompiledForm form = WithEndpoint(planned, new WotEndpointDescriptor(
                    scheme, host, -1, scheme + "://broker.example"));
                ServiceResultException? preparation = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await MqttWotConnection.PrepareAsync(
                        form, attempts.Context, attempts.Options, "security-regression", CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(preparation!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                AssertRejected(
                    () => attempts.Executor.ActivateAsync(form, attempts.Context),
                    StatusCodes.BadInvalidArgument);
                AssertRejected(() => registry.OpenChannelAsync(form), StatusCodes.BadInvalidArgument);
            }

            attempts.AssertNoAttempt();
        }

        /// <summary>
        /// The registry rejects a hostless authority before invoking a usable actual destination.
        /// </summary>
        [TestCase("mqtt")]
        [TestCase("mqtts")]
        [Category("SecurityRegression")]
        public void SecurityF28HostlessMqttAuthorityNeverActivates(string scheme)
        {
            var attempts = new RejectedMqttConnection();
            WotProtocolBinderRegistry registry = attempts.Registry();
            WotCompiledForm planned = Plan(registry, scheme + "://broker.example/private/status", true)[0];
            WotCompiledForm form = WithEndpoint(planned, new WotEndpointDescriptor(
                scheme, "broker.example", -1, scheme + "://"));

            AssertRejected(() => registry.OpenChannelAsync(form), StatusCodes.BadInvalidArgument);

            attempts.AssertNoAttempt();
        }

        /// <summary>
        /// A separately supplied public BaseUri cannot authorize an actual loopback connection.
        /// </summary>
        [TestCase("mqtt", "127.0.0.1")]
        [TestCase("mqtts", "127.0.0.1")]
        [TestCase("mqtt", "::1")]
        [TestCase("mqtts", "::1")]
        [TestCase("mqtt", "localhost")]
        [Category("SecurityRegression")]
        public void SecurityF28FinalMqttDestinationCannotBypassLoopbackPolicy(string scheme, string host)
        {
            var attempts = new RejectedMqttConnection();
            WotProtocolBinderRegistry registry = attempts.Registry();
            WotCompiledForm planned = Plan(registry, scheme + "://broker.example/private/status", true)[0];
            WotCompiledForm form = WithEndpoint(planned, new WotEndpointDescriptor(
                scheme, host, -1, scheme + "://broker.example"));

            AssertRejected(
                () => attempts.Executor.ActivateAsync(form, attempts.Context),
                StatusCodes.BadSecurityChecksFailed);
            AssertRejected(() => registry.OpenChannelAsync(form), StatusCodes.BadSecurityChecksFailed);

            attempts.AssertNoAttempt();
        }

        /// <summary>
        /// Final-host validation applies both exclusive allow lists and explicit block lists.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public void SecurityF28FinalMqttDestinationHonorsHostRestrictions(bool useAllowList)
        {
            var policy = new WotEndpointPolicy();
            if (useAllowList)
            {
                policy.AllowedHosts.Add("broker.example");
            }
            else
            {
                policy.BlockedHosts.Add("other.example");
            }
            var attempts = new RejectedMqttConnection(policy);
            WotProtocolBinderRegistry registry = attempts.Registry();
            WotCompiledForm planned = Plan(registry, "mqtts://broker.example/private/status", true)[0];
            WotCompiledForm form = WithEndpoint(planned, new WotEndpointDescriptor(
                "mqtts", "other.example", 8883, "mqtts://broker.example"));

            AssertRejected(
                () => attempts.Executor.ActivateAsync(form, attempts.Context),
                StatusCodes.BadSecurityChecksFailed);
            AssertRejected(() => registry.OpenChannelAsync(form), StatusCodes.BadSecurityChecksFailed);

            attempts.AssertNoAttempt();
        }

        /// <summary>
        /// Permitted named brokers and explicitly permitted loopback brokers still publish through MQTTnet.
        /// </summary>
        [TestCase("mqtt://broker.example/private/status", "broker.example", 1883, false)]
        [TestCase("mqtt://broker.example:2883/private/status", "broker.example", 2883, false)]
        [TestCase("mqtt://127.0.0.1/private/status", "127.0.0.1", 1883, true)]
        [TestCase("mqtt://[::1]/private/status", "::1", 1883, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF28PermittedMqttConnectsToExactDestination(
            string href, string expectedHost, int expectedPort, bool allowLoopback)
        {
            var policy = new WotEndpointPolicy { AllowLoopback = allowLoopback };
            policy.AllowedHosts.Add(expectedHost);
            var provider = new Mock<IMqttClientStreamProvider>(MockBehavior.Strict);
            using var stream = new MqttReplyStream();
            EndPoint? destination = null;
            provider.Setup(value => value.ConnectAsync(It.IsAny<EndPoint>(), It.IsAny<CancellationToken>()))
                .Callback((EndPoint endpoint, CancellationToken _) => destination = endpoint)
                .ReturnsAsync(stream);
            var executor = new MqttWotBindingExecutor(new MqttWotBindingOptions { StreamProvider = provider.Object });
            var registry = new WotProtocolBinderRegistry(
                [new MqttBindingPlanner()], [executor], endpointPolicy: policy);
            WotCompiledForm form = Plan(registry, href, false)
                .Single(value => value.Operation == WoTBindingCapabilityEnum.WriteProperty);

            await using (IWotBindingChannel channel = await registry.OpenChannelAsync(form).ConfigureAwait(false))
            {
                WotWriteResult result = await channel.WriteAsync(new DataValue(Variant.From(42))).ConfigureAwait(false);

                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(stream.PublishedTopic, Is.EqualTo("private/status"));
                Assert.That(stream.PublishedPayload, Is.EqualTo("42"));
            }

            provider.Verify(
                value => value.ConnectAsync(It.IsAny<EndPoint>(), It.IsAny<CancellationToken>()), Times.Once);
            if (destination is DnsEndPoint dns)
            {
                Assert.That(dns.Host, Is.EqualTo(expectedHost));
                Assert.That(dns.Port, Is.EqualTo(expectedPort));
            }
            else
            {
                Assert.That(destination, Is.TypeOf<IPEndPoint>());
                var ip = (IPEndPoint)destination!;
                Assert.That(ip.Address.ToString(), Is.EqualTo(expectedHost));
                Assert.That(ip.Port, Is.EqualTo(expectedPort));
            }
        }

        private static ImmutableArray<WotCompiledForm> Plan(
            WotProtocolBinderRegistry registry, string href, bool withSecurity)
        {
            string security = withSecurity
                ? "\"securityDefinitions\":{\"basic_sc\":{\"scheme\":\"basic\"}},\"security\":\"basic_sc\","
                : string.Empty;
            string document = $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "MQTT destination",
                  {{security}}
                  "properties": {
                    "status": {
                      "type": "number",
                      "observable": true,
                      "forms": [{
                        "href": "{{href}}",
                        "op": ["readproperty", "writeproperty", "observeproperty"]
                      }]
                    }
                  },
                  "actions": {
                    "set": {"forms": [{"href": "{{href}}", "op": "invokeaction"}]}
                  }
                }
                """;
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "mqtt-destination", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document)));
            return plan.CompiledForms;
        }

        private static WotCompiledForm WithEndpoint(WotCompiledForm form, WotEndpointDescriptor endpoint)
        {
            return new WotCompiledForm(
                form.Binding, form.AffordanceKind, form.AffordanceName, form.JsonPointer, form.Operation, form.OpToken,
                endpoint, form.Addressing, form.OperationInfo, form.Payload, form.Security, form.IsExecutable);
        }

        private static void AssertRejected(Func<ValueTask<IWotBindingChannel>> activate, StatusCode expected)
        {
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await using IWotBindingChannel channel = await activate().ConfigureAwait(false);
            });
            Assert.That(exception!.StatusCode, Is.EqualTo(expected));
        }

        /// <summary>
        /// Counts all sensitive boundaries and ensures even an unexpected attempt cannot use a real socket.
        /// </summary>
        private sealed class RejectedMqttConnection
        {
            /// <summary>
            /// Configures counting credential, client and stream boundaries for a policy rejection.
            /// </summary>
            public RejectedMqttConnection(WotEndpointPolicy? policy = null)
            {
                m_policy = policy ?? new WotEndpointPolicy();
                m_credentials.Setup(value => value.ResolveAsync(
                    It.IsAny<WotCredentialReference>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new WotCredential(WotSecurityScheme.Basic));
                m_provider.Setup(value => value.ConnectAsync(It.IsAny<EndPoint>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new IOException("An unauthorized connector was invoked."));
                Options = new MqttWotBindingOptions
                {
                    StreamProvider = m_provider.Object,
                    ClientFactory = () =>
                    {
                        m_clientCreations++;
                        return new MqttClientFactory().CreateMqttClient();
                    }
                };
                Executor = new MqttWotBindingExecutor(Options);
                Context = new WotExecutorContext(m_credentials.Object, endpointPolicy: m_policy);
            }

            /// <summary>
            /// Gets options whose provider cannot open a real connection.
            /// </summary>
            public MqttWotBindingOptions Options { get; }

            /// <summary>
            /// Gets the real executor under test.
            /// </summary>
            public MqttWotBindingExecutor Executor { get; }

            /// <summary>
            /// Gets the direct activation context with the same policy as the registry.
            /// </summary>
            public WotExecutorContext Context { get; }

            /// <summary>
            /// Creates the normal registry activation path.
            /// </summary>
            public WotProtocolBinderRegistry Registry()
            {
                return new WotProtocolBinderRegistry(
                    [new MqttBindingPlanner()], [Executor], m_credentials.Object, endpointPolicy: m_policy);
            }

            /// <summary>
            /// Requires rejection before credentials, client construction or stream connection.
            /// </summary>
            public void AssertNoAttempt()
            {
                Assert.That(m_clientCreations, Is.Zero, "No MQTT client may be created before endpoint approval.");
                m_credentials.VerifyNoOtherCalls();
                m_provider.VerifyNoOtherCalls();
            }

            private readonly WotEndpointPolicy m_policy;
            private readonly Mock<IWotCredentialProvider> m_credentials = new(MockBehavior.Strict);
            private readonly Mock<IMqttClientStreamProvider> m_provider = new(MockBehavior.Strict);
            private int m_clientCreations;
        }

        /// <summary>
        /// Supplies only in-memory MQTT CONNACK/PINGRESP replies and records QoS-zero publications.
        /// </summary>
        private sealed class MqttReplyStream : Stream
        {
            /// <summary>
            /// Gets the topic emitted by the real MQTT encoder.
            /// </summary>
            public string? PublishedTopic { get; private set; }

            /// <summary>
            /// Gets the JSON payload emitted by the real binding and MQTT encoder.
            /// </summary>
            public string? PublishedPayload { get; private set; }

            /// <inheritdoc/>
            public override bool CanRead => !m_disposed;

            /// <inheritdoc/>
            public override bool CanSeek => false;

            /// <inheritdoc/>
            public override bool CanWrite => !m_disposed;

            /// <inheritdoc/>
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc/>
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (buffer.IsEmpty)
                {
                    return 0;
                }
                while (m_readOffset == m_reply.Length)
                {
                    try
                    {
                        m_reply = await m_replies.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (ChannelClosedException)
                    {
                        return 0;
                    }
                    m_readOffset = 0;
                }
                int count = Math.Min(buffer.Length, m_reply.Length - m_readOffset);
                m_reply.AsMemory(m_readOffset, count).CopyTo(buffer);
                m_readOffset += count;
                return count;
            }

            /// <inheritdoc/>
            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            /// <inheritdoc/>
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_pending.AddRange(buffer.ToArray());
                while (m_pending.Count >= 2)
                {
                    int index = 1;
                    int remaining = 0;
                    int multiplier = 1;
                    byte next;
                    do
                    {
                        if (index == m_pending.Count)
                        {
                            return default;
                        }
                        next = m_pending[index++];
                        remaining += (next & 0x7F) * multiplier;
                        multiplier *= 128;
                    }
                    while ((next & 0x80) != 0);
                    int packetLength = index + remaining;
                    if (m_pending.Count < packetLength)
                    {
                        return default;
                    }
                    byte[] packet = [.. m_pending.GetRange(0, packetLength)];
                    m_pending.RemoveRange(0, packetLength);
                    switch (packet[0] >> 4)
                    {
                        case 1:
                            m_version5 = packet[index + 6] == 5;
                            m_replies.Writer.TryWrite(m_version5
                                ? [0x20, 0x03, 0x00, 0x00, 0x00]
                                : [0x20, 0x02, 0x00, 0x00]);
                            break;
                        case 3:
                            int topicLength = (packet[index] << 8) | packet[index + 1];
                            index += 2;
                            PublishedTopic = Encoding.UTF8.GetString(packet, index, topicLength);
                            index += topicLength;
                            Assert.That(packet[0] & 0x06, Is.Zero, "The test publishes with QoS zero.");
                            if (m_version5)
                            {
                                int propertyLength = 0;
                                multiplier = 1;
                                do
                                {
                                    next = packet[index++];
                                    propertyLength += (next & 0x7F) * multiplier;
                                    multiplier *= 128;
                                }
                                while ((next & 0x80) != 0);
                                index += propertyLength;
                            }
                            PublishedPayload = Encoding.UTF8.GetString(packet, index, packet.Length - index);
                            break;
                        case 12:
                            m_replies.Writer.TryWrite([0xD0, 0x00]);
                            break;
                    }
                }
                return default;
            }

            /// <inheritdoc/>
            public override Task WriteAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            /// <inheritdoc/>
            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc/>
            public override void Flush()
            {
            }

            /// <inheritdoc/>
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_disposed = true;
                    m_replies.Writer.TryComplete();
                }
                base.Dispose(disposing);
            }

            private readonly Channel<byte[]> m_replies = Channel.CreateUnbounded<byte[]>();
            private readonly List<byte> m_pending = [];
            private byte[] m_reply = [];
            private int m_readOffset;
            private bool m_version5;
            private bool m_disposed;
        }
    }
}
