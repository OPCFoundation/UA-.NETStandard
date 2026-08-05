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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Fault-injection tests for the registry's transactional store commit
    /// contract. Ordinary pre-commit failures and confirmed not-committed outcomes
    /// leave the previous snapshot published. A validated committed-but-uncertain
    /// outcome is published and rethrown, while an indeterminate outcome blocks
    /// mutation until reload.
    /// </summary>
    [TestFixture]
    public sealed class WotRegistryTransactionTests
    {
        private string m_root = null!;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "wot-tx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        private static WotUpsertResourceRequest TdRequest(string resourceId, string id)
        {
            return new()
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td(id)
            };
        }

        [Test]
        public async Task CommitFailureLeavesCurrentUnchangedAndRaisesNoEvent()
        {
            var store = new FaultInjectingWotRegistryStore(new InMemoryWotRegistryStore());
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            // Seed a first, successful mutation so there is a prior generation.
            await service.UpsertResourceAsync(TdRequest("a", "urn:a"));
            long generationBefore = service.Current.Generation;

            int changedCount = 0;
            service.Changed += (_, _) => changedCount++;

            // Arm the injected failure: the next commit throws before it persists.
            store.FailNextCommit = true;
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.UpsertResourceAsync(TdRequest("b", "urn:b")));

            // Current must still be the previous generation: no partial publish.
            Assert.That(service.Current.Generation, Is.EqualTo(generationBefore),
                "A failed commit must not advance the published generation.");
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Null, "A failed commit must not publish the new resource.");
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null, "The prior generation must remain intact after a failed commit.");
            Assert.That(changedCount, Is.Zero,
                "A failed commit must not raise a Changed event.");
        }

        [Test]
        public async Task CommitFailureThenRetryPersistsAndRaisesExactlyOneEvent()
        {
            var store = new FaultInjectingWotRegistryStore(new InMemoryWotRegistryStore());
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            int changedCount = 0;
            service.Changed += (_, _) => changedCount++;

            store.FailNextCommit = true;
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.UpsertResourceAsync(TdRequest("a", "urn:a")));
            Assert.That(changedCount, Is.Zero);

            // Retry after the fault clears: the same mutation now commits and the
            // resource becomes visible with a single change notification.
            store.FailNextCommit = false;
            WotRegistryMutationResult retry = await service.UpsertResourceAsync(
                TdRequest("a", "urn:a"));

            Assert.That(retry.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null);
            Assert.That(changedCount, Is.EqualTo(1),
                "Exactly one Changed event must be raised, only on the successful commit.");
            Assert.That(store.CommitAttempts, Is.EqualTo(2),
                "The retry must re-attempt persistence.");
        }

        [Test]
        public async Task CommitFailureRestartSeesNoPartialData()
        {
            // First service instance persists 'a', then fails to commit 'b'.
            var store = new FaultInjectingWotRegistryStore(new FileWotRegistryStore(m_root));
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(TdRequest("a", "urn:a"));

                store.FailNextCommit = true;
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await service.UpsertResourceAsync(TdRequest("b", "urn:b")));
            }

            // Restart over the same folder with a fresh store/service: only the
            // durably committed 'a' is restored; 'b' was never persisted.
            using var reloaded = new WotRegistryService(new FileWotRegistryStore(m_root));
            await reloaded.InitializeAsync();

            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null, "The committed resource must survive a restart.");
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Null, "A resource whose commit failed must never appear after a restart.");
        }

        [Test]
        public async Task FileStoreLoadReadsOnlyCommittedGenerationIgnoringStagedFiles()
        {
            using (var service = new WotRegistryService(new FileWotRegistryStore(m_root)))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(TdRequest("a", "urn:a"));
            }

            // Simulate a crash after staging but before the atomic manifest switch:
            // an orphan version blob and a temp manifest exist, but manifest.json
            // still points at the committed generation.
            string blobsDir = Path.Combine(m_root, "blobs");
            Directory.CreateDirectory(blobsDir);
            File.WriteAllBytes(
                Path.Combine(blobsDir, new string('e', 40) + ".bin"),
                TestMaterialization.Td("urn:staged"));
            File.WriteAllText(
                Path.Combine(m_root, "manifest.json.tmp-" + Guid.NewGuid().ToString("N")),
                "{ staged, not committed");

            using var reloaded = new WotRegistryService(new FileWotRegistryStore(m_root));
            await reloaded.InitializeAsync();

            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null, "Load must restore the committed generation.");
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "staged"),
                Is.Null, "Load must ignore staged, not-yet-committed data.");
        }

        [Test]
        public async Task ProjectionResultsAreDurablyCommittedAndSurviveRestart()
        {
            string activeVersionId;
            using (var service = new WotRegistryService(new FileWotRegistryStore(m_root)))
            {
                await service.InitializeAsync();
                WotRegistryMutationResult upsert = await service.UpsertResourceAsync(
                    TdRequest("a", "urn:a"));
                activeVersionId = upsert.Resource!.DefaultVersionId!;

                await service.ApplyProjectionResultsAsync(
                [
                    new WotResourceProjection(
                        WotRegistryGroups.ThingDescriptions,
                        "a",
                        WoTLoadStateEnum.Active,
                        activeVersionId,
                        refreshGeneration: 7,
                        materializedNodeCount: 5,
                        rootNodeId: new NodeId(5000, 1),
                        validation: null,
                        diagnostics: [],
                        lastRefreshTime: DateTime.UtcNow)
                ]);
            }

            using var reloaded = new WotRegistryService(new FileWotRegistryStore(m_root));
            await reloaded.InitializeAsync();

            WotResource restored = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a")!;
            Assert.That(restored.LoadState, Is.EqualTo(WoTLoadStateEnum.Active),
                "Projection load state must be durably committed.");
            Assert.That(restored.ActiveVersionId, Is.EqualTo(activeVersionId));
            Assert.That(restored.RefreshGeneration, Is.EqualTo(7u));
            Assert.That(restored.MaterializedNodeCount, Is.EqualTo(5));
            Assert.That(restored.RootNodeId, Is.EqualTo(new NodeId(5000, 1)));
        }

        [Test]
        public async Task InMemoryStoreCommitFailureLoadReturnsPreviousGeneration()
        {
            var store = new FaultInjectingWotRegistryStore(new InMemoryWotRegistryStore());
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(TdRequest("a", "urn:a"));

                store.FailNextCommit = true;
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await service.UpsertResourceAsync(TdRequest("b", "urn:b")));
            }

            // A brand new service over the same in-memory store instance loads the
            // last committed generation only (the failed 'b' commit is absent).
            using var reloaded = new WotRegistryService(store);
            await reloaded.InitializeAsync();
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null);
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Null);
        }

        [Test]
        public async Task ExternalStoreNotCommittedOutcomeLeavesServiceRetryable()
        {
            var store = new OutcomeInjectingWotRegistryStore
            {
                NextOutcome = CommitOutcome.NotCommitted
            };
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            int changedCount = 0;
            service.Changed += (_, _) => changedCount++;

            WotRegistryCommitNotCommittedException error =
                Assert.ThrowsAsync<WotRegistryCommitNotCommittedException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("a", "urn:a")));

            Assert.That(error.IntendedGeneration, Is.EqualTo(1));
            Assert.That(service.Current, Is.SameAs(WotRegistrySnapshot.Empty));
            Assert.That(changedCount, Is.Zero);

            WotRegistryMutationResult retry = await service.UpsertResourceAsync(
                TdRequest("a", "urn:a"));
            Assert.That(retry.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(changedCount, Is.EqualTo(1));
        }

        [Test]
        public void PostSwitchOutcomeConstructorsArePublicForExternalStores()
        {
            Assert.That(
                typeof(WotRegistryCommitNotCommittedException).GetConstructor(
                [
                    typeof(WotRegistrySnapshot),
                    typeof(Exception),
                    typeof(string)
                ]),
                Is.Not.Null);
            Assert.That(
                typeof(WotRegistryCommitDurabilityUncertainException).GetConstructor(
                [
                    typeof(WotRegistrySnapshot),
                    typeof(Exception)
                ]),
                Is.Not.Null);
            Assert.That(
                typeof(WotRegistryCommitIndeterminateException).GetConstructor(
                [
                    typeof(WotRegistrySnapshot),
                    typeof(Exception),
                    typeof(Exception)
                ]),
                Is.Not.Null);
        }

        [Test]
        public async Task ExternalStoreCommittedUncertainOutcomeIsPublishedAndRethrown()
        {
            var store = new OutcomeInjectingWotRegistryStore
            {
                NextOutcome = CommitOutcome.CommittedUncertain
            };
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            int changedCount = 0;
            service.Changed += (_, _) => changedCount++;

            WotRegistryCommitDurabilityUncertainException error =
                Assert.ThrowsAsync<WotRegistryCommitDurabilityUncertainException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("a", "urn:a")));

            Assert.That(service.Current, Is.SameAs(error.CommittedSnapshot));
            Assert.That(service.Current, Is.SameAs(store.Persisted));
            Assert.That(changedCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ExternalStoreIndeterminateOutcomeBlocksUntilReload()
        {
            var store = new OutcomeInjectingWotRegistryStore
            {
                NextOutcome = CommitOutcome.Indeterminate
            };
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            WotRegistryCommitIndeterminateException error =
                Assert.ThrowsAsync<WotRegistryCommitIndeterminateException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("a", "urn:a")));

            Assert.That(error.IntendedGeneration, Is.EqualTo(1));
            Assert.That(service.Current, Is.SameAs(WotRegistrySnapshot.Empty));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.UpsertResourceAsync(
                    TdRequest("b", "urn:b")));

            await service.InitializeAsync();
            Assert.That(service.Current, Is.SameAs(store.Persisted));
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null);
        }

        /// <summary>
        /// An <see cref="IWotRegistryStore"/> decorator that can be armed to throw
        /// on the next <see cref="CommitAsync"/> (before delegating to the inner
        /// store), so a persistence failure can be injected deterministically.
        /// </summary>
        private sealed class FaultInjectingWotRegistryStore : IWotRegistryStore
        {
            public FaultInjectingWotRegistryStore(IWotRegistryStore inner)
            {
                m_inner = inner;
            }

            public bool FailNextCommit { get; set; }

            public int CommitAttempts { get; private set; }

            public ValueTask<WotRegistrySnapshot> LoadAsync(
                CancellationToken cancellationToken = default)
            {
                return m_inner.LoadAsync(cancellationToken);
            }

            public ValueTask CommitAsync(
                WotRegistrySnapshot snapshot, CancellationToken cancellationToken = default)
            {
                CommitAttempts++;
                if (FailNextCommit)
                {
                    throw new InvalidOperationException("Injected commit failure.");
                }
                return m_inner.CommitAsync(snapshot, cancellationToken);
            }

            private readonly IWotRegistryStore m_inner;
        }

        private sealed class OutcomeInjectingWotRegistryStore : IWotRegistryStore
        {
            public CommitOutcome NextOutcome { get; set; }

            public WotRegistrySnapshot Persisted { get; private set; } =
                WotRegistrySnapshot.Empty;

            public ValueTask<WotRegistrySnapshot> LoadAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotRegistrySnapshot>(Persisted);
            }

            public ValueTask CommitAsync(
                WotRegistrySnapshot snapshot,
                CancellationToken cancellationToken = default)
            {
                CommitOutcome outcome = NextOutcome;
                NextOutcome = CommitOutcome.Success;
                var failure = new IOException($"Injected external {outcome} outcome.");
                switch (outcome)
                {
                    case CommitOutcome.NotCommitted:
                        throw new WotRegistryCommitNotCommittedException(
                            snapshot,
                            failure,
                            "external-recovery-manifest");
                    case CommitOutcome.CommittedUncertain:
                        Persisted = snapshot;
                        throw new WotRegistryCommitDurabilityUncertainException(
                            snapshot,
                            failure);
                    case CommitOutcome.Indeterminate:
                        Persisted = snapshot;
                        throw new WotRegistryCommitIndeterminateException(
                            snapshot,
                            failure,
                            new InvalidDataException(
                                "Injected external validation uncertainty."));
                    default:
                        Persisted = snapshot;
                        return default;
                }
            }
        }

        private enum CommitOutcome
        {
            Success,
            NotCommitted,
            CommittedUncertain,
            Indeterminate
        }
    }
}
