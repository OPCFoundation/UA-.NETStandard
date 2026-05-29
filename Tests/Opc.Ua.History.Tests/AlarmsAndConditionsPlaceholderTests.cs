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
            => AssertTypeExistsAsync(new NodeId(17279), "AlarmMetricsType");

        [Test]
        public Task AudibleSoundPlaceholder()
            => AssertTypeExistsAsync(new NodeId(16390), "AudibleSound");

        [Test]
        public Task ConditionSubClassesPlaceholder()
            => AssertTypeExistsAsync(new NodeId(11163), "BaseConditionClassType");

        [Test]
        public Task ConditionClassesPlaceholder()
            => AssertTypeExistsAsync(new NodeId(11163), "BaseConditionClassType");

        [Test]
        public Task DialogPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.DialogConditionType, "DialogConditionType");

        [Test]
        public Task DiscrepancyPlaceholder()
            => AssertTypeExistsAsync(new NodeId(17080), "DiscrepancyAlarmType");

        [Test]
        public Task DiscretePlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.DiscreteAlarmType, "DiscreteAlarmType");

        [Test]
        public Task ExclusiveDeviationPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.ExclusiveDeviationAlarmType, "ExclusiveDeviationAlarmType");

        [Test]
        public Task ExclusiveLevelPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.ExclusiveLevelAlarmType, "ExclusiveLevelAlarmType");

        [Test]
        public Task ExclusiveLimitPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.ExclusiveLimitAlarmType, "ExclusiveLimitAlarmType");

        [Test]
        public Task ExclusiveRateOfChangePlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.ExclusiveRateOfChangeAlarmType, "ExclusiveRateOfChangeAlarmType");

        [Test]
        public Task FirstInGroupAlarmPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.AlarmGroupType, "AlarmGroupType");

        [Test]
        public Task NonExclusiveDeviationPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.NonExclusiveDeviationAlarmType, "NonExclusiveDeviationAlarmType");

        [Test]
        public Task NonExclusiveLevelPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.NonExclusiveLevelAlarmType, "NonExclusiveLevelAlarmType");

        [Test]
        public Task NonExclusiveLimitPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.NonExclusiveLimitAlarmType, "NonExclusiveLimitAlarmType");

        [Test]
        public Task NonExclusiveRateOfChangePlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.NonExclusiveRateOfChangeAlarmType, "NonExclusiveRateOfChangeAlarmType");

        [Test]
        public Task OffNormalPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.OffNormalAlarmType, "OffNormalAlarmType");

        [Test]
        public Task OnOffDelayPlaceholder()
            => AssertTypeExistsAsync(new NodeId(16395), "OnDelay");

        [Test]
        public Task OutOfServicePlaceholder()
            => AssertTypeExistsAsync(new NodeId(16371), "OutOfServiceState");

        [Test]
        public Task ReAlarmingPlaceholder()
            => AssertTypeExistsAsync(new NodeId(16400), "ReAlarmTime");

        [Test]
        public Task SilencingPlaceholder()
            => AssertTypeExistsAsync(new NodeId(16380), "SilenceState");

        [Test]
        public Task SuppressionByOperatorPlaceholder()
            => AssertTypeExistsAsync(new NodeId(9215), "SuppressedOrShelved");

        [Test]
        public Task SystemOffNormalPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.SystemOffNormalAlarmType, "SystemOffNormalAlarmType");

        [Test]
        public Task TripPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.TripAlarmType, "TripAlarmType");

        [Test]
        public Task WrapperMappingPlaceholder()
            => AssertTypeExistsAsync(ObjectTypeIds.RefreshStartEventType, "RefreshStartEventType");

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
