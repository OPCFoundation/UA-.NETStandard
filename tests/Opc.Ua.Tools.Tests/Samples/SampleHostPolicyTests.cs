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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Tools.Tests.Samples
{
    [TestFixture]
    public sealed class SampleHostPolicyTests
    {
        [Test]
        public async Task McpHostRequiresEncryptedConnectionsWhenModeIsOmittedAsync()
        {
            await using ServiceProvider provider = CreateHost();
            var parameters = new CallToolRequestParams
            {
                Name = "Connect",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["endpointUrl"] = JsonSerializer.SerializeToElement("opc.tcp://localhost:4840")
                }
            };
            CallToolRequestParams? forwarded = null;

            CallToolResult result = await InvokeAsync(provider, parameters, request =>
            {
                forwarded = request.Params;
                return new CallToolResult();
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.Not.True,
                string.Join(
                    Environment.NewLine,
                    result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
            Assert.That(forwarded, Is.Not.Null);
            Assert.That(forwarded!.Arguments!.TryGetValue("securityMode", out JsonElement mode), Is.True);
            Assert.That(mode.GetString(), Is.EqualTo("SignAndEncrypt"));
            Assert.That(parameters.Arguments.ContainsKey("securityMode"), Is.False,
                "The caller's arguments must not become state shared with another connection.");
        }

        [TestCase("null")]
        [TestCase("\"\"")]
        [TestCase("\"Unknown\"")]
        [TestCase("false")]
        [TestCase("{}")]
        [TestCase("[\"None\"]")]
        public async Task McpHostRejectsInvalidExplicitModeWithoutConnectingAsync(string value)
        {
            await using ServiceProvider provider = CreateHost();
            using var document = JsonDocument.Parse(value);
            var parameters = new CallToolRequestParams
            {
                Name = "Connect",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["endpointUrl"] = JsonSerializer.SerializeToElement("opc.tcp://localhost:4840"),
                    ["securityMode"] = document.RootElement.Clone()
                }
            };
            bool connected = false;

            CallToolResult result = await InvokeAsync(provider, parameters, _ =>
            {
                connected = true;
                return new CallToolResult();
            }).ConfigureAwait(false);

            Assert.That(connected, Is.False);
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(((TextContentBlock)result.Content[0]).Text,
                Does.Contain("securityMode").And.Contain("SignAndEncrypt").And.Contain("GetEndpoints"));
        }

        [TestCase("None")]
        [TestCase("none")]
        [TestCase("Sign")]
        [TestCase("SignAndEncrypt")]
        public async Task McpHostPreservesExplicitModesAndOtherConnectionArgumentsAsync(string mode)
        {
            await using ServiceProvider provider = CreateHost();
            var parameters = new CallToolRequestParams
            {
                Name = "Connect",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["endpointUrl"] = JsonSerializer.SerializeToElement("opc.tcp://localhost:4840"),
                    ["securityMode"] = JsonSerializer.SerializeToElement(mode),
                    ["securityPolicy"] = JsonSerializer.SerializeToElement("Basic256Sha256"),
                    ["autoAcceptCerts"] = JsonSerializer.SerializeToElement(false),
                    ["name"] = JsonSerializer.SerializeToElement("explicit-session")
                }
            };

            CallToolResult result = await InvokeAsync(provider, parameters, request =>
            {
                Assert.That(request.Params.Arguments, Is.EqualTo(parameters.Arguments));
                return new CallToolResult();
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.Not.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task McpHostKeepsConnectionConsentIsolatedAcrossRequestsAsync(bool composedProfiles)
        {
            await using ServiceProvider provider = CreateHost(composedProfiles);
            var optedIn = new CallToolRequestParams
            {
                Name = "Connect",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["endpointUrl"] = JsonSerializer.SerializeToElement("opc.tcp://localhost:4840"),
                    ["securityMode"] = JsonSerializer.SerializeToElement("None"),
                    ["autoAcceptCerts"] = JsonSerializer.SerializeToElement(true)
                }
            };
            var secure = new CallToolRequestParams
            {
                Name = "Connect",
                Meta = new JsonObject { ["trace"] = "request-marker" },
                RequestState = "state-marker",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["endpointUrl"] = JsonSerializer.SerializeToElement("opc.tcp://localhost:4841"),
                    ["securityPolicy"] = JsonSerializer.SerializeToElement("Basic256Sha256")
                }
            };
            CallToolRequestParams? forwarded = null;

            await InvokeAsync(provider, optedIn, _ => new CallToolResult()).ConfigureAwait(false);
            CallToolResult result = await InvokeAsync(provider, secure, request =>
            {
                forwarded = request.Params;
                return new CallToolResult();
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.Not.True);
            Assert.That(forwarded, Is.Not.Null);
            Assert.That(forwarded!.Arguments!["securityMode"].GetString(), Is.EqualTo("SignAndEncrypt"));
            Assert.That(forwarded.Arguments["securityPolicy"].GetString(), Is.EqualTo("Basic256Sha256"));
            Assert.That(forwarded.Arguments.ContainsKey("autoAcceptCerts"), Is.False);
            Assert.That(forwarded.Meta, Is.SameAs(secure.Meta));
            Assert.That(forwarded.RequestState, Is.EqualTo("state-marker"));
            Assert.That(secure.Arguments.ContainsKey("securityMode"), Is.False);
        }

        [Test]
        public async Task McpHostStillReportsMissingEndpointWithoutInvokingToolAsync()
        {
            await using ServiceProvider provider = CreateHost();
            bool invoked = false;

            CallToolResult result = await InvokeAsync(
                provider, new CallToolRequestParams { Name = "Connect" }, _ =>
                {
                    invoked = true;
                    return new CallToolResult();
                }).ConfigureAwait(false);

            Assert.That(invoked, Is.False);
            Assert.That(result.IsError, Is.True);
            Assert.That(((TextContentBlock)result.Content[0]).Text,
                Does.Contain("missing required argument(s): endpointUrl"));
        }

        [Test]
        public async Task McpHostDoesNotChangeNonConnectionRequestsAsync()
        {
            await using ServiceProvider provider = CreateHost();
            var parameters = new CallToolRequestParams { Name = "GetConnectionStatus" };

            CallToolResult result = await InvokeAsync(provider, parameters, request =>
            {
                Assert.That(request.Params, Is.SameAs(parameters));
                Assert.That(request.Params.Arguments, Is.Null);
                return new CallToolResult();
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.Not.True);
        }

        [Test]
        public async Task EmbeddedCoreKeepsItsOmittedModeCompatibilityAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaMcpFilters().WithOpcUaCoreTools(McpToolProfile.Core);
            await using ServiceProvider provider = services.BuildServiceProvider();
            var parameters = new CallToolRequestParams
            {
                Name = "Connect",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["endpointUrl"] = JsonSerializer.SerializeToElement("opc.tcp://localhost:4840")
                }
            };

            CallToolResult result = await InvokeAsync(provider, parameters, request =>
            {
                Assert.That(request.Params.Arguments!.ContainsKey("securityMode"), Is.False);
                return new CallToolResult();
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.Not.True);
        }

        [TestCase("None", false, "securityMode=None", "untrusted")]
        [TestCase("SignAndEncrypt", true, "untrusted", "securityMode=None")]
        public async Task McpHostWarnsBeforeEachExplicitRelaxationWithoutLoggingArgumentsAsync(
            string mode,
            bool autoAccept,
            string expected,
            string absent)
        {
            using var log = new WarningLog();
            await using ServiceProvider provider = CreateHost(log: log);
            var parameters = new CallToolRequestParams
            {
                Name = "Connect",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["endpointUrl"] = JsonSerializer.SerializeToElement("opc.tcp://do-not-log:4840"),
                    ["securityMode"] = JsonSerializer.SerializeToElement(mode),
                    ["autoAcceptCerts"] = JsonSerializer.SerializeToElement(autoAccept),
                    ["name"] = JsonSerializer.SerializeToElement("do-not-log")
                }
            };

            CallToolResult result = await InvokeAsync(provider, parameters, _ =>
            {
                Assert.That(log.Messages, Has.Count.EqualTo(1));
                Assert.That(log.Messages[0], Does.Contain(expected).And.Not.Contain(absent)
                    .And.Not.Contain("do-not-log"));
                return new CallToolResult();
            }).ConfigureAwait(false);

            Assert.That(result.IsError, Is.Not.True);
        }

        [Test]
        [NonParallelizable]
        public async Task StdioHostWritesRelaxationWarningsOnlyToStandardErrorAsync()
        {
            TextWriter originalOutput = Console.Out;
            TextWriter originalError = Console.Error;
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                await using ServiceProvider provider = CreateHost(useStdioTransport: true);
                CallToolResult result = await InvokeAsync(
                    provider,
                    new CallToolRequestParams
                    {
                        Name = "Connect",
                        Arguments = new Dictionary<string, JsonElement>
                        {
                            ["endpointUrl"] = JsonSerializer.SerializeToElement("opc.tcp://do-not-log:4840"),
                            ["securityMode"] = JsonSerializer.SerializeToElement("None"),
                            ["autoAcceptCerts"] = JsonSerializer.SerializeToElement(true)
                        }
                    },
                    _ => new CallToolResult()).ConfigureAwait(false);

                Assert.That(result.IsError, Is.Not.True);
            }
            finally
            {
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
            }

            Assert.That(output.ToString(), Is.Empty, "stdout is reserved for MCP JSON-RPC messages.");
            Assert.That(error.ToString(), Does.Contain("securityMode=None").And.Contain("autoAcceptCerts=true")
                .And.Not.Contain("do-not-log"));
        }

        private static ServiceProvider CreateHost(
            bool composedProfiles = false,
            WarningLog? log = null,
            bool useStdioTransport = false)
        {
            var services = new ServiceCollection();
            if (useStdioTransport)
            {
                services.AddLogging(builder => McpHostBuilder.ConfigureLogging(builder, useStdioTransport: true));
            }
            if (log != null)
            {
                services.AddLogging(builder => builder.AddProvider(log));
            }
            McpHostBuilder.ConfigureServices(services, new PcapOptions());
            IMcpServerBuilder builder = services.AddMcpServer()
                .WithStreamServerTransport(Stream.Null, Stream.Null);
            if (composedProfiles)
            {
                McpHostBuilder.ConfigureMcpTools(
                    builder, McpToolProfileSet.Parse("vision,robotics"), diagnosticsToolsEnabled: false);
            }
            else
            {
                McpHostBuilder.ConfigureMcpTools(builder, McpToolProfile.Core, diagnosticsToolsEnabled: false);
            }
            return services.BuildServiceProvider();
        }

        private static async Task<CallToolResult> InvokeAsync(
            ServiceProvider provider,
            CallToolRequestParams parameters,
            Func<RequestContext<CallToolRequestParams>, CallToolResult> invoke)
        {
            var request = new RequestContext<CallToolRequestParams>(
                provider.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("test") },
                parameters)
            {
                MatchedPrimitive = provider.GetServices<McpServerTool>()
                    .Single(tool => tool.ProtocolTool.Name == parameters.Name)
            };
            McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
                (context, _) => ValueTask.FromResult(invoke(context));
            IList<McpRequestFilter<CallToolRequestParams, CallToolResult>> filters =
                provider.GetRequiredService<IOptions<McpServerOptions>>().Value.Filters.Request.CallToolFilters;
            for (int i = filters.Count - 1; i >= 0; i--)
            {
                handler = filters[i](handler);
            }
            return await handler(request, CancellationToken.None).ConfigureAwait(false);
        }

        private sealed class WarningLog : ILoggerProvider, ILogger
        {
            public List<string> Messages { get; } = [];

            public ILogger CreateLogger(string categoryName)
            {
                return this;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel == LogLevel.Warning;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    Messages.Add(formatter(state, exception));
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
#endif
