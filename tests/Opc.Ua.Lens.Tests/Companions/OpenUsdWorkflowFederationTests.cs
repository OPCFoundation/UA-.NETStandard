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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using UaLens.Connection;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.CellProviderTestSession;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdWorkflowFederationTests
    {
        [Test]
        public async Task PrimarySessionReturnedByPeerFactoryIsNeverDisposed()
        {
            var primary = new OpenUsdWorkflowTestContext();
            Mock<IConnectionSession> owner = Owner(primary);
            AddComponent(primary, primary);
            var federation = new OpenUsdWorkflowFederation([Peer("peer", primary, owner)]);

            await Assert.ThatAsync(() => federation.PrepareAsync(
                primary.Context, primary.Representation, primary.CreateReader, CancellationToken.None),
                Throws.InvalidOperationException).ConfigureAwait(false);

            owner.Verify(value => value.DisposeAsync(), Times.Never);
            Assert.That(primary.Discoveries, Is.Zero);
            primary.Server.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task ConfiguredPeerReadsAreBoundedAndOwnedWithoutPrimaryCredentialsOrAssets()
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("peer");
            Mock<IConnectionSession> owner = Owner(remote);
            AddComponent(primary, remote);
            var federation = new OpenUsdWorkflowFederation([Peer("peer", remote, owner)]);
            ArrayOf<OpenUsdWorkflowOrigin> origins = await federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), CancellationToken.None)
                .ConfigureAwait(false);
            OpenUsdWorkflowTaskInput task = TaskInput(primary, origins);

            CompanionOperationResult result = await federation.ExecuteAsync(
                primary.Context, task, primary.Representation, Readers(primary, remote),
                primary.Exporter.Object, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(origins, Has.Count.EqualTo(1));
            Assert.That(origins[0].RuleId, Is.EqualTo("peer"));
            Assert.That(origins[0].MetadataDigest.Length, Is.EqualTo(32));
            Assert.That(Field(result.Values, "Sample 1 origin").TryGetValue(out string? origin), Is.True);
            Assert.That(origin, Is.EqualTo("peer"));
            Assert.That(Field(result.Values, "Sample 1 converted").TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(primary.Exports, Is.EqualTo(1));
            Assert.That(primary.ExportedSamples[0].PrimPath, Is.EqualTo("/Peers/peer/World/Cell"));
            owner.Verify(value => value.DisposeAsync(), Times.Exactly(3));
            remote.Reader.Verify(reader => reader.ReadAssetAsync(
                It.IsAny<NodeId>(), It.IsAny<CancellationToken>()), Times.Never);
            primary.Server.VerifyNoMutationOrSessionOwnership();
            remote.Server.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("metadata")]
        [TestCase("identity")]
        [TestCase("session")]
        [TestCase("disconnected")]
        [TestCase("policy")]
        [TestCase("certificate")]
        public async Task PeerChangesDuringAcquisitionPreventExportAndReleaseItsOwner(string fault)
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("peer");
            Mock<IConnectionSession> owner = Owner(remote);
            AddComponent(primary, remote);
            var federation = new OpenUsdWorkflowFederation([Peer("peer", remote, owner)]);
            ArrayOf<OpenUsdWorkflowOrigin> origins = await federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), CancellationToken.None)
                .ConfigureAwait(false);
            OpenUsdWorkflowTaskInput task = TaskInput(primary, origins);
            remote.ReadValue = (_, _) =>
            {
                switch (fault)
                {
                    case "metadata":
                        remote.Binding.Scale = 3;
                        break;
                    case "identity":
                        remote.Identity = new UserIdentity();
                        break;
                    case "session":
                        remote.SessionId = new NodeId("replacement", remote.Server.NamespaceIndex);
                        break;
                    case "disconnected":
                        remote.Connected = false;
                        break;
                    case "policy":
                        remote.Endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                        break;
                    case "certificate":
                        remote.Endpoint.ServerCertificate = ByteString.From([4, 5, 6]);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(fault));
                }
                return Task.FromResult(OpenUsdWorkflowTestContext.Value(Variant.From(3d)));
            };

            await Assert.ThatAsync(() => federation.ExecuteAsync(
                primary.Context, task, primary.Representation, Readers(primary, remote),
                primary.Exporter.Object, null, CancellationToken.None),
                Throws.InvalidOperationException).ConfigureAwait(false);

            Assert.That(primary.Exports, Is.Zero);
            owner.Verify(value => value.DisposeAsync(), Times.Exactly(3));
        }

        [TestCase("endpoint")]
        [TestCase("application")]
        [TestCase("policy")]
        [TestCase("mode")]
        [TestCase("disconnected")]
        public async Task UnmatchedPeerIsDisposedWithoutReadingOrExporting(string fault)
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("peer");
            Mock<IConnectionSession> owner = Owner(remote);
            AddComponent(primary, remote);
            var federation = new OpenUsdWorkflowFederation([Peer("peer", remote, owner)]);
            switch (fault)
            {
                case "endpoint":
                    remote.Endpoint.EndpointUrl += "/wrong";
                    break;
                case "application":
                    remote.Endpoint.Server.ApplicationUri = "urn:wrong";
                    break;
                case "policy":
                    remote.Endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                    break;
                case "mode":
                    remote.Endpoint.SecurityMode = MessageSecurityMode.None;
                    break;
                case "disconnected":
                    remote.Connected = false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            await Assert.ThatAsync(() => federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), CancellationToken.None),
                Throws.InvalidOperationException).ConfigureAwait(false);

            Assert.That(remote.Discoveries, Is.Zero);
            Assert.That(remote.ReadNodes, Is.Empty);
            owner.Verify(value => value.DisposeAsync(), Times.Once);
        }

        [TestCase("cycle")]
        [TestCase("unconfigured")]
        [TestCase("duplicate")]
        public async Task UnconfiguredCyclicAndDuplicateOriginsCannotBePrepared(string fault)
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("peer");
            Mock<IConnectionSession> owner = Owner(remote);
            AddComponent(primary, remote);
            var federation = new OpenUsdWorkflowFederation([Peer("peer", remote, owner)]);
            switch (fault)
            {
                case "cycle":
                    AddComponent(remote, remote);
                    break;
                case "unconfigured":
                    primary.Representation.Components[0].ComponentServerUri = "urn:unconfigured";
                    break;
                case "duplicate":
                    AddComponent(primary, remote);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            await Assert.ThatAsync(() => federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), CancellationToken.None),
                Throws.InvalidOperationException).ConfigureAwait(false);

            owner.Verify(value => value.DisposeAsync(), fault == "unconfigured" ? Times.Never() : Times.Once());
            Assert.That(remote.ReadNodes, Is.Empty);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public async Task NonfinitePeerValuesNeverReachTheExporter(double invalid)
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("peer")
            {
                ReadValue = (_, _) => Task.FromResult(OpenUsdWorkflowTestContext.Value(Variant.From(invalid)))
            };
            Mock<IConnectionSession> owner = Owner(remote);
            AddComponent(primary, remote);
            var federation = new OpenUsdWorkflowFederation([Peer("peer", remote, owner)]);
            ArrayOf<OpenUsdWorkflowOrigin> origins = await federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.ThatAsync(() => federation.ExecuteAsync(
                primary.Context, TaskInput(primary, origins), primary.Representation, Readers(primary, remote),
                primary.Exporter.Object, null, CancellationToken.None),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadTypeMismatch))
                .ConfigureAwait(false);

            Assert.That(primary.Exports, Is.Zero);
            owner.Verify(value => value.DisposeAsync(), Times.Exactly(3));
        }

        [TestCase(4)]
        [TestCase(5)]
        public async Task PeerDepthIsBoundedWithoutRejectingTheFourthLeaf(int depth)
        {
            var primary = new OpenUsdWorkflowTestContext();
            OpenUsdWorkflowTestContext[] peers = [.. Enumerable.Range(1, depth)
                .Select(index => new OpenUsdWorkflowTestContext("peer" + index))];
            Mock<IConnectionSession>[] owners = [.. peers.Select(Owner)];
            var rules = new List<OpenUsdWorkflowPeer>();
            OpenUsdWorkflowTestContext parent = primary;
            for (int index = 0; index < peers.Length; index++)
            {
                AddComponent(parent, peers[index]);
                rules.Add(Peer("peer" + (index + 1), peers[index], owners[index]));
                parent = peers[index];
            }
            var federation = new OpenUsdWorkflowFederation(rules.ToArray());
            IOpenUsdCompanionReader Reader(CompanionContext context, OpenUsdConnectorOptions options)
            {
                return peers.Single(peer => ReferenceEquals(peer.Context.Session, context.Session))
                    .CreateReader(context, options);
            }

            if (depth == 4)
            {
                ArrayOf<OpenUsdWorkflowOrigin> origins = await federation.PrepareAsync(
                    primary.Context, primary.Representation, Reader, CancellationToken.None).ConfigureAwait(false);
                Assert.That(origins, Has.Count.EqualTo(4));
                Assert.That(origins[3].TargetPrimPath, Is.EqualTo("/Peers/peer1/peer2/peer3/peer4"));
            }
            else
            {
                await Assert.ThatAsync(() => federation.PrepareAsync(
                    primary.Context, primary.Representation, Reader, CancellationToken.None),
                    Throws.InvalidOperationException.With.Message.Contains("depth")).ConfigureAwait(false);
            }
            for (int index = 0; index < owners.Length; index++)
            {
                owners[index].Verify(owner => owner.DisposeAsync(), index < 4 ? Times.Once() : Times.Never());
            }
        }

        [TestCase(0)]
        [TestCase(17)]
        public void InvalidPeerCountsCannotBeConfigured(int count)
        {
            var remote = new OpenUsdWorkflowTestContext("peer");
            Mock<IConnectionSession> owner = Owner(remote);
            OpenUsdWorkflowPeer[] rules =
                [.. Enumerable.Range(0, count).Select(index => Peer("p" + index, remote, owner))];

            Assert.That(() => new OpenUsdWorkflowFederation(rules), Throws.ArgumentException);

            owner.Verify(value => value.DisposeAsync(), Times.Never);
        }

        [Test]
        public async Task ExactlySixteenIndependentOriginsCanBePreparedAndAreAllReleased()
        {
            var primary = new OpenUsdWorkflowTestContext();
            OpenUsdWorkflowTestContext[] peers = [.. Enumerable.Range(0, 16)
                .Select(index => new OpenUsdWorkflowTestContext("peer" + index))];
            Mock<IConnectionSession>[] owners = [.. peers.Select(Owner)];
            var rules = new List<OpenUsdWorkflowPeer>();
            for (int index = 0; index < peers.Length; index++)
            {
                AddComponent(primary, peers[index]);
                rules.Add(Peer("peer" + index, peers[index], owners[index]));
            }
            var federation = new OpenUsdWorkflowFederation(rules.ToArray());

            ArrayOf<OpenUsdWorkflowOrigin> origins = await federation.PrepareAsync(
                primary.Context, primary.Representation,
                (context, options) => peers.Single(peer => ReferenceEquals(peer.Context.Session, context.Session))
                    .CreateReader(context, options), CancellationToken.None).ConfigureAwait(false);

            Assert.That(origins, Has.Count.EqualTo(16));
            for (int index = 0; index < owners.Length; index++)
            {
                Assert.That(origins[index].RuleId, Is.EqualTo("peer" + index));
                owners[index].Verify(owner => owner.DisposeAsync(), Times.Once);
            }
        }

        [Test]
        public async Task AmbiguousOriginRulesCannotChooseAnIdentityByRegistrationOrder()
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("peer");
            Mock<IConnectionSession> owner = Owner(remote);
            AddComponent(primary, remote);
            var federation = new OpenUsdWorkflowFederation(
                [Peer("first", remote, owner), Peer("second", remote, owner)]);

            await Assert.ThatAsync(() => federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains("Ambiguous")).ConfigureAwait(false);

            owner.Verify(value => value.DisposeAsync(), Times.Never);
            Assert.That(remote.Discoveries, Is.Zero);
        }

        [TestCase("denied")]
        [TestCase("representation")]
        public async Task PeerIdentityAndRepresentationMustMatchTheExplicitRule(string fault)
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("peer");
            Mock<IConnectionSession> owner = Owner(remote);
            AddComponent(primary, remote);
            OpenUsdWorkflowPeer rule = Peer("peer", remote, owner);
            if (fault == "denied")
            {
                rule = rule with { AcceptsIdentity = static _ => false };
            }
            else
            {
                primary.Representation.Components[0].ComponentRepresentation =
                    new NodeId("different-representation", remote.Server.NamespaceIndex);
            }
            var federation = new OpenUsdWorkflowFederation([rule]);

            await Assert.ThatAsync(() => federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), CancellationToken.None),
                Throws.InvalidOperationException).ConfigureAwait(false);

            owner.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(remote.Discoveries, Is.Zero);
        }

        [Test]
        public async Task AnAncestorSessionReturnedByAnotherRuleIsNotDisposedTwice()
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("first");
            var second = new OpenUsdWorkflowTestContext("second");
            Mock<IConnectionSession> owner = Owner(remote);
            Mock<IConnectionSession> unused = Owner(second);
            AddComponent(primary, remote);
            AddComponent(remote, second);
            OpenUsdWorkflowPeer invalid = Peer("second", second, unused) with
            {
                OpenSession = _ => Task.FromResult(owner.Object)
            };
            var federation = new OpenUsdWorkflowFederation([Peer("first", remote, owner), invalid]);

            await Assert.ThatAsync(() => federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains("borrowed")).ConfigureAwait(false);

            owner.Verify(value => value.DisposeAsync(), Times.Once);
            unused.Verify(value => value.DisposeAsync(), Times.Never);
        }

        [Test]
        public async Task LateSessionAcquisitionAfterCancellationStillReleasesItsOwnedLease()
        {
            var primary = new OpenUsdWorkflowTestContext();
            var remote = new OpenUsdWorkflowTestContext("peer");
            Mock<IConnectionSession> owner = Owner(remote);
            AddComponent(primary, remote);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<IConnectionSession>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var federation = new OpenUsdWorkflowFederation([Peer("peer", remote, owner) with
            {
                OpenSession = _ =>
                {
                    entered.SetResult();
                    return release.Task;
                }
            }]);
            Task<ArrayOf<OpenUsdWorkflowOrigin>> preparation = federation.PrepareAsync(
                primary.Context, primary.Representation, Readers(primary, remote), cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            release.SetResult(owner.Object);

            await Assert.ThatAsync(() => preparation.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            owner.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(remote.Discoveries, Is.Zero);
        }

        private static OpenUsdWorkflowTaskInput TaskInput(
            OpenUsdWorkflowTestContext primary, ArrayOf<OpenUsdWorkflowOrigin> origins)
        {
            return new OpenUsdWorkflowTaskInput(
                primary.Context, primary.Target, "export-composition",
                OpenUsdWorkflowMetadata.Digest(primary.Context, primary.Representation), 16, TimeSpan.Zero,
                default, default, OpenUsdTestPaths.NewDestination(), "Configured read-only peers", origins);
        }

        private static Mock<IConnectionSession> Owner(OpenUsdWorkflowTestContext fixture)
        {
            var owner = new Mock<IConnectionSession>(MockBehavior.Strict);
            owner.SetupGet(value => value.Session).Returns(fixture.Server.Session.Object);
            owner.SetupGet(value => value.State).Returns(new ConnectionSessionState(ConnectionPhase.Connected));
            owner.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
            return owner;
        }

        private static OpenUsdWorkflowPeer Peer(
            string id, OpenUsdWorkflowTestContext fixture, Mock<IConnectionSession> owner)
        {
            IUserIdentity identity = fixture.Identity;
            return new OpenUsdWorkflowPeer(id, fixture.Endpoint.EndpointUrl!, fixture.Endpoint.Server.ApplicationUri!,
                NodeId.ToExpandedNodeId(fixture.Target.NodeId, fixture.Server.NamespaceUris),
                fixture.Endpoint.SecurityPolicyUri!, candidate => ReferenceEquals(candidate, identity),
                _ => Task.FromResult(owner.Object));
        }

        private static Func<CompanionContext, OpenUsdConnectorOptions, IOpenUsdCompanionReader> Readers(
            OpenUsdWorkflowTestContext primary, OpenUsdWorkflowTestContext remote)
        {
            return (context, options) => ReferenceEquals(context.Session, primary.Server.Session.Object)
                ? primary.CreateReader(context, options) : remote.CreateReader(context, options);
        }

        private static void AddComponent(OpenUsdWorkflowTestContext parent, OpenUsdWorkflowTestContext target)
        {
            parent.Representation.Components.Add(new OpenUsdConnector.ComponentInfo
            {
                NodeId = new NodeId(
                    "component-" + parent.Representation.Components.Count, parent.Server.NamespaceIndex),
                ComponentEndpointUrl = target.Endpoint.EndpointUrl,
                ComponentServerUri = target.Endpoint.Server.ApplicationUri,
                ComponentRepresentation = target.Target.NodeId,
                TargetPrimPath = "/Peers",
                Enabled = true
            });
        }
    }
}
