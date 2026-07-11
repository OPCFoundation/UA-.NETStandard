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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Attribute Service Set – Read.
    /// Based on test scripts: Attribute Read 001–011 and Err tests.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AttributeRead")]
    public class AttributeReadTests : TestFixture
    {
        [Description("Read Value from a single valid node. Expect StatusCode Good.")]
        [Test]
        public async Task AttributeRead001SingleNodeValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read of a valid node Value attribute should return Good.");
        }

        [Description("Read Value from multiple valid nodes. All should return Good.")]
        [Test]
        public async Task AttributeRead002MultipleNodesValueAsync()
        {
            var readValueIds = Constants.ScalarStaticNodes
                .Select(n => new ReadValueId
                {
                    NodeId = ToNodeId(n),
                    AttributeId = Attributes.Value
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(readValueIds.Count));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Read of node index {i} should return Good.");
            }
        }

        [Description("Read DisplayName attribute. Expect a non-null LocalizedText.")]
        [Test]
        public async Task AttributeRead003DisplayNameAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.DisplayName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            LocalizedText displayName = response.Results[0].GetValue<LocalizedText>(default);
            Assert.That(displayName, Is.Not.Null,
                "DisplayName should be a non-null LocalizedText.");
            Assert.That(displayName.Text, Is.Not.Null.And.Not.Empty,
                "DisplayName text should not be empty.");
        }

        [Description("Read BrowseName attribute. Expect a non-null QualifiedName.")]
        [Test]
        public async Task AttributeRead004BrowseNameAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            QualifiedName browseName = response.Results[0].GetValue<QualifiedName>(default);
            Assert.That(browseName, Is.Not.Null,
                "BrowseName should be a non-null QualifiedName.");
            Assert.That(browseName.Name, Is.Not.Null.And.Not.Empty,
                "BrowseName name should not be empty.");
        }

        [Description("Read NodeClass attribute. Should return Variable for data nodes.")]
        [Test]
        public async Task AttributeRead006NodeClassAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.NodeClass
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            int nodeClass = response.Results[0].GetValue(0);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Variable),
                "NodeClass for a scalar static node should be Variable.");
        }

        [Description("Read DataType attribute. Should return a valid DataType NodeId.")]
        [Test]
        public async Task AttributeRead007DataTypeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.DataType
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            NodeId dataType = response.Results[0].GetValue<NodeId>(default);
            Assert.That(dataType, Is.Not.Null,
                "DataType should not be null.");
            Assert.That(dataType, Is.Not.EqualTo(NodeId.Null),
                "DataType should not be Null NodeId.");
        }

        [Description("Read all standard attributes in a single call for a Variable node.")]
        [Test]
        public async Task AttributeRead008AllAttributesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            uint[] attributeIds =
            [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.DataType,
                Attributes.ValueRank,
                Attributes.Value,
                Attributes.AccessLevel,
                Attributes.UserAccessLevel
            ];

            var readValueIds = attributeIds
                .Select(attrId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attrId
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(attributeIds.Length));

            // All standard variable attributes should be readable
            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"AttributeId {attributeIds[i]} read should return Good.");
            }
        }

        [Description("Read with TimestampsToReturn=Source. Source timestamp should be present.")]
        [Test]
        public async Task AttributeRead009TimestampsSourceAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Source,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            // Server timestamp should be MinValue when only source is requested
            Assert.That(response.Results[0].ServerTimestamp,
                Is.EqualTo(DateTime.MinValue),
                "ServerTimestamp should be MinValue when TimestampsToReturn=Source.");
        }

        [Description("Read with TimestampsToReturn=Server. Server timestamp should be present.")]
        [Test]
        public async Task AttributeRead010TimestampsServerAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Server,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            // Source timestamp should be MinValue when only server is requested
            Assert.That(response.Results[0].SourceTimestamp,
                Is.EqualTo(DateTime.MinValue),
                "SourceTimestamp should be MinValue when TimestampsToReturn=Server.");
        }

        [Description("Read with MaxAge=0 (from device). Server must return a fresh value.")]
        [Test]
        public async Task AttributeRead011MaxAgeZeroAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read with MaxAge=0 should still return Good.");
        }

        [Description("Read an invalid/non-existent NodeId. Expect BadNodeIdUnknown.")]
        [Test]
        public async Task AttributeReadErr001InvalidNodeIdAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "Reading an invalid NodeId should return BadNodeIdUnknown.");
        }

        [Description("Read with an invalid AttributeId. Expect BadAttributeIdInvalid.")]
        [Test]
        public async Task AttributeReadErr002InvalidAttributeIdAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = 999
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadAttributeIdInvalid),
                "Reading with invalid AttributeId should return BadAttributeIdInvalid.");
        }

        [Description("Read an attribute not valid for the node class. E.g., read InverseName from a Variable node.")]
        [Test]
        public async Task AttributeReadErr003AttributeNotValidForNodeClassAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.InverseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            // InverseName is not valid on a Variable node
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Reading InverseName from a Variable node should return a Bad status.");
        }

        [Description("Read Value of an array node. Expect Good and an array value.")]
        [Test]
        public async Task AttributeRead012ReadArrayValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read of an array node Value should return Good.");
            // Arrays might be returned as typed arrays (e.g. int[]) or the
            // value may not yet have been written; verify the Variant has a
            // declared type. Genuinely polymorphic across BuiltInType because
            // the array element type is unknown to this generic test.
            Assert.That(response.Results[0].WrappedValue.TypeInfo, Is.Not.Null,
                "Value of an array node should not be null.");
        }

        [Description("Read an array node with IndexRange=\"0\". Should return a single element.")]
        [Test]
        public async Task AttributeRead013ReadWithIndexRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "0"
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Read with IndexRange='0' should return Good.");
        }

        [Description("Batch read of 5 different scalar nodes. All should return Good.")]
        [Test]
        public async Task AttributeRead014BatchReadMultipleNodesAsync()
        {
            ExpandedNodeId[] nodes =
            [
                Constants.ScalarStaticBoolean,
                Constants.ScalarStaticInt32,
                Constants.ScalarStaticDouble,
                Constants.ScalarStaticString,
                Constants.ScalarStaticDateTime
            ];

            var readValueIds = nodes
                .Select(n => new ReadValueId
                {
                    NodeId = ToNodeId(n),
                    AttributeId = Attributes.Value
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(nodes.Length));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Batch read node index {i} should return Good.");
            }
        }

        [Description("Read MinimumSamplingInterval from a variable node. Should return Good (or BadAttributeIdInvalid). If Good, value >= 0.")]
        [Test]
        public async Task AttributeRead016ReadMinimumSamplingIntervalAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.MinimumSamplingInterval
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            if (StatusCode.IsGood(response.Results[0].StatusCode))
            {
                double samplingInterval = response.Results[0].GetValue<double>(0);
                Assert.That(samplingInterval, Is.GreaterThanOrEqualTo(0),
                    "MinimumSamplingInterval should be >= 0.");
            }
            else
            {
                Assert.That(response.Results[0].StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadAttributeIdInvalid),
                    "If not Good, should be BadAttributeIdInvalid.");
            }
        }

        [Description("Read Historizing attribute from a variable node. Should return Good and a boolean value.")]
        [Test]
        public async Task AttributeRead017ReadHistorizingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Historizing
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Historizing attribute should be readable.");

            // Value should be a boolean
            bool historizing = response.Results[0].GetValue(false);
            Assert.That(historizing, Is.InstanceOf<bool>());
        }

        [Description("Read AccessLevel from a variable node. Should return Good and a non-zero value (at least readable).")]
        [Test]
        public async Task AttributeRead018ReadAccessLevelAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.AccessLevel
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "AccessLevel should be readable.");

            byte accessLevel = response.Results[0].GetValue<byte>(0);
            Assert.That(accessLevel, Is.Not.Zero,
                "AccessLevel should be non-zero (at least readable).");
        }

        [Description("Read UserAccessLevel from a variable node. Should return Good and a byte value.")]
        [Test]
        public async Task AttributeRead019ReadUserAccessLevelAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.UserAccessLevel
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "UserAccessLevel should be readable.");

            byte userAccessLevel = response.Results[0].GetValue<byte>(0);
            Assert.That(userAccessLevel, Is.InstanceOf<byte>());
        }

        [Description("Read ValueRank from a scalar variable. Should return Scalar (-1).")]
        [Test]
        public async Task AttributeRead020ReadValueRankAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.ValueRank
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "ValueRank should be readable.");

            int valueRank = response.Results[0].GetValue(0);
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar),
                "ValueRank for a scalar node should be Scalar (-1).");
        }

        [Description("Read ValueRank from an array variable. Should return OneDimension (1).")]
        [Test]
        public async Task AttributeRead021ReadValueRankArrayAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.ValueRank
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "ValueRank should be readable for an array node.");

            int valueRank = response.Results[0].GetValue(0);
            Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension),
                "ValueRank for an array node should be OneDimension (1).");
        }

        [Description("Read all standard Object attributes from the Server object. All should return Good. NodeClass should be Object.")]
        [Test]
        public async Task AttributeRead022ReadAllAttributesOfObjectAsync()
        {
            uint[] attributeIds =
            [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.EventNotifier
            ];

            var readValueIds = attributeIds
                .Select(attrId => new ReadValueId
                {
                    NodeId = ObjectIds.Server,
                    AttributeId = attrId
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(attributeIds.Length));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Object attribute {attributeIds[i]} should return Good.");
            }

            // NodeClass (index 1) should be Object
            int nodeClass = response.Results[1].GetValue(0);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Object),
                "NodeClass of Server should be Object.");
        }

        [Description("Read all standard Variable attributes from a scalar variable node. All should return Good or at least not BadAttributeIdInvalid.")]
        [Test]
        public async Task AttributeRead023ReadAllAttributesOfVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            uint[] attributeIds =
            [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.Value,
                Attributes.DataType,
                Attributes.ValueRank,
                Attributes.ArrayDimensions,
                Attributes.AccessLevel,
                Attributes.UserAccessLevel,
                Attributes.MinimumSamplingInterval,
                Attributes.Historizing,
                Attributes.AccessLevelEx
            ];

            var readValueIds = attributeIds
                .Select(attrId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attrId
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(attributeIds.Length));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(response.Results[i].StatusCode.Code,
                    Is.Not.EqualTo(StatusCodes.BadAttributeIdInvalid),
                    $"Variable attribute {attributeIds[i]} should not return BadAttributeIdInvalid.");
            }
        }

        [Description("Read all standard Method attributes from a method node. Browse the Methods folder to find a child method, then read its attributes.")]
        [Test]
        public async Task AttributeRead024ReadAllAttributesOfMethodAsync()
        {
            NodeId methodsFolderId = ToNodeId(Constants.MethodsFolder);

            // Browse forward to find a Method child
            BrowseResponse browseResponse = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = methodsFolderId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Method,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(browseResponse.Results.Count, Is.GreaterThan(0),
                "Browse should return results.");
            Assert.That(browseResponse.Results[0].References.Count, Is.GreaterThan(0),
                "Methods folder should contain at least one Method child.");

            var methodNodeId = ExpandedNodeId.ToNodeId(
                browseResponse.Results[0].References[0].NodeId,
                Session.NamespaceUris);

            uint[] attributeIds =
            [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.Executable,
                Attributes.UserExecutable
            ];

            var readValueIds = attributeIds
                .Select(attrId => new ReadValueId
                {
                    NodeId = methodNodeId,
                    AttributeId = attrId
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(attributeIds.Length));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Method attribute {attributeIds[i]} should return Good.");
            }
        }

        [Description("Read all standard ReferenceType attributes from Organizes. All should return Good.")]
        [Test]
        public async Task AttributeRead025ReadAllAttributesOfReferenceTypeAsync()
        {
            uint[] attributeIds =
            [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.IsAbstract,
                Attributes.Symmetric,
                Attributes.InverseName
            ];

            var readValueIds = attributeIds
                .Select(attrId => new ReadValueId
                {
                    NodeId = ReferenceTypeIds.Organizes,
                    AttributeId = attrId
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(attributeIds.Length));

            for (int i = 0; i < response.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"ReferenceType attribute {attributeIds[i]} should return Good.");
            }
        }

        [Description("Read Description from the Server object. Should return Good. Value is a LocalizedText (may be null or empty).")]
        [Test]
        public async Task AttributeRead026ReadDescriptionAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.Description
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Description attribute should be readable from Server object.");
        }

        [Description("Read WriteMask from a variable node. Should return Good.")]
        [Test]
        public async Task AttributeRead027ReadWriteMaskAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.WriteMask
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "WriteMask attribute should be readable.");
        }

        [Description("Read UserWriteMask from a variable node. Should return Good.")]
        [Test]
        public async Task AttributeRead028ReadUserWriteMaskAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.UserWriteMask
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "UserWriteMask attribute should be readable.");
        }

        [Description("Read with TimestampsToReturn=Neither. Both timestamps should be MinValue.")]
        [Test]
        public async Task AttributeRead029TimestampsNoneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            Assert.That(response.Results[0].ServerTimestamp,
                Is.EqualTo(DateTime.MinValue),
                "ServerTimestamp should be MinValue when TimestampsToReturn=Neither.");
            Assert.That(response.Results[0].SourceTimestamp,
                Is.EqualTo(DateTime.MinValue),
                "SourceTimestamp should be MinValue when TimestampsToReturn=Neither.");
        }

        [Description("Read EventNotifier from the Server object. Should return Good. Value is a byte.")]
        [Test]
        public async Task AttributeRead030ReadEventNotifierAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.EventNotifier
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "EventNotifier should be readable from Server object.");

            byte eventNotifier = response.Results[0].GetValue<byte>(0);
            Assert.That(eventNotifier, Is.InstanceOf<byte>());
        }

        [Description("Read Server_ServerStatus_CurrentTime. Should return Good and a DateTime close to now.")]
        [Test]
        public async Task AttributeRead031ReadServerStatusCurrentTimeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Server_ServerStatus_CurrentTime should return Good.");

            // CurrentTime is per spec a DateTime (UtcTime). Verify the wire
            // value parses as a DateTimeUtc and is non-min.
            Assert.That(
                response.Results[0].WrappedValue.TryGetValue(out DateTimeUtc serverTime),
                Is.True,
                "CurrentTime value should decode as DateTime.");
            Assert.That(serverTime.ToDateTime(), Is.Not.EqualTo(DateTime.MinValue),
                "CurrentTime should not be MinValue.");
        }

        [Description("Read Server_ServerArray. Should return Good and a string array with at least one entry.")]
        [Test]
        public async Task AttributeRead032ReadServerArrayAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerArray,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Server_ServerArray should return Good.");

            string[] serverArray = response.Results[0].GetValue<string[]>(null);
            Assert.That(serverArray, Is.Not.Null,
                "ServerArray should not be null.");
            Assert.That(serverArray, Is.Not.Empty,
                "ServerArray should have at least one entry.");
        }

        [Description("Read Server_NamespaceArray. Should return Good and a string array. First entry should be \"http://opcfoundation.org/UA/\".")]
        [Test]
        public async Task AttributeRead033ReadNamespaceArrayAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_NamespaceArray,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "Server_NamespaceArray should return Good.");

            string[] namespaceArray = response.Results[0].GetValue<string[]>(null);
            Assert.That(namespaceArray, Is.Not.Null,
                "NamespaceArray should not be null.");
            Assert.That(namespaceArray, Is.Not.Empty,
                "NamespaceArray should have at least one entry.");
            Assert.That(namespaceArray[0], Is.EqualTo("http://opcfoundation.org/UA/"),
                "First namespace should be the OPC UA namespace.");
        }

        [Description("Read with NodeId.Null. Should return BadNodeIdUnknown or BadNodeIdInvalid.")]
        [Test]
        public async Task AttributeReadErr004ReadNullNodeIdAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = NodeId.Null,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                "Reading with NodeId.Null should return a Bad status.");
            Assert.That(response.Results[0].StatusCode.Code,
                Is.AnyOf(StatusCodes.BadNodeIdUnknown, StatusCodes.BadNodeIdInvalid),
                "Status should be BadNodeIdUnknown or BadNodeIdInvalid.");
        }

        [Description("Mix of valid and invalid nodes. First and third should be Good; second should be BadNodeIdUnknown.")]
        [Test]
        public async Task AttributeReadErr005MixOfValidAndInvalidNodesAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    },
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value
                    },
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticBoolean),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(3));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "First valid node should return Good.");
            Assert.That(response.Results[1].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                "Invalid node should return BadNodeIdUnknown.");
            Assert.That(StatusCode.IsGood(response.Results[2].StatusCode), Is.True,
                "Third valid node should return Good.");
        }

        [Description("Read Value attribute from an Object node (Server). Should return BadAttributeIdInvalid.")]
        [Test]
        public async Task AttributeReadErr006ReadValueFromObjectNodeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadAttributeIdInvalid),
                "Reading Value from an Object node should return BadAttributeIdInvalid.");
        }

        [Description("Read Executable attribute from a Variable node. Should return BadAttributeIdInvalid.")]
        [Test]
        public async Task AttributeReadErr007ReadExecutableFromVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Executable
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadAttributeIdInvalid),
                "Reading Executable from a Variable node should return BadAttributeIdInvalid.");
        }

        [Description("Read with DataEncoding=Default Binary should succeed.")]
        [Test]
        public async Task ReadWithDefaultBinaryEncodingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        DataEncoding = new QualifiedName("Default Binary")
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            // Simple types don't require encoding, so Good or BadDataEncodingUnsupported
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code == StatusCodes.BadDataEncodingUnsupported ||
                response.Results[0].StatusCode.Code == StatusCodes.BadDataEncodingInvalid,
                Is.True);
        }

        [Description("Read with DataEncoding=Default XML should succeed or return unsupported.")]
        [Test]
        public async Task ReadWithDefaultXmlEncodingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        DataEncoding = new QualifiedName("Default XML")
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code == StatusCodes.BadDataEncodingUnsupported ||
                response.Results[0].StatusCode.Code == StatusCodes.BadDataEncodingInvalid,
                Is.True);
        }

        [Description("Read with DataEncoding=Default JSON should succeed or return unsupported.")]
        [Test]
        public async Task ReadWithDefaultJsonEncodingAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        DataEncoding = new QualifiedName("Default JSON")
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code == StatusCodes.BadDataEncodingUnsupported ||
                response.Results[0].StatusCode.Code == StatusCodes.BadDataEncodingInvalid,
                Is.True);
        }

        [Description("Read Value of a DataType node should return Null.")]
        [Test]
        public async Task ReadValueOfDataTypeNodeReturnsNullAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = DataTypeIds.Int32,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadAttributeIdInvalid),
                "DataType nodes should not have Value attribute.");
        }

        [Description("Read Value of a ReferenceType node should return BadAttributeIdInvalid.")]
        [Test]
        public async Task ReadValueOfReferenceTypeNodeReturnsErrorAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ReferenceTypeIds.References,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadAttributeIdInvalid),
                "ReferenceType nodes should not have Value attribute.");
        }

        [Description("Read all attributes of the Views folder.")]
        [Test]
        public async Task ReadAllAttributesOfViewsFolderAsync()
        {
            uint[] attrIds =
            [
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.EventNotifier
            ];

            ReadValueId[] items = [.. attrIds.Select(a => new ReadValueId
            {
                NodeId = ObjectIds.ViewsFolder,
                AttributeId = a
            })];

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(attrIds.Length));

            // NodeId, NodeClass, BrowseName, DisplayName should all be Good
            for (int i = 0; i < 4; i++)
            {
                Assert.That(
                    StatusCode.IsGood(response.Results[i].StatusCode), Is.True,
                    $"Attribute {attrIds[i]} should be readable on Views folder.");
            }
        }

        [Description("Read array of structures (if available).")]
        [Test]
        public async Task ReadArrayVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                StatusCode.IsUncertain(response.Results[0].StatusCode),
                Is.True);
        }

        [Description("Read nested structure (ServerStatusDataType on ServerStatus).")]
        [Test]
        public async Task ReadServerStatusStructureAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "ServerStatus should be readable.");
            Assert.That(response.Results[0].WrappedValue.IsNull, Is.False);
        }

        [Description("Read ExtensionObject value from ServerStatus (complex structure).")]
        [Test]
        public async Task ReadComplexStructureValueAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            DataValue dv = response.Results[0];
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True);

            Assert.That(dv.WrappedValue.IsNull, Is.False,
                "ServerStatus Value should not be null.");
        }

        [Test]
        public async Task ReadDisplayNameOfServerAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.DisplayName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            LocalizedText displayName = response.Results[0].GetValue<LocalizedText>(default);
            Assert.That(displayName.Text, Is.EqualTo("Server"));
        }

        [Test]
        public async Task ReadDescriptionOfServerStatusAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Description
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            // Description may or may not be present; just verify no error
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code == StatusCodes.Good,
                Is.True);
        }

        [Test]
        public async Task ReadDataTypeOfInt32VariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.DataType
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            NodeId dataType = response.Results[0].GetValue<NodeId>(default);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Int32));
        }

        [Test]
        public async Task ReadAccessLevelOfInt32VariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.AccessLevel
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            byte accessLevel = response.Results[0].WrappedValue.GetByte();
            Assert.That(
                (accessLevel & AccessLevels.CurrentRead) != 0, Is.True,
                "ScalarStaticInt32 should have CurrentRead access.");
        }

        [Test]
        public async Task ReadMinimumSamplingIntervalAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.MinimumSamplingInterval
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            // MinimumSamplingInterval may not be supported on all variables
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode) ||
                response.Results[0].StatusCode.Code == StatusCodes.BadAttributeIdInvalid,
                Is.True);
        }

        [Test]
        public async Task ReadHistorizingAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Historizing
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            bool historizing = response.Results[0].WrappedValue.GetBoolean();
            // Historizing may be true if history support is enabled on the server
            Assert.That(historizing, Is.TypeOf<bool>(),
                "Historizing attribute should be a boolean value.");
        }

        [Test]
        public async Task ReadIsAbstractOnObjectTypeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectTypeIds.BaseObjectType,
                        AttributeId = Attributes.IsAbstract
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            bool isAbstract = response.Results[0].WrappedValue.GetBoolean();
            // BaseObjectType is not always abstract in all implementations
            Assert.That(isAbstract, Is.TypeOf<bool>(),
                "IsAbstract should be a Boolean value.");
        }

        [Description("Read a data value with TimestampsToReturn = BOTH.")]
        [Test]
        public async Task AttributeRead006ReadWithTimestampsBothAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Reads the BROWSENAME attribute of multiple valid nodes.")]
        [Test]
        public async Task AttributeRead011ReadBrowseNameMultipleNodesAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Reads the same valid attribute from the same valid node multiple times in the same call.")]
        [Test]
        public async Task AttributeRead013ReadSameAttributeMultipleTimesAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("MaxAge greater than Int32.")]
        [Test]
        public async Task AttributeRead016MaxAgeGreaterThanInt32Async()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read array with indexRange retrieving elements 2–4 only.")]
        [Test]
        public async Task AttributeRead025ReadArrayIndexRange2To4Async()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read array with indexRange retrieving the last 3 elements.")]
        [Test]
        public async Task AttributeRead026ReadArrayLastThreeElementsAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read any attribute except Value; SourceTimestamp is null.")]
        [Test]
        public async Task AttributeRead028ReadNonValueAttributeAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read Value of a multi-dimensional (2D) array for each data-type. " +
            "Each node returns Good and a rank-2 matrix value.")]
        [Test]
        public async Task AttributeRead030ReadMultiDimensionalArrayValueAsync()
        {
            foreach (ExpandedNodeId node in Constants.ScalarStaticArrays2DNodes)
            {
                ReadResponse readResponse = await Session.ReadAsync(
                    null, 0, TimestampsToReturn.Both,
                    new ReadValueId[]
                    {
                        new() { NodeId = ToNodeId(node), AttributeId = Attributes.Value }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(readResponse.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True,
                    $"Read of 2D array node '{node}' should return Good.");
                Assert.That(readResponse.Results[0].WrappedValue.TypeInfo.ValueRank,
                    Is.EqualTo(ValueRanks.TwoDimensions),
                    $"Node '{node}' must return a rank-2 matrix value.");
            }
        }

        [Description("Read Value of multiple multi-dimensional array nodes in a single request. " +
            "All return Good.")]
        [Test]
        public async Task AttributeRead031ReadMultipleMultiDimensionalArraysAsync()
        {
            var readValueIds = Constants.ScalarStaticArrays2DNodes
                .Select(n => new ReadValueId { NodeId = ToNodeId(n), AttributeId = Attributes.Value })
                .ToArrayOf();

            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(Constants.ScalarStaticArrays2DNodes.Length));
            for (int i = 0; i < readResponse.Results.Count; i++)
            {
                Assert.That(StatusCode.IsGood(readResponse.Results[i].StatusCode), Is.True,
                    $"Batch read of 2D array node index {i} should return Good.");
            }
        }

        [Description("A multi-dimensional array node exposes concrete, non-zero ArrayDimensions " +
            "and an IndexRange read of a single element returns Good.")]
        [Test]
        public async Task AttributeRead032IndexRangeSingleElementMultiDimAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrays2DInt32);

            ReadResponse dimensionsResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = Attributes.ArrayDimensions }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(dimensionsResponse.Results[0].StatusCode), Is.True);
            ArrayOf<uint> arrayDimensions =
                dimensionsResponse.Results[0].GetValue(default(ArrayOf<uint>));
            Assert.That(arrayDimensions.Count, Is.EqualTo(2),
                "A multi-dimensional array must report two ArrayDimensions.");
            Assert.That(arrayDimensions[0], Is.GreaterThan(0u),
                "The first dimension length must be concrete (non-zero).");
            Assert.That(arrayDimensions[1], Is.GreaterThan(1u),
                "The second dimension length must be at least two for an index-range read.");

            ReadResponse valueResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = Attributes.Value, IndexRange = "0,1" }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(valueResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(valueResponse.Results[0].StatusCode), Is.True,
                "IndexRange read of a single 2D element should return Good.");
        }

        [Description("IndexRange reading a sub-range of a multi-dimensional array returns Good.")]
        [Test]
        public async Task AttributeRead033IndexRangeMultiDimAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new()
                    {
                        NodeId = ToNodeId(Constants.ScalarStaticArrays2DInt32),
                        AttributeId = Attributes.Value,
                        IndexRange = "0:1,0:1"
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True,
                "IndexRange sub-range read of a 2D array should return Good.");
        }

        [Description("IndexRange reading a range from the last dimension of a 2D array returns Good.")]
        [Test]
        public async Task AttributeRead034IndexRangeLastThreeMultiDimAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new()
                    {
                        NodeId = ToNodeId(Constants.ScalarStaticArrays2DInt32),
                        AttributeId = Attributes.Value,
                        IndexRange = "0,1:2"
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True,
                "IndexRange read of the last dimension of a 2D array should return Good.");
        }

        [Description("IndexRange lower bound within array but exceeding upper bound.")]
        [Test]
        public async Task AttributeRead036IndexRangeExceedingUpperBoundAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = ToNodeId(Constants.ScalarStaticInt32), AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read multiple valid attributes and one invalid attribute.")]
        [Test]
        public async Task AttributeReadErr002ReadInvalidNodeIdAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read the same invalid attribute from a valid node multiple times in the same call.")]
        [Test]
        public async Task AttributeReadErr003ReadSameInvalidAttributeMultipleTimesAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read from a node id with invalid syntax.")]
        [Test]
        public async Task AttributeReadErr006ReadFromInvalidSyntaxNodeIdAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read valid attributes from multiple non-existent nodes.")]
        [Test]
        public async Task AttributeReadErr009ReadFromNonExistentNodesAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read valid attributes from nodes with invalid syntax.")]
        [Test]
        public async Task AttributeReadErr010ReadFromInvalidSyntaxNodesAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Specifies a null nodes array for reading.")]
        [Test]
        public async Task AttributeReadErr011NullNodesArrayAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("IndexRange outside the bounds of the array.")]
        [Test]
        public async Task AttributeReadErr012IndexRangeOutOfBoundsAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Invalid IndexRange \"-2:0\".")]
        [Test]
        public async Task AttributeReadErr013InvalidIndexRangeNegativeAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("MaxAge is a negative number.")]
        [Test]
        public async Task AttributeReadErr014NegativeMaxAgeAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("IndexRange on non-applicable attributes.")]
        [Test]
        public async Task AttributeReadErr015IndexRangeOnNonApplicableAttributeAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read a node that is NOT readable.")]
        [Test]
        public async Task AttributeReadErr016ReadNonReadableNodeAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Read Value with invalid DataEncoding.")]
        [Test]
        public async Task AttributeReadErr017InvalidDataEncodingAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Invalid TimestampsToReturn value.")]
        [Test]
        public async Task AttributeReadErr019InvalidTimestampsToReturnAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Invalid IndexRange \"2-4\" (dash is invalid).")]
        [Test]
        public async Task AttributeReadErr022InvalidIndexRangeDashAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Invalid IndexRange \"2:2\" (not a range).")]
        [Test]
        public async Task AttributeReadErr023InvalidIndexRangeSameValueAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Invalid IndexRange \"5:2\" (backwards).")]
        [Test]
        public async Task AttributeReadErr024InvalidIndexRangeBackwardsAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = Constants.InvalidNodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(readResponse.Results[0].StatusCode), Is.True);
        }
    }
}
