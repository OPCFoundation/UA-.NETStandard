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

using NUnit.Framework;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Guards the default element-NodeId convention on
    /// <see cref="FiniteStateMachineState"/>. The
    /// <c>GetStateNodeId</c> / <c>GetStateId</c> /
    /// <c>GetTransitionNodeId</c> hooks exist so the fluent builder can
    /// point <c>CurrentState/Id</c> at per-instance state nodes; every
    /// stack-shipped and generator-emitted machine must keep reporting
    /// the numeric <c>(elementId, ElementNamespaceIndex)</c> form.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class FiniteStateMachineElementIdTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void StackStateMachineKeepsTheNumericElementConvention()
        {
            var sm = new ExclusiveLimitStateMachineState(null);
            sm.Create(
                m_context,
                new NodeId(4200, 1),
                new QualifiedName("LimitState", 1),
                new LocalizedText("LimitState"),
                true);

            Assert.Multiple(() =>
            {
                Assert.That(
                    sm.GetStateNodeId(Objects.ExclusiveLimitStateMachineType_High),
                    Is.EqualTo(new NodeId(Objects.ExclusiveLimitStateMachineType_High)));
                Assert.That(
                    sm.GetTransitionNodeId(
                        Objects.ExclusiveLimitStateMachineType_HighToHighHigh),
                    Is.EqualTo(new NodeId(
                        Objects.ExclusiveLimitStateMachineType_HighToHighHigh)));
                Assert.That(
                    sm.GetStateId(new NodeId(Objects.ExclusiveLimitStateMachineType_Low)),
                    Is.EqualTo(Objects.ExclusiveLimitStateMachineType_Low));

                // OnAfterCreate seeds CurrentState with High.
                Assert.That(
                    sm.CurrentState!.Id!.Value,
                    Is.EqualTo(new NodeId(Objects.ExclusiveLimitStateMachineType_High)));
            });
        }

        [Test]
        public void ZeroElementIdsMapToNull()
        {
            var sm = new ExclusiveLimitStateMachineState(null);

            Assert.Multiple(() =>
            {
                Assert.That(sm.GetStateNodeId(0), Is.EqualTo(NodeId.Null));
                Assert.That(sm.GetTransitionNodeId(0), Is.EqualTo(NodeId.Null));
                Assert.That(sm.GetStateId(NodeId.Null), Is.Zero);
            });
        }
    }
}
