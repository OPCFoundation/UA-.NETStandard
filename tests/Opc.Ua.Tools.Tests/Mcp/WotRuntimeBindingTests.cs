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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies the separate bounded catalogue, real JSON array binding and honest executor capability metadata.
    /// </summary>
    [TestFixture]
    public sealed class WotRuntimeBindingTests
    {
        /// <summary>
        /// Sharing one package must not turn management/document registration into live executor registration.
        /// </summary>
        [Test]
        public async Task UnifiedWotPackageKeepsLiveRegistrationIndependentAsync()
        {
            Assert.That(typeof(WotRuntimeOptions).Assembly, Is.SameAs(typeof(WotDocumentTools).Assembly));
            Assert.That(typeof(WotRuntimeOptions).Assembly.GetName().Name, Is.EqualTo("Opc.Ua.Mcp.WotCon"));
            var services = new ServiceCollection();
            services.AddLogging().AddOpcUaMcpCore().AddOpcUaMcpWotCon();
            services.AddMcpServer()
                .WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaWotConTools(McpToolProfile.Full);
            await using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];

            Assert.That(names, Has.Length.EqualTo(25));
            Assert.That(names, Does.Contain("wot_inspect_document").And.Contain("wot_list_assets"));
            Assert.That(names.Any(name => name.StartsWith("wot_runtime_", StringComparison.Ordinal)), Is.False);
            Assert.That(provider.GetService<IWotRuntimeExecutorFactory>(), Is.Null);
            Assert.That(provider.GetService<WotRuntimeOptions>(), Is.Null);
            Assert.That(provider.GetRequiredService<OpcUaSessionManager>().IsConnected, Is.False);
        }

        [Test]
        public async Task CatalogueContainsOnlyApprovedToolsAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            string[] names = [.. fixture.Services.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];

            Assert.That(names.Where(name => name.StartsWith("wot_runtime_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_expectedTools));
            Assert.That(names, Does.Not.Contain("wot_plan_bindings"));
            Assert.That(names, Does.Not.Contain("wot_inspect_document"));
            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }

        [Test]
        public async Task CapabilitiesDistinguishExecutorsFromPlannersAsync()
        {
            await using var fixture = new WotRuntimeTestContext(realExecutors: true);
            using IDisposable caller = fixture.Enter();
            JsonObject result = WotRuntimeTestContext.Payload(
                await fixture.Tools.CapabilitiesAsync().ConfigureAwait(false));
            JsonArray bindings = result["bindings"]!.AsArray();

            Assert.That(bindings, Has.Count.EqualTo(8));
            Assert.That(bindings.Where(item => item!["executable"]!.GetValue<bool>())
                .Select(item => item!["bindingId"]!.GetValue<string>()),
                Is.EquivalentTo(s_expectedExecutors));
            Assert.That(bindings.Where(item => !item!["executable"]!.GetValue<bool>())
                .All(item => item!["operations"]!.AsArray().Count == 0), Is.True);
            Assert.That(result["automaticMutationRetries"]!.GetValue<bool>(), Is.False);
            fixture.Dns.VerifyNoOtherCalls();
        }

        [Test]
        public async Task InputSchemasHaveTypedArraysAndAtMostEightRootArgumentsAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            foreach (McpServerTool tool in fixture.Services.GetServices<McpServerTool>()
                .Where(tool => tool.ProtocolTool.Name.StartsWith("wot_runtime_", StringComparison.Ordinal)))
            {
                JsonElement root = tool.ProtocolTool.InputSchema.GetProperty("properties");
                Assert.That(root.EnumerateObject().Count(), Is.LessThanOrEqualTo(8), tool.ProtocolTool.Name);
                Assert.That(tool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean(), Is.False);
                foreach (string name in new[] { "operations", "dependencies", "credentials", "inputs" })
                {
                    if (root.TryGetProperty(name, out JsonElement array))
                    {
                        Assert.That(array.GetProperty("type").GetString(), Is.EqualTo("array"), name);
                        Assert.That(array.TryGetProperty("items", out _), Is.True, name);
                        Assert.That(array.GetProperty("maxItems").GetInt32(), Is.GreaterThan(0), name);
                    }
                }
            }
        }

        [Test]
        public async Task RealMcpBindingAcceptsStructuredInputArraysAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            WotRuntimeSelection selected = WotRuntimeTestContext.Selection();
            JsonObject opened = WotRuntimeTestContext.Payload(await fixture.InvokeAsync("wot_runtime_open",
                new JsonObject
                {
                    ["name"] = "bound",
                    ["document"] = new JsonObject { ["text"] = WotRuntimeTestContext.PropertyDocument },
                    ["selection"] = new JsonObject
                    {
                        ["sha256"] = selected.Sha256,
                        ["affordance"] = selected.Affordance,
                        ["bindingId"] = selected.BindingId,
                        ["bindingVersion"] = selected.BindingVersion
                    },
                    ["operations"] = new JsonArray("writeproperty"),
                    ["dependencies"] = new JsonArray(),
                    ["credentials"] = new JsonArray()
                }).ConfigureAwait(false));
            string handle = opened["runtime"]!["handle"]!.GetValue<string>();
            _ = WotRuntimeTestContext.Payload(await fixture.InvokeAsync("wot_runtime_write_property",
                new JsonObject
                {
                    ["handle"] = handle,
                    ["value"] = new JsonObject { ["type"] = "UInt16", ["value"] = new JsonArray(0, 65535) }
                }).ConfigureAwait(false));

            Assert.That(fixture.Factory.Writes.Single().WrappedValue.TryGetValue(out ArrayOf<ushort> values), Is.True);
            Assert.That(values.ToArray(), Is.EqualTo(new ushort[] { 0, 65535 }));
        }

        /// <summary>
        /// Pins the source-generated defaults that the actual SDK-bound open request relies on.
        /// </summary>
        [Test]
        public void SourceGeneratedSelectionPreservesOmittedOptionalIdentityFields()
        {
            string sha256 = WotRuntimeTestContext.Selection().Sha256;
            string json = new JsonObject
            {
                ["sha256"] = sha256,
                ["affordance"] = "temperature",
                ["bindingId"] = "w3c.http",
                ["bindingVersion"] = "1.1"
            }.ToJsonString();
            WotRuntimeSelection? selection = JsonSerializer.Deserialize(
                json, WotRuntimeInputJsonContext.Default.WotRuntimeSelection);

            Assert.That(selection, Is.Not.Null);
            Assert.That(selection!.DocumentReference, Is.EqualTo(string.Empty));
            Assert.That(selection.Kind, Is.EqualTo(WotCon.Bindings.WotAffordanceKind.Property));
            Assert.That(selection.FormIndex, Is.Zero);
            Assert.That(selection.Sha256, Is.EqualTo(sha256));
            Assert.That(selection.Affordance, Is.EqualTo("temperature"));
            Assert.That(selection.BindingId, Is.EqualTo("w3c.http"));
            Assert.That(selection.BindingVersion, Is.EqualTo("1.1"));
        }

        /// <summary>
        /// Empty options retain the same non-fetching logical base as omitted options and direct construction.
        /// </summary>
        [Test]
        public void SourceGeneratedEmptyOpenOptionsPreserveLocalPreparationDefaults()
        {
            WotRuntimeOpenOptions? options = JsonSerializer.Deserialize(
                "{}", WotRuntimeInputJsonContext.Default.WotRuntimeOpenOptions);

            Assert.That(options, Is.Not.Null);
            Assert.That(options!.BaseUri, Is.EqualTo("https://mcp.invalid/root.json"));
            Assert.That(options.DocumentSetArchive, Is.False);
            Assert.That(options.SessionName, Is.Null);
        }

        [Test]
        public async Task HostDisabledCatalogueIsHiddenAsync()
        {
            await using var fixture = new WotRuntimeTestContext(options => options.EnabledCapabilities = []);
            var available = new ListToolsResult
            {
                Tools = [.. fixture.Services.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool)]
            };
            var request = new RequestContext<ListToolsRequestParams>(fixture.Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/list", Id = new RequestId("wot-runtime-list") },
                new ListToolsRequestParams());
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> filter =
                WotRuntimeMcpFilters.Catalogue((_, _) => ValueTask.FromResult(available));
            ListToolsResult result = await filter(request, default).ConfigureAwait(false);

            Assert.That(result.Tools.Any(tool => tool.Name.StartsWith("wot_runtime_", StringComparison.Ordinal)),
                Is.False);
            Assert.That(result.Tools.Any(tool => tool.Name == "Connect"), Is.True);
        }

        [Test]
        public async Task UnknownRootArgumentsCannotClaimCallerAuthorityAsync()
        {
            await using var fixture = new WotRuntimeTestContext();
            using IDisposable caller = fixture.Enter();
            _ = WotRuntimeTestContext.Payload(await fixture.InvokeAsync("wot_runtime_list",
                new JsonObject { ["caller"] = "wot-other" }).ConfigureAwait(false), error: true);

            Assert.That(fixture.Factory.OpenedForms, Is.Empty);
        }

        private static readonly string[] s_expectedTools =
        [
            "wot_runtime_capabilities", "wot_runtime_open", "wot_runtime_list", "wot_runtime_inspect",
            "wot_runtime_close", "wot_runtime_read_property", "wot_runtime_write_property",
            "wot_runtime_invoke_action", "wot_runtime_observe_property", "wot_runtime_subscribe_events"
        ];

        private static readonly string[] s_expectedExecutors = ["w3c.http", "w3c.mqtt", "w3c.modbus", "opc.opcua"];
    }
}
#endif
