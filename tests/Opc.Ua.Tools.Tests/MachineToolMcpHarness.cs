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

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Runs the real MachineTool MCP binder and transport while borrowing only an explicitly supplied UA session.
    /// </summary>
    internal sealed class MachineToolMcpHarness : IAsyncDisposable
    {
        private MachineToolMcpHarness()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore().AddOpcUaMcpMachineTool();
            services.AddMcpServer()
                .WithStreamServerTransport(m_requests.Reader.AsStream(), m_responses.Writer.AsStream())
                .WithRequestFilters(filters => filters.AddCallToolFilter(next => (request, token) =>
                {
                    RequestId = request.JsonRpcRequest.Id;
                    return next(request, token);
                }))
                .WithOpcUaMcpFilters()
                .WithOpcUaMachineToolTools(McpToolProfile.MachineTool);
            Services = services.BuildServiceProvider();
            m_running = Services.GetRequiredService<McpServer>().RunAsync(m_shutdown.Token);
        }

        internal ServiceProvider Services { get; }

        internal McpClient Client { get; private set; } = null!;

        internal RequestId RequestId { get; private set; }

        internal static async Task<MachineToolMcpHarness> CreateAsync(ISession session)
        {
            var harness = new MachineToolMcpHarness();
            try
            {
                await harness.Services.GetRequiredService<OpcUaSessionManager>()
                    .RegisterExistingSessionAsync("machine", session, "Anonymous").ConfigureAwait(false);
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

        internal async Task<CallToolResult> CallAsync(string tool, JsonObject? arguments = null)
        {
            using JsonDocument json = JsonDocument.Parse(arguments?.ToJsonString() ?? "{}");
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in json.RootElement.EnumerateObject())
            {
                values.Add(property.Name, property.Value.Clone());
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            return await Client.CallToolAsync(
                new CallToolRequestParams { Name = tool, Arguments = values }, timeout.Token).ConfigureAwait(false);
        }

        internal Task CancelAsync()
        {
            return Client.SendNotificationAsync(NotificationMethods.CancelledNotification,
                new CancelledNotificationParams { RequestId = RequestId });
        }

        internal static JsonElement Json(CallToolResult result)
        {
            using JsonDocument document = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
            return document.RootElement.Clone();
        }

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

        private readonly Pipe m_requests = new();
        private readonly Pipe m_responses = new();
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly Task m_running;
    }
}
#endif
