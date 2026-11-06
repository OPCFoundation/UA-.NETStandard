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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Core.Tests.Redundancy
{
    /// <summary>
    /// Tests lease fencing and snapshot publication at the authority boundary.
    /// </summary>
    [TestFixture]
    [Category("Redundancy")]
    [Parallelizable(ParallelScope.All)]
    public sealed class LeasePublicationAuthorityTests
    {
        /// <summary>
        /// Verifies external authority implementations can construct valid contract results.
        /// </summary>
        [Test]
        public void AuthorityResultsValidateFencingIdentityAndOutcome()
        {
            var token = new LeasePublicationToken("group", "owner", 1);
            var acquired = new LeaseAcquisitionResult(LeaseAcquisitionStatus.Acquired, token);
            var held = new LeaseAcquisitionResult(LeaseAcquisitionStatus.LeaseHeld, null);
            Assert.That(acquired.Token, Is.SameAs(token));
            Assert.That(acquired.Status, Is.EqualTo(LeaseAcquisitionStatus.Acquired));
            Assert.That(held.Token, Is.Null);
            Assert.That(held.Status, Is.EqualTo(LeaseAcquisitionStatus.LeaseHeld));
            Assert.That(token.GroupIdentity, Is.EqualTo("group"));
            Assert.That(token.OwnerIdentity, Is.EqualTo("owner"));
            Assert.That(token.Epoch, Is.EqualTo(1));
            Assert.That(() => new LeasePublicationToken(" ", "owner", 1), Throws.ArgumentException);
            Assert.That(() => new LeasePublicationToken("group", "", 1), Throws.ArgumentException);
            Assert.That(() => new LeasePublicationToken("group", "owner", 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new LeasePublicationToken("group", "owner", -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new LeaseAcquisitionResult(LeaseAcquisitionStatus.Acquired, null),
                Throws.ArgumentException);
            Assert.That(() => new LeaseAcquisitionResult(LeaseAcquisitionStatus.LeaseHeld, token),
                Throws.ArgumentException);
            Assert.That(() => new LeaseAcquisitionResult((LeaseAcquisitionStatus)99, null),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Verifies both owner and epoch independently reject mutations of a current unexpired lease.
        /// </summary>
        [Test]
        public async Task OwnerAndEpochMismatchCannotMutateUnexpiredGroupAsync()
        {
            var authority = new InMemoryLeasePublicationAuthority(new FakeTimeProvider());
            LeasePublicationToken current = await AcquireAsync(authority, "group", "owner").ConfigureAwait(false);
            LeasePublicationToken[] mismatches =
            [
                new LeasePublicationToken(current.GroupIdentity, "different-owner", current.Epoch),
                new LeasePublicationToken(current.GroupIdentity, current.OwnerIdentity, current.Epoch + 1)
            ];
            foreach (LeasePublicationToken mismatch in mismatches)
            {
                Assert.That(await authority.PublishSnapshotAsync(mismatch, "new-key", ByteString.From([1]))
                    .ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.LeaseMismatch));
                Assert.That(await authority.DeleteSnapshotAsync(mismatch, "new-key").ConfigureAwait(false),
                    Is.EqualTo(LeasePublicationStatus.LeaseMismatch));
                Assert.That(await authority.RenewLeaseAsync(mismatch, s_leaseDuration).ConfigureAwait(false),
                    Is.EqualTo(LeaseOperationStatus.LeaseMismatch));
                Assert.That(await authority.ReleaseLeaseAsync(mismatch).ConfigureAwait(false),
                    Is.EqualTo(LeaseOperationStatus.LeaseMismatch));
            }
            (bool found, _) = await authority.ReadSnapshotAsync("group", "new-key").ConfigureAwait(false);
            Assert.That(found, Is.False);
            Assert.That(await authority.PublishSnapshotAsync(current, "new-key", ByteString.From([2]))
                .ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.Published));
        }

        /// <summary>
        /// Verifies renewal extends expiry without acquiring another epoch.
        /// </summary>
        [Test]
        public async Task RenewalExtendsAuthoritativeExpiryWithoutChangingEpochAsync()
        {
            var time = new FakeTimeProvider();
            var authority = new InMemoryLeasePublicationAuthority(time);
            LeasePublicationToken token = await AcquireAsync(authority, "group", "owner").ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(5));
            Assert.That(await authority.RenewLeaseAsync(token, s_leaseDuration).ConfigureAwait(false),
                Is.EqualTo(LeaseOperationStatus.Succeeded));
            time.Advance(TimeSpan.FromSeconds(5));
            Assert.That(await authority.PublishSnapshotAsync(token, "key", ByteString.From([1]))
                .ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.Published));
            time.Advance(TimeSpan.FromSeconds(5));
            Assert.That(await authority.PublishSnapshotAsync(token, "other-key", ByteString.From([2]))
                .ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.LeaseExpired));
            (bool found, _) = await authority.ReadSnapshotAsync("group", "other-key").ConfigureAwait(false);
            Assert.That(found, Is.False);
        }

        /// <summary>
        /// Verifies an unexpired lease blocks acquisition and epochs advance across release and expiry.
        /// </summary>
        [Test]
        public async Task AcquireRenewReleaseAndTakeoverPreserveFencingEpochAsync()
        {
            var authority = new InMemoryLeasePublicationAuthority(new FakeTimeProvider());
            LeasePublicationToken first = await AcquireAsync(authority, "group", "owner-a").ConfigureAwait(false);

            LeaseAcquisitionResult conflict = await authority.TryAcquireLeaseAsync(
                "group", "owner-b", s_leaseDuration).ConfigureAwait(false);
            Assert.That(conflict.Status, Is.EqualTo(LeaseAcquisitionStatus.LeaseHeld));
            Assert.That(conflict.Token, Is.Null);
            Assert.That(await authority.RenewLeaseAsync(first, s_leaseDuration).ConfigureAwait(false),
                Is.EqualTo(LeaseOperationStatus.Succeeded));
            Assert.That(await authority.ReleaseLeaseAsync(first).ConfigureAwait(false),
                Is.EqualTo(LeaseOperationStatus.Succeeded));

            LeasePublicationToken second = await AcquireAsync(authority, "group", "owner-b").ConfigureAwait(false);
            Assert.That(second.Epoch, Is.EqualTo(first.Epoch + 1));
            Assert.That(await authority.ReleaseLeaseAsync(first).ConfigureAwait(false),
                Is.EqualTo(LeaseOperationStatus.LeaseMismatch));
            Assert.That(await authority.RenewLeaseAsync(first, s_leaseDuration).ConfigureAwait(false),
                Is.EqualTo(LeaseOperationStatus.LeaseMismatch));
            Assert.That(await authority.ReleaseLeaseAsync(second).ConfigureAwait(false),
                Is.EqualTo(LeaseOperationStatus.Succeeded));

            LeasePublicationToken third = await AcquireAsync(authority, "group", "owner-a").ConfigureAwait(false);
            Assert.That(third.Epoch, Is.EqualTo(second.Epoch + 1));
        }

        /// <summary>
        /// Verifies lease expiry immediately before, at and after the authoritative duration boundary.
        /// </summary>
        [Test]
        public async Task LeaseExpiresAtExactDurationAndTakeoverAdvancesEpochAsync()
        {
            var time = new FakeTimeProvider();
            var authority = new InMemoryLeasePublicationAuthority(time);
            LeasePublicationToken first = await AcquireAsync(authority, "group", "owner-a").ConfigureAwait(false);
            ByteString payload = ByteString.From([1, 2, 3]);

            time.Advance(s_leaseDuration - TimeSpan.FromTicks(1));
            Assert.That(await authority.PublishSnapshotAsync(first, "absent", payload).ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.Published));

            time.Advance(TimeSpan.FromTicks(1));
            Assert.That(await authority.RenewLeaseAsync(first, s_leaseDuration).ConfigureAwait(false),
                Is.EqualTo(LeaseOperationStatus.LeaseExpired));
            Assert.That(await authority.PublishSnapshotAsync(first, "absent", payload).ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.LeaseExpired));
            Assert.That(await authority.TryPublishSnapshotAsync(
                first, "never-created-at-expiry", default, payload).ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.LeaseExpired));
            Assert.That(await authority.TryDeleteSnapshotAsync(first, "absent", payload).ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.LeaseExpired));
            Assert.That(await authority.DeleteSnapshotAsync(first, "absent").ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.LeaseExpired));
            (bool newFound, _) = await authority.ReadSnapshotAsync("group", "never-created-at-expiry")
                .ConfigureAwait(false);
            Assert.That(newFound, Is.False);
            (bool previousFound, ByteString previous) = await authority.ReadSnapshotAsync("group", "absent")
                .ConfigureAwait(false);
            Assert.That(previousFound, Is.True);
            Assert.That(previous.Span.SequenceEqual(payload.Span), Is.True);

            LeasePublicationToken second = await AcquireAsync(authority, "group", "owner-b").ConfigureAwait(false);
            Assert.That(second.Epoch, Is.EqualTo(first.Epoch + 1));
            time.Advance(s_leaseDuration + TimeSpan.FromTicks(1));
            Assert.That(await authority.DeleteSnapshotAsync(second, "absent").ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.LeaseExpired));
        }

        /// <summary>
        /// Verifies a stale group token cannot publish to an absent key and keys remain isolated by group.
        /// </summary>
        [Test]
        public async Task GroupEpochFencesUntouchedKeysAndSeparatesSnapshotScopesAsync()
        {
            var time = new FakeTimeProvider();
            var authority = new InMemoryLeasePublicationAuthority(time);
            LeasePublicationToken groupOne = await AcquireAsync(authority, "group-one", "owner-a")
                .ConfigureAwait(false);
            LeasePublicationToken groupTwo = await AcquireAsync(authority, "group-two", "owner-b")
                .ConfigureAwait(false);
            ByteString firstPayload = ByteString.From([10]);
            ByteString secondPayload = ByteString.From([20]);

            Assert.That(await authority.PublishSnapshotAsync(groupOne, "same-key", firstPayload).ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.Published));
            Assert.That(await authority.PublishSnapshotAsync(groupTwo, "same-key", secondPayload).ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.Published));
            (bool foundOne, ByteString valueOne) = await authority.ReadSnapshotAsync("group-one", "same-key")
                .ConfigureAwait(false);
            (bool foundTwo, ByteString valueTwo) = await authority.ReadSnapshotAsync("group-two", "same-key")
                .ConfigureAwait(false);
            Assert.That(foundOne, Is.True);
            Assert.That(valueOne.Span.SequenceEqual(firstPayload.Span), Is.True);
            Assert.That(foundTwo, Is.True);
            Assert.That(valueTwo.Span.SequenceEqual(secondPayload.Span), Is.True);

            time.Advance(s_leaseDuration);
            LeasePublicationToken takeover = await AcquireAsync(authority, "group-one", "owner-c")
                .ConfigureAwait(false);
            Assert.That(takeover.Epoch, Is.EqualTo(groupOne.Epoch + 1));
            Assert.That(await authority.PublishSnapshotAsync(groupOne, "never-created", firstPayload)
                .ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.LeaseMismatch));
            (bool absent, _) = await authority.ReadSnapshotAsync("group-one", "never-created").ConfigureAwait(false);
            Assert.That(absent, Is.False);
        }

        /// <summary>
        /// Verifies conditional publication, deletion and defensive ownership of payload memory.
        /// </summary>
        [Test]
        public async Task SnapshotCompareExchangeAndDeletionDetachPayloadsAsync()
        {
            var authority = new InMemoryLeasePublicationAuthority(new FakeTimeProvider());
            LeasePublicationToken token = await AcquireAsync(authority, "group", "owner").ConfigureAwait(false);
            byte[] original = [1, 2, 3];
            var wrappedInput = new ByteString(original.AsMemory());

            Assert.That(await authority.TryPublishSnapshotAsync(
                token,
                "key",
                default,
                wrappedInput).ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.Published));
            original[0] = 90;
            (bool found, ByteString firstRead) = await authority.ReadSnapshotAsync("group", "key")
                .ConfigureAwait(false);
            Assert.That(found, Is.True);
            Assert.That(firstRead.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            MemoryMarshal.AsMemory(firstRead.Memory).Span[1] = 91;
            (bool stillFound, ByteString secondRead) = await authority.ReadSnapshotAsync("group", "key")
                .ConfigureAwait(false);
            Assert.That(stillFound, Is.True);
            Assert.That(secondRead.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));

            Assert.That(await authority.TryPublishSnapshotAsync(
                token,
                "key",
                ByteString.From([9]),
                ByteString.From([4])).ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.SnapshotConflict));
            Assert.That(await authority.TryPublishSnapshotAsync(
                token,
                "key",
                secondRead,
                ByteString.From([4, 5])).ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.Published));
            Assert.That(await authority.TryDeleteSnapshotAsync(
                token,
                "key",
                ByteString.From([1, 2, 3])).ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.SnapshotConflict));
            Assert.That(await authority.TryDeleteSnapshotAsync(
                token,
                "key",
                ByteString.From([4, 5])).ConfigureAwait(false), Is.EqualTo(LeasePublicationStatus.Deleted));
            Assert.That(await authority.TryDeleteSnapshotAsync(token, "key", default).ConfigureAwait(false),
                Is.EqualTo(LeasePublicationStatus.Deleted));
            (bool deleted, _) = await authority.ReadSnapshotAsync("group", "key").ConfigureAwait(false);
            Assert.That(deleted, Is.False);
        }

        /// <summary>
        /// Verifies simultaneous acquisitions produce one token and one epoch for a group.
        /// </summary>
        [Test]
        public async Task ConcurrentAcquisitionHasSingleWinnerAsync()
        {
            var authority = new InMemoryLeasePublicationAuthority(new FakeTimeProvider());
            Task<LeaseAcquisitionResult>[] attempts = Enumerable.Range(0, 32)
                .Select(index => Task.Run(async () => await authority.TryAcquireLeaseAsync(
                    "group",
                    $"owner-{index}",
                    s_leaseDuration).ConfigureAwait(false)))
                .ToArray();

            LeaseAcquisitionResult[] results = await Task.WhenAll(attempts).ConfigureAwait(false);

            Assert.That(results.Count(result => result.Status == LeaseAcquisitionStatus.Acquired), Is.EqualTo(1));
            Assert.That(results.Count(result => result.Status == LeaseAcquisitionStatus.LeaseHeld), Is.EqualTo(31));
            Assert.That(results.Count(result => result.Token?.Epoch == 1), Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an expired publication racing takeover is rejected before or after epoch replacement.
        /// </summary>
        [Test]
        public async Task ExpiredPublicationRacingTakeoverCannotCreateSnapshotAsync()
        {
            var time = new FakeTimeProvider();
            var authority = new InMemoryLeasePublicationAuthority(time);
            for (int index = 0; index < 16; index++)
            {
                string group = $"group-{index}";
                LeasePublicationToken stale = await AcquireAsync(authority, group, "owner-a").ConfigureAwait(false);
                time.Advance(s_leaseDuration);
                Task<LeasePublicationStatus> publication = Task.Run(async () =>
                    await authority.PublishSnapshotAsync(stale, "new-key", ByteString.From([1]))
                        .ConfigureAwait(false));
                Task<LeaseAcquisitionResult> acquisition = Task.Run(async () =>
                    await authority.TryAcquireLeaseAsync(group, "owner-b", s_leaseDuration).ConfigureAwait(false));

                await Task.WhenAll(publication, acquisition).ConfigureAwait(false);
                LeasePublicationStatus publicationStatus = await publication.ConfigureAwait(false);
                LeaseAcquisitionResult acquired = await acquisition.ConfigureAwait(false);
                Assert.That(publicationStatus, Is.AnyOf(
                    LeasePublicationStatus.LeaseExpired,
                    LeasePublicationStatus.LeaseMismatch));
                Assert.That(acquired.Status, Is.EqualTo(LeaseAcquisitionStatus.Acquired));
                Assert.That(acquired.Token!.Epoch, Is.EqualTo(stale.Epoch + 1));
                (bool found, _) = await authority.ReadSnapshotAsync(group, "new-key").ConfigureAwait(false);
                Assert.That(found, Is.False);
            }
        }

        /// <summary>
        /// Verifies invalid identities, durations, tokens and null payloads are rejected.
        /// </summary>
        [Test]
        public async Task InvalidInputsAreRejectedAsync()
        {
            var authority = new InMemoryLeasePublicationAuthority(new FakeTimeProvider());
            Assert.That(async () => await authority.TryAcquireLeaseAsync(
                " ", "owner", s_leaseDuration).ConfigureAwait(false), Throws.ArgumentException);
            Assert.That(async () => await authority.TryAcquireLeaseAsync(
                "group", string.Empty, s_leaseDuration).ConfigureAwait(false), Throws.ArgumentException);
            Assert.That(async () => await authority.TryAcquireLeaseAsync(
                "group", "owner", TimeSpan.Zero).ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(async () => await authority.ReadSnapshotAsync(
                "group", " ").ConfigureAwait(false), Throws.ArgumentException);
            Assert.That(async () => await authority.RenewLeaseAsync(
                null!, s_leaseDuration).ConfigureAwait(false), Throws.ArgumentNullException);
            Assert.That(async () => await authority.PublishSnapshotAsync(
                null!, "key", ByteString.From([1])).ConfigureAwait(false), Throws.ArgumentNullException);
            Assert.That(async () => await authority.PublishSnapshotAsync(
                await AcquireAsync(authority, "group", "owner").ConfigureAwait(false),
                "key",
                default).ConfigureAwait(false), Throws.ArgumentException);
            LeasePublicationToken token = await AcquireAsync(authority, "other-group", "owner").ConfigureAwait(false);
            Assert.That(async () => await authority.RenewLeaseAsync(token, TimeSpan.FromTicks(-1))
                .ConfigureAwait(false), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Verifies cancellation prevents every authority operation from changing lease or snapshot state.
        /// </summary>
        [Test]
        public async Task CancellationPreventsAuthorityOperationsAsync()
        {
            var authority = new InMemoryLeasePublicationAuthority(new FakeTimeProvider());
            LeasePublicationToken token = await AcquireAsync(authority, "group", "owner").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            CancellationToken canceled = cancellation.Token;
            ByteString payload = ByteString.From([1]);

            Assert.That(async () => await authority.TryAcquireLeaseAsync(
                "other", "owner", s_leaseDuration, canceled).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await authority.RenewLeaseAsync(
                token, s_leaseDuration, canceled).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await authority.ReleaseLeaseAsync(token, canceled).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await authority.ReadSnapshotAsync("group", "key", canceled).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await authority.PublishSnapshotAsync(token, "key", payload, canceled)
                .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await authority.TryPublishSnapshotAsync(
                token, "key", default, payload, canceled).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await authority.DeleteSnapshotAsync(token, "key", canceled).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(async () => await authority.TryDeleteSnapshotAsync(
                token, "key", default, canceled).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(await authority.RenewLeaseAsync(token, s_leaseDuration).ConfigureAwait(false),
                Is.EqualTo(LeaseOperationStatus.Succeeded));
            (bool found, _) = await authority.ReadSnapshotAsync("group", "key").ConfigureAwait(false);
            Assert.That(found, Is.False);
        }

        /// <summary>
        /// Verifies the in-memory registration resolves one shared authority instance.
        /// </summary>
        [Test]
        public void InMemoryRegistrationSharesOneAuthorityInstance()
        {
            var services = new ServiceCollection();
            services.AddInMemoryLeasePublicationAuthority();
            using ServiceProvider provider = services.BuildServiceProvider();

            ILeasePublicationAuthority first = provider.GetRequiredService<ILeasePublicationAuthority>();
            ILeasePublicationAuthority second = provider.GetRequiredService<ILeasePublicationAuthority>();

            Assert.That(first, Is.SameAs(second));
            Assert.That(first, Is.TypeOf<InMemoryLeasePublicationAuthority>());
        }

        /// <summary>
        /// Acquires a lease and asserts that the operation returned a token.
        /// </summary>
        private static async Task<LeasePublicationToken> AcquireAsync(
            InMemoryLeasePublicationAuthority authority,
            string group,
            string owner)
        {
            LeaseAcquisitionResult result = await authority.TryAcquireLeaseAsync(
                group,
                owner,
                s_leaseDuration).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(LeaseAcquisitionStatus.Acquired));
            return result.Token ?? throw new AssertionException("Successful acquisition did not return a token.");
        }

        /// <summary>The deterministic lease interval.</summary>
        private static readonly TimeSpan s_leaseDuration = TimeSpan.FromSeconds(10);
    }
}
