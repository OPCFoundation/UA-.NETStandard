/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;

namespace Opc.Ua.Server.Tests.DataChannels
{
    /// <summary>
    /// Exercises real runtime readiness, revisions, scoped close and authorization lifetime.
    /// </summary>
    [TestFixture]
    [Category("DataChannelsControl")]
    public sealed class DataChannelControlLifetimeTests
    {
        /// <summary>
        /// Session/control snapshots share the binary protocol's unlimited-rate identity.
        /// </summary>
        [Test]
        public void ControlSnapshotCanonicalizesNegativeZeroWithoutChangingTheCaller()
        {
            DataChannelParametersDataType original = DataChannelControlTestContext.Parameters();
            original.MaxRate = BitConverter.Int64BitsToDouble(long.MinValue);

            DataChannelParametersDataType snapshot = DataChannelControlModel.Copy(original);

            Assert.Multiple(() =>
            {
                Assert.That(BitConverter.DoubleToInt64Bits(original.MaxRate), Is.EqualTo(long.MinValue));
                Assert.That(BitConverter.DoubleToInt64Bits(snapshot.MaxRate), Is.Zero);
                Assert.That(DataChannelControlModel.SameParameters(original, snapshot), Is.True);
            });
        }

        [Test]
        [Repeat(20)]
        public async Task ModifyStagesPendingAuthorizationAndCompletesOnlyThroughDataTransaction()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            (DataChannel opener, DataChannel acceptor) = await OpenSessionFlowAsync(test).ConfigureAwait(false);
            DataChannelParametersDataType proposed = DataChannelControlTestContext.Parameters();
            proposed.MaxMessageSize = 512;
            proposed.Priority = 3;
            uint before = opener.Revision;
            DataChannelStatusDataType staged = await test.Provider.ModifyAsync(
                test.Session.Object, opener.ChannelId, 9, before + 1, proposed).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(staged.Revision, Is.EqualTo(before));
                Assert.That(staged.PendingRevision, Is.EqualTo(before + 1));
                Assert.That(staged.OperationId, Is.EqualTo(9));
                Assert.That(staged.Result, Is.EqualTo(DataChannelResult.WouldBlock));
                Assert.That(staged.Parameters.MaxMessageSize, Is.EqualTo(1024));
                Assert.That(acceptor.Parameters.MaxMessageSize, Is.EqualTo(1024));
            });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            DataChannelResult updated = await opener.UpdateAsync(proposed, 9, deadline.Token).ConfigureAwait(false);
            // Local completion can precede the other peer consuming the queued acknowledgement.
            while (acceptor.GetOperationStatus(9).PendingRevision != 0)
            {
                await Task.Delay(1, deadline.Token).ConfigureAwait(false);
            }
            DataChannelStatusDataType repeated = await test.Provider.ModifyAsync(
                test.Session.Object, opener.ChannelId, 9, before + 1, proposed).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(updated, Is.EqualTo(DataChannelResult.Good));
                Assert.That(repeated.Revision, Is.EqualTo(before + 1));
                Assert.That(repeated.PendingRevision, Is.Zero);
                Assert.That(repeated.Result, Is.EqualTo(DataChannelResult.Good));
                Assert.That(repeated.Parameters.MaxMessageSize, Is.EqualTo(512));
                Assert.That(acceptor.Revision, Is.EqualTo(before + 1));
            });
            proposed.Priority++;
            ServiceResultException reused = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.Provider.ModifyAsync(
                    test.Session.Object, opener.ChannelId, 9, before + 1, proposed).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
            Assert.That(reused.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task ModifyRefusesUnstagedAndImmutableChangesAndExpiresUnpreparedProposal()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            (DataChannel opener, _) = await OpenSessionFlowAsync(test).ConfigureAwait(false);
            DataChannelParametersDataType changed = DataChannelControlTestContext.Parameters();
            changed.Protocol = DataChannelProfiles.JsonPayload;
            ServiceResultException immutable = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.Provider.ModifyAsync(
                    test.Session.Object, opener.ChannelId, 1, opener.Revision + 1, changed).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
            Assert.That(immutable.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            changed = DataChannelControlTestContext.Parameters();
            changed.MaxMessageSize = 512;
            uint revision = opener.Revision;
            await test.Provider.ModifyAsync(test.Session.Object, opener.ChannelId, 2, revision + 1, changed)
                .ConfigureAwait(false);
            test.Clock.Advance(test.Options.UpdateTimeout);
            DataChannelStatusDataType expired = await test.Provider.ModifyAsync(
                test.Session.Object, opener.ChannelId, 2, revision + 1, changed).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(expired.Revision, Is.EqualTo(revision));
                Assert.That(expired.PendingRevision, Is.Zero);
                Assert.That(expired.Result, Is.EqualTo(DataChannelResult.Timeout));
                Assert.That(opener.Parameters.MaxMessageSize, Is.EqualTo(1024));
            });
        }

        [Test]
        public async Task CloseOfAnotherSessionsChannelIsIndistinguishableFromUnknownChannel()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            (DataChannel opener, _) = await OpenSessionFlowAsync(test).ConfigureAwait(false);
            var other = new Mock<ISession>();
            other.SetupGet(value => value.Id).Returns(new NodeId(200u, 1));
            other.SetupGet(value => value.Activated).Returns(true);
            test.Sessions.Setup(value => value.GetSessions()).Returns([test.Session.Object, other.Object]);
            ServiceResultException inaccessible = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.Provider.CloseAsync(other.Object, opener.ChannelId, true).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
            ServiceResultException unknown = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.Provider.CloseAsync(other.Object, 999, true).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(inaccessible.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(unknown.StatusCode, Is.EqualTo(inaccessible.StatusCode));
                Assert.That(opener.State, Is.EqualTo(DataChannelState.Open));
            });
        }

        [Test]
        public async Task SameSessionCannotControlAmbiguousChannelIdInAnotherLiveContext()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            uint id = test.Pair.Client.ReserveChannelId();
            await test.GrantAsync(id).ConfigureAwait(false);
            await using DataChannelControlTestPair other = await DataChannelControlTestPair.CreateAsync(
                test.Provider, test.Options, test.Clock, test.Telemetry, "urn:control-tests:server",
                "urn:control-tests:client", test.PeerKey, test.PeerKey).ConfigureAwait(false);
            uint duplicate = other.Client.ReserveChannelId();
            Assert.That(duplicate, Is.EqualTo(id));
            DataChannelBindingDataType binding = test.Binding();
            binding.ContextId = other.Server.Identity.ContextId;
            binding.Generation = other.Server.Identity.Generation;
            binding.Challenge = other.Server.Identity.LocalChallenge;
            ServiceResultException conflict = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.Provider.OpenAsync(
                    test.Session.Object, duplicate, test.SourceNodeId, binding,
                    DataChannelControlTestContext.Parameters()).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(conflict.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("close")]
        [TestCase("policy")]
        [TestCase("expiry")]
        [TestCase("identity")]
        [TestCase("role")]
        public async Task SessionRevocationPreservesConfiguredNeighbour(string cause)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            if (cause == "role")
            {
                test.Metadata!.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Observer,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                    }
                ];
                test.EffectiveIdentity = new RoleBasedIdentity(test.Identity, [Role.Observer], test.NamespaceUris);
            }
            (DataChannel _, DataChannel dependent) = await OpenSessionFlowAsync(test).ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            DataChannel configured = await test.Pair.Client.OpenAsync(
                DataChannelControlTestContext.Parameters(), cancellationToken: deadline.Token).ConfigureAwait(false);
            DataChannel configuredAcceptor = (await test.Provider.AcceptAsync(
                DataChannelControlTestContext.ResourceUri, DataChannelProfiles.UadpPayload,
                DataChannelCarrierKind.SharedUasc, deadline.Token).ConfigureAwait(false))!;
            if (cause == "close")
            {
                test.Closing = true;
                test.Provider.RevokeSession(test.Session.Object.Id);
            }
            else if (cause == "policy")
            {
                test.SourcePolicy.AllowSession = false;
            }
            else if (cause == "expiry")
            {
                test.Expired = true;
            }
            else if (cause == "role")
            {
                test.EffectiveIdentity = new RoleBasedIdentity(test.Identity, [Role.Anonymous], test.NamespaceUris);
            }
            else
            {
                test.Identity = new UserIdentity("changed-user", []);
                test.Provider.SessionActivated(test.Session.Object);
            }
            await test.Provider.RecheckAuthorizationAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(dependent.State, Is.EqualTo(DataChannelState.Faulted));
                Assert.That(configured.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(configuredAcceptor.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(test.Pair.Server.IsReady, Is.True);
            });
        }

        [Test]
        public async Task DataContextLossInvalidatesUnredeemedGrantWithoutSessionClosure()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelGrantDataType grant = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            DataChannelAuthorizationRequest redemption = test.Redemption(grant);
            await test.Pair.Server.InvalidateAsync().ConfigureAwait(false);
            DataChannelAuthorization result = await test.Provider.AuthorizeAsync(redemption).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(test.Session.Object.Activated, Is.True);
                Assert.That(test.Session.Object.IsClosing, Is.False);
            });
        }

        [Test]
        public async Task CloseRetiresOnlyItsProvisionalGrant()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelGrantDataType first = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            DataChannelGrantDataType second = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            DataChannelStatusDataType closed = await test.Provider.CloseAsync(
                test.Session.Object, first.ChannelId, true).ConfigureAwait(false);
            DataChannelAuthorization denied = await test.Provider.AuthorizeAsync(test.Redemption(first))
                .ConfigureAwait(false);
            DataChannelAuthorization allowed = await test.Provider.AuthorizeAsync(test.Redemption(second))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(closed.State, Is.EqualTo(DataChannelState.Closed));
                Assert.That(denied.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(allowed.Result, Is.EqualTo(DataChannelResult.Good));
            });
        }

        [Test]
        public async Task ConfiguredFlowNeedsNeitherModelNorSessionAndSurvivesRecheckIntervals()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            test.Provider.DetachServer();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            DataChannel configured = await test.Pair.Client.OpenAsync(
                DataChannelControlTestContext.Parameters(), cancellationToken: deadline.Token).ConfigureAwait(false);
            DataChannel accepted = (await test.Provider.AcceptAsync(
                DataChannelControlTestContext.ResourceUri, DataChannelProfiles.UadpPayload,
                DataChannelCarrierKind.SharedUasc, deadline.Token).ConfigureAwait(false))!;
            test.Closing = true;
            test.Expired = true;
            test.Clock.Advance(TimeSpan.FromMinutes(2));
            await test.Provider.RecheckAuthorizationAsync(deadline.Token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(configured.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(accepted.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(accepted.GetStatus().SessionId.IsNull, Is.True);
            });
            ByteString payload = DataChannelControlTestContext.UadpPayload(ByteString.From([11, 12]));
            Assert.That(await configured.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await accepted.ReadAsync(deadline.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));
        }

        [Test]
        public async Task ApplicationContextAcquisitionWaitsForReadinessAndRefusesAmbiguity()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelContext found = await test.Provider.WaitForContextAsync(
                "urn:control-tests:client", DataChannelCarrierKind.SharedUasc).ConfigureAwait(false);
            Assert.That(found, Is.SameAs(test.Pair.Server));
            await using DataChannelControlTestPair duplicate = await DataChannelControlTestPair.CreateAsync(
                test.Provider, test.Options, test.Clock, test.Telemetry, "urn:control-tests:server",
                "urn:control-tests:client", test.PeerKey, test.PeerKey).ConfigureAwait(false);
            DataChannelException ambiguous = (await Assert.ThrowsAsync<DataChannelException>(async () =>
                await test.Provider.WaitForContextAsync(
                    "urn:control-tests:client", DataChannelCarrierKind.SharedUasc).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
            Assert.That(ambiguous.Result, Is.EqualTo(DataChannelResult.InvalidState));
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await test.Provider.WaitForContextAsync(
                    "urn:other:client", DataChannelCarrierKind.SharedUasc, cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task EvictedControlOperationCannotBeReplayedBelowRetainedHighWater()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            test.SessionTimeout = 1_000_000;
            (DataChannel opener, _) = await OpenSessionFlowAsync(test).ConfigureAwait(false);
            DataChannelParametersDataType proposed = DataChannelControlTestContext.Parameters();
            proposed.MaxMessageSize = 512;
            uint revision = opener.Revision;
            for (uint operation = 1; operation <= 65; operation++)
            {
                DataChannelStatusDataType staged = await test.Provider.ModifyAsync(
                    test.Session.Object, opener.ChannelId, operation, revision + 1, proposed).ConfigureAwait(false);
                Assert.That(staged.OperationId, Is.EqualTo(operation));
                Assert.That(staged.Revision, Is.EqualTo(revision));
                test.Clock.Advance(test.Options.UpdateTimeout);
            }
            ServiceResultException evicted = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.Provider.ModifyAsync(
                    test.Session.Object, opener.ChannelId, 1, revision + 1, proposed).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
            DataChannelStatusDataType recent = await test.Provider.ModifyAsync(
                test.Session.Object, opener.ChannelId, 65, revision + 1, proposed).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(evicted.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(recent.OperationId, Is.EqualTo(65));
                Assert.That(recent.Result, Is.EqualTo(DataChannelResult.Timeout));
                Assert.That(opener.Revision, Is.EqualTo(revision));
            });
        }

        internal static async Task<(DataChannel Opener, DataChannel Acceptor)> OpenSessionFlowAsync(
            DataChannelControlTestContext test)
        {
            uint id = test.Pair.Client.ReserveChannelId();
            DataChannelGrantDataType grant = await test.GrantAsync(id).ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            DataChannel opener = await test.Pair.Client.OpenAsync(
                DataChannelControlTestContext.Parameters(), DataChannelAuthorizationMode.Session,
                grant.GrantId, id, deadline.Token).ConfigureAwait(false);
            DataChannel acceptor = (await test.Provider.AcceptAsync(
                DataChannelControlTestContext.ResourceUri, DataChannelProfiles.UadpPayload,
                DataChannelCarrierKind.SharedUasc, deadline.Token).ConfigureAwait(false))!;
            return (opener, acceptor);
        }
    }
}
