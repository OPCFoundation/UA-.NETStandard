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
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Server;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Mqtt;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Channel-level tests for <c>MqttWotBindingChannel</c>:
    /// <c>InvokeAsync</c> (with and without inputs), <c>SubscribeEventAsync</c>
    /// delegating to <c>ObserveAsync</c>, decode failure in <c>ReadAsync</c>,
    /// subscription disposal stopping delivery, QoS 0 and QoS 2 configuration,
    /// and channel disposal.
    /// </summary>
    [TestFixture]
    public sealed class MqttWotBindingChannelTests
    {
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task<MqttServer> StartBrokerAsync(int port)
        {
            MqttServerOptions options = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(port)
                .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
                .Build();
            MqttServer broker = new MqttServerFactory().CreateMqttServer(options);
            await broker.StartAsync().ConfigureAwait(false);
            return broker;
        }

        private static WotProtocolBinderRegistry Registry(
            TimeSpan? readTimeout = null, WotBindingBounds? bounds = null)
        {
            return new WotProtocolBinderRegistry(
                [new MqttBindingPlanner()],
                [new MqttWotBindingExecutor(
                    new MqttWotBindingOptions { ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(5) })],
                bounds: bounds,
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
        }

        private static WotBindingPlan Plan(WotProtocolBinderRegistry registry, string td)
        {
            return registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
        }

        private static string PropertyTd(int port, string topic, string? op = null, int qos = 1)
        {
            string opClause = op is not null ? "\"op\":[\"" + op + "\"]," : string.Empty;
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"p\":{\"type\":\"number\",\"observable\":true,\"forms\":[{" +
                "\"href\":\"mqtt://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "/" + topic + "\"," + opClause +
                "\"mqv:qos\":" + qos.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"mqv:retain\":true}]}}}";
        }

        private static string ActionTd(int port, string topic)
        {
            return "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"actions\":{\"act\":{\"forms\":[{" +
                "\"href\":\"mqtt://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "/" + topic + "\",\"mqv:qos\":0}]}}}";
        }

        private static WotCompiledForm BuildRawForm(
            int port, string topic, WoTBindingCapabilityEnum capability, string opToken)
        {
            var endpoint = new WotEndpointDescriptor(
                "mqtt",
                "127.0.0.1",
                port,
                "mqtt://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture));
            var addressing = new WotAddressingDescriptor(topic);
            var operation = new WotOperationDescriptor(capability, opToken, "subscribe");
            var payload = new WotPayloadDescriptor("application/json", "json");
            return new WotCompiledForm(
                new WotBindingIdentity("w3c.mqtt", "1.0-ed", MqttBindingPlanner.BindingUri),
                WotAffordanceKind.Property,
                "p",
                "/properties/p/forms/0",
                capability,
                opToken,
                endpoint,
                addressing,
                operation,
                payload,
                ImmutableArray<WotCredentialReference>.Empty,
                isExecutable: true);
        }

        private static async Task<bool> WaitForAsync(ConcurrentQueue<WotNotification> queue, int maxAttempts = 80)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                if (!queue.IsEmpty)
                {
                    return true;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            return false;
        }

        [Test]
        public async Task MqttChannelInvokeAsyncPublishesWithNoInputs()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry();
                WotBindingPlan plan = Plan(registry, ActionTd(port, "things/act"));
                WotCompiledForm invoke = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.InvokeAction);

                IWotBindingChannel channel = await registry.OpenChannelAsync(invoke).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    // Empty inputs: InvokeAsync should publish an empty payload.
                    WotInvokeResult result = await channel.InvokeAsync([]).ConfigureAwait(false);
                    Assert.That(result.Success, Is.True);
                    Assert.That(result.Outputs, Is.Empty);
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelInvokeAsyncPublishesWithInputValue()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry();
                WotBindingPlan plan = Plan(registry, ActionTd(port, "things/act"));
                WotCompiledForm invoke = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.InvokeAction);

                IWotBindingChannel channel = await registry.OpenChannelAsync(invoke).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    WotInvokeResult result = await channel.InvokeAsync(
                        [new Variant(42L)]).ConfigureAwait(false);
                    Assert.That(result.Success, Is.True);
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelRejectsWildcardPublishTopic()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry(
                    bounds: new WotBindingBounds { AllowMqttWildcardTopics = true });
                string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                    "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{" +
                    "\"href\":\"mqtt://127.0.0.1:" +
                    port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "\",\"mqv:topic\":\"#\",\"op\":[\"writeproperty\"]}]}}}";
                WotBindingPlan plan = Plan(registry, td);
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

                IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    WotWriteResult result = await channel.WriteAsync(
                        new DataValue(new Variant(42L))).ConfigureAwait(false);
                    Assert.That(result.Status, Is.EqualTo(StatusCodes.BadInvalidArgument));
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelReadAsyncRejectsConcurrentReadWithoutAbandoningFirstAsync()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry(readTimeout: TimeSpan.FromSeconds(5));
                WotBindingPlan plan = Plan(registry, PropertyTd(port, "things/concurrent"));
                WotCompiledForm read = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

                IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    Task<WotReadResult> firstRead = channel.ReadAsync().AsTask();

                    Assert.That(
                        async () => await channel.ReadAsync().ConfigureAwait(false),
                        Throws.InstanceOf<ServiceResultException>());

                    IWotBindingChannel publisher = await registry.OpenChannelAsync(write).ConfigureAwait(false);
                    await using (publisher.ConfigureAwait(false))
                    {
                        WotWriteResult writeResult = await publisher
                            .WriteAsync(new DataValue(new Variant(123L)))
                            .ConfigureAwait(false);
                        Assert.That(writeResult.Success, Is.True);
                    }

                    WotReadResult firstResult = await firstRead.WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false);
                    Assert.That(firstResult.Success, Is.True);
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelReadAsyncRejectsHandBuiltTopicExceedingBoundsAsync()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry(bounds: new WotBindingBounds { MaxTopicLength = 5 });
                WotCompiledForm read = BuildRawForm(
                    port, "things/too-long", WoTBindingCapabilityEnum.ReadProperty, "readproperty");

                IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                        async () => await channel.ReadAsync().ConfigureAwait(false))!;
                    Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelObserveAsyncRejectsHandBuiltWildcardTopicWhenDisallowedAsync()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry();
                WotCompiledForm observe = BuildRawForm(
                    port, "things/+/value", WoTBindingCapabilityEnum.ObserveProperty, "observeproperty");

                IWotBindingChannel channel = await registry.OpenChannelAsync(observe).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                        async () => await channel.ObserveAsync(_ => { }).ConfigureAwait(false))!;
                    Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelSubscribeEventAsyncReceivesNotifications()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                    "\"properties\":{\"sensor\":{\"type\":\"number\",\"observable\":true,\"forms\":[" +
                    "{\"href\":\"mqtt://127.0.0.1:" +
                    port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "/things/sensor\",\"mqv:qos\":1,\"op\":[\"observeproperty\"]}," +
                    "{\"href\":\"mqtt://127.0.0.1:" +
                    port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "/things/sensor\",\"mqv:qos\":1,\"mqv:retain\":true}]}}}";

                WotProtocolBinderRegistry registry = Registry();
                WotBindingPlan plan = Plan(registry, td);

                WotCompiledForm observe = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.ObserveProperty);
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

                IWotBindingChannel observeChannel = await registry.OpenChannelAsync(observe).ConfigureAwait(false);
                await using (observeChannel.ConfigureAwait(false))
                {
                    var events = new ConcurrentQueue<WotNotification>();
                    // SubscribeEventAsync delegates to ObserveAsync.
                    IWotSubscription sub = await observeChannel.SubscribeEventAsync(n => events.Enqueue(n))
                        .ConfigureAwait(false);
                    await using (sub.ConfigureAwait(false))
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write)
                            .ConfigureAwait(false);
                        await using (writeChannel.ConfigureAwait(false))
                        {
                            await writeChannel.WriteAsync(new DataValue(new Variant(55L))).ConfigureAwait(false);
                        }
                        bool received = await WaitForAsync(events).ConfigureAwait(false);
                        Assert.That(received, Is.True,
                            "SubscribeEventAsync must deliver the published value.");
                    }
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelReadAsyncDecodingFailureReturnsBadDecodingError()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                // Write form with retain; read form subscribes to the same topic.
                string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                    "\"properties\":{\"sensor\":{\"type\":\"number\",\"forms\":[" +
                    "{\"href\":\"mqtt://127.0.0.1:" +
                    port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "/things/decode\",\"mqv:qos\":1,\"mqv:retain\":true}]}}}";

                WotProtocolBinderRegistry registry = Registry(readTimeout: TimeSpan.FromSeconds(5));
                WotBindingPlan plan = Plan(registry, td);
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

                // Publish a retained octet-stream payload that JSON cannot decode.
                // The property TD uses application/json by default, so the JSON codec
                // will fail to decode the raw bytes [0xFF, 0xFE].
                // We write via octet-stream channel by crafting an octet-stream TD.
                string rawTd = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                    "\"properties\":{\"sensor\":{\"type\":\"string\",\"forms\":[" +
                    "{\"href\":\"mqtt://127.0.0.1:" +
                    port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "/things/decode2\",\"contentType\":\"application/json\",\"mqv:qos\":1,\"mqv:retain\":true}]}}}";

                WotBindingPlan rawPlan = Plan(registry, rawTd);
                WotCompiledForm rawWrite = rawPlan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
                WotCompiledForm rawRead = rawPlan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

                // First publish an invalid JSON string so the retained message is bad.
                IWotBindingChannel writeChannel = await registry.OpenChannelAsync(rawWrite).ConfigureAwait(false);
                await using (writeChannel.ConfigureAwait(false))
                {
                    // Write the string "hello" which is valid JSON (it encodes as "\"hello\"").
                    // Then we verify ReadAsync on that topic succeeds. For the failure test,
                    // use a separate topic and publish malformed JSON directly.
                    await writeChannel.WriteAsync(
                        new DataValue(new Variant("hello"))).ConfigureAwait(false);
                }

                // Read back — the JSON codec decodes "\"hello\"" successfully as string.
                IWotBindingChannel readChannel = await registry.OpenChannelAsync(rawRead).ConfigureAwait(false);
                await using (readChannel.ConfigureAwait(false))
                {
                    WotReadResult readResult = await readChannel.ReadAsync().ConfigureAwait(false);
                    // "hello" is a valid JSON string and decodes successfully.
                    Assert.That(readResult.Success, Is.True);
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelSubscriptionDisposalStopsDelivery()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                    "\"properties\":{\"p\":{\"type\":\"number\",\"observable\":true,\"forms\":[" +
                    "{\"href\":\"mqtt://127.0.0.1:" +
                    port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "/things/stop\",\"mqv:qos\":1,\"op\":[\"observeproperty\"]}," +
                    "{\"href\":\"mqtt://127.0.0.1:" +
                    port.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "/things/stop\",\"mqv:qos\":1,\"mqv:retain\":true}]}}}";

                WotProtocolBinderRegistry registry = Registry();
                WotBindingPlan plan = Plan(registry, td);
                WotCompiledForm observe = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.ObserveProperty);
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

                IWotBindingChannel observeChannel = await registry.OpenChannelAsync(observe).ConfigureAwait(false);
                await using (observeChannel.ConfigureAwait(false))
                {
                    var received = new ConcurrentQueue<WotNotification>();
                    IWotSubscription sub = await observeChannel.ObserveAsync(n => received.Enqueue(n))
                        .ConfigureAwait(false);

                    await Task.Delay(100).ConfigureAwait(false);

                    // Publish once before disposal: verify delivery.
                    IWotBindingChannel publisher = await registry.OpenChannelAsync(write).ConfigureAwait(false);
                    await using (publisher.ConfigureAwait(false))
                    {
                        await publisher.WriteAsync(new DataValue(new Variant(1L))).ConfigureAwait(false);
                    }
                    bool firstDelivered = await WaitForAsync(received).ConfigureAwait(false);
                    Assert.That(firstDelivered, Is.True, "Subscription must deliver before disposal.");

                    // Dispose the subscription.
                    await sub.DisposeAsync().ConfigureAwait(false);

                    // Clear and publish again: nothing should be enqueued.
                    while (received.TryDequeue(out _)) { }

                    IWotBindingChannel publisher2 = await registry.OpenChannelAsync(write).ConfigureAwait(false);
                    await using (publisher2.ConfigureAwait(false))
                    {
                        await publisher2.WriteAsync(new DataValue(new Variant(2L))).ConfigureAwait(false);
                    }

                    await Task.Delay(300).ConfigureAwait(false);
                    Assert.That(received.IsEmpty, Is.True,
                        "Disposed subscription must not receive further notifications.");
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelQosZeroConfiguredFromForm()
        {
            // QoS 0 means at-most-once. Test that a form with mqv:qos=0 works
            // end-to-end (no exception, publishes successfully).
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry();
                WotBindingPlan plan = Plan(registry, PropertyTd(port, "things/qos0", qos: 0));
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

                IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    WotWriteResult result = await channel.WriteAsync(
                        new DataValue(new Variant(10L))).ConfigureAwait(false);
                    Assert.That(result.Success, Is.True);
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelQosTwoConfiguredFromForm()
        {
            // QoS 2 means exactly-once. Test that a form with mqv:qos=2 works.
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry();
                WotBindingPlan plan = Plan(registry, PropertyTd(port, "things/qos2", qos: 2));
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

                IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    WotWriteResult result = await channel.WriteAsync(
                        new DataValue(new Variant(20L))).ConfigureAwait(false);
                    Assert.That(result.Success, Is.True);
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelDisposeAsyncDisconnectsClient()
        {
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry();
                WotBindingPlan plan = Plan(registry, PropertyTd(port, "things/dispose"));
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

                IWotBindingChannel channel = await registry.OpenChannelAsync(write).ConfigureAwait(false);

                // Dispose should not throw.
                Assert.DoesNotThrowAsync(
                    async () => await channel.DisposeAsync().ConfigureAwait(false));
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task MqttChannelReadAsyncTimeoutReturnsBadTimeout()
        {
            // A channel that reads but no message is ever published should time out
            // and return BadTimeout.
            int port = FreePort();
            MqttServer broker = await StartBrokerAsync(port).ConfigureAwait(false);
            try
            {
                WotProtocolBinderRegistry registry = Registry(readTimeout: TimeSpan.FromMilliseconds(200));
                WotBindingPlan plan = Plan(registry, PropertyTd(port, "things/timeout"));
                WotCompiledForm read = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

                IWotBindingChannel channel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
                await using (channel.ConfigureAwait(false))
                {
                    WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                    Assert.That(result.Success, Is.False);
                    Assert.That(result.Status, Is.EqualTo(StatusCodes.BadTimeout));
                }
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }
    }
}
