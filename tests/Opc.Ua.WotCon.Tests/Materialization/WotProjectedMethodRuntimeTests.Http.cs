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

#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotProjectedMethodRuntimeTests
    {
        [TestCase(HttpStatusCode.OK, false)]
        [TestCase(HttpStatusCode.OK, true)]
        [TestCase(HttpStatusCode.ServiceUnavailable, false)]
        public async Task ProjectedHttpCallUsesCompleteLayoutsAndNeverRetriesAnotherForm(
            HttpStatusCode status, bool malformed)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            MethodState method = h.AddMethod("Limits",
                [
                    new Argument { Name = "Minimum", DataType = Ua.DataTypeIds.Int64, ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "Maximum", DataType = Ua.DataTypeIds.Int64, ValueRank = ValueRanks.Scalar }
                ],
                [
                    new Argument { Name = "Maximum", DataType = Ua.DataTypeIds.Int64, ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "Minimum", DataType = Ua.DataTypeIds.Int64, ValueRank = ValueRanks.Scalar }
                ]);
            string? sent = null;
            Uri? target = null;
            int sends = 0;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, token) =>
                {
                    Interlocked.Increment(ref sends);
                    target = request.RequestUri;
                    sent = await request.Content!.ReadAsStringAsync(token).ConfigureAwait(false);
                    return new HttpResponseMessage(status)
                    {
                        Content = new StringContent(
                            malformed ? """{"Maximum":"wrong","Minimum":-8}""" : """{"Minimum":-8,"Maximum":43}""",
                            Encoding.UTF8, "application/json")
                    };
                });
            using var client = new HttpClient(handler.Object);
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(new HttpWotBindingOptions
                {
                    ClientFactory = () => client,
                    CallerClientHandlesRedirectSafety = true
                })]);
            const string td = """
                {
                  "title":"Limits",
                  "actions":{"limits":{
                    "input":{
                      "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Minimum","Maximum"],
                      "properties":{"Maximum":{"type":"integer"},"Minimum":{"type":"integer"}}
                    },
                    "output":{
                      "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Maximum","Minimum"],
                      "properties":{"Minimum":{"type":"integer"},"Maximum":{"type":"integer"}}
                    },
                    "forms":[
                      {"href":"https://methods.example/first","op":"invokeaction"},
                      {"href":"https://methods.example/alternative","op":"invokeaction"}
                    ]
                  }}
                }
                """;
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "limits", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)))
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(
                        WotAffordanceKind.Action, "limits", "/actions/limits",
                        method.NodeId.ToString(), h.Root.NodeId.ToString())
                ]);
            var factory = new WotProjectionBindingRuntimeFactory(registry);
            await using IAsyncDisposable runtime = await factory.CreateAsync(h.Builder, [plan]).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The projected method must be wired.");
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [new Variant(-7L), new Variant(42L)], [], outputs)
                .ConfigureAwait(false);

            Assert.That(sends, Is.EqualTo(1));
            Assert.That(target, Is.EqualTo(new Uri("https://methods.example/first")));
            Assert.That(sent, Is.EqualTo("""{"Minimum":-7,"Maximum":42}"""));
            if (status == HttpStatusCode.OK && !malformed)
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(outputs, Has.Count.EqualTo(2));
                Assert.That(outputs[0], Is.EqualTo(new Variant(43L)));
                Assert.That(outputs[1], Is.EqualTo(new Variant(-8L)));
            }
            else
            {
                Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
                if (malformed)
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                }
                Assert.That(outputs, Is.Empty);
            }
        }

        [Test]
        public async Task ProjectedHttpCallCarriesTheLocalInputContextAndRebasesReturnedNodeIds()
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            ushort sourceIndex = h.Builder.Context.NamespaceUris.GetIndexOrAppend("urn:projected-input");
            MethodState method = h.AddMethod("Reference",
                [new Argument { Name = "Input", DataType = Ua.DataTypeIds.NodeId, ValueRank = ValueRanks.Scalar }],
                [new Argument { Name = "Output", DataType = Ua.DataTypeIds.NodeId, ValueRank = ValueRanks.Scalar }]);
            string? body = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>(async (request, token) =>
                {
                    body = await request.Content!.ReadAsStringAsync(token).ConfigureAwait(false);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "\"nsu=urn:projected-output;s=Result\"", Encoding.UTF8, "application/json")
                    };
                });
            using var client = new HttpClient(handler.Object);
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(new HttpWotBindingOptions
                {
                    ClientFactory = () => client,
                    CallerClientHandlesRedirectSafety = true
                })]);
            const string td = """
                {
                  "title":"Reference",
                  "actions":{"reference":{
                    "input":{"type":"string","uav:dataTypeId":"i=17"},
                    "output":{"type":"string","uav:dataTypeId":"i=17"},
                    "forms":[{"href":"https://methods.example/reference","op":"invokeaction"}]
                  }}
                }
                """;
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "reference", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)))
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(
                        WotAffordanceKind.Action, "reference", "/actions/reference",
                        method.NodeId.ToString(), h.Root.NodeId.ToString())
                ]);
            var factory = new WotProjectionBindingRuntimeFactory(registry);
            await using IAsyncDisposable runtime = await factory.CreateAsync(h.Builder, [plan]).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The projected method must be wired.");
            var outputs = new List<Variant>();

            ServiceResult result = await method.CallAsync(
                h.Builder.Context, h.Root.NodeId, [new Variant(new NodeId("Input", sourceIndex))], [], outputs)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(body, Is.EqualTo("\"nsu=urn:projected-input;s=Input\""));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out NodeId output), Is.True);
            Assert.That(NodeId.ToExpandedNodeId(output, h.Builder.Context.NamespaceUris),
                Is.EqualTo(new ExpandedNodeId("Result", "urn:projected-output")));
        }
    }
}
#endif
