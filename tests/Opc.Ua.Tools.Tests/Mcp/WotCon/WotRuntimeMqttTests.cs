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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MQTTnet;
using MQTTnet.Channel;
using MQTTnet.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpRuntime;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Mqtt;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Uses the existing MQTTnet broker and executor rather than implementing a separate MQTT protocol engine.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public sealed class WotRuntimeMqttTests
    {
        [Test]
        public async Task LocalBrokerRoundTripsRetainedValueThroughAuthorizedStreamAsync()
        {
            int port = FreePort();
            using MqttServer broker = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
                .WithDefaultEndpoint().WithDefaultEndpointPort(port)
                .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).Build());
            await broker.StartAsync().ConfigureAwait(false);
            try
            {
                await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [
                    WotRuntimeTestContext.Rule(McpCapability.WotRuntime, ["subscribe"], ["things/value"],
                        "mqtt", port: port),
                    WotRuntimeTestContext.Rule(McpCapability.WotWrite, ["publish"], ["things/value"],
                        "mqtt", port: port)
                ], realExecutors: true);
                using IDisposable caller = fixture.Enter();
                string document = Document(port);
                WotRuntimeSelection selection = WotRuntimeTestContext.Selection(document) with
                {
                    BindingId = "w3c.mqtt",
                    BindingVersion = "1.0-ed"
                };
                JsonObject opened = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("mqtt",
                    new WotDocumentSource { Text = document }, selection, ["writeproperty", "readproperty"])
                    .ConfigureAwait(false));
                string handle = opened["runtime"]!["handle"]!.GetValue<string>();
                fixture.Dns.VerifyNoOtherCalls();

                _ = WotRuntimeTestContext.Payload(await fixture.Tools.WritePropertyAsync(handle,
                    WotRuntimeTestContext.Value(BuiltInType.Int32, "17")).ConfigureAwait(false));
                JsonObject result = WotRuntimeTestContext.Payload(
                    await fixture.Tools.ReadPropertyAsync(handle).ConfigureAwait(false));
                using var decoder = new JsonDecoder(result.ToJsonString(),
                    ServiceMessageContext.CreateEmpty(McpRuntimeTestContext.CreateTelemetry()));
                DataValue received = decoder.ReadDataValue("dataValue");

                Assert.That(received.WrappedValue.TryGetValue(out long integer), Is.True);
                Assert.That(integer, Is.EqualTo(17));
                Assert.That(received.StatusCode, Is.EqualTo(StatusCodes.Good));
                fixture.Dns.Verify(value => value.ResolveAsync("device.example", It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
                _ = WotRuntimeTestContext.Payload(await fixture.Tools.CloseAsync(handle).ConfigureAwait(false));
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task StreamProviderPreservesOriginalTlsHostAsync()
        {
            var provider = new Mock<IMqttClientStreamProvider>(MockBehavior.Strict);
            var client = new Mock<IMqttClient>(MockBehavior.Strict);
            MqttClientOptions? captured = null;
            client.Setup(value => value.ConnectAsync(It.IsAny<MqttClientOptions>(), It.IsAny<CancellationToken>()))
                .Callback((MqttClientOptions options, CancellationToken _) => captured = options)
                .ReturnsAsync(new MqttClientConnectResult());
            client.Setup(value => value.DisconnectAsync(
                It.IsAny<MqttClientDisconnectOptions>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            client.Setup(value => value.Dispose());
            var executor = new MqttWotBindingExecutor(new MqttWotBindingOptions
            {
                ClientFactory = () => client.Object,
                StreamProvider = provider.Object
            });
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll(), [executor]);
            string document = Document(8883).Replace("mqtt://device.example", "mqtts://device.example",
                StringComparison.Ordinal);
            WotCompiledForm form = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "mqtt", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document)))
                .CompiledForms.First(value => value.OpToken == "readproperty");
            await using IWotBindingChannel channel = await executor.ActivateAsync(form,
                new WotExecutorContext(telemetry: McpRuntimeTestContext.CreateTelemetry())).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ChannelOptions, Is.TypeOf<MqttClientTcpOptions>());
            var tcp = (MqttClientTcpOptions)captured.ChannelOptions;
            Assert.That(tcp.StreamProvider, Is.SameAs(provider.Object));
            Assert.That(tcp.RemoteEndpoint, Is.TypeOf<DnsEndPoint>());
            Assert.That(((DnsEndPoint)tcp.RemoteEndpoint).Host, Is.EqualTo("device.example"));
            Assert.That(tcp.TlsOptions, Is.Not.Null);
            Assert.That(tcp.TlsOptions!.UseTls, Is.True);
            provider.VerifyNoOtherCalls();
        }

        [TestCase("things/other")]
        [TestCase("things/+")]
        [TestCase("things/#")]
        public async Task UnapprovedOrWildcardTopicsNeverConnectAsync(string topic)
        {
            await using var fixture = new WotRuntimeTestContext(options => options.NetworkRules =
                [
                    WotRuntimeTestContext.Rule(
                        McpCapability.WotRuntime, ["subscribe"], ["things/value"], "mqtt", port: 1883)
                ],
                realExecutors: true);
            using IDisposable caller = fixture.Enter();
            string document = Document(1883).Replace("things/value", topic, StringComparison.Ordinal);
            _ = WotRuntimeTestContext.Payload(await fixture.Tools.OpenAsync("topic",
                new WotDocumentSource { Text = document },
                WotRuntimeTestContext.Selection(document) with
                {
                    BindingId = "w3c.mqtt",
                    BindingVersion = "1.0-ed"
                }, ["readproperty"]).ConfigureAwait(false), error: true);

            fixture.Dns.VerifyNoOtherCalls();
            Assert.That(fixture.Registry.List(), Is.Empty);
        }

        [TestCase(MqttClientPublishReasonCode.NotAuthorized)]
        [TestCase(MqttClientPublishReasonCode.QuotaExceeded)]
        public async Task BrokerPublicationRefusalIsReportedWithoutRetryAsync(MqttClientPublishReasonCode reason)
        {
            var client = new Mock<IMqttClient>(MockBehavior.Strict);
            client.Setup(value => value.ConnectAsync(It.IsAny<MqttClientOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MqttClientConnectResult());
            client.Setup(value => value.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MqttClientPublishResult(1, reason, "untrusted broker text", []));
            client.Setup(value => value.DisconnectAsync(
                It.IsAny<MqttClientDisconnectOptions>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            client.Setup(value => value.Dispose());
            var executor = new MqttWotBindingExecutor(
                new MqttWotBindingOptions { ClientFactory = () => client.Object });
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll(), [executor]);
            WotCompiledForm form = registry.Prepare(WotBindingPlanRequest.FromDocument("mqtt",
                WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(Document(1883))))
                .CompiledForms.Single(value => value.OpToken == "writeproperty");
            await using IWotBindingChannel channel = await executor.ActivateAsync(form,
                new WotExecutorContext(telemetry: McpRuntimeTestContext.CreateTelemetry())).ConfigureAwait(false);
            WotWriteResult result = await channel.WriteAsync(new DataValue(Variant.From(19))).ConfigureAwait(false);

            Assert.That(result.Status.Code, Is.EqualTo(StatusCodes.BadCommunicationError.Code));
            Assert.That(result.Success, Is.False);
            client.Verify(value => value.PublishAsync(
                It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Uses the repository MQTT fixture's ephemeral-port pattern for a loopback-only broker.
        /// </summary>
        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>
        /// Authors one exact QoS 1 retained topic for an acknowledged publish followed by a deterministic read.
        /// </summary>
        private static string Document(int port)
        {
            return $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Bounded MQTT runtime",
                  "properties": {
                    "temperature": {
                      "type": "number",
                      "forms": [{
                        "href": "mqtt://device.example:{{port}}/things/value",
                        "mqv:qos": 1,
                        "mqv:retain": true,
                        "op": ["readproperty", "writeproperty"]
                      }]
                    }
                  }
                }
                """;
        }
    }
}
#endif
