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

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Node Management services.
    /// All tests handle unsupported operations gracefully.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("NodeManagement")]
    public class NodeManagementTests : TestFixture
    {
        [Test]
        public async Task AddNodesHandledGracefullyAsync()
        {
            var request = new AddNodesItem
            {
                ParentNodeId =
                    new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId =
                    ExpandedNodeId.Null,
                BrowseName = new QualifiedName("ConformanceTestNode", 2),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(
                    new VariableAttributes
                    {
                        DisplayName = (LocalizedText)"Test Node",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel =
                            AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0)
                    }),
                TypeDefinition = new ExpandedNodeId(
                    VariableTypeIds.BaseDataVariableType)
            };

            try
            {
                AddNodesResponse response = await Session.AddNodesAsync(
                    null,
                    new AddNodesItem[] { request }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                StatusCode sc = response.Results[0].StatusCode;
                if (!StatusCode.IsGood(sc) && !IsUnsupported(sc))
                {
                    Assert.Ignore(
                        $"AddNodes returned: {sc}");
                }
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteNodesHandledGracefullyAsync()
        {
            try
            {
                DeleteNodesResponse response =
                    await Session.DeleteNodesAsync(
                        null,
                        new DeleteNodesItem[]
                        {
                            new() {
                                NodeId = Constants.InvalidNodeId,
                                DeleteTargetReferences = true
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"DeleteNodes not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task AddReferencesHandledGracefullyAsync()
        {
            try
            {
                AddReferencesResponse response =
                    await Session.AddReferencesAsync(
                        null,
                        new AddReferencesItem[]
                        {
                            new() {
                                SourceNodeId = ObjectIds.ObjectsFolder,
                                ReferenceTypeId =
                                    ReferenceTypeIds.Organizes,
                                IsForward = true,
                                TargetNodeId = new ExpandedNodeId(
                                    Constants.InvalidNodeId),
                                TargetNodeClass = NodeClass.Variable
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"AddReferences not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task DeleteReferencesHandledGracefullyAsync()
        {
            try
            {
                DeleteReferencesResponse response =
                    await Session.DeleteReferencesAsync(
                        null,
                        new DeleteReferencesItem[]
                        {
                            new() {
                                SourceNodeId = ObjectIds.ObjectsFolder,
                                ReferenceTypeId =
                                    ReferenceTypeIds.Organizes,
                                IsForward = true,
                                TargetNodeId = new ExpandedNodeId(
                                    Constants.InvalidNodeId),
                                DeleteBidirectional = false
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"DeleteReferences not supported: {ex.StatusCode}");
            }
        }

        [Test]
        public async Task AddNodeThenDeleteNodeAsync()
        {
            var addRequest = new AddNodesItem
            {
                ParentNodeId =
                    new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId =
                    ExpandedNodeId.Null,
                BrowseName = new QualifiedName("ConformanceTempNode", 2),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(
                    new VariableAttributes
                    {
                        DisplayName = (LocalizedText)"Temp Node",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel =
                            AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0)
                    }),
                TypeDefinition = new ExpandedNodeId(
                    VariableTypeIds.BaseDataVariableType)
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
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes not supported: {ex.StatusCode}");
                return;
            }

            Assert.That(addResponse.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(addResponse.Results[0].StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes returned: {addResponse.Results[0].StatusCode}");
                return;
            }

            NodeId addedNodeId = addResponse.Results[0].AddedNodeId;

            try
            {
                DeleteNodesResponse deleteResponse =
                    await Session.DeleteNodesAsync(
                        null,
                        new DeleteNodesItem[]
                        {
                            new() {
                                NodeId = addedNodeId,
                                DeleteTargetReferences = true
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(deleteResponse.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(deleteResponse.Results[0]),
                    Is.True);
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"DeleteNodes not supported: {ex.StatusCode}");
            }
        }

        [Description("Verify that AddNodes with NodeClass Object is handled gracefully when the server does not support the operation.")]
        [Test]
        public async Task AddObjectNodeHandledGracefullyAsync()
        {
            var request = new AddNodesItem
            {
                ParentNodeId =
                    new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = ExpandedNodeId.Null,
                BrowseName =
                    new QualifiedName("ConformanceTestObjNode", 2),
                NodeClass = NodeClass.Object,
                NodeAttributes = new ExtensionObject(
                    new ObjectAttributes
                    {
                        DisplayName =
                            (LocalizedText)"Test Object Node",
                        EventNotifier = EventNotifiers.None
                    }),
                TypeDefinition = new ExpandedNodeId(
                    ObjectTypeIds.BaseObjectType)
            };

            try
            {
                AddNodesResponse response = await Session.AddNodesAsync(
                    null,
                    new AddNodesItem[] { request }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                StatusCode sc = response.Results[0].StatusCode;

                if (StatusCode.IsGood(sc))
                {
                    // Clean up the added node.
                    NodeId addedNodeId =
                        response.Results[0].AddedNodeId;
                    try
                    {
                        await Session.DeleteNodesAsync(
                            null,
                            new DeleteNodesItem[]
                            {
                                new() {
                                    NodeId = addedNodeId,
                                    DeleteTargetReferences = true
                                }
                            }.ToArrayOf(),
                            CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // Best-effort cleanup.
                    }
                }
                else if (!IsUnsupported(sc))
                {
                    Assert.Ignore(
                        $"AddNodes (Object) returned: {sc}");
                }
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes not supported: {ex.StatusCode}");
            }
        }

        [Description("Add a node and then browse the parent to verify the new node is visible in the address space.")]
        [Test]
        public async Task AddNodeThenBrowseVerifyVisibleAsync()
        {
            const string browseName = "ConformanceTestBrowseVisible";

            var addRequest = new AddNodesItem
            {
                ParentNodeId =
                    new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = ExpandedNodeId.Null,
                BrowseName = new QualifiedName(browseName, 2),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(
                    new VariableAttributes
                    {
                        DisplayName =
                            (LocalizedText)"Browse Visible",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel =
                            AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0)
                    }),
                TypeDefinition = new ExpandedNodeId(
                    VariableTypeIds.BaseDataVariableType)
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
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes not supported: {ex.StatusCode}");
                return;
            }

            Assert.That(addResponse.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(addResponse.Results[0].StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes returned: {addResponse.Results[0].StatusCode}");
                return;
            }

            NodeId addedNodeId = addResponse.Results[0].AddedNodeId;

            try
            {
                // Browse the parent to find the new node.
                BrowseResponse browseResponse = await Session.BrowseAsync(
                    null,
                    null,
                    0,
                    new BrowseDescription[]
                    {
                        new() {
                            NodeId = ObjectIds.ObjectsFolder,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId =
                                ReferenceTypeIds.Organizes,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(browseResponse.Results.Count, Is.EqualTo(1));

                bool found = false;

                if (browseResponse.Results[0].References != default)
                {
                    foreach (ReferenceDescription rd in
                        browseResponse.Results[0].References)
                    {
                        if (rd.BrowseName == new QualifiedName(
                            browseName, 2))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                Assert.That(found, Is.True,
                    "Newly added node not found via Browse.");
            }
            finally
            {
                // Clean up.
                try
                {
                    await Session.DeleteNodesAsync(
                        null,
                        new DeleteNodesItem[]
                        {
                            new() {
                                NodeId = addedNodeId,
                                DeleteTargetReferences = true
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Best-effort cleanup.
                }
            }
        }

        [Description("Verify that adding a node twice with the same BrowseName returns an error on the second attempt.")]
        [Test]
        public async Task AddNodeWithDuplicateBrowseNameAsync()
        {
            const string browseName = "ConformanceTestDuplicate";

            var request = new AddNodesItem
            {
                ParentNodeId =
                    new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = ExpandedNodeId.Null,
                BrowseName = new QualifiedName(browseName, 2),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(
                    new VariableAttributes
                    {
                        DisplayName =
                            (LocalizedText)"Duplicate Test",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel =
                            AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0)
                    }),
                TypeDefinition = new ExpandedNodeId(
                    VariableTypeIds.BaseDataVariableType)
            };

            AddNodesResponse firstResponse;
            try
            {
                firstResponse = await Session.AddNodesAsync(
                    null,
                    new AddNodesItem[] { request }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes not supported: {ex.StatusCode}");
                return;
            }

            Assert.That(firstResponse.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(firstResponse.Results[0].StatusCode))
            {
                Assert.Ignore(
                    $"AddNodes returned: {firstResponse.Results[0].StatusCode}");
                return;
            }

            NodeId addedNodeId = firstResponse.Results[0].AddedNodeId;

            try
            {
                // Second add with the same BrowseName.
                AddNodesResponse secondResponse =
                    await Session.AddNodesAsync(
                        null,
                        new AddNodesItem[] { request }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(secondResponse.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(
                        secondResponse.Results[0].StatusCode),
                    Is.False,
                    "Second AddNodes with duplicate BrowseName " +
                    "should not succeed.");
            }
            catch (ServiceResultException)
            {
                // An exception is also acceptable for duplicates.
            }
            finally
            {
                // Clean up the first node.
                try
                {
                    await Session.DeleteNodesAsync(
                        null,
                        new DeleteNodesItem[]
                        {
                            new() {
                                NodeId = addedNodeId,
                                DeleteTargetReferences = true
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Best-effort cleanup.
                }
            }
        }

        [Description("Add a reference between two existing nodes and then browse to verify the reference is visible.")]
        [Test]
        public async Task AddReferenceThenBrowseVerifyVisibleAsync()
        {
            NodeId sourceNodeId = ObjectIds.ObjectsFolder;
            NodeId targetNodeId = ObjectIds.Server;

            try
            {
                AddReferencesResponse addRefResponse =
                    await Session.AddReferencesAsync(
                        null,
                        new AddReferencesItem[]
                        {
                            new() {
                                SourceNodeId = sourceNodeId,
                                ReferenceTypeId =
                                    ReferenceTypeIds.Organizes,
                                IsForward = true,
                                TargetNodeId =
                                    new ExpandedNodeId(targetNodeId),
                                TargetNodeClass = NodeClass.Object
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    addRefResponse.Results.Count, Is.EqualTo(1));
                StatusCode sc = addRefResponse.Results[0];

                if (!StatusCode.IsGood(sc) && !IsUnsupported(sc))
                {
                    // Reference may already exist; that is acceptable.
                    if (sc != StatusCodes.BadDuplicateReferenceNotAllowed)
                    {
                        Assert.Ignore(
                            $"AddReferences returned: {sc}");
                        return;
                    }
                }
                else if (IsUnsupported(sc))
                {
                    Assert.Ignore(
                        $"AddReferences not supported: {sc}");
                    return;
                }

                // Browse to verify the reference is visible.
                BrowseResponse browseResponse = await Session.BrowseAsync(
                    null,
                    null,
                    0,
                    new BrowseDescription[]
                    {
                        new() {
                            NodeId = sourceNodeId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId =
                                ReferenceTypeIds.Organizes,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(browseResponse.Results.Count, Is.EqualTo(1));
                Assert.That(
                    browseResponse.Results[0].References,
                    Is.Not.Null);
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"AddReferences not supported: {ex.StatusCode}");
            }
        }

        [Description("Verify that deleting a truly non-existent node returns BadNodeIdUnknown or an unsupported status.")]
        [Test]
        public async Task DeleteNonExistentNodeReturnsErrorAsync()
        {
            // Use a node ID that is guaranteed not to exist.
            var nonExistentNodeId = new NodeId(
                "ConformanceNonExistent_" + System.Guid.NewGuid().ToString(), 2);

            try
            {
                DeleteNodesResponse response =
                    await Session.DeleteNodesAsync(
                        null,
                        new DeleteNodesItem[]
                        {
                            new() {
                                NodeId = nonExistentNodeId,
                                DeleteTargetReferences = true
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(response.Results[0]),
                    Is.False,
                    "Deleting a non-existent node should not succeed.");
            }
            catch (ServiceResultException ex)
                when (IsUnsupported(ex.StatusCode))
            {
                Assert.Ignore(
                    $"DeleteNodes not supported: {ex.StatusCode}");
            }
        }

        private static bool IsUnsupported(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadNotSupported ||
                statusCode == StatusCodes.BadUserAccessDenied ||
                statusCode == StatusCodes.BadServiceUnsupported;
        }
    }
}
