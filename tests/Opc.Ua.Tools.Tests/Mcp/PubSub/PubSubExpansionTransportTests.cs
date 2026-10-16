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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Kafka;
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Real transport factories and supported stream/topic seams; all sockets remain injected in these tests.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionTransportTests
    {
        [TestCase("opc.udp://127.0.0.1:4840", true)]
        [TestCase("opc.dtls://127.0.0.1:4843", true)]
        [TestCase("OPC.DTLS://127.0.0.1:4843", true)]
        [TestCase("mqtt://127.0.0.1:1883", false)]
        [TestCase("opc.tcp://127.0.0.1:4840", false)]
        [TestCase("opc.dtls-other://127.0.0.1:4843", false)]
        public void UdpProfileValidationMatchesSupportedDatagramSchemes(string endpoint, bool accepted)
        {
            var configuration = new PubSubConfigurationDataType
            {
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        Name = "scheme-check",
                        TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                        Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = endpoint })
                    }
                ]
            };
            var validator = new PubSubConfigurationValidator(s_udpTransportProfiles);

            PubSubConfigurationValidationResult result = validator.Validate(configuration);

            Assert.That(result.IsValid, Is.EqualTo(accepted));
            Assert.That(result.Issues.FindIndex(issue => issue.Code == "PSC0007") >= 0,
                Is.EqualTo(!accepted));
        }

        [Test]
        public async Task KafkaSchemaIsSupportedButBuiltinLiveBackendIsExplicitlyBlockedAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            var dns = new RecordingDns();
            var sockets = new RecordingTcp();
            IMcpOperationPolicy authority = fixture.Services.GetRequiredService<IMcpOperationPolicy>();
            var provider = new McpPubSubTransportProvider(authority,
                new McpNetworkPolicy(authority, [], dns, sockets), new McpPubSubOptions(), fixture.Clock);
            PubSubApplicationInput baseline = PubSubExpansionTestContext.Input();
            PubSubApplicationInput input = baseline with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Kafka,
                    Endpoint = "kafka://broker.test:9092",
                    Encoding = PubSubMessageEncoding.Json,
                    Topic = "data",
                    MetadataTopic = "metadata"
                }
            };

            PubSubInputValidation.Validate(input, new McpPubSubOptions());
            JsonObject capability = provider.Capabilities().OfType<JsonObject>()
                .Single(item => item["transport"]!.GetValue<string>() == "Kafka");
            ServiceResultException? refusal = Assert.Throws<ServiceResultException>(
                () => provider.CreateFactory(input));

            Assert.That(PubSubInputValidation.Profile(input.Connection),
                Is.EqualTo(KafkaProfiles.PubSubKafkaJsonTransport));
            Assert.That(capability["backendAvailable"]!.GetValue<bool>(), Is.False);
            Assert.That(capability["activated"]!.GetValue<bool>(), Is.False);
            Assert.That(refusal!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(refusal.Message, Does.Contain("metadata-discovered"));
            Assert.That(dns.Calls, Is.Zero);
            Assert.That(sockets.Endpoints, Is.Empty);
        }

        [Test]
        public async Task MqttReconnectUsesCapturedGrantAndRejectsChangedDnsBeforeConnectingAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            var dns = new RecordingDns();
            dns.Answers.Enqueue([IPAddress.Parse("127.0.0.1")]);
            dns.Answers.Enqueue([IPAddress.Parse("127.0.0.1"), IPAddress.Parse("10.1.2.3")]);
            var sockets = new RecordingTcp();
            PubSubApplicationInput input = MqttInput();
            MqttBrokerTransport mqtt;
            using (fixture.Enter())
            {
                McpPubSubTransportProvider provider = Provider(fixture, input, dns, sockets);
                var source = new McpPubSubDataSetSource(
                    input, new McpPubSubOptions(), fixture.Remote.Context, fixture.Clock);
                IPubSubTransportFactory factory = provider.CreateFactory(input);
                mqtt = (MqttBrokerTransport)factory.Create(
                    McpPubSubConfiguration.Build(input, source.BuildMetaData()).Connections[0],
                    fixture.Sessions.Telemetry, fixture.Clock);
            }
            await using (mqtt.ConfigureAwait(false))
            {
                Assert.That(dns.Calls, Is.Zero, "Construction and policy preflight must not resolve or connect.");
                await using Stream connected = await mqtt.Options.GetStreamProvider()!.ConnectAsync(
                    new DnsEndPoint("broker.test", 1883), CancellationToken.None).ConfigureAwait(false);
                ServiceResultException? changed = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await mqtt.Options.GetStreamProvider()!.ConnectAsync(
                        new DnsEndPoint("broker.test", 1883), CancellationToken.None).ConfigureAwait(false));

                Assert.That(dns.Calls, Is.EqualTo(2));
                Assert.That(sockets.Endpoints, Has.Count.EqualTo(1));
                Assert.That(sockets.Endpoints[0], Is.EqualTo(new IPEndPoint(IPAddress.Loopback, 1883)));
                Assert.That(mqtt.Options.Endpoint, Is.EqualTo("mqtt://broker.test:1883"));
                Assert.That(changed!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [Test]
        public async Task MqttReaderInstallsOnlyExactConfiguredTopicsAndChecksPerMessageLimitsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubApplicationInput input = MqttInput() with { Mode = "Subscriber" };
            var dns = new RecordingDns();
            var sockets = new RecordingTcp();
            McpPubSubTransportProvider provider = Provider(fixture, input, dns, sockets);
            var source = new McpPubSubDataSetSource(
                input, new McpPubSubOptions(), fixture.Remote.Context, fixture.Clock);
            IPubSubTransportFactory factory = provider.CreateFactory(input);
            await using IPubSubTransport transport = factory.Create(
                McpPubSubConfiguration.Build(input, source.BuildMetaData()).Connections[0],
                fixture.Sessions.Telemetry, fixture.Clock);
            var mqtt = (MqttBrokerTransport)transport;

            Assert.That(mqtt.Subscriptions.Select(subscription => subscription.Topic),
                Is.EquivalentTo(s_readerTopics));
            Assert.That(mqtt.Options.IncludeDiscoverySubscriptions, Is.False);
            Assert.That(mqtt.Options.GetTopicPolicy()!.AcceptReceive("plant/data", 10), Is.True);
            Assert.That(mqtt.Options.GetTopicPolicy()!.AcceptReceive("plant/other", 10), Is.False);
            Assert.That(mqtt.Options.GetTopicPolicy()!.AcceptReceive("plant/data", 65537), Is.False);
            Assert.That(() => mqtt.Options.GetTopicPolicy()!.RequireSubscribe("plant/#"),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(() => mqtt.Options.GetTopicPolicy()!.RequirePublish("plant/data", 1),
                Throws.TypeOf<ServiceResultException>(), "Subscribe authority is not publish authority.");
            Assert.That(dns.Calls, Is.Zero);
            Assert.That(sockets.Endpoints, Is.Empty);
        }

        [Test]
        public async Task EmptyDatagramRulesFailBeforeTransportCreationOrDnsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            IMcpOperationPolicy authority = fixture.Services.GetRequiredService<IMcpOperationPolicy>();
            var dns = new RecordingDns();
            var sockets = new RecordingTcp();
            var provider = new McpPubSubTransportProvider(authority,
                new McpNetworkPolicy(authority, [], dns, sockets), new McpPubSubOptions(), fixture.Clock);

            ServiceResultException? refusal = Assert.Throws<ServiceResultException>(() =>
                provider.CreateFactory(PubSubExpansionTestContext.Input()));

            Assert.That(refusal!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(dns.Calls, Is.Zero);
            Assert.That(sockets.Endpoints, Is.Empty);
        }

        private static PubSubApplicationInput MqttInput()
        {
            return PubSubExpansionTestContext.Input() with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Mqtt,
                    Endpoint = "mqtt://broker.test:1883",
                    Encoding = PubSubMessageEncoding.Json,
                    Topic = "plant/data",
                    MetadataTopic = "plant/metadata"
                }
            };
        }

        private static McpPubSubTransportProvider Provider(
            PubSubExpansionTestContext fixture, PubSubApplicationInput input, RecordingDns dns, RecordingTcp sockets)
        {
            IMcpOperationPolicy authority = fixture.Services.GetRequiredService<IMcpOperationPolicy>();
            string status = MqttTopicBuilder.BuildPublisherTopic("opcua", MqttEncoding.Json, "status",
                PubSubInputValidation.Publisher(input.PublisherId, PubSubMessageEncoding.Json).ToVariant());
            var network = new McpNetworkPolicy(authority,
                [new McpNetworkRule
                {
                    Scheme = "mqtt", Host = "broker.test", Port = 1883, Networks = ["127.0.0.0/8"],
                    Operations = ["publish", "subscribe"], Resources = ["plant/data", "plant/metadata", status],
                    Capability = McpCapability.PubSubRuntime
                }], dns, sockets);
            return new McpPubSubTransportProvider(authority, network, new McpPubSubOptions(), fixture.Clock);
        }

        private static readonly string[] s_readerTopics = ["plant/data", "plant/metadata"];

        /// <summary>The actual profile shared by the UDP and DTLS transports.</summary>
        private static readonly string[] s_udpTransportProfiles = [Profiles.PubSubUdpUadpTransport];

        private sealed class RecordingDns : IMcpDnsResolver
        {
            internal int Calls { get; private set; }
            internal Queue<ArrayOf<IPAddress>> Answers { get; } = new();

            public ValueTask<ArrayOf<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
            {
                Calls++;
                Assert.That(host, Is.EqualTo("broker.test"));
                return ValueTask.FromResult(Answers.Dequeue());
            }
        }

        private sealed class RecordingTcp : IMcpTcpConnector
        {
            internal List<IPEndPoint> Endpoints { get; } = [];

            public ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct = default)
            {
                Endpoints.Add(endpoint);
                return ValueTask.FromResult<Stream>(new MemoryStream());
            }
        }
    }
}
#endif
