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
 *
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ChangePreparerTests
    {
        /// <summary>
        /// Capturing empty metadata preserves the standard namespace and default encodeable types.
        /// </summary>
        [Test]
        public void ConstructorCapturesUnchangedAndEmptyMetadata()
        {
            Mock<IServerInternal> server = CreateServer();
            NamespaceTable namespaces = server.Object.NamespaceUris;
            StringTable serverUris = server.Object.ServerUris;
            var preparation = new ChangePreparer(server.Object);

            Assert.Multiple(() =>
            {
                Assert.That(preparation.NamespaceRevision, Is.EqualTo(namespaces.Version));
                Assert.That(preparation.NamespaceUris.ToArray(), Is.EqualTo(s_expectedDefaultNamespaces));
                Assert.That(preparation.NamespaceUris.GetIndex("http://opcfoundation.org/UA/"), Is.Zero);
                Assert.That(preparation.ServerRevision, Is.EqualTo(serverUris.Version));
                Assert.That(preparation.ServerUris.Count, Is.Zero);
                Assert.That(preparation.TypeRevision, Is.Zero);
                Assert.That(preparation.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(
                    preparation.Factory.TryGetEncodeableType(DataTypeIds.Range, out IEncodeableType? rangeType),
                    Is.True);
                Assert.That(rangeType, Is.Not.Null);
            });
        }

        /// <summary>
        /// Serving metadata changes remain outside the preparation snapshot until binding.
        /// </summary>
        [Test]
        public void ConstructorSnapshotsNamespaceServerTypeAndFactoryMutationsUntilBind()
        {
            Mock<IServerInternal> server = CreateServer();
            server.Object.NamespaceUris.Append(kBaseNamespaceUri);
            server.Object.ServerUris.Append(kBaseServerUri);
            EncodeableFactory originalFactory = (EncodeableFactory)server.Object.Factory;
            Assert.That(originalFactory.TryGetEncodeableType(
                DataTypeIds.Range, out IEncodeableType? rangeType), Is.True);

            var preparation = new ChangePreparer(server.Object);
            ChangePreparer.StagingServer candidate = preparation.CreateServer();
            long namespaceRevision = preparation.NamespaceRevision;
            long serverRevision = preparation.ServerRevision;
            long typeRevision = preparation.TypeRevision;
            long factoryRevision = preparation.FactoryRevision;

            server.Object.NamespaceUris.Append(kSourceMutationNamespaceUri);
            server.Object.ServerUris.Append(kCandidateServerUri);
            server.Object.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
            server.Object.TypeTree.AddSubtype(s_subtypeId, s_supertypeId);
            originalFactory.Builder.AddEncodeableType(s_registrationId, rangeType!).Commit();

            Assert.Multiple(() =>
            {
                Assert.That(preparation.NamespaceRevision, Is.EqualTo(namespaceRevision));
                Assert.That(preparation.NamespaceUris.GetIndex(kSourceMutationNamespaceUri), Is.EqualTo(-1));
                Assert.That(candidate.NamespaceUris.GetIndex(kSourceMutationNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.NamespaceUris.Version, Is.EqualTo(namespaceRevision + 1));
                Assert.That(preparation.ServerRevision, Is.EqualTo(serverRevision));
                Assert.That(preparation.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(-1));
                Assert.That(candidate.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(-1));
                Assert.That(server.Object.ServerUris.Version, Is.EqualTo(serverRevision + 1));
                Assert.That(preparation.TypeRevision, Is.EqualTo(typeRevision));
                Assert.That(preparation.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(candidate.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(server.Object.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(preparation.FactoryRevision, Is.EqualTo(factoryRevision));
                Assert.That(preparation.Factory.TryGetEncodeableType(s_registrationId, out _), Is.False);
                Assert.That(candidate.Factory.TryGetEncodeableType(s_registrationId, out _), Is.False);
                Assert.That(
                    server.Object.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? canonicalType),
                    Is.True);
                Assert.That(canonicalType, Is.SameAs(rangeType));
                Assert.That(candidate.IsStaging, Is.True);
            });

            candidate.Bind();

            Assert.Multiple(() =>
            {
                Assert.That(candidate.IsStaging, Is.False);
                Assert.That(candidate.NamespaceUris.GetIndex(kSourceMutationNamespaceUri), Is.EqualTo(2));
                Assert.That(candidate.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(1));
                Assert.That(candidate.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(
                    candidate.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? boundType),
                    Is.True);
                Assert.That(boundType, Is.SameAs(rangeType));
            });
        }

        /// <summary>
        /// Candidate metadata changes do not affect serving registries before publication and binding.
        /// </summary>
        [Test]
        public void StagingServerKeepsCandidateMetadataPrivateUntilCanonicalBinding()
        {
            Mock<IServerInternal> server = CreateServer();
            server.Object.NamespaceUris.Append(kBaseNamespaceUri);
            server.Object.ServerUris.Append(kBaseServerUri);
            EncodeableFactory originalFactory = (EncodeableFactory)server.Object.Factory;
            Assert.That(originalFactory.TryGetEncodeableType(
                DataTypeIds.Range, out IEncodeableType? rangeType), Is.True);

            ChangePreparer.StagingServer candidate = new ChangePreparer(server.Object).CreateServer();
            ushort namespaceIndex = candidate.NamespaceUris.GetIndexOrAppend(kStagedNamespaceUri);
            ushort serverIndex = candidate.ServerUris.GetIndexOrAppend(kStagedServerUri);
            candidate.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
            candidate.TypeTree.AddSubtype(s_subtypeId, s_supertypeId);
            candidate.Factory.Builder.AddEncodeableType(s_registrationId, rangeType!).Commit();

            Assert.Multiple(() =>
            {
                Assert.That(namespaceIndex, Is.EqualTo(2));
                Assert.That(candidate.NamespaceUris.GetString(namespaceIndex), Is.EqualTo(kStagedNamespaceUri));
                Assert.That(serverIndex, Is.EqualTo(1));
                Assert.That(candidate.ServerUris.GetString(serverIndex), Is.EqualTo(kStagedServerUri));
                Assert.That(candidate.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(
                    candidate.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? stagedType),
                    Is.True);
                Assert.That(stagedType, Is.SameAs(rangeType));
                Assert.That(server.Object.NamespaceUris.Count, Is.EqualTo(2));
                Assert.That(server.Object.NamespaceUris.GetIndex(kStagedNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.ServerUris.Count, Is.EqualTo(1));
                Assert.That(server.Object.ServerUris.GetIndex(kStagedServerUri), Is.EqualTo(-1));
                Assert.That(server.Object.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(server.Object.Factory.TryGetEncodeableType(s_registrationId, out _), Is.False);
                Assert.That(candidate.IsStaging, Is.True);
            });

            server.Object.NamespaceUris.Append(kStagedNamespaceUri);
            server.Object.ServerUris.Append(kStagedServerUri);
            server.Object.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
            server.Object.TypeTree.AddSubtype(s_subtypeId, s_supertypeId);
            originalFactory.Builder.AddEncodeableType(s_registrationId, rangeType!).Commit();
            candidate.Bind();

            Assert.Multiple(() =>
            {
                Assert.That(candidate.IsStaging, Is.False);
                Assert.That(candidate.NamespaceUris.GetIndex(kStagedNamespaceUri), Is.EqualTo(namespaceIndex));
                Assert.That(candidate.ServerUris.GetIndex(kStagedServerUri), Is.EqualTo(serverIndex));
                Assert.That(candidate.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(
                    candidate.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? boundType),
                    Is.True);
                Assert.That(boundType, Is.SameAs(rangeType));
            });
        }

        /// <summary>
        /// Successful creation associates the candidate with its isolated staging facade.
        /// </summary>
        [Test]
        public async Task CreateNodeManagerAsyncSuccessBindsCandidateIdentityAndMetadata()
        {
            Mock<IServerInternal> server = CreateServer();
            var preparation = new ChangePreparer(server.Object);
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            IServerInternal? suppliedServer = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    suppliedServer = candidate;
                    candidate.NamespaceUris.GetIndexOrAppend(kCandidateNamespaceUri);
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            ChangePreparer.StagingServer staging = ChangePreparer.GetServer(created)!;

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.SameAs(manager));
                Assert.That(suppliedServer, Is.SameAs(staging));
                Assert.That(staging.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(staging.IsStaging, Is.True);
                Assert.That(staging.IsDetached, Is.False);
                Assert.That(factory.Invocations, Has.Count.EqualTo(1));
            });

            ChangePreparer.Bind(created);

            Assert.Multiple(() =>
            {
                Assert.That(ChangePreparer.GetServer(created), Is.SameAs(staging));
                Assert.That(staging.IsStaging, Is.False);
                Assert.That(staging.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
            });
        }

        /// <summary>
        /// A conflicting claim cannot replace the winning candidate's facade or metadata.
        /// </summary>
        [Test]
        public async Task CreateNodeManagerAsyncCompetingCandidateConflictLeavesWinnerIsolated()
        {
            Mock<IServerInternal> server = CreateServer();
            var firstPreparation = new ChangePreparer(server.Object);
            var secondPreparation = new ChangePreparer(server.Object);
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            IServerInternal? firstCandidate = null;
            IServerInternal? competingCandidate = null;
            int calls = 0;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        firstCandidate = candidate;
                        candidate.NamespaceUris.GetIndexOrAppend(kBaseNamespaceUri);
                    }
                    else
                    {
                        competingCandidate = candidate;
                        candidate.NamespaceUris.GetIndexOrAppend(kStagedNamespaceUri);
                    }
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            IAsyncNodeManager winner = await firstPreparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            ChangePreparer.StagingServer owner = ChangePreparer.GetServer(winner)!;
            NodeManagerAlreadyRegisteredException conflict = (await Assert.ThrowsAsync<NodeManagerAlreadyRegisteredException>(
                async () => await secondPreparation.CreateNodeManagerAsync(
                    factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(conflict, Is.Not.Null);
                Assert.That(firstCandidate, Is.SameAs(owner));
                Assert.That(competingCandidate, Is.Not.SameAs(owner));
                Assert.That(ChangePreparer.GetServer(manager), Is.SameAs(owner));
                Assert.That(owner.NamespaceUris.GetIndex(kBaseNamespaceUri), Is.EqualTo(1));
                Assert.That(owner.NamespaceUris.GetIndex(kStagedNamespaceUri), Is.EqualTo(-1));
                Assert.That(((IServerInternal)competingCandidate!).NamespaceUris.GetIndex(kStagedNamespaceUri),
                    Is.EqualTo(1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kBaseNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kStagedNamespaceUri), Is.EqualTo(-1));
                Assert.That(calls, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Factory failures propagate without publishing staged namespace changes.
        /// </summary>
        [Test]
        public async Task CreateNodeManagerAsyncFactoryFailureDoesNotClaimOrPublishCandidate()
        {
            Mock<IServerInternal> server = CreateServer();
            var preparation = new ChangePreparer(server.Object);
            IAsyncNodeManager unclaimedManager = new Mock<IAsyncNodeManager>().Object;
            IServerInternal? suppliedServer = null;
            var expected = new InvalidOperationException("candidate factory failed");
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    suppliedServer = candidate;
                    candidate.NamespaceUris.GetIndexOrAppend(kCandidateNamespaceUri);
                    throw expected;
                });

            InvalidOperationException actual = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(expected));
                Assert.That(suppliedServer, Is.Not.Null);
                Assert.That(((IServerInternal)suppliedServer!).NamespaceUris.GetIndex(kCandidateNamespaceUri),
                    Is.EqualTo(1));
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(ChangePreparer.GetServer(unclaimedManager), Is.Null);
                Assert.That(factory.Invocations, Has.Count.EqualTo(1));
            });
        }

        /// <summary>
        /// Null factories report the add or replacement failure without changing serving metadata.
        /// </summary>
        [TestCase(false, "The NodeManager factory returned null.")]
        [TestCase(true, "The replacement NodeManager factory returned null.")]
        public async Task CreateNodeManagerAsyncNullFactoryResultReportsExactFailure(
            bool replacement,
            string expectedMessage)
        {
            Mock<IServerInternal> server = CreateServer();
            var preparation = new ChangePreparer(server.Object);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IAsyncNodeManager>((IAsyncNodeManager)null!));

            InvalidOperationException actual = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await preparation.CreateNodeManagerAsync(
                    factory.Object, null!, replacement, _ => false, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(actual.Message, Is.EqualTo(expectedMessage));
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(factory.Invocations, Has.Count.EqualTo(1));
            });
        }

        /// <summary>
        /// Preparation rejects encodeable factories that cannot isolate staged registrations.
        /// </summary>
        [Test]
        public void ConstructorRejectsFactoryThatCannotStageRegistrations()
        {
            Mock<IServerInternal> server = CreateServer();
            server.SetupGet(value => value.Factory).Returns(new Mock<IEncodeableFactory>().Object);

            NotSupportedException actual = Assert.Throws<NotSupportedException>(
                () => new ChangePreparer(server.Object))!;

            Assert.That(actual.Message, Is.EqualTo("The server factory does not support staged registrations."));
        }

        /// <summary>
        /// Detachment keeps candidate metadata isolated from later serving registry changes.
        /// </summary>
        [Test]
        public async Task DetachPreservesCandidateSnapshotAfterCanonicalMetadataChanges()
        {
            Mock<IServerInternal> server = CreateServer();
            server.Object.NamespaceUris.Append(kBaseNamespaceUri);
            server.Object.ServerUris.Append(kBaseServerUri);
            EncodeableFactory originalFactory = (EncodeableFactory)server.Object.Factory;
            Assert.That(originalFactory.TryGetEncodeableType(
                DataTypeIds.Range, out IEncodeableType? rangeType), Is.True);
            var preparation = new ChangePreparer(server.Object);
            IAsyncNodeManager manager = new Mock<IAsyncNodeManager>().Object;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IServerInternal candidate, ApplicationConfiguration _, CancellationToken _) =>
                {
                    candidate.NamespaceUris.GetIndexOrAppend(kCandidateNamespaceUri);
                    candidate.ServerUris.GetIndexOrAppend(kCandidateServerUri);
                    candidate.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
                    candidate.TypeTree.AddSubtype(s_subtypeId, s_supertypeId);
                    candidate.Factory.Builder.AddEncodeableType(s_registrationId, rangeType!).Commit();
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            IAsyncNodeManager created = await preparation.CreateNodeManagerAsync(
                factory.Object, null!, false, _ => false, CancellationToken.None).ConfigureAwait(false);
            ChangePreparer.StagingServer staging = ChangePreparer.GetServer(created)!;
            ChangePreparer.Detach(created);

            server.Object.NamespaceUris.Append(kSourceMutationNamespaceUri);
            server.Object.ServerUris.Append(kStagedServerUri);
            server.Object.TypeTree.AddSubtype(s_supertypeId, NodeId.Null);
            server.Object.TypeTree.AddSubtype(new NodeId(99004), s_supertypeId);
            originalFactory.Builder.AddEncodeableType(s_postDetachRegistrationId, rangeType!).Commit();

            Assert.Multiple(() =>
            {
                Assert.That(staging.IsDetached, Is.True);
                Assert.That(staging.IsStaging, Is.True);
                Assert.That(staging.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(2));
                Assert.That(staging.NamespaceUris.GetIndex(kSourceMutationNamespaceUri), Is.EqualTo(-1));
                Assert.That(staging.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(1));
                Assert.That(staging.ServerUris.GetIndex(kStagedServerUri), Is.EqualTo(-1));
                Assert.That(staging.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(s_supertypeId));
                Assert.That(staging.TypeTree.FindSuperType(new NodeId(99004)), Is.EqualTo(NodeId.Null));
                Assert.That(
                    staging.Factory.TryGetEncodeableType(s_registrationId, out IEncodeableType? stagedType),
                    Is.True);
                Assert.That(stagedType, Is.SameAs(rangeType));
                Assert.That(staging.Factory.TryGetEncodeableType(s_postDetachRegistrationId, out _), Is.False);
                Assert.That(server.Object.NamespaceUris.GetIndex(kCandidateNamespaceUri), Is.EqualTo(-1));
                Assert.That(server.Object.ServerUris.GetIndex(kCandidateServerUri), Is.EqualTo(-1));
                Assert.That(server.Object.TypeTree.FindSuperType(s_subtypeId), Is.EqualTo(NodeId.Null));
                Assert.That(
                    server.Object.Factory.TryGetEncodeableType(s_postDetachRegistrationId, out IEncodeableType? sourceType),
                    Is.True);
                Assert.That(sourceType, Is.SameAs(rangeType));
                Assert.That(ChangePreparer.GetServer(created), Is.SameAs(staging));
            });
        }

        /// <summary>
        /// Creates isolated serving metadata and context without starting a server.
        /// </summary>
        private static Mock<IServerInternal> CreateServer()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            var serverUris = new StringTable();
            var typeTree = new TypeTable(namespaces);
            EncodeableFactory factory = (EncodeableFactory)EncodeableFactory.Create();
            ITelemetryContext telemetry = new Mock<ITelemetryContext>().Object;
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(serverUris);
            server.SetupGet(value => value.TypeTree).Returns(typeTree);
            server.SetupGet(value => value.Factory).Returns(factory);
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MessageContext).Returns(new ServiceMessageContext(telemetry, factory)
            {
                NamespaceUris = namespaces,
                ServerUris = serverUris
            });
            server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(server.Object));
            return server;
        }

        /// <summary>The namespace already present in serving metadata.</summary>
        private const string kBaseNamespaceUri = "urn:opcfoundation.org:Tests:ChangePreparer:Base";

        /// <summary>The namespace added through a staging facade.</summary>
        private const string kStagedNamespaceUri = "urn:opcfoundation.org:Tests:ChangePreparer:Staged";

        /// <summary>The namespace added to serving metadata after snapshot capture.</summary>
        private const string kSourceMutationNamespaceUri = "urn:opcfoundation.org:Tests:ChangePreparer:SourceMutation";

        /// <summary>The namespace added by a candidate factory.</summary>
        private const string kCandidateNamespaceUri = "urn:opcfoundation.org:Tests:ChangePreparer:Candidate";

        /// <summary>The server URI already present in serving metadata.</summary>
        private const string kBaseServerUri = "urn:opcfoundation.org:Tests:ChangePreparer:BaseServer";

        /// <summary>The server URI added through a staging facade.</summary>
        private const string kStagedServerUri = "urn:opcfoundation.org:Tests:ChangePreparer:StagedServer";

        /// <summary>The server URI added by a candidate factory.</summary>
        private const string kCandidateServerUri = "urn:opcfoundation.org:Tests:ChangePreparer:CandidateServer";

        /// <summary>The type whose parent differs between serving and staged metadata.</summary>
        private static readonly NodeId s_subtypeId = new(99001);

        /// <summary>The parent type registered by the tests.</summary>
        private static readonly NodeId s_supertypeId = new(99002);

        /// <summary>The standard namespace present in a new namespace table.</summary>
        private static readonly string[] s_expectedDefaultNamespaces = ["http://opcfoundation.org/UA/"];

        /// <summary>The encodeable registration captured or added during preparation.</summary>
        private static readonly ExpandedNodeId s_registrationId =
            new(99003, "urn:opcfoundation.org:Tests:ChangePreparer:Encodeables");

        /// <summary>The serving registration added after candidate detachment.</summary>
        private static readonly ExpandedNodeId s_postDetachRegistrationId =
            new(99004, "urn:opcfoundation.org:Tests:ChangePreparer:Encodeables");
    }
}
