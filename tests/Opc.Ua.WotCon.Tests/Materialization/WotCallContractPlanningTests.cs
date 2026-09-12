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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Export;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
#if NET8_0_OR_GREATER
using Opc.Ua.WotCon.Bindings.OpcUa;
#endif
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Server.Materialization;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotCallContractPlanningTests
    {
        [TestCase(/*lang=json,strict*/ """
            {"type":"object","uav:argumentLayout":"named","properties":{"Value":{"type":"integer"}}}
            """)]
        [TestCase(/*lang=json,strict*/ """
            {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Value","Value"],
             "properties":{"Value":{"type":"integer"}}}
            """)]
        [TestCase(/*lang=json,strict*/ """
            {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Value"],
             "required":true,"properties":{"Value":{"type":"integer"}}}
            """)]
        [TestCase(/*lang=json,strict*/ """
            {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Value"],
             "required":["Absent"],"properties":{"Value":{"type":"integer"}}}
            """)]
        [TestCase(/*lang=json,strict*/ """
            {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Value"],
             "required":["Value","Value"],"properties":{"Value":{"type":"integer"}}}
            """)]
        [TestCase(/*lang=json,strict*/ """
            {"type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Value"],"properties":{"Value":7}}
            """)]
        public void MalformedNamedCallLayoutsAreRejectedByThePublicCompiler(string input)
        {
            byte[] content = Document(input, null);
            var request = WotBindingPlanRequest.FromDocument(
                "invalid-call", WoTDocumentKindEnum.ThingDescription, content);

            WotBindingCompilation compilation = new OpcUaBindingPlanner().Compile(
                request.Forms[0], request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));

            Assert.That(compilation.HasErrors, Is.True);
            Assert.That(compilation.Entries, Is.Empty);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        [Test]
        public void ExplicitPublicRequestRetainsCapturedScopedPayloadFacts()
        {
            byte[] content = Document(s_input, s_output);
            var request = new WotBindingPlanRequest(
                "explicit-call", WoTDocumentKindEnum.ThingDescription, WotFormExtractor.Extract(content));

            WotBindingCompilation compilation = new OpcUaBindingPlanner().Compile(
                request.Forms[0], request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));

            Assert.That(compilation.HasErrors, Is.False);
            WotCompiledForm form = compilation.Entries.Single();
            Assert.That(form.Payload.Schema, Is.Not.Null);
            Assert.That(form.Payload.Schema!.TryGetTypeBinding(
                "/input/properties/Number", out WotPayloadTypeBinding? number), Is.True);
            Assert.That(number!.DataTypeId, Is.EqualTo(new ExpandedNodeId(5)));
            Assert.That(number.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt16));
            Assert.That(form.Payload.InputLayout!.FieldOrder, Is.EqualTo(s_inputOrder));
            Assert.That(form.Payload.OutputLayout!.FieldOrder, Is.EqualTo(s_outputOrder));
            Assert.That(form.Addressing.Metadata["callObjectId"], Is.EqualTo("nsu=urn:p15:source;s=Owner"));
        }

        [TestCase("Acknowledge", "missing-comment")]
        [TestCase("Confirm", "wrong-comment-type")]
        [TestCase("AddComment", "wrong-event-id-type")]
        [TestCase("Acknowledge", "single")]
        [TestCase("Confirm", "reordered")]
        [TestCase("AddComment", "outputs")]
        [TestCase("Enable", "inputs")]
        [TestCase("Disable", "outputs")]
        public void InvalidConditionSignaturesNeverBecomeExecutable(string action, string invalid)
        {
            string? input = action is "Enable" or "Disable" ? null : /*lang=json,strict*/ """
                {
                  "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["EventId","Comment"],
                  "required":["EventId"],
                  "properties":{
                    "EventId":{"type":"string","contentEncoding":"base64","uav:dataTypeId":"i=15"},
                    "Comment":{"type":"string","uav:dataTypeId":"i=21"}
                  }
                }
                """;
            string? output = null;
            switch (invalid)
            {
                case "missing-comment":
                    input = /*lang=json,strict*/ """
                        {
                          "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["EventId"],
                          "required":["EventId"],
                          "properties":{"EventId":{"type":"string","contentEncoding":"base64","uav:dataTypeId":"i=15"}}
                        }
                        """;
                    break;
                case "wrong-comment-type":
                    input = input!.Replace("\"i=21\"", "\"i=12\"", StringComparison.Ordinal);
                    break;
                case "wrong-event-id-type":
                    input = input!.Replace("\"i=15\"", "\"i=12\"", StringComparison.Ordinal);
                    break;
                case "single":
                    input = input!.Replace("\"named\"", "\"single\"", StringComparison.Ordinal);
                    break;
                case "reordered":
                    input = input!.Replace("[\"EventId\",\"Comment\"]", "[\"Comment\",\"EventId\"]",
                        StringComparison.Ordinal);
                    break;
                case "inputs":
                    input = /*lang=json,strict*/ """{"type":"integer","uav:dataTypeId":"i=6"}""";
                    break;
                case "outputs":
                    output = /*lang=json,strict*/ """{"type":"integer","uav:dataTypeId":"i=6"}""";
                    break;
            }
            var request = WotBindingPlanRequest.FromDocument(
                "invalid-condition", WoTDocumentKindEnum.ThingDescription, Document(input, output, action));

            WotBindingCompilation compilation = new OpcUaBindingPlanner().Compile(
                request.Forms[0], request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));

            Assert.That(compilation.HasErrors, Is.True);
            Assert.That(compilation.Entries, Is.Empty);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        [TestCase("input-count")]
        [TestCase("input-type")]
        [TestCase("input-rank")]
        [TestCase("input-name")]
        [TestCase("output-count")]
        [TestCase("output-type")]
        [TestCase("output-rank")]
        [TestCase("output-name")]
        public void ProjectedSignatureMismatchFailsBeforeOpeningTheSelectedSource(string mismatch)
        {
            var h = new WotProjectionBindingRuntimeTestHarness();
            var numberInput = new Argument
            {
                Name = mismatch == "input-name" ? "Different" : "Number",
                DataType = mismatch == "input-type" ? Ua.DataTypeIds.Int32 : Ua.DataTypeIds.UInt16,
                ValueRank = mismatch == "input-rank" ? ValueRanks.OneDimension : ValueRanks.Scalar
            };
            var referenceInput = new Argument
            {
                Name = "Reference",
                DataType = Ua.DataTypeIds.NodeId,
                ValueRank = ValueRanks.Scalar
            };
            var referenceOutput = new Argument
            {
                Name = "Reference",
                DataType = Ua.DataTypeIds.NodeId,
                ValueRank = ValueRanks.Scalar
            };
            var numberOutput = new Argument
            {
                Name = mismatch == "output-name" ? "Different" : "Number",
                DataType = mismatch == "output-type" ? Ua.DataTypeIds.Int32 : Ua.DataTypeIds.UInt16,
                ValueRank = mismatch == "output-rank" ? ValueRanks.OneDimension : ValueRanks.Scalar
            };
            MethodState method = h.AddMethod("Run",
                mismatch == "input-count" ? [numberInput] : [numberInput, referenceInput],
                mismatch == "output-count" ? [referenceOutput] : [referenceOutput, numberOutput]);
            var planner = new OpcUaBindingPlanner();
            var executor = new Mock<IWotBindingExecutor>();
            executor.SetupGet(value => value.Identity).Returns(planner.Identity);
            executor.Setup(value => value.CanExecute(It.IsAny<WotCompiledForm>())).Returns(true);
            var registry = new WotProtocolBinderRegistry([planner], [executor.Object]);
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "mismatched-call", WoTDocumentKindEnum.ThingDescription, Document(s_input, s_output)))
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(
                        Bindings.WotAffordanceKind.Action, "run", "/actions/run",
                        method.NodeId.ToString(), h.Root.NodeId.ToString())
                ]);
            var factory = new WotProjectionBindingRuntimeFactory(registry);

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await using IAsyncDisposable? runtime =
                    await factory.CreateAsync(h.Builder, [plan]).ConfigureAwait(false);
            });

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            executor.Verify(value => value.ActivateAsync(
                It.IsAny<WotCompiledForm>(), It.IsAny<WotExecutorContext>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
#if NET8_0_OR_GREATER
        [TestCase(false, true)]
        [TestCase(true, true)]
#endif
        [Category("Integration")]
        [NonParallelizable]
        public async Task ConvertedCallContractsReachDirectAndDiProjectedInvocations(
            bool dependencyInjection, bool native)
        {
            byte[] content = Document(s_input, s_output);
            ArrayOf<WotProjectedAffordance> affordances;
            UANodeSet nodes;
            ExpandedNodeId root;
            using (var document = WotDocument.Parse(content))
            {
                WotConversionResult<UANodeSet> converted = WotNodeSetConverter.ToNodeSetResult(document);
                Assert.That(converted.HasErrors, Is.False, string.Join("; ", converted.Diagnostics));
                nodes = converted.Value!;
                Assert.That(nodes.NamespaceUris, Is.Not.Null.And.Not.Empty);
                root = WotNodeSetConverter.TrySelectProjectionRoot(nodes);
                affordances = WotNodeSetConverter.ResolveAffordanceNodes(document, nodes, root)
                    .ConvertAll(WotProjectedAffordance.FromConverted);
            }
            ushort localSourceIndex = 0;
            var sourceContext = ServiceMessageContext.CreateEmpty(TelemetryExtensions.InternalOnly__TelemetryHook());
            ushort sourceIndex = sourceContext.NamespaceUris.GetIndexOrAppend("urn:p15:source");
            var planner = new OpcUaBindingPlanner();
            int calls = 0;
            int activations = 0;
            var contextual = new Mock<IWotContextualBindingChannel>();
            contextual.Setup(value => value.InvokeAsync(
                It.IsAny<WotInvokeRequest>(), It.IsAny<CancellationToken>()))
                .Returns<WotInvokeRequest, CancellationToken>((request, _) =>
                {
                    calls++;
                    Assert.That(request.Inputs.Count, Is.EqualTo(2));
                    Assert.That(request.Inputs[0], Is.EqualTo(new Variant((ushort)7)));
                    Assert.That(request.Inputs[1].TryGetValue(out NodeId reference), Is.True);
                    Assert.That(NodeId.ToExpandedNodeId(reference, request.Context.NamespaceUris),
                        Is.EqualTo(new ExpandedNodeId("Input", "urn:p15:source")));
                    return new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good,
                    [
                        new DataValue(new Variant(new NodeId("Output", sourceIndex))),
                        new DataValue(new Variant((ushort)8))
                    ]).WithContext(sourceContext));
                });
            IWotBindingExecutor executor;
            if (native)
            {
#if NET8_0_OR_GREATER
                var session = new Mock<ISession>();
                session.SetupGet(value => value.NamespaceUris).Returns(sourceContext.NamespaceUris);
                session.SetupGet(value => value.ServerUris).Returns(sourceContext.ServerUris);
                session.SetupGet(value => value.Factory).Returns(sourceContext.Factory);
                session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, _) =>
                    {
                        calls++;
                        Assert.That(requests.Count, Is.EqualTo(1));
                        Assert.That(requests[0].ObjectId, Is.EqualTo(new NodeId("Owner", sourceIndex)));
                        Assert.That(requests[0].MethodId, Is.EqualTo(new NodeId("Run", sourceIndex)));
                        Assert.That(requests[0].InputArguments.Count, Is.EqualTo(2));
                        Assert.That(requests[0].InputArguments[0], Is.EqualTo(new Variant((ushort)7)));
                        Assert.That(requests[0].InputArguments[1],
                            Is.EqualTo(new Variant(new NodeId("Input", sourceIndex))));
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new CallMethodResult
                                {
                                    StatusCode = StatusCodes.Good,
                                    OutputArguments =
                                    [
                                        new Variant(new NodeId("Output", sourceIndex)),
                                        new Variant((ushort)8)
                                    ]
                                }
                            ]
                        });
                    });
                executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
                {
                    SessionFactory = (endpoint, _) =>
                    {
                        activations++;
                        Assert.That(new Uri(endpoint), Is.EqualTo(new Uri("opc.tcp://p15-source.invalid:4840")));
                        return new ValueTask<ISession>(session.Object);
                    },
                    DisposeSession = false
                });
#else
                throw new NotSupportedException("Native execution requires net8.0 or later.");
#endif
            }
            else
            {
                var mock = new Mock<IWotBindingExecutor>();
                mock.SetupGet(value => value.Identity).Returns(planner.Identity);
                mock.Setup(value => value.CanExecute(It.IsAny<WotCompiledForm>())).Returns(true);
                mock.Setup(value => value.ActivateAsync(
                    It.IsAny<WotCompiledForm>(), It.IsAny<WotExecutorContext>(), It.IsAny<CancellationToken>()))
                    .Returns<WotCompiledForm, WotExecutorContext, CancellationToken>((form, context, _) =>
                    {
                        activations++;
                        Assert.That(form.Endpoint.Host, Is.EqualTo("p15-source.invalid"));
                        Assert.That(context.MessageContext.NamespaceUris.GetString(localSourceIndex),
                            Is.EqualTo("urn:p15:source"));
                        contextual.SetupGet(value => value.Form).Returns(form);
                        return new ValueTask<IWotBindingChannel>(contextual.Object);
                    });
                executor = mock.Object;
            }
            var services = new ServiceCollection();
            services.AddSingleton<IWotProtocolBinder>(planner);
            services.AddSingleton(executor);
            services.EnsureWotBinderRegistry();
            using ServiceProvider provider = services.BuildServiceProvider();
            WotProtocolBinderRegistry registry = dependencyInjection
                ? provider.GetRequiredService<WotProtocolBinderRegistry>()
                : new WotProtocolBinderRegistry([planner], [executor]);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "converted-call", WoTDocumentKindEnum.ThingDescription, content)
                .WithProjectedAffordances(affordances)
                .WithProjectionRoot(root)
                .WithDeclarationContext(false);
            WotBindingPlan plan = registry.Prepare(request);
            Assert.That(plan.CompiledForms, Has.Length.EqualTo(2));
            Assert.That(plan.CompiledForms[0].Payload.Schema, Is.SameAs(affordances[0].PayloadSchema));
            Assert.That(plan.ProjectedAffordances.Count, Is.EqualTo(1));
            WotProjectedAffordance action = plan.ProjectedAffordances[0];
            string directory = Path.Combine(Path.GetTempPath(), "w-p15-" + Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            ReferenceServer? server = null;
            try
            {
                server = await fixture.StartAsync(directory).ConfigureAwait(false);
                localSourceIndex = server.CurrentInstance.NamespaceUris.GetIndexOrAppend("urn:p15:source");
                Assert.That(localSourceIndex, Is.Not.EqualTo(sourceIndex));
                using var output = new MemoryStream();
                nodes.Write(output);
                var host = new LifecycleWotProjectionHost(
                    server.NodeManagerLifecycle, new WotProjectionBindingRuntimeFactory(registry));
                WotProjectionHandle handle = await host.AddAsync(new WotProjectionDocument(
                    "converted-call",
                    [new WotProjectionSource("converted-call", [.. nodes.NamespaceUris!], output.ToArray())],
                    [plan])).ConfigureAwait(false);
                try
                {
                    Assert.That(activations, Is.Zero);
                    using var client = new ClientFixture(NUnitTelemetryContext.Create());
                    await client.LoadClientConfigurationAsync(directory).ConfigureAwait(false);
                    using ISession clientSession = await client.ConnectAsync(
                        new UriBuilder(Utils.UriSchemeOpcTcp, "localhost", fixture.Port).Uri, SecurityPolicies.None)
                        .ConfigureAwait(false);
                    try
                    {
                        ushort clientIndex = clientSession.NamespaceUris.GetIndexOrAppend("urn:p15:source");
                        CallResponse response = await clientSession.CallAsync(null,
                        [
                            new CallMethodRequest
                            {
                                ObjectId = ExpandedNodeId.Parse(action.OwnerNodeId, clientSession.NamespaceUris),
                                MethodId = ExpandedNodeId.Parse(action.NodeId, clientSession.NamespaceUris),
                                InputArguments =
                                [
                                    new Variant((ushort)7),
                                    new Variant(new NodeId("Input", clientIndex))
                                ]
                            }
                        ], CancellationToken.None).ConfigureAwait(false);

                        Assert.That(response.Results.Count, Is.EqualTo(1));
                        CallMethodResult result = response.Results[0];
                        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(calls, Is.EqualTo(1));
                        Assert.That(activations, Is.EqualTo(1));
                        Assert.That(result.OutputArguments.Count, Is.EqualTo(2));
                        Assert.That(result.OutputArguments[0],
                            Is.EqualTo(new Variant(new NodeId("Output", clientIndex))));
                        Assert.That(result.OutputArguments[1], Is.EqualTo(new Variant((ushort)8)));
                    }
                    finally
                    {
                        await clientSession.CloseAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await host.RemoveAsync(handle).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static byte[] Document(string? input, string? output, string? conditionAction = null)
        {
            string condition = conditionAction is null
                ? string.Empty : "\"uav:conditionAction\":\"" + conditionAction + "\",";
            return Encoding.UTF8.GetBytes($$"""
                {
                  "@type":"uav:object","title":"Call owner","uav:id":"nsu=urn:p15:local;s=Owner",
                  "actions":{"run":{
                    "@type":"uav:method","uav:id":"nsu=urn:p15:local;s=Run",
                    {{condition}}
                    {{(input is null ? string.Empty : "\"input\":" + input + ",")}}
                    {{(output is null ? string.Empty : "\"output\":" + output + ",")}}
                    "forms":[
                      {
                        "href":"opc.tcp://p15-source.invalid:4840","op":"invokeaction",
                        "uav:id":"nsu=urn:p15:source;s=Run","uav:callObjectId":"nsu=urn:p15:source;s=Owner",
                        "contentType":"application/octet-stream"
                      },
                      {
                        "href":"opc.tcp://not-selected.invalid:4840","op":"invokeaction",
                        "uav:id":"nsu=urn:p15:source;s=Other","uav:callObjectId":"nsu=urn:p15:source;s=OtherOwner",
                        "contentType":"application/octet-stream"
                      }
                    ]
                  }
                  }
                }
                """);
        }

        private const string s_input = /*lang=json,strict*/ """
            {
              "@context":{"arg":"http://opcfoundation.org/UA/"},
              "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Number","Reference"],
              "required":["Number","Reference"],
              "properties":{
                "Reference":{"type":"string","uav:dataTypeName":"arg:NodeId"},
                "Number":{"type":"integer","uav:dataTypeName":"arg:UInt16"}
              }
            }
            """;

        private const string s_output = /*lang=json,strict*/ """
            {
              "@context":{"arg":"http://opcfoundation.org/UA/"},
              "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Reference","Number"],
              "properties":{
                "Number":{"type":"integer","uav:dataTypeName":"arg:UInt16"},
                "Reference":{"type":"string","uav:dataTypeName":"arg:NodeId"}
              }
            }
            """;

        private static readonly ArrayOf<string> s_inputOrder = ["Number", "Reference"];
        private static readonly ArrayOf<string> s_outputOrder = ["Reference", "Number"];
    }
}
