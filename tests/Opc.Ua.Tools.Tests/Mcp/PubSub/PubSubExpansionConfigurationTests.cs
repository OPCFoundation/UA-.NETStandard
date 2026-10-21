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
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Generated standard method binding, real version/per-item output and credential-safe artifact projection.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionConfigurationTests
    {
        [TestCase("WriterGroup")]
        [TestCase("ReaderGroup")]
        [TestCase("Writer")]
        [TestCase("Reader")]
        public async Task ComponentAdditionsUseSpecificTypedPayloadsAndReturnActualNodeAsync(string kind)
        {
            (ExpandedNodeId type, NodeId method, string operation) = kind switch
            {
                "WriterGroup" => (ObjectTypeIds.PubSubConnectionType,
                    MethodIds.PubSubConnectionType_AddWriterGroup, "pubsub_configuration_add_writer_group"),
                "ReaderGroup" => (ObjectTypeIds.PubSubConnectionType,
                    MethodIds.PubSubConnectionType_AddReaderGroup, "pubsub_configuration_add_reader_group"),
                "Writer" => (ObjectTypeIds.WriterGroupType,
                    MethodIds.WriterGroupType_AddDataSetWriter, "pubsub_configuration_add_writer"),
                _ => (ObjectTypeIds.ReaderGroupType,
                    MethodIds.ReaderGroupType_AddDataSetReader, "pubsub_configuration_add_reader")
            };
            PubSubApplicationInput input = PubSubExpansionTestContext.Input(
                mode: kind.StartsWith("Reader", StringComparison.Ordinal) ? "Subscriber" : "Publisher");
            McpPubSubOptions options = Options("target", operation) with
            {
                DatagramRules =
                [
                    new McpPubSubDatagramRule
                    {
                        Endpoint = input.Connection.Endpoint, NetworkInterface = input.Connection.NetworkInterface!,
                        LocalAddress = "127.0.0.1",
                        Operations = ["configure-connect", "configure-publish", "configure-subscribe"]
                    }
                ]
            };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext
                .CreateAsync(options: options).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId target = fixture.Remote.Id("target");
            NodeId assigned = fixture.Remote.Id("assigned");
            fixture.Remote.Type(target, type);
            bool connectionTarget = kind.EndsWith("Group", StringComparison.Ordinal);
            NodeId connection = connectionTarget ? target : fixture.Remote.Id("connection");
            fixture.Remote.Type(connection, ObjectTypeIds.PubSubConnectionType);
            if (!connectionTarget)
            {
                fixture.Remote.Browse(target, ReferenceTypeIds.HasComponent,
                    [fixture.Remote.Reference(connection, ObjectTypeIds.PubSubConnectionType)],
                    BrowseDirection.Inverse);
            }
            fixture.Remote.Property(connection, new QualifiedName(BrowseNames.TransportProfileUri),
                Variant.From(Profiles.PubSubUdpUadpTransport));
            NodeId address = fixture.Remote.Id("address");
            fixture.Remote.Child(connection, new QualifiedName(BrowseNames.Address), address);
            fixture.Remote.Property(address, new QualifiedName(BrowseNames.Url),
                Variant.From(input.Connection.Endpoint));
            fixture.Remote.Property(address, new QualifiedName(BrowseNames.NetworkInterface),
                Variant.From(input.Connection.NetworkInterface!));
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(assigned));
            PubSubConfigurationTools tools = fixture.Services.GetRequiredService<PubSubConfigurationTools>();
            CallToolResult result = kind switch
            {
                "WriterGroup" => await tools.AddWriterGroupAsync(target.ToString(), "Added", input,
                    PubSubExpansionTestContext.SessionName).ConfigureAwait(false),
                "ReaderGroup" => await tools.AddReaderGroupAsync(target.ToString(), "Added", input,
                    PubSubExpansionTestContext.SessionName).ConfigureAwait(false),
                "Writer" => await tools.AddWriterAsync(target.ToString(), "Added", input,
                    PubSubExpansionTestContext.SessionName).ConfigureAwait(false),
                _ => await tools.AddReaderAsync(target.ToString(), "Added", input,
                    PubSubExpansionTestContext.SessionName).ConfigureAwait(false)
            };

            Assert.That(result.IsError, Is.False);
            CallMethodRequest call = fixture.Remote.Calls.Single();
            Assert.That(call.ObjectId, Is.EqualTo(target));
            Assert.That(call.MethodId, Is.EqualTo(method));
            Assert.That(call.InputArguments, Has.Count.EqualTo(1));
            Assert.That(call.InputArguments[0].TryGetValue(out ExtensionObject body), Is.True);
            if (kind == "WriterGroup")
            {
                Assert.That(body.TryGetValue(out WriterGroupDataType? group), Is.True);
                Assert.That(group!.Name, Is.EqualTo("Added"));
                Assert.That(group.WriterGroupId, Is.EqualTo(1));
                Assert.That(group.DataSetWriters.ToList().Single().DataSetWriterId, Is.EqualTo(1));
            }
            else if (kind == "ReaderGroup")
            {
                Assert.That(body.TryGetValue(out ReaderGroupDataType? group), Is.True);
                Assert.That(group!.Name, Is.EqualTo("Added"));
                Assert.That(group.DataSetReaders.ToList().Single().DataSetMetaData.Fields, Has.Count.EqualTo(2));
            }
            else if (kind == "Writer")
            {
                Assert.That(body.TryGetValue(out DataSetWriterDataType? writer), Is.True);
                Assert.That(writer!.Name, Is.EqualTo("Added"));
                Assert.That(writer.DataSetName, Is.EqualTo("McpDataSet"));
            }
            else
            {
                Assert.That(body.TryGetValue(out DataSetReaderDataType? reader), Is.True);
                Assert.That(reader!.Name, Is.EqualTo("Added"));
                Assert.That(reader.PublisherId.TryGetValue(out ushort publisher), Is.True);
                Assert.That(publisher, Is.EqualTo(42));
            }
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("nodeId").GetString(),
                Is.EqualTo(assigned.ToString()));
        }

        [Test]
        public async Task AddFieldsUsesTypedProxyAndPreservesActualVersionAndMixedStatusesAsync()
        {
            McpPubSubOptions options = Options("items", "pubsub_configuration_add_fields");
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext
                .CreateAsync(options: options)
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            fixture.Remote.Type(fixture.Remote.Id("items"), ObjectTypeIds.PublishedDataItemsType);
            var actualVersion = new ConfigurationVersionDataType { MajorVersion = 27, MinorVersion = 5 };
            ArrayOf<StatusCode> outcomes = [StatusCodes.Good, StatusCodes.BadNodeIdUnknown];
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(new ExtensionObject(actualVersion)),
                Variant.From(outcomes));
            var input = new PubSubVariablesInput
            {
                Variables =
                [
                    new PubSubVariableInput
                    {
                        Name = "Temperature", NodeId = fixture.Remote.Id("temperature").ToString()
                    },
                    new PubSubVariableInput { Name = "Missing", NodeId = fixture.Remote.Id("missing").ToString() }
                ]
            };

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .AddFieldsAsync(fixture.Remote.Id("items").ToString(),
                    new PubSubVersionInput { MajorVersion = 26, MinorVersion = 9 }, input,
                    PubSubExpansionTestContext.SessionName).ConfigureAwait(false);

            CallMethodRequest call = fixture.Remote.Calls.Single();
            Assert.That(call.ObjectId, Is.EqualTo(fixture.Remote.Id("items")));
            Assert.That(call.MethodId, Is.EqualTo(MethodIds.PublishedDataItemsType_AddVariables));
            Assert.That(call.InputArguments[0].TryGetStructure<ConfigurationVersionDataType>(
                fixture.Remote.Context, out ConfigurationVersionDataType? sentVersion), Is.True);
            Assert.That(sentVersion!.MajorVersion, Is.EqualTo(26));
            Assert.That(sentVersion.MinorVersion, Is.EqualTo(9));
            Assert.That(call.InputArguments[1].TryGetValue(out ArrayOf<string> aliases), Is.True);
            Assert.That(aliases.ToArray(), Is.EqualTo(s_addedFieldNames));
            JsonElement output = RemoteModelTestSession.Payload(result);
            Assert.That(output.GetProperty("partial").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("methodCompleted").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("results")[0].GetProperty("code").GetUInt32(),
                Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(output.GetProperty("results")[1].GetProperty("code").GetUInt32(),
                Is.EqualTo(StatusCodes.BadNodeIdUnknown.Code));
            using var decoder = new JsonDecoder(
                "{\"version\":" + output.GetProperty("configurationVersion").GetRawText() + "}",
                fixture.Remote.Context);
            ConfigurationVersionDataType decoded = decoder.ReadEncodeable<ConfigurationVersionDataType>("version");
            Assert.That(decoded.MajorVersion, Is.EqualTo(27));
            Assert.That(decoded.MinorVersion, Is.EqualTo(5));
        }

        [Test]
        public async Task EmptyTargetRulesRefuseBeforeAnyRemoteMethodOrMetadataReadAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .EnableAsync(fixture.Remote.Id("status").ToString(), PubSubExpansionTestContext.SessionName)
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Operations, Is.Empty);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task EnableAndDisableBindExactStandardStatusMethodsAsync(bool enable)
        {
            string operation = enable ? "pubsub_configuration_enable" : "pubsub_configuration_disable";
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options("status", operation)).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            fixture.Remote.Type(fixture.Remote.Id("status"), ObjectTypeIds.PubSubStatusType);
            fixture.Remote.Reply(StatusCodes.Good);
            PubSubConfigurationTools tools = fixture.Services.GetRequiredService<PubSubConfigurationTools>();

            CallToolResult result = enable
                ? await tools.EnableAsync(
                    fixture.Remote.Id("status").ToString(), PubSubExpansionTestContext.SessionName)
                    .ConfigureAwait(false)
                : await tools.DisableAsync(
                    fixture.Remote.Id("status").ToString(), PubSubExpansionTestContext.SessionName)
                    .ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(fixture.Remote.Calls.Single().ObjectId, Is.EqualTo(fixture.Remote.Id("status")));
            Assert.That(fixture.Remote.Calls.Single().MethodId,
                Is.EqualTo(enable ? MethodIds.PubSubStatusType_Enable : MethodIds.PubSubStatusType_Disable));
            Assert.That(fixture.Remote.Calls.Single().InputArguments, Is.Empty);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public async Task ConfigurationProjectionRemovesPrivateBagsAndKeepsWireDiscriminatorAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubApplicationInput input = PubSubExpansionTestContext.Input();
            var source = new McpPubSubDataSetSource(
                input, new McpPubSubOptions(), fixture.Remote.Context, fixture.Clock);
            PubSubConfigurationDataType configuration = McpPubSubConfiguration.Build(input, source.BuildMetaData());
            configuration.Connections[0].ConnectionProperties =
                [new KeyValuePair { Key = new QualifiedName("Password"), Value = Variant.From("private-test-marker") }];
            configuration.PublishedDataSets[0].ExtensionFields =
                [new KeyValuePair { Key = new QualifiedName("Token"), Value = Variant.From("token-test-marker") }];
            configuration.Connections[0].WriterGroups[0].SecurityKeyServices =
                [new EndpointDescription { EndpointUrl = "opc.tcp://server.test:4840" }];

            int removed = McpPubSubConfigurationSafety.Sanitize(configuration, new McpPubSubOptions());

            Assert.That(removed, Is.EqualTo(3));
            Assert.That(configuration.Connections[0].ConnectionProperties, Is.Empty);
            Assert.That(configuration.PublishedDataSets[0].ExtensionFields, Is.Empty);
            Assert.That(configuration.Connections[0].WriterGroups[0].SecurityKeyServices, Is.Empty);
            Assert.That(configuration.Connections[0].PublisherId.TryGetValue(out ushort publisher), Is.True);
            Assert.That(publisher, Is.EqualTo(42));
            Assert.That(configuration.PublishedDataSets[0].DataSetMetaData.Fields.ToList().Select(field => field.Name),
                Is.EqualTo(s_defaultFieldNames));
        }

        private static McpPubSubOptions Options(string target, string operation)
        {
            return new McpPubSubOptions
            {
                ModelRules =
                [new McpPubSubModelRule
                {
                    SessionName = PubSubExpansionTestContext.SessionName,
                    NodeId = "nsu=urn:test:remote-model;s=" + target,
                    Operations = [operation]
                }]
            };
        }

        private static readonly string[] s_addedFieldNames = ["Temperature", "Missing"];
        private static readonly string[] s_defaultFieldNames = ["Count", "Enabled"];
    }
}
#endif
