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

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises actual MCP registration and reflection-free concrete contract binding without a network listener.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class SchemaBindingTests
    {
        /// <summary>
        /// Repeated and composed registration exposes precisely six domain tools and six deduplicated connections.
        /// </summary>
        [Test]
        public async Task SchemaCatalogueContainsSixToolsWithDeduplicatedConnectionsAsync()
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaConnectionTools()
                .WithOpcUaSchemaTools(McpToolProfile.Schemas)
                .WithOpcUaSchemaTools(new McpToolProfileSet(McpToolProfile.Schemas))
                .WithOpcUaSchemaTools(McpToolProfile.Full);
            await using ServiceProvider provider = services.BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];

            Assert.That(names, Is.Unique);
            Assert.That(names, Has.Length.EqualTo(12));
            Assert.That(names.Where(name => name.StartsWith("schema_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_names));
            Assert.That(names.Where(name => !name.StartsWith("schema_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_connectionNames));
        }

        /// <summary>
        /// Unrelated bounded profiles do not silently acquire a schema or connection catalogue.
        /// </summary>
        [TestCase(McpToolProfile.Core)]
        [TestCase(McpToolProfile.WotDocuments)]
        [TestCase(McpToolProfile.PubSub)]
        public async Task UnrelatedProfilesDoNotAcquireSchemaToolsAsync(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddMcpServer().WithOpcUaSchemaTools(profile);
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<McpServerTool>(), Is.Empty);
        }

        /// <summary>
        /// Registration remains idempotent and preserves an embedding host's explicitly registered limits.
        /// </summary>
        [Test]
        public async Task RepeatedModuleRegistrationPreservesInjectedPolicyAsync()
        {
            var services = new ServiceCollection();
            var options = new McpSchemaOptions { MaxTypes = 2 };
            services.AddSingleton(options);
            services.AddOpcUaMcpCore().AddOpcUaMcpSchema().AddOpcUaMcpSchema();
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetServices<McpSchemaOptions>().ToArray(), Has.Length.EqualTo(1));
            Assert.That(provider.GetRequiredService<McpSchemaOptions>(), Is.SameAs(options));
        }

        /// <summary>
        /// The real SDK catalogue advertises shallow typed arrays, explicit masks and bounded argument count.
        /// </summary>
        [Test]
        public async Task AdvertisedSchemasAreTypedBoundedAndShallowAsync()
        {
            await using var fixture = new SchemaTestContext();
            Tool[] tools = [.. fixture.Services.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool)
                .Where(tool => tool.Name.StartsWith("schema_", StringComparison.Ordinal))];

            foreach (Tool tool in tools)
            {
                JsonElement properties = tool.InputSchema.GetProperty("properties");
                Assert.That(properties.EnumerateObject().Count(), Is.LessThanOrEqualTo(8), tool.Name);
                Assert.That(properties.TryGetProperty("input", out _), Is.False, tool.Name);
                Assert.That(ContainerDepth(tool.InputSchema), Is.LessThanOrEqualTo(3), tool.Name);
                Assert.That(tool.InputSchema.GetProperty("additionalProperties").GetBoolean(), Is.False, tool.Name);
                foreach (JsonProperty parameter in properties.EnumerateObject())
                {
                    Assert.That(parameter.Value.TryGetProperty("description", out JsonElement description),
                        Is.True, tool.Name + ":" + parameter.Name);
                    Assert.That(description.GetString(), Is.Not.Null.And.Not.Empty,
                        tool.Name + ":" + parameter.Name);
                    if (parameter.Name == "options")
                    {
                        Assert.That(parameter.Value.GetProperty("examples").GetArrayLength(),
                            Is.GreaterThan(0), tool.Name);
                    }
                }
            }
            JsonElement load = tools.Single(tool => tool.Name == "schema_load_types")
                .InputSchema.GetProperty("properties").GetProperty("typeIds");
            Assert.That(load.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(load.GetProperty("items").GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(load.GetProperty("minItems").GetInt32(), Is.EqualTo(1));
            Assert.That(load.GetProperty("maxItems").GetInt32(), Is.EqualTo(512));
            JsonElement pubsub = tools.Single(tool => tool.Name == "schema_generate_pubsub").InputSchema;
            JsonElement fields = pubsub.GetProperty("properties").GetProperty("fields");
            Assert.That(fields.GetProperty("type").GetString(), Is.EqualTo("array"));
            Assert.That(fields.GetProperty("maxItems").GetInt32(), Is.EqualTo(1024));
            Assert.That(fields.GetProperty("items").GetProperty("properties").TryGetProperty("dataTypeId", out _),
                Is.True);
            Assert.That(pubsub.GetProperty("properties").GetProperty("options").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(s_pubSubMasks));
        }

        /// <summary>
        /// Flattened identifiers and grouped enum options invoke the original tool through the actual SDK binder.
        /// </summary>
        [Test]
        public async Task SdkBinderGeneratesRegisteredSchemaWithoutSessionAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId id = fixture.Register("Sample", SchemaTestContext.Structure(
                SchemaTestContext.Field("Count", DataTypeIds.Int32)));

            JsonObject result = SchemaTestContext.Payload(await fixture.InvokeAsync("schema_generate", new JsonObject
            {
                ["typeId"] = id.ToString(),
                ["outputPath"] = "bound.json",
                ["options"] = new JsonObject { ["source"] = "Registered", ["format"] = "JsonVerbose" }
            }).ConfigureAwait(false));
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "bound.json"))
                .ConfigureAwait(false))!;

            Assert.That(result["format"]!.GetValue<string>(), Is.EqualTo("JsonVerbose"));
            Assert.That(document["$defs"]!["Sample"]!["required"]!.AsArray()
                .Select(value => value!.GetValue<string>()), Does.Contain("Count"));
            Assert.That(fixture.Services.GetRequiredService<OpcUaSessionManager>().IsConnected, Is.False);
        }

        /// <summary>
        /// Concrete field arrays survive the real binder without JSON strings or an extra input envelope.
        /// </summary>
        [Test]
        public async Task SdkBinderAcceptsTypedPubSubFieldArrayAsync()
        {
            await using var fixture = new SchemaTestContext();

            SchemaTestContext.Payload(await fixture.InvokeAsync("schema_generate_pubsub", new JsonObject
            {
                ["outputPath"] = "fields.json",
                ["fields"] = new JsonArray(new JsonObject
                {
                    ["name"] = "Values",
                    ["dataTypeId"] = "i=6",
                    ["valueRank"] = 2
                }),
                ["options"] = new JsonObject
                {
                    ["fieldContentMask"] = (uint)DataSetFieldContentMask.RawData,
                    ["messageContentMask"] = 0,
                    ["networkContentMask"] = 0
                }
            }).ConfigureAwait(false));
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "fields.json"))
                .ConfigureAwait(false))!;

            Assert.That(document["properties"]!["Values"]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
            Assert.That(document["properties"]!["Values"]!["items"]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
            Assert.That(document["properties"]!["Values"]!["items"]!["items"]!["type"]!.GetValue<string>(),
                Is.EqualTo("integer"));
        }

        /// <summary>
        /// Both read tools reach the actual registered definitions through the SDK's closed input metadata.
        /// </summary>
        [Test]
        public async Task SdkBinderListsAndInspectsRegisteredDefinitionsAsync()
        {
            await using var fixture = new SchemaTestContext();
            NodeId id = fixture.Register("Record", SchemaTestContext.Structure(
                SchemaTestContext.Field("Count", DataTypeIds.Int32)));

            JsonObject listed = SchemaTestContext.Payload(await fixture.InvokeAsync("schema_list_types", new JsonObject
            {
                ["options"] = new JsonObject { ["namespaceUri"] = SchemaTestContext.TypeNamespaceUri }
            }).ConfigureAwait(false));
            JsonObject inspected = SchemaTestContext.Payload(await fixture.InvokeAsync(
                "schema_inspect_type", new JsonObject { ["typeId"] = id.ToString() }).ConfigureAwait(false));

            Assert.That(listed["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(listed["items"]![0]!["typeId"]!.GetValue<string>(), Is.EqualTo(id.ToString()));
            Assert.That(inspected["definition"]!["Fields"]![0]!["Name"]!.GetValue<string>(), Is.EqualTo("Count"));
            Assert.That(inspected["browseName"]!.GetValue<string>(), Is.EqualTo(fixture.NameNamespace + ":Record"));
        }

        /// <summary>
        /// Explicit type-id arrays reach the real AOT-safe loader through the bound schema_load_types method.
        /// </summary>
        [Test]
        public async Task SdkBinderLoadsExplicitEnumRootsAsync()
        {
            await using var fixture = new SchemaTestContext();
            await using RemoteModelTestSession remote =
                await RemoteModelTestSession.CreateAsync(false, false).ConfigureAwait(false);
            NodeId id = remote.Id("State");
            remote.Session.SetupGet(session => session.Factory).Returns(remote.Context.Factory);
            remote.Values[(id, Attributes.BrowseName)] =
                new DataValue(Variant.From(new QualifiedName("State", remote.InstanceNamespace)));
            remote.Values[(id, Attributes.DataTypeDefinition)] = new DataValue(Variant.From(
                new ExtensionObject(new EnumDefinition { Fields = [new EnumField { Name = "Ready", Value = 7 }] })));
            remote.Values[(id, Attributes.IsAbstract)] = new DataValue(Variant.From(false));
            await fixture.Services.GetRequiredService<OpcUaSessionManager>()
                .RegisterExistingSessionAsync("selected", remote.Session.Object, "Anonymous").ConfigureAwait(false);

            JsonObject loaded = SchemaTestContext.Payload(await fixture.InvokeAsync("schema_load_types", new JsonObject
            {
                ["typeIds"] = new JsonArray(id.ToString()),
                ["sessionName"] = "selected"
            }).ConfigureAwait(false));

            Assert.That(loaded["results"]![0]!["loaded"]!.GetValue<bool>(), Is.True);
            Assert.That(remote.Context.Factory.TryGetEnumeratedType(
                NodeId.ToExpandedNodeId(id, remote.Context.NamespaceUris), out IEnumeratedType? type), Is.True);
            Assert.That(type!.TryGetValue("Ready", out int value), Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(remote.Browses, Is.Empty);
        }

        /// <summary>
        /// The sixth bound tool publishes the existing sessionless REST document without connecting.
        /// </summary>
        [Test]
        public async Task SdkBinderGeneratesLocalOpenApiAsync()
        {
            await using var fixture = new SchemaTestContext();

            JsonObject result = SchemaTestContext.Payload(await fixture.InvokeAsync("schema_generate_openapi",
                new JsonObject
                {
                    ["outputPath"] = "rest.json",
                    ["options"] = new JsonObject { ["serviceSet"] = "Sessionless" }
                }).ConfigureAwait(false));
            JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "rest.json"))
                .ConfigureAwait(false))!;

            Assert.That(result["routeCount"]!.GetValue<int>(), Is.EqualTo(8));
            Assert.That(document["paths"]!.AsObject().ContainsKey("/read"), Is.True);
            Assert.That(document["paths"]!.AsObject().ContainsKey("/createsession"), Is.False);
            Assert.That(fixture.Services.GetRequiredService<OpcUaSessionManager>().IsConnected, Is.False);
        }

        /// <summary>
        /// Unknown cap-raising and code-loading options fail before ordinary tool execution.
        /// </summary>
        [TestCase("maxTypes")]
        [TestCase("assemblyPath")]
        [TestCase("overwrite")]
        public async Task SdkBinderRejectsUnadvertisedOptionsAsync(string name)
        {
            await using var fixture = new SchemaTestContext();

            JsonObject result = SchemaTestContext.Payload(await fixture.InvokeAsync("schema_generate", new JsonObject
            {
                ["typeId"] = "i=296",
                ["outputPath"] = "invalid.json",
                ["options"] = new JsonObject { [name] = 999999 }
            }).ConfigureAwait(false), error: true);

            Assert.That(result["message"]!.GetValue<string>(), Does.Contain(name));
            Assert.That(Directory.GetFiles(fixture.Root), Is.Empty);
        }

        /// <summary>
        /// Source-generated field-array parsing accepts exact bounds and rejects the adjacent element.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(1024, false)]
        [TestCase(1025, true)]
        public void ConcreteFieldArrayBindingEnforcesExactMaximum(int count, bool failure)
        {
            var fields = new JsonArray();
            for (int index = 0; index < count; index++)
            {
                fields.Add((JsonNode)new JsonObject { ["name"] = "Field" + index, ["dataTypeId"] = "i=6" });
            }
            string json = new JsonObject
            {
                ["fields"] = fields,
                ["fieldContentMask"] = 0,
                ["messageContentMask"] = 0,
                ["networkContentMask"] = 0
            }.ToJsonString();
            if (failure)
            {
                Assert.That(() => JsonSerializer.Deserialize(json, SchemaInputJsonContext.Default.SchemaPubSubInput),
                    Throws.TypeOf<JsonException>());
            }
            else
            {
                SchemaPubSubInput? result =
                    JsonSerializer.Deserialize(json, SchemaInputJsonContext.Default.SchemaPubSubInput);
                Assert.That(result!.Fields.Count, Is.EqualTo(count));
            }
        }

        /// <summary>
        /// Root identifier arrays are concrete strings and never exceed the approved maximum.
        /// </summary>
        [TestCase(512, false)]
        [TestCase(513, true)]
        public void ConcreteTypeRootBindingEnforcesExactMaximum(int count, bool failure)
        {
            var ids = new JsonArray();
            for (int index = 0; index < count; index++)
            {
                JsonNode id = JsonValue.Create("ns=1;i=" + index);
                ids.Add(id);
            }
            string json = new JsonObject { ["typeIds"] = ids }.ToJsonString();
            if (failure)
            {
                Assert.That(() => JsonSerializer.Deserialize(json, SchemaInputJsonContext.Default.SchemaLoadInput),
                    Throws.TypeOf<JsonException>());
            }
            else
            {
                SchemaLoadInput? result =
                    JsonSerializer.Deserialize(json, SchemaInputJsonContext.Default.SchemaLoadInput);
                Assert.That(result!.TypeIds.Count, Is.EqualTo(512));
                Assert.That(result.TypeIds[511], Is.EqualTo("ns=1;i=511"));
            }
        }

        /// <summary>
        /// Typed input cannot hide missing masks, unknown properties or untyped null array entries.
        /// </summary>
        [TestCase(/*lang=json,strict*/ """{"fields":[]}""")]
        [TestCase(/*lang=json,strict*/
            """{"fieldContentMask":0,"messageContentMask":0,"networkContentMask":0,"maxTypes":9999}""")]
        [TestCase(/*lang=json,strict*/
            """{"fieldContentMask":0,"messageContentMask":0,"networkContentMask":0,"fields":[null]}""")]
        public void ConcreteBindingRejectsMissingOrUnknownInput(string json)
        {
            Assert.That(() => JsonSerializer.Deserialize(json, SchemaInputJsonContext.Default.SchemaPubSubInput),
                Throws.TypeOf<JsonException>());
        }

        /// <summary>
        /// Measures object/array instance nesting, not irrelevant JSON Schema keyword nesting.
        /// </summary>
        private static int ContainerDepth(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                return 1 +
                    properties.EnumerateObject().Select(property => ContainerDepth(property.Value))
                        .DefaultIfEmpty(0).Max();
            }
            if (schema.TryGetProperty("items", out JsonElement items))
            {
                return 1 + ContainerDepth(items);
            }
            foreach (string keyword in new[] { "anyOf", "oneOf", "allOf" })
            {
                if (schema.TryGetProperty(keyword, out JsonElement alternatives))
                {
                    return alternatives.EnumerateArray().Select(ContainerDepth).DefaultIfEmpty(0).Max();
                }
            }
            return 0;
        }

        /// <summary>The approved six-tool domain catalogue.</summary>
        private static readonly string[] s_names =
        [
            "schema_list_types",
            "schema_inspect_type",
            "schema_load_types",
            "schema_generate",
            "schema_generate_pubsub",
            "schema_generate_openapi"
        ];

        /// <summary>The shared connection catalogue appears exactly once.</summary>
        private static readonly string[] s_connectionNames =
            ["Connect", "ConnectCertificate", "ConnectJwt", "Disconnect", "GetEndpoints", "GetConnectionStatus"];

        /// <summary>All PubSub mask choices must be explicit.</summary>
        private static readonly string[] s_pubSubMasks =
            ["fieldContentMask", "messageContentMask", "networkContentMask"];
    }
}
#endif
