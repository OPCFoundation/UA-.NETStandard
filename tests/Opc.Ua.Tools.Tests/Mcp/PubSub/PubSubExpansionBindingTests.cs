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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Real tool registration, bounded schema and SDK argument-binding contracts.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionBindingTests
    {
        [TestCase(McpToolProfile.PubSub, 14)]
        [TestCase(McpToolProfile.PubSubRuntime, 10)]
        [TestCase(McpToolProfile.PubSubConfiguration, 26)]
        [TestCase(McpToolProfile.PubSubBridge, 16)]
        [TestCase(McpToolProfile.PubSubSecurity, 18)]
        public async Task ProfilesRemainBoundedAndDeduplicateMixedRegistrationAsync(McpToolProfile profile, int count)
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaPubSubTools(profile)
                .WithOpcUaPubSubTools(new McpToolProfileSet(profile));
            await using ServiceProvider provider = services.BuildServiceProvider();

            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];

            Assert.That(names, Has.Length.EqualTo(count));
            Assert.That(names, Is.Unique);
            Assert.That(names, Has.Length.LessThanOrEqualTo(26));
        }

        [Test]
        public async Task FullDoesNotGrantExecutionAndOfflineToolsDoNotActivateAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext
                .CreateAsync(enabled: false)
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();

            ListToolsResult tools = await fixture.CatalogueAsync().ConfigureAwait(false);
            JsonObject validated = fixture.Runtimes.Validate(PubSubExpansionTestContext.Input());
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input()).ConfigureAwait(false));

            Assert.That(tools.Tools.Select(tool => tool.Name), Does.Contain("pubsub_capabilities"));
            Assert.That(tools.Tools.Select(tool => tool.Name), Does.Not.Contain("pubsub_application_start"));
            Assert.That(tools.Tools.Select(tool => tool.Name), Does.Not.Contain("pubsub_configuration_import"));
            Assert.That(tools.Tools.Select(tool => tool.Name), Does.Not.Contain("pubsub_security_pull_keys"));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(validated["valid"]!.GetValue<bool>(), Is.True);
            Assert.That(validated["started"]!.GetValue<bool>(), Is.False);
            Assert.That(fixture.Transports.Factories, Is.Zero);
            Assert.That(fixture.Registry.List(), Is.Empty);
        }

        [Test]
        public async Task SdkBindsConcreteApplicationAndFieldArraysWithoutStringEscapeHatchAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubApplicationInput input = PubSubExpansionTestContext.Input();
            CallToolResult result = await fixture.InvokeAsync("pubsub_application_create",
                PubSubExpansionTestContext.ApplicationArguments(input)).ConfigureAwait(false);
            JsonElement output = RemoteModelTestSession.Payload(result);
            string handle = output.GetProperty("handle").GetString()!;
            McpRuntimeUse<McpPubSubRuntime> use = await fixture.Registry.AcquireAsync<McpPubSubRuntime>(
                handle, McpCapability.PubSubRuntime).ConfigureAwait(false);
            await using (use.ConfigureAwait(false))
            {
                Assert.That(result.IsError, Is.False);
                Assert.That(output.GetProperty("started").GetBoolean(), Is.False);
                Assert.That(use.Runtime.Input.Fields.ToList().Select(field => field.Name).ToArray(),
                    Is.EqualTo(s_fieldNames));
                Assert.That(use.Runtime.Input.Fields[0].InitialValue!.Value.GetInt32(), Is.EqualTo(7));
                Assert.That(use.Runtime.Input.PublisherId.Type, Is.EqualTo(PubSub.Encoding.PublisherIdType.UInt16));
            }
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public async Task SdkBindsScalarReadArgumentsAndReturnsStructuredProtocolResultAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubApplicationInput input = PubSubExpansionTestContext.Input("subscriber", "Subscriber");
            CallToolResult created = await fixture.InvokeAsync("pubsub_application_create",
                PubSubExpansionTestContext.ApplicationArguments(input)).ConfigureAwait(false);
            string handle = RemoteModelTestSession.Payload(created).GetProperty("handle").GetString()!;

            CallToolResult read = await fixture.InvokeAsync("pubsub_application_read", new JsonObject
            {
                ["handle"] = handle,
                ["maximum"] = 1,
                ["clear"] = true
            }).ConfigureAwait(false);

            Assert.That(read.IsError, Is.False);
            JsonElement payload = RemoteModelTestSession.Payload(read);
            Assert.That(payload.GetProperty("running").GetBoolean(), Is.False);
            Assert.That(payload.GetProperty("dataSets").GetArrayLength(), Is.Zero);
            Assert.That(payload.GetProperty("bufferedBytes").GetInt32(), Is.Zero);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public async Task AdvertisedArraySchemaMatchesTheBinderAndRejectsCallerOwnerClaimsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            McpServerTool create = fixture.Services.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == "pubsub_application_create");
            JsonElement fields = create.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("fields");
            JsonObject arguments = PubSubExpansionTestContext.ApplicationArguments(PubSubExpansionTestContext.Input());
            arguments["owner"] = "pubsub-other";

            CallToolResult rejected = await fixture.InvokeAsync("pubsub_application_create", arguments)
                .ConfigureAwait(false);

            Assert.That(fields.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(fields.GetProperty("minItems").GetInt32(), Is.EqualTo(1));
            Assert.That(fields.GetProperty("maxItems").GetInt32(), Is.EqualTo(500));
            Assert.That(fields.GetProperty("items").GetProperty("properties").TryGetProperty("type", out _), Is.True);
            Assert.That(rejected.IsError, Is.True);
            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public async Task BoundExpansionSchemasStayShallowAfterResolvingReusableDefinitionsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            foreach (McpServerTool tool in fixture.Services.GetServices<McpServerTool>().Where(tool =>
                tool.ProtocolTool.Name.StartsWith("pubsub_application_", StringComparison.Ordinal) ||
                tool.ProtocolTool.Name.StartsWith("pubsub_bridge_", StringComparison.Ordinal) ||
                tool.ProtocolTool.Name.StartsWith("pubsub_configuration_", StringComparison.Ordinal) ||
                tool.ProtocolTool.Name.StartsWith("pubsub_security_", StringComparison.Ordinal) ||
                tool.ProtocolTool.Name.StartsWith("pubsub_adapter_", StringComparison.Ordinal)))
            {
                JsonElement schema = tool.ProtocolTool.InputSchema;
                Assert.That(schema.GetProperty("properties").EnumerateObject().Count(), Is.LessThanOrEqualTo(8),
                    tool.ProtocolTool.Name);
                Assert.That(StructuralDepth(schema, schema), Is.LessThanOrEqualTo(3), tool.ProtocolTool.Name);
                Assert.That(PinnedGraderDepth(schema), Is.LessThanOrEqualTo(3),
                    tool.ProtocolTool.Name + " must also satisfy the exact pinned S006 traversal.");
            }
            McpServerTool bridge = fixture.Services.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == "pubsub_bridge_validate");
            JsonElement properties = bridge.ProtocolTool.InputSchema.GetProperty("properties");
            Assert.That(properties.GetProperty("sourceFields").GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(properties.GetProperty("targetFields").GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(properties.GetProperty("renameFields").GetProperty("items")
                .GetProperty("properties").TryGetProperty("source", out _), Is.True);
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                Assert.That(property.Value.GetProperty("description").GetString(), Is.Not.Null.And.Not.Empty);
                Assert.That(property.Value.TryGetProperty("type", out _), Is.True,
                    property.Name + " must have an explicit root type even when it uses $ref.");
            }
            Assert.That(bridge.ProtocolTool.InputSchema.GetRawText().Length +
                bridge.ProtocolTool.Description!.Length +
                bridge.ProtocolTool.Name.Length,
                Is.LessThanOrEqualTo(4800), "The definition must fit the grader's approximate 1,200-token budget.");
        }

        [Test]
        public async Task ShallowBridgeBinderConsumesNativeArraysWithoutActivatingEndpointsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            var route = new PubSubBridgeInput
            {
                Name = "shallow-bridge",
                Source = PubSubExpansionTestContext.Input("source", "Subscriber"),
                Target = PubSubExpansionTestContext.Input("target"),
                RenameFields = [new PubSubFieldRenameInput { Source = "Count", Target = "Counter" }],
                SelectFields = ["Counter"]
            };
            JsonObject arguments = PubSubExpansionTestContext.BridgeArguments(route);

            CallToolResult validation = await fixture.InvokeAsync("pubsub_bridge_validate", arguments)
                .ConfigureAwait(false);
            CallToolResult created = await fixture.InvokeAsync("pubsub_bridge_create", arguments)
                .ConfigureAwait(false);

            Assert.That(validation.IsError, Is.False, RemoteModelTestSession.Payload(validation).GetRawText());
            Assert.That(created.IsError, Is.False, RemoteModelTestSession.Payload(created).GetRawText());
            Assert.That(RemoteModelTestSession.Payload(validation).GetProperty("valid").GetBoolean(), Is.True);
            Assert.That(RemoteModelTestSession.Payload(created).GetProperty("started").GetBoolean(), Is.False);
            Assert.That(fixture.Registry.List(), Has.Count.EqualTo(1));
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public void PinnedGraderCountsUnionArmsButNotDefinitionBookkeeping()
        {
            using var wrapped = JsonDocument.Parse("""
                {"type":"object","properties":{"fields":{"type":"array","items":{"type":"object",
                "properties":{"value":{"anyOf":[{"type":"string"},{"type":"null"}]}}}}}}
                """);
            using var compact = JsonDocument.Parse("""
                {"type":"object","properties":{"fields":{"type":"array","items":{"type":"object",
                "properties":{"value":{"type":["string","null"]}}}}}}
                """);
            using var definitions = JsonDocument.Parse("""
                {"type":"object","properties":{"value":{"type":"object","$ref":"#/$defs/value"}},
                "$defs":{"value":{"type":"object","properties":{"nested":{"type":"object",
                "properties":{"deeper":{"type":"object","properties":{"end":{"type":"string"}}}}}}}}}
                """);
            using var closedTuple = JsonDocument.Parse("""{"type":"array","items":false}""");

            Assert.That(PinnedGraderDepth(wrapped.RootElement), Is.EqualTo(4));
            Assert.That(PinnedGraderDepth(compact.RootElement), Is.EqualTo(3));
            Assert.That(PinnedGraderDepth(definitions.RootElement), Is.EqualTo(1));
            Assert.That(PinnedGraderDepth(closedTuple.RootElement), Is.Zero);
            Assert.That(StructuralDepth(definitions.RootElement, definitions.RootElement), Is.GreaterThan(3),
                "The independent resolved-shape check must still expose genuinely deep referenced data.");
        }

        /// <summary>
        /// Mirrors schemaDepth at the Test-McpGrade.ps1 pin feff7162903ab9fbe92f182be9a082fcb24c9bbc.
        /// </summary>
        private static int PinnedGraderDepth(JsonElement schema, int depth = 0)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return depth;
            }
            int maximum = depth;
            if (schema.TryGetProperty("properties", out JsonElement properties) &&
                properties.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    maximum = Math.Max(maximum, PinnedGraderDepth(property.Value, depth + 1));
                }
            }
            if (schema.TryGetProperty("items", out JsonElement items) &&
                items.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
            {
                if (items.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in items.EnumerateArray())
                    {
                        maximum = Math.Max(maximum, PinnedGraderDepth(item, depth + 1));
                    }
                }
                else
                {
                    maximum = Math.Max(maximum, PinnedGraderDepth(items, depth + 1));
                }
            }
            foreach (string keyword in s_unionKeywords)
            {
                if (schema.TryGetProperty(keyword, out JsonElement alternatives) &&
                    alternatives.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement alternative in alternatives.EnumerateArray())
                    {
                        maximum = Math.Max(maximum, PinnedGraderDepth(alternative, depth + 1));
                    }
                }
            }
            return maximum;
        }

        private static int StructuralDepth(JsonElement schema, JsonElement root)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }
            if (schema.TryGetProperty("$ref", out JsonElement reference))
            {
                string path = reference.GetString()!;
                Assert.That(path, Does.StartWith("#/$defs/"));
                return StructuralDepth(root.GetProperty("$defs").GetProperty(path["#/$defs/".Length..]), root);
            }
            int own = schema.TryGetProperty("properties", out _) || schema.TryGetProperty("items", out _) ? 1 : 0;
            int children = 0;
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    children = Math.Max(children, StructuralDepth(property.Value, root));
                }
            }
            if (schema.TryGetProperty("items", out JsonElement items))
            {
                children = Math.Max(children, StructuralDepth(items, root));
            }
            foreach (string keyword in s_unionKeywords)
            {
                if (schema.TryGetProperty(keyword, out JsonElement alternatives))
                {
                    foreach (JsonElement alternative in alternatives.EnumerateArray())
                    {
                        children = Math.Max(children, StructuralDepth(alternative, root));
                    }
                }
            }
            return own + children;
        }

        private static readonly string[] s_fieldNames = ["Count", "Enabled"];
        private static readonly string[] s_unionKeywords = ["anyOf", "oneOf", "allOf"];
    }
}
#endif
