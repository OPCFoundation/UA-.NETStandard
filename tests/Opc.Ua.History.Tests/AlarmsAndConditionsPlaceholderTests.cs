// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// Conformance unit smoke tests for Alarms and Conditions types and
    /// properties. Each test verifies the relevant standard nodeset entry
    /// is exposed by the server. These tests do not require a live alarm
    /// source — they only check that the type/property exists in the
    /// address space (BrowseName attribute readable). Skip when the type
    /// is not exposed by the server.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsPlaceholderTests : TestFixture
    {
        [Test]
        public Task AlarmMetricsPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(17279), "AlarmMetricsType");
        }

        [Test]
        public Task AudibleSoundPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(16390), "AudibleSound");
        }

        [Test]
        public Task ConditionSubClassesPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(11163), "BaseConditionClassType");
        }

        [Test]
        public Task ConditionClassesPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(11163), "BaseConditionClassType");
        }

        [Test]
        public Task DialogPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.DialogConditionType, "DialogConditionType");
        }

        [Test]
        public Task DiscrepancyPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(17080), "DiscrepancyAlarmType");
        }

        [Test]
        public Task DiscretePlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.DiscreteAlarmType, "DiscreteAlarmType");
        }

        [Test]
        public Task ExclusiveDeviationPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.ExclusiveDeviationAlarmType, "ExclusiveDeviationAlarmType");
        }

        [Test]
        public Task ExclusiveLevelPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.ExclusiveLevelAlarmType, "ExclusiveLevelAlarmType");
        }

        [Test]
        public Task ExclusiveLimitPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.ExclusiveLimitAlarmType, "ExclusiveLimitAlarmType");
        }

        [Test]
        public Task ExclusiveRateOfChangePlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.ExclusiveRateOfChangeAlarmType, "ExclusiveRateOfChangeAlarmType");
        }

        [Test]
        public Task FirstInGroupAlarmPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.AlarmGroupType, "AlarmGroupType");
        }

        [Test]
        public Task NonExclusiveDeviationPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.NonExclusiveDeviationAlarmType, "NonExclusiveDeviationAlarmType");
        }

        [Test]
        public Task NonExclusiveLevelPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.NonExclusiveLevelAlarmType, "NonExclusiveLevelAlarmType");
        }

        [Test]
        public Task NonExclusiveLimitPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.NonExclusiveLimitAlarmType, "NonExclusiveLimitAlarmType");
        }

        [Test]
        public Task NonExclusiveRateOfChangePlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.NonExclusiveRateOfChangeAlarmType, "NonExclusiveRateOfChangeAlarmType");
        }

        [Test]
        public Task OffNormalPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.OffNormalAlarmType, "OffNormalAlarmType");
        }

        [Test]
        public Task OnOffDelayPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(16395), "OnDelay");
        }

        [Test]
        public Task OutOfServicePlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(16371), "OutOfServiceState");
        }

        [Test]
        public Task ReAlarmingPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(16400), "ReAlarmTime");
        }

        [Test]
        public Task SilencingPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(16380), "SilenceState");
        }

        [Test]
        public Task SuppressionByOperatorPlaceholder()
        {
            return AssertTypeExistsAsync(new NodeId(9215), "SuppressedOrShelved");
        }

        [Test]
        public Task SystemOffNormalPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.SystemOffNormalAlarmType, "SystemOffNormalAlarmType");
        }

        [Test]
        public Task TripPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.TripAlarmType, "TripAlarmType");
        }

        [Test]
        public Task WrapperMappingPlaceholder()
        {
            return AssertTypeExistsAsync(ObjectTypeIds.RefreshStartEventType, "RefreshStartEventType");
        }

        private async Task AssertTypeExistsAsync(NodeId nodeId, string expectedName)
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
            DataValue dv = response.Results[0];

            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Ignore(
                    $"{expectedName} ({nodeId}) not exposed by server: " +
                    $"{dv.StatusCode}");
            }
            Assert.That(dv.WrappedValue.TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name.Name, Is.EqualTo(expectedName));
        }
    }
}
