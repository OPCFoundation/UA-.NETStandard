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
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Validates the real protected-reference gateway independently from a remote issuer.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsSecretReferenceTests
    {
        [Test]
        public async Task MissingWritableStoreRefusesAdmissionBeforeAllocatingAnyReferenceAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(writable: false)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            Assert.That(() => fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Store.Values, Is.Empty);
            Assert.That(fixture.Store.SetTokens, Is.Empty);
        }

        [Test]
        public async Task PairReservationIsAtomicAndDisposalReleasesOnlyUnusedSlotsAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 2)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            McpSecretReference stored;
            using (McpSecretReservation reservation = fixture.Secrets.ReserveIssuance(
                McpCapability.CredentialIssuance, 2))
            {
                Assert.That(() => fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadTooManyOperations));
                stored = await reservation.StoreAsync(ByteString.From([1, 2, 3]), "access-token", "urn:resource",
                    fixture.Clock.GetUtcNow().AddMinutes(1)).ConfigureAwait(false);
            }
            using McpSecretReservation next = fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance);
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(1));
            Assert.That(() => fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations));
            using ISecret secret = await fixture.Secrets.ResolveAsync(
                stored.Reference, "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
            Assert.That(secret.Bytes.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(fixture.Store.Views.Single().Disposed, Is.True);
        }

        [Test]
        public async Task OversizedPairReservationDoesNotConsumeTheRemainingSingleSlotAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 1)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            Assert.That(() => fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance, 2),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations));
            using McpSecretReservation reservation = fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance);
            McpSecretReference reference = await reservation.StoreAsync(
                ByteString.From([7]), "key-credential", "urn:resource", fixture.Clock.GetUtcNow().AddMinutes(1))
                .ConfigureAwait(false);
            Assert.That(reference.Reference, Does.StartWith("issued:"));
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(1));
        }

        [TestCase("urn:Resource")]
        [TestCase("urn:foreign")]
        public async Task IssuedReferenceRequiresExactResourceBeforeProviderLookupAsync(string resource)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            McpSecretReference reference = await IssueAsync(fixture).ConfigureAwait(false);

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Secrets.ResolveAsync(reference.Reference, resource, McpCapability.CredentialIssuance)
                    .ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Store.Views, Is.Empty);
        }

        [Test]
        public async Task ForeignCallerCannotResolveOrRevokeAnotherCallersReferenceAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            McpSecretReference reference;
            using (fixture.Enter())
            {
                reference = await IssueAsync(fixture).ConfigureAwait(false);
            }
            using (fixture.Enter(GdsTestContext.Other))
            {
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Secrets.ResolveAsync(
                        reference.Reference, "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Secrets.RevokeReferenceAsync(reference.Reference).ConfigureAwait(false));
            }
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(1));
            Assert.That(fixture.Store.Removed, Is.Empty);
            Assert.That(fixture.Store.Views, Is.Empty);
        }

        [TestCase("alias:proof", "urn:wrong")]
        [TestCase("alias:PROOF", "urn:resource")]
        [TestCase("alias:missing", "urn:resource")]
        public async Task AliasesRequireExactGrantAndResourceAsync(string reference, string resource)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.Seed("proof", "synthetic-credential");

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Secrets.ResolveAsync(reference, resource, McpCapability.CredentialIssuance)
                    .ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Store.Views, Is.Empty);
        }

        [Test]
        public async Task AllowedAliasReturnsAnOwnedViewAndNeverTransfersProviderDisposalToConsumerAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.Seed("proof", "synthetic-credential");

            ISecret view = await fixture.Secrets.ResolveAsync(
                "alias:proof", "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false);
            Assert.That(Encoding.UTF8.GetString(view.Bytes), Is.EqualTo("synthetic-credential"));
            Assert.That(fixture.Store.Views.Single().Disposed, Is.True);
            view.Dispose();

            Assert.That(view.Bytes.ToArray().All(value => value == 0), Is.True);
            Assert.That(Encoding.UTF8.GetString(fixture.Store.Values.Values.Single()),
                Is.EqualTo("synthetic-credential"), "Disposal must not zero the host provider's stored value.");
        }

        [Test]
        public async Task ExactExpiryRefusesUseAndPurgeReclaimsOnlyExpiredOwnedStorageAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 1)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.Seed("proof", "host-owned");
            McpSecretReference reference = await IssueAsync(fixture).ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1));
            using (ISecret allowed = await fixture.Secrets.ResolveAsync(
                reference.Reference, "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false))
            {
                Assert.That(allowed.Bytes.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
            fixture.Clock.Advance(TimeSpan.FromTicks(1));

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Secrets.ResolveAsync(
                    reference.Reference, "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false));
            await fixture.Secrets.PurgeExpiredAsync().ConfigureAwait(false);
            using McpSecretReservation free = fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance);

            Assert.That(fixture.Store.Values.Keys.Select(key => key.Name), Is.EqualTo(s_hostProofKeys));
            Assert.That(fixture.Store.Removed.Single().Name, Does.StartWith("mcp-"));
        }

        [Test]
        public async Task RevocationWinsAgainstAProviderLookupAlreadyInFlightAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            McpSecretReference reference = await IssueAsync(fixture).ConfigureAwait(false);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Store.AfterGet = async () =>
            {
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
            };
            Task<ISecret> lookup = fixture.Secrets.ResolveAsync(
                reference.Reference, "urn:resource", McpCapability.CredentialIssuance).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await fixture.Secrets.RevokeReferenceAsync(reference.Reference).ConfigureAwait(false);
            release.SetResult();

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lookup.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Store.Views.Single().Disposed, Is.True);
            Assert.That(fixture.Store.Values, Is.Empty);
        }

        [Test]
        public async Task SetCommitThenThrowRemovesOnlyTheGeneratedKeyAndSanitizesPrimaryFailureAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 1)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.Seed("proof", "host-owned");
            fixture.Store.SetFailure = new IOException("sensitive-provider-token-and-path");

            McpSecretPersistenceException? failure = await Assert.ThrowsAsync<McpSecretPersistenceException>(async () =>
                await IssueAsync(fixture).ConfigureAwait(false));

            Assert.That(failure!.ProviderStatus, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(failure.CleanupFailed, Is.False);
            Assert.That(failure.InnerException, Is.Null);
            Assert.That(failure.ToString(), Does.Not.Contain("sensitive-provider"));
            Assert.That(fixture.Store.Values.Keys.Select(key => key.Name), Is.EqualTo(s_hostProofKeys));
            Assert.That(fixture.Store.Removed.Single().Name, Does.StartWith("mcp-"));
            using McpSecretReservation next = fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance);
        }

        [Test]
        public async Task FailedUncertainWriteCleanupRetainsAnUnresolvableRetryableOwnedReferenceAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(maxReferences: 1)
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.SetFailure = new ServiceResultException(StatusCodes.BadTimeout, "primary-secret");
            fixture.Store.RemoveFailure = new IOException("cleanup-secret");
            McpSecretPersistenceException? failure = await Assert.ThrowsAsync<McpSecretPersistenceException>(async () =>
                await IssueAsync(fixture).ConfigureAwait(false));

            Assert.That(failure!.ProviderStatus, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(failure.CleanupFailed, Is.True);
            Assert.That(failure.ToString(), Does.Not.Contain("primary-secret").And.Not.Contain("cleanup-secret"));
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(1));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Secrets.ResolveAsync(failure.Reference.Reference, "urn:resource",
                    McpCapability.CredentialIssuance).ConfigureAwait(false));
            Assert.That(() => fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations));
            fixture.Store.SetFailure = null;
            fixture.Store.RemoveFailure = null;
            await fixture.Secrets.RevokeReferenceAsync(failure.Reference.Reference).ConfigureAwait(false);

            Assert.That(fixture.Store.Values, Is.Empty);
            Assert.That(fixture.Store.Removed, Has.Count.EqualTo(2));
            Assert.That(fixture.Store.Removed[0], Is.EqualTo(fixture.Store.Removed[1]));
            using McpSecretReservation next = fixture.Secrets.ReserveIssuance(McpCapability.CredentialIssuance);
        }

        [Test]
        public async Task RevokeInvalidatesEveryMatchingCredentialBeforeFirstCleanupFailureAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            McpSecretReference first = await IssueAsync(fixture, "credential-a").ConfigureAwait(false);
            McpSecretReference second = await IssueAsync(fixture, "credential-a").ConfigureAwait(false);
            McpSecretReference unrelated = await IssueAsync(fixture, "credential-b").ConfigureAwait(false);
            fixture.Store.RemoveFailure = new IOException("provider-sensitive-cleanup");

            await Assert.ThrowsAsync<McpSecretPersistenceException>(async () =>
                await fixture.Secrets.RevokeCredentialAsync("urn:resource", "credential-a").ConfigureAwait(false));
            foreach (McpSecretReference revoked in new[] { first, second })
            {
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Secrets.ResolveAsync(revoked.Reference, "urn:resource",
                        McpCapability.CredentialIssuance).ConfigureAwait(false));
            }
            using ISecret preserved = await fixture.Secrets.ResolveAsync(unrelated.Reference, "urn:resource",
                McpCapability.CredentialIssuance).ConfigureAwait(false);

            Assert.That(preserved.Bytes.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(fixture.Store.Removed, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ShutdownWaitsForAdmittedWriteAndRemovesItAfterPersistenceCompletesAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Store.AfterSet = async () =>
            {
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
            };
            Task<McpSecretReference> issuance = IssueAsync(fixture);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task shutdown = fixture.Secrets.DisposeAsync().AsTask();

            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(fixture.Store.Removed, Is.Empty);
            release.SetResult();
            McpSecretReference completed = await issuance.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await shutdown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(fixture.Store.Values, Is.Empty);
            Assert.That(fixture.Store.Removed, Has.Count.EqualTo(1));
            Assert.That(() => fixture.Secrets.RequireReference(
                completed.Reference, "urn:resource", McpCapability.CredentialIssuance),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task ProviderLookupFailureDoesNotExposeRawMessageOrInnerExceptionAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.GetFailure = new InvalidOperationException("the password is confidential");

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Secrets.ResolveAsync("alias:proof", "urn:resource", McpCapability.CredentialIssuance)
                    .ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(failure.ToString(), Does.Not.Contain("confidential"));
            Assert.That(failure.InnerException, Is.Null);
        }

        [Test]
        public async Task RuntimeDependencyInvalidatesBeforeARevocationCleanupFailureAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            McpSecretReference reference = await IssueAsync(fixture).ConfigureAwait(false);
            McpSecretDependency dependency = fixture.Secrets.GetDependency(
                reference.Reference, "urn:resource", McpCapability.CredentialIssuance);
            Assert.That(dependency.Reference, Is.EqualTo(reference));
            Assert.That(dependency.Invalidated.IsCompleted, Is.False);
            Assert.That(fixture.Store.Views, Is.Empty);
            fixture.Store.RemoveFailure = new IOException("private provider detail");

            await Assert.ThrowsAsync<McpSecretPersistenceException>(async () =>
                await fixture.Secrets.RevokeReferenceAsync(reference.Reference).ConfigureAwait(false));

            Assert.That(dependency.Invalidated.IsCompletedSuccessfully, Is.True);
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(1));
            Assert.That(() => fixture.Secrets.GetDependency(
                reference.Reference, "urn:resource", McpCapability.CredentialIssuance),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task ExpiringDependencySuppliesExactDeadlineAndPurgeCompletesItsInvalidationSignalAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            McpSecretReference reference = await IssueAsync(fixture).ConfigureAwait(false);
            McpSecretDependency dependency = fixture.Secrets.GetDependency(
                reference.Reference, "urn:resource", McpCapability.CredentialIssuance);

            Assert.That(dependency.Reference.ExpiresAt, Is.EqualTo(fixture.Clock.GetUtcNow().AddMinutes(1)));
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.That(() => fixture.Secrets.RequireReference(
                reference.Reference, "urn:resource", McpCapability.CredentialIssuance),
                Throws.TypeOf<ServiceResultException>());
            await fixture.Secrets.PurgeExpiredAsync().ConfigureAwait(false);

            Assert.That(dependency.Invalidated.IsCompletedSuccessfully, Is.True);
            Assert.That(fixture.Store.Values, Is.Empty);
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("name:with-colon")]
        public async Task AliasConfigurationRejectsInvalidNamesAsync(string alias)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            IMcpOperationPolicy policy = fixture.Services.GetRequiredService<IMcpOperationPolicy>();

            Assert.That(() => new McpSecretReferences(policy, new SecretRegistry(), fixture.Clock,
                [new McpSecretAlias(alias, new SecretIdentifier("name", "store"), "urn:resource")]),
                Throws.InstanceOf<ArgumentException>());
            Assert.That(fixture.Store.Values, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(4097)]
        public async Task ReferenceQuotaConfigurationRejectsOutOfRangeValuesAsync(int count)
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);

            Assert.That(() => new McpSecretReferences(
                fixture.Services.GetRequiredService<IMcpOperationPolicy>(), new SecretRegistry(), fixture.Clock,
                maxIssuedReferences: count), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Issues fixed non-production bytes using the real reservation/persistence path.
        /// </summary>
        private static async Task<McpSecretReference> IssueAsync(
            GdsTestContext fixture, string? credentialId = null)
        {
            return await fixture.Secrets.StoreIssuedAsync(ByteString.From([1, 2, 3]),
                McpCapability.CredentialIssuance, "key-credential", "urn:resource",
                fixture.Clock.GetUtcNow().AddMinutes(1), credentialId).ConfigureAwait(false);
        }

        private static readonly string[] s_hostProofKeys = ["proof"];
    }
}
#endif
