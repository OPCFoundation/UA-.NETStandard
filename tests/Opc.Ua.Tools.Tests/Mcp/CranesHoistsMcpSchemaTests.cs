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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Pins the public crane catalog, parameter contracts and device-changing metadata.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class CranesHoistsMcpSchemaTests
    {
        /// <summary>
        /// A selected package contributes practical grouped tools, not a tool for each generated field.
        /// </summary>
        [TestCase(McpToolProfile.CranesHoists)]
        [TestCase(McpToolProfile.Full)]
        public void SelectedProfilesExposeTheBoundedCatalog(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCranesHoists();
            services.AddMcpServer().WithOpcUaCranesHoistsTools(profile);
            using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That(names.Where(name => name.StartsWith("craneshoists_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_names));
            Assert.That(names, Does.Contain("Connect"));
            Assert.That(names, Does.Contain("ConnectJwt"));
            Assert.That(names, Is.Unique);
        }

        /// <summary>
        /// Passing another family's or a future profile is an ordinary exclusion, not an adapter error.
        /// </summary>
        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.Services)]
        [TestCase(McpToolProfile.Administration)]
        [TestCase(McpToolProfile.PubSub)]
        [TestCase(McpToolProfile.Diagnostics)]
        [TestCase(McpToolProfile.Robotics)]
        [TestCase(McpToolProfile.Vision)]
        [TestCase(McpToolProfile.MachineTool)]
        [TestCase(McpToolProfile.Gms)]
        [TestCase(McpToolProfile.AutoId)]
        [TestCase((McpToolProfile)99)]
        public void UnselectedProfilesAddNothing(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaCranesHoistsTools(profile);
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        /// <summary>
        /// A composed host has one connection catalog and one crane catalog despite repeat registration.
        /// </summary>
        [TestCase("core,craneshoists")]
        [TestCase("vision,robotics,craneshoists")]
        [TestCase("full")]
        public void ComposedRegistrationDeduplicatesConnectionsAndCraneTools(string selected)
        {
            var services = new ServiceCollection();
            McpToolProfileSet profiles = McpToolProfileSet.Parse(selected);
            services.AddMcpServer().WithOpcUaCoreTools(profiles)
                .WithOpcUaCranesHoistsTools(profiles)
                .WithOpcUaCranesHoistsTools(McpToolProfile.CranesHoists)
                .WithOpcUaCranesHoistsTools(McpToolProfile.Full)
                .WithOpcUaConnectionTools();
            using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That(names, Is.Unique);
            Assert.That(names, Does.Contain("Connect"));
            Assert.That(names, Does.Contain("ConnectJwt"));
            Assert.That(names.Where(name => name.StartsWith("craneshoists_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_names));
        }

        /// <summary>
        /// A composed catalog that excludes cranes remains unchanged.
        /// </summary>
        [Test]
        public void UnselectedCompositionAddsNoCraneTools()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaCranesHoistsTools(McpToolProfileSet.Parse("vision,robotics"));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        /// <summary>
        /// The parent-owned host invokes both concrete overloads and the same accessor without catalog collisions.
        /// </summary>
        [TestCase("craneshoists", true)]
        [TestCase("core,craneshoists", true)]
        [TestCase("full", true)]
        [TestCase("core", false)]
        [TestCase("vision,robotics", false)]
        public async Task RealHostRegistrationSelectsCraneToolsWithoutDuplicateConnections(string selected, bool crane)
        {
            var services = new ServiceCollection();
            McpToolProfileSet profiles = McpToolProfileSet.Parse(selected);
            McpHostBuilder.ConfigureServices(services, new PcapOptions(), new OpcUaMcpOptions
            {
                ToolProfiles = profiles
            });
            IMcpServerBuilder builder = services.AddMcpServer();
            if (profiles.Count == 1)
            {
                McpHostBuilder.ConfigureMcpTools(builder, profiles.Enumerate().Single(),
                    diagnosticsToolsEnabled: false);
            }
            else
            {
                McpHostBuilder.ConfigureMcpTools(builder, profiles, diagnosticsToolsEnabled: false);
            }
            await using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That(names, Is.Unique);
            Assert.That(names.Count(name => name == "Connect"), Is.EqualTo(1));
            Assert.That(names.Count(name => name == "ConnectJwt"), Is.EqualTo(1));
            Assert.That(names.Count(name => name.StartsWith("craneshoists_", StringComparison.Ordinal)),
                Is.EqualTo(crane ? 6 : 0));
            Assert.That(provider.GetRequiredService<CranesHoistsClientAccessor>(), Is.Not.Null);
        }

        /// <summary>
        /// The embedding registration is idempotent and respects the host's injected discovery limits.
        /// </summary>
        [Test]
        public void ServicesPreserveConfiguredOptions()
        {
            var services = new ServiceCollection();
            var options = new CranesHoistsClientOptions { MaximumNodes = 16 };
            services.AddSingleton(options);
            services.AddOpcUaMcpCranesHoists().AddOpcUaMcpCranesHoists();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<CranesHoistsClientOptions>(), Is.SameAs(options));
            Assert.That(services.Count(service => service.ServiceType == typeof(CranesHoistsClientAccessor)),
                Is.EqualTo(1));
            Assert.That(provider.GetRequiredService<TimeProvider>(), Is.SameAs(TimeProvider.System));
        }

        /// <summary>
        /// Every public embedding entry point rejects a null host.
        /// </summary>
        [Test]
        public void EmbeddingRejectsNullInputs()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaMcpCranesHoistsExtensions.AddOpcUaMcpCranesHoists(null!));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaMcpCranesHoistsExtensions.WithOpcUaCranesHoistsTools(null!, McpToolProfile.CranesHoists));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaMcpCranesHoistsExtensions.WithOpcUaCranesHoistsTools(null!,
                    new McpToolProfileSet(McpToolProfile.CranesHoists)));
            Assert.Throws<ArgumentNullException>(() => new CranesHoistsClientAccessor(null!));
        }

        /// <summary>
        /// The listed MCP schemas describe every actual parameter and accurately mark configuration side effects.
        /// </summary>
        [Test]
        public async Task ListedSchemasCarryDescriptionsBoundsAndDeviceChangingMetadata()
        {
            await using CranesHoistsMcpHarness harness = await CranesHoistsMcpHarness.CreateAsync()
                .ConfigureAwait(false);
            ListToolsResult result = await harness.Client.ListToolsAsync(new ListToolsRequestParams())
                .ConfigureAwait(false);
            foreach (Tool tool in result.Tools.Where(tool =>
                tool.Name.StartsWith("craneshoists_", StringComparison.Ordinal)))
            {
                JsonElement properties = tool.InputSchema.GetProperty("properties");
                Assert.That(properties.TryGetProperty("accessor", out _), Is.False, tool.Name);
                Assert.That(properties.TryGetProperty("ct", out _), Is.False, tool.Name);
                Assert.That(properties.TryGetProperty("sessionName", out _), Is.True, tool.Name);
                foreach (JsonProperty parameter in properties.EnumerateObject())
                {
                    Assert.That(parameter.Value.GetProperty("description").GetString(),
                        Has.Length.GreaterThan(20), $"{tool.Name}.{parameter.Name}");
                    if (parameter.Name.EndsWith("NodeId", StringComparison.Ordinal))
                    {
                        Assert.That(parameter.Value.GetProperty("minLength").GetInt32(), Is.EqualTo(1));
                        Assert.That(parameter.Value.GetProperty("maxLength").GetInt32(), Is.EqualTo(1024));
                    }
                }
                bool writes = tool.Name == "craneshoists_configure_limit";
                Assert.That(tool.Annotations!.ReadOnlyHint, Is.EqualTo(!writes), tool.Name);
                Assert.That(tool.Annotations.DestructiveHint, Is.EqualTo(writes), tool.Name);
                if (writes)
                {
                    Assert.That(tool.Annotations.IdempotentHint, Is.False);
                    Assert.That(properties.GetProperty("confirmConfiguration").GetProperty("default").GetBoolean(),
                        Is.False);
                    Assert.That(properties.GetProperty("signal").GetProperty("enum")
                        .EnumerateArray().Select(value => value.GetString()), Is.EquivalentTo(s_requests));
                    Assert.That(properties.GetProperty("value").GetProperty("anyOf").GetArrayLength(), Is.EqualTo(2));
                    JsonElement condition = tool.InputSchema.GetProperty("allOf")[0];
                    Assert.That(condition.GetProperty("then").GetProperty("properties").GetProperty("value")
                        .GetProperty("type").GetString(), Is.EqualTo("number"));
                    Assert.That(condition.GetProperty("else").GetProperty("properties").GetProperty("value")
                        .GetProperty("type").GetString(), Is.EqualTo("boolean"));
                }
                else
                {
                    JsonElement bound = properties.GetProperty("maximumResponseBytes");
                    Assert.That(bound.GetProperty("minimum").GetInt32(), Is.EqualTo(1024));
                    Assert.That(bound.GetProperty("maximum").GetInt32(), Is.EqualTo(1048576));
                }
                if (tool.Name == "craneshoists_observe_values")
                {
                    JsonElement nodes = properties.GetProperty("nodeIds");
                    Assert.That(nodes.GetProperty("type").GetString(), Is.EqualTo("array"));
                    Assert.That(nodes.GetProperty("minItems").GetInt32(), Is.EqualTo(1));
                    Assert.That(nodes.GetProperty("maxItems").GetInt32(), Is.EqualTo(64));
                    Assert.That(nodes.GetProperty("uniqueItems").GetBoolean(), Is.True);
                    Assert.That(nodes.GetProperty("items").GetProperty("maxLength").GetInt32(), Is.EqualTo(1024));
                }
                if (tool.Name.StartsWith("craneshoists_observe_", StringComparison.Ordinal))
                {
                    Assert.That(properties.GetProperty("durationSeconds").GetProperty("maximum").GetInt32(),
                        Is.EqualTo(60));
                    Assert.That(properties.GetProperty("maximumNotifications").GetProperty("maximum").GetInt32(),
                        Is.EqualTo(100));
                }
            }
            Assert.That(result.Tools.Select(tool => tool.Name), Is.Unique);
            Assert.That(result.Tools.Count(tool => tool.Name.StartsWith("craneshoists_", StringComparison.Ordinal)),
                Is.EqualTo(6));
        }

        /// <summary>The complete bounded crane catalog.</summary>
        private static readonly string[] s_names =
        [
            "craneshoists_list_systems",
            "craneshoists_read_system",
            "craneshoists_read_element",
            "craneshoists_observe_values",
            "craneshoists_observe_events",
            "craneshoists_configure_limit"
        ];

        /// <summary>The only permitted standard configuration request selectors.</summary>
        private static readonly string[] s_requests =
        [
            "SpeedLimitDirMinusSetpoint", "SpeedLimitDirPlusSetpoint",
            "SpeedLimitEnabledDirMinus", "SpeedLimitEnabledDirPlus", "SpeedLimitGlobalEnabled"
        ];
    }
}
#endif
