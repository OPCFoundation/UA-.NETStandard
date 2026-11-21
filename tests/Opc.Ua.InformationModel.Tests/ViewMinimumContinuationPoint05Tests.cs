// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for View Minimum Continuation Point 05.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ViewServices")]
    public class ViewMinimumContinuationPoint05Tests : TestFixture
    {
        [Description("Given five nodes to browse And the nodes exist And the nodes have at least two forward references When Browse is called Then the server returns a continuation point for each node A")]
        [Test]
        public async Task BrowseFiveNodesReturnsContinuationPointPerNodeAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given five nodes to browse And the nodes exist And each node has at least three forward references And RequestedMaxReferencesPerNode is 1 And Browse has been called When BrowseNext")]
        [Test]
        public async Task BrowseNextOnFiveNodesReturnsRemainingReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Description("Given two nodes to browse And the nodes exist And each node has at least three forward references And RequestedMaxReferencesPerNode is 1 And Browse has been called separately for e")]
        [Test]
        public async Task BrowseNextOnSeparateBrowsesReturnsRemainingReferencesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 1,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true, NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }
    }
}
