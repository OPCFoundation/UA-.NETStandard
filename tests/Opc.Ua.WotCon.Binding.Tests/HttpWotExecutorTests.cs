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
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Http;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.Binding.Tests.Support;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>End-to-end tests for the HTTP executor against an in-process HTTP server.</summary>
    [TestFixture]
    public sealed class HttpWotExecutorTests
    {
        private static WotProtocolBinderRegistry Registry(HttpWotBindingOptions? options = null)
            => new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new HttpBindingPlanner() },
                new IWotBindingExecutor[] { new HttpWotBindingExecutor(options ?? new HttpWotBindingOptions
                {
                    ClientFactory = () => new HttpClient()
                }) });

        private static WotBindingPlan Plan(WotProtocolBinderRegistry registry, string td)
            => registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));

        [Test]
        public async Task Http_ReadWriteAction_EndToEnd()
        {
            var store = new ConcurrentDictionary<string, string>();
            store["/prop"] = "10";
            using var server = new TestHttpServer((method, path, body) =>
            {
                if (path == "/prop" && method == "GET")
                {
                    return TestHttpResponse.Json(200, store.GetValueOrDefault("/prop", "0"));
                }
                if (path == "/prop" && method == "PUT")
                {
                    store["/prop"] = Encoding.UTF8.GetString(body);
                    return new TestHttpResponse(204, "text/plain", Array.Empty<byte>());
                }
                if (path == "/action" && method == "POST")
                {
                    return TestHttpResponse.Json(200, "\"done\"");
                }
                return TestHttpResponse.Json(404, "\"missing\"");
            });

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"temp\":{\"type\":\"number\",\"forms\":[{\"href\":\"" + server.BaseUrl + "/prop\"}]}}," +
                "\"actions\":{\"act\":{\"forms\":[{\"href\":\"" + server.BaseUrl + "/action\"}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);

            WotCompiledForm read = plan.CompiledForms.First(
                f => f.AffordanceName == "temp" && f.Operation == WoTBindingCapabilityEnum.ReadProperty);
            WotCompiledForm write = plan.CompiledForms.First(
                f => f.AffordanceName == "temp" && f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            WotCompiledForm invoke = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel readChannel = await registry.OpenChannelAsync(read);
            await using (readChannel.ConfigureAwait(false))
            {
                WotReadResult result = await readChannel.ReadAsync();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(10L));
            }

            IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write);
            await using (writeChannel.ConfigureAwait(false))
            {
                WotWriteResult result = await writeChannel.WriteAsync(new DataValue(new Variant(42L)));
                Assert.That(result.Success, Is.True);
            }

            IWotBindingChannel reread = await registry.OpenChannelAsync(read);
            await using (reread.ConfigureAwait(false))
            {
                WotReadResult result = await reread.ReadAsync();
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(42L));
            }

            IWotBindingChannel actionChannel = await registry.OpenChannelAsync(invoke);
            await using (actionChannel.ConfigureAwait(false))
            {
                WotInvokeResult result = await actionChannel.InvokeAsync(Array.Empty<Variant>());
                Assert.That(result.Success, Is.True);
                Assert.That(result.Outputs.Count, Is.EqualTo(1));
                Assert.That(result.Outputs[0].WrappedValue.AsBoxedObject(), Is.EqualTo("done"));
            }
        }

        [Test]
        public async Task Http_Observe_DeliversValueChanges()
        {
            var store = new ConcurrentDictionary<string, string>();
            store["/prop"] = "1";
            using var server = new TestHttpServer((method, path, body) =>
                TestHttpResponse.Json(200, store.GetValueOrDefault("/prop", "0")));

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"w\":{\"type\":\"number\",\"observable\":true,\"forms\":[{\"href\":\"" +
                server.BaseUrl + "/prop\",\"op\":[\"observeproperty\"]}]}}}";

            WotProtocolBinderRegistry registry = Registry(new HttpWotBindingOptions
            {
                ClientFactory = () => new HttpClient(),
                ObserveInterval = TimeSpan.FromMilliseconds(100)
            });
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm observe = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.ObserveProperty);

            var received = new ConcurrentQueue<long>();
            IWotBindingChannel channel = await registry.OpenChannelAsync(observe);
            await using (channel.ConfigureAwait(false))
            {
                IWotSubscription subscription = await channel.ObserveAsync(n =>
                {
                    if (n.Value.WrappedValue.AsBoxedObject() is long value)
                    {
                        received.Enqueue(value);
                    }
                });
                await using (subscription.ConfigureAwait(false))
                {
                    store["/prop"] = "99";
                    Assert.That(await WaitForAsync(received, 99), Is.True, "The observe channel must deliver the change.");
                }
            }
        }

        [Test]
        public async Task Http_NotFound_MapsToBadNodeIdUnknown()
        {
            using var server = new TestHttpServer((method, path, body) => TestHttpResponse.Json(404, "\"no\""));
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"temp\":{\"type\":\"number\",\"forms\":[{\"href\":\"" + server.BaseUrl + "/x\"}]}}}";

            WotProtocolBinderRegistry registry = Registry();
            WotBindingPlan plan = Plan(registry, td);
            WotCompiledForm read = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel channel = await registry.OpenChannelAsync(read);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
            }
        }

        private static async Task<bool> WaitForAsync(ConcurrentQueue<long> queue, long expected)
        {
            for (int i = 0; i < 50; i++)
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
