// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the bounded crane catalog with JSON reflection disabled, including its shared connection tools.
    /// </summary>
    public sealed class CranesHoistsMcpAotTests
    {
        /// <summary>
        /// Resolves actual SDK tool schemas rather than inferring native compatibility from an assembly annotation.
        /// </summary>
        [Test]
        public async Task CraneCatalogRegistersWithoutJsonReflectionAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore().AddOpcUaMcpCranesHoists();
            services.AddMcpServer().WithOpcUaMcpFilters()
                .WithOpcUaCranesHoistsTools(McpToolProfile.CranesHoists);
            await using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool[] tools = [.. provider.GetServices<McpServerTool>()];
            string[] names = [.. tools.Select(tool => tool.ProtocolTool.Name)];

            await Assert.That(names.Contains("craneshoists_read_system", StringComparer.Ordinal)).IsTrue();
            await Assert.That(names.Contains("craneshoists_configure_limit", StringComparer.Ordinal)).IsTrue();
            await Assert.That(names.Contains("Connect", StringComparer.Ordinal)).IsTrue();
            await Assert.That(names.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(names.Length);
        }
    }
}
