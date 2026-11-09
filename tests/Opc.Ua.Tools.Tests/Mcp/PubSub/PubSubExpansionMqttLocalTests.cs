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

#if NET10_0
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Mqtt;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Exercises the shipped MQTTnet client and real authorized socket against a bounded loopback protocol peer.
    /// The peer is a fixture for CONNECT/PUBLISH/PING only, not a production broker implementation.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [Category("PubSubLocal")]
    [NonParallelizable]
    public sealed class PubSubExpansionMqttLocalTests
    {
        [Test]
        public async Task LocalMqttPublisherUsesAuthorizedSocketAndEmitsNativeJsonTypedValuesAsync()
        {
            await using var peer = new PublisherPeer();
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            string[] auxiliary = [.. s_auxiliaryKinds
                .Select(kind => MqttTopicBuilder.BuildPublisherTopic(
                    "opcua", MqttEncoding.Json, kind, Variant.From((ushort)42)))];
            string status = MqttTopicBuilder.BuildPublisherTopic(
                "opcua", MqttEncoding.Json, "status", Variant.From((ushort)42));
            PubSubApplicationInput input = PubSubExpansionTestContext.Input() with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Mqtt,
                    Endpoint = $"mqtt://127.0.0.1:{peer.Port}",
                    Encoding = PubSubMessageEncoding.Json,
                    Topic = "plant/data",
                    MetadataTopic = "plant/metadata",
                    AuxiliaryTopics = new ArrayOf<string>(auxiliary)
                }
            };
            IMcpOperationPolicy authority = fixture.Services.GetRequiredService<IMcpOperationPolicy>();
            var network = new McpNetworkPolicy(authority,
                [new McpNetworkRule
                {
                    Scheme = "mqtt", Host = "127.0.0.1", Port = peer.Port, Networks = ["127.0.0.1/32"],
                    Operations = ["publish"],
                    Resources = new ArrayOf<string>(new[] { "plant/data", "plant/metadata", status }.Concat(auxiliary)
                        .ToArray()), Capability = McpCapability.PubSubRuntime
                }]);
            var options = new McpPubSubOptions();
            var transports = new McpPubSubTransportProvider(authority, network, options, TimeProvider.System);
            var runtime = new McpPubSubRuntimeManager(authority, fixture.Registry, options, transports, fixture.Secrets,
                fixture.Sessions.Telemetry, TimeProvider.System,
                fixture.Services.GetRequiredService<McpPubSubKeyReferences>());
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string handle = (await runtime.CreateAsync(input, lifetime.Token).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            try
            {
                _ = await runtime.StartAsync(handle, lifetime.Token).ConfigureAwait(false);
                byte[] payload = await peer.Data.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
                var metadataRegistry = new DataSetMetaDataRegistry();
                var declaredSource = new McpPubSubDataSetSource(
                    input, options, fixture.Remote.Context, TimeProvider.System);
                DataSetMetaDataType metadata = declaredSource.BuildMetaData();
                var metadataKey = new DataSetMetaDataKey(
                    PublisherId.FromUInt16(42), 0, 1, metadata.DataSetClassId, 1);
                metadataRegistry.Register(metadataKey, metadata);
                var context = new PubSubNetworkMessageContext(fixture.Remote.Context, metadataRegistry,
                    new PubSubDiagnostics(PubSubDiagnosticsLevel.Low), TimeProvider.System);
                var decoder = new PubSub.Encoding.Json.JsonDecoder();
                PubSubNetworkMessage? message = await decoder.TryDecodeAsync(payload, context, lifetime.Token)
                    .ConfigureAwait(false);

                Assert.That(peer.Connected, Is.True);
                Assert.That(message, Is.Not.Null);
                Assert.That(message!.TransportProfileUri, Is.EqualTo(Profiles.PubSubMqttJsonTransport));
                Assert.That(message.DataSetMessages, Has.Count.EqualTo(1));
                DataSetField count = message.DataSetMessages[0].Fields.ToList().Single(field => field.Name == "Count");
                DataSetField enabled = message.DataSetMessages[0].Fields.ToList()
                    .Single(field => field.Name == "Enabled");
                Assert.That(count.Value.TryGetValue(out int number), Is.True);
                Assert.That(number, Is.EqualTo(7));
                Assert.That(enabled.Value.TryGetValue(out bool flag), Is.True);
                Assert.That(flag, Is.False);
            }
            finally
            {
                _ = await runtime.RemoveAsync(handle).ConfigureAwait(false);
            }
        }

        private static readonly string[] s_auxiliaryKinds = ["application", "endpoints", "connection"];

        private sealed class PublisherPeer : IAsyncDisposable
        {
            internal PublisherPeer()
            {
                m_listener.Start();
                Port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
                m_pump = RunAsync();
            }

            internal int Port { get; }
            internal bool Connected { get; private set; }

            internal TaskCompletionSource<byte[]> Data { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await m_shutdown.CancelAsync().ConfigureAwait(false);
                    m_listener.Stop();
                    await m_pump.ConfigureAwait(false);
                }
                finally
                {
                    m_listener.Dispose();
                    m_shutdown.Dispose();
                }
            }

            private async Task RunAsync()
            {
                try
                {
                    using TcpClient client = await m_listener.AcceptTcpClientAsync(m_shutdown.Token)
                        .ConfigureAwait(false);
                    await using NetworkStream stream = client.GetStream();
                    byte[] single = new byte[1];
                    while (!m_shutdown.IsCancellationRequested)
                    {
                        int read = await stream.ReadAsync(single, m_shutdown.Token).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }
                        byte header = single[0];
                        int length = 0;
                        int multiplier = 1;
                        for (int index = 0; index < 4; index++)
                        {
                            await stream.ReadExactlyAsync(single, m_shutdown.Token).ConfigureAwait(false);
                            length += (single[0] & 127) * multiplier;
                            if ((single[0] & 128) == 0)
                            {
                                break;
                            }
                            multiplier *= 128;
                        }
                        if (length > 65536)
                        {
                            throw new IOException("The local fixture received an oversized MQTT packet.");
                        }
                        byte[] body = new byte[length];
                        await stream.ReadExactlyAsync(body, m_shutdown.Token).ConfigureAwait(false);
                        switch (header >> 4)
                        {
                            case 1:
                                Connected = true;
                                await stream.WriteAsync(new byte[] { 0x20, 3, 0, 0, 0 }, m_shutdown.Token)
                                    .ConfigureAwait(false);
                                break;
                            case 3:
                                (string topic, ushort identifier, byte[] payload) = Publish(header, body);
                                if ((header & 6) == 2)
                                {
                                    byte[] acknowledgement = [0x40, 4, (byte)(identifier >> 8), (byte)identifier, 0, 0];
                                    await stream.WriteAsync(acknowledgement, m_shutdown.Token).ConfigureAwait(false);
                                }
                                if (topic == "plant/data")
                                {
                                    Data.TrySetResult(payload);
                                }
                                break;
                            case 12:
                                await stream.WriteAsync(new byte[] { 0xD0, 0 }, m_shutdown.Token).ConfigureAwait(false);
                                break;
                            case 14:
                                return;
                            default:
                                throw new IOException(
                                    "The local publisher fixture received an unexpected packet type.");
                        }
                    }
                }
                catch (Exception exception) when (m_shutdown.IsCancellationRequested &&
                    exception is OperationCanceledException or IOException or
                        SocketException or ObjectDisposedException)
                {
                }
                catch (Exception exception)
                {
                    Data.TrySetException(exception);
                    throw;
                }
            }

            private static (string Topic, ushort Identifier, byte[] Payload) Publish(byte header, byte[] body)
            {
                int topicLength = BinaryPrimitives.ReadUInt16BigEndian(body);
                string topic = Encoding.UTF8.GetString(body, 2, topicLength);
                int position = 2 + topicLength;
                ushort identifier = 0;
                if ((header & 6) != 0)
                {
                    identifier = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(position));
                    position += 2;
                }
                int properties = 0;
                int multiplier = 1;
                byte digit;
                do
                {
                    digit = body[position++];
                    properties += (digit & 127) * multiplier;
                    multiplier *= 128;
                }
                while ((digit & 128) != 0);
                position += properties;
                return (topic, identifier, body[position..]);
            }

            private readonly TcpListener m_listener = new(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource m_shutdown = new(TimeSpan.FromSeconds(30));
            private readonly Task m_pump;
        }
    }
}
#endif
