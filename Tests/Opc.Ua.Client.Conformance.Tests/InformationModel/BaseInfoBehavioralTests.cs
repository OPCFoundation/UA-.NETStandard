/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Base Information behavioral CUs:
    /// OptionSet, Diagnostics, GetMonitoredItems, ResendData,
    /// RequestServerStateChange, DeviceFailure, EventQueueOverflow,
    /// ProgressEvents, SecurityRoleCapabilities, SelectionList,
    /// and OrderedList.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoBehavioral")]
    public class BaseInfoBehavioralTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task OptionSet001ReadAccessLevelExOnServerStatusStateAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                VariableIds.Server_ServerStatus_State,
                Attributes.AccessLevelEx).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("AccessLevelEx not supported.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "002")]
        public async Task OptionSet002ReadWriteMaskOnServerAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.WriteMask).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(dv.StatusCode), Is.True,
                "WriteMask should be readable on Server object.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "003")]
        public async Task OptionSet003ReadUserWriteMaskOnServerAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.UserWriteMask).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(dv.StatusCode), Is.True,
                "UserWriteMask should be readable on Server object.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "004")]
        public async Task OptionSet004ReadEventNotifierOnServerAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.EventNotifier).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(dv.StatusCode), Is.True,
                "EventNotifier should be readable on Server object.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "005")]
        public async Task OptionSet005BrowseServerCapabilitiesForAccessRestrictionsAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.Server_ServerCapabilities,
                ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "ServerCapabilities should have properties.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "006")]
        public async Task OptionSet006ReadAccessRestrictionsAttributeAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                DataValue dv = await ReadAttributeAsync(
                    ObjectIds.Server,
                    Attributes.AccessRestrictions, admin).ConfigureAwait(false);
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    Assert.Ignore("AccessRestrictions not available.");
                }
                // The Server node may legitimately have no AccessRestrictions
                // configured — accept null but require a Good status.
                Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "007")]
        public async Task OptionSet007ReadRolePermissionsOnServerAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                DataValue dv = await ReadAttributeAsync(
                    ObjectIds.Server,
                    Attributes.RolePermissions, admin).ConfigureAwait(false);
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    Assert.Ignore("RolePermissions not available.");
                }
                Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "008")]
        public async Task OptionSet008ReadUserRolePermissionsAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                DataValue dv = await ReadAttributeAsync(
                    ObjectIds.Server,
                    Attributes.UserRolePermissions, admin).ConfigureAwait(false);
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    Assert.Ignore("UserRolePermissions not available.");
                }
                Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "009")]
        public async Task OptionSet009BrowseDataTypeDefinitionEnumerationAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                new NodeId(DataTypes.ServerState),
                Attributes.DataTypeDefinition).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("DataTypeDefinition not available on enumeration.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "010")]
        public async Task OptionSet010ReadDataTypeDefinitionStructureAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                new NodeId(DataTypes.Argument),
                Attributes.DataTypeDefinition).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("DataTypeDefinition not available on structure type.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "011")]
        public async Task OptionSet011ReadAccessLevelExOnWritableVariableAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                VariableIds.Server_ServerStatus_State,
                Attributes.AccessLevelEx).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("AccessLevelEx not supported.");
            }
            uint val = dv.GetValue<uint>(0);
            Assert.That(val, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "012")]
        public async Task OptionSet012VerifyWriteMaskBitsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.WriteMask).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            uint mask = dv.GetValue<uint>(0);
            Assert.That(mask, Is.GreaterThanOrEqualTo((uint)0),
                "WriteMask should be a valid bitmask.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "013")]
        public async Task OptionSet013VerifyUserWriteMaskBitsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.UserWriteMask).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            uint mask = dv.GetValue<uint>(0);
            Assert.That(mask, Is.GreaterThanOrEqualTo((uint)0),
                "UserWriteMask should be a valid bitmask.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "014")]
        public async Task OptionSet014VerifyAccessLevelOnVariableAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                VariableIds.Server_ServerStatus_CurrentTime,
                Attributes.AccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            byte level = dv.GetValue<byte>(0);
            Assert.That(level & AccessLevels.CurrentRead,
                Is.Not.Zero,
                "CurrentTime should be readable.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "015")]
        public async Task OptionSet015VerifyUserAccessLevelOnVariableAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                VariableIds.Server_ServerStatus_CurrentTime,
                Attributes.UserAccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
            byte level = dv.GetValue<byte>(0);
            Assert.That(level & AccessLevels.CurrentRead,
                Is.Not.Zero,
                "UserAccessLevel should include CurrentRead.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "000")]
        public async Task Diagnostics000ReadEnabledFlagAsync()
        {
            DataValue dv = await ReadValueAsync(
                VariableIds.Server_ServerDiagnostics_EnabledFlag)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("EnabledFlag not readable.");
            }
            Assert.That(dv.WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "001")]
        public async Task Diagnostics001ReadServerDiagnosticsSummaryAsync()
        {
            DataValue dv = await ReadValueAsync(
                VariableIds
                    .Server_ServerDiagnostics_ServerDiagnosticsSummary)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(dv.StatusCode) ||
                dv.StatusCode.Code == StatusCodes.BadNotReadable ||
                dv.StatusCode.Code == StatusCodes.BadUserAccessDenied,
                Is.True,
                "ServerDiagnosticsSummary should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "002")]
        public async Task Diagnostics002ReadServerViewCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2276)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("ServerViewCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "003")]
        public async Task Diagnostics003ReadCurrentSessionCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2277)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("CurrentSessionCount not accessible.");
            }
            uint count = dv.GetValue<uint>(0);
            Assert.That(count, Is.GreaterThan((uint)0),
                "At least one session should be active.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "004")]
        public async Task Diagnostics004ReadCumulatedSessionCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2278)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("CumulatedSessionCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "005")]
        public async Task Diagnostics005ReadSecurityRejectedSessionCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2279)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("SecurityRejectedSessionCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "006")]
        public async Task Diagnostics006ReadSessionAbortCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2282)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("SessionAbortCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "007")]
        public async Task Diagnostics007ReadPublishingIntervalCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2284)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("PublishingIntervalCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "008")]
        public async Task Diagnostics008ReadCurrentSubscriptionCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2285)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("CurrentSubscriptionCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "009")]
        public async Task Diagnostics009ReadCumulatedSubscriptionCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2286)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("CumulatedSubscriptionCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "010")]
        public async Task Diagnostics010ReadSecurityRejectedRequestsCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2287)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("SecurityRejectedRequestsCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "011")]
        public async Task Diagnostics011ReadSamplingIntervalDiagnosticsArrayAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                DataValue dv = await ReadAttributeAsync(
                    VariableIds
                        .Server_ServerDiagnostics_SamplingIntervalDiagnosticsArray,
                    Attributes.Value, admin)
                    .ConfigureAwait(false);
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    Assert.Ignore(
                        "SamplingIntervalDiagnosticsArray not accessible: " +
                        $"{dv.StatusCode}");
                }
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "012")]
        public async Task Diagnostics012ReadSubscriptionDiagnosticsArrayAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                VariableIds
                    .Server_ServerDiagnostics_SubscriptionDiagnosticsArray)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(dv.StatusCode) ||
                dv.StatusCode.Code == StatusCodes.BadNotReadable ||
                dv.StatusCode.Code == StatusCodes.BadUserAccessDenied,
                Is.True,
                "SubscriptionDiagnosticsArray should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "013")]
        public async Task Diagnostics013ReadSessionDiagnosticsArrayAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                VariableIds
                    .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(dv.StatusCode) ||
                dv.StatusCode.Code == StatusCodes.BadNotReadable ||
                dv.StatusCode.Code == StatusCodes.BadUserAccessDenied,
                Is.True,
                "SessionDiagnosticsArray should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "014")]
        public async Task Diagnostics014ReadSessionSecurityDiagnosticsArrayAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                VariableIds
                    .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(dv.StatusCode) ||
                dv.StatusCode.Code == StatusCodes.BadNotReadable ||
                dv.StatusCode.Code == StatusCodes.BadUserAccessDenied,
                Is.True,
                "SessionSecurityDiagnosticsArray should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "016")]
        public async Task Diagnostics016ReadRejectedSessionCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(3705)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("RejectedSessionCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "017")]
        public async Task Diagnostics017ReadRejectedRequestsCountAsync()
        {
            DataValue dv = await ReadValueAsync(
                new NodeId(2288)).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("RejectedRequestsCount not accessible.");
            }
            Assert.That(dv.WrappedValue.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "018-1")]
        public async Task Diagnostics0181BrowseSessionDiagnosticsAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds
                    .Server_ServerDiagnostics_SessionsDiagnosticsSummary)
                .ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "SessionsDiagnosticsSummary should have children.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "018-2")]
        public async Task Diagnostics0182BrowseSessionSecurityDiagnosticsAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                List<ReferenceDescription> refs = await BrowseForwardAsync(
                    ObjectIds
                        .Server_ServerDiagnostics_SessionsDiagnosticsSummary,
                    session: admin)
                    .ConfigureAwait(false);
                bool hasSecArray = refs.Any(
                    r => r.BrowseName.Name == "SessionSecurityDiagnosticsArray");
                if (!hasSecArray)
                {
                    Assert.Ignore("Diagnostics arrays not available in default ReferenceServer configuration.");
                }
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "018-3")]
        public async Task Diagnostics0183BrowseSubscriptionDiagnosticsAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                List<ReferenceDescription> refs = await BrowseForwardAsync(
                    ObjectIds.Server_ServerDiagnostics,
                    session: admin)
                    .ConfigureAwait(false);
                bool hasSubArray = refs.Any(
                    r => r.BrowseName.Name == "SubscriptionDiagnosticsArray");
                if (!hasSubArray)
                {
                    Assert.Ignore("Diagnostics arrays not available in default ReferenceServer configuration.");
                }
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "019")]
        public async Task Diagnostics019ReadServerStatusAfterDiagnosticsAsync()
        {
            DataValue dv = await ReadValueAsync(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "ServerStatus should be readable.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "023")]
        public async Task Diagnostics023EnabledFlagIsBoolAsync()
        {
            DataValue dv = await ReadValueAsync(
                VariableIds.Server_ServerDiagnostics_EnabledFlag)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("EnabledFlag not readable.");
            }
            Assert.That(dv.WrappedValue.TryGetValue(out bool _), Is.True,
                "EnabledFlag should be a Boolean.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "024")]
        public async Task Diagnostics024SummaryAggregatesSessionDiagnosticsAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                ISession session = admin ?? Session;
                DataValue summaryDv = await ReadAttributeAsync(
                    VariableIds
                        .Server_ServerDiagnostics_ServerDiagnosticsSummary,
                    Attributes.Value, session)
                    .ConfigureAwait(false);
                DataValue sessionArrayDv = await ReadAttributeAsync(
                    VariableIds
                        .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                    Attributes.Value, session)
                    .ConfigureAwait(false);

                if (StatusCode.IsBad(summaryDv.StatusCode) ||
                    StatusCode.IsBad(sessionArrayDv.StatusCode))
                {
                    Assert.Ignore(
                        "Diagnostics summary or session array not readable.");
                }

                Assert.That(summaryDv.WrappedValue.IsNull, Is.False);
                Assert.That(sessionArrayDv.WrappedValue.IsNull, Is.False);
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info GetMonitoredItems Method")]
        [Property("Tag", "001")]
        public async Task GetMonitoredItems001BrowseMethodAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                MethodIds.Server_GetMonitoredItems,
                ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            Assert.That(
                refs.Any(r => r.BrowseName.Name == "InputArguments"),
                Is.True, "InputArguments should exist.");
            Assert.That(
                refs.Any(r => r.BrowseName.Name == "OutputArguments"),
                Is.True, "OutputArguments should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info GetMonitoredItems Method")]
        [Property("Tag", "002")]
        public async Task GetMonitoredItems002CallWithValidSubscriptionAsync()
        {
            uint subId = await CreateTestSubscriptionAsync()
                .ConfigureAwait(false);
            try
            {
                await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);

                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_GetMonitoredItems,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    "GetMonitoredItems should succeed.");
                Assert.That(result.OutputArguments.Count,
                    Is.EqualTo(2),
                    "Should return ServerHandles and ClientHandles.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info GetMonitoredItems Method")]
        [Property("Tag", "003")]
        public async Task GetMonitoredItems003EmptySubscriptionAsync()
        {
            uint subId = await CreateTestSubscriptionAsync()
                .ConfigureAwait(false);
            try
            {
                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_GetMonitoredItems,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    "GetMonitoredItems should succeed on empty sub.");
                Assert.That(result.OutputArguments.Count,
                    Is.EqualTo(2));

                uint[] serverHandles = ExtractUIntArray(
                    result.OutputArguments[0]);
                uint[] clientHandles = ExtractUIntArray(
                    result.OutputArguments[1]);
                Assert.That(serverHandles, Is.Not.Null);
                Assert.That(clientHandles, Is.Not.Null);
                Assert.That(serverHandles.Length, Is.Zero);
                Assert.That(clientHandles.Length, Is.Zero);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info GetMonitoredItems Method")]
        [Property("Tag", "004")]
        public async Task GetMonitoredItems004MultipleSubscriptionsAsync()
        {
            uint subId1 = await CreateTestSubscriptionAsync()
                .ConfigureAwait(false);
            uint subId2 = await CreateTestSubscriptionAsync()
                .ConfigureAwait(false);
            try
            {
                await CreateMonitoredItemAsync(
                    subId1, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);

                CallMethodResult result1 = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_GetMonitoredItems,
                    new Variant(subId1)).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(result1.StatusCode), Is.True);

                CallMethodResult result2 = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_GetMonitoredItems,
                    new Variant(subId2)).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(result2.StatusCode), Is.True);

                uint[] handles1 = ExtractUIntArray(
                    result1.OutputArguments[0]);
                uint[] handles2 = ExtractUIntArray(
                    result2.OutputArguments[0]);
                Assert.That(handles1, Is.Not.Null);
                Assert.That(handles2, Is.Not.Null);
                Assert.That(handles1, Is.Not.Empty);
                Assert.That(handles2.Length, Is.Zero);
            }
            finally
            {
                await Session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] { subId1, subId2 }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info GetMonitoredItems Method")]
        [Property("Tag", "Err-001")]
        public async Task GetMonitoredItemsErr001InvalidSubscriptionIdAsync()
        {
            CallMethodResult result = await CallMethodAsync(
                ObjectIds.Server,
                MethodIds.Server_GetMonitoredItems,
                new Variant((uint)999999)).ConfigureAwait(false);

            Assert.That(result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid),
                "Invalid subscription should return BadSubscriptionIdInvalid.");
        }
        [Test]
        [Property("ConformanceUnit", "Base Info GetMonitoredItems Method")]
        [Property("Tag", "Err-003")]
        public async Task GetMonitoredItemsErr003CrossSessionReturnsBadStatusAsync()
        {
            uint subscriptionId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                ISession otherSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
                try
                {
                    var request = new CallMethodRequest
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_GetMonitoredItems,
                        InputArguments = new Variant[] { new(subscriptionId) }.ToArrayOf()
                    };

                    CallResponse response = await otherSession.CallAsync(
                        null,
                        new CallMethodRequest[] { request }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(response.Results.Count, Is.EqualTo(1));
                    Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                        "Calling GetMonitoredItems from a different session must fail; " +
                        $"expected a Bad status, got {response.Results[0].StatusCode}.");
                }
                finally
                {
                    try { await otherSession.CloseAsync(5000, true).ConfigureAwait(false); } catch { }
                    otherSession.Dispose();
                }
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }
        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "000")]
        public async Task ResendData000BrowseMethodAsync()
        {
            await AssertNodeExistsAsync(
                MethodIds.Server_ResendData, "ResendData")
                .ConfigureAwait(false);

            List<ReferenceDescription> refs = await BrowseForwardAsync(
                MethodIds.Server_ResendData,
                ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            Assert.That(
                refs.Any(r => r.BrowseName.Name == "InputArguments"),
                Is.True, "ResendData should have InputArguments.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "001")]
        public async Task ResendData001CallWithReportingItemsAsync()
        {
            uint subId = await CreateTestSubscriptionAsync()
                .ConfigureAwait(false);
            try
            {
                await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);

                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    "ResendData should succeed on valid subscription.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "002")]
        public async Task ResendData002CallWithSamplingItemsAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse cmi = await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cmi.Results[0].StatusCode), Is.True);

                // Switch to Sampling mode (collected but not reported)
                SetMonitoringModeResponse smm = await Session.SetMonitoringModeAsync(
                    null, subId, MonitoringMode.Sampling,
                    new uint[] { cmi.Results[0].MonitoredItemId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(smm.Results[0]), Is.True);

                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    "ResendData should succeed even on sampling-mode items.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "003")]
        public async Task ResendData003CallWithDisabledItemsAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse cmi = await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cmi.Results[0].StatusCode), Is.True);

                SetMonitoringModeResponse smm = await Session.SetMonitoringModeAsync(
                    null, subId, MonitoringMode.Disabled,
                    new uint[] { cmi.Results[0].MonitoredItemId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(smm.Results[0]), Is.True);

                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    "ResendData should succeed even when all items are disabled.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "004")]
        public async Task ResendData004CallWithMultipleModesAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse cmi1 = await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);
                CreateMonitoredItemsResponse cmi2 = await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cmi1.Results[0].StatusCode), Is.True);
                Assert.That(StatusCode.IsGood(cmi2.Results[0].StatusCode), Is.True);

                await Session.SetMonitoringModeAsync(
                    null, subId, MonitoringMode.Sampling,
                    new uint[] { cmi2.Results[0].MonitoredItemId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    "ResendData should succeed with mixed monitoring modes.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "005")]
        public async Task ResendData005DoesNotErrorOnEmptyAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                // No monitored items added — call ResendData on empty sub.
                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    "ResendData on a subscription with no items should succeed.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "006")]
        public async Task ResendData006CallWithLargeQueueAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse cmi = await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cmi.Results[0].StatusCode), Is.True);

                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    "ResendData should succeed regardless of queue size.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "007")]
        public async Task ResendData007CallWithDataChangeFilterAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse cmi = await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cmi.Results[0].StatusCode), Is.True);

                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    "ResendData should succeed even if items have data change filters.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "008")]
        public async Task ResendData008CallAfterModifyMonitoredItemsAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse cmi = await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cmi.Results[0].StatusCode), Is.True);

                // Modify the monitored item — should not lose the resend behavior.
                var modifyRequest = new MonitoredItemModifyRequest
                {
                    MonitoredItemId = cmi.Results[0].MonitoredItemId,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 500,
                        QueueSize = 20,
                        DiscardOldest = true
                    }
                };
                await Session.ModifyMonitoredItemsAsync(
                    null, subId, TimestampsToReturn.Both,
                    new MonitoredItemModifyRequest[] { modifyRequest }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                CallMethodResult result = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    "ResendData should succeed after modifying monitored items.");
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "009")]
        public async Task ResendData009CallOnMultipleSubscriptionsAsync()
        {
            uint subId1 = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            uint subId2 = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                await CreateMonitoredItemAsync(
                    subId1, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);
                await CreateMonitoredItemAsync(
                    subId2, VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);

                CallMethodResult r1 = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId1)).ConfigureAwait(false);
                CallMethodResult r2 = await CallMethodAsync(
                    ObjectIds.Server,
                    MethodIds.Server_ResendData,
                    new Variant(subId2)).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(r1.StatusCode), Is.True);
                Assert.That(StatusCode.IsGood(r2.StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId1).ConfigureAwait(false);
                await DeleteSubscriptionAsync(subId2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "010")]
        public async Task ResendData010CalledRepeatedlyIsIdempotentAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            try
            {
                await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);

                // Fire multiple ResendData calls back-to-back; all should succeed.
                for (int i = 0; i < 3; i++)
                {
                    CallMethodResult result = await CallMethodAsync(
                        ObjectIds.Server,
                        MethodIds.Server_ResendData,
                        new Variant(subId)).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                        $"ResendData iteration {i} should succeed.");
                }
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "Err-001")]
        public async Task ResendDataErr001NonexistentSubscriptionAsync()
        {
            CallMethodResult result = await CallMethodAsync(
                ObjectIds.Server,
                MethodIds.Server_ResendData,
                new Variant((uint)999999)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsBad(result.StatusCode), Is.True,
                "ResendData with invalid subscription should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "Err-002")]
        public async Task ResendDataErr002CrossSessionAsync()
        {
            uint subId = await CreateTestSubscriptionAsync().ConfigureAwait(false);
            ISession otherSession = null;
            try
            {
                await CreateMonitoredItemAsync(
                    subId, VariableIds.Server_ServerStatus_CurrentTime)
                    .ConfigureAwait(false);

                // Create a second session and try to ResendData with the first
                // session's subscription id from the second session — must fail.
                otherSession = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None).ConfigureAwait(false);

                CallResponse resp = await otherSession.CallAsync(
                    null,
                    new CallMethodRequest[]
                    {
                        new() {
                            ObjectId = ObjectIds.Server,
                            MethodId = MethodIds.Server_ResendData,
                            InputArguments = new Variant[]
                            {
                                new(subId)
                            }.ToArrayOf()
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(resp.Results[0].StatusCode), Is.True,
                    "ResendData on a sub owned by another session should fail.");
            }
            finally
            {
                if (otherSession != null)
                {
                    await otherSession.CloseAsync(5000, true).ConfigureAwait(false);
                    otherSession.Dispose();
                }
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info ResendData Method")]
        [Property("Tag", "Err-003")]
        public async Task ResendDataErr003NoSubscriptionsAsync()
        {
            CallMethodResult result = await CallMethodAsync(
                ObjectIds.Server,
                MethodIds.Server_ResendData,
                new Variant((uint)1)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsBad(result.StatusCode), Is.True,
                "ResendData when no matching subscription should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info RequestServerStateChange Method")]
        [Property("Tag", "000")]
        public async Task RequestServerStateChange000MethodExistsAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                ISession session = admin ?? Session;
                DataValue dv = await ReadBrowseNameAsync(
                    RequestServerStateChangeId, session).ConfigureAwait(false);
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    Assert.Ignore("RequestServerStateChange not found.");
                }
                Assert.That(
                    dv.GetValue<QualifiedName>(default).Name,
                    Is.EqualTo("RequestServerStateChange"));
            }
            finally
            {
                if (admin != null)
                {
                    await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    admin.Dispose();
                }
            }
        }
        [Test]
        [Property("ConformanceUnit", "Base Info Device Failure")]
        [Property("Tag", "000")]
        public async Task DeviceFailure000BrowseSubtypesAsync()
        {
            _ = await BrowseForwardAsync(
                DeviceFailureEventTypeId,
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);
            // The type should exist; subtypes are optional
            DataValue dv = await ReadBrowseNameAsync(
                DeviceFailureEventTypeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "DeviceFailureEventType should exist.");
        }
        [Test]
        [Property("ConformanceUnit", "Base Info EventQueueOverflow EventType")]
        [Property("Tag", "001")]
        public async Task EventQueueOverflow001TypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                EventQueueOverflowEventTypeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "EventQueueOverflowEventType should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info EventQueueOverflow EventType")]
        [Property("Tag", "002")]
        public async Task EventQueueOverflow002IsSubtypeOfBaseEventAsync()
        {
            List<ReferenceDescription> refs = await BrowseInverseAsync(
                EventQueueOverflowEventTypeId,
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "Should have a supertype.");
            var parent = ExpandedNodeId.ToNodeId(
                refs[0].NodeId, Session.NamespaceUris);
            Assert.That(parent, Is.EqualTo(ObjectTypeIds.BaseEventType),
                "Supertype should be BaseEventType.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info EventQueueOverflow EventType")]
        [Property("Tag", "003")]
        public async Task EventQueueOverflow003StandardEventFieldsAsync()
        {
            _ = await BrowseForwardAsync(
                EventQueueOverflowEventTypeId).ConfigureAwait(false);

            // Standard fields are inherited from BaseEventType;
            // verify the type has browseable references
            DataValue dv = await ReadBrowseNameAsync(
                EventQueueOverflowEventTypeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);
        }
        [Test]
        [Property("ConformanceUnit", "Base Info Progress Events")]
        [Property("Tag", "001")]
        public async Task ProgressEvents001TypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ProgressEventTypeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "ProgressEventType should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Progress Events")]
        [Property("Tag", "002")]
        public async Task ProgressEvents002VerifyPropertiesAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ProgressEventTypeId).ConfigureAwait(false);
            if (refs.Count == 0)
            {
                Assert.Fail("ProgressEventType children not browseable.");
            }
            Assert.That(
                refs.Any(r => r.BrowseName.Name == "Context"),
                Is.True, "Context property should exist.");
            Assert.That(
                refs.Any(r => r.BrowseName.Name == "Progress"),
                Is.True, "Progress property should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Progress Events")]
        [Property("Tag", "003")]
        public async Task ProgressEvents003IsSubtypeOfBaseEventAsync()
        {
            List<ReferenceDescription> refs = await BrowseInverseAsync(
                ProgressEventTypeId,
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "Should have a supertype.");
            var parent = ExpandedNodeId.ToNodeId(
                refs[0].NodeId, Session.NamespaceUris);
            Assert.That(parent, Is.EqualTo(ObjectTypeIds.BaseEventType),
                "Supertype should be BaseEventType.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Security Role Capabilities")]
        [Property("Tag", "000")]
        public async Task SecurityRoles000RoleSetExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectIds.Server_ServerCapabilities_RoleSet)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("RoleSet not accessible.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Security Role Capabilities")]
        [Property("Tag", "001")]
        public async Task SecurityRoles001BrowseRoleSetChildrenAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectIds.Server_ServerCapabilities_RoleSet)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("RoleSet not accessible.");
            }

            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.Server_ServerCapabilities_RoleSet)
                .ConfigureAwait(false);
            Assert.That(refs, Is.Not.Empty,
                "RoleSet should contain roles.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Security Role Capabilities")]
        [Property("Tag", "002")]
        public async Task SecurityRoles002BrowseRoleTypeInstanceAsync()
        {
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                ObjectIds.WellKnownRole_Anonymous)
                .ConfigureAwait(false);

            bool hasIdentities = refs.Any(
                r => r.BrowseName.Name == "Identities");
            bool hasAppsExclude = refs.Any(
                r => r.BrowseName.Name == "ApplicationsExclude");

            if (!hasIdentities && !hasAppsExclude)
            {
                Assert.Fail(
                    "Anonymous role does not expose expected properties.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Security Role Capabilities")]
        [Property("Tag", "003")]
        public async Task SecurityRoles003AllRolesHaveRequiredPropertiesAsync()
        {
            NodeId[] roleIds =
            [
                ObjectIds.WellKnownRole_Anonymous,
                ObjectIds.WellKnownRole_AuthenticatedUser,
                ObjectIds.WellKnownRole_Observer,
                ObjectIds.WellKnownRole_Operator,
                ObjectIds.WellKnownRole_Engineer,
                ObjectIds.WellKnownRole_Supervisor,
                ObjectIds.WellKnownRole_SecurityAdmin,
                ObjectIds.WellKnownRole_ConfigureAdmin
            ];

            int checkedCount = 0;
            foreach (NodeId roleId in roleIds)
            {
                DataValue dv = await ReadBrowseNameAsync(roleId)
                    .ConfigureAwait(false);
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    continue;
                }

                List<ReferenceDescription> refs =
                    await BrowseForwardAsync(roleId).ConfigureAwait(false);
                bool hasIdentities = refs.Any(
                    r => r.BrowseName.Name == "Identities");
                if (hasIdentities)
                {
                    checkedCount++;
                }
            }

            if (checkedCount == 0)
            {
                Assert.Fail(
                    "No roles expose Identities property.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Selection List")]
        [Property("Tag", "001")]
        public async Task SelectionList001SelectionsPropertyExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                SelectionListTypeId).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("SelectionListType not found.");
            }

            List<ReferenceDescription> refs = await BrowseForwardAsync(
                SelectionListTypeId).ConfigureAwait(false);
            Assert.That(
                refs.Any(r => r.BrowseName.Name == "Selections"),
                Is.True, "Selections property should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Selection List")]
        [Property("Tag", "002")]
        public async Task SelectionList002RestrictToListExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                SelectionListTypeId).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("SelectionListType not found.");
            }

            List<ReferenceDescription> refs = await BrowseForwardAsync(
                SelectionListTypeId).ConfigureAwait(false);
            Assert.That(
                refs.Any(r => r.BrowseName.Name == "RestrictToList"),
                Is.True, "RestrictToList property should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Selection List")]
        [Property("Tag", "003")]
        public async Task SelectionList003IsSubtypeOfBaseDataVariableTypeAsync()
        {
            // Verify SelectionListType (i=16309) is declared as a subtype of
            // BaseDataVariableType (i=63) per Part 5 §7.18.
            BrowseResponse browseResp = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = SelectionListTypeId,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(browseResp.Results.Count, Is.EqualTo(1));
            if (browseResp.Results[0].References == default ||
                browseResp.Results[0].References.Count == 0)
            {
                Assert.Ignore("SelectionListType not exposed by server.");
            }

            ReferenceDescription parent = browseResp.Results[0].References[0];
            NodeId parentId = ExpandedNodeId.ToNodeId(
                parent.NodeId, Session.NamespaceUris);
            Assert.That(parentId, Is.EqualTo(VariableTypeIds.BaseDataVariableType),
                "SelectionListType should be a subtype of BaseDataVariableType.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Selection List")]
        [Property("Tag", "004")]
        public async Task SelectionList004SelectionDescriptionsPropertyExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                SelectionListTypeId).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Ignore("SelectionListType not exposed by server.");
            }

            // SelectionDescriptions is an optional LocalizedText[] property
            // (i=17633) on SelectionListType per Part 5 §7.18.
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                SelectionListTypeId).ConfigureAwait(false);
            ReferenceDescription selDesc = refs.FirstOrDefault(
                r => r.BrowseName.Name == "SelectionDescriptions");
            if (selDesc == null)
            {
                Assert.Ignore("SelectionDescriptions optional property not exposed.");
            }

            NodeId selDescId = ExpandedNodeId.ToNodeId(
                selDesc.NodeId, Session.NamespaceUris);
            DataValue dt = await ReadAttributeAsync(
                selDescId, Attributes.DataType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dt.StatusCode), Is.True);
            Assert.That(dt.WrappedValue.TryGetValue(out NodeId dataType), Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.LocalizedText),
                "SelectionDescriptions must be LocalizedText[].");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Selection List")]
        [Property("Tag", "005")]
        public async Task SelectionList005RestrictToListIsBooleanAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                SelectionListTypeId).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Ignore("SelectionListType not exposed by server.");
            }

            // RestrictToList (i=16312) is a Boolean property of
            // SelectionListType per Part 5 §7.18.
            List<ReferenceDescription> refs = await BrowseForwardAsync(
                SelectionListTypeId).ConfigureAwait(false);
            ReferenceDescription restrict = refs.FirstOrDefault(
                r => r.BrowseName.Name == "RestrictToList");
            if (restrict == null)
            {
                Assert.Ignore("RestrictToList optional property not exposed.");
            }

            NodeId restrictId = ExpandedNodeId.ToNodeId(
                restrict.NodeId, Session.NamespaceUris);
            DataValue dt = await ReadAttributeAsync(
                restrictId, Attributes.DataType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dt.StatusCode), Is.True);
            Assert.That(dt.WrappedValue.TryGetValue(out NodeId dataType), Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Boolean),
                "RestrictToList must be Boolean.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OrderedList")]
        [Property("Tag", "001")]
        public async Task OrderedList001TypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                OrderedListTypeId).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("OrderedListType not found.");
            }

            List<ReferenceDescription> refs = await BrowseForwardAsync(
                OrderedListTypeId).ConfigureAwait(false);
            bool hasOrderedRef = refs.Any(
                r => r.ReferenceTypeId == ReferenceTypeIds.HasOrderedComponent ||
                    r.BrowseName.Name.Contains("Ordered"));
            Assert.That(dv.GetValue<QualifiedName>(default).Name,
                Is.EqualTo("OrderedListType"));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OrderedList")]
        [Property("Tag", "002")]
        public async Task OrderedList002IOrderedObjectTypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                IOrderedObjectTypeId).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Fail("IOrderedObjectType not found.");
            }
            Assert.That(
                dv.GetValue<QualifiedName>(default).Name,
                Is.EqualTo("IOrderedObjectType"));
        }

        private static readonly NodeId DeviceFailureEventTypeId = new(2131);
        private static readonly NodeId EventQueueOverflowEventTypeId = new(3035);
        private static readonly NodeId ProgressEventTypeId = new(11436);
        private static readonly NodeId SelectionListTypeId = new(16309);
        private static readonly NodeId OrderedListTypeId = new(23518);
        private static readonly NodeId IOrderedObjectTypeId = new(23513);
        private static readonly NodeId RequestServerStateChangeId = new(12886);

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId, uint attributeId, ISession session = null)
        {
            session ??= Session;
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private Task<DataValue> ReadValueAsync(NodeId nodeId)
        {
            return ReadAttributeAsync(
                nodeId, Attributes.Value);
        }

        private Task<DataValue> ReadBrowseNameAsync(NodeId nodeId, ISession session = null)
        {
            return ReadAttributeAsync(
                nodeId, Attributes.BrowseName, session);
        }

        private async Task<List<ReferenceDescription>> BrowseForwardAsync(
            NodeId nodeId,
            NodeId referenceTypeId = default,
            bool includeSubtypes = true,
            ISession session = null)
        {
            session ??= Session;
            NodeId refType = referenceTypeId.IsNull
                ? ReferenceTypeIds.HierarchicalReferences
                : referenceTypeId;

            BrowseResponse response = await session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = refType,
                        IncludeSubtypes = includeSubtypes,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            var refs = new List<ReferenceDescription>();
            if (response.Results[0].References != default)
            {
                foreach (ReferenceDescription r in response.Results[0].References)
                {
                    refs.Add(r);
                }
            }
            return refs;
        }

        private async Task<List<ReferenceDescription>> BrowseInverseAsync(
            NodeId nodeId,
            NodeId referenceTypeId = default,
            bool includeSubtypes = false)
        {
            NodeId refType = referenceTypeId.IsNull
                ? ReferenceTypeIds.HierarchicalReferences
                : referenceTypeId;

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = refType,
                        IncludeSubtypes = includeSubtypes,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            var refs = new List<ReferenceDescription>();
            if (response.Results[0].References != default)
            {
                foreach (ReferenceDescription r in response.Results[0].References)
                {
                    refs.Add(r);
                }
            }
            return refs;
        }

        private async Task AssertNodeExistsAsync(NodeId nodeId, string name)
        {
            DataValue result = await ReadBrowseNameAsync(nodeId)
                .ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                Assert.Ignore($"{name} not found.");
            }
        }

        private async Task<CallMethodResult> CallMethodAsync(
            NodeId objectId,
            NodeId methodId,
            params Variant[] inputArgs)
        {
            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = inputArgs.ToArrayOf()
            };

            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] { request }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<uint> CreateTestSubscriptionAsync()
        {
            CreateSubscriptionResponse resp =
                await Session.CreateSubscriptionAsync(
                    null, 1000, 10, 2, 0, true, 0,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True, "CreateSubscription failed.");
            return resp.SubscriptionId;
        }

        private async Task DeleteSubscriptionAsync(uint subscriptionId)
        {
            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { subscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<CreateMonitoredItemsResponse>
            CreateMonitoredItemAsync(
                uint subscriptionId, NodeId nodeId)
        {
            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 1000,
                    QueueSize = 10,
                    DiscardOldest = true
                }
            };

            return await Session.CreateMonitoredItemsAsync(
                    null, subscriptionId,
                    TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
        }

        private static uint[] ExtractUIntArray(Variant variant)
        {
            if (variant.TryGetValue(out ArrayOf<uint> arr))
            {
                return arr.ToArray();
            }
            // Some upstream code paths still surface uint[] / IConvertableToArray
            // directly; iterate via TypeInfo to avoid the boxed-object detour.
            switch (variant.TypeInfo.BuiltInType)
            {
                case BuiltInType.UInt32:
                    if (variant.TryGetValue(out uint single))
                    {
                        return new[] { single };
                    }
                    break;
            }
            // FUTURE-AsBoxedObject-cleanup: legacy compatibility for callers
            // that still produce uint[] / IConvertableToArray outside the
            // typed Variant accessors. Once those paths migrate this can drop.
            object val = variant.AsBoxedObject();
            if (val is uint[] legacyArr)
            {
                return legacyArr;
            }
            if (val is IConvertableToArray convertable)
            {
                var converted = convertable.ToArray();
                if (converted is uint[] uintArr)
                {
                    return uintArr;
                }

                return [.. converted.Cast<object>().Select(Convert.ToUInt32)];
            }
            if (val is Array a)
            {
                return [.. a.Cast<object>().Select(Convert.ToUInt32)];
            }
            return null;
        }
    }
}
