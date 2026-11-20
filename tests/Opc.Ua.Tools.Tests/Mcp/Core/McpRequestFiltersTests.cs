/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using UaMcpRequestFilters = Opc.Ua.Mcp.McpRequestFilters;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class McpRequestFiltersTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var services = new ServiceCollection();
            services
                .AddMcpServer()
                .WithStreamServerTransport(Stream.Null, Stream.Null);
            m_serviceProvider = services.BuildServiceProvider();
            m_serviceProvider.GetRequiredService<McpServer>();
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_serviceProvider != null)
            {
                await m_serviceProvider.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void ValidateRequiredArgumentsWithNullNextThrows()
        {
            Assert.That(
                () => UaMcpRequestFilters.ValidateRequiredArguments(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task ValidateRequiredArgumentsWithMissingArgumentsReturnsActionableErrorAsync()
        {
            McpServerTool tool = CreateTool(
                """
                {
                  "type": "object",
                  "required": ["endpointUrl", "securityMode"]
                }
                """);
            RequestContext<CallToolRequestParams> request = CreateRequest(
                tool,
                new CallToolRequestParams
                {
                    Name = "Connect",
                    Arguments = new Dictionary<string, JsonElement>()
                });
            bool nextCalled = false;
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                UaMcpRequestFilters.ValidateRequiredArguments((_, _) =>
                {
                    nextCalled = true;
                    return ValueTask.FromResult(new CallToolResult());
                });

            CallToolResult result = await filter(request, CancellationToken.None).ConfigureAwait(false);

            Assert.That(nextCalled, Is.False);
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(result.Content[0], Is.TypeOf<TextContentBlock>());
            Assert.That(
                ((TextContentBlock)result.Content[0]).Text,
                Is.EqualTo(
                    "Tool 'Connect' is missing required argument(s): endpointUrl, securityMode. " +
                    "Call the tool again with values for each named argument."));
        }

        [Test]
        public async Task ValidateRequiredArgumentsWithAllArgumentsInvokesNextAsync()
        {
            McpServerTool tool = CreateTool(
                """
                {
                  "type": "object",
                  "required": ["endpointUrl"]
                }
                """);
            RequestContext<CallToolRequestParams> request = CreateRequest(
                tool,
                new CallToolRequestParams
                {
                    Name = "Connect",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["endpointUrl"] = JsonSerializer.SerializeToElement(
                            "opc.tcp://localhost:4840")
                    }
                });
            var expected = new CallToolResult();
            bool nextCalled = false;
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                UaMcpRequestFilters.ValidateRequiredArguments((forwardedRequest, _) =>
                {
                    nextCalled = true;
                    Assert.That(forwardedRequest, Is.SameAs(request));
                    return ValueTask.FromResult(expected);
                });

            CallToolResult result = await filter(request, CancellationToken.None).ConfigureAwait(false);

            Assert.That(nextCalled, Is.True);
            Assert.That(result, Is.SameAs(expected));
        }

        [Test]
        public async Task ValidateRequiredArgumentsWithoutMatchedToolInvokesNextAsync()
        {
            RequestContext<CallToolRequestParams> request = CreateRequest(
                null,
                new CallToolRequestParams { Name = "Connect" });
            var expected = new CallToolResult();
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                UaMcpRequestFilters.ValidateRequiredArguments((_, _) =>
                    ValueTask.FromResult(expected));

            CallToolResult result = await filter(request, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(expected));
        }

        [TestCase("""{"type":"object"}""")]
        [TestCase("""{"type":"object","required":true}""")]
        public async Task ValidateRequiredArgumentsWithNonRequiredSchemaInvokesNextAsync(
            string inputSchema)
        {
            McpServerTool tool = CreateTool(inputSchema);
            RequestContext<CallToolRequestParams> request = CreateRequest(
                tool,
                new CallToolRequestParams { Name = "TestTool" });
            var expected = new CallToolResult();
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                UaMcpRequestFilters.ValidateRequiredArguments((_, _) =>
                    ValueTask.FromResult(expected));

            CallToolResult result = await filter(request, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(expected));
        }

        [Test]
        public async Task ValidateRequiredArgumentsWithMixedRequiredEntriesIgnoresNonStringsAsync()
        {
            McpServerTool tool = CreateTool(
                """
                {
                  "type": "object",
                  "required": [42, "nodeId"]
                }
                """);
            RequestContext<CallToolRequestParams> request = CreateRequest(
                tool,
                new CallToolRequestParams { Name = "Read" });
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                UaMcpRequestFilters.ValidateRequiredArguments((_, _) =>
                    ValueTask.FromResult(new CallToolResult()));

            CallToolResult result = await filter(request, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(
                ((TextContentBlock)result.Content[0]).Text,
                Does.Contain("missing required argument(s): nodeId."));
        }

        [Test]
        public async Task ValidateRequiredArgumentsWithNullParametersUsesProtocolToolNameAsync()
        {
            McpServerTool tool = CreateTool(
                """
                {
                  "type": "object",
                  "required": ["nodeId"]
                }
                """);
            RequestContext<CallToolRequestParams> request = CreateRequest(tool, null);
            McpRequestHandler<CallToolRequestParams, CallToolResult> filter =
                UaMcpRequestFilters.ValidateRequiredArguments((_, _) =>
                    ValueTask.FromResult(new CallToolResult()));

            CallToolResult result = await filter(request, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(
                ((TextContentBlock)result.Content[0]).Text,
                Does.StartWith($"Tool '{tool.ProtocolTool.Name}' is missing required argument(s): nodeId."));
        }

        private RequestContext<CallToolRequestParams> CreateRequest(
            McpServerTool? tool,
            CallToolRequestParams? parameters)
        {
            var request = new RequestContext<CallToolRequestParams>(
                (m_serviceProvider ??
                    throw new InvalidOperationException("The service provider is not initialized."))
                    .GetRequiredService<McpServer>(),
                new JsonRpcRequest
                {
                    Method = "tools/call",
                    Id = new RequestId("1")
                },
                parameters!);
            request.MatchedPrimitive = tool;
            return request;
        }

        private static McpServerTool CreateTool(string inputSchema)
        {
            McpServerTool tool = McpServerTool.Create(
                (Func<string>)(() => "unused"),
                new McpServerToolCreateOptions { Name = "test_tool" });
            tool.ProtocolTool.InputSchema = JsonSerializer.Deserialize<JsonElement>(inputSchema);
            return tool;
        }

        private ServiceProvider? m_serviceProvider;
    }
}
#endif
