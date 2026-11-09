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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// R07: historical event reads deliver an event only when the caller of the current request
    /// holds ReceiveEvents on the stored EventType and SourceNode.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Category("SecurityRegression")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HistorianEventAuthorizationSecurityTests
    {
        [Test]
        public async Task SecurityR07HistoryReadFiltersSourceAndTypeDeniedRecordsAsync()
        {
            var fixture = new Fixture();
            HistorianEventRecord allowed = Record("allowed", s_allowedType, new Variant(s_allowedSource));
            HistorianEventRecord deniedSource = Record("source", s_allowedType, new Variant(s_deniedSource));
            HistorianEventRecord deniedType = Record("type", s_deniedType, new Variant(s_allowedSource));

            ArrayOf<HistorianEventRecord> result = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                fixture.CreateContext(s_role),
                s_notifier,
                [deniedSource, allowed, deniedType],
                CancellationToken.None);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].EventId, Is.EqualTo(allowed.EventId));
        }

        [Test]
        public async Task SecurityR07HistoryReadExcludesUnknownAndMalformedIdentitiesAsync()
        {
            var fixture = new Fixture();
            HistorianEventRecord unknownSource = Record("unknown", s_allowedType, new Variant(s_unknown));
            HistorianEventRecord unknownType = Record("unknown-type", s_unknown, new Variant(s_allowedSource));
            HistorianEventRecord malformed = Record("malformed", s_allowedType, new Variant("not-a-node"));
            HistorianEventRecord nullSource = Record("null-source", s_allowedType, new Variant(NodeId.Null));
            HistorianEventRecord nullType = Record("null-type", NodeId.Null, new Variant(s_allowedSource));

            ArrayOf<HistorianEventRecord> result = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                fixture.CreateContext(s_role),
                s_notifier,
                [unknownSource, unknownType, malformed, nullSource, nullType],
                CancellationToken.None);

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public async Task SecurityR07HistoryReadUsesNotifierWhenSourceNodeWasNotStoredAsync()
        {
            var fixture = new Fixture();
            HistorianEventRecord stored = Record("notifier", s_allowedType, null);

            ArrayOf<HistorianEventRecord> granted = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                fixture.CreateContext(s_role),
                s_allowedSource,
                [stored],
                CancellationToken.None);
            ArrayOf<HistorianEventRecord> denied = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                fixture.CreateContext(s_role),
                s_deniedSource,
                [stored],
                CancellationToken.None);

            Assert.That(granted.Count, Is.EqualTo(1));
            Assert.That(denied.Count, Is.Zero);
        }

        [Test]
        public async Task SecurityR07HistoryReadAuthorizesEachPageForCurrentCallerAsync()
        {
            var fixture = new Fixture();
            ArrayOf<HistorianEventRecord> page =
                [Record("page", s_allowedType, new Variant(s_allowedSource))];

            ArrayOf<HistorianEventRecord> first = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                fixture.CreateContext(s_role), s_notifier, page, CancellationToken.None);
            ArrayOf<HistorianEventRecord> continuation = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                fixture.CreateContext(s_otherRole), s_notifier, page, CancellationToken.None);

            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(continuation.Count, Is.Zero);
            fixture.Master.Verify(
                m => m.GetManagerHandleAsync(s_allowedType, It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Test]
        public async Task SecurityR07HistoryReadEnforcesSourceAccessRestrictionsAsync()
        {
            var fixture = new Fixture();
            HistorianEventRecord restricted = Record("restricted", s_allowedType, new Variant(s_encryptedSource));

            ArrayOf<HistorianEventRecord> result = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                fixture.CreateContext(s_role),
                s_notifier,
                [restricted],
                CancellationToken.None);

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public async Task SecurityR07HistoryReadFailsClosedWhenMetadataLookupThrowsAsync()
        {
            var fixture = new Fixture();
            HistorianEventRecord failing = Record("failing", s_allowedType, new Variant(s_failingSource));

            ArrayOf<HistorianEventRecord> result = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                fixture.CreateContext(s_role),
                s_notifier,
                [failing],
                CancellationToken.None);

            Assert.That(result.Count, Is.Zero);
        }

        [Test]
        public async Task SecurityR07TrustedInternalHistoryReadKeepsStoredRecordsAsync()
        {
            var fixture = new Fixture();
            ArrayOf<HistorianEventRecord> records =
            [
                Record("denied", s_deniedType, new Variant(s_deniedSource)),
                Record("malformed", s_allowedType, new Variant("x"))
            ];

            ArrayOf<HistorianEventRecord> result = await HistorianDispatcher.AuthorizeHistoricalEventsAsync(
                new ServerSystemContext(fixture.Server.Object),
                s_notifier,
                records,
                CancellationToken.None);

            Assert.That(result.Count, Is.EqualTo(2));
            fixture.Master.Verify(
                m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static HistorianEventRecord Record(string id, NodeId eventType, Variant? sourceNode)
        {
            var fields = new Dictionary<string, Variant>(StringComparer.Ordinal)
            {
                [BrowseNames.EventType] = new Variant(eventType)
            };
            if (sourceNode is Variant source)
            {
                fields[BrowseNames.SourceNode] = source;
            }
            return new HistorianEventRecord(
                new ByteString(Encoding.UTF8.GetBytes(id)),
                eventType,
                new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                fields.ToArrayOf());
        }

        private static readonly NodeId s_notifier = new("notifier", 2);
        private static readonly NodeId s_allowedSource = new("allowed-source", 2);
        private static readonly NodeId s_deniedSource = new("denied-source", 2);
        private static readonly NodeId s_encryptedSource = new("encrypted-source", 2);
        private static readonly NodeId s_failingSource = new("failing-source", 2);
        private static readonly NodeId s_allowedType = new("allowed-type", 2);
        private static readonly NodeId s_deniedType = new("denied-type", 2);
        private static readonly NodeId s_unknown = new("unknown", 2);
        private static readonly NodeId s_role = new("operator", 2);
        private static readonly NodeId s_otherRole = new("observer", 2);

        /// <summary>
        /// A master node manager whose nodes grant ReceiveEvents only to the operator role.
        /// </summary>
        private sealed class Fixture
        {
            public Fixture()
            {
                Server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
                Server.Setup(s => s.ServerUris).Returns(new StringTable());
                Server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                Server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                Server.Setup(s => s.Telemetry).Returns(new Mock<ITelemetryContext>().Object);
                Server.Setup(s => s.NodeManager).Returns(Master.Object);
                Master
                    .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns((NodeId nodeId, CancellationToken _) =>
                        new ValueTask<(object?, IAsyncNodeManager?)>(
                            nodeId == s_unknown
                                ? ((object?)null, (IAsyncNodeManager?)null)
                                : ((object?)nodeId, (IAsyncNodeManager?)Manager.Object)));
                Manager
                    .Setup(m => m.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, object handle, BrowseResultMask _, CancellationToken _) =>
                        CreateMetadata((NodeId)handle));
            }

            public Mock<IServerInternal> Server { get; } = new();

            public Mock<IMasterNodeManager> Master { get; } = new();

            public Mock<IAsyncNodeManager> Manager { get; } = new();

            public ServerSystemContext CreateContext(NodeId role)
            {
                var identity = new Mock<IUserIdentity>();
                identity.Setup(i => i.GrantedRoleIds).Returns([role]);
                var context = new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.HistoryRead,
                    RequestLifetime.None,
                    identity.Object);
                return new ServerSystemContext(Server.Object, context);
            }

            private static ValueTask<NodeMetadata> CreateMetadata(NodeId nodeId)
            {
                if (nodeId == s_failingSource)
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError);
                }
                bool denied = nodeId == s_deniedSource || nodeId == s_deniedType;
                PermissionType permissions = PermissionType.Browse |
                    (denied ? PermissionType.None : PermissionType.ReceiveEvents);
                var metadata = new NodeMetadata(nodeId, nodeId)
                {
                    NodeClass = NodeClass.Object,
                    RolePermissions =
                    [
                        new RolePermissionType { RoleId = s_role, Permissions = (uint)permissions },
                        new RolePermissionType { RoleId = s_otherRole, Permissions = (uint)PermissionType.Browse }
                    ]
                };
                if (nodeId == s_encryptedSource)
                {
                    metadata.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
                }
                return new ValueTask<NodeMetadata>(metadata);
            }
        }
    }
}