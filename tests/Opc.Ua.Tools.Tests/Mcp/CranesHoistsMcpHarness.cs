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
using System.IO.Pipelines;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Exercises the actual MCP transport, binder, filters and shared session accessor in process.
    /// </summary>
    internal sealed class CranesHoistsMcpHarness : IAsyncDisposable
    {
        /// <summary>
        /// Builds a standalone crane MCP host using owned in-memory streams.
        /// </summary>
        private CranesHoistsMcpHarness(CranesHoistsClientOptions? options)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                Execution = new McpExecutionOptions
                {
                    LocalCallerId = "crane-tests",
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = new McpCallerIdentity("crane-tests", McpCallerContext.LocalIssuer, IsLocal: true),
                            SessionNames = ["crane", "second", "unauthorized"]
                        }
                    ]
                }
            });
            if (options != null)
            {
                services.AddSingleton(options);
            }
            services.AddOpcUaMcpCranesHoists();
            services.AddMcpServer()
                .WithStreamServerTransport(m_toServer.Reader.AsStream(), m_toClient.Writer.AsStream())
                .WithRequestFilters(filters => filters.AddCallToolFilter(next => (request, ct) =>
                {
                    LastToolCancellationToken = ct;
                    LastToolRequestId = request.JsonRpcRequest.Id;
                    return next(request, ct);
                }))
                .WithOpcUaMcpFilters()
                .WithOpcUaCranesHoistsTools(McpToolProfile.CranesHoists);
            Services = services.BuildServiceProvider();
            McpServer server = Services.GetRequiredService<McpServer>();
            m_running = server.RunAsync(m_stop.Token);
        }

        /// <summary>
        /// Gets the real MCP client after successful initialization.
        /// </summary>
        internal McpClient Client { get; private set; } = null!;

        /// <summary>
        /// Gets the owned service provider; registered OPC UA sessions remain borrowed.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the shared session manager, also used by connection tools.
        /// </summary>
        internal OpcUaSessionManager Manager => Services.GetRequiredService<OpcUaSessionManager>();

        /// <summary>
        /// Gets the actual accessor used by generated tool invocations.
        /// </summary>
        internal CranesHoistsClientAccessor Accessor => Services.GetRequiredService<CranesHoistsClientAccessor>();

        /// <summary>
        /// Gets the actual incoming request token used by the MCP tool binder.
        /// </summary>
        internal CancellationToken LastToolCancellationToken { get; private set; }

        /// <summary>
        /// Gets the actual protocol request identity for explicit MCP cancellation.
        /// </summary>
        internal RequestId LastToolRequestId { get; private set; }

        /// <summary>
        /// Initializes MCP and optionally borrows a named test session.
        /// </summary>
        internal static async Task<CranesHoistsMcpHarness> CreateAsync(
            ISession? session = null,
            CranesHoistsClientOptions? options = null)
        {
            var harness = new CranesHoistsMcpHarness(options);
            try
            {
                if (session != null)
                {
                    await harness.Manager.RegisterExistingSessionAsync("crane", session, "Anonymous")
                        .ConfigureAwait(false);
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                harness.Client = await McpClient.CreateAsync(
                    new StreamClientTransport(
                        harness.m_toServer.Writer.AsStream(), harness.m_toClient.Reader.AsStream()),
                    cancellationToken: timeout.Token).ConfigureAwait(false);
                return harness;
            }
            catch
            {
                await harness.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Sends real MCP call parameters without an untyped public object API.
        /// </summary>
        internal async Task<CallToolResult> CallAsync(
            string name,
            JsonObject? arguments = null,
            CancellationToken cancellationToken = default)
        {
            using JsonDocument document = JsonDocument.Parse(arguments?.ToJsonString() ?? "{}");
            var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty argument in document.RootElement.EnumerateObject())
            {
                parameters.Add(argument.Name, argument.Value.Clone());
            }
            return await Client.CallToolAsync(new CallToolRequestParams
            {
                Name = name,
                Arguments = parameters
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the structured JSON text actually delivered by MCP.
        /// </summary>
        internal static JsonElement Json(CallToolResult result)
        {
            using JsonDocument document = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Sends the standard MCP cancellation notification for the captured in-flight request.
        /// </summary>
        internal Task CancelLastRequestAsync()
        {
            return Client.SendNotificationAsync(NotificationMethods.CancelledNotification,
                new CancelledNotificationParams { RequestId = LastToolRequestId });
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Client != null)
                {
                    await Client.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await m_stop.CancelAsync().ConfigureAwait(false);
                try
                {
                    await m_running.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (m_stop.IsCancellationRequested)
                {
                }
                finally
                {
                    await Services.DisposeAsync().ConfigureAwait(false);
                    m_stop.Dispose();
                }
            }
        }

        /// <summary>The request pipe owned by this harness.</summary>
        private readonly Pipe m_toServer = new();

        /// <summary>The response pipe owned by this harness.</summary>
        private readonly Pipe m_toClient = new();

        /// <summary>The owned server shutdown token.</summary>
        private readonly CancellationTokenSource m_stop = new();

        /// <summary>The bounded-lifetime server request loop.</summary>
        private readonly Task m_running;
    }
}
#endif
