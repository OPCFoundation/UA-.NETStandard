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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MQTTnet.Server;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Mqtt;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>End-to-end tests for the MQTT executor against an ephemeral in-process broker.</summary>
    [TestFixture]
    public sealed class MqttWotExecutorTests
    {
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static WotProtocolBinderRegistry Registry()
            => new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new MqttBindingPlanner() },
                new IWotBindingExecutor[] { new MqttWotBindingExecutor(
                    new MqttWotBindingOptions { ReadTimeout = TimeSpan.FromSeconds(5) }) });

        private static WotBindingPlan Plan(WotProtocolBinderRegistry registry, string td)
            => registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));

        [Test]
        public async Task Mqtt_PublishSubscribeObserve_EndToEnd()
        {
            int port = FreePort();
            MqttServerOptions serverOptions = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(port)
                .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
                .Build();
            MqttServer broker = new MqttServerFactory().CreateMqttServer(serverOptions);
            await broker.StartAsync();
            try
            {
                string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                    "\"properties\":{" +
                    "\"temp\":{\"type\":\"number\",\"forms\":[{\"href\":\"mqtt://127.0.0.1:" + port +
                    "/things/temp\",\"mqv:qos\":1,\"mqv:retain\":true}]}," +
                    "\"watch\":{\"type\":\"number\",\"observable\":true,\"forms\":[{\"href\":\"mqtt://127.0.0.1:" +
                    port + "/things/temp\",\"mqv:qos\":1,\"op\":[\"observeproperty\"]}]}}}";

                WotProtocolBinderRegistry registry = Registry();
                WotBindingPlan plan = Plan(registry, td);
                WotCompiledForm write = plan.CompiledForms.First(
                    f => f.AffordanceName == "temp" && f.Operation == WoTBindingCapabilityEnum.WriteProperty);
                WotCompiledForm read = plan.CompiledForms.First(
                    f => f.AffordanceName == "temp" && f.Operation == WoTBindingCapabilityEnum.ReadProperty);
                WotCompiledForm observe = plan.CompiledForms.First(
                    f => f.Operation == WoTBindingCapabilityEnum.ObserveProperty);

                // Publish a retained value, then read it back.
                IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write);
                await using (writeChannel.ConfigureAwait(false))
                {
                    Assert.That((await writeChannel.WriteAsync(new DataValue(new Variant(42L)))).Success, Is.True);
                }

                IWotBindingChannel readChannel = await registry.OpenChannelAsync(read);
                await using (readChannel.ConfigureAwait(false))
                {
                    WotReadResult result = await readChannel.ReadAsync();
                    Assert.That(result.Success, Is.True);
                    Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(42L));
                }

                // Observe, then publish a new value and expect a notification.
                var received = new ConcurrentQueue<long>();
                IWotBindingChannel observeChannel = await registry.OpenChannelAsync(observe);
                await using (observeChannel.ConfigureAwait(false))
                {
                    IWotSubscription subscription = await observeChannel.ObserveAsync(n =>
                    {
                        if (n.Value.WrappedValue.AsBoxedObject() is long value)
                        {
                            received.Enqueue(value);
                        }
                    });
                    await using (subscription.ConfigureAwait(false))
                    {
                        await Task.Delay(200);
                        IWotBindingChannel publisher = await registry.OpenChannelAsync(write);
                        await using (publisher.ConfigureAwait(false))
                        {
                            await publisher.WriteAsync(new DataValue(new Variant(77L)));
                        }
                        Assert.That(await WaitForAsync(received, 77), Is.True,
                            "The MQTT observe channel must deliver the published change.");
                    }
                }
            }
            finally
            {
                await broker.StopAsync();
                broker.Dispose();
            }
        }

        private static async Task<bool> WaitForAsync(ConcurrentQueue<long> queue, long expected)
        {
            for (int i = 0; i < 60; i++)
            {
                if (queue.Contains(expected))
                {
                    return true;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
            return false;
        }
    }
}
