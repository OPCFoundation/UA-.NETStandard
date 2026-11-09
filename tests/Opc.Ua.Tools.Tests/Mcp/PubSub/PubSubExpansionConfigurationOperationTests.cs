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
    /// Covers individual standard mutation methods, observed versions and actual subtype resolution.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionConfigurationOperationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task InspectPreservesActualVersionAndOptionalStatusWithoutStartingAnythingAsync(bool status)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options(ObjectIds.PublishSubscribe.ToString(), "pubsub_configuration_inspect"))
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            fixture.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PublishSubscribeType);
            fixture.Remote.Property(ObjectIds.PublishSubscribe, new QualifiedName(BrowseNames.ConfigurationVersion),
                Variant.From(91u));
            fixture.Remote.Property(ObjectIds.PublishSubscribe,
                new QualifiedName(BrowseNames.SupportedTransportProfiles),
                Variant.From(new ArrayOf<string>(s_profiles)));
            if (status)
            {
                NodeId state = fixture.Remote.Id("status");
                fixture.Remote.Child(ObjectIds.PublishSubscribe, new QualifiedName(BrowseNames.Status), state);
                fixture.Remote.Property(state, new QualifiedName(BrowseNames.State), Variant.From(2));
            }

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .InspectAsync(Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            JsonElement output = RemoteModelTestSession.Payload(result);
            Assert.That(output.GetProperty("serverProcessStarted").GetBoolean(), Is.False);
            Assert.That(output.GetProperty(BrowseNames.ConfigurationVersion).GetProperty("present").GetBoolean(),
                Is.True);
            DataValue observedVersion = ReadValue(output.GetProperty(BrowseNames.ConfigurationVersion), fixture);
            Assert.That(observedVersion.WrappedValue.TryGetValue(out uint version), Is.True);
            Assert.That(version, Is.EqualTo(91));
            DataValue observedProfiles = ReadValue(output.GetProperty(BrowseNames.SupportedTransportProfiles), fixture);
            Assert.That(observedProfiles.WrappedValue.TryGetValue(out ArrayOf<string> profiles), Is.True);
            Assert.That(profiles.ToArray(), Is.EqualTo(s_profiles));
            Assert.That(output.GetProperty("status").ValueKind,
                Is.EqualTo(status ? JsonValueKind.Object : JsonValueKind.Null));
            if (status)
            {
                Assert.That(output.GetProperty("status").GetProperty(BrowseNames.State)
                    .GetProperty("present").GetBoolean(), Is.True);
                DataValue observedState = ReadValue(
                    output.GetProperty("status").GetProperty(BrowseNames.State), fixture);
                Assert.That(observedState.WrappedValue.TryGetValue(out int state), Is.True);
                Assert.That(state, Is.EqualTo(2));
            }
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [TestCase("group")]
        [TestCase("writer")]
        [TestCase("reader")]
        [TestCase("dataset")]
        [TestCase("folder")]
        public async Task RemovalsAcceptARealSubtypeAndPreserveExactChildIdentityAsync(string kind)
        {
            (ExpandedNodeId type, ExpandedNodeId method, string operation) = kind switch
            {
                "group" => (ObjectTypeIds.PubSubConnectionType, MethodIds.PubSubConnectionType_RemoveGroup,
                    "pubsub_configuration_remove_group"),
                "writer" => (ObjectTypeIds.WriterGroupType, MethodIds.WriterGroupType_RemoveDataSetWriter,
                    "pubsub_configuration_remove_writer"),
                "reader" => (ObjectTypeIds.ReaderGroupType, MethodIds.ReaderGroupType_RemoveDataSetReader,
                    "pubsub_configuration_remove_reader"),
                "dataset" => (ObjectTypeIds.DataSetFolderType, MethodIds.DataSetFolderType_RemovePublishedDataSet,
                    "pubsub_configuration_remove_dataset"),
                _ => (ObjectTypeIds.DataSetFolderType, MethodIds.DataSetFolderType_RemoveDataSetFolder,
                    "pubsub_configuration_remove_folder")
            };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options(Target, operation)).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId parent = fixture.Remote.Id("target");
            NodeId derived = fixture.Remote.Id("derived-type");
            NodeId child = fixture.Remote.Id("actual-child");
            fixture.Remote.Type(parent, derived);
            fixture.Remote.Subtype(derived, type);
            fixture.Remote.Reply(StatusCodes.Good);
            PubSubConfigurationTools tools = fixture.Services.GetRequiredService<PubSubConfigurationTools>();

            CallToolResult result = kind switch
            {
                "group" => await tools.RemoveGroupAsync(parent.ToString(), child.ToString(), Session)
                    .ConfigureAwait(false),
                "writer" => await tools.RemoveWriterAsync(parent.ToString(), child.ToString(), Session)
                    .ConfigureAwait(false),
                "reader" => await tools.RemoveReaderAsync(parent.ToString(), child.ToString(), Session)
                    .ConfigureAwait(false),
                "dataset" => await tools.RemoveDataSetAsync(parent.ToString(), child.ToString(), Session)
                    .ConfigureAwait(false),
                _ => await tools.RemoveFolderAsync(parent.ToString(), child.ToString(), Session).ConfigureAwait(false)
            };

            Assert.That(result.IsError, Is.False);
            fixture.Remote.AssertCall(parent, method, Variant.From(child));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("methodCompleted").GetBoolean(), Is.True);
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public async Task RemoveConnectionVerifiesTheActualChildTypeBeforeCallingTheRootAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options(ObjectIds.PublishSubscribe.ToString(), "pubsub_configuration_remove_connection"))
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId child = fixture.Remote.Id("connection");
            fixture.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PublishSubscribeType);
            fixture.Remote.Type(child, ObjectTypeIds.PubSubConnectionType);
            fixture.Remote.Reply(StatusCodes.Good);

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .RemoveConnectionAsync(child.ToString(), Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            fixture.Remote.AssertCall(ObjectIds.PublishSubscribe,
                MethodIds.PublishSubscribeType_RemoveConnection, Variant.From(child));
            Assert.That(fixture.Remote.Browses.Any(browse => browse.NodeId == child &&
                browse.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition), Is.True);
        }

        [Test]
        public async Task AddedDataSetPreservesPromotedFlagsAttributeMappingsAndMixedFieldResultsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options(Target, "pubsub_configuration_add_dataset")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId folder = fixture.Remote.Id("target");
            NodeId added = fixture.Remote.Id("new-dataset");
            fixture.Remote.Type(folder, ObjectTypeIds.DataSetFolderType);
            var version = new ConfigurationVersionDataType { MajorVersion = 9, MinorVersion = 14 };
            ArrayOf<StatusCode> statuses = [StatusCodes.Good, StatusCodes.BadAttributeIdInvalid];
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(added), Variant.From(new ExtensionObject(version)),
                Variant.From(statuses));
            var input = new PubSubVariablesInput
            {
                Variables =
                [
                    new PubSubVariableInput
                    {
                        Name = "value", NodeId = fixture.Remote.Id("value").ToString(), Promoted = true
                    },
                    new PubSubVariableInput
                    {
                        Name = "label", NodeId = fixture.Remote.Id("label").ToString(),
                        AttributeId = Attributes.DisplayName
                    }
                ]
            };

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .AddDataSetAsync(folder.ToString(), "new", input, Session).ConfigureAwait(false);

            CallMethodRequest call = fixture.Remote.Calls.Single();
            Assert.That(call.MethodId, Is.EqualTo(MethodIds.DataSetFolderType_AddPublishedDataItems));
            Assert.That(call.InputArguments[1].TryGetValue(out ArrayOf<string> names), Is.True);
            Assert.That(names.ToArray(), Is.EqualTo(s_names));
            Assert.That(call.InputArguments[2].TryGetValue(out ArrayOf<ushort> flags), Is.True);
            Assert.That(flags.ToArray(), Is.EqualTo(s_flags));
            Assert.That(call.InputArguments[3].TryGetValue(out ArrayOf<ExtensionObject> variables), Is.True);
            Assert.That(variables[1].TryGetValue(out PublishedVariableDataType? label), Is.True);
            Assert.That(label!.PublishedVariable, Is.EqualTo(fixture.Remote.Id("label")));
            Assert.That(label.AttributeId, Is.EqualTo(Attributes.DisplayName));
            JsonElement output = RemoteModelTestSession.Payload(result);
            Assert.That(output.GetProperty("partial").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("nodeId").GetString(), Is.EqualTo(added.ToString()));
            AssertVersion(output, fixture, 9, 14);
            Assert.That(output.GetProperty("results")[1].GetProperty("code").GetUInt32(),
                Is.EqualTo(StatusCodes.BadAttributeIdInvalid.Code));
        }

        [Test]
        public async Task AddedFolderReturnsServerAssignedIdentityRatherThanSynthesizingAPathAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options(Target, "pubsub_configuration_add_folder")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId folder = fixture.Remote.Id("target");
            NodeId assigned = fixture.Remote.Id("opaque-assigned-id");
            fixture.Remote.Type(folder, ObjectTypeIds.DataSetFolderType);
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(assigned));

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .AddFolderAsync(folder.ToString(), "Visible name", Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            fixture.Remote.AssertCall(folder, MethodIds.DataSetFolderType_AddDataSetFolder,
                Variant.From("Visible name"));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("nodeId").GetString(),
                Is.EqualTo(assigned.ToString()));
        }

        [TestCase("good")]
        [TestCase("bad")]
        [TestCase("mixed")]
        public async Task FieldRemovalPreservesIndexOrderObservedVersionAndPerItemStatusAsync(string outcome)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options(Target, "pubsub_configuration_remove_fields")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId dataSet = fixture.Remote.Id("target");
            fixture.Remote.Type(dataSet, ObjectTypeIds.PublishedDataItemsType);
            ArrayOf<StatusCode> statuses =
            [
                outcome == "bad" ? StatusCodes.BadInvalidArgument : StatusCodes.Good,
                outcome == "good" ? StatusCodes.Good : StatusCodes.BadOutOfRange
            ];
            fixture.Remote.Reply(StatusCodes.Good,
                Variant.From(new ExtensionObject(new ConfigurationVersionDataType
                {
                    MajorVersion = 101,
                    MinorVersion = 7
                })), Variant.From(statuses));

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .RemoveFieldsAsync(dataSet.ToString(),
                    new PubSubVersionInput { MajorVersion = 100, MinorVersion = 8 },
                    new PubSubFieldIndicesInput
                    {
                        Fields = [new PubSubFieldIndexInput { Index = 7 }, new PubSubFieldIndexInput { Index = 2 }]
                    }, Session).ConfigureAwait(false);

            CallMethodRequest call = fixture.Remote.Calls.Single();
            Assert.That(call.MethodId, Is.EqualTo(MethodIds.PublishedDataItemsType_RemoveVariables));
            Assert.That(call.InputArguments[0].TryGetStructure<ConfigurationVersionDataType>(
                fixture.Remote.Context, out ConfigurationVersionDataType? sent), Is.True);
            Assert.That(sent!.MajorVersion, Is.EqualTo(100));
            Assert.That(sent.MinorVersion, Is.EqualTo(8));
            Assert.That(call.InputArguments[1].TryGetValue(out ArrayOf<uint> indices), Is.True);
            Assert.That(indices.ToArray(), Is.EqualTo(s_indices));
            JsonElement output = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.EqualTo(outcome != "good"));
            Assert.That(output.GetProperty("partial").GetBoolean(), Is.EqualTo(outcome == "mixed"));
            Assert.That(output.GetProperty("results")[0].GetProperty("code").GetUInt32(),
                Is.EqualTo(statuses[0].Code));
            Assert.That(output.GetProperty("results")[1].GetProperty("code").GetUInt32(),
                Is.EqualTo(statuses[1].Code));
            AssertVersion(output, fixture, 101, 7);
        }

        [TestCase("unrelated-type")]
        [TestCase("cross-server-child")]
        [TestCase("unknown-namespace")]
        public async Task InvalidTypeOrChildIdentityCannotReachTheMutationAsync(string invalid)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: Options(Target, "pubsub_configuration_remove_group")).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            NodeId parent = fixture.Remote.Id("target");
            fixture.Remote.Type(parent, invalid == "unrelated-type"
                ? ObjectTypeIds.BaseObjectType : ObjectTypeIds.PubSubConnectionType);
            string child = invalid switch
            {
                "cross-server-child" => "svr=2;s=child",
                "unknown-namespace" => "nsu=urn:not-registered;s=child",
                _ => fixture.Remote.Id("child").ToString()
            };

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .RemoveGroupAsync(parent.ToString(), child, Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Remote.Browses, Is.Not.Empty, "The real existing session/type path was exercised.");
        }

        private static McpPubSubOptions Options(string target, string operation)
        {
            return new McpPubSubOptions
            {
                ModelRules =
                [
                    new McpPubSubModelRule
                    {
                        SessionName = Session, NodeId = target, Operations = [operation]
                    }
                ]
            };
        }

        private static void AssertVersion(
            JsonElement output, PubSubExpansionTestContext fixture, uint major, uint minor)
        {
            using var decoder = new JsonDecoder(
                "{\"version\":" + output.GetProperty("configurationVersion").GetRawText() + "}",
                fixture.Remote.Context);
            ConfigurationVersionDataType version = decoder.ReadEncodeable<ConfigurationVersionDataType>("version");
            Assert.That(version.MajorVersion, Is.EqualTo(major));
            Assert.That(version.MinorVersion, Is.EqualTo(minor));
        }

        private static DataValue ReadValue(JsonElement property, PubSubExpansionTestContext fixture)
        {
            using var decoder = new JsonDecoder("{\"value\":" + property.GetProperty("value").GetRawText() + "}",
                fixture.Remote.Context);
            return decoder.ReadDataValue("value");
        }

        private const string Session = PubSubExpansionTestContext.SessionName;
        private const string Target = "nsu=urn:test:remote-model;s=target";
        private static readonly string[] s_profiles = [Profiles.PubSubUdpUadpTransport];
        private static readonly string[] s_names = ["value", "label"];
        private static readonly ushort[] s_flags = [1, 0];
        private static readonly uint[] s_indices = [7, 2];
    }
}
#endif
