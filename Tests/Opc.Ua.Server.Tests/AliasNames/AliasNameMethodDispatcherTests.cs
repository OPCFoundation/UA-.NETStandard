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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

// Test fixtures construct short-lived literal arrays inline as method
// arguments; the per-call allocation cost is irrelevant for tests and
// keeping the data adjacent to the assertion improves readability.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Direct coverage tests for the <c>AliasNameMethodDispatcher</c>
    /// static glue layer — argument validation (Part 17 §6.3.4/§6.3.5
    /// parallel-array shape), null-row coercion, and registry
    /// result-to-MethodStateResult conversion.
    /// </summary>
    /// <remarks>
    /// The dispatcher is marked <c>internal</c>; this test fixture lives
    /// in the same assembly via <c>InternalsVisibleTo</c> already in
    /// place for Opc.Ua.Server tests.
    /// </remarks>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameMethodDispatcherTests
    {
        private static readonly NodeId s_categoryId = new("Cat", 2);

        private static (AliasNameStoreRegistry Registry, InMemoryAliasNameStore Store)
            CreateRegistryWithCapableStore()
        {
            var descriptor = new AliasNameCategoryDescriptor(
                s_categoryId,
                new QualifiedName("Cat", 2),
                AliasNameCapabilities.All);
            var store = new InMemoryAliasNameStore([descriptor]);
            var registry = new AliasNameStoreRegistry();
            registry.Register(store);
            return (registry, store);
        }

        [Test]
        public async Task AddAliasesRejectsAliasNamesShorterThanTargetNodesAsync()
        {
            (AliasNameStoreRegistry registry, InMemoryAliasNameStore store) =
                CreateRegistryWithCapableStore();
            using (registry)
            using (store)
            {
                AddAliasesToCategoryMethodStateResult result = await AliasNameMethodDispatcher
                    .AddAliasesAsync(
                        registry,
                        s_categoryId,
                        new string[] { "A" }.ToArrayOf(),
                        new ExpandedNodeId[]
                        {
                            new("T1", 2),
                            new("T2", 2)
                        }.ToArrayOf(),
                        new string[] { "", "" }.ToArrayOf(),
                        ReferenceTypeIds.AliasFor,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult, Is.Not.Null);
                Assert.That(result.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadInvalidArgument),
                    "Per Part 17 §6.3.4 the dispatcher must reject mismatched parallel arrays.");
            }
        }

        [Test]
        public async Task AddAliasesRejectsTargetServersShorterThanAliasNamesAsync()
        {
            (AliasNameStoreRegistry registry, InMemoryAliasNameStore store) =
                CreateRegistryWithCapableStore();
            using (registry)
            using (store)
            {
                AddAliasesToCategoryMethodStateResult result = await AliasNameMethodDispatcher
                    .AddAliasesAsync(
                        registry,
                        s_categoryId,
                        new string[] { "A", "B" }.ToArrayOf(),
                        new ExpandedNodeId[]
                        {
                            new("T1", 2),
                            new("T2", 2)
                        }.ToArrayOf(),
                        new string[] { "" }.ToArrayOf(),
                        ReferenceTypeIds.AliasFor,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadInvalidArgument),
                    "TargetServers shorter than AliasNames must surface as BadInvalidArgument.");
            }
        }

        [Test]
        public async Task DeleteAliasesRejectsMismatchedArraysAsync()
        {
            (AliasNameStoreRegistry registry, InMemoryAliasNameStore store) =
                CreateRegistryWithCapableStore();
            using (registry)
            using (store)
            {
                DeleteAliasesFromCategoryMethodStateResult result = await AliasNameMethodDispatcher
                    .DeleteAliasesAsync(
                        registry,
                        s_categoryId,
                        new string[] { "A", "B" }.ToArrayOf(),
                        new ExpandedNodeId[] { new("T1", 2) }.ToArrayOf(),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadInvalidArgument),
                    "Per Part 17 §6.3.5 the dispatcher must reject mismatched parallel arrays.");
            }
        }

        [Test]
        public async Task AddAliasesCoercesNullAliasNameToEmptyAndReturnsBadBrowseNameInvalidAsync()
        {
            (AliasNameStoreRegistry registry, InMemoryAliasNameStore store) =
                CreateRegistryWithCapableStore();
            using (registry)
            using (store)
            {
                // A null alias name must NOT crash the dispatcher (NRE);
                // it must be coerced to string.Empty and rejected per-row
                // by the store with BadBrowseNameInvalid.
                AddAliasesToCategoryMethodStateResult result = await AliasNameMethodDispatcher
                    .AddAliasesAsync(
                        registry,
                        s_categoryId,
                        new string[] { null! }.ToArrayOf(),
                        new ExpandedNodeId[] { new("T1", 2) }.ToArrayOf(),
                        new string[] { "" }.ToArrayOf(),
                        ReferenceTypeIds.AliasFor,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
                Assert.That(result.ErrorCodes.Count, Is.EqualTo(1));
                Assert.That(result.ErrorCodes[0].Code,
                    Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            }
        }

        [Test]
        public async Task DeleteAliasesCoercesNullAliasNameToEmptyAndReturnsBadBrowseNameInvalidAsync()
        {
            (AliasNameStoreRegistry registry, InMemoryAliasNameStore store) =
                CreateRegistryWithCapableStore();
            using (registry)
            using (store)
            {
                DeleteAliasesFromCategoryMethodStateResult result = await AliasNameMethodDispatcher
                    .DeleteAliasesAsync(
                        registry,
                        s_categoryId,
                        new string[] { null! }.ToArrayOf(),
                        new ExpandedNodeId[] { new("T1", 2) }.ToArrayOf(),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
                Assert.That(result.ErrorCodes.Count, Is.EqualTo(1));
                Assert.That(result.ErrorCodes[0].Code,
                    Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            }
        }

        [Test]
        public async Task FindAliasReturnsEmptyArrayOfWhenNoMatchesAsync()
        {
            (AliasNameStoreRegistry registry, InMemoryAliasNameStore store) =
                CreateRegistryWithCapableStore();
            using (registry)
            using (store)
            {
                FindAliasMethodStateResult result = await AliasNameMethodDispatcher
                    .FindAliasAsync(
                        registry,
                        new TypeTable(new NamespaceTable()),
                        s_categoryId,
                        "Nothing-matches-this",
                        NodeId.Null,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
                Assert.That(result.AliasNodeList.Count, Is.Zero,
                    "Empty result list must round-trip through ToArrayOf without throwing.");
            }
        }

        [Test]
        public async Task FindAliasVerboseReturnsEmptyArrayOfWhenNoMatchesAsync()
        {
            (AliasNameStoreRegistry registry, InMemoryAliasNameStore store) =
                CreateRegistryWithCapableStore();
            using (registry)
            using (store)
            {
                FindAliasVerboseMethodStateResult result = await AliasNameMethodDispatcher
                    .FindAliasVerboseAsync(
                        registry,
                        new TypeTable(new NamespaceTable()),
                        s_categoryId,
                        "Nothing-matches-this",
                        NodeId.Null,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
                Assert.That(result.AliasNodeList.Count, Is.Zero);
            }
        }

        [Test]
        public async Task AddAliasesRoundTripsPerEntryStatusCodesFromStoreAsync()
        {
            (AliasNameStoreRegistry registry, InMemoryAliasNameStore store) =
                CreateRegistryWithCapableStore();
            using (registry)
            using (store)
            {
                AddAliasesToCategoryMethodStateResult result = await AliasNameMethodDispatcher
                    .AddAliasesAsync(
                        registry,
                        s_categoryId,
                        new string[] { "A", "" }.ToArrayOf(),
                        new ExpandedNodeId[]
                        {
                            new("T1", 2),
                            new("T2", 2)
                        }.ToArrayOf(),
                        new string[] { "", "" }.ToArrayOf(),
                        ReferenceTypeIds.AliasFor,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(result.ServiceResult, Is.EqualTo(ServiceResult.Good));
                Assert.That(result.ErrorCodes.Count, Is.EqualTo(2));
                Assert.That(result.ErrorCodes[0].Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.ErrorCodes[1].Code,
                    Is.EqualTo(StatusCodes.BadBrowseNameInvalid),
                    "Per-row store outcomes must be surfaced verbatim through ErrorCodes.");
            }
        }
    }
}
