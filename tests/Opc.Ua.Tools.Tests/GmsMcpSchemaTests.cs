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
using Moq;
using NUnit.Framework;
using Opc.Ua.GMS.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Pins the GMS catalog, side-effect metadata, schema bounds and additive profile behavior.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GmsMcpSchemaTests
    {
        /// <summary>
        /// A selected profile contributes exactly six grouped tools and one shared connection catalog.
        /// </summary>
        [TestCase(McpToolProfile.Gms)]
        [TestCase(McpToolProfile.Full)]
        public void SelectedProfilesExposeSixGroupedTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpGms();
            services.AddMcpServer().WithOpcUaGmsTools(profile);
            using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That(names.Where(name => name.StartsWith("gms_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_names));
            Assert.That(names, Is.Unique);
            Assert.That(names.Count(name => name == "Connect"), Is.EqualTo(1));
            Assert.That(names.Any(name => name.Contains("start", StringComparison.OrdinalIgnoreCase)), Is.False);
        }

        /// <summary>
        /// Other and future profile values remain normal exclusions.
        /// </summary>
        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.Services)]
        [TestCase(McpToolProfile.Administration)]
        [TestCase(McpToolProfile.PubSub)]
        [TestCase(McpToolProfile.Diagnostics)]
        [TestCase(McpToolProfile.Robotics)]
        [TestCase(McpToolProfile.Vision)]
        [TestCase(McpToolProfile.MachineTool)]
        [TestCase(McpToolProfile.CranesHoists)]
        [TestCase((McpToolProfile)99)]
        public void UnselectedProfilesAddNothing(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaGmsTools(profile);
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        /// <summary>
        /// Single, composed, repeated and default Full selection do not duplicate connection or family tools.
        /// </summary>
        [TestCase("core,gms")]
        [TestCase("gms,craneshoists,machinetool")]
        [TestCase("full")]
        public void ComposedRegistrationIsIdempotent(string selection)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpGms();
            var profiles = McpToolProfileSet.Parse(selection);
            services.AddMcpServer().WithOpcUaCoreTools(profiles)
                .WithOpcUaGmsTools(profiles).WithOpcUaGmsTools()
                .WithOpcUaGmsTools(McpToolProfile.Gms).WithOpcUaConnectionTools();
            using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That(names, Is.Unique);
            Assert.That(names.Count(name => name == "Connect"), Is.EqualTo(1));
            Assert.That(names.Where(name => name.StartsWith("gms_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_names));
        }

        /// <summary>
        /// Set-based exclusion does not silently enable the package.
        /// </summary>
        [Test]
        public void UnselectedCompositionAddsNothing()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaGmsTools(McpToolProfileSet.Parse("vision,robotics"));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        /// <summary>
        /// The parent-owned executable host invokes the correct concrete profile overloads.
        /// </summary>
        [TestCase("gms", true)]
        [TestCase("core,gms", true)]
        [TestCase("full", true)]
        [TestCase("core", false)]
        [TestCase("vision,robotics", false)]
        public async Task HostCompositionSelectsTheExpectedCatalog(string selected, bool expected)
        {
            var services = new ServiceCollection();
            var profiles = McpToolProfileSet.Parse(selected);
            McpHostBuilder.ConfigureServices(
                services, new PcapOptions(), new OpcUaMcpOptions { ToolProfiles = profiles });
            IMcpServerBuilder builder = services.AddMcpServer();
            if (profiles.Count == 1)
            {
                McpHostBuilder.ConfigureMcpTools(
                    builder, profiles.Enumerate().Single(), diagnosticsToolsEnabled: false);
            }
            else
            {
                McpHostBuilder.ConfigureMcpTools(builder, profiles, diagnosticsToolsEnabled: false);
            }
            await using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That(names, Is.Unique);
            Assert.That(names.Count(name => name == "Connect"), Is.EqualTo(1));
            Assert.That(names.Count(name => name.StartsWith("gms_", StringComparison.Ordinal)),
                Is.EqualTo(expected ? 6 : 0));
            Assert.That(provider.GetRequiredService<GmsClientAccessor>(), Is.Not.Null);
        }

        /// <summary>
        /// DI registration retains supplied bounds and clock instead of silently replacing host configuration.
        /// </summary>
        [Test]
        public void ServicesPreserveOptionsAndRegisterOnlyOnce()
        {
            var services = new ServiceCollection();
            var options = new GmsClientOptions { MaximumElements = 3 };
            services.AddSingleton(options);
            services.AddOpcUaMcpGms().AddOpcUaMcpGms();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<GmsClientOptions>(), Is.SameAs(options));
            Assert.That(provider.GetRequiredService<TimeProvider>(), Is.SameAs(TimeProvider.System));
            Assert.That(services.Count(service => service.ServiceType == typeof(GmsClientAccessor)), Is.EqualTo(1));
        }

        /// <summary>
        /// A supplied observation clock is not silently replaced by the package's default clock.
        /// </summary>
        [Test]
        public void ServicesPreserveTheInjectedClock()
        {
            TimeProvider clock = new Mock<TimeProvider>().Object;
            var services = new ServiceCollection();
            services.AddSingleton(clock);
            services.AddOpcUaMcpGms();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<TimeProvider>(), Is.SameAs(clock));
        }

        /// <summary>
        /// Null embedding hosts and session accessors fail at the public API boundary.
        /// </summary>
        [Test]
        public void EmbeddingRejectsNullInputs()
        {
            Assert.Throws<ArgumentNullException>(() => OpcUaMcpGmsExtensions.AddOpcUaMcpGms(null!));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaMcpGmsExtensions.WithOpcUaGmsTools(null!, McpToolProfile.Gms));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaMcpGmsExtensions.WithOpcUaGmsTools(null!, new McpToolProfileSet(McpToolProfile.Gms)));
            Assert.Throws<ArgumentNullException>(() => new GmsClientAccessor(null!));
            Assert.Throws<ArgumentNullException>(() => GmsMcpFilters.AddSchemas(null!));
        }

        /// <summary>
        /// Real listed schemas expose native arrays, exact selectors and hard limits, with truthful annotations.
        /// </summary>
        [Test]
        public async Task ListedSchemasDescribeEveryWireParameterAndSemanticBound()
        {
            await using GmsMcpHarness harness = await GmsMcpHarness.CreateAsync().ConfigureAwait(false);
            ListToolsResult listed = await harness.Client.ListToolsAsync(new ListToolsRequestParams())
                .ConfigureAwait(false);
            Tool[] tools = [.. listed.Tools.Where(tool => tool.Name.StartsWith("gms_", StringComparison.Ordinal))];
            Assert.That(tools.Select(tool => tool.Name), Is.EquivalentTo(s_names));
            foreach (Tool tool in tools)
            {
                JsonElement properties = tool.InputSchema.GetProperty("properties");
                Assert.That(properties.TryGetProperty("accessor", out _), Is.False);
                Assert.That(properties.TryGetProperty("ct", out _), Is.False);
                Assert.That(properties.EnumerateObject().Count(), Is.LessThanOrEqualTo(8), tool.Name);
                Assert.That(tool.InputSchema.GetProperty("additionalProperties").GetBoolean(), Is.False);
                if (tool.Name is "gms_read_system" or "gms_read_results" or "gms_observe")
                {
                    Assert.That(tool.Description, Does.Contain("Returns "), tool.Name);
                }
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    Assert.That(property.Value.GetProperty("description").GetString(),
                        Has.Length.GreaterThan(20), tool.Name + "." + property.Name);
                }
                bool writes = tool.Name == "gms_acknowledge_results";
                Assert.That(tool.Annotations!.ReadOnlyHint, Is.EqualTo(!writes), tool.Name);
                Assert.That(tool.Annotations.DestructiveHint, Is.EqualTo(writes), tool.Name);
                JsonElement bytes = properties.GetProperty("maximumResponseBytes");
                Assert.That(bytes.GetProperty("minimum").GetInt32(), Is.EqualTo(1024));
                Assert.That(bytes.GetProperty("maximum").GetInt32(), Is.EqualTo(1048576));
                Assert.That(bytes.GetProperty("default").GetInt32(), Is.EqualTo(131072));
            }
            JsonElement read = tools.Single(tool => tool.Name == "gms_read_system")
                .InputSchema.GetProperty("properties");
            Assert.That(read.GetProperty("section").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()),
                Is.EquivalentTo(s_sections));
            JsonElement results = tools.Single(tool => tool.Name == "gms_read_results")
                .InputSchema.GetProperty("properties");
            Assert.That(results.GetProperty("operation").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()),
                Is.EquivalentTo(s_operations));
            Assert.That(results.EnumerateObject().Count(), Is.EqualTo(7));
            Assert.That(results.TryGetProperty("filterField", out _), Is.False);
            Assert.That(results.TryGetProperty("filterValue", out _), Is.False);
            Assert.That(results.TryGetProperty("orderBy", out _), Is.False);
            JsonElement query = results.GetProperty("query");
            Assert.That(query.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(query.GetProperty("additionalProperties").GetBoolean(), Is.False);
            JsonElement queryProperties = query.GetProperty("properties");
            Assert.That(queryProperties.GetProperty("filterField").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()),
                Is.EquivalentTo(s_filterFields));
            Assert.That(queryProperties.GetProperty("filterValue").GetProperty("anyOf").GetArrayLength(),
                Is.EqualTo(2));
            Assert.That(queryProperties.GetProperty("filterValue").GetProperty("anyOf")[0]
                .GetProperty("maxLength").GetInt32(), Is.EqualTo(1024));
            foreach (JsonProperty property in queryProperties.EnumerateObject())
            {
                Assert.That(property.Value.GetProperty("description").GetString(), Has.Length.GreaterThan(20));
            }
            Assert.That(query.GetProperty("examples")[0].ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(query.GetProperty("examples")[0].GetProperty("filterField").GetString(), Is.EqualTo("JobId"));
            Assert.That(query.GetProperty("examples")[0].GetProperty("filterValue").GetString(), Is.EqualTo("J-1"));
            JsonElement conditions = tools.Single(tool => tool.Name == "gms_read_results").InputSchema
                .GetProperty("allOf");
            Assert.That(conditions[0].GetProperty("then").GetProperty("required")[0].GetString(),
                Is.EqualTo("resultId"));
            Assert.That(query.GetProperty("allOf")[1].GetProperty("then").GetProperty("properties")
                .GetProperty("filterValue")
                .GetProperty("type").GetString(), Is.EqualTo("boolean"));
            JsonElement observe = tools.Single(tool => tool.Name == "gms_observe")
                .InputSchema.GetProperty("properties");
            Assert.That(observe.GetProperty("durationSeconds").GetProperty("maximum").GetInt32(), Is.EqualTo(60));
            Assert.That(observe.GetProperty("maximumNotifications").GetProperty("maximum").GetInt32(), Is.EqualTo(100));
            JsonElement names = observe.GetProperty("characteristics");
            Assert.That(names.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(names.GetProperty("maxItems").GetInt32(), Is.EqualTo(32));
            Assert.That(names.GetProperty("items").GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(names.GetProperty("items").GetProperty("properties").GetProperty("namespaceIndex")
                .GetProperty("maximum").GetInt32(), Is.EqualTo(65535));
            Assert.That(names.GetProperty("examples")[0].ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(names.GetProperty("examples")[0][0].GetProperty("name").GetString(), Is.EqualTo("Bore"));
            Assert.That(names.GetProperty("examples")[0][0].GetProperty("namespaceIndex").GetInt32(), Is.EqualTo(12));
            JsonElement ack = tools.Single(tool => tool.Name == "gms_acknowledge_results").InputSchema
                .GetProperty("properties");
            Assert.That(ack.GetProperty("resultIds").GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(ack.GetProperty("resultIds").GetProperty("maxItems").GetInt32(), Is.EqualTo(100));
            Assert.That(ack.GetProperty("resultIds").GetProperty("examples")[0].ValueKind,
                Is.EqualTo(JsonValueKind.Array));
            Assert.That(ack.GetProperty("resultIds").GetProperty("examples")[0][0].GetString(), Is.EqualTo("R-1"));
            Assert.That(ack.GetProperty("confirmAcknowledgement").GetProperty("default").GetBoolean(), Is.False);
            JsonElement download = tools.Single(tool => tool.Name == "gms_download_result").InputSchema
                .GetProperty("properties");
            Assert.That(download.GetProperty("maximumDownloadBytes").GetProperty("maximum").GetInt32(),
                Is.EqualTo(524288));
            Assert.That(download.GetProperty("timeoutSeconds").GetProperty("maximum").GetInt32(), Is.EqualTo(60));
        }

        /// <summary>The entire family catalog, with no control or condition-acknowledgement verbs.</summary>
        private static readonly string[] s_names =
        [
            "gms_list_systems", "gms_read_system", "gms_read_results",
            "gms_download_result", "gms_observe", "gms_acknowledge_results"
        ];

        /// <summary>The exact case-sensitive native snapshot selectors.</summary>
        private static readonly string[] s_sections =
            ["System", "Equipment", "Calibration", "Production", "Capabilities"];

        /// <summary>The exact passive result-operation selectors.</summary>
        private static readonly string[] s_operations = ["Latest", "ById", "Query", "Published", "Corrections"];

        /// <summary>The selected standard Machinery metadata fields.</summary>
        private static readonly string[] s_filterFields = ["None", "ResultId", "JobId", "IsPartial"];
    }
}
#endif
