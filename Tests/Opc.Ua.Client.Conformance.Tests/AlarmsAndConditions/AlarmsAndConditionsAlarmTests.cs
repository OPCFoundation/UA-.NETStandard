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

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for the A and C Alarm conformance unit.
    /// Verifies that AlarmConditionType, AlarmGroupType, and
    /// AlarmSuppressionGroupType exist and have the correct hierarchy.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsAlarmTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "A and C Alarm")]
        [Property("Tag", "Test_000")]
        public async Task AlarmConditionTypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.AlarmConditionType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmConditionType should exist in the address space.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Alarm")]
        [Property("Tag", "Test_000")]
        public async Task AlarmConditionIsSubtypeOfAcknowledgeableAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.AlarmConditionType,
                ObjectTypeIds.AcknowledgeableConditionType)
                .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "A and C Alarm")]
        [Property("Tag", "Test_000")]
        public async Task AlarmGroupTypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.AlarmGroupType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmGroupType should exist in the address space.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Alarm")]
        [Property("Tag", "Test_000")]
        public async Task AlarmGroupTypeIsSubtypeOfFolderTypeAsync()
        {
            await VerifySubtypeOfAsync(
                ObjectTypeIds.AlarmGroupType,
                ObjectTypeIds.FolderType).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "A and C Alarm")]
        [Property("Tag", "Test_000")]
        public async Task AlarmSuppressionGroupTypeExistsAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.AlarmSuppressionGroupType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmSuppressionGroupType should exist.");
        }

        private async Task<DataValue> ReadBrowseNameAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task VerifySubtypeOfAsync(
            NodeId typeId, NodeId expectedParent)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = typeId,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            bool found = false;
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                NodeId parentId = ToNodeId(r.NodeId);
                if (parentId == expectedParent)
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                $"Type {typeId} should be a subtype of {expectedParent}.");
        }
    }
}
