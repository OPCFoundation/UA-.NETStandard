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
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Pins the approved industrial catalog, composition and host configuration contracts.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpIndustrialProfileTests
    {
        /// <summary>
        /// A standalone family is usable without silently enabling another family's tools.
        /// </summary>
        [TestCase(McpToolProfile.Amb, "amb_")]
        [TestCase(McpToolProfile.Machinery, "machinery_")]
        [TestCase(McpToolProfile.Scales, "scales_")]
        [TestCase(McpToolProfile.Pumps, "pumps_")]
        [TestCase(McpToolProfile.Di, "di_")]
        [TestCase(McpToolProfile.Isa95, "isa95_")]
        [TestCase(McpToolProfile.Positioning, "positioning_")]
        public void CompanionProfilesContributeOnlyOwnCatalogAndConnections(McpToolProfile profile, string prefix)
        {
            McpServerTool[] tools = ResolveTools(new McpToolProfileSet(profile));
            string[] names = tools.Select(tool => tool.ProtocolTool.Name).ToArray();

            Assert.That(names.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(names.Length));
            Assert.That(names.Count(name => name == "Connect"), Is.EqualTo(1));
            Assert.That(names.Count(name => name == "Disconnect"), Is.EqualTo(1));
            Assert.That(names.Count(name => name == "GetEndpoints"), Is.EqualTo(1));
            Assert.That(names.Count(name => name == "GetConnectionStatus"), Is.EqualTo(1));
            Assert.That(names.Count(name => name.StartsWith(prefix, StringComparison.Ordinal)), Is.GreaterThan(1));
            Assert.That(names.Where(name => !kConnections.Contains(name, StringComparer.Ordinal)),
                Has.All.StartsWith(prefix));

            foreach (McpServerTool tool in tools.Where(tool =>
                tool.ProtocolTool.Name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                JsonElement schema = tool.ProtocolTool.InputSchema;
                Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"));
                Assert.That(schema.GetProperty("properties").TryGetProperty("sessionName", out _), Is.True,
                    $"{tool.ProtocolTool.Name} must be scoped to a named session.");
                Assert.That(schema.GetProperty("properties").TryGetProperty("accessor", out _), Is.False,
                    "Injected dependencies must not be exposed as tool arguments.");
            }
        }

        /// <summary>
        /// Composing profiles exposes their exact union and never duplicates registrations.
        /// </summary>
        [TestCase("machinery,isa95")]
        [TestCase("scales,di")]
        [TestCase("amb,pumps,machinery")]
        [TestCase("positioning,vision,robotics")]
        [TestCase("full,di,amb")]
        public void IndustrialCompositionsAreExactUnionsWithoutDuplicates(string profiles)
        {
            McpToolProfileSet set = McpToolProfileSet.Parse(profiles);
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (McpToolProfile profile in set.Enumerate())
            {
                expected.UnionWith(ResolveTools(new McpToolProfileSet(profile))
                    .Select(tool => tool.ProtocolTool.Name));
            }
            string[] actual = ResolveTools(set).Select(tool => tool.ProtocolTool.Name).ToArray();

            Assert.That(actual, Is.EquivalentTo(expected));
            Assert.That(actual.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(actual.Length));
        }

        /// <summary>
        /// The complete additive catalog remains inside the reviewed budget.
        /// </summary>
        [Test]
        public void FullIncludesAllIndustrialToolsWithinApprovedBudget()
        {
            var selected = McpToolProfileSet.Parse("amb,machinery,scales,pumps,di,isa95,positioning");
            string[] industrial = ResolveTools(selected)
                .Select(tool => tool.ProtocolTool.Name)
                .Where(name => !kConnections.Contains(name, StringComparer.Ordinal))
                .ToArray();
            string[] full = ResolveTools(new McpToolProfileSet(McpToolProfile.Full))
                .Select(tool => tool.ProtocolTool.Name)
                .ToArray();

            Assert.That(industrial, Has.Length.InRange(7, 120));
            Assert.That(full, Is.SupersetOf(industrial));
            Assert.That(full.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(full.Length));
        }

        /// <summary>
        /// Adding profiles never changes persisted values of existing public enum members.
        /// </summary>
        [Test]
        public void ExistingProfileNumericValuesRemainStable()
        {
            Assert.That((int)McpToolProfile.Core, Is.Zero);
            Assert.That((int)McpToolProfile.Services, Is.EqualTo(1));
            Assert.That((int)McpToolProfile.Administration, Is.EqualTo(2));
            Assert.That((int)McpToolProfile.PubSub, Is.EqualTo(3));
            Assert.That((int)McpToolProfile.Diagnostics, Is.EqualTo(4));
            Assert.That((int)McpToolProfile.Robotics, Is.EqualTo(5));
            Assert.That((int)McpToolProfile.Vision, Is.EqualTo(6));
            Assert.That((int)McpToolProfile.Full, Is.EqualTo(7));
        }

        /// <summary>
        /// CLI profile overrides do not bypass transfer policy binding.
        /// </summary>
        [TestCase(null)]
        [TestCase("scales,di")]
        public void TransferPolicyBindsAlongsideProfileSelection(string? cliProfile)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:TransferRoot"] = "host-transfer-root",
                    ["McpServer:MaxTransferBytes"] = "4096",
                    ["McpServer:ToolProfile"] = "amb"
                })
                .Build();

            OpcUaMcpOptions options = McpHostBuilder.CreateOpcUaMcpOptions(configuration, cliProfile);

            Assert.That(options.TransferRoot, Is.EqualTo("host-transfer-root"));
            Assert.That(options.MaxTransferBytes, Is.EqualTo(4096));
            Assert.That(options.EffectiveToolProfiles,
                Is.EqualTo(McpToolProfileSet.Parse(cliProfile ?? "amb")));
        }

        /// <summary>
        /// Invalid host byte limits fail explicitly instead of removing the limit.
        /// </summary>
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("1.5")]
        [TestCase("")]
        [TestCase("9223372036854775808")]
        public void InvalidConfiguredTransferLimitsAreRejected(string limit)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServer:MaxTransferBytes"] = limit
                })
                .Build();

            Assert.That(() => McpHostBuilder.CreateOpcUaMcpOptions(configuration, "amb"),
                Throws.InvalidOperationException.With.Message.Contains("MaxTransferBytes"));
        }

        /// <summary>
        /// Resolves actual registrations, retaining duplicates rather than hiding them in a set.
        /// </summary>
        private static McpServerTool[] ResolveTools(McpToolProfileSet profiles)
        {
            var services = new ServiceCollection();
            McpHostBuilder.ConfigureMcpTools(services.AddMcpServer(), profiles, diagnosticsToolsEnabled: false);
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetServices<McpServerTool>().ToArray();
        }

        /// <summary>
        /// The shared session lifecycle tools.
        /// </summary>
        private static readonly string[] kConnections =
            ["Connect", "Disconnect", "GetEndpoints", "GetConnectionStatus"];
    }
}
#endif
