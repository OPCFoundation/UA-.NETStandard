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
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using Opc.Ua.Redundancy;
using Opc.Ua.Storage;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Verifies Client checkpoint protection, context binding, conditional commits and local restart recovery.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableCheckpointStoreTests
    {
        [TestCase(ServiceOperationRecoveryMode.Session)]
        [TestCase(ServiceOperationRecoveryMode.Owner)]
        [TestCase(ServiceOperationRecoveryMode.Durable)]
        public async Task ProtectedCheckpointPreservesRecoveryContextWithoutAuthenticationToken(
            ServiceOperationRecoveryMode mode)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            ServiceMessageContext context = CreateContext();
            RecoverableOperationCheckpoint original = CreateCheckpoint(context, mode);

            ByteString version = await store.SaveAsync(original, default, context, default).ConfigureAwait(false);
            (RecoverableOperationCheckpoint restored, ByteString restoredVersion) =
                await store.LoadAsync(original.ClientOperationId, context, default).ConfigureAwait(false);

            Assert.That(restoredVersion, Is.EqualTo(version));
            Assert.That(restored.ClientOperationId, Is.EqualTo(original.ClientOperationId));
            Assert.That(restored.RecoveryMode, Is.EqualTo(mode));
            Assert.That(restored.Identity.Matches(original.Identity), Is.True);
            Assert.That(restored.SessionId, Is.EqualTo(original.SessionId));
            Assert.That(restored.ServiceId, Is.EqualTo(DataTypeIds.ReadRequest));
            Assert.That(restored.ServerToken, Is.EqualTo(original.ServerToken));
            Assert.That(restored.Request.RequestHeader.AuthenticationToken.IsNull, Is.True);
            Assert.That(restored.Request.RequestHeader.RequestHandle, Is.EqualTo(37u));
            Assert.That(restored.Acceptance, Is.EqualTo(RecoverableCheckpointAcceptance.Accepted));
            Assert.That(CoreUtils.IsEqual(restored.OriginalResponse!, original.OriginalResponse!), Is.True);
            Assert.That(restored.NamespaceUris, Is.EqualTo(original.NamespaceUris));
            Assert.That(restored.ServerUris, Is.EqualTo(original.ServerUris));
            Assert.That(version.Span.IndexOf(original.ServerToken.Span), Is.EqualTo(-1));
            Assert.That(Encoding.UTF8.GetString(version.ToArray()),
                Does.Not.Contain("synthetic-original-node").And.Not.Contain("synthetic-authentication-token"));
            Assert.That(await store.GetOperationIdsAsync().ConfigureAwait(false),
                Is.EqualTo(ArrayOf.Wrapped(original.ClientOperationId)));
        }

        [TestCase("bytes")]
        [TestCase("different-key")]
        [TestCase("different-prefix")]
        public async Task TamperingAndContextSubstitutionFailClosed(string alteration)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            ServiceMessageContext context = CreateContext();
            RecoverableOperationCheckpoint original = CreateCheckpoint(context, ServiceOperationRecoveryMode.Owner);
            ByteString version = await store.SaveAsync(original, default, context, default).ConfigureAwait(false);
            Uuid target = alteration == "different-key" ? Uuid.NewUuid() : original.ClientOperationId;
            string prefix = alteration == "different-prefix" ? "another-client/" : "rse-client/";
            if (alteration == "bytes")
            {
                byte[] changed = version.ToArray();
                changed[changed.Length / 2] ^= 1;
                version = ByteString.From(changed);
            }
            await backend.Raw.SetAsync(prefix + target, version).ConfigureAwait(false);
            var reader = new RecoverableOperationCheckpointStore(backend, protector, prefix);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await reader.LoadAsync(target, context, default).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [TestCase("namespace")]
        [TestCase("server-table")]
        public async Task ChangedTypeContextCannotReinterpretSavedPayload(string alteration)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            ServiceMessageContext context = CreateContext();
            RecoverableOperationCheckpoint original = CreateCheckpoint(context, ServiceOperationRecoveryMode.Owner);
            _ = await store.SaveAsync(original, default, context, default).ConfigureAwait(false);
            if (alteration == "namespace")
            {
                context.NamespaceUris.Append("urn:different:type-context");
            }
            else
            {
                context.ServerUris.Update(["urn:different:server"]);
            }
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.LoadAsync(original.ClientOperationId, context, default).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public async Task ConcurrentCheckpointWriterCannotOverwriteAcceptedState()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            ServiceMessageContext context = CreateContext();
            RecoverableOperationCheckpoint original = CreateCheckpoint(context, ServiceOperationRecoveryMode.Owner);
            ByteString version = await store.SaveAsync(original, default, context, default).ConfigureAwait(false);
            RecoverableOperationCheckpoint acknowledged = original with
            {
                Acceptance = RecoverableCheckpointAcceptance.Acknowledged
            };
            _ = await store.SaveAsync(acknowledged, version, context, default).ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.SaveAsync(original, version, context, default).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            (RecoverableOperationCheckpoint loaded, _) =
                await store.LoadAsync(original.ClientOperationId, context, default).ConfigureAwait(false);
            Assert.That(loaded.Acceptance, Is.EqualTo(RecoverableCheckpointAcceptance.Acknowledged));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedPersistenceNeverReportsSuccessEvenWhenCommitMayHaveOccurred(bool afterCommit)
        {
            using var backend = new TestCheckpointStore { FailWriteAt = 1, FailAfterCommit = afterCommit };
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            ServiceMessageContext context = CreateContext();
            RecoverableOperationCheckpoint original = CreateCheckpoint(context, ServiceOperationRecoveryMode.Owner);
            _ = await Assert.ThrowsAsync<IOException>(async () =>
                await store.SaveAsync(original, default, context, default).ConfigureAwait(false)).ConfigureAwait(false);
            if (afterCommit)
            {
                (RecoverableOperationCheckpoint loaded, _) =
                    await store.LoadAsync(original.ClientOperationId, context, default).ConfigureAwait(false);
                Assert.That(loaded.Acceptance, Is.EqualTo(RecoverableCheckpointAcceptance.Accepted));
            }
            else
            {
                ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await store.LoadAsync(original.ClientOperationId, context, default).ConfigureAwait(false))
                    .ConfigureAwait(false))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            }
        }

        [Test]
        public void UnqualifiedAndPlaintextStoresAreRejected()
        {
            using var memory = new InMemorySharedKeyValueStore();
            using var qualified = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            _ = Assert.Throws<ArgumentException>(() => new RecoverableOperationCheckpointStore(memory, protector));
            _ = Assert.Throws<ArgumentException>(() =>
                new RecoverableOperationCheckpointStore(qualified, NullRecordProtector.Instance));
            _ = Assert.Throws<ArgumentException>(() =>
                new RecoverableOperationCheckpointStore(qualified, protector, " "));
            _ = Assert.Throws<ArgumentNullException>(() => new RecoverableOperationCheckpointStore(null!, protector));
            _ = Assert.Throws<ArgumentNullException>(() => new RecoverableOperationCheckpointStore(qualified, null!));
        }

        [Test]
        public async Task UnavailableDurabilityPreventsAnyCheckpointWrite()
        {
            using var backend = new TestCheckpointStore { Available = false };
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            ServiceMessageContext context = CreateContext();
            _ = await Assert.ThrowsAsync<IOException>(async () =>
                await store.SaveAsync(CreateCheckpoint(context, ServiceOperationRecoveryMode.Owner),
                    default, context, default).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(backend.WriteAttempts, Is.Zero);
        }

        [Test]
        public async Task OldUnqualifiedCheckpointProtectionVersionIsRejectedWithoutFallback()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            var id = Uuid.NewUuid();
            string key = "rse-client/" + id;
            ByteString old = protector.Protect(
                RecordProtectionContext.Create("OpcUa.RSE.Client/0.4/v1", key),
                ByteString.From("unqualified-old-checkpoint"u8));
            await backend.Raw.SetAsync(key, old).ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.LoadAsync(id, CreateContext(), default).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(backend.WriteAttempts, Is.Zero);
        }

        [Test]
        public async Task LossOfLinearizableQualificationPreventsAnyCheckpointWrite()
        {
            using var backend = new TestCheckpointStore { Linearizable = false };
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            ServiceMessageContext context = CreateContext();
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.SaveAsync(CreateCheckpoint(context, ServiceOperationRecoveryMode.Owner),
                    default, context, default).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(backend.WriteAttempts, Is.Zero);
        }

        [TestCase("missing-metadata")]
        [TestCase("pending-metadata")]
        [TestCase("missing-response")]
        public async Task IncompleteAcceptedCheckpointCannotAuthorizeAcknowledgement(string malformed)
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var store = new RecoverableOperationCheckpointStore(backend, protector);
            ServiceMessageContext context = CreateContext();
            RecoverableOperationCheckpoint original = CreateCheckpoint(context, ServiceOperationRecoveryMode.Owner);
            if (malformed == "missing-metadata")
            {
                original = original with { Operation = null };
            }
            else if (malformed == "missing-response")
            {
                original = original with { OriginalResponse = null };
            }
            else
            {
                original.Operation!.State = ServiceOperationState.Pending;
            }
            _ = await store.SaveAsync(original, default, context, default).ConfigureAwait(false);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.LoadAsync(original.ClientOperationId, context, default).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public async Task DurableFileCheckpointReopensWithAcceptedOutcomeUnchanged()
        {
            string directory = Path.GetFullPath(Path.Combine("rse04-client-checkpoints", Guid.NewGuid().ToString("N")));
            using AesCbcHmacRecordProtector protector = CreateProtector();
            ServiceMessageContext context = CreateContext();
            RecoverableOperationCheckpoint original = CreateCheckpoint(context, ServiceOperationRecoveryMode.Durable);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
                await using (var first = new FileSharedKeyValueStore(
                    directory, "synthetic-rse-client", protector, 1_048_576))
                {
                    var writer = new RecoverableOperationCheckpointStore(first, protector);
                    _ = await writer.SaveAsync(original, default, context, default).ConfigureAwait(false);
                }
                await using var reopened = new FileSharedKeyValueStore(
                    directory, "synthetic-rse-client", protector, 1_048_576);
                var reader = new RecoverableOperationCheckpointStore(reopened, protector);
                (RecoverableOperationCheckpoint restored, _) =
                    await reader.LoadAsync(original.ClientOperationId, context, default).ConfigureAwait(false);
                Assert.That(restored.Acceptance, Is.EqualTo(RecoverableCheckpointAcceptance.Accepted));
                Assert.That(CoreUtils.IsEqual(restored.OriginalResponse!, original.OriginalResponse!), Is.True);
                Assert.That(restored.ServerToken, Is.EqualTo(original.ServerToken));
                Assert.That(restored.ExecutionAllowance, Is.EqualTo(original.ExecutionAllowance));
                Assert.That(restored.ResultRetentionTime, Is.EqualTo(original.ResultRetentionTime));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public async Task RecoveryProvidersAndCheckpointStoreAreInjectableWithoutImplicitActivation()
        {
            using var backend = new TestCheckpointStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var mapping = new Mock<IRecoverableExecutionIdentityProvider>(MockBehavior.Strict);
            var services = new ServiceCollection();
            services.AddSingleton<ISharedKeyValueStore>(backend);
            services.AddSingleton<IRecordProtector>(protector);
            services.AddSingleton(mapping.Object);
            services.AddRecoverableOperationCheckpoints("injected-client/");
            services.AddRecoverableOperationCheckpoints("must-not-replace/");
            int configurations = 0;
            services.AddExperimentalAsyncServiceClient(provider =>
            {
                configurations++;
                return new RecoverableSessionOptions
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Durable,
                    CheckpointStore = provider.GetRequiredService<RecoverableOperationCheckpointStore>(),
                    IdentityProvider = provider.GetRequiredService<IRecoverableExecutionIdentityProvider>()
                };
            });
            await using ServiceProvider provider = services.BuildServiceProvider();
            AsyncServiceClient client = provider.GetRequiredService<AsyncServiceClient>();
            Assert.That(provider.GetRequiredService<AsyncServiceClient>(), Is.SameAs(client));
            RecoverableOperationCheckpointStore firstStore =
                provider.GetRequiredService<RecoverableOperationCheckpointStore>();
            Assert.That(provider.GetRequiredService<RecoverableOperationCheckpointStore>(), Is.SameAs(firstStore));
            Assert.That(configurations, Is.EqualTo(1));
            Assert.That(backend.WriteAttempts, Is.Zero);
            mapping.VerifyNoOtherCalls();
        }

        [TestCase(0, "server")]
        [TestCase(0, "application")]
        [TestCase(0, "user")]
        [TestCase(4097, "server")]
        [TestCase(4097, "application")]
        [TestCase(4097, "user")]
        public void EmptyOrOversizedIdentityDimensionsFailClosed(int length, string dimension)
        {
            var valid = ByteString.From("mapped-identity"u8);
            var invalid = ByteString.From(new byte[length]);
            ArgumentException failure = Assert.Throws<ArgumentException>(() => new RecoverableExecutionIdentity(
                dimension == "server" ? invalid : valid,
                dimension == "application" ? invalid : valid,
                dimension == "user" ? invalid : valid))!;
            Assert.That(failure.ParamName, Is.EqualTo(dimension switch
            {
                "server" => "logicalServerIdentity",
                "application" => "applicationIdentity",
                _ => "userIdentity"
            }));
        }

        [TestCase(1)]
        [TestCase(4096)]
        public void IdentityKeyBoundaryValuesPreserveAllDimensions(int length)
        {
            var key = ByteString.From(new byte[length]);
            var identity = new RecoverableExecutionIdentity(key, key, key);
            Assert.That(identity.LogicalServerIdentity.Length, Is.EqualTo(length));
            Assert.That(identity.ApplicationIdentity.Length, Is.EqualTo(length));
            Assert.That(identity.UserIdentity.Length, Is.EqualTo(length));
        }

        internal static AesCbcHmacRecordProtector CreateProtector()
        {
            byte[] key = new byte[32];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(key);
            }
            try
            {
                return new AesCbcHmacRecordProtector(key);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        private static ServiceMessageContext CreateContext()
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse);
            context.ServerUris.Update(["urn:synthetic:server"]);
            context.Factory.Builder.AddOpcUaRse().Commit();
            return context;
        }

        private static RecoverableOperationCheckpoint CreateCheckpoint(
            ServiceMessageContext context, ServiceOperationRecoveryMode mode)
        {
            var id = Uuid.NewUuid();
            var token = ByteString.From(Guid.NewGuid().ToByteArray());
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    RequestHandle = 37,
                    AuthenticationToken = new NodeId("synthetic-authentication-token", 0)
                },
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId("synthetic-original-node", 0), AttributeId = Attributes.Value
                    }
                ],
                TimestampsToReturn = TimestampsToReturn.Neither
            };
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader { RequestHandle = 37 },
                Results = [new DataValue(Variant.From(42))]
            };
            var operation = new ServiceOperationInfoDataType
            {
                ClientOperationId = id,
                RecoveryMode = mode,
                ServerToken = token,
                ServiceId = DataTypeIds.ReadRequest,
                State = ServiceOperationState.OutcomeAvailable,
                OutcomeKind = ServiceOperationOutcome.ServiceResponse,
                ExecutionAllowance = 8000,
                ResultRetentionTime = 4000,
                ResultRemaining = 2000
            };
            return new RecoverableOperationCheckpoint(id, mode,
                new RecoverableExecutionIdentity(ByteString.From("logical-server"u8),
                    ByteString.From("application-owner"u8), ByteString.From("user-owner"u8)),
                new NodeId("issuing-session", 0), DataTypeIds.ReadRequest,
                context.NamespaceUris.ToArrayOf(), context.ServerUris.ToArrayOf(), 8000, 4000,
                request, token, operation, response, RecoverableCheckpointAcceptance.Accepted);
        }
    }
}
