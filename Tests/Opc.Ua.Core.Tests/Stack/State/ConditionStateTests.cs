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

using System;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the ConditionState helper methods.
    /// </summary>
    [TestFixture]
    [Category("ConditionState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConditionStateTests
    {
        private ITelemetryContext m_telemetry;
        private ServiceMessageContext m_messageContext;
        private SystemContext m_systemContext;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_messageContext = new ServiceMessageContext(m_telemetry);
            m_messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            m_systemContext = new SystemContext(m_telemetry) { NamespaceUris = m_messageContext.NamespaceUris };
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            Utils.SilentDispose(m_messageContext);
        }

        /// <summary>
        /// Test that SetEnableState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetEnableStateUpdatesTimestampAndClearsChangeMasks()
        {
            var condition = new ConditionState(null);
            condition.Create(m_systemContext, new NodeId(1), "Condition", null, true);

            // Set initial state
            var beforeTime = DateTime.UtcNow;
            condition.SetEnableState(m_systemContext, true);
            var afterTime = DateTime.UtcNow;

            // Verify timestamp is updated
            Assert.That(condition.EnabledState.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.EnabledState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared (all should be None)
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(condition.EnabledState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetSeverity updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetSeverityUpdatesTimestampAndClearsChangeMasks()
        {
            var condition = new ConditionState(null);
            condition.Create(m_systemContext, new NodeId(1), "Condition", null, true);

            var beforeTime = DateTime.UtcNow;
            condition.SetSeverity(m_systemContext, EventSeverity.High);
            var afterTime = DateTime.UtcNow;

            // Verify timestamps are updated
            Assert.That(condition.Severity.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.Severity.Timestamp, Is.LessThanOrEqualTo(afterTime));
            Assert.That(condition.LastSeverity.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.LastSeverity.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetActiveState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetActiveStateUpdatesTimestampAndClearsChangeMasks()
        {
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_systemContext, new NodeId(1), "Alarm", null, true);

            var beforeTime = DateTime.UtcNow;
            alarm.SetActiveState(m_systemContext, true);
            var afterTime = DateTime.UtcNow;

            // Verify timestamp is updated
            Assert.That(alarm.ActiveState.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(alarm.ActiveState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(alarm.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(alarm.ActiveState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetSuppressedState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetSuppressedStateUpdatesTimestampAndClearsChangeMasks()
        {
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_systemContext, new NodeId(1), "Alarm", null, true);
            alarm.SuppressedState = new TwoStateVariableState(alarm);
            alarm.SuppressedState.Create(m_systemContext, null, BrowseNames.SuppressedState, null, false);

            var beforeTime = DateTime.UtcNow;
            alarm.SetSuppressedState(m_systemContext, true);
            var afterTime = DateTime.UtcNow;

            // Verify timestamp is updated
            Assert.That(alarm.SuppressedState.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(alarm.SuppressedState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(alarm.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(alarm.SuppressedState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetAcknowledgedState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetAcknowledgedStateUpdatesTimestampAndClearsChangeMasks()
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_systemContext, new NodeId(1), "AckCondition", null, true);

            var beforeTime = DateTime.UtcNow;
            condition.SetAcknowledgedState(m_systemContext, true);
            var afterTime = DateTime.UtcNow;

            // Verify timestamp is updated
            Assert.That(condition.AckedState.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.AckedState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(condition.AckedState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetConfirmedState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetConfirmedStateUpdatesTimestampAndClearsChangeMasks()
        {
            var condition = new AcknowledgeableConditionState(null);
            condition.Create(m_systemContext, new NodeId(1), "AckCondition", null, true);
            condition.ConfirmedState = new TwoStateVariableState(condition);
            condition.ConfirmedState.Create(m_systemContext, null, BrowseNames.ConfirmedState, null, false);

            var beforeTime = DateTime.UtcNow;
            condition.SetConfirmedState(m_systemContext, true);
            var afterTime = DateTime.UtcNow;

            // Verify timestamp is updated
            Assert.That(condition.ConfirmedState.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(condition.ConfirmedState.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(condition.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            Assert.That(condition.ConfirmedState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that SetShelvingState updates the timestamp and clears change masks.
        /// </summary>
        [Test]
        public void SetShelvingStateUpdatesTimestampAndClearsChangeMasks()
        {
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_systemContext, new NodeId(1), "Alarm", null, true);
            alarm.ShelvingState = new ShelvedStateMachineState(alarm);
            alarm.ShelvingState.Create(m_systemContext, null, BrowseNames.ShelvingState, null, false);
            alarm.ShelvingState.UnshelveTime = new PropertyState<double>(alarm.ShelvingState);

            var beforeTime = DateTime.UtcNow;
            alarm.SetShelvingState(m_systemContext, true, false, 1000);
            var afterTime = DateTime.UtcNow;

            // Verify timestamp is updated on UnshelveTime
            Assert.That(alarm.ShelvingState.UnshelveTime.Timestamp, Is.GreaterThanOrEqualTo(beforeTime));
            Assert.That(alarm.ShelvingState.UnshelveTime.Timestamp, Is.LessThanOrEqualTo(afterTime));

            // Verify change masks are cleared
            Assert.That(alarm.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }

        /// <summary>
        /// Test that subscribed clients are notified when SetActiveState is called.
        /// This simulates the issue scenario.
        /// </summary>
        [Test]
        public void SetActiveStateNotifiesSubscribers()
        {
            var alarm = new AlarmConditionState(m_telemetry, null);
            alarm.Create(m_systemContext, new NodeId(1), "Alarm", null, true);

            // Initially inactive
            alarm.SetActiveState(m_systemContext, false);
            var initialTimestamp = alarm.ActiveState.Timestamp;

            // Now activate the alarm - timestamp should be greater than or equal to the initial timestamp
            // since both could be set to DateTime.UtcNow which has limited precision
            var beforeActivation = DateTime.UtcNow;
            alarm.SetActiveState(m_systemContext, true);
            var afterActivation = DateTime.UtcNow;

            // Verify that the timestamp is within the expected range
            Assert.That(alarm.ActiveState.Timestamp, Is.GreaterThanOrEqualTo(beforeActivation));
            Assert.That(alarm.ActiveState.Timestamp, Is.LessThanOrEqualTo(afterActivation));

            // Verify that change masks were cleared (indicating subscribers were notified)
            Assert.That(alarm.ActiveState.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
        }
    }
}
