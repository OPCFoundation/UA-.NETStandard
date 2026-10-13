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
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion.ClientWorkflows
{
    /// <summary>
    /// Covers each workflow's independent metadata/read outcomes, including bad quality versus optional absence.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class ClientWorkflowMetadataTests
    {
        /// <summary>
        /// Role inspection does not turn unreadable exclusion flags into misleading default booleans.
        /// </summary>
        [Test]
        public async Task RoleReadPreservesBadQualityOptionalAbsenceAndExplicitFalseAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId role = fixture.Role();
            fixture.Remote.Property(role, new QualifiedName(BrowseNames.ApplicationsExclude),
                DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied));
            fixture.Remote.Property(role, new QualifiedName(BrowseNames.EndpointsExclude), Variant.From(false));
            CallToolResult result = await fixture.Roles.ReadAsync(role.ToString(), "selected").ConfigureAwait(false);
            JsonElement properties = RemoteModelTestSession.Payload(result).GetProperty("properties");
            Assert.That(result.IsError, Is.True);
            Assert.That(ReadProperty(properties, BrowseNames.ApplicationsExclude, fixture).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(ReadProperty(properties, BrowseNames.EndpointsExclude, fixture).WrappedValue,
                Is.EqualTo(Variant.From(false)));
            Assert.That(properties.GetProperty(BrowseNames.CustomConfiguration).GetProperty("supported").GetBoolean(),
                Is.False);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Role inspection forwards the host mapping ceiling rather than reverting to the unbounded SDK overload.
        /// </summary>
        [TestCase(3)]
        [TestCase(4)]
        public async Task RoleReadEnforcesHostMappingLimitAtExactBoundaryAsync(int count)
        {
            await using ClientWorkflowTestContext fixture = await ClientWorkflowTestContext.CreateAsync(
                options: new ClientWorkflowOptions { MaxItems = 3 }).ConfigureAwait(false);
            NodeId role = fixture.Role();
            var rules = Enumerable.Range(1, count).Select(_ =>
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "operator"
                }).ToArrayOf();
            fixture.Remote.Property(role, new QualifiedName(BrowseNames.Identities), Variant.FromStructure(rules));
            CallToolResult result = await fixture.Roles.ReadAsync(role.ToString(), "selected").ConfigureAwait(false);
            if (count > 3)
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadEncodingLimitsExceeded);
            }
            else
            {
                Assert.That(result.IsError, Is.False);
                DataValue value = ReadProperty(
                    RemoteModelTestSession.Payload(result).GetProperty("properties"), BrowseNames.Identities, fixture);
                Assert.That(value.WrappedValue.TryGetStructure(
                    out ArrayOf<IdentityMappingRuleType> returned), Is.True);
                Assert.That(returned.Count, Is.EqualTo(3));
                Assert.That(returned[0].CriteriaType, Is.EqualTo(IdentityCriteriaType.UserName));
                Assert.That(returned[0].Criteria, Is.EqualTo("operator"));
            }
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// List returns vendor role subtypes with namespace identities and does not fetch the next server page.
        /// </summary>
        [Test]
        public async Task RoleListIncludesRealSubtypesAndReleasesUnconsumedBrowsePointAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId subtype = fixture.Remote.Id("custom-role-type");
            fixture.Remote.Subtype(subtype, ObjectTypeIds.RoleType);
            var point = ByteString.From(new byte[] { 4 });
            fixture.Remote.Browse(ObjectIds.Server_ServerCapabilities_RoleSet, ReferenceTypeIds.HierarchicalReferences,
                [
                    fixture.Remote.Reference(fixture.Remote.Id("custom-role"), subtype, "custom"),
                    fixture.Remote.Reference(fixture.Remote.Id("other-role"), ObjectTypeIds.RoleType, "other")
                ], continuation: point);
            CallToolResult result = await fixture.Roles.ListAsync(1, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("items")[0].GetProperty("browseName").GetString(), Is.EqualTo("custom"));
            Assert.That(json.GetProperty("items")[0].GetProperty("browseNamespaceUri").GetString(),
                Is.EqualTo("urn:test:remote-model"));
            Assert.That(json.GetProperty("items")[0].GetProperty("typeDefinition").GetString(),
                Does.Contain("urn:test:remote-model"));
            Assert.That(json.GetProperty("nextOffset").GetInt32(), Is.EqualTo(1));
            Assert.That(fixture.Remote.Continuations.Single().Point, Is.EqualTo(point));
            Assert.That(fixture.Remote.Continuations.Single().Release, Is.True);
            Assert.That(fixture.Remote.Reads, Is.Empty);
        }

        /// <summary>
        /// Declared and effective permission attributes are kept distinct, including unsupported effective values.
        /// </summary>
        [Test]
        public async Task PermissionReadKeepsDeclaredEffectiveAndAccessRestrictionAttributesDistinctAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("node");
            fixture.Remote.Values[(node, Attributes.RolePermissions)] = new DataValue(Variant.FromStructure(
                [new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Observer,
                    Permissions = 1 }]));
            fixture.Remote.Values[(node, Attributes.UserRolePermissions)] =
                DataValue.FromStatusCode(StatusCodes.BadAttributeIdInvalid);
            fixture.Remote.Values[(node, Attributes.AccessRestrictions)] = new DataValue(Variant.From((ushort)2));
            CallToolResult result = await fixture.Roles.ReadPermissionsAsync(node.ToString(), "selected")
                .ConfigureAwait(false);
            JsonElement attributes = RemoteModelTestSession.Payload(result).GetProperty("attributes");
            Assert.That(Decode(attributes.GetProperty("RolePermissions"), fixture).WrappedValue.TryGetStructure(
                out ArrayOf<RolePermissionType> roles), Is.True);
            Assert.That(roles[0].RoleId, Is.EqualTo(ObjectIds.WellKnownRole_Observer));
            Assert.That(Decode(attributes.GetProperty("UserRolePermissions"), fixture).StatusCode,
                Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
            Assert.That(Decode(attributes.GetProperty("AccessRestrictions"), fixture).WrappedValue,
                Is.EqualTo(Variant.From((ushort)2)));
            Assert.That(fixture.Writes, Is.Empty);
        }

        /// <summary>
        /// Password policy distinguishes unsupported optional text from failed reads of exposed fields.
        /// </summary>
        [Test]
        public async Task PasswordPolicyPreservesRangeBadOptionsAndAbsentRestrictionsAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Property(ObjectIds.UserManagement, new QualifiedName(BrowseNames.PasswordLength),
                Variant.FromStructure(new Range { Low = 12, High = 128 }));
            fixture.Remote.Property(ObjectIds.UserManagement, new QualifiedName(BrowseNames.PasswordOptions),
                DataValue.FromStatusCode(StatusCodes.BadNotReadable));
            CallToolResult result = await fixture.Users.PolicyAsync("selected").ConfigureAwait(false);
            JsonElement properties = RemoteModelTestSession.Payload(result).GetProperty("properties");
            Assert.That(result.IsError, Is.True);
            Assert.That(ReadProperty(properties, BrowseNames.PasswordLength, fixture)
                .WrappedValue.TryGetStructure<Range>(
                fixture.Remote.Context, out Range? range), Is.True);
            Assert.That(range!.Low, Is.EqualTo(12));
            Assert.That(range.High, Is.EqualTo(128));
            Assert.That(ReadProperty(properties, BrowseNames.PasswordOptions, fixture).StatusCode,
                Is.EqualTo(StatusCodes.BadNotReadable));
            Assert.That(properties.GetProperty(BrowseNames.PasswordRestrictions).GetProperty("supported").GetBoolean(),
                Is.False);
            Assert.That(fixture.Secrets, Is.Empty);
        }

        /// <summary>
        /// User list applies a bounded wire-level index range rather than reading and truncating the whole property.
        /// </summary>
        [Test]
        public async Task UserListUsesBoundedServerIndexRangeAndReportsNextOffsetAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            NodeId users = fixture.Remote.Property(ObjectIds.UserManagement, new QualifiedName(BrowseNames.Users),
                Variant.FromStructure(
                [
                    new UserManagementDataType { UserName = "user7", UserConfiguration = 0, Description = "seven" },
                    new UserManagementDataType
                    {
                        UserName = "user8", UserConfiguration = 2, Description = string.Empty
                    },
                    new UserManagementDataType { UserName = "user9", UserConfiguration = 0, Description = "nine" }
                ]));
            CallToolResult result = await fixture.Users.ListAsync(2, 7, "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(fixture.Remote.Reads.Single().NodeId, Is.EqualTo(users));
            Assert.That(fixture.Remote.Reads.Single().IndexRange, Is.EqualTo("7:9"));
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
            Assert.That(json.GetProperty("items")[0].GetProperty("UserName").GetString(), Is.EqualTo("user7"));
            Assert.That(json.GetProperty("items")[1].GetProperty("UserName").GetString(), Is.EqualTo("user8"));
            Assert.That(json.GetProperty("nextOffset").GetInt32(), Is.EqualTo(9));
            Assert.That(fixture.Secrets, Is.Empty);
        }

        /// <summary>
        /// A server that refuses index ranges never triggers an unbounded fallback.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task UserListRangeFailureNeverTriggersUnboundedFallbackAsync(bool error)
        {
            StatusCode code = error ? StatusCodes.BadIndexRangeInvalid : StatusCodes.BadIndexRangeNoData;
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Property(ObjectIds.UserManagement, new QualifiedName(BrowseNames.Users),
                DataValue.FromStatusCode(code));
            CallToolResult result = await fixture.Users.ListAsync(2, 7, "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.EqualTo(error));
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(code.Code));
            Assert.That(json.GetProperty("items").GetArrayLength(), Is.Zero);
            Assert.That(fixture.Remote.Reads, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Reads[0].IndexRange, Is.EqualTo("7:9"));
        }

        /// <summary>
        /// File discovery uses actual model subtypes, not a name that resembles FileDirectoryType.
        /// </summary>
        [Test]
        public async Task FileRootsRecognizeActualSubtypesButRejectNamespaceLookalikesAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId subtype = fixture.Remote.Id("subtype");
            fixture.Remote.Subtype(subtype, ObjectTypeIds.FileDirectoryType);
            fixture.Remote.Browse(ObjectIds.ObjectsFolder, ReferenceTypeIds.HierarchicalReferences,
                [
                    fixture.Remote.Reference(fixture.Remote.Id("root"), subtype, "Mounted"),
                    fixture.Remote.Reference(fixture.Remote.Id("lookalike"),
                        fixture.Remote.Id("FileDirectoryType"), "FileDirectoryType")
                ]);
            CallToolResult result = await fixture.Files.RootsAsync(maxDepth: 1, sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement items = RemoteModelTestSession.Payload(result).GetProperty("items");
            Assert.That(items.GetArrayLength(), Is.EqualTo(1));
            Assert.That(items[0].GetProperty("browseName").GetString(), Is.EqualTo("Mounted"));
            Assert.That(items[0].GetProperty("expandedNodeId").GetString(), Does.Contain("urn:test:remote-model"));
        }

        /// <summary>
        /// Optional file metadata is never converted into a false zero-value guarantee.
        /// </summary>
        [Test]
        public async Task FileMetadataPreservesSizeQualityFalseWritableAndMissingMimeAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            (_, NodeId file) = fixture.FileTree();
            fixture.Remote.Property(file, new QualifiedName(BrowseNames.Size),
                new DataValue(Variant.From(123UL), StatusCodes.Uncertain, kTime, kTime));
            fixture.Remote.Property(file, new QualifiedName(BrowseNames.UserWritable), Variant.From(false));
            CallToolResult result = await fixture.Files.MetadataAsync(file.ToString(), "selected")
                .ConfigureAwait(false);
            JsonElement properties = RemoteModelTestSession.Payload(result).GetProperty("properties");
            DataValue size = ReadProperty(properties, BrowseNames.Size, fixture);
            Assert.That(size.WrappedValue, Is.EqualTo(Variant.From(123UL)));
            Assert.That(size.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(ReadProperty(properties, BrowseNames.UserWritable, fixture).WrappedValue,
                Is.EqualTo(Variant.From(false)));
            Assert.That(properties.GetProperty(BrowseNames.MimeType).GetProperty("supported").GetBoolean(), Is.False);
        }

        /// <summary>
        /// Completion inspection returns the real state without automatically calling a transition or retry.
        /// </summary>
        [Test]
        public async Task FileCompletionReadsRealStateWithoutDrivingTheMachineAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId completion = fixture.Remote.Id("completion");
            fixture.Remote.Type(completion, ObjectTypeIds.FileTransferStateMachineType);
            fixture.Remote.Property(completion, new QualifiedName(BrowseNames.CurrentState),
                Variant.From(new LocalizedText("en", "WriteTransfer")));
            CallToolResult result = await fixture.Files.CompletionAsync(completion.ToString(), "selected")
                .ConfigureAwait(false);
            JsonElement properties = RemoteModelTestSession.Payload(result).GetProperty("properties");
            Assert.That(ReadProperty(properties, BrowseNames.CurrentState, fixture).WrappedValue,
                Is.EqualTo(Variant.From(new LocalizedText("en", "WriteTransfer"))));
            Assert.That(properties.GetProperty(BrowseNames.LastTransition).GetProperty("supported").GetBoolean(),
                Is.False);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Optional alarm commands report both presence and actual per-caller executable state.
        /// </summary>
        [Test]
        public async Task AlarmInspectionPreservesOptionalCapabilityAndUserExecutableFalseAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId condition = fixture.Remote.Id("condition");
            fixture.Remote.Type(condition, ObjectTypeIds.ConditionType);
            NodeId enable = fixture.Remote.Id("enable");
            fixture.Remote.Child(condition, new QualifiedName(BrowseNames.Enable), enable);
            fixture.Remote.Values[(enable, Attributes.Executable)] = new DataValue(Variant.From(true));
            fixture.Remote.Values[(enable, Attributes.UserExecutable)] = new DataValue(Variant.From(false));
            CallToolResult result = await fixture.Alarms.InspectAsync(condition.ToString(), "selected")
                .ConfigureAwait(false);
            JsonElement methods = RemoteModelTestSession.Payload(result).GetProperty("methods");
            Assert.That(Decode(methods.GetProperty(BrowseNames.Enable).GetProperty("attributes")
                .GetProperty("Executable"), fixture).WrappedValue, Is.EqualTo(Variant.From(true)));
            Assert.That(Decode(methods.GetProperty(BrowseNames.Enable).GetProperty("attributes")
                .GetProperty("UserExecutable"), fixture).WrappedValue, Is.EqualTo(Variant.From(false)));
            Assert.That(methods.GetProperty(BrowseNames.Disable).GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Group membership calls the real typed AlarmClient method and pages exact returned node ids.
        /// </summary>
        [Test]
        public async Task AlarmGroupsUseTypedClientAndPreserveExactGroupNamespaceAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId condition = fixture.Remote.Id("condition");
            NodeId group = fixture.Remote.Id("group");
            fixture.Remote.Reply(StatusCodes.Good, Variant.From([group, fixture.Remote.Id("next")]));
            CallToolResult result = await fixture.Alarms.GroupMembershipsAsync(
                condition.ToString(), 1, sessionName: "selected").ConfigureAwait(false);
            fixture.Remote.AssertCall(condition, MethodIds.AlarmConditionType_GetGroupMemberships);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("items")[0].GetProperty("nodeId").GetString(), Is.EqualTo(group.ToString()));
            Assert.That(json.GetProperty("items")[0].GetProperty("expandedNodeId").GetString(),
                Does.Contain("urn:test:remote-model"));
            Assert.That(json.GetProperty("hasMore").GetBoolean(), Is.True);
        }

        /// <summary>
        /// History capability inspection does not equate missing capability properties with supported false values.
        /// </summary>
        [Test]
        public async Task HistoryCapabilitiesPreserveFalseBadQualityAndActualAggregateCatalogueAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Remote.Property(ObjectIds.HistoryServerCapabilities,
                new QualifiedName(BrowseNames.InsertDataCapability), Variant.From(false));
            fixture.Remote.Property(ObjectIds.HistoryServerCapabilities,
                new QualifiedName(BrowseNames.ReplaceDataCapability),
                DataValue.FromStatusCode(StatusCodes.BadNotReadable));
            NodeId folder = fixture.Remote.Id("aggregates");
            NodeId aggregate = fixture.Remote.Id("custom-aggregate");
            fixture.Remote.Child(ObjectIds.HistoryServerCapabilities,
                new QualifiedName(BrowseNames.AggregateFunctions), folder);
            fixture.Remote.Browse(folder, ReferenceTypeIds.HierarchicalReferences,
                [fixture.Remote.Reference(aggregate, ObjectTypeIds.BaseObjectType, "ActualAggregate")]);
            CallToolResult result = await fixture.History.CapabilitiesAsync(sessionName: "selected")
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            JsonElement properties = json.GetProperty("properties");
            Assert.That(ReadProperty(properties, BrowseNames.InsertDataCapability, fixture).WrappedValue,
                Is.EqualTo(Variant.From(false)));
            Assert.That(ReadProperty(properties, BrowseNames.ReplaceDataCapability, fixture).StatusCode,
                Is.EqualTo(StatusCodes.BadNotReadable));
            Assert.That(properties.GetProperty(BrowseNames.UpdateDataCapability).GetProperty("supported").GetBoolean(),
                Is.False);
            Assert.That(
                json.GetProperty("aggregateFunctions").GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(aggregate.ToString()));
            Assert.That(fixture.HistoryReads, Is.Empty);
        }

        /// <summary>
        /// History configuration uses actual links and exposes both aggregate defaults and event type identities.
        /// </summary>
        [Test]
        public async Task HistoryConfigurationPreservesActualLinksAggregateDefaultsAndEventTypesAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId node = fixture.Remote.Id("historian");
            NodeId configuration = fixture.Remote.Id("configuration");
            NodeId aggregate = fixture.Remote.Id("aggregate-defaults");
            NodeId eventTypes = fixture.Remote.Id("event-types");
            fixture.Remote.Browse(node, ReferenceTypeIds.HasHistoricalConfiguration,
                [fixture.Remote.Reference(configuration, ObjectTypeIds.HistoricalDataConfigurationType)]);
            fixture.Remote.Property(configuration, new QualifiedName(BrowseNames.Stepped), Variant.From(false));
            fixture.Remote.Child(configuration, new QualifiedName(BrowseNames.AggregateConfiguration), aggregate);
            fixture.Remote.Property(aggregate, new QualifiedName(BrowseNames.PercentDataGood), Variant.From((byte)95));
            fixture.Remote.Child(configuration, new QualifiedName(BrowseNames.EventTypes), eventTypes);
            fixture.Remote.Browse(eventTypes, ReferenceTypeIds.Organizes,
                [fixture.Remote.Reference(fixture.Remote.Id("event-type"), ObjectTypeIds.BaseObjectType)]);
            CallToolResult result = await fixture.History.ConfigurationAsync(
                node.ToString(), sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("supported").GetBoolean(), Is.True);
            JsonElement entry = json.GetProperty("configurations")[0];
            Assert.That(entry.GetProperty("nodeId").GetString(), Is.EqualTo(configuration.ToString()));
            Assert.That(ReadProperty(entry.GetProperty("properties"), BrowseNames.Stepped, fixture).WrappedValue,
                Is.EqualTo(Variant.From(false)));
            Assert.That(ReadProperty(entry.GetProperty("aggregateConfiguration").GetProperty("properties"),
                BrowseNames.PercentDataGood, fixture).WrappedValue, Is.EqualTo(Variant.From((byte)95)));
            Assert.That(entry.GetProperty("eventTypes").GetProperty("items")[0].GetProperty("nodeId").GetString(),
                Is.EqualTo(fixture.Remote.Id("event-type").ToString()));
        }

        /// <summary>
        /// An absent historical configuration is explicit and does not produce invented default settings.
        /// </summary>
        [Test]
        public async Task AbsentHistoryConfigurationReturnsUnsupportedWithoutInventedDefaultsAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.History.ConfigurationAsync(
                fixture.Remote.Id("signal").ToString(), sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("supported").GetBoolean(), Is.False);
            Assert.That(json.GetProperty("configurations").GetArrayLength(), Is.Zero);
            Assert.That(fixture.HistoryReads, Is.Empty);
        }

        /// <summary>
        /// Gets one supported quality-bearing property.
        /// </summary>
        private static DataValue ReadProperty(JsonElement properties, string name, ClientWorkflowTestContext fixture)
        {
            JsonElement property = properties.GetProperty(name);
            Assert.That(property.GetProperty("supported").GetBoolean(), Is.True);
            return Decode(property.GetProperty("dataValue"), fixture);
        }

        /// <summary>
        /// Decodes UA values independently of the projection helper used by production.
        /// </summary>
        private static DataValue Decode(JsonElement value, ClientWorkflowTestContext fixture)
        {
            using var decoder = new JsonDecoder("{\"value\":" + value.GetRawText() + "}", fixture.Remote.Context);
            return decoder.ReadDataValue("value");
        }

        private static readonly DateTime kTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
#endif
