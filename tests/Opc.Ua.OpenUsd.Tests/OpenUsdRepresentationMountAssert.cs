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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.OpenUsd.Tests
{
    internal static class OpenUsdRepresentationMountAssert
    {
        public static void AllAreAddIns(ISystemContext context, NodeState owner)
        {
            var children = new List<BaseInstanceState>();
            owner.GetChildren(context, children);

            int representationCount = 0;
            foreach (BaseInstanceState child in children)
            {
                if (child is not OpenUsdRepresentationState representation)
                {
                    continue;
                }

                representationCount++;
                Assert.That(
                    representation.ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasAddIn),
                    $"{representation.BrowseName} is not mounted with HasAddIn.");
            }

            Assert.That(representationCount, Is.GreaterThan(0), "No OpenUSD representations were found.");
        }

        public static async Task AllAreAddInsAsync(
            ISession session,
            IReadOnlyList<OpenUsdConnector.RepresentationInfo> representations,
            CancellationToken cancellationToken = default)
        {
            Assert.That(representations, Is.Not.Empty, "No OpenUSD representations were discovered.");

            foreach (OpenUsdConnector.RepresentationInfo representation in representations)
            {
                ArrayOf<ReferenceDescription> addInOwners = await BrowseOwnersAsync(
                    session,
                    representation.NodeId,
                    ReferenceTypeIds.HasAddIn,
                    cancellationToken).ConfigureAwait(false);
                ArrayOf<ReferenceDescription> componentOwners = await BrowseOwnersAsync(
                    session,
                    representation.NodeId,
                    ReferenceTypeIds.HasComponent,
                    cancellationToken).ConfigureAwait(false);

                Assert.That(
                    addInOwners,
                    Has.Count.EqualTo(1),
                    $"{representation.PrimPath} is not mounted on exactly one owner with HasAddIn.");
                Assert.That(
                    componentOwners,
                    Is.Empty,
                    $"{representation.PrimPath} is mounted with plain HasComponent instead of HasAddIn.");
            }
        }

        private static async Task<ArrayOf<ReferenceDescription>> BrowseOwnersAsync(
            ISession session,
            NodeId representationId,
            NodeId referenceTypeId,
            CancellationToken cancellationToken)
        {
            BrowseResponse response = await session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new()
                    {
                        NodeId = representationId,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = referenceTypeId,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                cancellationToken).ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            return response.Results[0].References;
        }
    }
}
