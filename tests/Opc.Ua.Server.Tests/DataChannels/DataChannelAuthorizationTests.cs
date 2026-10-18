/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Server.Tests.DataChannels
{
    /// <summary>
    /// Grants validate live application, context, source, identity and direction policy before single redemption.
    /// </summary>
    [TestFixture]
    [Category("DataChannelsControl")]
    public sealed class DataChannelAuthorizationTests
    {
        [TestCase("context")]
        [TestCase("generation")]
        [TestCase("challenge")]
        [TestCase("application")]
        [TestCase("key")]
        public async Task OpenRejectsBindingMismatchWithoutConsumingTuple(string mismatch)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            uint id = test.Pair.Client.ReserveChannelId();
            DataChannelBindingDataType binding = test.Binding();
            switch (mismatch)
            {
                case "context":
                    binding.ContextId = Guid.NewGuid();
                    break;
                case "generation":
                    binding.Generation++;
                    break;
                case "challenge":
                    binding.Challenge = ByteString.From(new byte[32]);
                    break;
                case "application":
                    binding.PeerApplicationUri = "urn:another:application";
                    break;
                case "key":
                    binding.PeerKeyThumbprint = ByteString.From(new byte[32]);
                    break;
            }
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.Provider.OpenAsync(
                    test.Session.Object, id, test.SourceNodeId, binding, DataChannelControlTestContext.Parameters())
                    .ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            DataChannelGrantDataType grant = await test.GrantAsync(id).ConfigureAwait(false);
            Assert.That(grant.ChannelId, Is.EqualTo(id));
        }

        [Test]
        public async Task GrantIsSingleUseAndTupleCannotBeIssuedAgain()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelGrantDataType grant = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            DataChannelAuthorization first = await test.Provider.AuthorizeAsync(test.Redemption(grant))
                .ConfigureAwait(false);
            DataChannelAuthorization replay = await test.Provider.AuthorizeAsync(test.Redemption(grant))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Result, Is.EqualTo(DataChannelResult.Good));
                Assert.That(first.SessionId, Is.EqualTo(test.Session.Object.Id));
                Assert.That(first.SourceNodeId, Is.EqualTo(test.Source.SourceNodeId));
                Assert.That(replay.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            });
            ServiceResultException reissue = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.GrantAsync(grant.ChannelId).ConfigureAwait(false))!;
            Assert.That(reissue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("local-key")]
        [TestCase("peer-key")]
        [TestCase("peer-challenge")]
        [TestCase("local-challenge")]
        [TestCase("parameters")]
        public async Task WrongRedemptionDoesNotConsumeValidGrant(string mismatch)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelGrantDataType grant = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            DataChannelContextIdentity identity = test.Pair.Server.Identity;
            DataChannelPeerIdentity peer = identity.Peer;
            var changedPeer = new DataChannelPeerIdentity(
                peer.LocalApplicationUri, peer.PeerApplicationUri,
                mismatch == "local-key" ? ByteString.From(new byte[32]) : peer.LocalKeyThumbprint,
                mismatch == "peer-key" ? ByteString.From(new byte[32]) : peer.PeerKeyThumbprint,
                peer.Protection, peer.IsTransportInitiator);
            var changedIdentity = new DataChannelContextIdentity(
                identity.ContextId, identity.Generation, changedPeer,
                mismatch == "local-challenge" ? ByteString.From(new byte[32]) : identity.LocalChallenge,
                mismatch == "peer-challenge" ? ByteString.From(new byte[32]) : identity.PeerChallenge,
                identity.Carrier);
            DataChannelParametersDataType parameters = DataChannelControlTestContext.Parameters();
            if (mismatch == "parameters")
            {
                parameters.Priority++;
            }
            DataChannelAuthorization rejected = await test.Provider.AuthorizeAsync(
                test.Redemption(grant, changedIdentity, parameters)).ConfigureAwait(false);
            DataChannelAuthorization valid = await test.Provider.AuthorizeAsync(test.Redemption(grant))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(valid.Result, Is.EqualTo(DataChannelResult.Good));
            });
        }

        [TestCase(-1, DataChannelResult.Good)]
        [TestCase(0, DataChannelResult.NotAuthorized)]
        [TestCase(1, DataChannelResult.NotAuthorized)]
        public async Task GrantExpiresAtIssuerMonotonicDeadline(long offsetTicks, DataChannelResult expected)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelBindingDataType binding = test.Binding();
            DateTime expiry = test.Clock.GetUtcNow().UtcDateTime.AddSeconds(3);
            binding.ExpiresAt = expiry;
            DataChannelGrantDataType grant = await test.Provider.OpenAsync(
                test.Session.Object, test.Pair.Client.ReserveChannelId(), test.SourceNodeId,
                binding, DataChannelControlTestContext.Parameters()).ConfigureAwait(false);
            Assert.That(grant.ExpiresAt.ToDateTime(), Is.EqualTo(expiry));
            test.Clock.Advance(TimeSpan.FromTicks(TimeSpan.FromSeconds(3).Ticks + offsetTicks));
            DataChannelAuthorization result = await test.Provider.AuthorizeAsync(test.Redemption(grant))
                .ConfigureAwait(false);
            Assert.That(result.Result, Is.EqualTo(expected));
        }

        [TestCase(120000, 10)]
        [TestCase(2000, 2)]
        public async Task GrantClampsUnboundedRequestedExpiryToIssuerAndSessionLimits(double timeout, int seconds)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            test.SessionTimeout = timeout;
            DataChannelGrantDataType grant = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            Assert.That(grant.ExpiresAt.ToDateTime(),
                Is.EqualTo(test.Clock.GetUtcNow().UtcDateTime.AddSeconds(seconds)));
        }

        [TestCase(DataChannelDirection.OpenerToAcceptor, false)]
        [TestCase(DataChannelDirection.AcceptorToOpener, true)]
        [TestCase(DataChannelDirection.Duplex, false)]
        public async Task ReadPermissionNeverGrantsInboundOrDuplexContent(
            DataChannelDirection direction, bool permitted)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            test.Metadata!.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                }
            ];
            uint id = test.Pair.Client.ReserveChannelId();
            if (permitted)
            {
                DataChannelGrantDataType grant = await test.Provider.OpenAsync(
                    test.Session.Object, id, test.SourceNodeId, test.Binding(),
                    DataChannelControlTestContext.Parameters(direction)).ConfigureAwait(false);
                Assert.That(grant.Parameters.Direction, Is.EqualTo(direction));
            }
            else
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await test.Provider.OpenAsync(
                        test.Session.Object, id, test.SourceNodeId, test.Binding(),
                        DataChannelControlTestContext.Parameters(direction)).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingOwnerOrMetadataFailsClosed(bool missingMetadata)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            if (missingMetadata)
            {
                test.Metadata = null;
            }
            else
            {
                test.HasSource = false;
            }
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.GrantAsync(test.Pair.Client.ReserveChannelId()).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task ChangedIdentityAndRevokedSourcePolicyInvalidateUnredeemedGrants()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelGrantDataType grant = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            test.SourcePolicy.AllowSession = false;
            DataChannelAuthorization deniedPolicy = await test.Provider.AuthorizeAsync(test.Redemption(grant))
                .ConfigureAwait(false);
            test.SourcePolicy.AllowSession = true;
            test.Identity = new UserIdentity("different-user", []);
            DataChannelAuthorization changedIdentity = await test.Provider.AuthorizeAsync(test.Redemption(grant))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(deniedPolicy.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(changedIdentity.Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            });
        }

        [Test]
        public async Task ChangedControlChannelAloneDoesNotInvalidateGrant()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelGrantDataType grant = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            test.ControlChannelId = "replacement-control-channel";
            test.Provider.SessionActivated(test.Session.Object);
            DataChannelAuthorization result = await test.Provider.AuthorizeAsync(test.Redemption(grant))
                .ConfigureAwait(false);
            Assert.That(result.Result, Is.EqualTo(DataChannelResult.Good));
        }

        [Test]
        public async Task RevocationTokenInvalidatesAuthorizationBeforeReadinessCommit()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            DataChannelGrantDataType grant = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            DataChannelAuthorization authorized = await test.Provider.AuthorizeAsync(test.Redemption(grant))
                .ConfigureAwait(false);
            Assert.That(authorized.RevocationToken.CanBeCanceled, Is.True);
            Assert.That(authorized.RevocationToken.IsCancellationRequested, Is.False);
            test.Provider.RevokeSession(test.Session.Object.Id);
            Assert.That(authorized.RevocationToken.IsCancellationRequested, Is.True);
            Assert.That(test.Pair.Server.IsReady, Is.True, "Revoking a Session must not close its data context.");
        }

        [Test]
        public async Task AVariableCannotActAsASourceObject()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            test.Metadata!.NodeClass = NodeClass.Variable;
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.GrantAsync(test.Pair.Client.ReserveChannelId()).ConfigureAwait(false))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCase("application")]
        [TestCase("key")]
        [TestCase("unvalidated")]
        public async Task GrantRequiresTheSessionsValidatedApplicationAndKey(string mismatch)
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            switch (mismatch)
            {
                case "application":
                    test.Session.SetupGet(session => session.ClientApplicationUri).Returns("urn:other:application");
                    break;
                case "key":
                    test.Session.SetupGet(session => session.ClientCertificate).Returns(test.ServerCertificate);
                    break;
                case "unvalidated":
                    ClientCertificateProvenance.SetValidated(test.Session.Object, false);
                    break;
            }
            ServiceResultException mismatchError = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.GrantAsync(test.Pair.Client.ReserveChannelId()).ConfigureAwait(false))!;
            Assert.That(mismatchError.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task AThirdPartyControlServerCannotIssueForAnotherApplicationsContext()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            await using DataChannelControlTestPair otherApplication = await DataChannelControlTestPair.CreateAsync(
                test.Provider, test.Options, test.Clock, test.Telemetry, "urn:other:server",
                "urn:control-tests:client", test.PeerKey, test.PeerKey).ConfigureAwait(false);
            DataChannelBindingDataType binding = test.Binding();
            binding.ContextId = otherApplication.Server.Identity.ContextId;
            binding.Generation = otherApplication.Server.Identity.Generation;
            binding.Challenge = otherApplication.Server.Identity.LocalChallenge;
            ServiceResultException thirdParty = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.Provider.OpenAsync(test.Session.Object, otherApplication.Client.ReserveChannelId(),
                    test.SourceNodeId, binding, DataChannelControlTestContext.Parameters()).ConfigureAwait(false))!;
            Assert.That(thirdParty.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task SourceQuotaIncludesPendingGrantsAndReleasesThemAtExpiry()
        {
            await using DataChannelControlTestContext test =
                await DataChannelControlTestContext.CreateAsync().ConfigureAwait(false);
            for (int i = 0; i < test.Source.MaxFlows; i++)
            {
                DataChannelGrantDataType grant = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                    .ConfigureAwait(false);
                Assert.That(grant.GrantId, Is.Not.EqualTo(Uuid.Empty));
            }
            ServiceResultException full = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await test.GrantAsync(test.Pair.Client.ReserveChannelId()).ConfigureAwait(false))!;
            Assert.That(full.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            test.Clock.Advance(test.Options.GrantLifetime);
            DataChannelGrantDataType afterExpiry = await test.GrantAsync(test.Pair.Client.ReserveChannelId())
                .ConfigureAwait(false);
            Assert.That(afterExpiry.ChannelId, Is.EqualTo(11));
        }
    }
}
