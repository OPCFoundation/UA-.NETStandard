/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the ConditionState class.
    /// </summary>
    [TestFixture]
    [Category("ConditionState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConditionStateTests
    {
        private ISystemContext m_context;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(m_telemetry);
            // Add OPC UA namespace
            messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            m_context = new SystemContext(m_telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris
            };
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            Utils.SilentDispose(m_context);
        }

        /// <summary>
        /// Test that UpdateStateAfterEnable calls EvaluateRetainStateOnEnable
        /// and that the default implementation sets Retain based on GetRetainState.
        /// </summary>
        [Test]
        public void UpdateStateAfterEnableCallsEvaluateRetainStateOnEnable()
        {
            // Arrange
            var condition = new ConditionState(null);
            condition.Create(m_context, null, new QualifiedName("TestCondition"), null, true);

            // Initially disabled
            condition.SetEnableState(m_context, false);
            Assert.That(condition.EnabledState.Id.Value, Is.False);
            Assert.That(condition.Retain.Value, Is.False);

            // Act - Enable the condition
            condition.SetEnableState(m_context, true);

            // Assert - Enabled state should be true
            Assert.That(condition.EnabledState.Id.Value, Is.True);
            
            // The default implementation should call UpdateRetainState which uses GetRetainState
            // For base ConditionState with no branches, GetRetainState returns false when enabled
            Assert.That(condition.Retain.Value, Is.False);
        }

        /// <summary>
        /// Test that UpdateStateAfterDisable sets Retain to false as per specification.
        /// </summary>
        [Test]
        public void UpdateStateAfterDisableSetsRetainToFalse()
        {
            // Arrange
            var condition = new TestConditionStateWithRetain(null);
            condition.Create(m_context, null, new QualifiedName("TestCondition"), null, true);

            // Enable the condition and set retain to true
            condition.SetEnableState(m_context, true);
            condition.ForceRetain(true);
            Assert.That(condition.Retain.Value, Is.True);

            // Act - Disable the condition
            condition.SetEnableState(m_context, false);

            // Assert - Retain should be false per specification
            Assert.That(condition.EnabledState.Id.Value, Is.False);
            Assert.That(condition.Retain.Value, Is.False);
        }

        /// <summary>
        /// Test that a derived class can override EvaluateRetainStateOnEnable
        /// to provide custom logic for determining the Retain value.
        /// </summary>
        [Test]
        public void DerivedClassCanOverrideEvaluateRetainStateOnEnable()
        {
            // Arrange
            var condition = new TestConditionStateWithCustomRetain(null);
            condition.Create(m_context, null, new QualifiedName("TestCondition"), null, true);

            // Initially disabled
            condition.SetEnableState(m_context, false);
            Assert.That(condition.Retain.Value, Is.False);

            // Act - Enable the condition
            condition.SetEnableState(m_context, true);

            // Assert - The custom implementation should have been called
            Assert.That(condition.EnabledState.Id.Value, Is.True);
            Assert.That(condition.EvaluateCalled, Is.True);
            Assert.That(condition.Retain.Value, Is.True);  // Custom logic always sets to true
        }

        /// <summary>
        /// Test condition that exposes method to force Retain value for testing.
        /// </summary>
        private sealed class TestConditionStateWithRetain : ConditionState
        {
            public TestConditionStateWithRetain(NodeState parent) : base(parent) { }

            public void ForceRetain(bool value)
            {
                Retain.Value = value;
            }
        }

        /// <summary>
        /// Test condition that overrides EvaluateRetainStateOnEnable.
        /// </summary>
        private sealed class TestConditionStateWithCustomRetain : ConditionState
        {
            public bool EvaluateCalled { get; private set; }

            public TestConditionStateWithCustomRetain(NodeState parent) : base(parent) { }

            protected override void EvaluateRetainStateOnEnable(ISystemContext context)
            {
                EvaluateCalled = true;
                // Custom logic: always set Retain to true when enabled
                Retain.Value = true;
            }
        }
    }
}
