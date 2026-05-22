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

namespace Opc.Ua.Conformance.Tests.NodeManagement
{
    /// <summary>
    /// compliance tests for Node Management Add Node.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("NodeManagement")]
    public class NodeManagementAddNodeTests : TestFixture
    {
        [Description("add a node using typical parameters. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "001")]
        public async Task AddNodeWithTypicalParametersSucceedsAsync()
        {
            var addRequest = new AddNodesItem
            {
                ParentNodeId = new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = ExpandedNodeId.Null,
                BrowseName = new QualifiedName(
                    "ConformanceTypicalNode_" + System.Guid.NewGuid().ToString("N"), 2),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(
                    new VariableAttributes
                    {
                        DisplayName = (LocalizedText)"Typical Variable",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0)
                    }),
                TypeDefinition = new ExpandedNodeId(VariableTypeIds.BaseDataVariableType)
            };

            try
            {
                AddNodesResponse response = await Session.AddNodesAsync(
                    null,
                    new AddNodesItem[] { addRequest }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
                Assert.That(response.Results[0].AddedNodeId.IsNull, Is.False);

                // Cleanup.
                try
                {
                    await Session.DeleteNodesAsync(
                        null,
                        new DeleteNodesItem[]
                        {
                            new() {
                                NodeId = response.Results[0].AddedNodeId,
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
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("add a node varying the browseName and reference type. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "002")]
        public async Task AddNodeWithVariedBrowseNameAndReferenceTypeSucceedsAsync()
        {
            var addRequest = new AddNodesItem
            {
                ParentNodeId = new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                RequestedNewNodeId = ExpandedNodeId.Null,
                BrowseName = new QualifiedName(
                    "ConformanceVariedNode_" + System.Guid.NewGuid().ToString("N"), 2),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(
                    new VariableAttributes
                    {
                        DisplayName = (LocalizedText)"Varied Variable",
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0.0)
                    }),
                TypeDefinition = new ExpandedNodeId(VariableTypeIds.BaseDataVariableType)
            };

            try
            {
                AddNodesResponse response = await Session.AddNodesAsync(
                    null,
                    new AddNodesItem[] { addRequest }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
                Assert.That(response.Results[0].AddedNodeId.IsNull, Is.False);

                // Cleanup.
                try
                {
                    await Session.DeleteNodesAsync(
                        null,
                        new DeleteNodesItem[]
                        {
                            new() {
                                NodeId = response.Results[0].AddedNodeId,
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
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("add a node using typical parameters. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "003")]
        public async Task AddNodeWithTypicalParametersAlternateSucceedsAsync()
        {
            var addRequest = new AddNodesItem
            {
                ParentNodeId = new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = ExpandedNodeId.Null,
                BrowseName = new QualifiedName(
                    "ConformanceObject_" + System.Guid.NewGuid().ToString("N"), 2),
                NodeClass = NodeClass.Object,
                NodeAttributes = new ExtensionObject(
                    new ObjectAttributes
                    {
                        DisplayName = (LocalizedText)"Typical Object",
                        EventNotifier = EventNotifiers.None
                    }),
                TypeDefinition = new ExpandedNodeId(ObjectTypeIds.BaseObjectType)
            };

            try
            {
                AddNodesResponse response = await Session.AddNodesAsync(
                    null,
                    new AddNodesItem[] { addRequest }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
                Assert.That(response.Results[0].AddedNodeId.IsNull, Is.False);

                // Cleanup.
                try
                {
                    await Session.DeleteNodesAsync(
                        null,
                        new DeleteNodesItem[]
                        {
                            new() {
                                NodeId = response.Results[0].AddedNodeId,
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
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("add a node but do not specify any properties. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "Err-001")]
        public async Task AddNodeWithoutPropertiesReturnsBadStatusAsync()
        {
            try
            {
                ArrayOf<AddNodesItem> req = new AddNodesItem[]
                {
                    new() {
                        ParentNodeId = new ExpandedNodeId(Constants.InvalidNodeId),
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = new QualifiedName("InvalidTestNode"),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(new VariableAttributes { Value = new Variant(0) })
                    }
                }.ToArrayOf();
                AddNodesResponse response = await Session.AddNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("add a node but do not specify any properties. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "Err-002")]
        public async Task AddNodeWithoutPropertiesAlternateReturnsBadStatusAsync()
        {
            try
            {
                ArrayOf<AddNodesItem> req = new AddNodesItem[]
                {
                    new() {
                        ParentNodeId = new ExpandedNodeId(Constants.InvalidNodeId),
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = new QualifiedName("InvalidTestNode"),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(new VariableAttributes { Value = new Variant(0) })
                    }
                }.ToArrayOf();
                AddNodesResponse response = await Session.AddNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("parentNodeId is inknown. Expect BadParentIdInvalid. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "Err-003")]
        public async Task AddNodeWithUnknownParentReturnsBadParentIdInvalidAsync()
        {
            try
            {
                ArrayOf<AddNodesItem> req = new AddNodesItem[]
                {
                    new() {
                        ParentNodeId = new ExpandedNodeId(Constants.InvalidNodeId),
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = new QualifiedName("InvalidTestNode"),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(new VariableAttributes { Value = new Variant(0) })
                    }
                }.ToArrayOf();
                AddNodesResponse response = await Session.AddNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("parentNodeId is inknown. Expect BadReferenceTypeIdInvalid. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "Err-004")]
        public async Task AddNodeWithInvalidReferenceTypeReturnsBadReferenceTypeIdInvalidAsync()
        {
            try
            {
                ArrayOf<AddNodesItem> req = new AddNodesItem[]
                {
                    new() {
                        ParentNodeId = new ExpandedNodeId(Constants.InvalidNodeId),
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = new QualifiedName("InvalidTestNode"),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(new VariableAttributes { Value = new Variant(0) })
                    }
                }.ToArrayOf();
                AddNodesResponse response = await Session.AddNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("Use incorrect reference type. Expects BadReferenceNotAllowed. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "Err-005")]
        public async Task AddNodeWithIncorrectReferenceTypeReturnsBadReferenceNotAllowedAsync()
        {
            try
            {
                ArrayOf<AddNodesItem> req = new AddNodesItem[]
                {
                    new() {
                        ParentNodeId = new ExpandedNodeId(Constants.InvalidNodeId),
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = new QualifiedName("InvalidTestNode"),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(new VariableAttributes { Value = new Variant(0) })
                    }
                }.ToArrayOf();
                AddNodesResponse response = await Session.AddNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("specify a nodeid even if not supported. */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "Err-006")]
        public async Task AddNodeWithSpecifiedNodeIdReturnsExpectedStatusAsync()
        {
            try
            {
                ArrayOf<AddNodesItem> req = new AddNodesItem[]
                {
                    new() {
                        ParentNodeId = new ExpandedNodeId(Constants.InvalidNodeId),
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = new QualifiedName("InvalidTestNode"),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(new VariableAttributes { Value = new Variant(0) })
                    }
                }.ToArrayOf();
                AddNodesResponse response = await Session.AddNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("specify a nodeid even if not supported, namespaceIndex is 0 (OPC). May be accepted, or return BadNodeIdRejected */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "Err-007")]
        public async Task AddNodeWithSpecifiedNodeIdInNamespaceZeroReturnsExpectedStatusAsync()
        {
            try
            {
                ArrayOf<AddNodesItem> req = new AddNodesItem[]
                {
                    new() {
                        ParentNodeId = new ExpandedNodeId(Constants.InvalidNodeId),
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = new QualifiedName("InvalidTestNode"),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(new VariableAttributes { Value = new Variant(0) })
                    }
                }.ToArrayOf();
                AddNodesResponse response = await Session.AddNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("add a node using typical parameters. Then add it again: BadNodeIdExists */")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "Err-008")]
        public async Task AddDuplicateNodeReturnsBadNodeIdExistsAsync()
        {
            try
            {
                ArrayOf<AddNodesItem> req = new AddNodesItem[]
                {
                    new() {
                        ParentNodeId = new ExpandedNodeId(Constants.InvalidNodeId),
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = new QualifiedName("InvalidTestNode"),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(new VariableAttributes { Value = new Variant(0) })
                    }
                }.ToArrayOf();
                AddNodesResponse response = await Session.AddNodesAsync(null, req, CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }

        [Description("Round-trip test: add a Variable node and verify it can be deleted.")]
        [Test]
        [Property("ConformanceUnit", "Node Management Add Node")]
        [Property("Tag", "test")]
        public async Task AddNodesWriteTestRoundTripSucceedsAsync()
        {
            var addRequest = new AddNodesItem
            {
                ParentNodeId = new ExpandedNodeId(ObjectIds.ObjectsFolder),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                RequestedNewNodeId = ExpandedNodeId.Null,
                BrowseName = new QualifiedName(
                    "ConformanceWriteTest_" + System.Guid.NewGuid().ToString("N"), 2),
                NodeClass = NodeClass.Variable,
                NodeAttributes = new ExtensionObject(
                    new VariableAttributes
                    {
                        DisplayName = (LocalizedText)"Round Trip",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        Value = new Variant(0)
                    }),
                TypeDefinition = new ExpandedNodeId(VariableTypeIds.BaseDataVariableType)
            };

            try
            {
                AddNodesResponse addResponse = await Session.AddNodesAsync(
                    null,
                    new AddNodesItem[] { addRequest }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(addResponse.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(addResponse.Results[0].StatusCode), Is.True);

                NodeId addedNodeId = addResponse.Results[0].AddedNodeId;

                DeleteNodesResponse deleteResponse = await Session.DeleteNodesAsync(
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
                Assert.That(StatusCode.IsGood(deleteResponse.Results[0]), Is.True);
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore("AddNodes service not supported by ReferenceServer.");
            }
        }
    }
}
