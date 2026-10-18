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

using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    [TestFixture]
    [NonParallelizable]
    public sealed class FlowGrantTests
    {
        [Test]
        public async Task SessionGrantIsRedeemedOnceAgainstActualContextBeforeReady()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using var store = new DataChannelGrantStore(pair.AcceptorBudget, pair.Clock);
            uint channel = pair.Initiator.ReserveChannelId();
            DataChannelParametersDataType parameters = FlowLoopback.Parameters();
            var session = new NodeId(71u);
            DataChannelGrantDataType grant = store.Issue(
                pair.Acceptor.Identity, channel, parameters, new ExpandedNodeId(51u), session);
            var grantId = (Guid)grant.GrantId;
            Assert.That(store.TryGetGrant(grantId, out DataChannelGrantDataType? before), Is.True);
            Assert.That(before!.SessionId, Is.EqualTo(session));
            pair.AcceptorPolicy.Handler = (request, _) => new ValueTask<DataChannelAuthorization>(
                request.Purpose == DataChannelAuthorizationPurpose.Open
                    ? store.Redeem(request)
                    : new DataChannelAuthorization(DataChannelResult.Good, request.Parameters, sessionId: session));
            DataChannel opened = await pair.Initiator.OpenAsync(
                parameters, DataChannelAuthorizationMode.Session, grantId, channel)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            DataChannel? accepted = await pair.Acceptor.AcceptAsync().ConfigureAwait(false);
            DataChannelAuthorizationRequest request = pair.AcceptorPolicy.Requests.First();
            Assert.Multiple(() =>
            {
                Assert.That(opened.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(accepted!.GetStatus().SessionId, Is.EqualTo(session));
                Assert.That(accepted.GetStatus().SourceNodeId, Is.EqualTo(new ExpandedNodeId(51u)));
                Assert.That(store.TryGetGrant(grantId, out _), Is.False);
                Assert.That(store.Redeem(request).Result, Is.EqualTo(DataChannelResult.NotAuthorized));
                Assert.That(pair.AcceptorBudget.Usage.Grants, Is.Zero);
                Assert.That(request.Context.Peer.PeerKeyThumbprint,
                    Is.EqualTo(pair.Initiator.Identity.Peer.LocalKeyThumbprint));
            });
            await pair.Acceptor.RecheckAuthorizationAsync().ConfigureAwait(false);
            Assert.That(accepted!.State, Is.EqualTo(DataChannelState.Open),
                "Continuing authorization must not redeem an already consumed grant again.");
        }

        [TestCase(10000)]
        [TestCase(250)]
        public async Task GrantLifetimeIsMonotonicAndExpiresAtExactShorterDeadline(int milliseconds)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using var store = new DataChannelGrantStore(pair.AcceptorBudget, pair.Clock);
            DataChannelGrantDataType grant = store.Issue(pair.Acceptor.Identity, 1,
                FlowLoopback.Parameters(), new ExpandedNodeId(1u), new NodeId(2u),
                TimeSpan.FromMilliseconds(milliseconds));
            var id = (Guid)grant.GrantId;
            pair.Clock.Advance(TimeSpan.FromMilliseconds(milliseconds - 1));
            Assert.That(store.TryGetGrant(id, out _), Is.True);
            pair.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(store.TryGetGrant(id, out _), Is.False);
            Assert.That(pair.AcceptorBudget.Usage.Grants, Is.Zero);
        }

        [Test]
        public async Task GrantBudgetAndRevocationAreScopedAndReleaseExactlyOnce()
        {
            var budget = new DataChannelApplicationBudget(new DataChannelApplicationLimits { MaxGrants = 2 });
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using var store = new DataChannelGrantStore(budget, pair.Clock);
            DataChannelGrantDataType first = store.Issue(pair.Acceptor.Identity, 1,
                FlowLoopback.Parameters(), new ExpandedNodeId(1u), new NodeId(2u));
            DataChannelGrantDataType other = store.Issue(pair.Acceptor.Identity, 3,
                FlowLoopback.Parameters(), new ExpandedNodeId(1u), new NodeId(3u));
            DataChannelException? failure = Assert.Throws<DataChannelException>(() =>
                store.Issue(pair.Acceptor.Identity, 5,
                    FlowLoopback.Parameters(), new ExpandedNodeId(1u), new NodeId(4u)));
            Assert.That(failure!.Result, Is.EqualTo(DataChannelResult.WouldBlock));
            Assert.That(budget.Usage.Grants, Is.EqualTo(2));
            Assert.That(store.RevokeGrant((Guid)first.GrantId), Is.True);
            Assert.That(store.RevokeGrant((Guid)first.GrantId), Is.False);
            Assert.That(store.TryGetGrant((Guid)other.GrantId, out _), Is.True);
            store.RevokeSession(new NodeId(2u));
            Assert.That(budget.Usage.Grants, Is.EqualTo(1));
            store.RevokeContext(pair.Acceptor.Identity.ContextId);
            Assert.That(budget.Usage.Grants, Is.Zero);
            Assert.That(store.TryGetGrant((Guid)other.GrantId, out _), Is.False);
        }

        [TestCase("context")]
        [TestCase("generation")]
        [TestCase("localApplication")]
        [TestCase("peerApplication")]
        [TestCase("localKey")]
        [TestCase("peerKey")]
        [TestCase("protection")]
        [TestCase("carrier")]
        [TestCase("localChallenge")]
        [TestCase("peerChallenge")]
        [TestCase("channel")]
        [TestCase("parameters")]
        public async Task GrantRejectsEveryChangedBindingWithoutConsumingTheOwnersGrant(string changed)
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using var store = new DataChannelGrantStore(pair.AcceptorBudget, pair.Clock);
            DataChannelContextIdentity context = pair.Acceptor.Identity;
            DataChannelPeerIdentity peer = context.Peer;
            ByteString different = new(Enumerable.Repeat((byte)0xAB, 32).ToArray());
            var changedPeer = new DataChannelPeerIdentity(
                changed == "localApplication" ? "urn:other:local" : peer.LocalApplicationUri,
                changed == "peerApplication" ? "urn:other:peer" : peer.PeerApplicationUri,
                changed == "localKey" ? different : peer.LocalKeyThumbprint,
                changed == "peerKey" ? different : peer.PeerKeyThumbprint,
                changed == "protection" ? DataChannelProtectionMode.Integrity : peer.Protection,
                peer.IsTransportInitiator);
            var changedContext = new DataChannelContextIdentity(
                changed == "context" ? Guid.NewGuid() : context.ContextId,
                changed == "generation" ? context.Generation + 1 : context.Generation,
                changedPeer,
                changed == "localChallenge" ? different : context.LocalChallenge,
                changed == "peerChallenge" ? different : context.PeerChallenge,
                changed == "carrier" ? DataChannelCarrierKind.DedicatedUasc : context.Carrier);
            DataChannelParametersDataType parameters = FlowLoopback.Parameters();
            DataChannelGrantDataType grant = store.Issue(
                context, 1, parameters, new ExpandedNodeId(1u), new NodeId(2u));
            if (changed == "parameters")
            {
                parameters.MaxMessageSize--;
            }
            var request = new DataChannelAuthorizationRequest(
                changedContext, changed == "channel" ? 3u : 1u,
                false, DataChannelAuthorizationMode.Session, (Guid)grant.GrantId,
                DataChannelAuthorizationPurpose.Open, 1, 0, parameters);
            Assert.That(store.Redeem(request).Result, Is.EqualTo(DataChannelResult.NotAuthorized));
            Assert.That(store.TryGetGrant((Guid)grant.GrantId, out _), Is.True);
            Assert.That(pair.AcceptorBudget.Usage.Grants, Is.EqualTo(1));
        }

        [Test]
        public async Task ConfiguredFlowDoesNotAcquireOrDependOnASessionGrant()
        {
            await using var pair = new FlowLoopback();
            await pair.StartAsync().ConfigureAwait(false);
            using var store = new DataChannelGrantStore(pair.AcceptorBudget, pair.Clock);
            FlowPair flow = await pair.OpenAsync().ConfigureAwait(false);
            store.RevokeSession(new NodeId(1u));
            store.RevokeContext(pair.Acceptor.Identity.ContextId);
            Assert.That(await flow.Local.SendAsync(ByteString.Empty).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage? received = await flow.Remote.ReadAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(received!.MessageId, Is.EqualTo(1));
                Assert.That(flow.Remote.GetStatus().SessionId.IsNull, Is.True);
                Assert.That(pair.AcceptorPolicy.Requests.All(
                    request => request.Mode == DataChannelAuthorizationMode.Configured && !request.GrantId.HasValue),
                    Is.True);
                Assert.That(pair.AcceptorBudget.Usage.Grants, Is.Zero);
            });
        }
    }
}
