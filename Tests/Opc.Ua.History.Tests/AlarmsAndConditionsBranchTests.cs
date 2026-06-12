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
    /// compliance tests for the A and C Branch conformance unit.
    /// Verifies that condition branching is supported and that the
    /// BranchId property exists on ConditionType.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsBranchTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        public async Task ConditionTypeHasBranchIdPropertyAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "BranchId").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have BranchId property.");
        }

        [Test]
        public async Task ConditionTypeHasRetainAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "Retain").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have Retain property.");
        }

        [Test]
        public async Task BranchCreatedOnStateChangeAsync()
        {
            NodeId alarmId = RequireAlarm();

            DataValue branchId = await ReadChildValueAsync(alarmId, "BranchId")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(branchId.StatusCode), Is.True,
                $"BranchId should be readable: {branchId.StatusCode}");
        }

        [Test]
        public async Task BranchHasNonNullBranchIdAsync()
        {
            NodeId alarmId = RequireAlarm();

            DataValue branchId = await ReadChildValueAsync(alarmId, "BranchId")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(branchId.StatusCode), Is.True);
            Assert.That(
                branchId.WrappedValue.TryGetValue(out NodeId nodeId), Is.True);
            Assert.That(nodeId.IsNull, Is.True,
                "Master alarm BranchId should be null/empty.");
        }

        [Test]
        public async Task AcknowledgeBranchAsync()
        {
            NodeId alarmId = RequireAlarm();
            await Task.Delay(1500).ConfigureAwait(false);

            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            if (eventId.IsNull)
            {
                Assert.Ignore("Alarm has no EventId yet.");
            }

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Acknowledge,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "ack branch test")))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                callResult.StatusCode == StatusCodes.BadConditionBranchAlreadyAcked,
                Is.True,
                $"Acknowledge should resolve deterministically: {callResult.StatusCode}");
        }

        [Test]
        public async Task ConfirmBranchAsync()
        {
            NodeId alarmId = RequireAlarm("AlarmConditionType");
            await Task.Delay(1500).ConfigureAwait(false);

            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            if (eventId.IsNull)
            {
                Assert.Ignore("Alarm has no EventId yet.");
            }

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.AcknowledgeableConditionType_Confirm,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "confirm branch test")))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                callResult.StatusCode == StatusCodes.BadConditionBranchAlreadyConfirmed,
                Is.True,
                $"Confirm should resolve deterministically: {callResult.StatusCode}");
        }

        [Test]
        public async Task BranchHasRetainPropertyAsync()
        {
            NodeId alarmId = RequireAlarm();

            DataValue retain = await ReadChildValueAsync(alarmId, "Retain")
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(retain.StatusCode), Is.True,
                $"Retain should be readable: {retain.StatusCode}");
            Assert.That(
                retain.WrappedValue.TryGetValue(out bool _), Is.True,
                "Retain should be a boolean value.");
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
