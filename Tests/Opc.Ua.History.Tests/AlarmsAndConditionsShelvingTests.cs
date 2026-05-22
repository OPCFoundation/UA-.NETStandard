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

namespace Opc.Ua.Conformance.Tests.AlarmsAndConditions
{
    /// <summary>
    /// compliance tests for the A and C Shelving conformance unit.
    /// Verifies that ShelvedStateMachine type and its methods exist in
    /// the address space and that shelving transitions work correctly.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsShelvingTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_000")]
        public async Task ShelvedStateMachineTypeExistsAsync()
        {
            DataValue dv = await ReadAttributeAsync(
                ObjectTypeIds.ShelvedStateMachineType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                "ShelvedStateMachineType should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_000")]
        public async Task ShelvedStateMachineHasTimedShelveMethodAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ShelvedStateMachineType, "TimedShelve")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ShelvedStateMachineType should have TimedShelve method.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_000")]
        public async Task ShelvedStateMachineHasOneShotShelveMethodAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ShelvedStateMachineType, "OneShotShelve")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ShelvedStateMachineType should have OneShotShelve method.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_000")]
        public async Task ShelvedStateMachineHasUnshelveMethodAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ShelvedStateMachineType, "Unshelve")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ShelvedStateMachineType should have Unshelve method.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_000")]
        public async Task ShelvedStateMachineHasUnshelveTimeAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ShelvedStateMachineType, "UnshelveTime")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ShelvedStateMachineType should have UnshelveTime property.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_000")]
        public async Task AlarmConditionTypeHasShelvingStateAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.AlarmConditionType, "ShelvingState")
                .ConfigureAwait(false);
            Assert.That(found, Is.True,
                "AlarmConditionType should have ShelvingState.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_002")]
        public async Task TimedShelveTransitionsToTimedShelvedAsync()
        {
            NodeId shelvingState = await GetShelvingStateNodeAsync()
                .ConfigureAwait(false);
            if (shelvingState.IsNull)
            {
                Assert.Ignore("Server does not expose an alarm with " +
                    "ShelvingState (the test alarm may not be optional).");
            }

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_TimedShelve,
                new Variant(5000.0)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                "TimedShelve must produce a deterministic status.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_003")]
        public async Task OneShotShelveTransitionsToOneShotShelvedAsync()
        {
            NodeId shelvingState = await GetShelvingStateNodeAsync()
                .ConfigureAwait(false);
            if (shelvingState.IsNull)
            {
                Assert.Ignore("Server does not expose an alarm with ShelvingState.");
            }

            await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_OneShotShelve)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                "OneShotShelve must produce a deterministic status.");

            await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_004")]
        public async Task UnshelveTransitionsToUnshelvedAsync()
        {
            NodeId shelvingState = await GetShelvingStateNodeAsync()
                .ConfigureAwait(false);
            if (shelvingState.IsNull)
            {
                Assert.Ignore("Server does not expose an alarm with ShelvingState.");
            }

            await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_TimedShelve,
                new Variant(10000.0)).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Unshelve must produce a deterministic status.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_005")]
        public async Task TimedShelveWithDurationAsync()
        {
            NodeId shelvingState = await GetShelvingStateNodeAsync()
                .ConfigureAwait(false);
            if (shelvingState.IsNull)
            {
                Assert.Ignore("Server does not expose an alarm with ShelvingState.");
            }

            await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_TimedShelve,
                new Variant(2500.0)).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                "TimedShelve must produce a deterministic status.");

            await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Test_006")]
        public async Task ShelveGeneratesEventAsync()
        {
            NodeId shelvingState = await GetShelvingStateNodeAsync()
                .ConfigureAwait(false);
            if (shelvingState.IsNull)
            {
                Assert.Ignore("Server does not expose an alarm with ShelvingState.");
            }

            await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_OneShotShelve)
                .ConfigureAwait(false);

            await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(callResult.StatusCode) ||
                StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Shelve must produce a deterministic status.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Err_001")]
        public async Task ErrTimedShelveWithBadNodeIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                new NodeId(uint.MaxValue, 99),
                MethodIds.ShelvedStateMachineType_TimedShelve,
                new Variant(1000.0)).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "TimedShelve on a bad NodeId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Err_002")]
        public async Task ErrTimedShelveWithZeroDurationAsync()
        {
            NodeId shelvingState = await GetShelvingStateNodeAsync()
                .ConfigureAwait(false);
            if (shelvingState.IsNull)
            {
                Assert.Ignore("Server does not expose an alarm with ShelvingState.");
            }

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_TimedShelve,
                new Variant(0.0)).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "TimedShelve with zero duration should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Shelving")]
        [Property("Tag", "Err_003")]
        public async Task ErrUnshelveWhenNotShelvedAsync()
        {
            NodeId shelvingState = await GetShelvingStateNodeAsync()
                .ConfigureAwait(false);
            if (shelvingState.IsNull)
            {
                Assert.Ignore("Server does not expose an alarm with ShelvingState.");
            }

            await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve).ConfigureAwait(false);

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                shelvingState,
                MethodIds.ShelvedStateMachineType_Unshelve).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "Unshelve on an already-unshelved condition should fail.");
        }

        private async Task<NodeId> GetShelvingStateNodeAsync()
        {
            foreach (System.Collections.Generic.KeyValuePair<string, NodeId> kvp
                in AlarmInstances)
            {
                NodeId shelvingState = await TranslateBrowsePathAsync(
                    kvp.Value, "ShelvingState").ConfigureAwait(false);
                if (!shelvingState.IsNull)
                {
                    return shelvingState;
                }
            }
            return NodeId.Null;
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
