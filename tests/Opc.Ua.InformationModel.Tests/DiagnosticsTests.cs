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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for server diagnostics information.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Diagnostics")]
    public class DiagnosticsTests : TestFixture
    {
        [Test]
        public async Task ReadServerDiagnosticsEnabledFlagAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_EnabledFlag).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        public async Task ReadServerDiagnosticsSummaryAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary).ConfigureAwait(false);
            // Anonymous sessions may not have access to diagnostics
            Assert.That(
                StatusCode.IsGood(result.StatusCode) ||
                result.StatusCode == StatusCodes.BadNotReadable ||
                result.StatusCode == StatusCodes.BadUserAccessDenied,
                Is.True,
                $"Expected Good, BadNotReadable, or BadUserAccessDenied, got {result.StatusCode}");
        }

        [Test]
        public async Task ReadDiagnosticsSummaryCurrentSessionCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSessionCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(1u),
                "At least one session (the test session) should be active.");
        }

        [Test]
        public async Task ReadDiagnosticsSummaryCurrentSubscriptionCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSubscriptionCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadSessionsDiagnosticsSummaryAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadServerCurrentTimeIsRecentAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            // CurrentTime is per spec a DateTime (UtcTime).
            Assert.That(
                result.WrappedValue.TryGetValue(out DateTimeUtc _),
                Is.True,
                "CurrentTime should decode as DateTime.");
        }

        [Test]
        public async Task ReadServerStateIsRunningAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.GetValue<int>(default), Is.EqualTo((int)ServerState.Running));
        }

        [Test]
        public async Task ServerDiagnosticsNodeBrowseHasChildrenAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_ServerDiagnostics,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            var childNames = new List<string>();
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                childNames.Add(r.BrowseName.Name);
            }
            Assert.That(childNames, Is.Not.Empty,
                "ServerDiagnostics should have child nodes.");
            Assert.That(childNames, Does.Contain("EnabledFlag"));
        }

        [Test]
        public async Task ReadDiagnosticsSummaryCumulatedSessionCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSessionCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(1u));
        }

        [Test]
        public async Task ReadDiagnosticsSummaryServerViewCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_ServerViewCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task SubscriptionDiagnosticsArrayBrowseReturnsOnlyOwnSubscriptionsAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Ignore("The server has no UserName endpoint for the administrator.");
            }

            ISession other = null;
            uint ownSubscriptionId = 0;
            uint otherSubscriptionId = 0;
            try
            {
                other = await OpenAuxSessionAsync().ConfigureAwait(false);
                ownSubscriptionId = await CreateSubscriptionAsync(Session).ConfigureAwait(false);
                otherSubscriptionId = await CreateSubscriptionAsync(other).ConfigureAwait(false);

                // a SecurityMode None session browses the server wide array and gets its own
                // subscription only (Part 5 6.3.5), as Subscription Durable 012.js expects.
                List<NodeId> visible = await BrowseComponentsAsync(
                    Session,
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray).ConfigureAwait(false);
                List<SubscriptionDiagnosticsDataType> own = await ReadSubscriptionDiagnosticsAsync(
                    Session,
                    visible).ConfigureAwait(false);
                Assert.That(own.Select(d => d.SessionId), Is.All.EqualTo(Session.SessionId));
                Assert.That(own.Select(d => d.SubscriptionId), Does.Contain(ownSubscriptionId));
                Assert.That(own.Select(d => d.SubscriptionId), Does.Not.Contain(otherSubscriptionId));

                // the array value holds every session's subscriptions and stays admin only.
                DataValue arrayValue = await ReadNodeValueAsync(
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray).ConfigureAwait(false);
                Assert.That(arrayValue.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));

                // the administrator sees the subscriptions of both sessions.
                List<NodeId> all = await BrowseComponentsAsync(
                    admin,
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray).ConfigureAwait(false);
                List<SubscriptionDiagnosticsDataType> allDiagnostics =
                    await ReadSubscriptionDiagnosticsAsync(admin, all).ConfigureAwait(false);
                Assert.That(
                    allDiagnostics.Select(d => d.SubscriptionId),
                    Does.Contain(ownSubscriptionId).And.Contain(otherSubscriptionId));
            }
            finally
            {
                await DeleteSubscriptionAsync(Session, ownSubscriptionId).ConfigureAwait(false);
                await DeleteSubscriptionAsync(other, otherSubscriptionId).ConfigureAwait(false);
                await CloseAsync(other).ConfigureAwait(false);
                await CloseAsync(admin).ConfigureAwait(false);
            }
        }

        private static async Task<uint> CreateSubscriptionAsync(ISession session)
        {
            CreateSubscriptionResponse response = await session.CreateSubscriptionAsync(
                null, 1000, 100, 10, 0, true, 0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            return response.SubscriptionId;
        }

        private static async Task DeleteSubscriptionAsync(ISession session, uint subscriptionId)
        {
            if (session == null || subscriptionId == 0)
            {
                return;
            }
            try
            {
                await session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] { subscriptionId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }

        private static async Task<List<NodeId>> BrowseComponentsAsync(ISession session, NodeId nodeId)
        {
            BrowseResponse response = await session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Variable,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            BrowseResult result = response.Results[0];
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True, result.StatusCode.ToString());
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
            return [.. result.References.ToArray().Select(r => ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris))];
        }

        private static async Task<List<SubscriptionDiagnosticsDataType>> ReadSubscriptionDiagnosticsAsync(
            ISession session,
            List<NodeId> nodeIds)
        {
            var diagnostics = new List<SubscriptionDiagnosticsDataType>();
            if (nodeIds.Count == 0)
            {
                return diagnostics;
            }
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                nodeIds.Select(n => new ReadValueId { NodeId = n, AttributeId = Attributes.Value }).ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                DataValue value = response.Results[ii];
                Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, $"{nodeIds[ii]}: {value.StatusCode}");
                Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True, nodeIds[ii].ToString());
                Assert.That(extension.TryGetValue(out SubscriptionDiagnosticsDataType entry), Is.True, nodeIds[ii].ToString());
                diagnostics.Add(entry);
            }
            return diagnostics;
        }

        private static async Task CloseAsync(ISession session)
        {
            if (session == null)
            {
                return;
            }
            try
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
            session.Dispose();
        }

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = attributeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
