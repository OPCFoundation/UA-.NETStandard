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
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Owns real in-process MCP endpoints and registers only the independently bounded service tools.
    /// </summary>
    internal sealed class MachineToolServiceMcpHarness : IAsyncDisposable
    {
        /// <summary>
        /// Creates the actual MCP binder, filters, and shared-session accessor without opening another UA session.
        /// </summary>
        private MachineToolServiceMcpHarness()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddSingleton<MachineToolClientAccessor>();
            services.AddMcpServer()
                .WithStreamServerTransport(m_requests.Reader.AsStream(), m_responses.Writer.AsStream())
                .WithTools<MachineToolServiceTools>()
                .WithRequestFilters(filters =>
                {
                    filters.AddListToolsFilter(MachineToolServiceFilters.AddSchemas);
                    filters.AddCallToolFilter(next => async (request, ct) =>
                    {
                        LastRequestId = request.JsonRpcRequest.Id;
                        LastCancellationToken = ct;
                        Finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        try
                        {
                            return await next(request, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            Finished.TrySetResult(true);
                        }
                    });
                })
                .WithOpcUaMcpFilters();
            Services = services.BuildServiceProvider();
            m_running = Services.GetRequiredService<McpServer>().RunAsync(m_stop.Token);
        }

        /// <summary>
        /// Gets the initialized MCP client.
        /// </summary>
        internal McpClient Client { get; private set; } = null!;

        /// <summary>
        /// Gets the harness-owned service provider.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the existing-session registry used by the actual tool accessor.
        /// </summary>
        internal OpcUaSessionManager Manager => Services.GetRequiredService<OpcUaSessionManager>();

        /// <summary>
        /// Gets the real server request identity for standard MCP cancellation notifications.
        /// </summary>
        internal RequestId LastRequestId { get; private set; }

        /// <summary>
        /// Gets the real server-side request token.
        /// </summary>
        internal CancellationToken LastCancellationToken { get; private set; }

        /// <summary>
        /// Gets a completion signal after the tool's awaited cleanup, including cancellation paths.
        /// </summary>
        internal TaskCompletionSource<bool> Finished { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Initializes the actual MCP transport with a bounded handshake.
        /// </summary>
        internal static async Task<MachineToolServiceMcpHarness> CreateAsync()
        {
            var harness = new MachineToolServiceMcpHarness();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                harness.Client = await McpClient.CreateAsync(
                    new StreamClientTransport(
                        harness.m_requests.Writer.AsStream(), harness.m_responses.Reader.AsStream()),
                    cancellationToken: deadline.Token).ConfigureAwait(false);
                return harness;
            }
            catch
            {
                await harness.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Sends native JSON parameters through the real protocol rather than directly calling the tool.
        /// </summary>
        internal async Task<CallToolResult> CallAsync(
            string name, JsonObject? arguments = null, CancellationToken ct = default)
        {
            using JsonDocument document = JsonDocument.Parse(arguments?.ToJsonString() ?? "{}");
            var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                parameters.Add(property.Name, property.Value.Clone());
            }
            return await Client.CallToolAsync(new CallToolRequestParams { Name = name, Arguments = parameters }, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Parses the JSON text actually delivered by MCP.
        /// </summary>
        internal static JsonElement Json(CallToolResult result)
        {
            return Parse(result.Content.OfType<TextContentBlock>().Single().Text);
        }

        /// <summary>
        /// Creates a detached native JSON value for input and result assertions.
        /// </summary>
        internal static JsonElement Parse(string text)
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        /// <summary>
        /// Cancels the captured request through the standard MCP protocol.
        /// </summary>
        internal Task CancelAsync()
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
                await m_stop.CancelAsync().ConfigureAwait(false);
                try
                {
                    await m_running.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
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

        /// <summary>The harness-owned request pipe.</summary>
        private readonly Pipe m_requests = new();

        /// <summary>The harness-owned response pipe.</summary>
        private readonly Pipe m_responses = new();

        /// <summary>The harness-owned server lifetime.</summary>
        private readonly CancellationTokenSource m_stop = new();

        /// <summary>The actual server transport loop.</summary>
        private readonly Task m_running;
    }
}
#endif
