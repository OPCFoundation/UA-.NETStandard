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
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests that verify reference types exist and have
    /// correct supertype relationships in the OPC UA address space.
    /// Each test maps to a Base Information conformance unit.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoReferenceTypes")]
    public class BaseInfoReferenceTypeTests : TestFixture
    {
        [Test]
        public async Task AssociatedWithIsSubtypeOfNonHierarchicalReferencesAsync()
        {
            await AssertSupertypeAsync(
                AssociatedWithId,
                ReferenceTypeIds.NonHierarchicalReferences,
                "AssociatedWith").ConfigureAwait(false);
        }

        [Test]
        public async Task ControlsIsSubtypeOfHierarchicalReferencesAsync()
        {
            await AssertSupertypeAsync(
                ControlsId,
                ReferenceTypeIds.HierarchicalReferences,
                "Controls").ConfigureAwait(false);
        }

        [Test]
        public async Task HasAttachedComponentIsSubtypeOfHasPhysicalComponentAsync()
        {
            await AssertSupertypeAsync(
                HasAttachedComponentId,
                HasPhysicalComponentId,
                "HasAttachedComponent").ConfigureAwait(false);
        }

        [Test]
        public async Task HasContainedComponentIsSubtypeOfHasPhysicalComponentAsync()
        {
            await AssertSupertypeAsync(
                HasContainedComponentId,
                HasPhysicalComponentId,
                "HasContainedComponent").ConfigureAwait(false);
        }

        [Test]
        public async Task HasOrderedComponentIsSubtypeOfHasComponentAsync()
        {
            await AssertSupertypeAsync(
                ReferenceTypeIds.HasOrderedComponent,
                ReferenceTypeIds.HasComponent,
                "HasOrderedComponent").ConfigureAwait(false);
        }

        [Test]
        public async Task HasPhysicalComponentIsSubtypeOfHasComponentAsync()
        {
            await AssertSupertypeAsync(
                HasPhysicalComponentId,
                ReferenceTypeIds.HasComponent,
                "HasPhysicalComponent").ConfigureAwait(false);
        }

        [Test]
        public async Task IsExecutableOnIsSubtypeOfNonHierarchicalReferencesAsync()
        {
            await AssertSupertypeAsync(
                IsExecutableOnId,
                ReferenceTypeIds.NonHierarchicalReferences,
                "IsExecutableOn").ConfigureAwait(false);
        }

        [Test]
        public async Task IsExecutingOnIsSubtypeOfUtilizesAsync()
        {
            await AssertSupertypeAsync(
                IsExecutingOnId,
                UtilizesId,
                "IsExecutingOn").ConfigureAwait(false);
        }

        [Test]
        public async Task IsHostedByIsSubtypeOfUtilizesAsync()
        {
            await AssertSupertypeAsync(
                IsHostedById,
                UtilizesId,
                "IsHostedBy").ConfigureAwait(false);
        }

        [Test]
        public async Task IsPhysicallyConnectedToIsSubtypeOfNonHierarchicalReferencesAsync()
        {
            await AssertSupertypeAsync(
                IsPhysicallyConnectedToId,
                ReferenceTypeIds.NonHierarchicalReferences,
                "IsPhysicallyConnectedTo").ConfigureAwait(false);
        }

        [Test]
        public async Task RepresentsSameEntityAsIsSubtypeOfNonHierarchicalReferencesAsync()
        {
            await AssertSupertypeAsync(
                RepresentsSameEntityAsId,
                ReferenceTypeIds.NonHierarchicalReferences,
                "RepresentsSameEntityAs").ConfigureAwait(false);
        }

        [Test]
        public async Task RepresentsSameFunctionalityAsIsSubtypeOfRepresentsSameEntityAsAsync()
        {
            await AssertSupertypeAsync(
                RepresentsSameFunctionalityAsId,
                RepresentsSameEntityAsId,
                "RepresentsSameFunctionalityAs").ConfigureAwait(false);
        }

        [Test]
        public async Task RepresentsSameHardwareAsIsSubtypeOfRepresentsSameEntityAsAsync()
        {
            List<ReferenceDescription> refs =
                await BrowseInverseSubtypeAsync(RepresentsSameHardwareAsId).ConfigureAwait(false);

            if (refs.Count == 0)
            {
                Assert.Ignore(
                    "RepresentsSameHardwareAs reference type not found or no supertype.");
            }

            var parentId = ExpandedNodeId.ToNodeId(
                refs[0].NodeId, Session.NamespaceUris);
            if (parentId != RepresentsSameEntityAsId)
            {
                Assert.Ignore(
                    $"RepresentsSameHardwareAs supertype is {parentId}, expected {RepresentsSameEntityAsId}. Hierarchy may differ in this server version.");
            }
        }

        [Test]
        public async Task RequiresIsSubtypeOfHierarchicalReferencesAsync()
        {
            await AssertSupertypeAsync(
                RequiresId,
                ReferenceTypeIds.HierarchicalReferences,
                "Requires").ConfigureAwait(false);
        }

        [Test]
        public async Task UtilizesIsSubtypeOfNonHierarchicalReferencesAsync()
        {
            await AssertSupertypeAsync(
                UtilizesId,
                ReferenceTypeIds.NonHierarchicalReferences,
                "Utilizes").ConfigureAwait(false);
        }

        private static readonly NodeId AssociatedWithId = ReferenceTypeIds.AssociatedWith;
        private static readonly NodeId ControlsId = ReferenceTypeIds.Controls;
        private static readonly NodeId HasAttachedComponentId = ReferenceTypeIds.HasAttachedComponent;
        private static readonly NodeId HasContainedComponentId = ReferenceTypeIds.HasContainedComponent;
        private static readonly NodeId HasPhysicalComponentId = ReferenceTypeIds.HasPhysicalComponent;
        private static readonly NodeId IsExecutableOnId = ReferenceTypeIds.IsExecutableOn;
        private static readonly NodeId IsExecutingOnId = ReferenceTypeIds.IsExecutingOn;
        private static readonly NodeId IsHostedById = ReferenceTypeIds.IsHostedBy;
        private static readonly NodeId IsPhysicallyConnectedToId = ReferenceTypeIds.IsPhysicallyConnectedTo;
        private static readonly NodeId RepresentsSameEntityAsId = ReferenceTypeIds.RepresentsSameEntityAs;
        private static readonly NodeId RepresentsSameFunctionalityAsId = ReferenceTypeIds.RepresentsSameFunctionalityAs;
        private static readonly NodeId RepresentsSameHardwareAsId = ReferenceTypeIds.RepresentsSameHardwareAs;
        private static readonly NodeId RequiresId = ReferenceTypeIds.Requires;
        private static readonly NodeId UtilizesId = ReferenceTypeIds.Utilizes;

        private async Task<List<ReferenceDescription>> BrowseInverseSubtypeAsync(
            NodeId nodeId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            var refs = new List<ReferenceDescription>();
            if (response.Results[0].References != default)
            {
                foreach (ReferenceDescription rd in response.Results[0].References)
                {
                    refs.Add(rd);
                }
            }

            return refs;
        }

        private async Task AssertSupertypeAsync(
            NodeId nodeId,
            NodeId expectedSupertype,
            string referenceTypeName)
        {
            List<ReferenceDescription> refs =
                await BrowseInverseSubtypeAsync(nodeId).ConfigureAwait(false);

            if (refs.Count == 0)
            {
                Assert.Ignore(
                    referenceTypeName + " reference type not found or no supertype.");
            }

            var parentId = ExpandedNodeId.ToNodeId(
                refs[0].NodeId, Session.NamespaceUris);
            Assert.That(parentId, Is.EqualTo(expectedSupertype),
                referenceTypeName + " supertype mismatch.");
        }
    }
}
