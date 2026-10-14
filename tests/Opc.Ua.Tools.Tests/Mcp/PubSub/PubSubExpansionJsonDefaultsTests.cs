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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Uses production generated metadata and raw omitted properties, not a default-filled fixture serializer.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionJsonDefaultsTests
    {
        [Test]
        public void ProductionContextRetainsApplicationFieldAndConnectionDefaults()
        {
            PubSubApplicationInput input = JsonSerializer.Deserialize(
                MinimalApplication, McpPubSubExpansionJsonContext.Default.PubSubApplicationInput)!;

            AssertDefaults(input);
            Assert.That(input.Fields[0].InitialValue!.Value.GetInt32(), Is.Zero);
            Assert.That(input.Fields[1].InitialValue!.Value.GetBoolean(), Is.False);
            Assert.That(input.Security, Is.Null);
            Assert.That(input.Connection.AuxiliaryTopics.IsNull, Is.False);
            Assert.That(input.Connection.AuxiliaryTopics, Is.Empty);
            Assert.That(input.Connection.Encoding, Is.EqualTo(PubSubMessageEncoding.Uadp));
            Assert.That(() => PubSubInputValidation.Validate(input, new McpPubSubOptions()), Throws.Nothing);
        }

        [Test]
        public async Task SdkOmittedDefaultsReachTheActualStoppedRuntimeAndMetadataAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            CallToolResult result = await fixture.InvokeAsync("pubsub_application_create",
                PubSubExpansionTestContext.ApplicationArguments(JsonNode.Parse(MinimalApplication)!.AsObject()))
                .ConfigureAwait(false);

            Assert.That(result.IsError, Is.False, RemoteModelTestSession.Payload(result).GetRawText());
            string handle = RemoteModelTestSession.Payload(result).GetProperty("handle").GetString()!;
            McpRuntimeUse<McpPubSubRuntime> use = await fixture.Runtimes.AcquireAsync(handle, CancellationToken.None)
                .ConfigureAwait(false);
            await using (use.ConfigureAwait(false))
            {
                AssertDefaults(use.Runtime.Input);
                DataSetMetaDataType metadata = use.Runtime.DeclaredMetadata();
                Assert.That(metadata.Name, Is.EqualTo("McpDataSet"));
                Assert.That(metadata.Fields[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(metadata.Fields[1].ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(metadata.Fields[0].BuiltInType, Is.EqualTo((byte)BuiltInType.Int32));
                Assert.That(metadata.Fields[1].BuiltInType, Is.EqualTo((byte)BuiltInType.Boolean));
            }
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [TestCase("name")]
        [TestCase("mode")]
        [TestCase("connection")]
        [TestCase("publisherId")]
        [TestCase("fields")]
        [TestCase("field-name")]
        [TestCase("field-type")]
        [TestCase("connection-endpoint")]
        [TestCase("connection-transport")]
        public void ProductionContextStillRejectsEachMissingRequiredMember(string missing)
        {
            JsonObject input = JsonNode.Parse(MinimalApplication)!.AsObject();
            if (missing.StartsWith("field-", StringComparison.Ordinal))
            {
                Assert.That(input["fields"]![0]!.AsObject().Remove(missing["field-".Length..]), Is.True);
            }
            else if (missing.StartsWith("connection-", StringComparison.Ordinal))
            {
                Assert.That(input["connection"]!.AsObject().Remove(missing["connection-".Length..]), Is.True);
            }
            else
            {
                Assert.That(input.Remove(missing), Is.True);
            }

            Assert.That(() => JsonSerializer.Deserialize(
                input.ToJsonString(), McpPubSubExpansionJsonContext.Default.PubSubApplicationInput),
                Throws.TypeOf<JsonException>());
        }

        [TestCase("input-omitted")]
        [TestCase("input-empty")]
        [TestCase("connection-empty")]
        [TestCase("publisher-empty")]
        [TestCase("field-empty")]
        [TestCase("security-empty")]
        public async Task SdkEmptyObjectsCannotMasqueradeAsOmittedOptionalValuesAsync(string invalid)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            JsonObject arguments = PubSubExpansionTestContext.ApplicationArguments(
                JsonNode.Parse(MinimalApplication)!.AsObject());
            JsonObject input = arguments["input"]!.AsObject();
            switch (invalid)
            {
                case "input-omitted":
                    arguments.Remove("input");
                    break;
                case "input-empty":
                    arguments["input"] = new JsonObject();
                    break;
                case "connection-empty":
                    arguments["connection"] = new JsonObject();
                    break;
                case "publisher-empty":
                    input["publisherId"] = new JsonObject();
                    break;
                case "field-empty":
                    arguments["fields"] = new JsonArray(new JsonObject());
                    break;
                default:
                    input["security"] = new JsonObject();
                    break;
            }

            string failure = await BindingFailureAsync(fixture, arguments).ConfigureAwait(false);
            string required = invalid switch
            {
                "input-omitted" => "input",
                "connection-empty" => "endpoint",
                "publisher-empty" or "field-empty" => "type",
                "security-empty" => "groupId",
                _ => "name"
            };
            Assert.That(failure, Does.Contain(required).IgnoreCase);
            Assert.That(failure, Does.Not.Contain("JsonTypeInfo"));
            Assert.That(fixture.Registry.List(), Is.Empty);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public void ExplicitZeroAndEmptyValuesAreNotReplacedByDefaults()
        {
            JsonObject json = JsonNode.Parse(MinimalApplication)!.AsObject();
            json["writerGroupId"] = 0;
            json["dataSetWriterId"] = 0;
            json["dataSetName"] = string.Empty;
            json["publishingIntervalMilliseconds"] = 0;
            json["fields"]![0]!["valueRank"] = 0;

            PubSubApplicationInput input = JsonSerializer.Deserialize(
                json.ToJsonString(), McpPubSubExpansionJsonContext.Default.PubSubApplicationInput)!;

            Assert.That(input.WriterGroupId, Is.Zero);
            Assert.That(input.DataSetWriterId, Is.Zero);
            Assert.That(input.DataSetName, Is.Empty);
            Assert.That(input.PublishingIntervalMilliseconds, Is.Zero);
            Assert.That(input.Fields[0].ValueRank, Is.Zero);
            Assert.That(() => PubSubInputValidation.Validate(input, new McpPubSubOptions()),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void ProductionSecurityMappingAndActionDefaultsRemainSecureAndTyped()
        {
            PubSubSecurityInput security = JsonSerializer.Deserialize("""
                {"groupId":"group","policyUri":"urn:policy","keyReference":"issued:opaque",
                 "resource":"urn:resource","tokenId":7,"expiresAt":"2030-01-01T00:00:00Z"}
                """, McpPubSubExpansionJsonContext.Default.PubSubSecurityInput)!;
            PubSubVariableInput variable = JsonSerializer.Deserialize(
                """{"name":"value","nodeId":"i=1"}""",
                McpPubSubExpansionJsonContext.Default.PubSubVariableInput)!;
            PubSubActionBindingInput action = JsonSerializer.Deserialize(
                """{"objectId":"i=85","methodId":"i=123","actionTargetId":9}""",
                McpPubSubExpansionJsonContext.Default.PubSubActionBindingInput)!;

            Assert.That(security.Mode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(security.TokenId, Is.EqualTo(7));
            Assert.That(variable.AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(variable.Promoted, Is.False);
            Assert.That(action.OutputFieldNames.IsNull, Is.False);
            Assert.That(action.OutputFieldNames, Is.Empty);
            Assert.That(action.AllowUnsecured, Is.False);
            Assert.That(() => JsonSerializer.Deserialize(
                "{}", McpPubSubExpansionJsonContext.Default.PubSubSecurityInput),
                Throws.TypeOf<JsonException>());
            Assert.That(() => JsonSerializer.Deserialize(
                "{}", McpPubSubExpansionJsonContext.Default.PubSubVariableInput),
                Throws.TypeOf<JsonException>());
            Assert.That(() => JsonSerializer.Deserialize("{}",
                McpPubSubExpansionJsonContext.Default.PubSubActionBindingInput), Throws.TypeOf<JsonException>());
        }

        [Test]
        public void ProductionBridgeDefaultsPreserveMetadataAndEmptyProjectionLists()
        {
            JsonObject source = JsonNode.Parse(MinimalApplication)!.AsObject();
            JsonObject target = JsonNode.Parse(MinimalApplication)!.AsObject();
            target["name"] = "target";
            target["mode"] = "Publisher";
            var wire = new JsonObject { ["name"] = "route", ["source"] = source, ["target"] = target };

            PubSubBridgeInput bridge = JsonSerializer.Deserialize(
                wire.ToJsonString(), McpPubSubExpansionJsonContext.Default.PubSubBridgeInput)!;

            Assert.That(bridge.PreserveMetadataVersion, Is.True);
            Assert.That(bridge.RenameFields.IsNull, Is.False);
            Assert.That(bridge.SelectFields.IsNull, Is.False);
            Assert.That(bridge.ExcludeFields.IsNull, Is.False);
            Assert.That(bridge.PromoteFields.IsNull, Is.False);
            Assert.That(bridge.RenameFields, Is.Empty);
            Assert.That(bridge.SelectFields, Is.Empty);
            Assert.That(bridge.AllowSecurityDowngrade, Is.False);
            AssertDefaults(bridge.Source);
            Assert.That(() => JsonSerializer.Deserialize("{}", McpPubSubExpansionJsonContext.Default.PubSubBridgeInput),
                Throws.TypeOf<JsonException>());
        }

        private static void AssertDefaults(PubSubApplicationInput input)
        {
            Assert.That(input.WriterGroupId, Is.EqualTo(1));
            Assert.That(input.DataSetWriterId, Is.EqualTo(1));
            Assert.That(input.DataSetName, Is.EqualTo("McpDataSet"));
            Assert.That(input.PublishingIntervalMilliseconds, Is.EqualTo(100));
            Assert.That(input.Fields, Has.Count.EqualTo(2));
            Assert.That(input.Fields[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(input.Fields[1].ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        private static async Task<string> BindingFailureAsync(
            PubSubExpansionTestContext fixture, JsonObject arguments)
        {
            try
            {
                CallToolResult result = await fixture.InvokeAsync("pubsub_application_create", arguments)
                    .ConfigureAwait(false);
                Assert.That(result.IsError, Is.True);
                return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            }
            catch (McpProtocolException failure)
            {
                Assert.That(failure.ErrorCode, Is.EqualTo(McpErrorCode.InvalidParams));
                return Messages(failure);
            }
            catch (Exception failure) when (failure is ArgumentException or JsonException)
            {
                return Messages(failure);
            }
        }

        private static string Messages(Exception failure)
        {
            var messages = new List<string>();
            for (Exception? current = failure; current is not null; current = current.InnerException)
            {
                Assert.That(current, Is.Not.InstanceOf<NotSupportedException>(),
                    "Missing serializer metadata is not a required-member validation result.");
                messages.Add(current.Message);
            }
            return string.Join("\n", messages);
        }

        private const string MinimalApplication = """
            {"name":"raw-defaults","mode":"Subscriber",
             "connection":{"transport":"Udp","endpoint":"opc.udp://127.0.0.1:4849","networkInterface":"test-interface"},
             "publisherId":{"type":"UInt16","value":42},
             "fields":[{"name":"Count","type":"Int32","initialValue":0},
                       {"name":"Enabled","type":"Boolean","initialValue":false}]}
            """;
    }
}
#endif
