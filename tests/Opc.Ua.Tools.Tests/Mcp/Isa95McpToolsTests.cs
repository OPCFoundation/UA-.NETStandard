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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Pins independent role dispatch, named-session resolution and full-width model statuses.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class Isa95McpToolsTests
    {
        [Test]
        public async Task CatalogContainsExactlyTwentyExplicitIsa95Tools()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaIsa95Tools(McpToolProfile.Isa95);
            await using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool[] tools = provider.GetServices<McpServerTool>().ToArray();
            McpServerTool[] family = tools.Where(tool =>
                tool.ProtocolTool.Name.StartsWith("isa95_", StringComparison.Ordinal)).ToArray();

            Assert.That(family, Has.Length.EqualTo(20));
            Assert.That(tools.Select(tool => tool.ProtocolTool.Name).Distinct().Count(), Is.EqualTo(tools.Length));
            Assert.That(tools.Count(tool => tool.ProtocolTool.Name == "Connect"), Is.EqualTo(1));
            foreach (McpServerTool tool in family)
            {
                string name = tool.ProtocolTool.Name;
                bool readOnly = name.Contains("list_", StringComparison.Ordinal) ||
                    name.Contains("read_", StringComparison.Ordinal) ||
                    name.Contains("discover_", StringComparison.Ordinal) ||
                    name.Contains("query_", StringComparison.Ordinal) ||
                    name.Contains("observe_", StringComparison.Ordinal);
                Assert.That(tool.ProtocolTool.Annotations?.ReadOnlyHint, Is.EqualTo(readOnly), name);
                Assert.That(tool.ProtocolTool.Annotations?.DestructiveHint, Is.EqualTo(!readOnly), name);
                Assert.That(tool.ProtocolTool.InputSchema.GetProperty("properties")
                    .TryGetProperty("sessionName", out _),
                    Is.True, name);
            }
        }

        [TestCase(McpToolProfile.Machinery)]
        [TestCase(McpToolProfile.Core)]
        public async Task UnselectedIsa95ProfileContributesNothing(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaIsa95Tools(profile);
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        [TestCase("Store", V2.Methods.ISA95JobOrderReceiverObjectType_Store)]
        [TestCase("StoreAndStart", V2.Methods.ISA95JobOrderReceiverObjectType_StoreAndStart)]
        [TestCase("Update", V2.Methods.ISA95JobOrderReceiverObjectType_Update)]
        [TestCase("Start", V2.Methods.ISA95JobOrderReceiverObjectType_Start)]
        [TestCase("Stop", V2.Methods.ISA95JobOrderReceiverObjectType_Stop)]
        [TestCase("Pause", V2.Methods.ISA95JobOrderReceiverObjectType_Pause)]
        [TestCase("Resume", V2.Methods.ISA95JobOrderReceiverObjectType_Resume)]
        [TestCase("Abort", V2.Methods.ISA95JobOrderReceiverObjectType_Abort)]
        [TestCase("RevokeStart", V2.Methods.ISA95JobOrderReceiverObjectType_RevokeStart)]
        [TestCase("Cancel", V2.Methods.ISA95JobOrderReceiverObjectType_Cancel)]
        [TestCase("Clear", V2.Methods.ISA95JobOrderReceiverObjectType_Clear)]
        public async Task EachV2VerbCallsOnlyItsActualReceiverAndGeneratedMethodAsync(string operation, uint methodId)
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> selected = JobMcpTestSession.Create(manager.Telemetry);
            Mock<ISession> other = JobMcpTestSession.Create(manager.Telemetry);
            var calls = new List<CallMethodRequest>();
            JobMcpTestSession.SetupCall(selected, calls, [Variant.From(1UL)]);
            await manager.RegisterExistingSessionAsync("other", other.Object, "Anonymous").ConfigureAwait(false);
            await manager.RegisterExistingSessionAsync("selected", selected.Object, "Anonymous").ConfigureAwait(false);
            var tools = new Isa95Tools(manager);
            const string receiver = "ns=5;s=order-receiver";
            var order = new Isa95JobOrderInput { JobOrderId = "job-1" };
            var comment = new Isa95CommentInput
            {
                Texts = [new Isa95TextInput { Text = "approved", Locale = "en" }]
            };

            CallToolResult result = operation switch
            {
                "Store" => await tools.StoreV2Async(receiver, order, comment, "selected").ConfigureAwait(false),
                "StoreAndStart" => await tools.StoreAndStartV2Async(receiver, order, comment, "selected")
                    .ConfigureAwait(false),
                "Update" => await tools.UpdateV2Async(receiver, order, comment, "selected").ConfigureAwait(false),
                "Start" => await tools.StartV2Async(receiver, "job-1", comment, "selected").ConfigureAwait(false),
                "Stop" => await tools.StopV2Async(receiver, "job-1", comment, "selected").ConfigureAwait(false),
                "Pause" => await tools.PauseV2Async(receiver, "job-1", comment, "selected").ConfigureAwait(false),
                "Resume" => await tools.ResumeV2Async(receiver, "job-1", comment, "selected").ConfigureAwait(false),
                "Abort" => await tools.AbortV2Async(receiver, "job-1", comment, "selected").ConfigureAwait(false),
                "RevokeStart" => await tools.RevokeStartV2Async(receiver, "job-1", comment, "selected")
                    .ConfigureAwait(false),
                "Cancel" => await tools.CancelV2Async(receiver, "job-1", comment, "selected").ConfigureAwait(false),
                _ => await tools.ClearV2Async(receiver, "job-1", comment, "selected").ConfigureAwait(false)
            };

            Assert.That(result.IsError, Is.False);
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(calls[0].ObjectId, Is.EqualTo(NodeId.Parse(receiver)));
            Assert.That(calls[0].MethodId.TryGetValue(out uint actual), Is.True);
            Assert.That(actual, Is.EqualTo(methodId));
            Assert.That(calls[0].InputArguments[1].TryGetValue(out ArrayOf<LocalizedText> comments), Is.True);
            Assert.That(comments.Count, Is.EqualTo(1));
            Assert.That(comments[0].Text, Is.EqualTo("approved"));
            Assert.That(comments[0].Locale, Is.EqualTo("en"));
            other.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestCase(0UL, true)]
        [TestCase(1UL, false)]
        [TestCase(8UL, true)]
        [TestCase(9UL, true)]
        [TestCase(4294967296UL, true)]
        [TestCase(18446744073709551615UL, true)]
        public async Task ServiceGoodDoesNotHideFullWidthModelRefusalAsync(ulong status, bool isError)
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> session = JobMcpTestSession.Create(manager.Telemetry);
            JobMcpTestSession.SetupCall(session, [], [Variant.From(status)]);
            await manager.RegisterExistingSessionAsync("selected", session.Object, "Anonymous").ConfigureAwait(false);

            CallToolResult result = await new Isa95Tools(manager).StartV2Async(
                "ns=5;s=receiver", "job-1", sessionName: "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.EqualTo(isError));
            Assert.That(result.StructuredContent!.Value.GetProperty("returnStatus").GetUInt64(), Is.EqualTo(status));
            Assert.That(result.StructuredContent.Value.GetProperty("returnStatusText").GetString(),
                Is.EqualTo(status.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        [TestCase("V1Order", V1.Methods.ISA95JobOrderReceiverObjectType_ReceiveJobOrder)]
        [TestCase("V1Query", V1.Methods.ISA95JobResponseProviderObjectType_RequestJobResponse)]
        [TestCase("V1Response", V1.Methods.ISA95JobResponseReceiverObjectType_ReceiveJobResponse)]
        [TestCase("V2QueryId", V2.Methods.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderID)]
        [TestCase("V2QueryState", V2.Methods.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderState)]
        [TestCase("V2Response", V2.Methods.ISA95JobResponseReceiverObjectType_ReceiveJobResponse)]
        public async Task VersionedQueriesAndReceiversUseOnlyTheSuppliedRoleAsync(string operation, uint methodId)
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> session = JobMcpTestSession.Create(manager.Telemetry);
            var calls = new List<CallMethodRequest>();
            ArrayOf<Variant> output = operation switch
            {
                "V1Query" => [Variant.FromStructure(ArrayOf<V1.ISA95JobResponseDataType>.Empty), Variant.From(8UL)],
                "V2QueryState" =>
                    [Variant.FromStructure(ArrayOf<V2.ISA95JobResponseDataType>.Empty), Variant.From(8UL)],
                "V2QueryId" => [Variant.FromStructure(new V2.ISA95JobResponseDataType()), Variant.From(8UL)],
                _ => [Variant.From(8UL)]
            };
            JobMcpTestSession.SetupCall(session, calls, output);
            await manager.RegisterExistingSessionAsync("selected", session.Object, "Anonymous").ConfigureAwait(false);
            var tools = new Isa95Tools(manager);
            const string role = "ns=5;s=the-actual-role";
            var response = new Isa95JobResponseInput
            {
                JobOrderId = "job-1",
                JobResponseId = "response-1"
            };

            CallToolResult result;
            switch (operation)
            {
                case "V1Order":
                    result = await tools.ReceiveV1OrderAsync(role, Isa95V1Command.Store,
                        new Isa95JobOrderInput { JobOrderId = "job-1" }, "selected").ConfigureAwait(false);
                    break;
                case "V1Query":
                    result = await tools.QueryV1ResponsesAsync(role, "job-1", sessionName: "selected")
                        .ConfigureAwait(false);
                    break;
                case "V1Response":
                    result = await tools.ReceiveV1ResponseAsync(role, response, "selected").ConfigureAwait(false);
                    break;
                case "V2QueryId":
                    result = await tools.QueryV2ResponsesAsync(role,
                        new Isa95ResponseQueryInput { JobOrderId = "job-1" }, sessionName: "selected")
                        .ConfigureAwait(false);
                    break;
                case "V2QueryState":
                    result = await tools.QueryV2ResponsesAsync(role,
                        new Isa95ResponseQueryInput { States = [new Isa95StateInput { StateNumber = 3 }] },
                        sessionName: "selected").ConfigureAwait(false);
                    break;
                default:
                    response.States = [new Isa95StateInput { StateNumber = 3 }];
                    result = await tools.ReceiveV2ResponseAsync(role, response, "selected").ConfigureAwait(false);
                    break;
            }

            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent!.Value.GetProperty("returnStatus").GetUInt64(), Is.EqualTo(8UL));
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(calls[0].ObjectId, Is.EqualTo(NodeId.Parse(role)));
            Assert.That(calls[0].MethodId.TryGetValue(out uint actual), Is.True);
            Assert.That(actual, Is.EqualTo(methodId));
        }

        [Test]
        public async Task ReplacingNamedSessionDoesNotReuseOldProxyAsync()
        {
            await using ServiceProvider services = JobMcpTestSession.Services();
            OpcUaSessionManager manager = services.GetRequiredService<OpcUaSessionManager>();
            Mock<ISession> first = JobMcpTestSession.Create(manager.Telemetry);
            Mock<ISession> second = JobMcpTestSession.Create(manager.Telemetry);
            var firstCalls = new List<CallMethodRequest>();
            var secondCalls = new List<CallMethodRequest>();
            JobMcpTestSession.SetupCall(first, firstCalls, [Variant.From(1UL)]);
            JobMcpTestSession.SetupCall(second, secondCalls, [Variant.From(8UL)]);
            var tools = new Isa95Tools(manager);
            await manager.RegisterExistingSessionAsync("selected", first.Object, "Anonymous").ConfigureAwait(false);
            CallToolResult accepted = await tools.StartV2Async(
                "ns=5;s=receiver", "job-1", sessionName: "selected").ConfigureAwait(false);
            await manager.RegisterExistingSessionAsync("selected", second.Object, "Anonymous").ConfigureAwait(false);
            CallToolResult refused = await tools.StartV2Async(
                "ns=5;s=receiver", "job-1", sessionName: "selected").ConfigureAwait(false);

            Assert.That(accepted.IsError, Is.False);
            Assert.That(refused.IsError, Is.True);
            Assert.That(firstCalls, Has.Count.EqualTo(1));
            Assert.That(secondCalls, Has.Count.EqualTo(1));
        }
    }

    /// <summary>
    /// A small session-only seam shared by the scoped Machinery and ISA-95 tool tests.
    /// </summary>
    internal static class JobMcpTestSession
    {
        public static ServiceProvider Services()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore().AddOpcUaMcpMachinery().AddOpcUaMcpIsa95();
            return services.BuildServiceProvider();
        }

        public static Mock<ISession> Create(ITelemetryContext telemetry)
        {
            var context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(ISA95.Namespaces.ISA95);
            context.NamespaceUris.GetIndexOrAppend(V1.Namespaces.ISA95JobControlV1);
            context.NamespaceUris.GetIndexOrAppend(V2.Namespaces.ISA95JobControlV2);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            session.SetupGet(value => value.Connected).Returns(true);
            session.SetupGet(value => value.SessionName).Returns("job-mcp-tests");
            session.SetupGet(value => value.SessionId).Returns(new NodeId("session", 5));
            session.SetupGet(value => value.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                null, new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" },
                EndpointConfiguration.Create()));
            return session;
        }

        public static void SetupCall(
            Mock<ISession> session,
            List<CallMethodRequest> calls,
            ArrayOf<Variant> output)
        {
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    calls.AddRange(requests);
                    return new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = output }]
                    };
                });
        }
    }
}
#endif
