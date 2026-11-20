/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.History.Tests
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
        public async Task AlarmConditionTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectTypeIds.AlarmConditionType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "AlarmConditionType should exist.");
        }

        [Test]
        public async Task AlarmConditionTypeHasInputNodeAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.AlarmConditionType, "InputNode")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "AlarmConditionType should have InputNode property.");
        }

        [Test]
        public async Task AlarmConditionTypeHasActiveStateAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.AlarmConditionType, "ActiveState")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "AlarmConditionType should have ActiveState.");
        }

        [Test]
        public void AlarmInstancesExistInAddressSpace()
        {
            Assert.That(AlarmInstances, Has.Count.GreaterThan(0),
                "At least one alarm condition instance should exist.");
        }

        [Test]
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
