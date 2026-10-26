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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Deterministic Part 14 reservation, ownership and persistence tests.
    /// </summary>
    [TestFixture]
    public sealed class PubSubCommunicationIdReservationTests
    {
        [Test]
        public async Task ExactBoundsCapacityAndIndependentCategories()
        {
            using var store = CreateStore();
            PubSubCommunicationIdReservation result = await Reserve(store, 32768, 32768).ConfigureAwait(false);
            Assert.That(result.DefaultPublisherId, Is.EqualTo(Variant.From(7u)));
            Assert.That(result.WriterGroupIds.Count, Is.EqualTo(32768));
            Assert.That(result.DataSetWriterIds.Count, Is.EqualTo(32768));
            Assert.That(result.WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
            Assert.That(result.WriterGroupIds[32767], Is.EqualTo(ushort.MaxValue));
            Assert.That(result.DataSetWriterIds, Is.EqualTo(result.WriterGroupIds));
            Assert.That(new HashSet<ushort>([.. result.WriterGroupIds]), Has.Count.EqualTo(32768));
            await AssertStatus(() => Reserve(store, 1, 0).AsTask(), StatusCodes.BadResourceUnavailable)
                .ConfigureAwait(false);
            await AssertStatus(() => Reserve(store, 0, 1).AsTask(), StatusCodes.BadResourceUnavailable)
                .ConfigureAwait(false);
            PubSubCommunicationIdReservation empty = await Reserve(store, 0, 0).ConfigureAwait(false);
            Assert.That(empty.WriterGroupIds.Count, Is.Zero);
            Assert.That(empty.DataSetWriterIds.Count, Is.Zero);
        }

        [Test]
        public async Task DualCategoryFailureDoesNotLeakFirstCategory()
        {
            using var store = CreateStore();
            _ = await Reserve(store, 0, 32768).ConfigureAwait(false);
            await AssertStatus(() => Reserve(store, 1, 1).AsTask(), StatusCodes.BadResourceUnavailable)
                .ConfigureAwait(false);
            PubSubCommunicationIdReservation groups = await Reserve(store, 32768, 0).ConfigureAwait(false);
            Assert.That(groups.WriterGroupIds.Count, Is.EqualTo(32768));
            Assert.That(groups.WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
        }

        [Test]
        public async Task RequestsBeyondExactCapacityFailWithoutReservingAnything()
        {
            using var store = CreateStore();
            await AssertStatus(() => Reserve(store, 32769, 0).AsTask(), StatusCodes.BadResourceUnavailable)
                .ConfigureAwait(false);
            await AssertStatus(() => Reserve(store, 0, ushort.MaxValue).AsTask(), StatusCodes.BadResourceUnavailable)
                .ConfigureAwait(false);
            PubSubCommunicationIdReservation complete = await Reserve(store, 32768, 32768).ConfigureAwait(false);
            Assert.That(complete.WriterGroupIds.Count, Is.EqualTo(32768));
            Assert.That(complete.DataSetWriterIds.Count, Is.EqualTo(32768));
        }

        [TestCase((ushort)0x7FFF, (ushort)0x8000)]
        [TestCase((ushort)0x8000, (ushort)0x8001)]
        [TestCase((ushort)0xFFFF, (ushort)0x8000)]
        public async Task ConfiguredBoundaryIdsNeverEscapeTheReservedRange(ushort configured, ushort expected)
        {
            using var store = CreateStore();
            PubSubCommunicationIdReservation result = await store.ReserveAsync(s_owner, Scope, 1, 1,
                Configuration(configured, configured)).ConfigureAwait(false);
            Assert.That(result.WriterGroupIds[0], Is.EqualTo(expected));
            Assert.That(result.DataSetWriterIds[0], Is.EqualTo(expected));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task ExplicitDefaultPublisherPolicyPreservesEveryValidTypeAndRejectsUnknownScope(int partition)
        {
            Variant publisher = partition switch
            {
                0 => Variant.From((byte)7),
                1 => Variant.From((ushort)7),
                2 => Variant.From(7u),
                3 => Variant.From(7UL),
                _ => Variant.From("7")
            };
            var policy = new PubSubDefaultPublisherIdPolicy([new PubSubDefaultPublisherId(Scope, publisher)]);
            Assert.That(await policy.ResolveAsync(Scope).ConfigureAwait(false), Is.EqualTo(publisher));
            await AssertStatus(async () => _ = await policy.ResolveAsync("urn:unknown").ConfigureAwait(false),
                StatusCodes.BadNotSupported).ConfigureAwait(false);
            using var store = new InMemoryPubSubCommunicationIdReservations(policy);
            PubSubCommunicationIdReservation result = await Reserve(store, 1, 1).ConfigureAwait(false);
            Assert.That(result.DefaultPublisherId, Is.EqualTo(publisher));
        }

        [Test]
        public async Task ExplicitDefaultPublisherPolicyValidatesItsConstructorAndCancellation()
        {
            Assert.Throws<ArgumentException>(() =>
                new PubSubDefaultPublisherIdPolicy([new PubSubDefaultPublisherId(Scope, Variant.From(0u))]));
            Assert.Throws<ArgumentException>(() =>
                new PubSubDefaultPublisherIdPolicy([new PubSubDefaultPublisherId(string.Empty, Variant.From(7u))]));
            Assert.Throws<ArgumentException>(() =>
                new PubSubDefaultPublisherIdPolicy(
                [
                    new PubSubDefaultPublisherId(Scope, Variant.From(7u)),
                    new PubSubDefaultPublisherId(Scope, Variant.From(8u))
                ]));
            var empty = new PubSubDefaultPublisherIdPolicy([]);
            await AssertStatus(async () => _ = await empty.ResolveAsync(Scope).ConfigureAwait(false),
                StatusCodes.BadNotSupported).ConfigureAwait(false);
            var policy = new PubSubDefaultPublisherIdPolicy([new PubSubDefaultPublisherId(Scope, Variant.From(7u))]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                _ = await policy.ResolveAsync(Scope, cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task SessionContextRestoresTheOuterOwnerEvenAfterAFailedNestedMutation()
        {
            Assert.That(PubSubConfigurationSession.Current.IsNull, Is.True);
            _ = await PubSubConfigurationSession.ExecuteAsync(s_owner, async ct =>
            {
                Assert.That(PubSubConfigurationSession.Current, Is.EqualTo(s_owner));
                await Assert.ThrowsAsync<IOException>(async () =>
                    _ = await PubSubConfigurationSession.ExecuteAsync<bool>(s_other,
                        _ => throw new IOException("nested failure"), ct).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(PubSubConfigurationSession.Current, Is.EqualTo(s_owner));
                return true;
            }).ConfigureAwait(false);
            Assert.That(PubSubConfigurationSession.Current.IsNull, Is.True);
        }

        [Test]
        public async Task DisabledConfiguredAndPendingIdsAreExcluded()
        {
            using var store = CreateStore();
            PubSubConfigurationDataType configuration = Configuration(0x8000, 0x8001);
            configuration.Connections[0].Enabled = false;
            PubSubCommunicationIdReservation first = await store.ReserveAsync(
                s_owner, Scope, 2, 2, configuration).ConfigureAwait(false);
            PubSubCommunicationIdReservation second = await store.ReserveAsync(
                s_other, Scope, 1, 1, configuration).ConfigureAwait(false);
            Assert.That(first.WriterGroupIds, Is.EqualTo(new ushort[] { 0x8001, 0x8002 }));
            Assert.That(first.DataSetWriterIds, Is.EqualTo(new ushort[] { 0x8000, 0x8002 }));
            Assert.That(second.WriterGroupIds[0], Is.EqualTo((ushort)0x8003));
            Assert.That(second.DataSetWriterIds[0], Is.EqualTo((ushort)0x8003));
        }

        [Test]
        public async Task TypedPublishersAndTransportScopesDoNotAlias()
        {
            Variant publisher = Variant.From(7u);
            var policy = new Mock<IPubSubDefaultPublisherIdPolicy>();
            policy.Setup(p => p.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<Variant>(publisher));
            using var store = new InMemoryPubSubCommunicationIdReservations(policy.Object);
            PubSubCommunicationIdReservation numeric = await Reserve(store, 1, 1).ConfigureAwait(false);
            publisher = Variant.From("7");
            PubSubCommunicationIdReservation text = await Reserve(store, 1, 1).ConfigureAwait(false);
            publisher = Variant.From(7UL);
            PubSubCommunicationIdReservation wide = await Reserve(store, 1, 1).ConfigureAwait(false);
            PubSubCommunicationIdReservation scope = await store.ReserveAsync(
                s_owner, "urn:other", 1, 1, new PubSubConfigurationDataType()).ConfigureAwait(false);
            Assert.That(numeric.DefaultPublisherId, Is.Not.EqualTo(text.DefaultPublisherId));
            Assert.That(numeric.DefaultPublisherId, Is.Not.EqualTo(wide.DefaultPublisherId));
            Assert.That(text.WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
            Assert.That(wide.WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
            Assert.That(scope.WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
            Assert.That(scope.DataSetWriterIds[0], Is.EqualTo((ushort)0x8000));
        }

        [Test]
        public async Task ForeignAndSessionlessConsumptionIsDeniedAndOwnerCanCommit()
        {
            using var store = CreateStore();
            _ = await Reserve(store, 1, 1).ConfigureAwait(false);
            PubSubConfigurationDataType configuration = Configuration(0x8000, 0x8000);
            bool persisted = false;
            ValueTask Persist(CancellationToken ct)
            {
                persisted = true;
                return default;
            }
            await AssertStatus(
                () => store.PersistAsync(configuration, Persist).AsTask(), StatusCodes.BadUserAccessDenied)
                .ConfigureAwait(false);
            await AssertStatus(() => PubSubConfigurationSession.ExecuteAsync(s_other, async ct =>
            {
                await store.PersistAsync(configuration, Persist, ct).ConfigureAwait(false);
                return true;
            }).AsTask(), StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            Assert.That(persisted, Is.False);
            await PubSubConfigurationSession.ExecuteAsync(s_owner, async ct =>
            {
                await store.PersistAsync(configuration, Persist, ct).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            Assert.That(persisted, Is.True);
            store.CloseSession(s_owner);
            PubSubCommunicationIdReservation next = await store.ReserveAsync(
                s_other, Scope, 1, 1, new PubSubConfigurationDataType()).ConfigureAwait(false);
            Assert.That(next.WriterGroupIds[0], Is.EqualTo((ushort)0x8001));
            Assert.That(next.DataSetWriterIds[0], Is.EqualTo((ushort)0x8001));
        }

        [Test]
        public async Task SuccessfulDeletionMakesConsumedIdsReusable()
        {
            using var store = CreateStore();
            await store.PersistAsync(Configuration(0x8000, 0x8000), _ => default).ConfigureAwait(false);
            await store.PersistAsync(new PubSubConfigurationDataType(), _ => default).ConfigureAwait(false);
            PubSubCommunicationIdReservation reused = await Reserve(store, 1, 1).ConfigureAwait(false);
            Assert.That(reused.WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
            Assert.That(reused.DataSetWriterIds[0], Is.EqualTo((ushort)0x8000));
        }

        [Test]
        public async Task FailedAndCancelledPersistencePreservesOwnership()
        {
            using var store = CreateStore();
            _ = await Reserve(store, 1, 1).ConfigureAwait(false);
            await PubSubConfigurationSession.ExecuteAsync(s_owner, async ct =>
            {
                await Assert.ThrowsAsync<IOException>(async () => await store.PersistAsync(
                    Configuration(0x8000, 0x8000), _ => throw new IOException("save failed"), ct)
                    .ConfigureAwait(false)).ConfigureAwait(false);
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Assert.ThrowsAsync<TaskCanceledException>(async () => await store.PersistAsync(
                    Configuration(0x8000, 0x8000), _ => default, cancelled.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            await AssertStatus(() => store.PersistAsync(
                Configuration(0x8000, 0x8000), _ => default).AsTask(), StatusCodes.BadUserAccessDenied)
                .ConfigureAwait(false);
            await PubSubConfigurationSession.ExecuteAsync(s_owner, async ct =>
            {
                await store.PersistAsync(Configuration(0x8000, 0x8000), _ => default, ct).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task SessionCloseReleasesUnusedIdsAndRejectsClosedOwner()
        {
            using var store = CreateStore();
            _ = await Reserve(store, 1, 1).ConfigureAwait(false);
            store.CloseSession(s_owner);
            store.CloseSession(s_owner);
            await AssertStatus(() => Reserve(store, 1, 1).AsTask(), StatusCodes.BadUserAccessDenied)
                .ConfigureAwait(false);
            PubSubCommunicationIdReservation next = await store.ReserveAsync(
                s_other, Scope, 1, 1, new PubSubConfigurationDataType()).ConfigureAwait(false);
            Assert.That(next.WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
            Assert.That(next.DataSetWriterIds[0], Is.EqualTo((ushort)0x8000));
            bool persisted = false;
            await AssertStatus(() => PubSubConfigurationSession.ExecuteAsync(s_owner, async ct =>
            {
                await store.PersistAsync(new PubSubConfigurationDataType(), _ =>
                {
                    persisted = true;
                    return default;
                }, ct).ConfigureAwait(false);
                return true;
            }).AsTask(), StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            Assert.That(persisted, Is.False);
        }

        [Test]
        public async Task CancellationDuringPersistenceRetainsTheOwnersReservation()
        {
            using var store = CreateStore();
            _ = await Reserve(store, 1, 1).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> pending = PubSubConfigurationSession.ExecuteAsync(s_owner, async ct =>
            {
                await store.PersistAsync(Configuration(0x8000, 0x8000), async token =>
                {
                    entered.SetResult(true);
                    _ = await release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }, ct).ConfigureAwait(false);
                return true;
            }, cancellation.Token).AsTask();
            _ = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            cancellation.Cancel();
            release.SetResult(true);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false))
                .ConfigureAwait(false);
            await AssertStatus(() => store.PersistAsync(Configuration(0x8000, 0x8000), _ => default).AsTask(),
                StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            PubSubCommunicationIdReservation other = await store.ReserveAsync(
                s_other, Scope, 1, 1, new PubSubConfigurationDataType()).ConfigureAwait(false);
            Assert.That(other.WriterGroupIds[0], Is.EqualTo((ushort)0x8001));
            Assert.That(other.DataSetWriterIds[0], Is.EqualTo((ushort)0x8001));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CloseOrCancellationDuringDelayedReserveCannotLeak(bool cancel)
        {
            var result = new TaskCompletionSource<Variant>(TaskCreationOptions.RunContinuationsAsynchronously);
            var policy = new Mock<IPubSubDefaultPublisherIdPolicy>();
            policy.Setup(p => p.ResolveAsync(Scope, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Variant>(result.Task));
            using var store = new InMemoryPubSubCommunicationIdReservations(policy.Object);
            using var cancellation = new CancellationTokenSource();
            Task<PubSubCommunicationIdReservation> pending = store.ReserveAsync(
                s_owner, Scope, 1, 1, new PubSubConfigurationDataType(), cancellation.Token).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            if (cancel)
            {
                cancellation.Cancel();
            }
            else
            {
                store.CloseSession(s_owner);
            }
            result.SetResult(Variant.From(7u));
            if (cancel)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            else
            {
                await AssertStatus(() => pending, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
            }
            PubSubCommunicationIdReservation next = await store.ReserveAsync(
                s_other, Scope, 1, 1, new PubSubConfigurationDataType()).ConfigureAwait(false);
            Assert.That(next.WriterGroupIds[0], Is.EqualTo((ushort)0x8000));
            Assert.That(next.DataSetWriterIds[0], Is.EqualTo((ushort)0x8000));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CloseDuringPersistenceDoesNotReleasePendingIdsEarly(bool fail)
        {
            using var store = CreateStore();
            _ = await Reserve(store, 1, 1).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> pending = PubSubConfigurationSession.ExecuteAsync(s_owner, async ct =>
            {
                await store.PersistAsync(Configuration(0x8000, 0x8000), async token =>
                {
                    entered.SetResult(true);
                    _ = await complete.Task.ConfigureAwait(false);
                    if (fail)
                    {
                        throw new IOException("save failed");
                    }
                }, ct).ConfigureAwait(false);
                return true;
            }).AsTask();
            _ = await entered.Task.ConfigureAwait(false);
            store.CloseSession(s_owner);
            Task<PubSubCommunicationIdReservation> competing = store.ReserveAsync(
                s_other, Scope, 1, 1, new PubSubConfigurationDataType()).AsTask();
            Assert.That(competing.IsCompleted, Is.False);
            complete.SetResult(true);
            if (fail)
            {
                await Assert.ThrowsAsync<IOException>(async () => await pending.ConfigureAwait(false))
                    .ConfigureAwait(false);
            }
            else
            {
                Assert.That(await pending.ConfigureAwait(false), Is.True);
            }
            PubSubCommunicationIdReservation next = await competing.ConfigureAwait(false);
            ushort expected = fail ? (ushort)0x8000 : (ushort)0x8001;
            Assert.That(next.WriterGroupIds[0], Is.EqualTo(expected));
            Assert.That(next.DataSetWriterIds[0], Is.EqualTo(expected));
        }

        [Test]
        public async Task UnsupportedScopeAndSessionlessReserveFailExplicitly()
        {
            using var store = new InMemoryPubSubCommunicationIdReservations();
            await AssertStatus(() => Reserve(store, 1, 1).AsTask(), StatusCodes.BadNotSupported).ConfigureAwait(false);
            await AssertStatus(() => store.ReserveAsync(default, Scope, 1, 1, new PubSubConfigurationDataType())
                .AsTask(), StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public async Task InvalidDefaultPublisherIsNotReturnedAsSuccess(int partition)
        {
            Variant publisher = partition switch
            {
                0 => Variant.From(0u),
                1 => Variant.From(0UL),
                2 => Variant.From(string.Empty),
                3 => Variant.From(-1),
                4 => Variant.From(true),
                6 => Variant.From((byte)0),
                7 => Variant.From((ushort)0),
                _ => Variant.Null
            };
            var policy = new Mock<IPubSubDefaultPublisherIdPolicy>();
            policy.Setup(p => p.ResolveAsync(Scope, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Variant>(publisher));
            using var store = new InMemoryPubSubCommunicationIdReservations(policy.Object);
            await AssertStatus(() => Reserve(store, 1, 1).AsTask(), StatusCodes.BadConfigurationError)
                .ConfigureAwait(false);
        }

        private static InMemoryPubSubCommunicationIdReservations CreateStore()
        {
            var policy = new Mock<IPubSubDefaultPublisherIdPolicy>();
            policy.Setup(p => p.ResolveAsync(Scope, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Variant>(Variant.From(7u)));
            return new InMemoryPubSubCommunicationIdReservations(policy.Object);
        }

        private static ValueTask<PubSubCommunicationIdReservation> Reserve(
            InMemoryPubSubCommunicationIdReservations store, ushort groups, ushort writers)
        {
            return store.ReserveAsync(s_owner, Scope, groups, writers, new PubSubConfigurationDataType());
        }

        private static async Task AssertStatus(Func<Task> action, StatusCode status)
        {
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await action().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(exception!.StatusCode, Is.EqualTo(status));
        }

        private static PubSubConfigurationDataType Configuration(ushort group, ushort writer)
        {
            return new PubSubConfigurationDataType
            {
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        TransportProfileUri = Scope,
                        PublisherId = Variant.From(7u),
                        WriterGroups =
                        [
                            new WriterGroupDataType
                            {
                                WriterGroupId = group,
                                DataSetWriters = [new DataSetWriterDataType { DataSetWriterId = writer }]
                            }
                        ]
                    }
                ]
            };
        }

        private const string Scope = "urn:transport";
        private static readonly NodeId s_owner = new("owner", 1);
        private static readonly NodeId s_other = new("other", 1);
    }
}
