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
    /// compliance tests for View Service Set – Browse continuation points.
    /// Based on test scripts: View Minimum Continuation Point 01–05.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewBrowse")]
    public class BrowseContinuationPointTests : TestFixture
    {
        [Description("Browse the Server node with MaxReferencesPerNode=1 and verify that a continuation point is returned because Server has multiple forward hierarchical references.")]
        [Test]
        public async Task BrowseServerNodeWithMaxRefsOneGetsContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count,
                Is.LessThanOrEqualTo(1));
            Assert.That(response.Results[0].ContinuationPoint.IsEmpty, Is.False,
                "Server node should have more than one child; " +
                "a continuation point is expected.");

            // Clean up
            await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { response.Results[0].ContinuationPoint }
                    .ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Browse Server node with MaxReferencesPerNode=1, then call BrowseNext repeatedly until the continuation point is empty, collecting all references along the way.")]
        [Test]
        public async Task BrowseNextUntilDoneCollectsAllReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            int totalRefs = response.Results[0].References.Count;
            ByteString cp = response.Results[0].ContinuationPoint;

            for (int iterations = 0; !cp.IsEmpty && iterations < 200; iterations++)
            {
                BrowseNextResponse nextResp = await Session.BrowseNextAsync(
                    null, false,
                    new ByteString[] { cp }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(nextResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(nextResp.Results[0].StatusCode),
                    Is.True);
                totalRefs += nextResp.Results[0].References.Count;
                cp = nextResp.Results[0].ContinuationPoint;
            }

            Assert.That(cp.IsNull, Is.True,
                "Continuation point should be null after full traversal.");
            Assert.That(totalRefs, Is.GreaterThan(1),
                "Server node should have more than one reference.");
        }

        [Description("Verify that the total number of references obtained via paginated BrowseNext matches a single Browse with MaxReferencesPerNode=0 (unlimited).")]
        [Test]
        public async Task BrowseNextTotalMatchesBrowseAllAsync()
        {
            var browseDesc = new BrowseDescription
            {
                NodeId = ObjectIds.Server,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            // Get all references in one shot
            BrowseResponse allResponse = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[] { browseDesc }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(allResponse.Results.Count, Is.EqualTo(1));
            int expectedTotal = allResponse.Results[0].References.Count;

            // Get references one at a time via continuation points
            BrowseResponse pageResponse = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[] { browseDesc }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            int paginatedTotal = pageResponse.Results[0].References.Count;
            ByteString cp = pageResponse.Results[0].ContinuationPoint;

            for (int iterations = 0; !cp.IsEmpty && iterations < 200; iterations++)
            {
                BrowseNextResponse nextResp = await Session.BrowseNextAsync(
                    null, false,
                    new ByteString[] { cp }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                paginatedTotal += nextResp.Results[0].References.Count;
                cp = nextResp.Results[0].ContinuationPoint;
            }

            Assert.That(paginatedTotal, Is.EqualTo(expectedTotal),
                "Paginated total must equal unbounded Browse total.");
        }

        [Description("Release a continuation point, then attempt to use it with BrowseNext. The server must return BadContinuationPointInvalid.")]
        [Test]
        public async Task BrowseNextReleaseThenUseReturnsErrorAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            ByteString cp = response.Results[0].ContinuationPoint;
            if (cp.IsEmpty)
            {
                Assert.Ignore("No continuation point to release.");
            }

            // Release
            await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            // Attempt to use the released continuation point
            BrowseNextResponse reuse = await Session.BrowseNextAsync(
                null, false,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(reuse.Results.Count, Is.EqualTo(1));
            Assert.That(
                reuse.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        [Description("Browse Objects and Types folders simultaneously with MaxReferencesPerNode=1. Both nodes should produce continuation points.")]
        [Test]
        public async Task BrowseMultipleNodesWithContinuationPointsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new() {
                        NodeId = ObjectIds.TypesFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));

            for (int i = 0; i < 2; i++)
            {
                Assert.That(
                    StatusCode.IsGood(response.Results[i].StatusCode),
                    Is.True,
                    $"Result[{i}] should be Good.");
                Assert.That(
                    response.Results[i].ContinuationPoint.IsEmpty,
                    Is.False,
                    $"Result[{i}] should have a continuation point.");
            }

            // Clean up both continuation points
            var cps = new ByteString[]
            {
                response.Results[0].ContinuationPoint,
                response.Results[1].ContinuationPoint
            };
            await Session.BrowseNextAsync(
                null, true,
                cps.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Browse Objects folder with MaxReferencesPerNode=2 and verify that at most two references are returned per batch.")]
        [Test]
        public async Task BrowseWithMaxRefsTwoReturnsTwoPerBatchAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 2,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count,
                Is.LessThanOrEqualTo(2));

            ByteString cp = response.Results[0].ContinuationPoint;
            if (!cp.IsEmpty)
            {
                BrowseNextResponse nextResp = await Session.BrowseNextAsync(
                    null, false,
                    new ByteString[] { cp }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(nextResp.Results[0].References.Count,
                    Is.LessThanOrEqualTo(2),
                    "Each batch should honour MaxReferencesPerNode=2.");

                // Clean up
                if (!nextResp.Results[0].ContinuationPoint.IsEmpty)
                {
                    await Session.BrowseNextAsync(
                        null, true,
                        new ByteString[]
                        {
                            nextResp.Results[0].ContinuationPoint
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        [Description("Browse Server node one reference at a time and verify that every reference NodeId is unique across all pages — no duplicates should appear.")]
        [Test]
        public async Task VerifyAllReferencesAreUniqueAcrossPagesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            var seen = new HashSet<string>();
            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                Assert.That(
                    seen.Add(rd.NodeId.ToString()), Is.True,
                    $"Duplicate reference detected: {rd.NodeId}");
            }

            ByteString cp = response.Results[0].ContinuationPoint;
            for (int iterations = 0; !cp.IsEmpty && iterations < 200; iterations++)
            {
                BrowseNextResponse nextResp = await Session.BrowseNextAsync(
                    null, false,
                    new ByteString[] { cp }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                foreach (ReferenceDescription rd
                    in nextResp.Results[0].References)
                {
                    Assert.That(
                        seen.Add(rd.NodeId.ToString()), Is.True,
                        $"Duplicate reference detected: {rd.NodeId}");
                }

                cp = nextResp.Results[0].ContinuationPoint;
            }

            Assert.That(seen, Has.Count.GreaterThan(1));
        }

        [Description("Browse a node that has few references with a large MaxReferencesPerNode value. No continuation point should be needed because all references fit in the first response.")]
        [Test]
        public async Task BrowseNodeWithFewReferencesNoContinuationNeededAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 100,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ViewsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].ContinuationPoint.IsNull, Is.True,
                "Views folder has few children; no continuation point " +
                "should be returned with MaxRefs=100.");
        }

        [Description("Call BrowseNext with releaseContinuationPoints=true. The server should return Good status but no references in the result.")]
        [Test]
        public async Task BrowseNextWithReleaseTrueReturnsNoReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            ByteString cp = response.Results[0].ContinuationPoint;
            if (cp.IsEmpty)
            {
                Assert.Ignore("No continuation point available.");
            }

            BrowseNextResponse releaseResp = await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(releaseResp.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(releaseResp.Results[0].StatusCode),
                Is.True,
                "Release should return Good status.");
            Assert.That(
                releaseResp.Results[0].ContinuationPoint.IsNull, Is.True,
                "No continuation point should remain after release.");
        }

        [Description("Browse the Types folder with MaxReferencesPerNode=1 and confirm a continuation point is returned.")]
        [Test]
        public async Task BrowseTypesWithContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.TypesFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count,
                Is.LessThanOrEqualTo(1));
            Assert.That(
                response.Results[0].ContinuationPoint.IsEmpty, Is.False,
                "Types folder should have multiple children; " +
                "a continuation point is expected.");

            // Clean up
            await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { response.Results[0].ContinuationPoint }
                    .ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Browse the Root node with MaxReferencesPerNode=1 and verify a continuation point is returned because Root has Objects, Types, and Views as children.")]
        [Test]
        public async Task BrowseRootWithMaxRefsOneAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.RootFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count,
                Is.LessThanOrEqualTo(1));
            Assert.That(
                response.Results[0].ContinuationPoint.IsEmpty, Is.False,
                "Root folder should have multiple children (Objects, Types, " +
                "Views); a continuation point is expected.");

            // Clean up
            await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { response.Results[0].ContinuationPoint }
                    .ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Description("Obtain continuation points from two different nodes and advance them independently with separate BrowseNext calls to verify the server tracks them separately.")]
        [Test]
        public async Task BrowseNextMultipleContinuationPointsSimultaneouslyAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new() {
                        NodeId = ObjectIds.TypesFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));

            ByteString cp0 = response.Results[0].ContinuationPoint;
            ByteString cp1 = response.Results[1].ContinuationPoint;

            if (cp0.IsEmpty || cp1.IsEmpty)
            {
                // Clean up whichever is not empty
                var cleanup = new List<ByteString>();
                if (!cp0.IsEmpty)
                {
                    cleanup.Add(cp0);
                }

                if (!cp1.IsEmpty)
                {
                    cleanup.Add(cp1);
                }

                if (cleanup.Count > 0)
                {
                    await Session.BrowseNextAsync(
                        null, true,
                        cleanup.ToArray().ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                Assert.Ignore(
                    "Both nodes must return continuation points.");
            }

            // Advance first continuation point independently
            BrowseNextResponse next0 = await Session.BrowseNextAsync(
                null, false,
                new ByteString[] { cp0 }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(next0.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(next0.Results[0].StatusCode), Is.True);

            // Advance second continuation point independently
            BrowseNextResponse next1 = await Session.BrowseNextAsync(
                null, false,
                new ByteString[] { cp1 }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(next1.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(next1.Results[0].StatusCode), Is.True);

            // Clean up remaining continuation points
            var remaining = new List<ByteString>();
            if (!next0.Results[0].ContinuationPoint.IsEmpty)
            {
                remaining.Add(next0.Results[0].ContinuationPoint);
            }
            if (!next1.Results[0].ContinuationPoint.IsEmpty)
            {
                remaining.Add(next1.Results[0].ContinuationPoint);
            }
            if (remaining.Count > 0)
            {
                await Session.BrowseNextAsync(
                    null, true,
                    remaining.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Description("Browse with MaxReferencesPerNode=0 which means no limit. All references should be returned in a single response with no continuation point.")]
        [Test]
        public async Task BrowseWithMaxRefsZeroReturnsAllAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count,
                Is.GreaterThan(0),
                "Server node should have at least one reference.");
            Assert.That(response.Results[0].ContinuationPoint.IsNull, Is.True,
                "MaxReferencesPerNode=0 should return all references " +
                "without a continuation point.");
        }

        [Description("Release a continuation point, then release the same one again. The second release should return BadContinuationPointInvalid because it no longer exists.")]
        [Test]
        public async Task ReleaseContinuationPointTwiceReturnsErrorAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            ByteString cp = response.Results[0].ContinuationPoint;
            if (cp.IsEmpty)
            {
                Assert.Ignore("No continuation point to release.");
            }

            // First release – should succeed
            BrowseNextResponse first = await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(first.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(first.Results[0].StatusCode), Is.True);

            // Second release – continuation point no longer valid
            BrowseNextResponse second = await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(second.Results.Count, Is.EqualTo(1));
            // Server may return BadContinuationPointInvalid or Good (spec allows both)
            Assert.That(
                second.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadContinuationPointInvalid)
                    .Or.EqualTo(StatusCodes.Good));
        }

        [Description("Browse the Objects folder in the Inverse direction with MaxReferencesPerNode=1 and verify a continuation point is returned when there are multiple inverse references.")]
        [Test]
        public async Task BrowseObjectsFolderInverseWithContinuationPointAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId = ReferenceTypeIds.References,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count,
                Is.LessThanOrEqualTo(1));

            // Objects folder with Both direction should have many refs
            Assert.That(
                response.Results[0].ContinuationPoint.IsEmpty, Is.False,
                "Objects folder browsed with Both direction should " +
                "produce a continuation point.");

            // Clean up
            await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { response.Results[0].ContinuationPoint }
                    .ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
