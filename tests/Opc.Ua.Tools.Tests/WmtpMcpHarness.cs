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
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Uses real MCP client/server framing, binding and filters in the single MachineTool profile.
    /// </summary>
    internal sealed class WmtpMcpHarness : IAsyncDisposable
    {
        /// <summary>
        /// Starts an isolated stream transport without opening or owning any OPC UA session.
        /// </summary>
        private WmtpMcpHarness()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                Execution = new McpExecutionOptions
                {
                    LocalCallerId = "wmtp-tests",
                    CallerGrants =
                    [
                        new McpCallerGrant
                        {
                            Caller = new McpCallerIdentity("wmtp-tests", McpCallerContext.LocalIssuer, IsLocal: true),
                            SessionNames = ["wireless", "second"]
                        }
                    ]
                }
            }).AddOpcUaMcpMachineTool();
            services.AddMcpServer()
                .WithStreamServerTransport(m_requests.Reader.AsStream(), m_responses.Writer.AsStream())
                .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, ct) =>
                {
                    var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    LastRequestId = request.JsonRpcRequest.Id;
                    LastRequestToken = ct;
                    LastRequestCompleted = completed.Task;
                    try
                    {
                        return await next(request, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        completed.TrySetResult(true);
                    }
                }))
                .WithOpcUaMcpFilters()
                .WithOpcUaMachineToolTools(McpToolProfile.MachineTool);
            services.Configure<McpServerOptions>(options =>
                options.Filters.Message.IncomingFilters.Add(next => async (context, ct) =>
                {
                    if (context.JsonRpcMessage is JsonRpcNotification
                        { Method: NotificationMethods.CancelledNotification })
                    {
                        await CancellationDelivery.WaitAsync(ct).ConfigureAwait(false);
                    }
                    await next(context, ct).ConfigureAwait(false);
                }));
            Services = services.BuildServiceProvider();
            m_running = Services.GetRequiredService<McpServer>().RunAsync(m_stop.Token);
        }

        /// <summary>
        /// Gets the connected real MCP client.
        /// </summary>
        internal McpClient Client { get; private set; } = null!;

        /// <summary>
        /// Gets the isolated host services.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the same connection manager used by the bundled connection tools.
        /// </summary>
        internal OpcUaSessionManager Manager => Services.GetRequiredService<OpcUaSessionManager>();

        /// <summary>
        /// Gets the typed shared-session accessor for direct boundary tests.
        /// </summary>
        internal MachineToolClientAccessor Accessor => Services.GetRequiredService<MachineToolClientAccessor>();

        /// <summary>
        /// Gets the last actual incoming request identity.
        /// </summary>
        internal RequestId LastRequestId { get; private set; }

        /// <summary>
        /// Gets the token passed by MCP to the generated tool binder.
        /// </summary>
        internal CancellationToken LastRequestToken { get; private set; }

        /// <summary>
        /// Gets completion of the captured server handler, including its cleanup path.
        /// </summary>
        internal Task LastRequestCompleted { get; private set; } = Task.CompletedTask;

        /// <summary>
        /// Gets or sets the gate before the server processes a cancellation notification.
        /// </summary>
        internal Task CancellationDelivery { get; set; } = Task.CompletedTask;

        /// <summary>
        /// Initializes the MCP client and optionally borrows one named OPC UA session.
        /// </summary>
        internal static async Task<WmtpMcpHarness> CreateAsync(ISession? session = null)
        {
            var harness = new WmtpMcpHarness();
            try
            {
                if (session != null)
                {
                    await harness.Manager.RegisterExistingSessionAsync("wireless", session, "Anonymous")
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
        /// Sends native JSON arguments over the actual MCP stream.
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
            return await Client.CallToolAsync(new CallToolRequestParams
            {
                Name = name, Arguments = parameters
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Parses the single bounded JSON text block received by the real client.
        /// </summary>
        internal static JsonElement Json(CallToolResult result)
        {
            using JsonDocument document = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Delivers the standard cancellation notification to one captured request, not a UA wire abort.
        /// </summary>
        internal Task CancelAsync(RequestId id)
        {
            return Client.SendNotificationAsync(NotificationMethods.CancelledNotification,
                new CancelledNotificationParams { RequestId = id });
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

        /// <summary>The owned inbound transport pipe.</summary>
        private readonly Pipe m_requests = new();

        /// <summary>The owned outbound transport pipe.</summary>
        private readonly Pipe m_responses = new();

        /// <summary>The owned MCP shutdown source.</summary>
        private readonly CancellationTokenSource m_stop = new();

        /// <summary>The real server request loop.</summary>
        private readonly Task m_running;
    }
}
#endif
