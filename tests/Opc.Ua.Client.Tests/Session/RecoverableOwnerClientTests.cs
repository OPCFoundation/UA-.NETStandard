/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Storage;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Exercises explicit authenticated owner reattachment with synthetic channels and no devices or credentials.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableOwnerClientTests
    {
        [Test]
        public async Task OwnerReplacementCollectsWithoutWeakeningStrictSessionContext()
        {
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync().ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.FastResult = false;
            await using SessionRequestContext strict = await SessionRequestContext.CaptureAsync(
                peer.Session, peer.Timeout).ConfigureAwait(false);
            await using SessionRequestContext sameSession = await SessionRequestContext.CaptureRecoverableAsync(
                peer.Session, peer.Timeout).ConfigureAwait(false);
            var factory = new AsyncServiceClient(authenticated.Options);
            await using AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            NodeId originalSession = peer.Owner.SessionId;

            await ReplaceSessionAsync(peer).ConfigureAwait(false);
            Assert.That(peer.Owner.SessionId, Is.Not.EqualTo(originalSession));
            Assert.That(strict.Invalidated.IsCompleted, Is.True);
            Assert.That(sameSession.Invalidated.IsCompleted, Is.True);
            _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await strict.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    ReadRequest().NodesToRead, peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);

            _ = await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            AssertValue(await operation.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false));
            Assert.That(operation.Invalidated.IsCompleted, Is.False);
            Assert.That(operation.SessionId, Is.EqualTo(originalSession));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections.Single().RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Owner));
            Assert.That(peer.ManagerCalls.First().RequestHeader.AuthenticationToken,
                Is.Not.EqualTo(peer.ManagerCalls.Last().RequestHeader.AuthenticationToken));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase("server")]
        [TestCase("application")]
        [TestCase("user")]
        [TestCase("denied")]
        public async Task ChangedOwnerMappingCannotCollectThroughReplacementSession(string change)
        {
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync().ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.FastResult = false;
            var factory = new AsyncServiceClient(authenticated.Options);
            await using AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            await ReplaceSessionAsync(peer).ConfigureAwait(false);
            authenticated.Identity.Change = change;

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.CollectAsync(ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(change == "denied"
                ? StatusCodes.BadUserAccessDenied : StatusCodes.BadIdentityTokenRejected));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(ServiceOperationRecoveryMode.Owner)]
        [TestCase(ServiceOperationRecoveryMode.Durable)]
        public async Task UnavailableOwnerModeFailsBeforeSubmitInsteadOfDowngrading(ServiceOperationRecoveryMode mode)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                mode, store, enabled: true).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.SupportedRecoveryModes = [(int)ServiceOperationRecoveryMode.Session];

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peer.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    ReadRequest().NodesToRead, peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [TestCase("anonymous")]
        [TestCase("application")]
        [TestCase("insecure-transport")]
        public async Task ClaimedIdentityWithoutAuthenticationCannotEstablishOwnerContinuity(string missing)
        {
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync().ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            if (missing == "anonymous")
            {
                await peer.Owner.UpdateSessionAsync(new UserIdentity(), [], peer.Timeout).ConfigureAwait(false);
            }
            else if (missing == "application")
            {
                peer.Channels.CreatedChannels[0].Mock.Setup(channel => channel.ClientChannelCertificate).Returns([]);
            }
            else
            {
                peer.Owner.ConfiguredEndpoint.Description.TransportProfileUri = Profiles.UaTcpTransport;
            }
            var factory = new AsyncServiceClient(authenticated.Options);
            _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.CreateReadAsync(peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.ManagerCalls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task AcceptedCheckpointPrecedesAcknowledgementAndRestoresAfterLostControlReply()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            var factory = new AsyncServiceClient(authenticated.Options);
            Uuid id;
            await using (AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
            {
                id = operation.ClientOperationId;
                operation.RequireExplicitAcknowledgement()
                    .WithAcceptancePolicy(async (outcome, ct) =>
                {
                    Assert.That(outcome.Operation.ClientOperationId, Is.EqualTo(id));
                    (RecoverableOperationCheckpoint checkpoint, _) =
                        await store.LoadAsync(id, peer.Session.MessageContext, ct).ConfigureAwait(false);
                    Assert.That(checkpoint.Acceptance, Is.EqualTo(RecoverableCheckpointAcceptance.OutcomeValidated));
                    Assert.That(checkpoint.OriginalResponse, Is.InstanceOf<ReadResponse>());
                    Assert.That(peer.Controls, Is.Empty);
                });
                _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
                peer.BeforeRespondAsync = async (request, ct) =>
                {
                    if (request is OperationControlRequestDataType)
                    {
                        (RecoverableOperationCheckpoint checkpoint, _) =
                            await store.LoadAsync(id, peer.Session.MessageContext, ct).ConfigureAwait(false);
                        Assert.That(checkpoint.Acceptance, Is.EqualTo(RecoverableCheckpointAcceptance.Accepted));
                        Assert.That(checkpoint.OriginalResponse, Is.InstanceOf<ReadResponse>());
                    }
                };
                peer.LoseAckReply = true;
                _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await operation.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
                AssertValue(await operation.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false));
            }

            await using AsyncServiceOperation<ReadResponse> restored =
                await new AsyncServiceClient(authenticated.Options).ReattachAsync<ReadResponse>(
                    peer.Session, id, ct: peer.Timeout).ConfigureAwait(false);
            AssertValue(await restored.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false));
            ControlResponseDataType retried = await restored.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(retried.Result, Is.EqualTo(ServiceOperationControlResult.AlreadyApplied));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty,
                "A durably accepted outcome is restored from its protected snapshot.");
            Assert.That(peer.Controls, Has.Count.EqualTo(2));
            (RecoverableOperationCheckpoint final, _) =
                await store.LoadAsync(id, peer.Session.MessageContext, peer.Timeout).ConfigureAwait(false);
            Assert.That(final.Acceptance, Is.EqualTo(RecoverableCheckpointAcceptance.Acknowledged));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await restored.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task MissingPreSubmissionCheckpointCommitPreventsAnyOriginalDispatch()
        {
            using var backend = new TestCheckpointStore { FailWriteAt = 1 };
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            await using AsyncServiceOperation<ReadResponse> operation =
                await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                    peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);

            _ = await Assert.ThrowsAsync<IOException>(async () =>
                await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.ManagerCalls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedAcceptanceCommitNeverAcknowledgesAndRecoveryNeverSubmits(bool afterCommit)
        {
            using var backend = new TestCheckpointStore { FailWriteAt = 4, FailAfterCommit = afterCommit };
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            var factory = new AsyncServiceClient(authenticated.Options);
            Uuid id;
            await using (AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
            {
                id = operation.ClientOperationId;
                _ = await Assert.ThrowsAsync<IOException>(async () =>
                    await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(operation.Completion.IsCompleted, Is.False);
                Assert.That(peer.Controls, Is.Empty);
            }
            await using AsyncServiceOperation<ReadResponse> restored = await factory.ReattachAsync<ReadResponse>(
                peer.Session, id, ct: peer.Timeout).ConfigureAwait(false);
            if (!afterCommit)
            {
                Assert.That(restored.Completion.IsCompleted, Is.False);
                _ = await restored.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            }
            AssertValue(await restored.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false));
            Assert.That(peer.Controls, Is.Empty);
            _ = await restored.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [TestCase("server")]
        [TestCase("application")]
        [TestCase("user")]
        public async Task CheckpointReattachmentRequiresTheSameAuthenticatedOwner(string change)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Owner, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            var factory = new AsyncServiceClient(authenticated.Options);
            Uuid id;
            await using (AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
            {
                operation.RequireExplicitAcknowledgement();
                id = operation.ClientOperationId;
                _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            }
            authenticated.Identity.Change = change;
            int calls = peer.ManagerCalls.Count;
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.ReattachAsync<ReadResponse>(peer.Session, id, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(peer.ManagerCalls, Has.Count.EqualTo(calls));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task SessionCheckpointCannotAttachToAnAuthorizedReplacementSession()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Session, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            var factory = new AsyncServiceClient(authenticated.Options);
            Uuid id;
            await using (AsyncServiceOperation<ReadResponse> operation = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
            {
                id = operation.ClientOperationId;
                operation.RequireExplicitAcknowledgement();
                _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            }
            await ReplaceSessionAsync(peer).ConfigureAwait(false);
            _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.ReattachAsync<ReadResponse>(peer.Session, id, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [Test]
        public async Task DurableIndeterminateOutcomeRemainsAnAcceptedProtocolFailure()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.MutateResponse = (_, response) =>
            {
                if (response is SubmitResponseDataType submit)
                {
                    submit.Operation.OutcomeKind = ServiceOperationOutcome.ExecutionIndeterminate;
                    submit.Operation.OutcomeStatus = StatusCodes.BadUnexpectedError;
                    submit.Result = default;
                }
            };
            await using AsyncServiceOperation<ReadResponse> operation =
                await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                    peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            AsyncServiceResult<ReadResponse> result = await operation.Completion.WaitAsync(peer.Timeout)
                .ConfigureAwait(false);
            Assert.That(result.OriginalResponse, Is.Null);
            Assert.That(result.HasValue, Is.False);
            ServiceResultException failure = Assert.Throws<ServiceResultException>(() => result.GetValueOrThrow())!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            (RecoverableOperationCheckpoint saved, _) =
                await store.LoadAsync(operation.ClientOperationId, peer.Session.MessageContext, peer.Timeout)
                    .ConfigureAwait(false);
            Assert.That(saved.Acceptance, Is.EqualTo(RecoverableCheckpointAcceptance.Accepted));
            Assert.That(saved.Operation!.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ExecutionIndeterminate));
            Assert.That(saved.OriginalResponse, Is.Null);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ReopenedFileCheckpointRecoversLostSubmitByClientIdThroughReplacementSession()
        {
            string parent = Path.Combine(Path.GetTempPath(), "ua-rse-client-" + Guid.NewGuid().ToString("N"));
            string directory = Path.Combine(parent, "store");
            Directory.CreateDirectory(parent);
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            try
            {
                await using var first = new FileSharedKeyValueStore(
                    directory, "synthetic-client-recovery", protector, 1_048_576);
                var writer = new RecoverableOperationCheckpointStore(first, protector);
                await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                    ServiceOperationRecoveryMode.Durable, writer).ConfigureAwait(false);
                RecoverableSessionPeer peer = authenticated.Peer;
                peer.LoseSubmitReply = true;
                Uuid id;
                await using (AsyncServiceOperation<ReadResponse> operation =
                    await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                        peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
                {
                    id = operation.ClientOperationId;
                    _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(operation.ServerToken.IsNull, Is.True);
                    Assert.That(operation.Completion.IsCompleted, Is.False);
                }
                await first.DisposeAsync().ConfigureAwait(false);
                await ReplaceSessionAsync(peer).ConfigureAwait(false);
                await using var reopened = new FileSharedKeyValueStore(
                    directory, "synthetic-client-recovery", protector, 1_048_576);
                var reader = new RecoverableOperationCheckpointStore(reopened, protector);
                var recovery = new AsyncServiceClient(authenticated.Options with { CheckpointStore = reader });
                await using AsyncServiceOperation<ReadResponse> restored = await recovery.ReattachAsync<ReadResponse>(
                    peer.Session, id, ct: peer.Timeout).ConfigureAwait(false);
                Assert.That(restored.ClientOperationId, Is.EqualTo(id));
                Assert.That(restored.ServerToken.IsNull, Is.True);
                _ = await restored.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
                AssertValue(await restored.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false));
                Assert.That(peer.Collections.Single().ClientOperationId, Is.EqualTo(id));
                Assert.That(peer.Collections.Single().ServerToken.IsNull, Is.True);
                Assert.That(restored.ServerToken.IsNull, Is.False);
                _ = await restored.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(peer.Submissions, Has.Count.EqualTo(1));
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
                Assert.That(peer.OrdinaryRequests, Is.Empty);
            }
            finally
            {
                if (Directory.Exists(parent))
                {
                    Directory.Delete(parent, recursive: true);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReattachedTypedMethodMustMatchTheSavedOriginalCall(bool changeOriginal)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            var factory = new AsyncServiceClient(authenticated.Options);
            Uuid id;
            await using (AsyncServiceOperation<ByteString> original = await factory.CreateMethodAsync(
                peer.Session, (client, ct) => SigningRequestAsync(client, "CN=Synthetic", peer.Channels.Telemetry, ct),
                peer.Timeout).ConfigureAwait(false))
            {
                id = original.ClientOperationId;
                original.RequireExplicitAcknowledgement();
                _ = await original.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            }
            if (changeOriginal)
            {
                ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await factory.ReattachMethodAsync(peer.Session, id,
                        (client, ct) => SigningRequestAsync(client, "CN=Different", peer.Channels.Telemetry, ct),
                        ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(peer.Controls, Is.Empty);
            }
            else
            {
                await using AsyncServiceOperation<ByteString> restored = await factory.ReattachMethodAsync(
                    peer.Session, id,
                    (client, ct) => SigningRequestAsync(client, "CN=Synthetic", peer.Channels.Telemetry, ct),
                    ct: peer.Timeout).ConfigureAwait(false);
                AsyncServiceResult<ByteString> accepted = await restored.Completion.WaitAsync(peer.Timeout)
                    .ConfigureAwait(false);
                Assert.That(accepted.GetValueOrThrow(), Is.EqualTo(ByteString.From("synthetic-csr"u8)));
                _ = await restored.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(peer.Controls, Has.Count.EqualTo(1));
            }
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DurableCompoundReceiptCommitsOnlyAfterCompleteTypedAcceptance(bool reject)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store, enabled: true).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            var refusal = new IOException("Synthetic application acceptance failure.");
            int acceptances = 0;
            await using RecoverableAcknowledgementScope scope = peer.Session.BeginAcknowledgementScope(
                requireRecoverableReceipt: true, acceptAsync: async (context, ct) =>
                {
                    Assert.That(context.TryGetResult(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(85));
                    ArrayOf<Uuid> ids = await store.GetOperationIdsAsync(ct).ConfigureAwait(false);
                    Assert.That(ids.Count, Is.EqualTo(2));
                    for (int i = 0; i < ids.Count; i++)
                    {
                        (RecoverableOperationCheckpoint checkpoint, _) =
                            await store.LoadAsync(ids[i], peer.Session.MessageContext, ct).ConfigureAwait(false);
                        Assert.That(checkpoint.Acceptance,
                            Is.EqualTo(RecoverableCheckpointAcceptance.OutcomeValidated));
                        Assert.That(checkpoint.OriginalResponse, Is.InstanceOf<ReadResponse>());
                    }
                    Assert.That(peer.Controls, Is.Empty);
                    acceptances++;
                    if (reject)
                    {
                        throw refusal;
                    }
                });
            async ValueTask<int> ReadBothAsync(CancellationToken ct)
            {
                int first = await peer.Session.ReadValueAsync<int>(new NodeId(100u), ct).ConfigureAwait(false);
                int second = await peer.Session.ReadValueAsync<int>(new NodeId(101u), ct).ConfigureAwait(false);
                return first + second;
            }
            if (reject)
            {
                IOException actual = (await Assert.ThrowsAsync<IOException>(async () =>
                    await ClientOperationContext.ExecuteAsync(peer.Session, ReadBothAsync, peer.Timeout)
                        .ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(actual, Is.SameAs(refusal));
                _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else
            {
                int value = await ClientOperationContext.ExecuteAsync(peer.Session, ReadBothAsync, peer.Timeout)
                    .ConfigureAwait(false);
                Assert.That(value, Is.EqualTo(85));
            }
            Assert.That(acceptances, Is.EqualTo(1));
            ArrayOf<Uuid> savedIds = await store.GetOperationIdsAsync(peer.Timeout).ConfigureAwait(false);
            for (int i = 0; i < savedIds.Count; i++)
            {
                (RecoverableOperationCheckpoint saved, _) =
                    await store.LoadAsync(savedIds[i], peer.Session.MessageContext, peer.Timeout).ConfigureAwait(false);
                Assert.That(saved.Acceptance, Is.EqualTo(reject
                    ? RecoverableCheckpointAcceptance.OutcomeValidated : RecoverableCheckpointAcceptance.Accepted));
            }
            Assert.That(peer.Controls, Is.Empty);
            if (!reject)
            {
                await scope.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
                Assert.That(peer.Controls, Has.Count.EqualTo(2));
                Assert.That(scope.Receipt!.IsAcknowledged, Is.True);
            }
            Assert.That(peer.Submissions, Has.Count.EqualTo(2));
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task LostTypedSubmissionCannotRestoreThroughAnUntypedAcceptancePath()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.LoseSubmitReply = true;
            var factory = new AsyncServiceClient(authenticated.Options);
            Uuid id;
            await using (AsyncServiceOperation<ByteString> original = await factory.CreateMethodAsync(peer.Session,
                (client, ct) => SigningRequestAsync(client, "CN=Synthetic", peer.Channels.Telemetry, ct), peer.Timeout)
                .ConfigureAwait(false))
            {
                id = original.ClientOperationId;
                _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await original.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            ServiceResultException rejected = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await factory.ReattachAsync<CallResponse>(peer.Session, id, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);

            await using AsyncServiceOperation<ByteString> restored = await factory.ReattachMethodAsync(peer.Session, id,
                (client, ct) => SigningRequestAsync(client, "CN=Synthetic", peer.Channels.Telemetry, ct),
                ct: peer.Timeout).ConfigureAwait(false);
            _ = await restored.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            Assert.That((await restored.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false)).GetValueOrThrow(),
                Is.EqualTo(ByteString.From("synthetic-csr"u8)));
            _ = await restored.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections.Single().ClientOperationId, Is.EqualTo(id));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task UnacceptedApplicationPolicyCannotDisappearAfterClientRestart()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            var factory = new AsyncServiceClient(authenticated.Options);
            Uuid id;
            await using (AsyncServiceOperation<ReadResponse> original = await factory.CreateReadAsync(
                peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
            {
                id = original.ClientOperationId;
                original.WithAcceptancePolicy((_, _) => throw new IOException("Synthetic application refusal."));
                _ = await Assert.ThrowsAsync<IOException>(async () =>
                    await original.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await factory.ReattachAsync<ReadResponse>(peer.Session, id, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false);
            int acceptances = 0;
            await using AsyncServiceOperation<ReadResponse> restored = await factory.ReattachAsync<ReadResponse>(
                peer.Session, id, (outcome, _) =>
                {
                    AssertValue(outcome);
                    acceptances++;
                    return default;
                }, peer.Timeout).ConfigureAwait(false);
            _ = await restored.CollectAsync(ct: peer.Timeout).ConfigureAwait(false);
            Assert.That(acceptances, Is.EqualTo(1));
            Assert.That(peer.Controls, Is.Empty);
            _ = await restored.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [TestCase("schema")]
        [TestCase("order")]
        [TestCase("type")]
        [TestCase("revision")]
        [TestCase("unqualified")]
        public async Task ChangedRestartInterpretationRejectsBeforeSavedPayloadDecodeOrAnyRemoteAction(string change)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var qualifier = new RecoveryTypeTestQualifier();
            var compatibility = new RecoverableExecutionTypeCompatibility(qualifier);
            var store = new RecoverableOperationCheckpointStore(backend, protector, typeCompatibility: compatibility);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            var originalCodec = new RecoveryTypeTestCodec();
            peer.Session.Factory.Builder.AddEncodeableType(originalCodec).Commit();
            IEncodeableFactory facade = peer.Session.Factory;
            var namespaces = peer.Session.NamespaceUris.ToArrayOf();
            peer.ReadValueFactory = _ => new DataValue(Variant.From(
                new ExtensionObject(new RecoveryTypeTestValue { First = 4_000_000_001, Second = 17 })));
            Uuid id;
            await using (AsyncServiceOperation<ReadResponse> operation =
                await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                    peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
            {
                id = operation.ClientOperationId;
                operation.RequireExplicitAcknowledgement();
                _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            }
            var changed = new RecoveryTypeTestCodec { Change = change };
            peer.Session.Factory.Builder.AddEncodeableType(changed).Commit();
            if (change == "revision")
            {
                qualifier.Revision = ByteString.From("synthetic-codec-v2"u8);
            }
            RecoverableOperationCheckpointStore reader = change == "unqualified"
                ? new RecoverableOperationCheckpointStore(backend, protector) : store;
            int remoteCalls = peer.ManagerCalls.Count;
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await new AsyncServiceClient(authenticated.Options with { CheckpointStore = reader })
                    .ReattachAsync<ReadResponse>(peer.Session, id, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(change == "unqualified"
                ? StatusCodes.BadNotSupported : StatusCodes.BadDecodingError));
            Assert.That(peer.Session.Factory, Is.SameAs(facade));
            Assert.That(peer.Session.NamespaceUris.ToArrayOf(), Is.EqualTo(namespaces));
            Assert.That(changed.Decodes, Is.Zero);
            Assert.That(peer.ManagerCalls, Has.Count.EqualTo(remoteCalls));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task CompatibleCustomCheckpointReopensUsingTheSameQualifiedInterpretation()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var compatibility = new RecoverableExecutionTypeCompatibility(new RecoveryTypeTestQualifier());
            var store = new RecoverableOperationCheckpointStore(backend, protector, typeCompatibility: compatibility);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.Session.Factory.Builder.AddEncodeableType(new RecoveryTypeTestCodec()).Commit();
            peer.ReadValueFactory = _ => new DataValue(Variant.From(
                new ExtensionObject(new RecoveryTypeTestValue { First = 4_000_000_001, Second = 17 })));
            Uuid id;
            await using (AsyncServiceOperation<ReadResponse> operation =
                await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                    peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
            {
                id = operation.ClientOperationId;
                operation.RequireExplicitAcknowledgement();
                _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            }
            var reopened = new RecoveryTypeTestCodec();
            peer.Session.Factory.Builder.AddEncodeableType(reopened).Commit();
            await using AsyncServiceOperation<ReadResponse> restored =
                await new AsyncServiceClient(authenticated.Options).ReattachAsync<ReadResponse>(
                    peer.Session, id, ct: peer.Timeout).ConfigureAwait(false);
            ReadResponse response = (await restored.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false))
                .GetValueOrThrow();
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out ExtensionObject payload), Is.True);
            Assert.That(payload.TryGetValue(out RecoveryTypeTestValue? value), Is.True);
            Assert.That(value!.First, Is.EqualTo(4_000_000_001));
            Assert.That(value.Second, Is.EqualTo(17));
            Assert.That(reopened.Decodes, Is.GreaterThan(0));
            _ = await restored.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task FactoryMutationAfterAcceptancePreventsAcknowledgement()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector,
                typeCompatibility: new RecoverableExecutionTypeCompatibility(new RecoveryTypeTestQualifier()));
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.Session.Factory.Builder.AddEncodeableType(new RecoveryTypeTestCodec()).Commit();
            await using AsyncServiceOperation<ReadResponse> operation =
                await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                    peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            operation.RequireExplicitAcknowledgement();
            _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            peer.Session.Factory.Builder.AddEncodeableType(new RecoveryTypeTestCodec { Change = "order" }).Commit();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.AcknowledgeAsync(peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            AssertValue(await operation.Completion.WaitAsync(peer.Timeout).ConfigureAwait(false));
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task FactoryMutationAfterHandleCreationPreventsSubmit()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector,
                typeCompatibility: new RecoverableExecutionTypeCompatibility(new RecoveryTypeTestQualifier()));
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.Session.Factory.Builder.AddEncodeableType(new RecoveryTypeTestCodec()).Commit();
            await using AsyncServiceOperation<ReadResponse> operation =
                await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                    peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            peer.Session.Factory.Builder.AddEncodeableType(new RecoveryTypeTestCodec { Change = "type" }).Commit();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(peer.Submissions, Is.Empty);
            Assert.That(peer.ManagerCalls, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        [Test]
        public async Task ChangedRseRegistrationIsNotSilentlyRepairedBeforeRestore()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            Uuid id;
            await using (AsyncServiceOperation<ReadResponse> operation =
                await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                    peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false))
            {
                id = operation.ClientOperationId;
                operation.RequireExplicitAcknowledgement();
                _ = await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false);
            }
            var changed = new RecoveryRseOverrideCodec();
            peer.Session.Factory.Builder.AddEncodeableType(changed).Commit();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await new AsyncServiceClient(authenticated.Options)
                    .ReattachAsync<ReadResponse>(peer.Session, id, ct: peer.Timeout).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(peer.Session.Factory.TryGetEncodeableType(
                RseDataTypeIds.SubmitResponseDataType, out IEncodeableType? registered), Is.True);
            Assert.That(registered, Is.SameAs(changed));
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
        }

        [Test]
        public async Task UnknownOpaqueOriginalPayloadCannotBeCheckpointedOrAcknowledged()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = RecoverableCheckpointStoreTests.CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            await using AuthenticatedPeer authenticated = await AuthenticatedPeer.CreateAsync(
                ServiceOperationRecoveryMode.Durable, store).ConfigureAwait(false);
            RecoverableSessionPeer peer = authenticated.Peer;
            peer.ReadValueFactory = _ => new DataValue(Variant.From(new ExtensionObject(
                new ExpandedNodeId(999u, RecoveryTypeTestCodec.NamespaceUri),
                ByteString.From(new byte[] { 1, 2, 3, 4 }))));
            await using AsyncServiceOperation<ReadResponse> operation =
                await new AsyncServiceClient(authenticated.Options).CreateReadAsync(
                    peer.Session, ReadRequest(), peer.Timeout).ConfigureAwait(false);
            _ = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.SubmitAsync(8000, 4000, ct: peer.Timeout).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(operation.Completion.IsCompleted, Is.False);
            Assert.That(peer.Submissions, Has.Count.EqualTo(1));
            Assert.That(peer.Collections, Is.Empty);
            Assert.That(peer.Controls, Is.Empty);
            Assert.That(peer.OrdinaryRequests, Is.Empty);
        }

        private static ValueTask<ByteString> SigningRequestAsync(
            ISessionClient client, string subject, ITelemetryContext telemetry, CancellationToken ct)
        {
            return new ServerConfigurationTypeClient(client, ObjectIds.ServerConfiguration, telemetry)
                .CreateSigningRequestAsync(ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, subject, false, default, ct);
        }

        private static async ValueTask ReplaceSessionAsync(RecoverableSessionPeer peer)
        {
            int activations = 0;
            peer.BeforeRespondAsync = (request, _) =>
            {
                if (request is ActivateSessionRequest && Interlocked.Increment(ref activations) == 1)
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }
                return default;
            };
            await peer.RecoverAsync().ConfigureAwait(false);
            await peer.Owner.StateMachine.WaitForConnectedAsync(peer.Timeout).ConfigureAwait(false);
            SessionConfiguration configuration = peer.Owner.InnerSession.SaveSessionConfiguration();
            Assert.That(configuration.SessionId.IsNull, Is.False, "The replacement must have an activated Session ID.");
            Assert.That(configuration.AuthenticationToken.IsNull, Is.False,
                "The replacement must have an activated authentication token.");
            Assert.That(peer.Owner.InnerSession.Reconnecting, Is.False, "The managed replacement must be ready.");
            peer.BeforeRespondAsync = null;
        }

        private static ReadRequest ReadRequest()
        {
            return new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 37 },
                TimestampsToReturn = TimestampsToReturn.Neither,
                NodesToRead = [new ReadValueId { NodeId = new NodeId(100u), AttributeId = Attributes.Value }]
            };
        }

        private static void AssertValue(AsyncServiceResult<ReadResponse> result)
        {
            Assert.That(result.GetValueOrThrow().Results[0].WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
        }

        private sealed class AuthenticatedPeer : IAsyncDisposable
        {
            private AuthenticatedPeer(
                RecoverableSessionPeer peer, MappedIdentity identity, RecoverableSessionOptions options,
                Certificate application, Certificate server)
            {
                Peer = peer;
                Identity = identity;
                Options = options;
                m_application = application;
                m_server = server;
            }

            internal RecoverableSessionPeer Peer { get; }
            internal MappedIdentity Identity { get; }
            internal RecoverableSessionOptions Options { get; }

            internal static async ValueTask<AuthenticatedPeer> CreateAsync(
                ServiceOperationRecoveryMode mode = ServiceOperationRecoveryMode.Owner,
                RecoverableOperationCheckpointStore? store = null, bool enabled = false)
            {
                Certificate application = CertificateBuilder.Create("CN=Synthetic RSE Client").CreateForRSA();
                Certificate server = CertificateBuilder.Create("CN=Synthetic RSE Server")
                    .AddExtension(new X509SubjectAltNameExtension(
                        "urn:synthetic:rse-server", ["server.example.test"])).CreateForRSA();
                var user = new UserIdentity("synthetic-owner", Guid.NewGuid().ToByteArray());
                var identity = new MappedIdentity(application.RawData, server.RawData, user);
                var options = new RecoverableSessionOptions
                {
                    RecoveryMode = mode,
                    IdentityProvider = identity,
                    CheckpointStore = store
                };
                RecoverableSessionPeer? peer = null;
                try
                {
                    peer = await RecoverableSessionPeer.CreateAsync(options, enabled: enabled,
                        configureEndpoint: endpoint =>
                        {
                            endpoint.Description.EndpointUrl = "https://server.example.test:4840";
                            endpoint.Description.TransportProfileUri = Profiles.HttpsJsonTransport;
                            endpoint.Description.UserIdentityTokens =
                            [
                                endpoint.Description.UserIdentityTokens[0],
                                new UserTokenPolicy
                                {
                                    PolicyId = "synthetic-user",
                                    TokenType = UserTokenType.UserName,
                                    SecurityPolicyUri = SecurityPolicies.None
                                }
                            ];
                        },
                        configureChannel: channel =>
                        {
                            channel.Mock.Setup(value => value.ClientChannelCertificate).Returns(application.RawData);
                            channel.Mock.Setup(value => value.ServerChannelCertificate).Returns(server.RawData);
                        }).ConfigureAwait(false);
                    // This scripted peer tests owner continuity, not token encryption.
                    peer.Channels.Configuration.SecurityConfiguration.AllowUnencryptedUserNameTokens = true;
                    await peer.Owner.UpdateSessionAsync(user, [], peer.Timeout).ConfigureAwait(false);
                    peer.SupportedRecoveryModes =
                    [
                        (int)ServiceOperationRecoveryMode.Session, (int)ServiceOperationRecoveryMode.Owner,
                        (int)ServiceOperationRecoveryMode.Durable
                    ];
                    return new AuthenticatedPeer(peer, identity, options, application, server);
                }
                catch
                {
                    if (peer != null)
                    {
                        await peer.DisposeAsync().ConfigureAwait(false);
                    }
                    application.Dispose();
                    server.Dispose();
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                await Peer.DisposeAsync().ConfigureAwait(false);
                m_application.Dispose();
                m_server.Dispose();
            }

            private readonly Certificate m_application;
            private readonly Certificate m_server;
        }

        private sealed class MappedIdentity(byte[] application, byte[] server, IUserIdentity expected) :
            IRecoverableExecutionIdentityProvider
        {
            internal string? Change { get; set; }

            public ValueTask<RecoverableExecutionIdentity> VerifyAsync(ISession session, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (Change == "denied")
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                if (!session.Identity.TokenHandler.Equals(expected.TokenHandler) ||
                    !session.TransportChannel.ClientChannelCertificate.AsSpan().SequenceEqual(application) ||
                    !session.TransportChannel.ServerChannelCertificate.AsSpan().SequenceEqual(server))
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
                }
                return new ValueTask<RecoverableExecutionIdentity>(new RecoverableExecutionIdentity(
                    Change == "server" ? ByteString.From("other-server"u8) : ByteString.From("logical-server"u8),
                    Change == "application"
                        ? ByteString.From("other-application"u8) : ByteString.From("application-owner"u8),
                    Change == "user" ? ByteString.From("other-user"u8) : ByteString.From("user-owner"u8)));
            }
        }
    }
}
