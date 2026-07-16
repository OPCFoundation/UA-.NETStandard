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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Tests for <see cref="ClientFailoverCoordinator"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientRedundancy")]
    public sealed class ClientFailoverCoordinatorTests
    {
        [Test]
        public async Task TransferActiveSubscriptionsUsesDiagnosticsAndTransferServiceAsync()
        {
            var sessionId = new NodeId(42);
            Mock<ISession> session = CreateSession("operator");
            SetupDiagnostics(session, sessionId, "active-client", [11u, 12u]);
            ArrayOf<uint> transferredIds = [];
            session.Setup(s => s.TransferSubscriptionsAsync(
                    null,
                    It.IsAny<ArrayOf<uint>>(),
                    true,
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ArrayOf<uint>, bool, CancellationToken>(
                    (_, ids, _, _) => transferredIds = ids)
                .ReturnsAsync(new TransferSubscriptionsResponse
                {
                    Results =
                    [
                        new TransferResult { StatusCode = StatusCodes.Good },
                        new TransferResult { StatusCode = StatusCodes.Good }
                    ],
                    DiagnosticInfos = []
                });

            var coordinator = new ClientFailoverCoordinator();
            ArrayOf<TransferResult> results = await coordinator
                .TransferActiveSubscriptionsAsync(
                    session.Object,
                    new ClientRedundancyTransferOptions
                    {
                        ActiveSessionName = "active-client",
                        ActiveUserDisplayName = "operator",
                        SendInitialValues = true
                    })
                .ConfigureAwait(false);

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(transferredIds, Is.EqualTo(new uint[] { 11, 12 }));
        }

        [Test]
        public void TransferActiveSubscriptionsRejectsDifferentUser()
        {
            Mock<ISession> session = CreateSession("backup-user");
            var coordinator = new ClientFailoverCoordinator();

            Assert.ThrowsAsync<UnauthorizedAccessException>(
                async () => await coordinator.TransferActiveSubscriptionsAsync(
                    session.Object,
                    new ClientRedundancyTransferOptions
                    {
                        ActiveUserDisplayName = "active-user",
                        ActiveSessionName = "active-client"
                    }).ConfigureAwait(false));
        }

        [Test]
        public async Task DiscoverActiveSubscriptionIdsReturnsEmptyWhenSessionNameIsUnknownAsync()
        {
            Mock<ISession> session = CreateSession("operator");
            SetupRead(
                session,
                VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                new DataValue(new Variant(new ArrayOf<ExtensionObject>()), StatusCodes.Good));

            var coordinator = new ClientFailoverCoordinator();
            ArrayOf<uint> ids = await coordinator
                .DiscoverActiveSubscriptionIdsAsync(
                    session.Object,
                    new ClientRedundancyTransferOptions
                    {
                        ActiveSessionName = "missing"
                    })
                .ConfigureAwait(false);

            Assert.That(ids, Is.Empty);
            session.Verify(s => s.TransferSubscriptionsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<uint>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task TransferActiveSubscriptionsReturnsEmptyWhenSubscriptionDiagnosticsAreUnavailableAsync()
        {
            var sessionId = new NodeId(42);
            Mock<ISession> session = CreateSession("operator");
            ArrayOf<ExtensionObject> sessions =
            [
                new ExtensionObject(new SessionDiagnosticsDataType
                {
                    SessionId = sessionId,
                    SessionName = "active-client"
                })
            ];
            SetupRead(
                session,
                VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                new DataValue(new Variant(sessions), StatusCodes.Good));
            SetupRead(
                session,
                VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray,
                new DataValue(new Variant(new ArrayOf<ExtensionObject>()), StatusCodes.Good));

            var coordinator = new ClientFailoverCoordinator();
            ArrayOf<TransferResult> results = await coordinator
                .TransferActiveSubscriptionsAsync(
                    session.Object,
                    new ClientRedundancyTransferOptions
                    {
                        ActiveSessionName = "active-client"
                    })
                .ConfigureAwait(false);

            Assert.That(results, Is.Empty);
        }

        [Test]
        public void DiscoverActiveSubscriptionIdsRejectsNullArguments()
        {
            var coordinator = new ClientFailoverCoordinator();
            Mock<ISession> session = CreateSession("operator");

            Assert.That(
                async () => await coordinator
                    .DiscoverActiveSubscriptionIdsAsync(null!, new ClientRedundancyTransferOptions())
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
            Assert.That(
                async () => await coordinator
                    .DiscoverActiveSubscriptionIdsAsync(session.Object, null!)
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        private static Mock<ISession> CreateSession(string displayName)
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Identity)
                .Returns(new UserIdentity { DisplayName = displayName });
            return session;
        }

        private static void SetupDiagnostics(
            Mock<ISession> session,
            NodeId sessionId,
            string sessionName,
            uint[] subscriptionIds)
        {
            ArrayOf<ExtensionObject> sessions =
            [
                new ExtensionObject(new SessionDiagnosticsDataType
                {
                    SessionId = sessionId,
                    SessionName = sessionName
                })
            ];
            var subscriptions = new ArrayOf<ExtensionObject>();
            foreach (uint subscriptionId in subscriptionIds)
            {
                subscriptions = subscriptions.AddItem(
                    new ExtensionObject(new SubscriptionDiagnosticsDataType
                    {
                        SessionId = sessionId,
                        SubscriptionId = subscriptionId
                    }));
            }

            SetupRead(
                session,
                VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                new DataValue(new Variant(sessions), StatusCodes.Good));
            SetupRead(
                session,
                VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray,
                new DataValue(new Variant(subscriptions), StatusCodes.Good));
        }

        private static void SetupRead(
            Mock<ISession> session,
            NodeId nodeId,
            DataValue value)
        {
            session.Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(r => r.Count == 1 && r[0].NodeId == nodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = [value],
                    DiagnosticInfos = []
                });
        }
    }
}
