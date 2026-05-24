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

using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for the A and C Suppression conformance unit.
    /// Verifies that suppression-related properties exist on the
    /// AlarmConditionType.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsSuppressionTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        public async Task AlarmConditionTypeHasSuppressedStateAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.AlarmConditionType, "SuppressedState")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "AlarmConditionType should have SuppressedState.");
        }

        [Test]
        public async Task AlarmConditionTypeHasSuppressedOrShelvedAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.AlarmConditionType, "SuppressedOrShelved")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "AlarmConditionType should have SuppressedOrShelved property.");
        }

        [Test]
        public async Task AlarmConditionTypeHasMaxTimeShelvedAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.AlarmConditionType, "MaxTimeShelved")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "AlarmConditionType should have MaxTimeShelved property.");
        }

        [Test]
        public async Task SuppressionStateTransitionAsync()
        {
            foreach (System.Collections.Generic.KeyValuePair<string, NodeId> kvp
                in AlarmInstances)
            {
                DataValue dv = await ReadStateIdAsync(kvp.Value, "SuppressedState")
                    .ConfigureAwait(false);
                if (StatusCode.IsGood(dv.StatusCode))
                {
                    Assert.That(
                        dv.WrappedValue.TryGetValue(out bool _), Is.True,
                        "SuppressedState/Id should be a boolean.");
                    return;
                }
            }
            Assert.Ignore("No alarm instance exposes SuppressedState.");
        }

        private async Task<bool> TypeHasChildAsync(NodeId typeId, string name)
        {
            BrowseResult result = await BrowseForwardAsync(typeId)
                .ConfigureAwait(false);
            int count = result.References.Count;
            for (int i = 0; i < count; i++)
            {
                if (result.References[i].BrowseName.Name == name)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
