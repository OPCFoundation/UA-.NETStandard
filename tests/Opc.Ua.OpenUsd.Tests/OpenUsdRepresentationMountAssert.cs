/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
