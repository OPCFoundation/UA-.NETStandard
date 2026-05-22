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

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for the A and C Basic conformance unit.
    /// Verifies fundamental alarm and condition types exist in the
    /// address space with expected properties.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsBasicTests : TestFixture
    {
        [Test]
        public async Task ConditionTypeExistsInAddressSpaceAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.ConditionType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "ConditionType should exist in the address space.");
        }

        [Test]
        public async Task AlarmConditionTypeExistsInAddressSpaceAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.AlarmConditionType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmConditionType should exist in the address space.");
        }

        [Test]
        public async Task AcknowledgeableConditionTypeHasAckedStateAsync()
        {
            DataValue dv = await ReadBrowseNameAsync(
                ObjectTypeIds.AcknowledgeableConditionType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AcknowledgeableConditionType should exist.");

            bool has = await TypeHasPropertyAsync(
                ObjectTypeIds.AcknowledgeableConditionType,
                "AckedState").ConfigureAwait(false);
            Assert.That(has, Is.True,
                "AcknowledgeableConditionType should have " +
                "AckedState property.");
        }

        [Test]
        public async Task AlarmConditionTypeHasActiveAndSuppressedStateAsync()
        {
            bool hasActive = await TypeHasPropertyAsync(
                ObjectTypeIds.AlarmConditionType,
                "ActiveState").ConfigureAwait(false);
            Assert.That(hasActive, Is.True,
                "AlarmConditionType should have ActiveState.");

            bool hasSuppressed = await TypeHasPropertyAsync(
                ObjectTypeIds.AlarmConditionType,
                "SuppressedState").ConfigureAwait(false);
            Assert.That(hasSuppressed, Is.True,
                "AlarmConditionType should have SuppressedState.");
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

        private async Task<BrowseResult> BrowseForwardAsync(NodeId nodeId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
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
            return response.Results[0];
        }

        private async Task<bool> TypeHasPropertyAsync(
            NodeId typeId, string propertyName)
        {
            BrowseResult result = await BrowseForwardAsync(typeId)
                .ConfigureAwait(false);
            foreach (ReferenceDescription r in result.References)
            {
                if (r.BrowseName.Name == propertyName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
