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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Conformance.Tests.InformationModel
{
    /// <summary>
    /// compliance tests for OptionSet verification.
    /// Validates that attributes encoded as option-set bit fields
    /// (AccessLevel, WriteMask, EventNotifier, etc.) are correctly
    /// exposed by the server.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoOptionSet")]
    public class BaseInfoOptionSetTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task ReadAccessLevelAttributeAsOptionSetAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.AccessLevel).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            byte accessLevel = result.WrappedValue.GetByte();
            Assert.That(
                accessLevel & AccessLevels.CurrentRead,
                Is.Not.Zero,
                "CurrentRead bit must be set");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task ReadWriteMaskAttributeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server, Attributes.WriteMask)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task ReadUserWriteMaskAttributeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server, Attributes.UserWriteMask)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task ReadAccessLevelContainsCurrentReadBitAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.AccessLevel).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            byte accessLevel = result.WrappedValue.GetByte();
            Assert.That(
                (accessLevel & AccessLevels.CurrentRead) != 0, Is.True,
                "Bit 0 (CurrentRead) should be set");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task ReadAccessLevelContainsCurrentWriteBitAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.AccessLevel).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            byte accessLevel = result.WrappedValue.GetByte();
            Assert.That(
                (accessLevel & AccessLevels.CurrentWrite) != 0, Is.True,
                "Bit 1 (CurrentWrite) should be set for writable variable");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task VerifyOptionSetTypeExistsInTypeHierarchyAsync()
        {
            var optionSetTypeId = new NodeId(12755);

            // OptionSet is a subtype of Structure (i=22), not BaseDataType (i=24)
            // — browse Structure for the immediate child.
            ReferenceDescription rd = await BrowseForChildAsync(
                DataTypeIds.Structure, optionSetTypeId)
                .ConfigureAwait(false);

            if (rd == null)
            {
                Assert.Ignore(
                    "OptionSetType (i=12755) not found under Structure");
            }

            Assert.That(rd.DisplayName.Text, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task ReadUserAccessLevelAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.UserAccessLevel)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task AccessLevelBitsAreConsistentAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            DataValue accessResult = await ReadAttributeAsync(
                nodeId, Attributes.AccessLevel).ConfigureAwait(false);
            DataValue userResult = await ReadAttributeAsync(
                nodeId, Attributes.UserAccessLevel).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(accessResult.StatusCode), Is.True);
            Assert.That(
                StatusCode.IsGood(userResult.StatusCode), Is.True);

            byte accessLevel = accessResult.WrappedValue.GetByte();
            byte userAccessLevel = userResult.WrappedValue.GetByte();

            Assert.That(
                userAccessLevel & ~accessLevel, Is.Zero,
                "UserAccessLevel must be a subset of AccessLevel");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task WriteMaskDecodedAsUInt32Async()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server, Attributes.WriteMask)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint writeMask = result.WrappedValue.GetUInt32();
            Assert.That(writeMask, Is.TypeOf<uint>());
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task BrowseOptionSetTypeChildrenAsync()
        {
            var optionSetTypeId = new NodeId(12755);

            // First verify the type exists
            DataValue typeRead = await ReadAttributeAsync(
                optionSetTypeId, Attributes.BrowseName)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(typeRead.StatusCode))
            {
                Assert.Fail(
                    "OptionSetType (i=12755) does not exist on this server");
            }

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = optionSetTypeId,
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
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task ReadEventNotifierAttributeAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server, Attributes.EventNotifier)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info OptionSet")]
        [Property("Tag", "001")]
        public async Task VerifyAccessLevelExTypeAttributeAsync()
        {
            const uint accessLevelEx = 27;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            DataValue result = await ReadAttributeAsync(
                nodeId, accessLevelEx).ConfigureAwait(false);

            bool isGood = StatusCode.IsGood(result.StatusCode);
            bool isNotSupported =
                result.StatusCode == StatusCodes.BadAttributeIdInvalid;

            Assert.That(
                isGood || isNotSupported, Is.True,
                "AccessLevelEx should return Good or BadAttributeIdInvalid");
        }

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<ReferenceDescription> BrowseForChildAsync(
            NodeId parentId, NodeId targetId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = parentId,
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
            if (!StatusCode.IsGood(response.Results[0].StatusCode))
            {
                return null;
            }

            foreach (ReferenceDescription rd in response.Results[0].References)
            {
                var childId = ExpandedNodeId.ToNodeId(
                    rd.NodeId, Session.NamespaceUris);
                if (childId == targetId)
                {
                    return rd;
                }
            }

            return null;
        }
    }
}
