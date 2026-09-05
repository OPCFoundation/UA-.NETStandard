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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// A registry is a set of blobs a store may fail to hand back, and a delete
    /// policy has to answer for the ones it could not read.
    /// </summary>
    /// <remarks>
    /// Dependency discovery reads every stored document to find the edges. When
    /// one read failed it used to throw out of the whole delete, so a single
    /// corrupt blob anywhere in the registry wedged every policy - including
    /// <c>Force</c>, whose entire purpose is to remove a target when the tidy
    /// answer is unavailable. An unreadable document is now recorded rather than
    /// propagated, and each policy states what it did about it.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotDeletePolicyUnreadableTests
    {
        private const string ModelXid = "/groups/thingmodels/resources/tm-a";
        private const string UnrelatedXid = "/groups/thingdescriptions/resources/td-x";

        private FaultyResourceStore m_faults = null!;
        private WotRegistryService m_registry = null!;

        [SetUp]
        public void SetUp()
        {
            var store = new FaultInjectingRegistryStore();
            m_faults = store.Faults;
            m_registry = new WotRegistryService(store);
        }

        [TearDown]
        public void TearDown()
        {
            m_registry.Dispose();
        }

        /// <summary>
        /// The target's own blob being gone says nothing about whether anything
        /// depends on it, and it is being removed anyway. Every policy still
        /// deletes it.
        /// </summary>
        [TestCase(WoTDeletePolicyEnum.Reject)]
        [TestCase(WoTDeletePolicyEnum.Cascade)]
        [TestCase(WoTDeletePolicyEnum.Force)]
        public async Task AMissingTargetBlobDoesNotStopTheDeleteAsync(
            WoTDeletePolicyEnum policy)
        {
            await RegisterModelAsync().ConfigureAwait(false);
            m_faults.Remove(await ModelDigestAsync().ConfigureAwait(false));

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", policy).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.True);
                Assert.That(ModelResource(), Is.Null);
                Assert.That(
                    result.Unreadable,
                    Is.Empty,
                    "The target is the one document whose readability the delete does " +
                    "not depend on.");
            });
        }

        /// <summary>
        /// Reject means "refuse while something might still be using it". A
        /// document that could not be read might be, so the safety it asserts
        /// has not been established and it refuses.
        /// </summary>
        [Test]
        public async Task RejectRefusesWhileDependencySafetyIsUnknownAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterUnrelatedAsync().ConfigureAwait(false);
            long generation = m_registry.Current.Generation;
            m_faults.Remove(await UnrelatedDigestAsync().ConfigureAwait(false));

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Reject)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(result.Deleted, Is.False);
                Assert.That(result.Dependents, Is.Empty);
                Assert.That(
                    result.Unreadable,
                    Is.EqualTo(new[] { UnrelatedXid }).AsCollection);
                Assert.That(result.Message, Does.Contain("could not be read"));
                Assert.That(
                    m_registry.Current.Generation,
                    Is.EqualTo(generation),
                    "A refusal moves no state.");
                Assert.That(ModelResource(), Is.Not.Null);
            });
        }

        /// <summary>
        /// A blob whose bytes no longer match the digest the registry recorded
        /// is the same fault wearing a different shape, and is handled the same
        /// way.
        /// </summary>
        [Test]
        public async Task ACorruptDigestIsTreatedAsUnreadableAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterUnrelatedAsync().ConfigureAwait(false);
            m_faults.Corrupt(await UnrelatedDigestAsync().ConfigureAwait(false));

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Reject)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(
                    result.Unreadable,
                    Is.EqualTo(new[] { UnrelatedXid }).AsCollection);
            });
        }

        /// <summary>
        /// Cascade takes down only what the delete provably broke. An
        /// unreadable document is not proof of anything, so unloading it would
        /// remove a projection on a guess - it is left alone and reported.
        /// </summary>
        [Test]
        public async Task CascadeLeavesAnUnreadableDocumentAloneAndSaysSoAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterUnrelatedAsync().ConfigureAwait(false);
            m_faults.Remove(await UnrelatedDigestAsync().ConfigureAwait(false));

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Cascade)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.True);
                Assert.That(result.Unloaded, Is.Empty);
                Assert.That(result.Failed, Is.Empty);
                Assert.That(
                    result.Unreadable,
                    Is.EqualTo(new[] { UnrelatedXid }).AsCollection);
                Assert.That(result.Message, Does.Contain("could not be read"));
                Assert.That(UnrelatedResource(), Is.Not.Null);
                Assert.That(UnrelatedResource()!.Enabled, Is.True);
            });
        }

        /// <summary>
        /// Retire keeps the document stored and therefore resolvable, so
        /// nothing that might depend on it loses a reference and an unreadable
        /// document is not this policy's problem.
        /// </summary>
        [Test]
        public async Task RetireKeepsResolvabilityDespiteAnUnreadableDocumentAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterUnrelatedAsync().ConfigureAwait(false);
            m_faults.Remove(await UnrelatedDigestAsync().ConfigureAwait(false));

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Retire)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.False);
                Assert.That(result.Retired, Is.True);
                Assert.That(
                    ModelResource(),
                    Is.Not.Null,
                    "The document stays stored, so anything resolving through it still " +
                    "resolves.");
                Assert.That(
                    ModelResource()!.LoadState,
                    Is.EqualTo(WoTLoadStateEnum.Retired));
                Assert.That(UnrelatedResource()!.Enabled, Is.True);
            });
        }

        /// <summary>
        /// Force removes the target whatever the state of the store, and says
        /// what it broke. It cannot say the unreadable document was unaffected,
        /// so it marks it <c>Failed</c> rather than leaving it advertised as
        /// projected.
        /// </summary>
        [Test]
        public async Task ForceRemovesTheTargetAndMarksUnreadableDocumentsFailedAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterUnrelatedAsync().ConfigureAwait(false);
            m_faults.Remove(await UnrelatedDigestAsync().ConfigureAwait(false));
            var changed = new List<string>();
            m_registry.Changed += (_, e) => changed.AddRange(e.ChangedResourceXids);

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Force)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Deleted, Is.True);
                Assert.That(ModelResource(), Is.Null);
                Assert.That(result.Failed, Is.EqualTo(new[] { UnrelatedXid }).AsCollection);
                Assert.That(
                    result.Unreadable,
                    Is.EqualTo(new[] { UnrelatedXid }).AsCollection);
                Assert.That(
                    UnrelatedResource()!.LoadState,
                    Is.EqualTo(WoTLoadStateEnum.Failed));
                Assert.That(UnrelatedResource()!.Enabled, Is.False);
                Assert.That(
                    UnrelatedResource()!.Diagnostics.Any(
                        d => d.Contains("could not be read", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(changed, Does.Contain(ModelXid));
                Assert.That(
                    changed,
                    Does.Contain(UnrelatedXid),
                    "A document whose state changed is published as changed.");
            });
        }

        /// <summary>
        /// A document Force already marked <c>Failed</c> as a proven dependent
        /// is not marked a second time because it also failed to read.
        /// </summary>
        [Test]
        public async Task ForceMarksAProvenDependentExactlyOnceAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterDependentAsync().ConfigureAwait(false);
            m_faults.Remove(await DependentDigestAsync().ConfigureAwait(false));

            WotDeleteResult result = await m_registry.DeleteResourceAsync(
                WotRegistryGroups.ThingModels, "tm-a", WoTDeletePolicyEnum.Force)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(result.Failed, Is.Unique);
                Assert.That(result.Failed, Has.Length.EqualTo(1));
            });
        }

        /// <summary>
        /// The walk itself reports the two facts separately, so a caller that
        /// is not a delete policy can tell "does not depend on it" from "was
        /// never checked".
        /// </summary>
        [Test]
        public async Task TheWalkSeparatesDependentsFromUnreadableDocumentsAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterUnrelatedAsync().ConfigureAwait(false);
            m_faults.Remove(await UnrelatedDigestAsync().ConfigureAwait(false));

            WotDependentSet found = await WotDependencyGraph.FindDependentsWithFaultsAsync(
                m_registry.Current,
                ModelResource()!,
                m_registry.Bounds.MaxJsonDepth,
                m_registry.ReadContentAsync,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found.Dependents, Is.Empty);
                Assert.That(
                    found.Unreadable, Is.EqualTo(new[] { UnrelatedXid }).AsCollection);
                Assert.That(found.IsComplete, Is.False);
            });
        }

        /// <summary>
        /// A registry every blob of which reads is a complete walk, so the
        /// incompleteness above is a distinction the code draws rather than a
        /// state it always reports.
        /// </summary>
        [Test]
        public async Task AReadableRegistryReportsACompleteWalkAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterUnrelatedAsync().ConfigureAwait(false);

            WotDependentSet found = await WotDependencyGraph.FindDependentsWithFaultsAsync(
                m_registry.Current,
                ModelResource()!,
                m_registry.Bounds.MaxJsonDepth,
                m_registry.ReadContentAsync,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found.IsComplete, Is.True);
                Assert.That(found.Unreadable, Is.Empty);
            });
        }

        /// <summary>
        /// The dependents-only entry point answers the same question with the
        /// faults left off, which is what a caller that has no policy to apply
        /// wants.
        /// </summary>
        [Test]
        public async Task TheDependentsOnlyEntryPointAnswersTheSameWalkAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterDependentAsync().ConfigureAwait(false);

            ImmutableArray<WotDependent> dependents =
                await WotDependencyGraph.FindDependentsAsync(
                    m_registry.Current,
                    ModelResource()!,
                    m_registry.Bounds.MaxJsonDepth,
                    m_registry.ReadContentAsync,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                dependents.Select(d => d.Xid),
                Is.EqualTo(s_dependentOnly).AsCollection);
        }

        /// <summary>
        /// A reader that hands back a null <see cref="ByteString"/> rather than
        /// throwing has still not produced a document, and the walk treats the
        /// two the same way.
        /// </summary>
        [Test]
        public async Task AReaderThatReturnsNothingIsAlsoUnreadableAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);
            await RegisterUnrelatedAsync().ConfigureAwait(false);

            WotDependentSet found = await WotDependencyGraph.FindDependentsWithFaultsAsync(
                m_registry.Current,
                ModelResource()!,
                m_registry.Bounds.MaxJsonDepth,
                (_, _) => new ValueTask<ByteString>(default(ByteString)),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found.IsComplete, Is.False);
                Assert.That(found.Unreadable, Has.Length.EqualTo(2));
                Assert.That(found.Dependents, Is.Empty);
            });
        }

        /// <summary>
        /// A resource with no default version has no bytes to read and is not
        /// therefore unreadable: nothing was ever stored for it.
        /// </summary>
        [Test]
        public async Task AResourceWithNoDefaultVersionIsNotUnreadableAsync()
        {
            await RegisterModelAsync().ConfigureAwait(false);

            WotDependentSet found = await WotDependencyGraph.FindDependentsWithFaultsAsync(
                m_registry.Current,
                ModelResource()!,
                m_registry.Bounds.MaxJsonDepth,
                m_registry.ReadContentAsync,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(found.IsComplete, Is.True);
        }

        private static readonly string[] s_dependentOnly =
            ["/groups/thingdescriptions/resources/td-a"];

        private WotResource? ModelResource()
        {
            return m_registry.Current.FindResource(WotRegistryGroups.ThingModels, "tm-a");
        }

        private WotResource? UnrelatedResource()
        {
            return m_registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "td-x");
        }

        private ValueTask<string> ModelDigestAsync()
        {
            return new ValueTask<string>(ModelResource()!.DefaultVersion!.DigestHex);
        }

        private ValueTask<string> UnrelatedDigestAsync()
        {
            return new ValueTask<string>(UnrelatedResource()!.DefaultVersion!.DigestHex);
        }

        private ValueTask<string> DependentDigestAsync()
        {
            return new ValueTask<string>(
                m_registry.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, "td-a")!.DefaultVersion!.DigestHex);
        }

        private Task RegisterModelAsync()
        {
            return RegisterAsync(
                WotRegistryGroups.ThingModels,
                "tm-a",
                WoTDocumentKindEnum.ThingModel,
                TestMaterialization.Tm("urn:tm-a"));
        }

        private Task RegisterUnrelatedAsync()
        {
            return RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "td-x",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:td-x"));
        }

        private Task RegisterDependentAsync()
        {
            return RegisterAsync(
                WotRegistryGroups.ThingDescriptions,
                "td-a",
                WoTDocumentKindEnum.ThingDescription,
                TestMaterialization.Td("urn:td-a", extendsHrefs: "urn:tm-a"));
        }

        private async Task RegisterAsync(
            string groupId, string resourceId, WoTDocumentKindEnum kind, byte[] content)
        {
            await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = groupId,
                ResourceId = resourceId,
                Kind = kind,
                Content = ByteString.From(content)
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// An in-memory registry store whose resource store can be made to lose
        /// a blob or hand back one that no longer matches its digest.
        /// </summary>
        private sealed class FaultInjectingRegistryStore
            : IWotRegistryStore, IWotRegistryResourceStoreProvider
        {
            public FaultInjectingRegistryStore()
            {
                m_inner = new InMemoryWotRegistryStore();
                Faults = new FaultyResourceStore(m_inner.ResourceStore);
            }

            public FaultyResourceStore Faults { get; }

            public IXRegistryResourceStore ResourceStore => Faults;

            public ValueTask<WotRegistrySnapshot> LoadAsync(
                CancellationToken cancellationToken = default)
            {
                return m_inner.LoadAsync(cancellationToken);
            }

            public ValueTask CommitAsync(
                WotRegistrySnapshot snapshot, CancellationToken cancellationToken = default)
            {
                return m_inner.CommitAsync(snapshot, cancellationToken);
            }

            private readonly InMemoryWotRegistryStore m_inner;
        }

        /// <summary>
        /// A resource store that answers for every key except the ones a test
        /// has removed or corrupted.
        /// </summary>
        private sealed class FaultyResourceStore : IXRegistryResourceStore
        {
            public FaultyResourceStore(IXRegistryResourceStore inner)
            {
                m_inner = inner;
            }

            /// <summary>
            /// Makes the blob under one key vanish, the way a store that lost a
            /// file answers.
            /// </summary>
            public void Remove(string resourceKey)
            {
                m_removed[resourceKey] = true;
            }

            /// <summary>
            /// Makes the blob under one key hand back bytes that no longer
            /// match the digest the registry recorded.
            /// </summary>
            public void Corrupt(string resourceKey)
            {
                m_corrupt[resourceKey] = true;
            }

            public async ValueTask<ByteString> ReadAsync(
                string resourceKey, long offset, int count, CancellationToken ct = default)
            {
                if (m_removed.ContainsKey(resourceKey))
                {
                    return default;
                }
                ByteString read = await m_inner.ReadAsync(resourceKey, offset, count, ct)
                    .ConfigureAwait(false);
                if (!m_corrupt.ContainsKey(resourceKey) || read.IsNull || read.Length == 0)
                {
                    return read;
                }
                byte[] mangled = read.Span.ToArray();
                mangled[0] ^= 0xFF;
                return ByteString.From(mangled);
            }

            public ValueTask WriteAsync(
                string resourceKey, long offset, ByteString data, CancellationToken ct = default)
            {
                return m_inner.WriteAsync(resourceKey, offset, data, ct);
            }

            public ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(resourceKey, ct);
            }

            public ValueTask<long> GetLengthAsync(
                string resourceKey, CancellationToken ct = default)
            {
                return m_inner.GetLengthAsync(resourceKey, ct);
            }

            private readonly IXRegistryResourceStore m_inner;
            private readonly ConcurrentDictionary<string, bool> m_removed = new(StringComparer.Ordinal);
            private readonly ConcurrentDictionary<string, bool> m_corrupt = new(StringComparer.Ordinal);
        }
    }
}
