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
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for the Redundancy Server conformance unit.
    /// Verifies the ServerRedundancy object and its properties.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("RedundancyModel")]
    public class RedundancyModelTests : TestFixture
    {
        [Test]
        public async Task ServerObjectHasServerRedundancyChildAsync()
        {
            BrowseResult result = await BrowseChildrenAsync(
                ObjectIds.Server).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            bool found = false;
            foreach (ReferenceDescription rd in result.References)
            {
                if (rd.BrowseName == BrowseNames.ServerRedundancy)
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                "Server object must have a ServerRedundancy child.");
        }

        [Test]
        public async Task RedundancySupportIsValidEnumAsync()
        {
            DataValue dv = await ReadValueAsync(
                VariableIds.Server_ServerRedundancy_RedundancySupport)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "RedundancySupport should be readable.");

            int value = (int)dv.WrappedValue.GetInt32();
            // RedundancySupport enum: None=0, Cold=1, Warm=2,
            // Hot=3, Transparent=4, HotAndMirrored=5
            Assert.That(value, Is.InRange(0, 5),
                $"RedundancySupport value {value} is not a valid enum.");
        }

        [Test]
        public async Task ServerUriArrayIsReadableAsync()
        {
            DataValue dv = await ReadValueAsync(
                VariableIds.Server_ServerRedundancy_RedundancySupport)
                .ConfigureAwait(false);

            int redundancySupport = (int)dv.WrappedValue.GetInt32();

            // ServerUriArray is only mandatory when redundancy != None
            if (redundancySupport == 0)
            {
                Assert.Ignore(
                    "RedundancySupport is None; ServerUriArray not required.");
            }

            BrowseResult result = await BrowseChildrenAsync(
                ObjectIds.Server_ServerRedundancy).ConfigureAwait(false);

            bool found = false;
            foreach (ReferenceDescription rd in result.References)
            {
                if (rd.BrowseName.Name == "ServerUriArray")
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                "ServerRedundancy should have ServerUriArray when redundancy != None.");
        }

        [Test]
        public async Task RedundancySupportHasCorrectDataTypeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerRedundancy_RedundancySupport,
                        AttributeId = Attributes.DataType
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            var dataType = response.Results[0].WrappedValue.GetNodeId();
            // DataTypeId i=851 is the RedundancySupport enumeration
            Assert.That(dataType, Is.EqualTo(DataTypeIds.RedundancySupport),
                "RedundancySupport DataType should be RedundancySupport enum.");
        }

        [Test]
        public async Task ServerRedundancyHasTypeDefinitionAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_ServerRedundancy,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
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
                "ServerRedundancy must have a type definition.");

            var typeDefId = ExpandedNodeId.ToNodeId(
                response.Results[0].References[0].NodeId,
                Session.NamespaceUris);

            // Should be ServerRedundancyType or a subtype
            Assert.That(typeDefId, Is.Not.Null,
                "Type definition NodeId should not be null.");
        }

        [Test]
        public async Task CurrentServerIdExistsIfRedundancyEnabledAsync()
        {
            DataValue dv = await ReadValueAsync(
                VariableIds.Server_ServerRedundancy_RedundancySupport)
                .ConfigureAwait(false);

            int redundancySupport = (int)dv.WrappedValue.GetInt32();
            if (redundancySupport == 0)
            {
                Assert.Ignore(
                    "RedundancySupport is None; CurrentServerId not required.");
            }

            BrowseResult result = await BrowseChildrenAsync(
                ObjectIds.Server_ServerRedundancy).ConfigureAwait(false);

            bool found = false;
            foreach (ReferenceDescription rd in result.References)
            {
                if (rd.BrowseName.Name == "CurrentServerId")
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                "CurrentServerId should exist when redundancy is enabled.");
        }

        private async Task<DataValue> ReadValueAsync(NodeId nodeId)
        {
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
            return response.Results[0];
        }

        private async Task<BrowseResult> BrowseChildrenAsync(
            NodeId parentId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = parentId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
