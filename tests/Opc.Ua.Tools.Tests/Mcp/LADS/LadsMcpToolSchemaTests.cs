// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET10_0_OR_GREATER
using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.McpLads
{
    /// <summary>
    /// Verifies LADS MCP profiles, tool names and generated input schemas without a live endpoint.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class LadsMcpToolSchemaTests
    {
        /// <summary>
        /// The LADS profile exposes each supported read and command tool with described parameters.
        /// </summary>
        [Test]
        public void LadsProfileRegistersBoundedToolCatalog()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpLads();
            services.AddMcpServer().WithOpcUaLadsTools(McpToolProfile.Lads);
            using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool[] tools = [.. provider.GetServices<McpServerTool>()
                .Where(tool => tool.ProtocolTool.Name.StartsWith("lads_", StringComparison.Ordinal))];

            Assert.That(tools.Select(tool => tool.ProtocolTool.Name), Is.EquivalentTo(s_toolNames));
            Assert.That(tools.Select(tool => tool.ProtocolTool.Name), Is.Unique);
            foreach (McpServerTool tool in tools)
            {
                Assert.That(
                    tool.ProtocolTool.InputSchema.TryGetProperty("properties", out JsonElement properties),
                    Is.True,
                    tool.ProtocolTool.InputSchema.ToString());
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    Assert.That(property.Value.GetProperty("description").GetString(),
                        Has.Length.GreaterThan(10), $"{tool.ProtocolTool.Name}.{property.Name}");
                }
            }
        }

        /// <summary>
        /// An unrelated profile does not register LADS tools or connection tools.
        /// </summary>
        [Test]
        public void UnselectedProfileAddsNoTools()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaLadsTools(McpToolProfile.Core);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        /// <summary>
        /// Repeated composition registers one LADS catalog and one connection catalog.
        /// </summary>
        [Test]
        public void ComposedProfilesDoNotDuplicateTools()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            McpToolProfileSet profiles = McpToolProfileSet.Parse("core,lads");
            services.AddMcpServer()
                .WithOpcUaCoreTools(profiles)
                .WithOpcUaLadsTools(profiles)
                .WithOpcUaLadsTools(McpToolProfile.Lads);
            using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];

            Assert.That(names, Is.Unique);
            Assert.That(names.Count(name => name == "Connect"), Is.EqualTo(1));
            Assert.That(names.Count(name => name.StartsWith("lads_", StringComparison.Ordinal)),
                Is.EqualTo(s_toolNames.Length));
        }

        /// <summary>
        /// The public adapter rejects null registration inputs.
        /// </summary>
        [Test]
        public void EmbeddingRejectsNullInputs()
        {
            Assert.Throws<ArgumentNullException>(() => OpcUaMcpLadsExtensions.AddOpcUaMcpLads(null!));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaMcpLadsExtensions.WithOpcUaLadsTools(null!, McpToolProfile.Lads));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaMcpLadsExtensions.WithOpcUaLadsTools(null!, new McpToolProfileSet(McpToolProfile.Lads)));
        }

        private static readonly string[] s_toolNames =
        [
            "lads_list_devices",
            "lads_read_device",
            "lads_read_functional_unit",
            "lads_read_function",
            "lads_list_program_templates",
            "lads_start_program",
            "lads_command",
            "lads_read_results",
            "lads_list_result_files",
            "lads_download_result_file",
            "lads_read_maintenance_tasks",
            "lads_maintenance_command"
        ];
    }
}
#endif
