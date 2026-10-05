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
