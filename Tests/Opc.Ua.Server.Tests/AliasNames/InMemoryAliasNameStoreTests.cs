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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Coverage tests for <see cref="InMemoryAliasNameStore"/> — including
    /// per-entry status codes, mismatched-input handling,
    /// <c>LastChange</c> monotonicity (Part 17 §6.3.1) and sub-category
    /// cascading on FindAlias (Part 17 §6.3.2).
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class InMemoryAliasNameStoreTests
    {
        private static readonly NodeId s_root = new("Root", 2);
        private static readonly NodeId s_child = new("Child", 2);
        private static readonly ExpandedNodeId s_t1 = new("T1", 2);
        private static readonly ExpandedNodeId s_t2 = new("T2", 2);
        private static readonly ExpandedNodeId s_t3 = new("T3", 2);

        private static InMemoryAliasNameStore CreateStore(
            bool withSubCategory = false,
            AliasNameCapabilities capabilities =
                AliasNameCapabilities.AddAliasesToCategory
                | AliasNameCapabilities.DeleteAliasesFromCategory
                | AliasNameCapabilities.LastChange)
        {
            var subs = withSubCategory
                ? new[]
                {
                    new AliasNameCategoryDescriptor(
                        s_child,
                        new QualifiedName("Child", 2),
                        capabilities)
                }
                : null;
            var root = new AliasNameCategoryDescriptor(
                s_root,
                new QualifiedName("Root", 2),
                capabilities,
                subs);
            return new InMemoryAliasNameStore([root]);
        }

        private static TypeTable EmptyTypeTree()
            => new(new NamespaceTable());

        [Test]
        public async Task AddAndFindRoundTripAsync()
        {
            using var store = CreateStore();
            await store.AddAliasesAsync(s_root,
                [
                    new AliasAddRequest("Temp", s_t1, null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);

            IReadOnlyList<AliasNameDataType> result = await store
                .FindAliasAsync(s_root, "Temp", NodeId.Null, EmptyTypeTree())
                .ConfigureAwait(false);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].AliasName.Name, Is.EqualTo("Temp"));
            Assert.That(result[0].ReferencedNodes.Count, Is.EqualTo(1));
            Assert.That(result[0].ReferencedNodes[0], Is.EqualTo(s_t1));
        }

        [Test]
        public async Task DuplicateAddReturnsBadBrowseNameDuplicatedAsync()
        {
            using var store = CreateStore();
            StatusCode[] first = await store.AddAliasesAsync(s_root,
                [
                    new AliasAddRequest("X", s_t1, null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(first[0].Code, Is.EqualTo(StatusCodes.Good));

            StatusCode[] second = await store.AddAliasesAsync(s_root,
                [
                    new AliasAddRequest("X", s_t1, null,
                        ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(second[0].Code, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public async Task DeleteMissingReturnsBadNotFoundAsync()
        {
            using var store = CreateStore();
            StatusCode[] r = await store.DeleteAliasesAsync(s_root,
                [new AliasDeleteRequest("Ghost", s_t1)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r[0].Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task LastChangeAdvancesMonotonicallyPerBatchAsync()
        {
            using var store = CreateStore();
            Assert.That(store.GetLastChange(s_root), Is.EqualTo((uint?)0));

            await store.AddAliasesAsync(s_root,
                [
                    new AliasAddRequest("A", s_t1, null, ReferenceTypeIds.AliasFor),
                    new AliasAddRequest("B", s_t2, null, ReferenceTypeIds.AliasFor)
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(store.GetLastChange(s_root), Is.EqualTo((uint?)1),
                "Batch with two successes must bump LastChange exactly once.");

            await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("C", s_t3, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(store.GetLastChange(s_root), Is.EqualTo((uint?)2));

            await store.DeleteAliasesAsync(s_root,
                [new AliasDeleteRequest("Missing", s_t1)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(store.GetLastChange(s_root), Is.EqualTo((uint?)2),
                "All-fail batch must NOT bump LastChange.");
        }

        [Test]
        public async Task ChangedEventCarriesCategoryAndVersionAsync()
        {
            using var store = CreateStore();
            AliasStoreChangedEventArgs captured = null;
            store.Changed += (_, e) => captured = e;
            await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("X", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.CategoryId, Is.EqualTo(s_root));
            Assert.That(captured.LastChange, Is.EqualTo(1u));
        }

        private static readonly string[] s_rootAndSub = ["RootA", "SubA"];

        [Test]
        public async Task SubCategoryMatchesCascadeFromParentAsync()
        {
            using var store = CreateStore(withSubCategory: true);
            await store.AddAliasesAsync(s_child,
                [new AliasAddRequest("SubA", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("RootA", s_t2, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);

            // Find on root with wildcard returns BOTH the root- and
            // child-category aliases.
            IReadOnlyList<AliasNameDataType> result = await store
                .FindAliasAsync(s_root, "%", NodeId.Null, EmptyTypeTree())
                .ConfigureAwait(false);
            var names = new HashSet<string>();
            foreach (AliasNameDataType a in result)
            {
                names.Add(a.AliasName.Name!);
            }
            Assert.That(names, Is.EquivalentTo(s_rootAndSub));
        }

        [Test]
        public async Task FindAliasWithEmptyPatternReturnsEmptyAsync()
        {
            using var store = CreateStore();
            await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("A", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<AliasNameDataType> result = await store
                .FindAliasAsync(s_root, "", NodeId.Null, EmptyTypeTree())
                .ConfigureAwait(false);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task FindAliasWithUnknownCategoryReturnsEmptyAsync()
        {
            using var store = CreateStore();
            IReadOnlyList<AliasNameDataType> result = await store
                .FindAliasAsync(new NodeId("Unknown", 2),
                    "%", NodeId.Null, EmptyTypeTree())
                .ConfigureAwait(false);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task AddOnUnsupportedCategoryThrowsBadNotSupportedAsync()
        {
            using var store = CreateStore(
                capabilities: AliasNameCapabilities.None);

            ServiceResultException ex = null;
            try
            {
                await store.AddAliasesAsync(s_root,
                    [new AliasAddRequest("X", s_t1, null, ReferenceTypeIds.AliasFor)],
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                ex = sre;
            }

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task FindAliasVerboseProducesServerUrisAndCategoryIdAsync()
        {
            using var store = CreateStore();
            store.Seed(s_root, "S1", s_t1, serverUri: "urn:other-server",
                referenceTypeId: ReferenceTypeIds.AliasFor);

            IReadOnlyList<AliasNameVerboseDataType> result = await store
                .FindAliasVerboseAsync(s_root, "%", NodeId.Null, EmptyTypeTree())
                .ConfigureAwait(false);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].AliasName.Name, Is.EqualTo("S1"));
            Assert.That(result[0].AliasNameCategoryId, Is.EqualTo(s_root));
            Assert.That(result[0].ServerUris.Count, Is.EqualTo(1));
            Assert.That(result[0].ServerUris[0], Is.EqualTo("urn:other-server"));
        }

        [Test]
        public async Task OwnsCategoryReflectsRegisteredTreeAsync()
        {
            using var store = CreateStore(withSubCategory: true);
            Assert.That(store.OwnsCategory(s_root), Is.True);
            Assert.That(store.OwnsCategory(s_child), Is.True);
            Assert.That(store.OwnsCategory(new NodeId("Other", 2)), Is.False);
            // Suppress async warning on a synchronous fixture
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task DeleteExistingAliasRoundTripsAndBumpsLastChangeAsync()
        {
            using var store = CreateStore();
            await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("X", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(store.GetLastChange(s_root), Is.EqualTo((uint?)1));

            StatusCode[] result = await store.DeleteAliasesAsync(s_root,
                [new AliasDeleteRequest("X", s_t1)],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Code, Is.EqualTo(StatusCodes.Good),
                "Successful delete must return Good.");
            Assert.That(store.GetLastChange(s_root), Is.EqualTo((uint?)2),
                "LastChange must bump exactly once on a successful delete batch.");

            IReadOnlyList<AliasNameDataType> find = await store
                .FindAliasAsync(s_root, "X", NodeId.Null, EmptyTypeTree())
                .ConfigureAwait(false);
            Assert.That(find, Is.Empty,
                "Deleted alias must no longer be findable.");
        }

        [Test]
        public async Task DeleteFiresChangedEventWithCategoryAndVersionAsync()
        {
            using var store = CreateStore();
            await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("X", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);

            var captured = new List<AliasStoreChangedEventArgs>();
            store.Changed += (_, e) => captured.Add(e);

            await store.DeleteAliasesAsync(s_root,
                [new AliasDeleteRequest("X", s_t1)],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1),
                "Successful delete must fire Changed exactly once.");
            Assert.That(captured[0].CategoryId, Is.EqualTo(s_root));
            Assert.That(captured[0].LastChange, Is.EqualTo(2u),
                "Changed event must carry the new LastChange value.");
        }

        [Test]
        public async Task DeleteRemovesEmptyAliasGroupSoReAddSucceedsAsync()
        {
            using var store = CreateStore();
            await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("X", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            await store.DeleteAliasesAsync(s_root,
                [new AliasDeleteRequest("X", s_t1)],
                CancellationToken.None).ConfigureAwait(false);

            // The (name, target) tuple must not survive as a stale BadBrowseNameDuplicated
            // poison entry — re-adding the same alias must succeed.
            StatusCode[] result = await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("X", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(result[0].Code, Is.EqualTo(StatusCodes.Good),
                "Re-adding a deleted (name,target) must succeed (group cleanup).");
        }

        [Test]
        public async Task DeleteAllFailBatchDoesNotBumpLastChangeAsync()
        {
            using var store = CreateStore();
            await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("X", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            uint? before = store.GetLastChange(s_root);

            // All entries fail (wrong target node).
            StatusCode[] result = await store.DeleteAliasesAsync(s_root,
                [new AliasDeleteRequest("X", s_t2)],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result[0].Code, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(store.GetLastChange(s_root), Is.EqualTo(before),
                "All-fail delete batch must NOT bump LastChange.");
        }

        [Test]
        public async Task AddWithEmptyNameReturnsBadBrowseNameInvalidAsync()
        {
            using var store = CreateStore();
            StatusCode[] r = await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("", s_t1, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r[0].Code, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [Test]
        public async Task AddWithNullTargetNodeReturnsBadNodeIdInvalidAsync()
        {
            using var store = CreateStore();
            StatusCode[] r = await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("X", ExpandedNodeId.Null, null, ReferenceTypeIds.AliasFor)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r[0].Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task AddWithNullReferenceTypeReturnsBadReferenceTypeIdInvalidAsync()
        {
            using var store = CreateStore();
            StatusCode[] r = await store.AddAliasesAsync(s_root,
                [new AliasAddRequest("X", s_t1, null, NodeId.Null)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r[0].Code, Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
        }

        [Test]
        public async Task DeleteWithEmptyNameReturnsBadBrowseNameInvalidAsync()
        {
            using var store = CreateStore();
            StatusCode[] r = await store.DeleteAliasesAsync(s_root,
                [new AliasDeleteRequest("", s_t1)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r[0].Code, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [Test]
        public async Task DeleteWithNullTargetNodeReturnsBadNodeIdInvalidAsync()
        {
            using var store = CreateStore();
            StatusCode[] r = await store.DeleteAliasesAsync(s_root,
                [new AliasDeleteRequest("X", ExpandedNodeId.Null)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r[0].Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task AddBatchAccumulatesPerEntryFailuresWithoutBumpAsync()
        {
            using var store = CreateStore();
            // All three rows fail individually — none should bump LastChange.
            StatusCode[] r = await store.AddAliasesAsync(s_root,
                [
                    new AliasAddRequest("", s_t1, null, ReferenceTypeIds.AliasFor),
                    new AliasAddRequest("X", ExpandedNodeId.Null, null, ReferenceTypeIds.AliasFor),
                    new AliasAddRequest("Y", s_t1, null, NodeId.Null)
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(r, Has.Length.EqualTo(3));
            Assert.That(r[0].Code, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            Assert.That(r[1].Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(r[2].Code, Is.EqualTo(StatusCodes.BadReferenceTypeIdInvalid));
            Assert.That(store.GetLastChange(s_root), Is.EqualTo((uint?)0),
                "All-fail add batch must NOT bump LastChange.");
        }

        [Test]
        public async Task FindAliasFiltersByReferenceTypeSubtypeViaTypeTreeAsync()
        {
            // Seed an alias whose ReferenceTypeId is a *subtype* of AliasFor
            // (a synthetic 'SpecialAliasFor' from a custom namespace).
            // FindAlias with a filter equal to AliasFor must return the alias
            // because ITypeTable.IsTypeOf(subtype, base) returns true (Part 17
            // §6.3.2 reference-type filter semantics: include subtypes).
            using var store = CreateStore();
            var specialAliasFor = new NodeId("SpecialAliasFor", 2);
            store.Seed(s_root, "Tagged", s_t1, serverUri: null,
                referenceTypeId: specialAliasFor);

            var typeTree = new Mock<ITypeTable>();
            typeTree
                .Setup(t => t.IsTypeOf(specialAliasFor, ReferenceTypeIds.AliasFor))
                .Returns(true);
            // Anything else NOT in the subtype chain returns false.
            typeTree
                .Setup(t => t.IsTypeOf(specialAliasFor, ReferenceTypeIds.Organizes))
                .Returns(false);

            // Filter on the base type — subtype alias should match.
            IReadOnlyList<AliasNameDataType> match = await store
                .FindAliasAsync(s_root, "%", ReferenceTypeIds.AliasFor, typeTree.Object)
                .ConfigureAwait(false);
            Assert.That(match, Has.Count.EqualTo(1),
                "Alias with subtype ReferenceType must match filter on its base type.");

            // Filter on an unrelated type — no match.
            IReadOnlyList<AliasNameDataType> miss = await store
                .FindAliasAsync(s_root, "%", ReferenceTypeIds.Organizes, typeTree.Object)
                .ConfigureAwait(false);
            Assert.That(miss, Is.Empty,
                "Alias with subtype ReferenceType must NOT match filter on an unrelated type.");
        }

        [Test]
        public async Task FindAliasWithReferencesAsFilterMatchesAllAsync()
        {
            using var store = CreateStore();
            await store.AddAliasesAsync(s_root,
                [
                    new AliasAddRequest("A", s_t1, null, ReferenceTypeIds.AliasFor),
                    new AliasAddRequest("B", s_t2, null, ReferenceTypeIds.HasProperty)
                ],
                CancellationToken.None).ConfigureAwait(false);

            IReadOnlyList<AliasNameDataType> result = await store
                .FindAliasAsync(s_root, "%", ReferenceTypeIds.References, EmptyTypeTree())
                .ConfigureAwait(false);
            Assert.That(result, Has.Count.EqualTo(2),
                "Filter == References must short-circuit and match every alias regardless of refType.");
        }
    }
}
