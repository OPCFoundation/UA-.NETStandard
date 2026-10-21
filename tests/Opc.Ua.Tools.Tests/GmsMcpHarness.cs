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
using Opc.Ua.GMS.Client;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Runs the real MCP binder, filters and client transport against borrowed OPC UA sessions.
    /// </summary>
    internal sealed class GmsMcpHarness : IAsyncDisposable
    {
        /// <summary>
        /// Constructs a standalone GMS catalog on owned in-process streams.
        /// </summary>
        private GmsMcpHarness(GmsClientOptions? options)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                Execution = new McpExecutionOptions
                {
                    LocalCallerId = "gms-tests",
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = new McpCallerIdentity("gms-tests", McpCallerContext.LocalIssuer, IsLocal: true),
                            SessionNames = ["gms", "second"]
                        }
                    ]
                }
            });
            if (options != null)
            {
                services.AddSingleton(options);
            }
            services.AddOpcUaMcpGms();
            services.AddMcpServer()
                .WithStreamServerTransport(m_requests.Reader.AsStream(), m_responses.Writer.AsStream())
                .WithRequestFilters(filters => filters.AddCallToolFilter(next => (request, ct) =>
                {
                    LastRequestId = request.JsonRpcRequest.Id;
                    LastRequestToken = ct;
                    return next(request, ct);
                }))
                .WithOpcUaMcpFilters()
                .WithOpcUaGmsTools(McpToolProfile.Gms);
            Services = services.BuildServiceProvider();
            m_running = Services.GetRequiredService<McpServer>().RunAsync(m_shutdown.Token);
        }

        /// <summary>
        /// Gets the initialized real MCP client.
        /// </summary>
        internal McpClient Client { get; private set; } = null!;

        /// <summary>
        /// Gets the owned service container, which does not own registered existing sessions.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the shared named-session manager.
        /// </summary>
        internal OpcUaSessionManager Manager => Services.GetRequiredService<OpcUaSessionManager>();

        /// <summary>
        /// Gets the same adapter accessor used by MCP dispatch.
        /// </summary>
        internal GmsClientAccessor Accessor => Services.GetRequiredService<GmsClientAccessor>();

        /// <summary>
        /// Gets the actual last tool request's server-side cancellation token.
        /// </summary>
        internal CancellationToken LastRequestToken { get; private set; }

        /// <summary>
        /// Gets the actual incoming JSON-RPC request identity for protocol cancellation.
        /// </summary>
        internal RequestId LastRequestId { get; private set; }

        /// <summary>
        /// Initializes transport and optionally borrows an already connected named OPC UA session.
        /// </summary>
        internal static async Task<GmsMcpHarness> CreateAsync(
            ISession? session = null, GmsClientOptions? options = null)
        {
            var harness = new GmsMcpHarness(options);
            try
            {
                if (session != null)
                {
                    await harness.Manager.RegisterExistingSessionAsync("gms", session, "Anonymous")
                        .ConfigureAwait(false);
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                harness.Client = await McpClient.CreateAsync(
                    new StreamClientTransport(
                        harness.m_requests.Writer.AsStream(), harness.m_responses.Reader.AsStream()),
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
        /// Invokes the real wire tool without a public boxed-object argument map.
        /// </summary>
        internal async Task<CallToolResult> CallAsync(
            string name, JsonObject? arguments = null, CancellationToken cancellationToken = default)
        {
            using JsonDocument document = JsonDocument.Parse(arguments?.ToJsonString() ?? "{}");
            var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                parameters.Add(property.Name, property.Value.Clone());
            }
            return await Client.CallToolAsync(
                new CallToolRequestParams { Name = name, Arguments = parameters }, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Reads JSON delivered over the actual MCP transport.
        /// </summary>
        internal static JsonElement Json(CallToolResult value)
        {
            using JsonDocument document = JsonDocument.Parse(value.Content.OfType<TextContentBlock>().Single().Text);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Explicitly sends the standard MCP cancellation notification, independently of local call-token behavior.
        /// </summary>
        internal Task CancelLastRequestAsync()
        {
            return Client.SendNotificationAsync(NotificationMethods.CancelledNotification,
                new CancelledNotificationParams { RequestId = LastRequestId });
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
                await m_shutdown.CancelAsync().ConfigureAwait(false);
                try
                {
                    await m_running.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
                {
                }
                finally
                {
                    await Services.DisposeAsync().ConfigureAwait(false);
                    m_shutdown.Dispose();
                }
            }
        }

        /// <summary>The owned client-to-server transport.</summary>
        private readonly Pipe m_requests = new();

        /// <summary>The owned server-to-client transport.</summary>
        private readonly Pipe m_responses = new();

        /// <summary>The bounded server lifetime.</summary>
        private readonly CancellationTokenSource m_shutdown = new();

        /// <summary>The active MCP server run.</summary>
        private readonly Task m_running;
    }
}
#endif
