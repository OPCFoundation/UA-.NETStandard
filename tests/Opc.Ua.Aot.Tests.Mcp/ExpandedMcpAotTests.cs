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

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Schema;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the expanded packages' real registrations and binders with JSON reflection disabled.
    /// </summary>
    /// <remarks>
    /// Only the four legacy connection registrations are isolated. Their existing CORE limitation remains
    /// pinned by McpAotTests; none of these tests substitutes metadata for an expanded production registration.
    /// Protocol effects are confined to explicit local definitions, owned temporary artifacts and typed fakes.
    /// </remarks>
    public sealed class ExpandedMcpAotTests
    {
        /// <summary>
        /// Each approved profile resolves its complete new catalogue through its own production JSON options.
        /// </summary>
        [Test]
        [Arguments(McpToolProfile.Gds, "gds_", 9)]
        [Arguments(McpToolProfile.Certificates, "certificate_", 23)]
        [Arguments(McpToolProfile.Credentials, "credential_", 10)]
        [Arguments(McpToolProfile.Onboarding, "onboarding_", 4)]
        [Arguments(McpToolProfile.Roles, "roles_", 13)]
        [Arguments(McpToolProfile.Users, "users_", 6)]
        [Arguments(McpToolProfile.Files, "files_", 13)]
        [Arguments(McpToolProfile.Alarms, "alarms_", 19)]
        [Arguments(McpToolProfile.History, "history_", 23)]
        [Arguments(McpToolProfile.Schemas, "schema_", 6)]
        [Arguments(McpToolProfile.PubSubRuntime, "pubsub_", 10)]
        [Arguments(McpToolProfile.PubSubConfiguration, "pubsub_configuration_", 20)]
        [Arguments(McpToolProfile.PubSubBridge, "pubsub_", 10)]
        [Arguments(McpToolProfile.PubSubSecurity, "pubsub_security_", 12)]
        [Arguments(McpToolProfile.WotRuntime, "wot_runtime_", 10)]
        public async Task ExpandedProfilesResolveProductionMetadataWithoutReflectionAsync(
            McpToolProfile profile, string prefix, int expectedCount)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(profile));
            string[] names = [.. host.Tools.Select(tool => tool.ProtocolTool.Name)];

            await Assert.That(host.IsolatedConnectionCount).IsEqualTo(4);
            await Assert.That(names.Length).IsEqualTo(expectedCount);
            await Assert.That(names.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(expectedCount);
            await Assert.That(names.Count(name => name.StartsWith(prefix, StringComparison.Ordinal)))
                .IsEqualTo(expectedCount);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// New high-bit profiles neither alias CORE nor disappear when composed or registered repeatedly.
        /// </summary>
        [Test]
        public async Task HighBitProfilesComposeWithoutGrantingCoreOrDuplicatingToolsAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            var profiles = McpToolProfileSet.Parse(
                "pubsub-security+wot-runtime+pubsub-security");
            await using var host = new ExpandedHost(profiles);

            await Assert.That(profiles.Count).IsEqualTo(2);
            await Assert.That(string.Join(",", profiles.Enumerate().Select(profile => (int)profile)))
                .IsEqualTo("31,32");
            await Assert.That(profiles.Contains(McpToolProfile.Core)).IsFalse();
            await Assert.That(profiles.Contains((McpToolProfile)33)).IsFalse();
            await Assert.That(host.Tools.Length).IsEqualTo(22);
            await Assert.That(host.Tools.Select(tool => tool.ProtocolTool.Name)
                .Distinct(StringComparer.Ordinal).Count()).IsEqualTo(22);
        }

        /// <summary>
        /// Unrelated selections do not silently import any of the expanded catalogues.
        /// </summary>
        [Test]
        [Arguments(McpToolProfile.Core)]
        [Arguments(McpToolProfile.WotDocuments)]
        public async Task UnrelatedProfilesDoNotRegisterExpandedToolsAsync(McpToolProfile profile)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(profile));

            await Assert.That(host.Tools.Length).IsEqualTo(0);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
            await Assert.That(host.PubSubTransports.FactoryCalls).IsEqualTo(0);
        }

        /// <summary>
        /// Representative new signatures retain exact closed roots, bounded typed arrays and intended depth.
        /// </summary>
        [Test]
        public async Task RepresentativeCataloguesPublishExactClosedArrayShapesAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(s_expandedProfiles);

            JsonElement gds = host.Tool("gds_register_application").ProtocolTool.InputSchema;
            await AssertRootAsync(gds, "applicationNames,directoryNodeId,input,sessionName", 3).ConfigureAwait(false);
            await AssertArrayAsync(gds.GetProperty("properties").GetProperty("applicationNames"),
                1, 100, "object").ConfigureAwait(false);
            JsonElement localized = gds.GetProperty("properties").GetProperty("applicationNames")
                .GetProperty("items").GetProperty("properties");
            await Assert.That(PropertyNames(localized)).IsEqualTo("locale,text");
            await Assert.That(RequiredNames(gds)).IsEqualTo("applicationNames,directoryNodeId,input");

            JsonElement history = host.Tool("history_insert_values").ProtocolTool.InputSchema;
            await AssertRootAsync(history, "arrayElements,nodeId,sessionName,values", 3).ConfigureAwait(false);
            await Assert.That(RequiredNames(history)).IsEqualTo("nodeId,values");
            JsonElement values = history.GetProperty("properties").GetProperty("values");
            await AssertArrayAsync(values, 1, 500, "object").ConfigureAwait(false);
            await Assert.That(PropertyNames(values.GetProperty("items").GetProperty("properties")))
                .IsEqualTo("arrayLength,serverTimestamp,sourceTimestamp,statusCode,type,value");
            await Assert.That(RequiredNames(values.GetProperty("items"))).IsEqualTo("sourceTimestamp,type");
            JsonElement scalar = values.GetProperty("items").GetProperty("properties").GetProperty("value");
            await Assert.That(string.Join(",", scalar.GetProperty("type").EnumerateArray()
                .Select(type => type.GetString()).Order(StringComparer.Ordinal)))
                .IsEqualTo("boolean,null,number,string");
            await Assert.That(scalar.TryGetProperty("anyOf", out _)).IsFalse();
            await AssertArrayAsync(history.GetProperty("properties").GetProperty("arrayElements"),
                0, 500, "object").ConfigureAwait(false);

            JsonElement schema = host.Tool("schema_generate_pubsub").ProtocolTool.InputSchema;
            await AssertRootAsync(schema, "fields,metadataNodeId,options,outputPath,sessionName", 3)
                .ConfigureAwait(false);
            await AssertArrayAsync(schema.GetProperty("properties").GetProperty("fields"),
                0, 1024, "object").ConfigureAwait(false);
            await Assert.That(PropertyNames(schema.GetProperty("properties").GetProperty("fields")
                .GetProperty("items").GetProperty("properties"))).IsEqualTo("dataTypeId,name,valueRank");
            await Assert.That(RequiredNames(schema.GetProperty("properties").GetProperty("options")))
                .IsEqualTo("fieldContentMask,messageContentMask,networkContentMask");

            JsonElement pubsub = host.Tool("pubsub_application_create").ProtocolTool.InputSchema;
            await AssertRootAsync(pubsub, "connection,fields,input", 3).ConfigureAwait(false);
            await Assert.That(RequiredNames(pubsub)).IsEqualTo("connection,fields,input");
            await AssertArrayAsync(pubsub.GetProperty("properties").GetProperty("fields"), 1, 500, "object")
                .ConfigureAwait(false);
            await Assert.That(PropertyNames(pubsub.GetProperty("properties").GetProperty("fields")
                .GetProperty("items").GetProperty("properties")))
                .IsEqualTo("id,initialValue,name,type,valueRank");
            JsonElement bridge = host.Tool("pubsub_bridge_validate").ProtocolTool.InputSchema;
            await AssertRootAsync(bridge,
                "input,renameFields,source,sourceConnection,sourceFields,target,targetConnection,targetFields", 3)
                .ConfigureAwait(false);
            await Assert.That(RequiredNames(bridge))
                .IsEqualTo("input,source,sourceConnection,sourceFields,target,targetConnection,targetFields");
            await Assert.That(bridge.GetProperty("properties").GetProperty("sourceFields")
                .GetProperty("items").GetProperty("$ref").GetString()).IsEqualTo("#/$defs/field");
            await Assert.That(bridge.GetProperty("properties").GetProperty("targetFields")
                .GetProperty("maxItems").GetInt32()).IsEqualTo(500);
            JsonElement read = host.Tool("pubsub_application_read").ProtocolTool.InputSchema;
            await AssertRootAsync(read, "clear,handle,maximum", 1).ConfigureAwait(false);
            await Assert.That(read.GetProperty("properties").GetProperty("maximum")
                .GetProperty("default").GetInt32()).IsEqualTo(64);
            await Assert.That(read.GetProperty("properties").GetProperty("clear")
                .GetProperty("default").GetBoolean()).IsFalse();

            JsonElement wot = host.Tool("wot_runtime_open").ProtocolTool.InputSchema;
            await AssertRootAsync(wot, "credentials,dependencies,document,name,operations,options,selection", 3)
                .ConfigureAwait(false);
            await AssertArrayAsync(wot.GetProperty("properties").GetProperty("operations"), 1, 5, "string")
                .ConfigureAwait(false);
            await AssertArrayAsync(wot.GetProperty("properties").GetProperty("credentials"), 0, 1, "object")
                .ConfigureAwait(false);
            await Assert.That(PropertyNames(wot.GetProperty("properties").GetProperty("credentials")
                .GetProperty("items").GetProperty("properties")))
                .IsEqualTo("schemeName,secretReference,userNameReference");
            await AssertArrayAsync(wot.GetProperty("properties").GetProperty("dependencies"), 0, 255, "object")
                .ConfigureAwait(false);
            await Assert.That(RequiredNames(wot)).IsEqualTo("document,name,operations,selection");
        }

        /// <summary>
        /// The registered list and inspection tools use actual local definitions and omitted scalar defaults.
        /// </summary>
        [Test]
        public async Task SchemaBinderListsAndInspectsOfflineDefinitionsWithDefaultsAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(McpToolProfile.Schemas));
            NodeId id = host.RegisterRecord();

            JsonElement listed = await PayloadAsync(await host.InvokeAsync("schema_list_types", new JsonObject
            {
                ["options"] = new JsonObject { ["namespaceUri"] = kTypeNamespace }
            }).ConfigureAwait(false)).ConfigureAwait(false);
            JsonElement inspected = await PayloadAsync(await host.InvokeAsync("schema_inspect_type", new JsonObject
            {
                ["typeId"] = id.ToString()
            }).ConfigureAwait(false)).ConfigureAwait(false);

            await Assert.That(listed.GetProperty("items").GetArrayLength()).IsEqualTo(1);
            await Assert.That(listed.GetProperty("items")[0].GetProperty("typeId").GetString())
                .IsEqualTo(id.ToString());
            await Assert.That(listed.GetProperty("offset").GetInt32()).IsEqualTo(0);
            await Assert.That(listed.GetProperty("hasMore").GetBoolean()).IsFalse();
            await Assert.That(listed.GetProperty("nextOffset").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(inspected.GetProperty("browseName").GetString())
                .IsEqualTo(host.NameNamespace + ":Record");
            JsonElement fields = inspected.GetProperty("definition").GetProperty("Fields");
            await Assert.That(fields.GetArrayLength()).IsEqualTo(2);
            await Assert.That(fields[0].GetProperty("Name").GetString()).IsEqualTo("Count");
            await Assert.That(fields[1].GetProperty("Name").GetString()).IsEqualTo("Enabled");
            await Assert.That(inspected.GetProperty("isStructureOptionSet").GetBoolean()).IsFalse();
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// The real SDK binder and generators publish concrete schemas, not merely test-context JSON.
        /// </summary>
        [Test]
        [Arguments("Xsd")]
        [Arguments("Bsd")]
        [Arguments("JsonCompact")]
        [Arguments("JsonVerbose")]
        public async Task SchemaBinderGeneratesOfflineArtifactsWithoutReflectionAsync(string format)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(
                new McpToolProfileSet(McpToolProfile.Schemas), artifacts: true);
            NodeId id = host.RegisterRecord();
            JsonElement result = await PayloadAsync(await host.InvokeAsync("schema_generate", new JsonObject
            {
                ["typeId"] = id.ToString(),
                ["outputPath"] = "record.schema",
                ["options"] = new JsonObject { ["format"] = format }
            }).ConfigureAwait(false)).ConfigureAwait(false);
            byte[] bytes = await AssertArtifactAsync(host, result, "record.schema").ConfigureAwait(false);

            await Assert.That(result.GetProperty("format").GetString()).IsEqualTo(format);
            await Assert.That(result.GetProperty("targetNamespace").GetString()).IsEqualTo(kTypeNamespace);
            await Assert.That(result.GetProperty("definitionCount").GetInt32()).IsEqualTo(1);
            await Assert.That(result.GetProperty("dependencyArtifactsGenerated").GetBoolean()).IsFalse();
            if (format is "Xsd" or "Bsd")
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var document = XDocument.Load(stream);
                await Assert.That(document.Root!.Name.LocalName)
                    .IsEqualTo(format == "Xsd" ? "schema" : "TypeDictionary");
                await Assert.That(document.Descendants().Count(element =>
                    (string?)element.Attribute(format == "Xsd" ? "name" : "Name") == "Count")).IsEqualTo(1);
            }
            else
            {
                using var document = JsonDocument.Parse(bytes);
                JsonElement properties = document.RootElement.GetProperty("$defs").GetProperty("Record")
                    .GetProperty("properties");
                await Assert.That(properties.GetProperty("Count").GetProperty("type").GetString())
                    .IsEqualTo("integer");
                await Assert.That(properties.GetProperty("Enabled").GetProperty("type").GetString())
                    .IsEqualTo("boolean");
            }
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// Omitted and empty options retain the production generator's Registered/JsonCompact/Type defaults.
        /// </summary>
        [Test]
        [Arguments(false)]
        [Arguments(true)]
        public async Task SchemaBinderPreservesOmittedAndEmptyOptionDefaultsAsync(bool includeOptions)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(
                new McpToolProfileSet(McpToolProfile.Schemas), artifacts: true);
            var arguments = new JsonObject
            {
                ["typeId"] = host.RegisterRecord().ToString(),
                ["outputPath"] = "defaults.json"
            };
            if (includeOptions)
            {
                arguments["options"] = new JsonObject();
            }

            JsonElement result = await PayloadAsync(
                await host.InvokeAsync("schema_generate", arguments).ConfigureAwait(false))
                .ConfigureAwait(false);
            byte[] bytes = await AssertArtifactAsync(host, result, "defaults.json").ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);

            await Assert.That(result.GetProperty("format").GetString()).IsEqualTo("JsonCompact");
            await Assert.That(document.RootElement.GetProperty("$defs").GetProperty("Record")
                .GetProperty("properties").GetProperty("Count").GetProperty("type").GetString())
                .IsEqualTo("integer");
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// Flattened concrete fields and required zero masks reach the actual PubSub schema generator.
        /// </summary>
        [Test]
        public async Task SchemaBinderGeneratesTypedPubSubArraysAndSessionlessOpenApiAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(
                new McpToolProfileSet(McpToolProfile.Schemas), artifacts: true);
            JsonElement result = await PayloadAsync(await host.InvokeAsync("schema_generate_pubsub", new JsonObject
            {
                ["outputPath"] = "fields.json",
                ["fields"] = new JsonArray(
                    new JsonObject { ["name"] = "Counts", ["dataTypeId"] = "i=6", ["valueRank"] = 2 },
                    new JsonObject { ["name"] = "Enabled", ["dataTypeId"] = "i=1" }),
                ["options"] = new JsonObject
                {
                    ["fieldContentMask"] = (uint)DataSetFieldContentMask.RawData,
                    ["messageContentMask"] = 0,
                    ["networkContentMask"] = 0,
                    ["verbose"] = false
                }
            }).ConfigureAwait(false)).ConfigureAwait(false);
            byte[] bytes = await AssertArtifactAsync(host, result, "fields.json").ConfigureAwait(false);
            using var fields = JsonDocument.Parse(bytes);
            JsonElement properties = fields.RootElement.GetProperty("properties");

            await Assert.That(result.GetProperty("kind").GetString()).IsEqualTo("Payload");
            await Assert.That(properties.GetProperty("Counts").GetProperty("type").GetString())
                .IsEqualTo("array");
            await Assert.That(properties.GetProperty("Counts").GetProperty("items")
                .GetProperty("items").GetProperty("type").GetString()).IsEqualTo("integer");
            await Assert.That(properties.GetProperty("Enabled").GetProperty("type").GetString())
                .IsEqualTo("boolean");

            JsonElement openapi = await PayloadAsync(await host.InvokeAsync("schema_generate_openapi", new JsonObject
            {
                ["outputPath"] = "rest.json",
                ["options"] = new JsonObject { ["serviceSet"] = "Sessionless" }
            }).ConfigureAwait(false)).ConfigureAwait(false);
            byte[] apiBytes = await AssertArtifactAsync(host, openapi, "rest.json").ConfigureAwait(false);
            using var api = JsonDocument.Parse(apiBytes);
            await Assert.That(openapi.GetProperty("routeCount").GetInt32()).IsEqualTo(8);
            await Assert.That(api.RootElement.GetProperty("paths").TryGetProperty("/read", out _)).IsTrue();
            await Assert.That(api.RootElement.GetProperty("paths").TryGetProperty("/createsession", out _)).IsFalse();
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// A closed array reaches the sixth schema method, which refuses a missing session without loading code.
        /// </summary>
        [Test]
        public async Task SchemaLoadBinderRequiresAnExistingSessionWithoutOpeningOneAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(McpToolProfile.Schemas));

            JsonElement result = await PayloadAsync(await host.InvokeAsync("schema_load_types", new JsonObject
            {
                ["typeIds"] = new JsonArray("i=6"),
                ["sessionName"] = "not-opened"
            }).ConfigureAwait(false), error: true).ConfigureAwait(false);

            await Assert.That(result.GetProperty("errorType").GetString()).IsEqualTo("InvalidOperationException");
            await Assert.That(result.GetProperty("message").GetString())
                .IsEqualTo("Session 'not-opened' not found or not connected. Use the 'Connect' tool first.");
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// Unknown cap-raising options fail before generating an artifact and cannot be silently ignored.
        /// </summary>
        [Test]
        public async Task SchemaBinderRejectsUnadvertisedOptionsBeforeWritingAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(
                new McpToolProfileSet(McpToolProfile.Schemas), artifacts: true);

            JsonElement result = await PayloadAsync(await host.InvokeAsync("schema_generate", new JsonObject
            {
                ["typeId"] = host.RegisterRecord().ToString(),
                ["outputPath"] = "refused.json",
                ["options"] = new JsonObject { ["maxTypes"] = 999999 }
            }).ConfigureAwait(false), error: true).ConfigureAwait(false);

            await Assert.That(result.GetProperty("message").GetString())
                .IsEqualTo("Unknown or duplicate schema option 'maxTypes'.");
            await Assert.That(Directory.GetFiles(host.Root!).Length).IsEqualTo(0);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// The production GDS array adapter binds localized names and defaults before enforcing the host gate.
        /// </summary>
        [Test]
        public async Task GdsBinderAcceptsClosedLocalizedArraysButHostDisabledRefusesIssuanceAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(
                McpToolProfileSet.Parse("gds,credentials"), enabled: false);
            GdsApplicationInput input = JsonSerializer.Deserialize(
                                     /*lang=json,strict*/
                                     """
                {"applicationUri":"urn:aot:application","productUri":"urn:aot:product",
                 "applicationNames":[{"locale":"en","text":"Pump"},{"locale":"de","text":"Pumpe"}],
                 "discoveryUrls":["opc.tcp://127.0.0.1:4840"],"serverCapabilities":["DA","HD"]}
                """, ExpandedMcpAotJsonContext.Default.GdsApplicationInput)!;
            JsonObject wire = JsonSerializer.SerializeToNode(
                input, ExpandedMcpAotJsonContext.Default.GdsApplicationInput)!.AsObject();
            JsonNode names = wire["applicationNames"]!.DeepClone();
            wire.Remove("applicationNames");

            await Assert.That(input.ApplicationType).IsEqualTo(ApplicationType.Server);
            await Assert.That(input.ApplicationId).IsNull();
            await Assert.That(input.ApplicationNames.Count).IsEqualTo(2);
            await Assert.That(input.ApplicationNames[1].Locale).IsEqualTo("de");
            await Assert.That(input.ApplicationNames[1].Text).IsEqualTo("Pumpe");
            await Assert.That(names.AsArray().Count).IsEqualTo(2);
            await Assert.That(wire["serverCapabilities"]![1]!.GetValue<string>()).IsEqualTo("HD");

            JsonElement result = await PayloadAsync(await host.InvokeAsync("gds_register_application", new JsonObject
            {
                ["directoryNodeId"] = "i=1",
                ["applicationNames"] = names,
                ["input"] = wire
            }).ConfigureAwait(false), error: true).ConfigureAwait(false);
            JsonElement query = await PayloadAsync(await host.InvokeAsync("gds_query_applications", new JsonObject
            {
                ["directoryNodeId"] = "i=1",
                ["input"] = new JsonObject()
            }).ConfigureAwait(false), error: true).ConfigureAwait(false);
            JsonElement token = await PayloadAsync(await host.InvokeAsync("credential_finish_token_request",
                new JsonObject
                {
                    ["serviceNodeId"] = "i=1",
                    ["requestId"] = "c6a79d9d-f18d-4e7c-9a4e-492c487a8621",
                    ["input"] = new JsonObject
                    {
                        ["requestedRoles"] = new JsonArray("Observer"),
                        ["proof"] = new JsonObject { ["kind"] = "Anonymous" }
                    }
                }).ConfigureAwait(false), error: true).ConfigureAwait(false);

            await Assert.That(result.GetProperty("statusCodeValue").GetUInt32())
                .IsEqualTo(StatusCodes.BadUserAccessDenied.Code);
            await Assert.That(query.GetProperty("statusCodeValue").GetUInt32())
                .IsEqualTo(StatusCodes.BadUserAccessDenied.Code);
            await Assert.That(token.GetProperty("statusCodeValue").GetUInt32())
                .IsEqualTo(StatusCodes.BadUserAccessDenied.Code);
            await Assert.That((await host.ListAsync().ConfigureAwait(false)).Tools.Count).IsEqualTo(0);
            await Assert.That(host.Store.SetCalls).IsEqualTo(0);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// A real generation-bound session reaches GDS's exact status/role projections and protected persistence.
        /// </summary>
        [Test]
        public async Task GdsKeyFinishBinderPreservesStatusAndRoleArraysWithoutReflectionAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(
                new McpToolProfileSet(McpToolProfile.Credentials), gdsSession: true);
            await using GdsKeySession session = await GdsKeySession.CreateAsync(host).ConfigureAwait(false);

            JsonElement result = await PayloadAsync(await host.InvokeAsync("credential_finish_key_request",
                new JsonObject
                {
                    ["serviceNodeId"] = session.Channel.ServiceId.ToString(),
                    ["requestId"] = session.Channel.RequestId.ToString(),
                    ["sessionName"] = GdsKeySession.Name
                }).ConfigureAwait(false)).ConfigureAwait(false);

            await Assert.That(result.GetProperty("statusCodeValue").GetUInt32())
                .IsEqualTo(GdsKeyChannel.Completion.Code);
            JsonElement arguments = result.GetProperty("inputArgumentResults");
            await Assert.That(arguments.ValueKind).IsEqualTo(JsonValueKind.Array);
            await Assert.That(arguments.GetArrayLength()).IsEqualTo(2);
            await Assert.That(arguments[0].GetUInt32()).IsEqualTo(0u);
            await Assert.That(arguments[1].GetUInt32()).IsEqualTo(0x480u);
            await Assert.That(result.GetProperty("inputArgumentResultsTruncated").GetBoolean()).IsFalse();
            await Assert.That(result.GetProperty("methodAcknowledged").GetBoolean()).IsTrue();
            await Assert.That(string.Join(",", result.GetProperty("grantedRoles").EnumerateArray()
                .Select(role => role.GetString()))).IsEqualTo("i=15644,i=15668");
            await Assert.That(result.GetProperty("grantedRoleCount").GetInt32()).IsEqualTo(2);
            await Assert.That(result.GetProperty("grantedRolesTruncated").GetBoolean()).IsFalse();
            await Assert.That(result.GetProperty("credentialId").GetString()).IsEqualTo("credential-17");
            await Assert.That(result.GetProperty("resource").GetString()).IsEqualTo("urn:aot:resource");
            await Assert.That(result.GetProperty("remoteIssuance").GetString()).IsEqualTo("issued");
            await Assert.That(result.GetProperty("sessionName").GetString()).IsEqualTo(GdsKeySession.Name);
            await Assert.That(result.GetProperty("sessionId").GetString())
                .IsEqualTo(session.Channel.SessionId.ToString());
            await Assert.That(result.GetProperty("objectId").GetString())
                .IsEqualTo(session.Channel.ServiceId.ToString());
            string reference = result.GetProperty("credential").GetProperty("reference").GetString()!;
            await Assert.That(reference.StartsWith("issued:", StringComparison.Ordinal)).IsTrue();
            await Assert.That(result.GetRawText().Contains(session.Channel.SecretText, StringComparison.Ordinal))
                .IsFalse();
            await Assert.That(result.GetRawText().Contains(Convert.ToBase64String(
                Encoding.UTF8.GetBytes(session.Channel.SecretText)), StringComparison.Ordinal)).IsFalse();
            using IDisposable caller = host.Enter();
            using ISecret secret = await host.Secrets.ResolveAsync(
                reference, "urn:aot:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
            await Assert.That(Convert.ToHexString(SHA256.HashData(secret.Bytes))).IsEqualTo(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session.Channel.SecretText))));
            await Assert.That(host.Store.SetCalls).IsEqualTo(1);
            await Assert.That(session.Factory.CreateCalls).IsEqualTo(1);
            await Assert.That(string.Join(",", session.Channel.Requests.Select(request => request.GetType().Name)))
                .IsEqualTo("BrowseRequest,TranslateBrowsePathsToNodeIdsRequest,ReadRequest,CallRequest");
            await Assert.That(host.Dns.Calls).IsEqualTo(0);
        }

        /// <summary>
        /// Client data arrays retain every quality bit and explicit zero/false while binding without a session.
        /// </summary>
        [Test]
        public async Task ClientBinderAcceptsTypedHistoryValuesAndRejectsEmptyBatchAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(McpToolProfile.History));
            WorkflowDataValuesInput input = JsonSerializer.Deserialize(
                                     /*lang=json,strict*/
                                     """
                {"values":[
                  {"type":"Boolean","value":false,
                   "sourceTimestamp":"2026-10-12T14:30:00+02:00","statusCode":1073742976},
                  {"type":"Int32","value":0,"sourceTimestamp":"2026-10-12T12:30:01Z"},
                  {"type":"UInt16","arrayLength":2,"sourceTimestamp":"2026-10-12T12:30:02Z"},
                  {"type":"UInt16","arrayLength":0,"sourceTimestamp":"2026-10-12T12:30:03Z"}],
                 "arrayElements":[{"valueIndex":2,"elementIndex":0,"value":0},
                                  {"valueIndex":2,"elementIndex":1,"value":65535}]}
                """, ExpandedMcpAotJsonContext.Default.WorkflowDataValuesInput)!;
            JsonObject wire = JsonSerializer.SerializeToNode(
                input, ExpandedMcpAotJsonContext.Default.WorkflowDataValuesInput)!.AsObject();

            await Assert.That(input.Values.Count).IsEqualTo(4);
            await Assert.That(input.Values[0].Type).IsEqualTo(BuiltInType.Boolean);
            await Assert.That(input.Values[0].Value.GetBoolean()).IsFalse();
            await Assert.That(input.Values[0].StatusCode).IsEqualTo(0x40000480u);
            await Assert.That(input.Values[0].SourceTimestamp).IsEqualTo("2026-10-12T14:30:00+02:00");
            await Assert.That(input.Values[1].Value.GetInt32()).IsEqualTo(0);
            await Assert.That(input.Values[1].StatusCode).IsEqualTo(0u);
            await Assert.That(input.Values[2].Type).IsEqualTo(BuiltInType.UInt16);
            await Assert.That(input.Values[2].ArrayLength).IsEqualTo(2);
            await Assert.That(input.Values[3].ArrayLength).IsEqualTo(0);
            await Assert.That(input.ArrayElements.Count).IsEqualTo(2);
            await Assert.That(input.ArrayElements[1].ElementIndex).IsEqualTo(1);
            await Assert.That(input.ArrayElements[1].Value.GetInt32()).IsEqualTo(65535);
            await Assert.That(wire["values"]!.AsArray().Count).IsEqualTo(4);
            await Assert.That(wire["values"]![0]!["value"]!.GetValue<bool>()).IsFalse();
            await Assert.That(wire["values"]![2]!.AsObject().ContainsKey("value")).IsFalse();
            wire["nodeId"] = "i=1";

            JsonElement accepted = await PayloadAsync(
                await host.InvokeAsync("history_insert_values", wire).ConfigureAwait(false), error: true)
                .ConfigureAwait(false);
            JsonElement empty = await PayloadAsync(await host.InvokeAsync("history_insert_values", new JsonObject
            {
                ["nodeId"] = "i=1",
                ["values"] = new JsonArray()
            }).ConfigureAwait(false), error: true).ConfigureAwait(false);

            await Assert.That(accepted.GetProperty("errorType").GetString()).IsEqualTo("InvalidOperationException");
            await Assert.That(accepted.GetProperty("message").GetString())
                .IsEqualTo("Not connected to an OPC UA server. Use the 'Connect' tool first.");
            await Assert.That(empty.GetProperty("errorType").GetString()).IsEqualTo("ArgumentOutOfRangeException");
            await Assert.That(host.Sessions.IsConnected).IsFalse();
            await Assert.That(host.Store.SetCalls).IsEqualTo(0);
        }

        /// <summary>
        /// Missing required scalars and structured fields are actual binding errors, not metadata exceptions.
        /// </summary>
        [Test]
        [Arguments(McpToolProfile.Schemas, "schema_generate",
                                 /*lang=json,strict*/
                                 """{"typeId":"i=6"}""", "outputPath")]
        [Arguments(McpToolProfile.Schemas, "schema_generate_pubsub",
            /*lang=json,strict*/
            """
            {"outputPath":"missing-mask.json","options":{"messageContentMask":0,"networkContentMask":0}}
            """,
            "fieldContentMask")]
        [Arguments(McpToolProfile.History, "history_insert_values",
                                 /*lang=json,strict*/
                                 """{"nodeId":"i=1","values":[{"type":"Int32","value":0}]}""", "sourceTimestamp")]
        [Arguments(McpToolProfile.PubSubRuntime, "pubsub_application_create",
                                 /*lang=json,strict*/
                                 """
            {"input":{},
             "connection":{"transport":"Udp","endpoint":"opc.udp://127.0.0.1:4849","networkInterface":"test-only"},
             "fields":[{"name":"Count","type":"Int32"}]}
            """, "name")]
        [Arguments(McpToolProfile.Gds, "gds_register_application",
            /*lang=json,strict*/
            """
            {"directoryNodeId":"i=1","input":{"applicationUri":"urn:aot:app","productUri":"urn:aot:product"}}
            """,
            "applicationNames")]
        public async Task RequiredParametersAreRejectedByRealBindersAsync(
            McpToolProfile profile, string tool, string json, string requiredName)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(profile), artifacts: true);

            string message = await BindingErrorAsync(host, tool, JsonNode.Parse(json)!.AsObject())
                .ConfigureAwait(false);

            await Assert.That(message.Contains(requiredName, StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(message.Contains("JsonTypeInfo", StringComparison.Ordinal)).IsFalse();
            await Assert.That(Directory.GetFiles(host.Root!).Length).IsEqualTo(0);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
            await Assert.That(host.PubSubTransports.FactoryCalls).IsEqualTo(0);
        }

        /// <summary>
        /// Typed PubSub construction remains stopped and exposes the exact discriminator, defaults and field schema.
        /// </summary>
        [Test]
        public async Task PubSubBinderCreatesReadsAndRemovesTypedStoppedApplicationAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(McpToolProfile.PubSubRuntime));
            PubSubApplicationInput input = JsonSerializer.Deserialize(
                kPubSubApplication, ExpandedMcpAotJsonContext.Default.PubSubApplicationInput)!;
            JsonNode wire = JsonSerializer.SerializeToNode(
                input, ExpandedMcpAotJsonContext.Default.PubSubApplicationInput)!;

            await Assert.That(input.Fields.Count).IsEqualTo(2);
            await Assert.That(input.Fields[0].InitialValue!.Value.GetInt32()).IsEqualTo(0);
            await Assert.That(input.Fields[1].InitialValue!.Value.GetBoolean()).IsFalse();
            await Assert.That(wire["fields"]!.AsArray().Count).IsEqualTo(2);
            await Assert.That(input.WriterGroupId).IsEqualTo((ushort)1);
            await Assert.That(input.DataSetWriterId).IsEqualTo((ushort)1);
            await Assert.That(input.DataSetName).IsEqualTo("McpDataSet");
            await Assert.That(input.PublishingIntervalMilliseconds).IsEqualTo(100);

            JsonElement created = await PayloadAsync(await host.InvokeAsync("pubsub_application_create",
                PubSubArguments()).ConfigureAwait(false)).ConfigureAwait(false);
            string handle = created.GetProperty("handle").GetString()!;
            JsonElement status = await PayloadAsync(await host.InvokeAsync("pubsub_application_status",
                new JsonObject { ["handle"] = handle }).ConfigureAwait(false)).ConfigureAwait(false);
            JsonElement read = await PayloadAsync(await host.InvokeAsync("pubsub_application_read",
                new JsonObject { ["handle"] = handle }).ConfigureAwait(false)).ConfigureAwait(false);

            await Assert.That(created.GetProperty("name").GetString()).IsEqualTo("typed-subscriber");
            await Assert.That(created.GetProperty("started").GetBoolean()).IsFalse();
            await Assert.That(created.GetProperty("legacyDefaultReplaced").GetBoolean()).IsFalse();
            await Assert.That(status.GetProperty("state").GetString()).IsEqualTo("Stopped");
            await Assert.That(status.GetProperty("isRunning").GetBoolean()).IsFalse();
            await Assert.That(status.GetProperty("publisherId").GetProperty("UaType").GetInt32())
                .IsEqualTo((int)BuiltInType.UInt16);
            await Assert.That(status.GetProperty("publisherId").GetProperty("Value").GetUInt16())
                .IsEqualTo((ushort)42);
            JsonElement fields = status.GetProperty("metadata").GetProperty("Fields");
            await Assert.That(fields.GetArrayLength()).IsEqualTo(2);
            await Assert.That(fields[0].GetProperty("Name").GetString()).IsEqualTo("Count");
            await Assert.That(fields[1].GetProperty("BuiltInType").GetInt32()).IsEqualTo((int)BuiltInType.Boolean);
            await Assert.That(status.GetProperty("writerGroupId").GetUInt16()).IsEqualTo((ushort)1);
            await Assert.That(read.GetProperty("running").GetBoolean()).IsFalse();
            await Assert.That(read.GetProperty("dataSets").GetArrayLength()).IsEqualTo(0);
            await Assert.That(read.GetProperty("bufferedBytes").GetInt64()).IsEqualTo(0L);
            await Assert.That(read.GetProperty("more").GetBoolean()).IsFalse();

            JsonElement removed = await PayloadAsync(await host.InvokeAsync("pubsub_application_remove",
                new JsonObject { ["handle"] = handle }).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.That(removed.GetProperty("removed").GetBoolean()).IsTrue();
            await Assert.That(removed.GetProperty("handle").GetString()).IsEqualTo(handle);
            using IDisposable caller = host.Enter();
            await Assert.That(host.Runtimes.List().Count).IsEqualTo(0);
            await Assert.That(host.PubSubTransports.FactoryCalls).IsEqualTo(0);
            await Assert.That(host.Dns.Calls).IsEqualTo(0);
        }

        /// <summary>
        /// Host-disabled profiles still permit offline validation but cannot create a runtime or emit secrets.
        /// </summary>
        [Test]
        public async Task DisabledHostHidesPrivilegedToolsAndNeverEchoesPlaintextSecretsAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(s_expandedProfiles, enabled: false);
            ListToolsResult catalogue = await host.ListAsync().ConfigureAwait(false);
            string[] names = [.. catalogue.Tools.Select(tool => tool.Name)];

            await Assert.That(names.Contains("roles_list", StringComparer.Ordinal)).IsTrue();
            await Assert.That(names.Contains("users_add", StringComparer.Ordinal)).IsFalse();
            await Assert.That(names.Contains("pubsub_application_validate", StringComparer.Ordinal)).IsTrue();
            await Assert.That(names.Contains("pubsub_application_create", StringComparer.Ordinal)).IsFalse();
            await Assert.That(names.Count(name => name.StartsWith("credential_", StringComparison.Ordinal)))
                .IsEqualTo(0);
            await Assert.That(names.Count(name => name.StartsWith("wot_runtime_", StringComparison.Ordinal)))
                .IsEqualTo(0);

            JsonElement validated = await PayloadAsync(await host.InvokeAsync("pubsub_application_validate",
                PubSubArguments()).ConfigureAwait(false)).ConfigureAwait(false);
            JsonElement denied = await PayloadAsync(await host.InvokeAsync("pubsub_application_create",
                PubSubArguments()).ConfigureAwait(false), error: true).ConfigureAwait(false);
            string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            JsonElement refused = await PayloadAsync(await host.InvokeAsync("credential_cancel_key_request",
                new JsonObject
                {
                    ["serviceNodeId"] = "i=1",
                    ["requestId"] = "i=2",
                    ["password"] = secret
                }).ConfigureAwait(false), error: true).ConfigureAwait(false);

            await Assert.That(validated.GetProperty("valid").GetBoolean()).IsTrue();
            await Assert.That(validated.GetProperty("started").GetBoolean()).IsFalse();
            await Assert.That(denied.GetProperty("statusCodeValue").GetUInt32())
                .IsEqualTo(StatusCodes.BadUserAccessDenied.Code);
            await Assert.That(refused.GetProperty("statusCodeValue").GetUInt32())
                .IsEqualTo(StatusCodes.BadInvalidArgument.Code);
            await Assert.That(refused.GetRawText().Contains(secret, StringComparison.Ordinal)).IsFalse();
            await Assert.That(host.Store.SetCalls).IsEqualTo(0);
            await Assert.That(host.PubSubTransports.FactoryCalls).IsEqualTo(0);
            using IDisposable caller = host.Enter();
            await Assert.That(host.Runtimes.List().Count).IsEqualTo(0);
        }

        /// <summary>
        /// Closed constructor defaults, exact IP grants and typed values reach the real runtime without sockets.
        /// </summary>
        [Test]
        [Arguments(false)]
        [Arguments(true)]
        public async Task WotBinderPreservesDefaultsFullStatusFalseZeroAndTypedArraysAsync(bool includeOptions)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(McpToolProfile.WotRuntime));
            JsonObject arguments = WotOpenArguments(["readproperty", "writeproperty"], includeOptions);
            WotRuntimeSelection selection = JsonSerializer.Deserialize(arguments["selection"]!.ToJsonString(),
                ExpandedMcpAotJsonContext.Default.WotRuntimeSelection)!;
            WotRuntimeOpenOptions options = JsonSerializer.Deserialize("{}",
                ExpandedMcpAotJsonContext.Default.WotRuntimeOpenOptions)!;

            await Assert.That(selection.DocumentReference).IsEqualTo(string.Empty);
            await Assert.That(selection.Kind).IsEqualTo(WotAffordanceKind.Property);
            await Assert.That(selection.FormIndex).IsEqualTo(0);
            await Assert.That(options.BaseUri).IsEqualTo("https://mcp.invalid/root.json");
            await Assert.That(options.DocumentSetArchive).IsFalse();
            await Assert.That(options.SessionName).IsNull();

            JsonElement opened = await PayloadAsync(
                await host.InvokeAsync("wot_runtime_open", arguments).ConfigureAwait(false))
                .ConfigureAwait(false);
            string handle = opened.GetProperty("runtime").GetProperty("handle").GetString()!;
            await Assert.That(opened.GetProperty("formIndex").GetInt32()).IsEqualTo(0);
            await Assert.That(opened.GetProperty("sha256").GetString()).IsEqualTo(selection.Sha256);
            await Assert.That(opened.GetProperty("bindingId").GetString()).IsEqualTo("w3c.http");
            await Assert.That(opened.GetProperty("channelsOpened").GetInt32()).IsEqualTo(0);
            await Assert.That(opened.GetProperty("operations").GetArrayLength()).IsEqualTo(2);
            await Assert.That(host.WotExecutors.Opened.Count).IsEqualTo(0);

            JsonElement read = await PayloadAsync(await host.InvokeAsync("wot_runtime_read_property",
                new JsonObject { ["handle"] = handle }).ConfigureAwait(false)).ConfigureAwait(false);
            using var decoder = new JsonDecoder(read.GetRawText(), host.Context);
            DataValue value = decoder.ReadDataValue("dataValue");
            bool isBoolean = value.WrappedValue.TryGetValue(out bool flag);
            await Assert.That(isBoolean).IsTrue();
            await Assert.That(flag).IsFalse();
            await Assert.That(value.StatusCode.Code).IsEqualTo(0x40000480u);
            await Assert.That(value.SourceTimestamp).IsEqualTo(LocalWotExecutors.SourceTime);
            await Assert.That(value.ServerTimestamp).IsEqualTo(LocalWotExecutors.ServerTime);
            await Assert.That(read.GetProperty("statusCodeValue").GetUInt32()).IsEqualTo(0x40000480u);
            await Assert.That(read.GetProperty("completion").GetString()).IsEqualTo("reportedFailure");
            await Assert.That(read.GetProperty("automaticallyRetried").GetBoolean()).IsFalse();

            foreach (string json in new[]
            {
                /*lang=json,strict*/
                                     """{"type":"Boolean","value":false}""",
                /*lang=json,strict*/
                                     """{"type":"Int32","value":0}""",
                /*lang=json,strict*/
                                     """{"type":"UInt16","value":[0,65535]}"""
            })
            {
                WotRuntimeValue input = JsonSerializer.Deserialize(
                    json, ExpandedMcpAotJsonContext.Default.WotRuntimeValue)!;
                JsonNode wire = JsonSerializer.SerializeToNode(
                    input, ExpandedMcpAotJsonContext.Default.WotRuntimeValue)!;
                JsonElement written = await PayloadAsync(await host.InvokeAsync("wot_runtime_write_property",
                    new JsonObject { ["handle"] = handle, ["value"] = wire }).ConfigureAwait(false))
                    .ConfigureAwait(false);
                await Assert.That(written.GetProperty("statusCodeValue").GetUInt32()).IsEqualTo(0u);
                await Assert.That(written.GetProperty("completion").GetString()).IsEqualTo("completed");
                await Assert.That(written.GetProperty("automaticallyRetried").GetBoolean()).IsFalse();
            }

            await Assert.That(host.WotExecutors.Writes.Count).IsEqualTo(3);
            bool wroteBoolean = host.WotExecutors.Writes[0].WrappedValue.TryGetValue(out bool writtenFlag);
            bool wroteInteger = host.WotExecutors.Writes[1].WrappedValue.TryGetValue(out int writtenZero);
            bool wroteArray = host.WotExecutors.Writes[2].WrappedValue.TryGetValue(out ArrayOf<ushort> writtenArray);
            await Assert.That(wroteBoolean).IsTrue();
            await Assert.That(writtenFlag).IsFalse();
            await Assert.That(wroteInteger).IsTrue();
            await Assert.That(writtenZero).IsEqualTo(0);
            await Assert.That(wroteArray).IsTrue();
            await Assert.That(writtenArray.Count).IsEqualTo(2);
            await Assert.That(writtenArray[0]).IsEqualTo((ushort)0);
            await Assert.That(writtenArray[1]).IsEqualTo(ushort.MaxValue);
            await Assert.That(host.WotExecutors.ReadCalls).IsEqualTo(1);
            await Assert.That(string.Join(",", host.WotExecutors.Opened.Select(form => form.OpToken)))
                .IsEqualTo("readproperty,writeproperty");
            await Assert.That(string.Join(",", host.WotExecutors.GrantedOperations)).IsEqualTo("GET,PUT");

            JsonElement closed = await PayloadAsync(await host.InvokeAsync("wot_runtime_close",
                new JsonObject { ["handle"] = handle }).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.That(closed.GetProperty("closed").GetBoolean()).IsTrue();
            await Assert.That(closed.GetProperty("handle").GetString()).IsEqualTo(handle);
            await Assert.That(host.WotExecutors.DisposedChannels).IsEqualTo(2);
            await Assert.That(host.Dns.Calls).IsEqualTo(0);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// Listing supported writes or selecting the runtime profile cannot grant a caller write authority.
        /// </summary>
        [Test]
        [Arguments(true, false, 1)]
        [Arguments(false, true, 0)]
        public async Task WotCapabilityCatalogueDoesNotGrantWritePermissionAsync(
            bool hostWriteEnabled, bool callerWriteGranted, int visibleWriteTools)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(
                new McpToolProfileSet(McpToolProfile.WotRuntime),
                writeGranted: callerWriteGranted, writeEnabled: hostWriteEnabled);
            ListToolsResult catalogue = await host.ListAsync().ConfigureAwait(false);
            JsonElement capabilities = await PayloadAsync(
                await host.InvokeAsync("wot_runtime_capabilities", []).ConfigureAwait(false)).ConfigureAwait(false);
            JsonElement opened = await PayloadAsync(await host.InvokeAsync("wot_runtime_open",
                WotOpenArguments(["readproperty"], includeOptions: false)).ConfigureAwait(false)).ConfigureAwait(false);
            string handle = opened.GetProperty("runtime").GetProperty("handle").GetString()!;

            // This is host catalogue enablement, deliberately not a claim that this caller may write.
            await Assert.That(catalogue.Tools.Count(tool => tool.Name == "wot_runtime_write_property"))
                .IsEqualTo(visibleWriteTools);
            await Assert.That(string.Join(",", capabilities.GetProperty("bindings").EnumerateArray()
                .Where(binding => binding.GetProperty("executable").GetBoolean())
                .Select(binding => binding.GetProperty("bindingId").GetString()).Order(StringComparer.Ordinal)))
                .IsEqualTo("opc.opcua,w3c.http,w3c.modbus,w3c.mqtt");
            await Assert.That(capabilities.GetProperty("automaticMutationRetries").GetBoolean()).IsFalse();

            JsonElement refused = await PayloadAsync(await host.InvokeAsync("wot_runtime_write_property",
                new JsonObject
                {
                    ["handle"] = handle,
                    ["value"] = new JsonObject { ["type"] = "Int32", ["value"] = 0 }
                }).ConfigureAwait(false), error: true).ConfigureAwait(false);

            await Assert.That(refused.GetProperty("statusCodeValue").GetUInt32())
                .IsEqualTo(StatusCodes.BadUserAccessDenied.Code);
            await Assert.That(host.WotExecutors.Writes.Count).IsEqualTo(0);
            await Assert.That(host.WotExecutors.Opened.Count).IsEqualTo(0);
            using IDisposable caller = host.Enter();
            await Assert.That(host.Runtimes.List().Count).IsEqualTo(1);
        }

        /// <summary>
        /// Runtime and workflow wrappers reject authority-shaped root additions before binding or side effects.
        /// </summary>
        [Test]
        [Arguments(McpToolProfile.WotRuntime, "wot_runtime_list")]
        [Arguments(McpToolProfile.History, "history_capabilities")]
        [Arguments(McpToolProfile.PubSubRuntime, "pubsub_application_list")]
        public async Task ExpandedBindersRejectCallerSuppliedAuthorityAsync(McpToolProfile profile, string tool)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(profile));
            string ownerClaim = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            JsonElement refused = await PayloadAsync(await host.InvokeAsync(tool,
                new JsonObject { ["owner"] = ownerClaim }).ConfigureAwait(false), error: true).ConfigureAwait(false);

            await Assert.That(refused.GetProperty("message").GetString()!.Contains(
                "argument", StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(refused.GetRawText().Contains(ownerClaim, StringComparison.Ordinal)).IsFalse();
            await Assert.That(host.WotExecutors.Opened.Count).IsEqualTo(0);
            await Assert.That(host.PubSubTransports.FactoryCalls).IsEqualTo(0);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// Closed DTO metadata rejects nested authority additions, not merely unknown top-level arguments.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [Test]
        [Arguments(McpToolProfile.Gds, "gds_query_applications")]
        [Arguments(McpToolProfile.History, "history_insert_values")]
        [Arguments(McpToolProfile.Schemas, "schema_generate_pubsub")]
        [Arguments(McpToolProfile.PubSubRuntime, "pubsub_application_create")]
        [Arguments(McpToolProfile.WotRuntime, "wot_runtime_open")]
        public async Task ProductionDtoBindersRejectUnmappedNestedMembersAsync(McpToolProfile profile, string tool)
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(profile), artifacts: true);
            JsonObject arguments = profile switch
            {
                McpToolProfile.Gds => JsonNode.Parse(
                    """{"directoryNodeId":"i=1","input":{"writeGranted":true}}""")!.AsObject(),
                McpToolProfile.History => JsonNode.Parse(
                    """
                    {"nodeId":"i=1","values":[{"type":"Int32","value":0,
                     "sourceTimestamp":"2026-10-12T00:00:00Z","writeGranted":true}]}
                    """)!.AsObject(),
                McpToolProfile.Schemas => JsonNode.Parse(
                    """
                    {"outputPath":"closed.json",
                     "fields":[{"name":"Count","dataTypeId":"i=6","writeGranted":true}],
                     "options":{"fieldContentMask":0,"messageContentMask":0,"networkContentMask":0}}
                    """)!.AsObject(),
                McpToolProfile.PubSubRuntime => PubSubArguments(),
                McpToolProfile.WotRuntime => WotOpenArguments(["readproperty"], includeOptions: false),
                _ => throw new ArgumentOutOfRangeException(nameof(profile))
            };
            if (profile == McpToolProfile.PubSubRuntime)
            {
                arguments["connection"]!["writeGranted"] = true;
            }
            if (profile == McpToolProfile.WotRuntime)
            {
                arguments["selection"]!["writeGranted"] = true;
            }

            string error = await BindingErrorAsync(host, tool, arguments).ConfigureAwait(false);

            string expected = profile == McpToolProfile.WotRuntime ? "bounded typed schema" : "writeGranted";
            await Assert.That(error.Contains(expected, StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(Directory.GetFiles(host.Root!).Length).IsEqualTo(0);
            await Assert.That(host.PubSubTransports.FactoryCalls).IsEqualTo(0);
            await Assert.That(host.WotExecutors.Opened.Count).IsEqualTo(0);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        /// <summary>
        /// GDS-owned access/refresh references expose only metadata and cannot alias host-owned secret bytes.
        /// </summary>
        [Test]
        public async Task GdsOwnedTokenReferencesKeepImmutableStoreBytesAndCallerOwnershipAsync()
        {
            await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
            await using var host = new ExpandedHost(new McpToolProfileSet(McpToolProfile.Credentials));
            byte[] accessBytes = RandomNumberGenerator.GetBytes(32);
            byte[] refreshBytes = RandomNumberGenerator.GetBytes(32);
            string expectedAccessDigest = Convert.ToHexString(SHA256.HashData(accessBytes));
            string expectedRefreshDigest = Convert.ToHexString(SHA256.HashData(refreshBytes));
            McpSecretReference access;
            McpSecretReference refresh;
            DateTimeOffset expires = host.Clock.GetUtcNow().AddMinutes(5);
            using (host.Enter())
            {
                using McpSecretReservation reservation = host.Secrets.ReserveIssuance(
                    McpCapability.CredentialIssuance, 2);
                access = await reservation.StoreAsync(
                    ByteString.From(accessBytes), "access-token", "urn:aot:resource", expires).ConfigureAwait(false);
                refresh = await reservation.StoreAsync(
                    ByteString.From(refreshBytes), "refresh-token", "urn:aot:resource", expires).ConfigureAwait(false);
            }
            JsonElement metadata = JsonSerializer.SerializeToElement(
                access, ExpandedMcpAotJsonContext.Default.McpSecretReference);
            await Assert.That(PropertyNames(metadata))
                .IsEqualTo("credentialId,expiresAt,issuer,purpose,reference,resource");
            await Assert.That(metadata.GetProperty("purpose").GetString()).IsEqualTo("access-token");
            await Assert.That(metadata.GetProperty("resource").GetString()).IsEqualTo("urn:aot:resource");
            await Assert.That(metadata.GetProperty("expiresAt").GetDateTimeOffset()).IsEqualTo(expires);
            await Assert.That(access.Reference.StartsWith("issued:", StringComparison.Ordinal)).IsTrue();
            await Assert.That(access.Reference == refresh.Reference).IsFalse();
            await Assert.That(metadata.GetRawText().Contains(
                Convert.ToBase64String(accessBytes), StringComparison.Ordinal)).IsFalse();

            CryptographicOperations.ZeroMemory(accessBytes);
            CryptographicOperations.ZeroMemory(refreshBytes);
            using (host.Enter())
            {
                using (ISecret first = await host.Secrets.ResolveAsync(
                    access.Reference, "urn:aot:resource", McpCapability.CredentialIssuance).ConfigureAwait(false))
                {
                    await Assert.That(Convert.ToHexString(SHA256.HashData(first.Bytes)))
                        .IsEqualTo(expectedAccessDigest);
                    await Assert.That(host.Store.DisposedViews).IsEqualTo(1);
                }
                using ISecret second = await host.Secrets.ResolveAsync(
                    access.Reference, "urn:aot:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
                await Assert.That(Convert.ToHexString(SHA256.HashData(second.Bytes)))
                    .IsEqualTo(expectedAccessDigest);
            }
            int lookups = host.Store.LookupCalls;
            using (host.Enter(ExpandedHost.Other))
            {
                StatusCode refusal = StatusCodes.Good;
                await Assert.That(async () =>
                {
                    try
                    {
                        using ISecret secret = await host.Secrets.ResolveAsync(
                            access.Reference, "urn:aot:resource", McpCapability.CredentialIssuance)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException failure)
                    {
                        refusal = failure.StatusCode;
                        throw;
                    }
                }).Throws<ServiceResultException>();
                await Assert.That(refusal.Code).IsEqualTo(StatusCodes.BadUserAccessDenied.Code);
            }
            await Assert.That(host.Store.LookupCalls).IsEqualTo(lookups);
            await Assert.That(host.Store.Count).IsEqualTo(2);
            using (host.Enter())
            {
                await host.Secrets.RevokeReferenceAsync(access.Reference).ConfigureAwait(false);
                using ISecret remaining = await host.Secrets.ResolveAsync(
                    refresh.Reference, "urn:aot:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
                await Assert.That(Convert.ToHexString(SHA256.HashData(remaining.Bytes)))
                    .IsEqualTo(expectedRefreshDigest);
            }
            await Assert.That(host.Store.SetCalls).IsEqualTo(2);
            await Assert.That(host.Store.RemoveCalls).IsEqualTo(1);
            await Assert.That(host.Store.Count).IsEqualTo(1);
            await Assert.That(host.Sessions.IsConnected).IsFalse();
        }

        private static JsonObject PubSubArguments()
        {
            JsonObject input = JsonNode.Parse(kPubSubApplication)!.AsObject();
            JsonNode connection = input["connection"]!.DeepClone();
            JsonNode fields = input["fields"]!.DeepClone();
            input.Remove("connection");
            input.Remove("fields");
            return new JsonObject { ["input"] = input, ["connection"] = connection, ["fields"] = fields };
        }

        private static JsonObject WotOpenArguments(ArrayOf<string> operations, bool includeOptions)
        {
            var selected = new WotRuntimeSelection
            {
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kPropertyDocument))),
                Affordance = "temperature",
                BindingId = "w3c.http",
                BindingVersion = "1.1"
            };
            JsonObject selection = JsonSerializer.SerializeToNode(
                selected, ExpandedMcpAotJsonContext.Default.WotRuntimeSelection)!.AsObject();
            selection.Remove("documentReference");
            selection.Remove("kind");
            selection.Remove("formIndex");
            var requested = new JsonArray();
            foreach (string operation in operations)
            {
                requested.Add((JsonNode?)operation);
            }
            var result = new JsonObject
            {
                ["name"] = "typed-property",
                ["document"] = new JsonObject { ["text"] = kPropertyDocument },
                ["selection"] = selection,
                ["operations"] = requested,
                ["dependencies"] = new JsonArray(),
                ["credentials"] = new JsonArray()
            };
            if (includeOptions)
            {
                result["options"] = new JsonObject();
            }
            return result;
        }

        private static async Task<string> BindingErrorAsync(ExpandedHost host, string name, JsonObject arguments)
        {
            try
            {
                CallToolResult result = await host.InvokeAsync(name, arguments).ConfigureAwait(false);
                await Assert.That(result.IsError ?? false).IsTrue();
                return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            }
            catch (McpProtocolException failure)
            {
                await Assert.That(failure.ErrorCode).IsEqualTo(McpErrorCode.InvalidParams);
                return BindingMessages(failure);
            }
            catch (ArgumentException failure)
            {
                // Missing arguments and JSON conversion failures are SDK binding errors; missing metadata is not.
                return BindingMessages(failure);
            }
            catch (JsonException failure)
            {
                return BindingMessages(failure);
            }
        }

        private static string BindingMessages(Exception failure)
        {
            var messages = new List<string>();
            for (Exception? current = failure; current is not null; current = current.InnerException)
            {
                if (current is NotSupportedException)
                {
                    throw new InvalidOperationException("Missing metadata is not an expected binding error.", current);
                }
                messages.Add(current.Message);
            }
            return string.Join("\n", messages);
        }

        private static async Task<JsonElement> PayloadAsync(CallToolResult result, bool error = false)
        {
            await Assert.That(result.IsError ?? false).IsEqualTo(error)
                .Because(result.StructuredContent?.GetRawText() ??
                    string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
            await Assert.That(result.StructuredContent.HasValue).IsTrue();
            JsonElement payload = result.StructuredContent!.Value;
            await Assert.That(result.Content.Count).IsEqualTo(1);
            await Assert.That(((TextContentBlock)result.Content[0]).Text).IsEqualTo(payload.GetRawText());
            return payload;
        }

        private static async Task<byte[]> AssertArtifactAsync(ExpandedHost host, JsonElement result, string name)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(host.Root!, name)).ConfigureAwait(false);
            await Assert.That(result.GetProperty("filePath").GetString()).IsEqualTo(name);
            await Assert.That(result.GetProperty("bytesTransferred").GetInt64()).IsEqualTo(bytes.LongLength);
            await Assert.That(result.GetProperty("sha256").GetString())
                .IsEqualTo(Convert.ToHexString(SHA256.HashData(bytes)));
            return bytes;
        }

        private static async Task AssertRootAsync(JsonElement schema, string names, int maximumDepth)
        {
            JsonElement properties = schema.GetProperty("properties");
            await Assert.That(PropertyNames(properties)).IsEqualTo(names);
            await Assert.That(properties.EnumerateObject().Count() <= 8).IsTrue();
            await Assert.That(schema.GetProperty("additionalProperties").GetBoolean()).IsFalse();
            await Assert.That(ContainerDepth(schema) <= maximumDepth).IsTrue();
        }

        private static async Task AssertArrayAsync(JsonElement schema, int minimum, int maximum, string itemType)
        {
            await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("array");
            await Assert.That(schema.GetProperty("minItems").GetInt32()).IsEqualTo(minimum);
            await Assert.That(schema.GetProperty("maxItems").GetInt32()).IsEqualTo(maximum);
            if (itemType == "object")
            {
                // Generated root DTO schemas may also admit null; their concrete members still identify the type.
                await Assert.That(schema.GetProperty("items").GetProperty("properties").ValueKind)
                    .IsEqualTo(JsonValueKind.Object);
            }
            else
            {
                await Assert.That(schema.GetProperty("items").GetProperty("type").GetString()).IsEqualTo(itemType);
            }
        }

        private static string PropertyNames(JsonElement properties)
        {
            return string.Join(",", properties.EnumerateObject().Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        }

        private static string RequiredNames(JsonElement schema)
        {
            return string.Join(",", schema.GetProperty("required").EnumerateArray()
                .Select(value => value.GetString()).Order(StringComparer.Ordinal));
        }

        private static int ContainerDepth(JsonElement schema)
        {
            return ContainerDepth(schema, schema, 0);
        }

        private static int ContainerDepth(JsonElement schema, JsonElement root, int references)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }
            if (schema.TryGetProperty("$ref", out JsonElement reference))
            {
                string path = reference.GetString()!;
                if (references >= 16 || !path.StartsWith("#/$defs/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Expected bounded local schema definitions.");
                }
                return ContainerDepth(root.GetProperty("$defs").GetProperty(path["#/$defs/".Length..]),
                    root, references + 1);
            }
            int depth = 0;
            if (schema.TryGetProperty("properties", out JsonElement properties))
            {
                depth = 1;
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    depth = Math.Max(depth, 1 + ContainerDepth(property.Value, root, references));
                }
            }
            if (schema.TryGetProperty("items", out JsonElement items))
            {
                depth = Math.Max(depth, 1 + ContainerDepth(items, root, references));
            }
            foreach (string union in new[] { "anyOf", "oneOf", "allOf" })
            {
                if (schema.TryGetProperty(union, out JsonElement alternatives))
                {
                    foreach (JsonElement alternative in alternatives.EnumerateArray())
                    {
                        depth = Math.Max(depth, ContainerDepth(alternative, root, references));
                    }
                }
            }
            return depth;
        }

        private const string kTypeNamespace = "urn:aot:expanded:types";

        private const string kPubSubApplication = """
            {"name":"typed-subscriber","mode":"Subscriber",
             "connection":{"transport":"Udp","endpoint":"opc.udp://127.0.0.1:4849","networkInterface":"test-only"},
             "publisherId":{"type":"UInt16","value":42},
             "fields":[{"name":"Count","type":"Int32","initialValue":0},
                       {"name":"Enabled","type":"Boolean","initialValue":false}]}
            """;

        private const string kPropertyDocument = /*lang=json,strict*/ """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "id": "urn:aot:expanded:property",
              "title": "Offline executor fixture",
              "securityDefinitions": {"nosec_sc": {"scheme": "nosec"}},
              "security": "nosec_sc",
              "properties": {
                "temperature": {
                  "type": "number",
                  "forms": [{"href":"http://127.0.0.1:8080/value","op":["readproperty","writeproperty"]}]
                }
              }
            }
            """;

        private static readonly McpToolProfileSet s_expandedProfiles = McpToolProfileSet.Parse(
            "gds,certificates,credentials,onboarding,roles,users,files,alarms,history,schemas," +
            "pubsub-runtime,pubsub-configuration,pubsub-bridge,pubsub-security,wot-runtime");

        /// <summary>
        /// Resolves only the approved new graph, preserving its production serializers and filter registrations.
        /// </summary>
        private sealed class ExpandedHost : IAsyncDisposable
        {
            internal ExpandedHost(
                McpToolProfileSet profiles, bool artifacts = false, bool enabled = true,
                bool writeGranted = true, bool writeEnabled = true, bool gdsSession = false)
            {
                if (artifacts)
                {
                    // OS temporary directories can have linked ancestors, which transfer policy correctly rejects.
                    Root = Path.Combine(AppContext.BaseDirectory, "opc-ua-expanded-aot-" + Guid.NewGuid().ToString("N"));
                }
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IMcpDnsResolver>(Dns);
                services.AddOpcUaMcpSecrets(new SecretRegistry(Store), issuanceStore: Store);
                services.AddOpcUaMcpCore(new OpcUaMcpOptions
                {
                    TransferRoot = Root,
                    MaxTransferBytes = 16 * 1024 * 1024,
                    Execution = new McpExecutionOptions
                    {
                        EnabledCapabilities = !enabled ? [] : writeEnabled ? s_capabilities :
                            s_capabilities.ToList().Where(value => value != McpCapability.WotWrite).ToArrayOf(),
                        CallerGrants =
                        [
                            new McpCallerGrant
                            {
                                Caller = Owner,
                                SessionNames = gdsSession ? [GdsKeySession.Name] : [],
                                Capabilities = writeGranted
                                    ? s_capabilities : s_capabilities.ToList()
                                        .Where(value => value != McpCapability.WotWrite).ToArrayOf()
                            },
                            new McpCallerGrant { Caller = Other, Capabilities = s_capabilities }
                        ],
                        NetworkRules =
                        [
                            NetworkRule(McpCapability.WotRuntime, "GET"),
                            NetworkRule(McpCapability.WotWrite, "PUT"),
                            NetworkRule(McpCapability.WotInvoke, "POST")
                        ]
                    }
                });
                services.AddSingleton<IServiceMessageContext>(Context);
                services.AddSingleton<IMcpPubSubTransportProvider>(PubSubTransports);
                services.AddSingleton<IWotRuntimeExecutorFactory>(WotExecutors);
                services.AddOpcUaMcpGds().AddOpcUaMcpWorkflows().AddOpcUaMcpSchema()
                    .AddOpcUaMcpPubSub().AddOpcUaMcpWotRuntime();
                IMcpServerBuilder builder = services.AddMcpServer()
                    .WithStreamServerTransport(Stream.Null, Stream.Null).WithOpcUaConnectionTools();
                ServiceDescriptor[] legacy = [.. services.Where(value => value.ServiceType == typeof(McpServerTool))];
                IsolatedConnectionCount = legacy.Length;

                Register(builder, profiles);
                Register(builder, profiles);

                // Only this known legacy set is isolated; every new factory must resolve successfully.
                foreach (ServiceDescriptor descriptor in legacy)
                {
                    services.Remove(descriptor);
                }
                Services = services.BuildServiceProvider();
                Tools = [.. Services.GetServices<McpServerTool>()];
                if (Root is not null)
                {
                    Directory.CreateDirectory(Root);
                }
            }

            internal static McpCallerIdentity Owner { get; } = new("expanded-aot-owner", "urn:aot:authority");
            internal static McpCallerIdentity Other { get; } = new("expanded-aot-other", "urn:aot:authority");
            internal ServiceProvider Services { get; }
            internal McpServerTool[] Tools { get; }
            internal int IsolatedConnectionCount { get; }
            internal string? Root { get; }

            internal ServiceMessageContext Context { get; } =
                ServiceMessageContext.Create(DefaultTelemetry.Create(static _ => { }));

            internal ushort NameNamespace { get; private set; }
            internal FrozenClock Clock { get; } = new();
            internal LocalDns Dns { get; } = new();
            internal ImmutableSecretStoreFake Store { get; } = new();
            internal NoNetworkPubSubTransports PubSubTransports { get; } = new();
            internal LocalWotExecutors WotExecutors { get; } = new();
            internal OpcUaSessionManager Sessions => Services.GetRequiredService<OpcUaSessionManager>();
            internal McpRuntimeRegistry Runtimes => Services.GetRequiredService<McpRuntimeRegistry>();
            internal McpSecretReferences Secrets => Services.GetRequiredService<McpSecretReferences>();

            internal McpServerTool Tool(string name)
            {
                return Tools.Single(tool => tool.ProtocolTool.Name == name);
            }

            internal IDisposable Enter(McpCallerIdentity? caller = null)
            {
                return Services.GetRequiredService<McpCallerContext>().EnterTrustedCaller(caller ?? Owner);
            }

            internal NodeId RegisterRecord()
            {
                ushort typeNamespace = Context.NamespaceUris.GetIndexOrAppend(kTypeNamespace);
                NameNamespace = Context.NamespaceUris.GetIndexOrAppend("urn:aot:expanded:names");
                var id = new NodeId("Record", typeNamespace);
                Services.GetRequiredService<DataTypeDefinitionRegistry>().Add(new UaTypeDescription(
                    id, new QualifiedName("Record", NameNamespace), new StructureDefinition
                    {
                        BaseDataType = DataTypeIds.Structure,
                        StructureType = StructureType.Structure,
                        Fields =
                        [
                            new StructureField
                            {
                                Name = "Count", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar
                            },
                            new StructureField
                            {
                                Name = "Enabled", DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.Scalar
                            }
                        ]
                    }, kTypeNamespace));
                return id;
            }

            internal async Task<CallToolResult> InvokeAsync(string name, JsonObject arguments)
            {
                using IDisposable caller = Enter();
                using var document = JsonDocument.Parse(arguments.ToJsonString());
                var original = document.RootElement.EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value.Clone());
                McpServerTool tool = Tool(name);
                var parameters = new CallToolRequestParams { Name = name, Arguments = original };
                var request = new RequestContext<CallToolRequestParams>(Services.GetRequiredService<McpServer>(),
                    new JsonRpcRequest { Method = "tools/call", Id = new RequestId("expanded-aot") }, parameters)
                {
                    MatchedPrimitive = tool
                };
                try
                {
                    return await tool.InvokeAsync(request).ConfigureAwait(false);
                }
                finally
                {
                    await Assert.That(ReferenceEquals(parameters.Arguments, original)).IsTrue();
                    await Assert.That(PropertyNames(document.RootElement)).IsEqualTo(
                        string.Join(",", original.Keys.Order(StringComparer.Ordinal)));
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        await Assert.That(original[property.Name].GetRawText()).IsEqualTo(property.Value.GetRawText());
                    }
                }
            }

            internal async Task<ListToolsResult> ListAsync()
            {
                using IDisposable caller = Enter();
                McpServerOptions options = Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
                McpRequestHandler<ListToolsRequestParams, ListToolsResult> handler = (_, _) =>
                    ValueTask.FromResult(new ListToolsResult
                    {
                        Tools = [.. Tools.Select(tool => tool.ProtocolTool)]
                    });
                foreach (McpRequestFilter<ListToolsRequestParams, ListToolsResult>? filter in
                    options.Filters.Request.ListToolsFilters.AsEnumerable().Reverse())
                {
                    handler = filter(handler);
                }
                var request = new RequestContext<ListToolsRequestParams>(Services.GetRequiredService<McpServer>(),
                    new JsonRpcRequest { Method = "tools/list", Id = new RequestId("expanded-aot-list") },
                    new ListToolsRequestParams());
                return await handler(request, CancellationToken.None).ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                await Services.DisposeAsync().ConfigureAwait(false);
                if (Root is not null)
                {
                    Directory.Delete(Root, recursive: true);
                }
            }

            private static void Register(IMcpServerBuilder builder, McpToolProfileSet profiles)
            {
                builder.WithOpcUaGdsTools(profiles).WithOpcUaWorkflowTools(profiles)
                    .WithOpcUaSchemaTools(profiles).WithOpcUaPubSubTools(profiles)
                    .WithOpcUaWotRuntimeTools(profiles);
            }

            private static McpNetworkRule NetworkRule(McpCapability capability, string operation)
            {
                return new McpNetworkRule
                {
                    Capability = capability,
                    Scheme = "http",
                    Host = "127.0.0.1",
                    Port = 8080,
                    Networks = ["127.0.0.1/32"],
                    Operations = [operation],
                    Resources = ["/value"]
                };
            }

            private static readonly ArrayOf<McpCapability> s_capabilities =
            [
                McpCapability.DirectoryAdministration, McpCapability.CertificateAdministration,
                McpCapability.CredentialIssuance, McpCapability.Onboarding, McpCapability.IdentityAdministration,
                McpCapability.PubSubRuntime, McpCapability.PubSubConfiguration, McpCapability.PubSubBridge,
                McpCapability.PubSubSecurity, McpCapability.WotRuntime, McpCapability.WotWrite, McpCapability.WotInvoke
            ];
        }

        /// <summary>
        /// Captures a genuine ManagedSession generation over a four-request in-memory service transport.
        /// </summary>
        private sealed class GdsKeySession : IAsyncDisposable
        {
            private GdsKeySession(ExpandedHost host)
            {
                ITelemetryContext telemetry = host.Context.Telemetry;
                host.Context.NamespaceUris.GetIndexOrAppend(Gds.Namespaces.OpcUaGds);
                ushort instanceNamespace = host.Context.NamespaceUris.GetIndexOrAppend("urn:aot:gds:instances");
                var description = new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://127.0.0.1:4840",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    Server = new ApplicationDescription { ApplicationUri = "urn:aot:gds:issuer" }
                };
                m_endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create());
                m_configuration = new ApplicationConfiguration(telemetry)
                {
                    ClientConfiguration = new ClientConfiguration(),
                    SecurityConfiguration = new SecurityConfiguration()
                };
                Channel = new GdsKeyChannel(host.Context, m_endpoint, instanceNamespace);
                m_inner = new Session(Channel, m_configuration, m_endpoint,
                    engineFactory: DefaultSubscriptionEngineFactory.Instance, timeProvider: host.Clock);
                m_inner.NamespaceUris.Update(host.Context.NamespaceUris.ToArray());
                m_inner.SessionCreated(Channel.SessionId, Channel.AuthenticationId);
                Factory = new GdsSessionFactory(m_inner, telemetry)
                {
                    SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance,
                    TimeProvider = host.Clock
                };
            }

            internal const string Name = "aot-key-service";
            internal GdsKeyChannel Channel { get; }
            internal GdsSessionFactory Factory { get; }

            internal static async Task<GdsKeySession> CreateAsync(ExpandedHost host)
            {
                var fixture = new GdsKeySession(host);
                try
                {
                    fixture.m_managed = await ManagedSession.CreateAsync(
                        fixture.m_configuration, fixture.m_endpoint, fixture.Factory,
                        telemetry: host.Context.Telemetry, sessionName: Name,
                        timeProvider: host.Clock, updateBeforeConnect: false).ConfigureAwait(false);
                    await host.Sessions.RegisterExistingSessionAsync(Name, fixture.m_managed, "Anonymous")
                        .ConfigureAwait(false);
                    return fixture;
                }
                catch
                {
                    await fixture.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                m_inner.DeleteSubscriptionsOnClose = false;
                m_inner.SessionCreated(default, default);
                if (m_managed is not null)
                {
                    await m_managed.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await m_inner.DisposeAsync().ConfigureAwait(false);
                }
            }

            private readonly ApplicationConfiguration m_configuration;
            private readonly ConfiguredEndpoint m_endpoint;
            private readonly Session m_inner;
            private ManagedSession? m_managed;
        }

        /// <summary>
        /// Uses the public managed-session factory path without discovery, sockets or a fabricated request context.
        /// </summary>
        private sealed class GdsSessionFactory(Session session, ITelemetryContext telemetry)
            : DefaultSessionFactory(telemetry)
        {
            internal int CreateCalls { get; private set; }

            public override Task<ISession> CreateAsync(
                ApplicationConfiguration configuration, ConfiguredEndpoint endpoint,
                bool updateBeforeConnect, bool checkDomain, string sessionName, uint sessionTimeout,
                IUserIdentity? identity, ArrayOf<string> preferredLocales, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (updateBeforeConnect || endpoint.Description.EndpointUrl != "opc.tcp://127.0.0.1:4840")
                {
                    throw new InvalidOperationException("The GDS fixture must not discover or change its endpoint.");
                }
                CreateCalls++;
                return Task.FromResult<ISession>(session);
            }
        }

        /// <summary>
        /// Accepts only the exact typed service requests needed by KeyCredential FinishRequest.
        /// </summary>
        private sealed class GdsKeyChannel : ITransportChannel
        {
            internal GdsKeyChannel(
                IServiceMessageContext context, ConfiguredEndpoint endpoint, ushort instanceNamespace)
            {
                MessageContext = context;
                EndpointDescription = endpoint.Description;
                EndpointConfiguration = endpoint.Configuration!;
                ServiceId = new NodeId("key-service", instanceNamespace);
                RequestId = new NodeId("key-request", instanceNamespace);
                SessionId = new NodeId("session", instanceNamespace);
                AuthenticationId = new NodeId("authentication", instanceNamespace);
                m_resourceId = new NodeId("resource", instanceNamespace);
            }

            public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;
            public EndpointDescription EndpointDescription { get; }
            public EndpointConfiguration EndpointConfiguration { get; }
            public IServiceMessageContext MessageContext { get; }
            public int OperationTimeout { get; set; } = 30_000;
            public byte[] ChannelThumbprint => [];
            public byte[] ClientChannelCertificate => [];
            public byte[] ServerChannelCertificate => [];
            internal NodeId ServiceId { get; }
            internal NodeId RequestId { get; }
            internal NodeId SessionId { get; }
            internal NodeId AuthenticationId { get; }
            internal List<IServiceRequest> Requests { get; } = [];
            internal string SecretText { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

            internal static StatusCode Completion { get; } =
                new(StatusCodes.GoodResultsMayBeIncomplete.Code | 0x480u);

            public async ValueTask<IServiceResponse> SendRequestAsync(
                IServiceRequest request, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Requests.Add(request);
                await Assert.That(request.RequestHeader.AuthenticationToken).IsEqualTo(AuthenticationId);
                IServiceResponse response = request switch
                {
                    BrowseRequest browse when Requests.Count == 1 => await BrowseAsync(browse).ConfigureAwait(false),
                    TranslateBrowsePathsToNodeIdsRequest translate when Requests.Count == 2 =>
                        await TranslateAsync(translate).ConfigureAwait(false),
                    ReadRequest read when Requests.Count == 3 => await ReadAsync(read).ConfigureAwait(false),
                    CallRequest call when Requests.Count == 4 => await CallAsync(call).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Unexpected request in the four-service GDS script.")
                };
                ResponseHeader header = response.ResponseHeader ??
                    throw new InvalidOperationException("The GDS script response must contain a header.");
                header.RequestHandle = request.RequestHeader.RequestHandle;
                return response;
            }

            public ValueTask ReconnectAsync(
                ITransportWaitingConnection? connection = null, CancellationToken ct = default)
            {
                throw new InvalidOperationException("The GDS fixture must not reconnect.");
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                return ValueTask.CompletedTask;
            }

            public void Dispose()
            {
            }

            private async Task<BrowseResponse> BrowseAsync(BrowseRequest request)
            {
                await Assert.That(request.NodesToBrowse.Count).IsEqualTo(1);
                BrowseDescription browse = request.NodesToBrowse[0];
                await Assert.That(browse.NodeId).IsEqualTo(ServiceId);
                await Assert.That(browse.BrowseDirection).IsEqualTo(BrowseDirection.Forward);
                await Assert.That(browse.ReferenceTypeId).IsEqualTo(ReferenceTypeIds.HasTypeDefinition);
                await Assert.That(browse.NodeClassMask).IsEqualTo((uint)NodeClass.ObjectType);
                return new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            References =
                            [
                                new ReferenceDescription
                                {
                                    NodeId = ExpandedNodeId.ToNodeId(
                                        Gds.ObjectTypeIds.KeyCredentialServiceType, MessageContext.NamespaceUris),
                                    NodeClass = NodeClass.ObjectType,
                                    ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition, IsForward = true
                                }
                            ]
                        }
                    ]
                };
            }

            private async Task<TranslateBrowsePathsToNodeIdsResponse> TranslateAsync(
                TranslateBrowsePathsToNodeIdsRequest request)
            {
                await Assert.That(request.BrowsePaths.Count).IsEqualTo(1);
                BrowsePath path = request.BrowsePaths[0];
                await Assert.That(path.StartingNode).IsEqualTo(ServiceId);
                await Assert.That(path.RelativePath.Elements.Count).IsEqualTo(1);
                RelativePathElement element = path.RelativePath.Elements[0];
                await Assert.That(element.ReferenceTypeId).IsEqualTo(ReferenceTypeIds.HierarchicalReferences);
                await Assert.That(element.IncludeSubtypes).IsTrue();
                await Assert.That(element.IsInverse).IsFalse();
                await Assert.That(element.TargetName).IsEqualTo(new QualifiedName(
                    "ResourceUri", (ushort)MessageContext.NamespaceUris.GetIndex(Gds.Namespaces.OpcUaGds)));
                return new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.Good,
                            Targets =
                            [
                                new BrowsePathTarget { TargetId = m_resourceId, RemainingPathIndex = uint.MaxValue }
                            ]
                        }
                    ]
                };
            }

            private async Task<ReadResponse> ReadAsync(ReadRequest request)
            {
                await Assert.That(request.NodesToRead.Count).IsEqualTo(1);
                await Assert.That(request.NodesToRead[0].NodeId).IsEqualTo(m_resourceId);
                await Assert.That(request.NodesToRead[0].AttributeId).IsEqualTo(Attributes.Value);
                await Assert.That(request.MaxAge).IsEqualTo(0d);
                await Assert.That(request.TimestampsToReturn).IsEqualTo(TimestampsToReturn.Both);
                return new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new DataValue(Variant.From("urn:aot:resource"))]
                };
            }

            private async Task<CallResponse> CallAsync(CallRequest request)
            {
                await Assert.That(request.MethodsToCall.Count).IsEqualTo(1);
                CallMethodRequest call = request.MethodsToCall[0];
                await Assert.That(call.ObjectId).IsEqualTo(ServiceId);
                await Assert.That(call.MethodId).IsEqualTo(ExpandedNodeId.ToNodeId(
                    Gds.MethodIds.KeyCredentialServiceType_FinishRequest, MessageContext.NamespaceUris));
                await Assert.That(call.InputArguments.Count).IsEqualTo(2);
                bool hasRequest = call.InputArguments[0].TryGetValue(out NodeId requestedId);
                bool hasCancel = call.InputArguments[1].TryGetValue(out bool cancel);
                await Assert.That(hasRequest).IsTrue();
                await Assert.That(requestedId).IsEqualTo(RequestId);
                await Assert.That(hasCancel).IsTrue();
                await Assert.That(cancel).IsFalse();
                return new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = Completion,
                            InputArgumentResults = [StatusCodes.Good, new StatusCode(0x480u)],
                            OutputArguments =
                            [
                                Variant.From("credential-17"),
                                Variant.From(ByteString.From(Encoding.UTF8.GetBytes(SecretText))),
                                Variant.From("public-thumbprint"), Variant.From(SecurityPolicies.Basic256Sha256),
                                Variant.From([NodeId.Parse("i=15644"), NodeId.Parse("i=15668")])
                            ]
                        }
                    ]
                };
            }

            private readonly NodeId m_resourceId;
        }

        /// <summary>
        /// Fixed leases and inert timers avoid wall-clock races; no test waits for expiry or an observation window.
        /// </summary>
        private sealed class FrozenClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
            }

            public override long GetTimestamp()
            {
                return 0;
            }

            public override ITimer CreateTimer(
                TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                return new InertTimer();
            }

            private sealed class InertTimer : ITimer
            {
                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    return true;
                }

                public void Dispose()
                {
                }

                public ValueTask DisposeAsync()
                {
                    return ValueTask.CompletedTask;
                }
            }
        }

        /// <summary>
        /// A named resolver is never needed for the explicitly authorized IP-literal endpoint.
        /// </summary>
        private sealed class LocalDns : IMcpDnsResolver
        {
            internal int Calls { get; private set; }

            public ValueTask<ArrayOf<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Calls++;
                throw new InvalidOperationException("An IP-literal-only AOT test must not request DNS.");
            }
        }

        /// <summary>
        /// Activating either legacy or new transports is an immediate failure in this stopped-runtime suite.
        /// </summary>
        private sealed class NoNetworkPubSubTransports : IMcpPubSubTransportProvider
        {
            internal int FactoryCalls { get; private set; }

            public JsonArray Capabilities()
            {
                return new JsonArray(new JsonObject { ["transport"] = "test-only", ["activated"] = false });
            }

            public IPubSubTransportFactory CreateFactory(
                PubSubApplicationInput input, ISecretRegistry? credentials = null)
            {
                FactoryCalls++;
                throw new InvalidOperationException("A stopped application must not activate a transport.");
            }

            public IPubSubTransportFactory CreateLegacyFactory(string endpoint, bool publisher)
            {
                FactoryCalls++;
                throw new InvalidOperationException("Expanded AOT tests must not activate legacy transports.");
            }
        }

        /// <summary>
        /// Keeps real planner and executor capability metadata while replacing only channel I/O with typed calls.
        /// </summary>
        private sealed class LocalWotExecutors : IWotRuntimeExecutorFactory
        {
            internal LocalWotExecutors()
            {
                Binders = new WotRuntimeExecutorFactory(
                    new WotRuntimeOptions(), DefaultTelemetry.Create(static _ => { })).Binders;
            }

            public IWotBinderRegistry Binders { get; }
            internal List<WotCompiledForm> Opened { get; } = [];
            internal List<string> GrantedOperations { get; } = [];
            internal List<DataValue> Writes { get; } = [];
            internal int ReadCalls { get; private set; }
            internal int DisposedChannels { get; private set; }

            internal static DateTimeUtc SourceTime { get; } =
                (DateTimeUtc)new DateTime(2026, 10, 12, 0, 0, 1, DateTimeKind.Utc);

            internal static DateTimeUtc ServerTime { get; } =
                (DateTimeUtc)new DateTime(2026, 10, 12, 0, 0, 2, DateTimeKind.Utc);

            public ValueTask<WotRuntimeConnection> OpenAsync(
                WotCompiledForm form, McpNetworkGrant grant, IWotCredentialProvider credentials,
                ISession? session = null, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (grant.Endpoint.AbsoluteUri != "http://127.0.0.1:8080/value" ||
                    grant.Resource != "/value" ||
                    session is not null)
                {
                    throw new InvalidOperationException("The executor received a different target or session.");
                }
                Opened.Add(form);
                GrantedOperations.Add(grant.Operation);
                return ValueTask.FromResult(new WotRuntimeConnection(new LocalChannel(this, form)));
            }

            private sealed class LocalChannel(LocalWotExecutors owner, WotCompiledForm form) : IWotBindingChannel
            {
                public WotCompiledForm Form { get; } = form;

                public ValueTask<WotReadResult> ReadAsync(CancellationToken ct = default)
                {
                    ct.ThrowIfCancellationRequested();
                    owner.ReadCalls++;
                    var status = new StatusCode(0x40000480u);
                    return ValueTask.FromResult(new WotReadResult(status,
                        new DataValue(Variant.From(false), status, SourceTime, ServerTime)));
                }

                public ValueTask<WotWriteResult> WriteAsync(DataValue value, CancellationToken ct = default)
                {
                    ct.ThrowIfCancellationRequested();
                    owner.Writes.Add(value);
                    return ValueTask.FromResult(new WotWriteResult(StatusCodes.Good));
                }

                public ValueTask<WotInvokeResult> InvokeAsync(
                    IReadOnlyList<Variant> inputs, CancellationToken ct = default)
                {
                    throw new InvalidOperationException("This property fixture must not invoke an action.");
                }

                public ValueTask<IWotSubscription> ObserveAsync(
                    Action<WotNotification> callback, CancellationToken ct = default)
                {
                    throw new InvalidOperationException("This fixture must not start a timed observation.");
                }

                public ValueTask<IWotSubscription> SubscribeEventAsync(
                    Action<WotNotification> callback, CancellationToken ct = default)
                {
                    throw new InvalidOperationException("This property fixture must not subscribe to an event.");
                }

                public ValueTask DisposeAsync()
                {
                    owner.DisposedChannels++;
                    return ValueTask.CompletedTask;
                }
            }
        }

        /// <summary>
        /// Copies on write and lookup; consumers never receive or mutate the store's retained token arrays.
        /// </summary>
        private sealed class ImmutableSecretStoreFake : ISecretStore
        {
            public string StoreType => "ExpandedAotFake";
            internal int Count => m_values.Count;
            internal int SetCalls { get; private set; }
            internal int LookupCalls { get; private set; }
            internal int RemoveCalls { get; private set; }
            internal int DisposedViews { get; private set; }

            public ISecret? TryGet(SecretIdentifier id)
            {
                LookupCalls++;
                return m_values.TryGetValue(id, out byte[]? bytes) ? new View(this, [.. bytes]) : null;
            }

            public ValueTask<ISecret?> GetAsync(SecretIdentifier id, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(TryGet(id));
            }

            public ValueTask SetAsync(
                SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                SetCalls++;
                m_values.Add(id, bytes.ToArray());
                return ValueTask.CompletedTask;
            }

            public ValueTask<bool> RemoveAsync(SecretIdentifier id, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                RemoveCalls++;
                bool removed = m_values.Remove(id, out byte[]? bytes);
                if (bytes is not null)
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
                return ValueTask.FromResult(removed);
            }

            private readonly Dictionary<SecretIdentifier, byte[]> m_values = [];

            private sealed class View(ImmutableSecretStoreFake owner, byte[] bytes) : ISecret
            {
                public ReadOnlySpan<byte> Bytes => bytes;

                public void Dispose()
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    owner.DisposedViews++;
                }
            }
        }
    }

    /// <summary>
    /// Closed caller-side request/metadata serialization; never substituted for a module's tool-binding options.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(GdsApplicationInput))]
    [JsonSerializable(typeof(WorkflowDataValuesInput))]
    [JsonSerializable(typeof(PubSubApplicationInput))]
    [JsonSerializable(typeof(WotRuntimeSelection))]
    [JsonSerializable(typeof(WotRuntimeOpenOptions))]
    [JsonSerializable(typeof(WotRuntimeValue))]
    [JsonSerializable(typeof(McpSecretReference))]
    internal sealed partial class ExpandedMcpAotJsonContext : JsonSerializerContext;
}
