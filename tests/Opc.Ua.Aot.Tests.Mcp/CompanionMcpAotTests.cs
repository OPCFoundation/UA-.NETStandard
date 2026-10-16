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

using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Executes the combined new companion catalogs and schema filters over real MCP messages in the native harness.
    /// </summary>
    public sealed class CompanionMcpAotTests
    {
        /// <summary>
        /// Native list and invalid-call round trips require no reflection serializer or duplicate connection catalog.
        /// </summary>
        [Test]
        public async Task CompanionCatalogSchemasAndInputErrorsRunWithoutReflectionAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            var requests = new Pipe();
            var replies = new Pipe();
            using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore().AddOpcUaMcpMachineTool().AddOpcUaMcpGms().AddOpcUaMcpCranesHoists();
            McpToolProfileSet profiles = McpToolProfileSet.Parse("machinetool,gms,craneshoists");
            services.AddMcpServer().WithStreamServerTransport(requests.Reader.AsStream(), replies.Writer.AsStream())
                .WithOpcUaMcpFilters()
                .WithOpcUaMachineToolTools(profiles)
                .WithOpcUaGmsTools(profiles)
                .WithOpcUaCranesHoistsTools(profiles);
            await using ServiceProvider provider = services.BuildServiceProvider();
            Task server = provider.GetRequiredService<McpServer>().RunAsync(stopping.Token);
            try
            {
                await using McpClient client = await McpClient.CreateAsync(
                    new StreamClientTransport(requests.Writer.AsStream(), replies.Reader.AsStream()),
                    cancellationToken: stopping.Token).ConfigureAwait(false);
                ListToolsResult catalog = await client.ListToolsAsync(
                    new ListToolsRequestParams(), stopping.Token).ConfigureAwait(false);
                string[] names = [.. catalog.Tools.Select(tool => tool.Name)];
                await Assert.That(names.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(names.Length);
                await Assert.That(names.Count(name => name == "Connect")).IsEqualTo(1);
                await Assert.That(names.Contains("machinetool_query_jobs", StringComparer.Ordinal)).IsTrue();
                await Assert.That(names.Contains("wmtp_transfer", StringComparer.Ordinal)).IsTrue();
                await Assert.That(names.Contains("gms_observe", StringComparer.Ordinal)).IsTrue();
                await Assert.That(names.Contains("craneshoists_configure_limit", StringComparer.Ordinal)).IsTrue();
                JsonElement selection = catalog.Tools.Single(tool => tool.Name == "machinetool_observe_values")
                    .InputSchema.GetProperty("properties").GetProperty("nodeIds");
                await Assert.That(selection.GetProperty("type").GetString()).IsEqualTo("array");
                await Assert.That(selection.GetProperty("maxItems").GetInt32()).IsEqualTo(256);
                using JsonDocument invalid = JsonDocument.Parse("""{"machineNodeId":"i=0"}""");
                CallToolResult rejected = await client.CallToolAsync(new CallToolRequestParams
                {
                    Name = "machinetool_read_machine",
                    Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["machineNodeId"] = invalid.RootElement.GetProperty("machineNodeId").Clone()
                    }
                }, stopping.Token).ConfigureAwait(false);
                await Assert.That(rejected.IsError).IsTrue();
                using JsonDocument error = JsonDocument.Parse(rejected.Content.OfType<TextContentBlock>().Single().Text);
                await Assert.That(error.RootElement.GetProperty("statusCode").GetString()!
                    .Contains("BadInvalidArgument", StringComparison.Ordinal)).IsTrue();
            }
            finally
            {
                await stopping.CancelAsync().ConfigureAwait(false);
                try
                {
                    await server.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                }
            }
        }
    }
}
