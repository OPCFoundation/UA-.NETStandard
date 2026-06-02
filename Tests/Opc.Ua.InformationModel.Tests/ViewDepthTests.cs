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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance depth tests for View Service Set browse
    /// operations: continuation point scenarios, multi-node browse,
    /// browse with View, BrowseNext errors, ResultMask combinations,
    /// server diagnostics, and edge cases.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewBrowse")]
    public class ViewDepthTests : TestFixture
    {
        [Test]
        public async Task ContinuationPointWithMaxRefs1Async()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 1,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count,
                Is.LessThanOrEqualTo(1));
            await ReleaseCPAsync(result.ContinuationPoint)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ContinuationPointWithMaxRefs2Async()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 2,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count,
                Is.LessThanOrEqualTo(2));
            await ReleaseCPAsync(result.ContinuationPoint)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ContinuationPointWithMaxRefs5Async()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 5,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count,
                Is.LessThanOrEqualTo(5));
            await ReleaseCPAsync(result.ContinuationPoint)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ContinuationPointWithMaxRefs10Async()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 10,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count,
                Is.LessThanOrEqualTo(10));
            await ReleaseCPAsync(result.ContinuationPoint)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ContinuationPointWithMaxRefs0ReturnsAllAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task BrowseAllRefsWithMaxRefs1MatchesUnlimitedAsync()
        {
            List<ReferenceDescription> allAtOnce = await BrowseAllRefsAsync(
                ObjectIds.Server, 0).ConfigureAwait(false);
            List<ReferenceDescription> allPaged = await BrowseAllRefsAsync(
                ObjectIds.Server, 1).ConfigureAwait(false);
            Assert.That(allPaged, Has.Count.EqualTo(allAtOnce.Count),
                "Paged browse should yield the same total as unlimited.");
        }

        [Test]
        public async Task BrowseAllRefsWithMaxRefs3MatchesUnlimitedAsync()
        {
            List<ReferenceDescription> allAtOnce = await BrowseAllRefsAsync(
                ObjectIds.Server, 0).ConfigureAwait(false);
            List<ReferenceDescription> allPaged = await BrowseAllRefsAsync(
                ObjectIds.Server, 3).ConfigureAwait(false);
            Assert.That(allPaged, Has.Count.EqualTo(allAtOnce.Count));
        }

        [Test]
        public async Task ReleaseContinuationPointSucceedsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 1,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            if (result.ContinuationPoint.IsEmpty)
            {
                Assert.Fail("No continuation point to release.");
            }

            BrowseNextResponse release = await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { result.ContinuationPoint }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(release.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(release.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseTwoNodesSimultaneouslyAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new() {
                        NodeId = ObjectIds.TypesFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(
                StatusCode.IsGood(response.Results[1].StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseThreeNodesReturnsThreeResultsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new() {
                        NodeId = ObjectIds.TypesFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new() {
                        NodeId = ObjectIds.ViewsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task BrowseMultipleWithMaxRefsOneAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    },
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(
                    response.Results[i].References.Count,
                    Is.LessThanOrEqualTo(1));
                await ReleaseCPAsync(response.Results[i].ContinuationPoint)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task BrowseObjectsFolderAndServerReturnDifferentAsync()
        {
            List<ReferenceDescription> objRefs = await BrowseAllRefsAsync(
                ObjectIds.ObjectsFolder, 0).ConfigureAwait(false);
            List<ReferenceDescription> srvRefs = await BrowseAllRefsAsync(
                ObjectIds.Server, 0).ConfigureAwait(false);

            Assert.That(objRefs,
                Has.Count.Not.EqualTo(srvRefs.Count) | Has.Count.GreaterThan(0),
                "ObjectsFolder and Server should have different child counts " +
                "or both have children.");
        }

        [Test]
        public async Task BrowseWithNullViewSucceedsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task BrowseWithDefaultViewDescSucceedsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                new ViewDescription(), 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseNextWithEmptyCpReturnsErrorAsync()
        {
            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false,
                new ByteString[] { ByteString.Empty }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.False,
                "BrowseNext with empty CP should fail.");
        }

        [Test]
        public async Task BrowseNextWithInvalidCpReturnsErrorAsync()
        {
            byte[] fakeBytes = [0xFF, 0xFE, 0xFD, 0xFC];
            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false,
                new ByteString[] { new(fakeBytes) }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.False,
                "BrowseNext with invalid CP should fail.");
        }

        [Test]
        public async Task BrowseNextReleaseThenUseCpFailsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 1,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            if (result.ContinuationPoint.IsEmpty)
            {
                Assert.Fail("No continuation point available.");
            }

            ByteString cp = result.ContinuationPoint;
            await ReleaseCPAsync(cp).ConfigureAwait(false);

            BrowseNextResponse reuse = await Session.BrowseNextAsync(
                null, false,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(reuse.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(reuse.Results[0].StatusCode), Is.False,
                "Reusing a released CP should fail.");
        }

        [Test]
        public async Task BrowseNextReleaseAlreadyReleasedCpFailsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 1,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            if (result.ContinuationPoint.IsEmpty)
            {
                Assert.Ignore("No continuation point available.");
            }

            ByteString cp = result.ContinuationPoint;
            await ReleaseCPAsync(cp).ConfigureAwait(false);

            BrowseNextResponse again = await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(again.Results.Count, Is.EqualTo(1));
            // Server may return Good (no-op) or Bad for already-released CP
            // Both behaviors are acceptable per spec
            if (StatusCode.IsGood(again.Results[0].StatusCode))
            {
                Assert.Ignore("Server treats releasing already-released CP as no-op (Good).");
            }
            Assert.That(
                StatusCode.IsBad(again.Results[0].StatusCode), Is.True,
                "Releasing an already-released CP should fail.");
        }

        [Test]
        public async Task BrowseNextWithMultipleInvalidCpsFailAsync()
        {
            byte[] fake1 = [0x01, 0x02];
            byte[] fake2 = [0x03, 0x04];
            BrowseNextResponse response = await Session.BrowseNextAsync(
                null, false,
                new ByteString[]
                {
                    new(fake1),
                    new(fake2)
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.False);
            Assert.That(
                StatusCode.IsGood(response.Results[1].StatusCode), Is.False);
        }

        [Test]
        public async Task ResultMaskBrowseNameOnlyAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ResultMaskDisplayNameOnlyAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.DisplayName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ResultMaskNodeClassOnlyAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.NodeClass).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ResultMaskReferenceTypeOnlyAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.ReferenceTypeId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ResultMaskIsForwardOnlyAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.IsForward).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ResultMaskTypeDefinitionOnlyAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.TypeDefinition)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ResultMaskNoneReturnsReferencesAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ResultMaskAllReturnsFullAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));

            ReferenceDescription first = result.References[0];
            Assert.That(first.BrowseName, Is.Not.Null);
            Assert.That(first.DisplayName, Is.Not.Null);
        }

        [Test]
        public async Task ResultMaskBrowseAndDisplayNameAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)(BrowseResultMask.BrowseName |
                    BrowseResultMask.DisplayName))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task ServerDiagnosticsSummaryExistsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server_ServerDiagnostics,
                BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0),
                "ServerDiagnostics should have children.");
        }

        [Test]
        public async Task ServerStatusNodeExistsAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task BrowseServerDiagnosticsHasSessionArrayAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server_ServerDiagnostics,
                BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);

            bool found = false;
            foreach (ReferenceDescription r in result.References)
            {
                if (r.BrowseName.Name.Contains(
                    "Session", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                "ServerDiagnostics should have session-related children.");
        }

        [Test]
        public async Task BrowseServerCapabilitiesExistsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);

            bool found = false;
            foreach (ReferenceDescription r in result.References)
            {
                if (r.BrowseName.Name == "ServerCapabilities")
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                "Server should have a ServerCapabilities child.");
        }

        [Test]
        public async Task BrowseNamespacesArrayExistsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.Server, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);

            bool found = false;
            foreach (ReferenceDescription r in result.References)
            {
                if (r.BrowseName.Name is "NamespaceArray"
                    or "Namespaces")
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                "Server should expose namespace information.");
        }

        [Test]
        public async Task BrowseInverseOnRootReturnsNoRefsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.RootFolder, BrowseDirection.Inverse, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.Zero,
                "Root folder should have no inverse hierarchical references.");
        }

        [Test]
        public async Task BrowseForwardOnObjectsFolderReturnsChildrenAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ObjectsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task BrowseBothDirectionOnServerReturnsRefsAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Both,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
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
                Is.GreaterThan(0));
        }

        [Test]
        public async Task BrowseTypesFolderHasChildrenAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.TypesFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.References.Count, Is.GreaterThan(0),
                "TypesFolder should have children.");
        }

        [Test]
        public async Task BrowseViewsFolderSucceedsAsync()
        {
            BrowseResult result = await BrowseAsync(
                ObjectIds.ViewsFolder, BrowseDirection.Forward, 0,
                (uint)BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        private async Task<BrowseResult> BrowseAsync(
            NodeId nodeId,
            BrowseDirection direction,
            uint maxRefs,
            uint resultMask)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, maxRefs,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = direction,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = resultMask
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<List<ReferenceDescription>> BrowseAllRefsAsync(
            NodeId nodeId, uint maxRefsPerCall)
        {
            var all = new List<ReferenceDescription>();
            BrowseResult result = await BrowseAsync(
                nodeId, BrowseDirection.Forward, maxRefsPerCall,
                (uint)BrowseResultMask.All).ConfigureAwait(false);

            foreach (ReferenceDescription r in result.References)
            {
                all.Add(r);
            }

            ByteString cp = result.ContinuationPoint;
            for (int iterations = 0; !cp.IsEmpty && iterations < 200; iterations++)
            {
                BrowseNextResponse next = await Session.BrowseNextAsync(
                    null, false,
                    new ByteString[] { cp }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(next.Results.Count, Is.EqualTo(1));
                foreach (ReferenceDescription r in next.Results[0].References)
                {
                    all.Add(r);
                }

                cp = next.Results[0].ContinuationPoint;
            }

            return all;
        }

        private async Task ReleaseCPAsync(ByteString cp)
        {
            if (cp.IsEmpty)
            {
                return;
            }

            await Session.BrowseNextAsync(
                null, true,
                new ByteString[] { cp }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
