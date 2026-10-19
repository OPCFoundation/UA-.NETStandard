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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Pins the new profile catalogues and host-only dependency policy without starting a UA server.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WotProfileIntegrationTests
    {
        /// <summary>
        /// The document profile has exactly the five approved tools and does not require a session.
        /// </summary>
        [Test]
        public async Task DocumentProfileHasExactlyFiveToolsWithoutConnectionsAsync()
        {
            string[] names = await NamesAsync(McpToolProfile.WotDocuments).ConfigureAwait(false);

            Assert.That(names, Is.EquivalentTo(s_documentToolNames));
        }

        /// <summary>
        /// The three new profiles compose as an exact union with one shared set of connection tools.
        /// </summary>
        [Test]
        public async Task WotRegistryCompositionContainsThirtyFiveDomainToolsAsync()
        {
            string[] names = await NamesAsync(McpToolProfileSet.Parse("wot,xregistry,wot-documents"))
                .ConfigureAwait(false);

            Assert.That(names, Has.Length.EqualTo(40));
            Assert.That(names, Is.Unique);
            Assert.That(names.Count(name => name.StartsWith("wot_", StringComparison.Ordinal)), Is.EqualTo(21));
            Assert.That(names.Count(name => name.StartsWith("xregistry_", StringComparison.Ordinal)), Is.EqualTo(14));
            Assert.That(names.Count(name => name == "Connect"), Is.EqualTo(1));
            Assert.That(names.Count(name => name == "ConnectCertificate"), Is.EqualTo(1));
            Assert.That(names.Count(name => name == "Disconnect"), Is.EqualTo(1));
            Assert.That(names.Count(name => name == "GetEndpoints"), Is.EqualTo(1));
            Assert.That(names.Count(name => name == "GetConnectionStatus"), Is.EqualTo(1));
        }

        /// <summary>
        /// A bounded existing profile does not silently acquire any WoT or registry tools.
        /// </summary>
        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.Services)]
        [TestCase(McpToolProfile.Administration)]
        [TestCase(McpToolProfile.PubSub)]
        [TestCase(McpToolProfile.Diagnostics)]
        public async Task ExistingProfilesRemainIsolatedAsync(McpToolProfile profile)
        {
            string[] names = await NamesAsync(profile).ConfigureAwait(false);

            Assert.That(names, Has.None.StartsWith("wot_"));
            Assert.That(names, Has.None.StartsWith("xregistry_"));
        }

        /// <summary>
        /// The full profile includes every new tool without duplicate registrations.
        /// </summary>
        [Test]
        public async Task FullProfileIncludesNewCataloguesExactlyOnceAsync()
        {
            string[] added = await NamesAsync(McpToolProfileSet.Parse("wot,xregistry,wot-documents"))
                .ConfigureAwait(false);
            string[] full = await NamesAsync(McpToolProfile.Full).ConfigureAwait(false);

            Assert.That(full, Is.SupersetOf(added));
            Assert.That(full, Is.Unique);
        }

        /// <summary>
        /// Default document policy enables neither remote origins nor private network addresses.
        /// </summary>
        [Test]
        public void DocumentPolicyDefaultsToNoRemoteEgress()
        {
            var options = new WotMcpOptions();

            Assert.That(options.AllowedDependencyOrigins.IsEmpty, Is.True);
            Assert.That(options.AllowPrivateDependencyAddresses, Is.False);
            Assert.That(options.MaxDocumentBytes, Is.EqualTo(16 * 1024 * 1024));
            Assert.That(options.MaxTotalBytes, Is.EqualTo(64L * 1024 * 1024));
            Assert.That(options.MaxDocuments, Is.EqualTo(256));
            Assert.That(options.MaxResolverDepth, Is.EqualTo(16));
            Assert.That(options.OperationTimeoutMilliseconds, Is.EqualTo(30_000));
        }

        /// <summary>
        /// Named host configuration controls origins and lowers processing bounds independently of profile selection.
        /// </summary>
        [Test]
        public void DocumentPolicyBindsExplicitOriginsAndLimits()
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
            {
                ["McpServer:Wot:AllowedDependencyOrigins:0"] = "https://example.org",
                ["McpServer:Wot:AllowedDependencyOrigins:1"] = "https://models.example.org:8443",
                ["McpServer:Wot:AllowPrivateDependencyAddresses"] = "false",
                ["McpServer:Wot:MaxDocumentBytes"] = "4096",
                ["McpServer:Wot:MaxTotalBytes"] = "16384",
                ["McpServer:Wot:MaxDocuments"] = "4",
                ["McpServer:Wot:MaxResolverDepth"] = "2",
                ["McpServer:Wot:OperationTimeoutMilliseconds"] = "5000",
                ["McpServer:Wot:MaxConcurrentOperations"] = "1"
            }).Build();

            WotMcpOptions options = OpcUaMcpWotConExtensions.CreateWotMcpOptions(configuration);

            Assert.That(options.AllowedDependencyOrigins.ToArray(),
                Is.EqualTo(s_dependencyOrigins));
            Assert.That(options.AllowPrivateDependencyAddresses, Is.False);
            Assert.That(options.MaxDocumentBytes, Is.EqualTo(4096));
            Assert.That(options.MaxTotalBytes, Is.EqualTo(16384));
            Assert.That(options.MaxDocuments, Is.EqualTo(4));
            Assert.That(options.MaxResolverDepth, Is.EqualTo(2));
            Assert.That(options.OperationTimeoutMilliseconds, Is.EqualTo(5000));
            Assert.That(options.MaxConcurrentOperations, Is.EqualTo(1));
        }

        /// <summary>
        /// Invalid host policy is an explicit configuration error, not a fallback to permissive defaults.
        /// </summary>
        [TestCase("MaxDocumentBytes", "wrong")]
        [TestCase("MaxTotalBytes", "wrong")]
        [TestCase("AllowPrivateDependencyAddresses", "maybe")]
        public void InvalidDocumentPolicyDoesNotFallBack(string property, string value)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
            {
                ["McpServer:Wot:AllowedDependencyOrigins:0"] = "https://example.org",
                ["McpServer:Wot:" + property] = value
            }).Build();

            Assert.That(() => OpcUaMcpWotConExtensions.CreateWotMcpOptions(configuration),
                Throws.InvalidOperationException.With.Message.Contains(property));
        }

        /// <summary>
        /// Tool registration preserves an embedding host's explicitly registered policy.
        /// </summary>
        [Test]
        public async Task RepeatedRegistrationPreservesHostPolicyAsync()
        {
            var options = new WotMcpOptions { MaxDocumentBytes = 2048 };
            var services = new ServiceCollection();
            services.AddSingleton(options);
            services.AddOpcUaMcpCore().AddOpcUaMcpWotCon().AddOpcUaMcpWotCon();
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<WotMcpOptions>(), Is.SameAs(options));
            Assert.That(provider.GetServices<WotMcpOptions>().ToArray(), Has.Length.EqualTo(1));
        }

        /// <summary>
        /// Actual SDK schemas describe bounded arrays and exactly one content source without changing other tools.
        /// </summary>
        [Test]
        public async Task DocumentAndRefreshSchemasMatchRuntimeContractsAsync()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaWotConTools(McpToolProfileSet.Parse("wot,wot-documents"));
            await using ServiceProvider provider = services.BuildServiceProvider();
            Tool[] tools = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool)];
            Tool unchanged = tools.Single(tool => tool.Name == "wot_create_asset");
            string original = unchanged.InputSchema.GetRawText();
            var response = new ListToolsResult { Tools = tools };
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> filter =
                WotMcpFilters.AddInputSchemas((_, _) => ValueTask.FromResult(response));

            Assert.That(await filter(null!, CancellationToken.None).ConfigureAwait(false), Is.SameAs(response));
            foreach (string name in s_documentToolNames)
            {
                JsonElement properties = tools.Single(tool => tool.Name == name).InputSchema
                    .GetProperty("properties");
                Assert.That(properties.TryGetProperty("input", out _), Is.False);
                Assert.That(properties.GetProperty("options").GetProperty("properties")
                    .TryGetProperty("strictAuthoring", out _), Is.True);
                JsonElement source = properties.GetProperty("source");
                Assert.That(source.GetProperty("oneOf").GetArrayLength(), Is.EqualTo(2));
                Assert.That(source.GetProperty("oneOf")[0].GetProperty("required")[0].GetString(), Is.EqualTo("text"));
                Assert.That(source.GetProperty("oneOf")[0].GetProperty("not").GetProperty("required")[0].GetString(),
                    Is.EqualTo("filePath"));
                Assert.That(source.GetProperty("properties").GetProperty("text").GetProperty("maxLength").GetInt32(),
                    Is.EqualTo(65_536));
                JsonElement dependencies = properties.GetProperty("dependencies");
                Assert.That(dependencies.GetProperty("type").GetString(), Is.EqualTo("array"));
                Assert.That(dependencies.GetProperty("maxItems").GetInt32(), Is.EqualTo(255));
                Assert.That(dependencies.GetProperty("items").GetProperty("required")[0].GetString(),
                    Is.EqualTo("reference"));
                Assert.That(properties.GetProperty("requiredConformance").GetProperty("maxItems").GetInt32(),
                    Is.EqualTo(64));
            }
            JsonElement selectors = tools.Single(tool => tool.Name == "wot_refresh").InputSchema
                .GetProperty("properties").GetProperty("selection");
            Assert.That(selectors.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(selectors.GetProperty("maxItems").GetInt32(), Is.EqualTo(500));
            Assert.That(selectors.GetProperty("items").GetProperty("properties").TryGetProperty("resourceId", out _),
                Is.True);
            Assert.That(unchanged.InputSchema.GetRawText(), Is.EqualTo(original));
            string[] first = [.. tools.Select(tool => tool.InputSchema.GetRawText())];
            await filter(null!, CancellationToken.None).ConfigureAwait(false);
            Assert.That(tools.Select(tool => tool.InputSchema.GetRawText()), Is.EqualTo(first));
        }

        /// <summary>
        /// Unexpected SDK schema shapes fail explicitly instead of silently advertising an unusable tool.
        /// </summary>
        [TestCase(/*lang=json,strict*/ "{\"type\":\"object\",\"properties\":{}}")]
        [TestCase(/*lang=json,strict*/ "{\"type\":\"object\",\"properties\":{\"input\":{\"type\":\"string\"}}}")]
        public async Task MalformedWotSchemasFailExplicitly(string schema)
        {
            using var document = JsonDocument.Parse(schema);
            var response = new ListToolsResult
            {
                Tools = [new Tool { Name = "wot_refresh", InputSchema = document.RootElement.Clone() }]
            };
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> filter =
                WotMcpFilters.AddInputSchemas((_, _) => ValueTask.FromResult(response));

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await filter(null!, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public void NullSchemaHandlerIsRejected()
        {
            Assert.That(() => WotMcpFilters.AddInputSchemas(null!), Throws.ArgumentNullException);
            Assert.That(() => WotMcpFilters.WrapInput(null!), Throws.ArgumentNullException);
        }

        /// <summary>
        /// The advertised flat document shape reaches the original typed method, even before listing tools.
        /// </summary>
        [Test]
        public async Task FlatDocumentToolBindsWithoutEnvelopeAndRestoresRequestAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore().AddOpcUaMcpWotCon();
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaWotConTools(McpToolProfile.WotDocuments);
            await using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool tool = provider.GetServices<McpServerTool>()
                .Single(value => value.ProtocolTool.Name == "wot_inspect_document");
            using var json = JsonDocument.Parse("""{"source":{"text":"{\"title\":\"flat input\"}"}}""");
            var arguments = json.RootElement.EnumerateObject()
                .ToDictionary(value => value.Name, value => value.Value.Clone());
            var parameters = new CallToolRequestParams { Name = tool.ProtocolTool.Name, Arguments = arguments };
            var request = new RequestContext<CallToolRequestParams>(
                provider.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("flat") }, parameters)
            {
                MatchedPrimitive = tool
            };

            CallToolResult result = await tool.InvokeAsync(request).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False, result.StructuredContent?.GetRawText());
            Assert.That(result.StructuredContent!.Value.GetProperty("documents")[0].GetProperty("title").GetString(),
                Is.EqualTo("flat input"));
            Assert.That(parameters.Arguments, Is.SameAs(arguments));
            foreach (string invalid in s_invalidOptions)
            {
                using var options = JsonDocument.Parse(invalid);
                arguments["options"] = options.RootElement.Clone();
                CallToolResult refused = await tool.InvokeAsync(request).ConfigureAwait(false);
                Assert.That(refused.IsError, Is.True, invalid);
                Assert.That(refused.StructuredContent!.Value.GetProperty("errorType").GetString(),
                    Is.EqualTo(nameof(ArgumentException)), invalid);
                Assert.That(parameters.Arguments, Is.SameAs(arguments));
                Assert.That(parameters.Arguments.ContainsKey("input"), Is.False);
            }
            arguments.Remove("options");
            Assert.That(parameters.Arguments.ContainsKey("input"), Is.False);
            arguments["unexpected"] = json.RootElement.Clone();
            CallToolResult unknown = await tool.InvokeAsync(request).ConfigureAwait(false);
            Assert.That(unknown.IsError, Is.True);
            Assert.That(unknown.StructuredContent!.Value.GetProperty("message").GetString(),
                Does.Contain("unexpected"));
            Assert.That(parameters.Arguments, Is.SameAs(arguments));
        }

        /// <summary>
        /// Resolves the real host catalogue without running a network listener.
        /// </summary>
        private static async Task<string[]> NamesAsync(McpToolProfileSet profiles)
        {
            var services = new ServiceCollection();
            McpHostBuilder.ConfigureServices(services, new PcapOptions(), new OpcUaMcpOptions());
            IMcpServerBuilder builder = services.AddMcpServer();
            McpHostBuilder.ConfigureMcpTools(builder, profiles, false);
            await using ServiceProvider provider = services.BuildServiceProvider();
            return [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
        }

        private static readonly string[] s_documentToolNames =
        [
            "wot_inspect_document",
            "wot_validate_document",
            "wot_plan_bindings",
            "wot_to_nodeset",
            "wot_from_nodeset"
        ];

        private static readonly string[] s_dependencyOrigins =
        [
            "https://example.org",
            "https://models.example.org:8443"
        ];

        private static readonly string[] s_invalidOptions =
        [
            "null",
            "[]",
            """{"unknown":true}""",
            """{"strictAuthoring":false,"strictAuthoring":true}"""
        ];
    }
}
#endif
