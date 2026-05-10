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

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for the A and C Instances conformance unit.
    /// Verifies that alarm condition instances are properly structured
    /// in the address space.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsInstancesTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        [Property("ConformanceUnit", "A and C Instances")]
        [Property("Tag", "Test_001")]
        public async Task AlarmConditionTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectTypeIds.AlarmConditionType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmConditionType should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Instances")]
        [Property("Tag", "Test_001")]
        public async Task AlarmConditionTypeHasInputNodeAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.AlarmConditionType, "InputNode")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "AlarmConditionType should have InputNode property.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Instances")]
        [Property("Tag", "Test_001")]
        public async Task AlarmConditionTypeHasActiveStateAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.AlarmConditionType, "ActiveState")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "AlarmConditionType should have ActiveState.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Instances")]
        [Property("Tag", "Test_001")]
        public void AlarmInstancesExistInAddressSpace()
        {
            Assert.That(AlarmInstances.Count, Is.GreaterThan(0),
                "At least one alarm condition instance should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Instances")]
        [Property("Tag", "Test_002")]
        public async Task AlarmInstanceHasSourceNodeAsync()
        {
            NodeId alarmId = RequireAlarm();
            DataValue dv = await ReadChildValueAsync(alarmId, "SourceNode")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                $"Alarm should expose a SourceNode property: {dv.StatusCode}");
            Assert.That(
                dv.WrappedValue.TryGetValue(out NodeId sourceNode), Is.True,
                "SourceNode should be a NodeId.");
            _ = sourceNode;
        }

        [Test]
        [Property("ConformanceUnit", "A and C Instances")]
        [Property("Tag", "Test_001")]
        public async Task AlarmInstanceHasCorrectTypeDefinitionAsync()
        {
            NodeId alarmId = RequireAlarm();

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = alarmId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0),
                "Alarm instance should have a type definition.");

            NodeId typeDef = ToNodeId(
                response.Results[0].References[0].NodeId);
            Assert.That(typeDef.IsNull, Is.False,
                "TypeDefinition NodeId should not be null.");
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
