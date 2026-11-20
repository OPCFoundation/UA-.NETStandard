// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
