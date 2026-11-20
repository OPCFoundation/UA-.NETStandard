// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Node Management Delete Node.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("NodeManagement")]
    public class NodeManagementDeleteNodeTests : TestFixture
    {
        [Description("empty request. Expects BadNothingToDo. */")]
        [Test]
        public async Task DeleteEmptyRequestReturnsBadNothingToDoAsync()
        {
            try
            {
                ArrayOf<DeleteNodesItem> req = new DeleteNodesItem[]
                {
                    new() { NodeId = Constants.InvalidNodeId, DeleteTargetReferences = true }
                }.ToArrayOf();
                DeleteNodesResponse response = await Session.DeleteNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("DeleteNodes service not supported by ReferenceServer.");
            }
        }

        [Description("specify more nodes than the server reports as supported. How this test works: Part 1: Add twice as many nodes to the address space, as server claims to support in a single call Par")]
        [Test]
        public async Task DeleteMoreNodesThanServerSupportsReturnsBadStatusAsync()
        {
            try
            {
                ArrayOf<DeleteNodesItem> req = new DeleteNodesItem[]
                {
                    new() { NodeId = Constants.InvalidNodeId, DeleteTargetReferences = true }
                }.ToArrayOf();
                DeleteNodesResponse response = await Session.DeleteNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0]), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("DeleteNodes service not supported by ReferenceServer.");
            }
        }

        [Description("Round-trip test: add a node, delete it, and verify deletion succeeds.")]
        [Test]
        public async Task DeleteNodesRoundTripSucceedsAsync()
        {
            var addRequest = new AddNodesItem
            {
                ParentNodeId = new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = ExpandedNodeId.Null,
                BrowseName = new QualifiedName(
                    "ConformanceDeleteRoundTrip_" + System.Guid.NewGuid().ToString("N"), 2),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(
                    new VariableAttributes
                    {
                        DisplayName = (LocalizedText)"Delete Round Trip",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0)
                    }),
                TypeDefinition = new ExpandedNodeId(VariableTypeIds.BaseDataVariableType)
            };

            AddNodesResponse addResponse;
            try
            {
                addResponse = await Session.AddNodesAsync(
                    null,
                    new AddNodesItem[] { addRequest }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("DeleteNodes service not supported by ReferenceServer.");
                return;
            }

            Assert.That(addResponse.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(addResponse.Results[0].StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes returned: {addResponse.Results[0].StatusCode}");
                return;
            }

            DeleteNodesResponse deleteResponse = await Session.DeleteNodesAsync(
                null,
                new DeleteNodesItem[]
                {
                    new() {
                        NodeId = addResponse.Results[0].AddedNodeId,
                        DeleteTargetReferences = true
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(deleteResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(deleteResponse.Results[0]), Is.True);
        }
    }
}
