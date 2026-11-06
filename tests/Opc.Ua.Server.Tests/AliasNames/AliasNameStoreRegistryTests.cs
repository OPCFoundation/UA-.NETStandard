/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Coverage tests for <see cref="AliasNameStoreRegistry"/> —
    /// registration, conflict detection, store routing, and dispatch
    /// of <c>BadNotImplemented</c> when no store owns the category.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameStoreRegistryTests
    {
        private static readonly NodeId s_a = new("A", 2);
        private static readonly NodeId s_b = new("B", 2);
        private static readonly string[] s_remoteUris = ["urn:remote"];

        private static InMemoryAliasNameStore Store(NodeId rootId, string name)
        {
            var d = new AliasNameCategoryDescriptor(
                rootId, new QualifiedName(name, 2));
            return new InMemoryAliasNameStore([d]);
        }

        [Test]
        public void RegisterTrackedAndRoutesByCategory()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore storeA = Store(s_a, "A");
            using InMemoryAliasNameStore storeB = Store(s_b, "B");

            registry.Register(storeA);
            registry.Register(storeB);

            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(storeA));
            Assert.That(registry.GetStoreForCategory(s_b), Is.SameAs(storeB));
            Assert.That(registry.GetStoreForCategory(new NodeId("C", 2)),
                Is.Null);
        }

        [Test]
        public void DuplicateRootCategoryIsRejected()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore one = Store(s_a, "A");
            using InMemoryAliasNameStore two = Store(s_a, "A2");
            registry.Register(one);
            Assert.That(() => registry.Register(two),
                Throws.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// A superseded lease cannot release a replacement, even when both use the same backend.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void OwnedRegistrationReplacesOnlyItsOwner(bool sharedStore, bool releasePreviousFirst)
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore original = Store(s_a, "A");
            using InMemoryAliasNameStore distinct = Store(s_a, "A2");
            InMemoryAliasNameStore replacement = sharedStore ? original : distinct;
            Guid owner = Guid.NewGuid();
            using IDisposable first = registry.AcquireRegistration(original, owner);
            using IDisposable second = registry.AcquireRegistration(replacement, owner);

            registry.Unregister(replacement);
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(replacement));
            Assert.That(registry.Stores, Is.EqualTo(new[] { replacement }));
            if (releasePreviousFirst)
            {
                first.Dispose();
                first.Dispose();
                Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(replacement));
            }
            second.Dispose();
            Assert.That(registry.GetStoreForCategory(s_a), Is.Null);
            first.Dispose();
            Assert.That(registry.GetStoreForCategory(s_a), Is.Null);
            Assert.That(registry.Stores, Is.Empty);
        }

        /// <summary>
        /// Independent owners share one backend without duplicating change notifications.
        /// </summary>
        [Test]
        public async System.Threading.Tasks.Task IndependentOwnedClaimsShareOneBackendAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            var descriptor = new AliasNameCategoryDescriptor(
                s_a, new QualifiedName("A", 2), AliasNameCapabilities.All);
            using var store = new InMemoryAliasNameStore([descriptor]);
            using IDisposable first = registry.AcquireRegistration(store, Guid.NewGuid());
            using IDisposable second = registry.AcquireRegistration(store, Guid.NewGuid());
            int changes = 0;
            registry.Changed += (_, _) => changes++;

            first.Dispose();
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(store));
            await store.AddAliasesAsync(s_a,
                [new AliasAddRequest("Live", new ExpandedNodeId("T", 2), null, ReferenceTypeIds.AliasFor)],
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            Assert.That(changes, Is.EqualTo(1));
            second.Dispose();
            Assert.That(registry.GetStoreForCategory(s_a), Is.Null);
            await store.AddAliasesAsync(s_a,
                [new AliasAddRequest("After", new ExpandedNodeId("T2", 2), null, ReferenceTypeIds.AliasFor)],
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            Assert.That(changes, Is.EqualTo(1));
        }

        /// <summary>
        /// Manual registration remains idempotent and independent of leased ownership.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ManualAndOwnedRegistrationsReleaseIndependently(bool manualFirst, bool releaseManualFirst)
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore store = Store(s_a, "A");
            if (manualFirst)
            {
                registry.Register(store);
            }
            using IDisposable owned = registry.AcquireRegistration(store, Guid.NewGuid());
            registry.Register(store);
            registry.Register(store);
            if (releaseManualFirst)
            {
                registry.Unregister(store);
                registry.Unregister(store);
            }
            else
            {
                owned.Dispose();
                owned.Dispose();
            }
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(store));
            Assert.That(registry.Stores, Is.EqualTo(new[] { store }));

            if (releaseManualFirst)
            {
                owned.Dispose();
            }
            else
            {
                registry.Unregister(store);
            }
            Assert.That(registry.GetStoreForCategory(s_a), Is.Null);
            Assert.That(registry.Stores, Is.Empty);
        }

        /// <summary>
        /// Replacing one owner cannot displace another owner's or a manual claim on the same backend.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OwnedReplacementCannotStealAnotherClaim(bool manual)
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore original = Store(s_a, "A");
            using InMemoryAliasNameStore replacement = Store(s_a, "A2");
            Guid owner = Guid.NewGuid();
            using IDisposable first = registry.AcquireRegistration(original, owner);
            using IDisposable other = registry.AcquireRegistration(original, Guid.NewGuid());
            if (manual)
            {
                registry.Register(original);
                other.Dispose();
            }
            Assert.That(() => registry.AcquireRegistration(replacement, owner),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(original));
            Assert.That(registry.Stores, Is.EqualTo(new[] { original }));
            first.Dispose();
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(original));
            if (manual)
            {
                registry.Unregister(original);
            }
            else
            {
                other.Dispose();
            }
            Assert.That(registry.GetStoreForCategory(s_a), Is.Null);
        }

        /// <summary>
        /// A fresh owner cannot use replacement semantics to claim another store's categories.
        /// </summary>
        [Test]
        public void IndependentOwnedOverlapIsRejectedWithoutChangingRouting()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore original = Store(s_a, "A");
            using InMemoryAliasNameStore overlap = Store(s_a, "A2");
            using IDisposable owner = registry.AcquireRegistration(original, Guid.NewGuid());
            Assert.That(() => registry.AcquireRegistration(overlap, Guid.NewGuid()),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => registry.Register(overlap), Throws.TypeOf<InvalidOperationException>());
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(original));
            Assert.That(registry.Stores, Is.EqualTo(new[] { original }));
        }

        /// <summary>
        /// An overlap on another category rejects the whole replacement before changing its current claim.
        /// </summary>
        [Test]
        public void OwnedReplacementValidatesEveryCategoryBeforeChangingClaims()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore original = Store(s_a, "A");
            using InMemoryAliasNameStore independent = Store(s_b, "B");
            using var replacement = new InMemoryAliasNameStore(
            [
                new AliasNameCategoryDescriptor(s_a, new QualifiedName("A2", 2)),
                new AliasNameCategoryDescriptor(s_b, new QualifiedName("B2", 2))
            ]);
            Guid owner = Guid.NewGuid();
            using IDisposable first = registry.AcquireRegistration(original, owner);
            using IDisposable other = registry.AcquireRegistration(independent, Guid.NewGuid());

            Assert.That(() => registry.AcquireRegistration(replacement, owner),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(original));
            Assert.That(registry.GetStoreForCategory(s_b), Is.SameAs(independent));
            Assert.That(registry.Stores, Is.EqualTo(new[] { original, independent }));
            first.Dispose();
            Assert.That(registry.GetStoreForCategory(s_a), Is.Null);
            Assert.That(registry.GetStoreForCategory(s_b), Is.SameAs(independent));
        }

        /// <summary>
        /// A replacement becomes the dispatcher and the sole change source for its categories.
        /// </summary>
        [Test]
        public async System.Threading.Tasks.Task OwnedReplacementChangesDispatchAndNotificationsAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            var descriptor = new AliasNameCategoryDescriptor(
                s_a, new QualifiedName("A", 2), AliasNameCapabilities.All);
            using var original = new InMemoryAliasNameStore([descriptor]);
            using var replacement = new InMemoryAliasNameStore([descriptor]);
            Guid owner = Guid.NewGuid();
            using IDisposable first = registry.AcquireRegistration(original, owner);
            using IDisposable second = registry.AcquireRegistration(replacement, owner);
            int changes = 0;
            registry.Changed += (_, _) => changes++;

            await original.AddAliasesAsync(s_a,
                [new AliasAddRequest("Old", new ExpandedNodeId("T", 2), null, ReferenceTypeIds.AliasFor)],
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            Assert.That(changes, Is.Zero);
            await replacement.AddAliasesAsync(s_a,
                [new AliasAddRequest("New", new ExpandedNodeId("T2", 2), null, ReferenceTypeIds.AliasFor)],
                System.Threading.CancellationToken.None).ConfigureAwait(false);
            Assert.That(changes, Is.EqualTo(1));
            (ServiceResult result, System.Collections.Generic.IReadOnlyList<AliasNameDataType> aliases) =
                await registry.DispatchFindAliasAsync(s_a, "%", NodeId.Null, new TypeTable(new NamespaceTable()))
                    .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(aliases, Has.Count.EqualTo(1));
            Assert.That(aliases[0].AliasName.Name, Is.EqualTo("New"));
        }

        /// <summary>
        /// Invalid claims fail explicitly and releasing a lease after registry disposal is safe.
        /// </summary>
        [Test]
        public void OwnedRegistrationValidatesInputsAndSurvivesRegistryDisposal()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore store = Store(s_a, "A");
            Assert.That(() => registry.AcquireRegistration(null!, Guid.NewGuid()),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => registry.AcquireRegistration(store, Guid.Empty),
                Throws.TypeOf<ArgumentException>());
            using IDisposable claim = registry.AcquireRegistration(store, Guid.NewGuid());
            registry.Dispose();
            Assert.That(claim.Dispose, Throws.Nothing);
            Assert.That(claim.Dispose, Throws.Nothing);
            Assert.That(() => registry.AcquireRegistration(store, Guid.NewGuid()),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async System.Threading.Tasks.Task DispatchBadNotImplementedWhenNoStoreOwnsCategoryAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            (ServiceResult result, _) = await registry
                .DispatchFindAliasAsync(s_a, "%", NodeId.Null,
                    new TypeTable(new NamespaceTable()))
                .ConfigureAwait(false);
            Assert.That(result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotImplemented));
        }

        [Test]
        public void UnregisterRemovesRouting()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore storeA = Store(s_a, "A");
            registry.Register(storeA);
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(storeA));
            registry.Unregister(storeA);
            Assert.That(registry.GetStoreForCategory(s_a), Is.Null);
        }

        [Test]
        public async System.Threading.Tasks.Task RegistryChangedFiresWhenContainedStoreChangesAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            var descriptor = new AliasNameCategoryDescriptor(
                s_a, new QualifiedName("A", 2),
                AliasNameCapabilities.AddAliasesToCategory |
                AliasNameCapabilities.LastChange);
            using var store = new InMemoryAliasNameStore([descriptor]);
            registry.Register(store);

            var captured = new System.Collections.Generic.List<AliasStoreChangedEventArgs>();
            registry.Changed += (_, e) => captured.Add(e);

            await store.AddAliasesAsync(s_a,
                [new AliasAddRequest("X", new ExpandedNodeId("T", 2), null,
                    ReferenceTypeIds.AliasFor)],
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1),
                "Registry.Changed must propagate the contained store's Changed event.");
            Assert.That(captured[0].CategoryId, Is.EqualTo(s_a));
            Assert.That(captured[0].LastChange, Is.EqualTo(1u));
        }

        [Test]
        public async System.Threading.Tasks.Task RegistryChangedStopsAfterUnregisterAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            var descriptor = new AliasNameCategoryDescriptor(
                s_a, new QualifiedName("A", 2),
                AliasNameCapabilities.AddAliasesToCategory |
                AliasNameCapabilities.LastChange);
            using var store = new InMemoryAliasNameStore([descriptor]);
            registry.Register(store);
            registry.Unregister(store);

            int hits = 0;
            registry.Changed += (_, _) => hits++;

            await store.AddAliasesAsync(s_a,
                [new AliasAddRequest("X", new ExpandedNodeId("T", 2), null,
                    ReferenceTypeIds.AliasFor)],
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.That(hits, Is.Zero,
                "Registry.Changed must NOT fire for stores that have been unregistered.");
        }

        [Test]
        public async System.Threading.Tasks.Task DispatchBadNotImplementedAcrossAllVerbsAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            var typeTree = new TypeTable(new NamespaceTable());

            (ServiceResult findResult, _) = await registry
                .DispatchFindAliasAsync(s_a, "%", NodeId.Null, typeTree)
                .ConfigureAwait(false);
            Assert.That(findResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotImplemented));

            (ServiceResult findVerboseResult, _) = await registry
                .DispatchFindAliasVerboseAsync(s_a, "%", NodeId.Null, typeTree)
                .ConfigureAwait(false);
            Assert.That(findVerboseResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotImplemented));

            (ServiceResult addResult, StatusCode[] addCodes) = await registry
                .DispatchAddAliasesAsync(s_a,
                    [new AliasAddRequest("X", new ExpandedNodeId("T", 2), null,
                        ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);
            Assert.That(addResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotImplemented));
            Assert.That(addCodes, Is.Empty);

            (ServiceResult deleteResult, StatusCode[] deleteCodes) = await registry
                .DispatchDeleteAliasesAsync(s_a,
                    [new AliasDeleteRequest("X", new ExpandedNodeId("T", 2))])
                .ConfigureAwait(false);
            Assert.That(deleteResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotImplemented));
            Assert.That(deleteCodes, Is.Empty);
        }

        [Test]
        public async System.Threading.Tasks.Task DispatchAddCatchesServiceResultExceptionAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            // Category WITHOUT AddAliasesToCategory capability — the
            // store throws BadNotSupported which the registry must
            // catch and turn into a ServiceResult.
            using InMemoryAliasNameStore store = Store(s_a, "A");
            registry.Register(store);

            (ServiceResult result, StatusCode[] codes) = await registry
                .DispatchAddAliasesAsync(s_a,
                    [new AliasAddRequest("X", new ExpandedNodeId("T", 2), null,
                        ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotSupported),
                "Service-level errors from the store must surface as the dispatch ServiceResult, not as exceptions.");
            Assert.That(codes, Is.Empty);
        }

        [Test]
        public async System.Threading.Tasks.Task DispatchDeleteCatchesServiceResultExceptionAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore store = Store(s_a, "A");
            registry.Register(store);

            (ServiceResult result, StatusCode[] codes) = await registry
                .DispatchDeleteAliasesAsync(s_a,
                    [new AliasDeleteRequest("X", new ExpandedNodeId("T", 2))])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(codes, Is.Empty);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var registry = new AliasNameStoreRegistry();
            registry.Dispose();
            Assert.That(registry.Dispose, Throws.Nothing,
                "Dispose must be idempotent — the SDK's standard server-shutdown flow disposes node managers twice.");
        }

        [Test]
        public async System.Threading.Tasks.Task ContributorAliasesAreAddedToOwnerResultsAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore owner = Store(s_a, "A");
            using InMemoryAliasNameStore contributor = Store(s_a, "A");
            owner.Seed(s_a, "Own", new ExpandedNodeId("T1", 2), null, ReferenceTypeIds.AliasFor);
            contributor.Seed(s_a, "Aggregated", new ExpandedNodeId("T2", 2), "urn:remote", ReferenceTypeIds.AliasFor);
            registry.Register(owner);
            registry.RegisterContributor(contributor);

            (ServiceResult result, System.Collections.Generic.IReadOnlyList<AliasNameDataType> aliases) = await registry
                .DispatchFindAliasAsync(s_a, "%", NodeId.Null, new TypeTable(new NamespaceTable()))
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(aliases, Has.Count.EqualTo(2));
            Assert.That(aliases[0].AliasName.Name, Is.EqualTo("Own"), "The owner answers first.");
            Assert.That(aliases[1].AliasName.Name, Is.EqualTo("Aggregated"));
            Assert.That(registry.Stores, Is.EqualTo(new[] { owner }),
                "A contributor is not an owning store.");
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(owner));
        }

        [Test]
        public async System.Threading.Tasks.Task ContributorAloneAnswersFindAliasVerboseAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore contributor = Store(s_a, "A");
            contributor.Seed(s_a, "Aggregated", new ExpandedNodeId("T2", 2), "urn:remote", ReferenceTypeIds.AliasFor);
            registry.RegisterContributor(contributor);
            registry.RegisterContributor(contributor);

            (ServiceResult result, System.Collections.Generic.IReadOnlyList<AliasNameVerboseDataType> aliases) =
                await registry
                    .DispatchFindAliasVerboseAsync(s_a, "Agg%", NodeId.Null, new TypeTable(new NamespaceTable()))
                    .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(aliases, Has.Count.EqualTo(1), "Registering a contributor twice is a no-op.");
            Assert.That(aliases[0].ServerUris.ToArray(), Is.EqualTo(s_remoteUris));

            (result, _) = await registry
                .DispatchFindAliasAsync(s_b, "%", NodeId.Null, new TypeTable(new NamespaceTable()))
                .ConfigureAwait(false);
            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotImplemented),
                "A category neither owned nor contributed is still not implemented.");
        }

        [Test]
        public async System.Threading.Tasks.Task ContributorReceivesNoMutationsAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            var descriptor = new AliasNameCategoryDescriptor(
                s_a, new QualifiedName("A", 2), AliasNameCapabilities.AddAliasesToCategory);
            using var contributor = new InMemoryAliasNameStore([descriptor]);
            registry.RegisterContributor(contributor);

            (ServiceResult result, StatusCode[] _) = await registry
                .DispatchAddAliasesAsync(s_a,
                    [new AliasAddRequest("X", new ExpandedNodeId("T", 2), null, ReferenceTypeIds.AliasFor)])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotImplemented));
            (_, System.Collections.Generic.IReadOnlyList<AliasNameDataType> aliases) = await registry
                .DispatchFindAliasAsync(s_a, "%", NodeId.Null, new TypeTable(new NamespaceTable()))
                .ConfigureAwait(false);
            Assert.That(aliases, Is.Empty);
        }

        [Test]
        public async System.Threading.Tasks.Task UnregisterContributorStopsContributionAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore contributor = Store(s_a, "A");
            contributor.Seed(s_a, "Aggregated", new ExpandedNodeId("T2", 2), null, ReferenceTypeIds.AliasFor);
            registry.RegisterContributor(contributor);
            registry.UnregisterContributor(contributor);
            registry.UnregisterContributor(contributor);

            (ServiceResult result, _) = await registry
                .DispatchFindAliasAsync(s_a, "%", NodeId.Null, new TypeTable(new NamespaceTable()))
                .ConfigureAwait(false);
            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotImplemented));
        }
    }
}
