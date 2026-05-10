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
    /// compliance tests for the A and C Comment conformance unit.
    /// Verifies that AddComment methods exist on the type system and
    /// that comments can be added to conditions.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("AlarmsAndConditions")]
    public class AlarmsAndConditionsCommentTests : AlarmsAndConditionsTestFixture
    {
        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Test_000")]
        public async Task ConditionTypeHasAddCommentMethodAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "AddComment").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have AddComment method.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Test_000")]
        public async Task ConditionTypeHasCommentPropertyAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "Comment").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have Comment property.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Test_000")]
        public async Task ConditionTypeHasClientUserIdAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "ClientUserId").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have ClientUserId property.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Test_000")]
        public async Task ConditionTypeHasLastSeverityAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "LastSeverity").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have LastSeverity property.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Test_000")]
        public async Task ConditionTypeHasQualityAsync()
        {
            bool found = await TypeHasChildAsync(
                ObjectTypeIds.ConditionType, "Quality").ConfigureAwait(false);
            Assert.That(found, Is.True,
                "ConditionType should have Quality property.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Err_004")]
        public async Task ErrAddCommentWithBadNodeIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                new NodeId(uint.MaxValue, 99),
                MethodIds.ConditionType_AddComment,
                new Variant(default(ByteString)),
                new Variant(new LocalizedText("en", "no node")))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "AddComment on a bad NodeId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Err_003")]
        public async Task ErrAddCommentWithInvalidMethodArgsAsync()
        {
            NodeId alarmId = RequireAlarm();

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_AddComment).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "AddComment with no arguments should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Err_002")]
        public async Task ErrAddCommentWithBadEventIdAsync()
        {
            NodeId alarmId = RequireAlarm();

            var badEventId = new ByteString(new byte[] {
                0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB
            });

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_AddComment,
                new Variant(badEventId),
                new Variant(new LocalizedText("en", "test"))).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "AddComment with an unknown EventId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Err_006")]
        public async Task ErrAddCommentWithNullEventIdAsync()
        {
            NodeId alarmId = RequireAlarm();

            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_AddComment,
                new Variant(default(ByteString)),
                new Variant(new LocalizedText("en", "no event")))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "AddComment with a null EventId should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "N/A")]
        public async Task ErrAddCommentOnDisabledConditionAsync()
        {
            NodeId alarmId = RequireAlarm();

            await Task.Delay(1500).ConfigureAwait(false);

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Disable).ConfigureAwait(false);

            ByteString eventId = await ReadEventIdAsync(alarmId).ConfigureAwait(false);
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_AddComment,
                new Variant(eventId),
                new Variant(new LocalizedText("en", "while disabled")))
                .ConfigureAwait(false);

            await CallMethodOnAlarmAsync(
                alarmId,
                MethodIds.ConditionType_Enable).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "AddComment on a disabled condition should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "A and C Comment")]
        [Property("Tag", "Err_005")]
        public async Task ErrAddCommentWithWrongObjectIdAsync()
        {
            CallMethodResult callResult = await CallMethodOnAlarmAsync(
                ObjectTypeIds.ConditionType,
                MethodIds.ConditionType_AddComment,
                new Variant(default(ByteString)),
                new Variant(new LocalizedText("en", "x"))).ConfigureAwait(false);

            Assert.That(StatusCode.IsBad(callResult.StatusCode), Is.True,
                "AddComment with the type-system ObjectId should fail.");
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
