/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    [TestFixture]
    [Category("StateMachine")]
    public sealed class FiniteStateMachineDispatcherTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void MoveWritesCurrentStateAndLastTransition()
        {
            FluentFiniteStateMachineState machine = StateMachineTestFixtures
                .NewBuilder(m_context)
                .AddState(1, "Idle", isInitial: true)
                .AddState(2, "Ready")
                .AddTransition(10, "IdleToReady", from: 1, to: 2)
                .StateMachine;
            var dispatcher = new FiniteStateMachineDispatcher(
                1,
                [new FiniteStateMachineEntry(1, 100, "Idle"), new FiniteStateMachineEntry(2, 200, "Ready")],
                [new FiniteStateMachineEntry(10, 300, "IdleToReady")]);

            dispatcher.InitializeToInitialState(machine, 1, m_context);
            dispatcher.Move(machine, 2, 10, m_context);

            Assert.That(machine.CurrentState!.Value.Text, Is.EqualTo("Ready"));
            Assert.That(machine.CurrentState.Id!.Value, Is.EqualTo(new NodeId(2, 1)));
            Assert.That(machine.CurrentState.Number!.Value, Is.EqualTo(200u));
            Assert.That(machine.LastTransition!.Value.Text, Is.EqualTo("IdleToReady"));
            Assert.That(machine.LastTransition.Id!.Value, Is.EqualTo(new NodeId(10, 1)));
            Assert.That(machine.LastTransition.Number!.Value, Is.EqualTo(300u));
            Assert.That(dispatcher.TryGetCurrentState(machine, out uint stateId), Is.True);
            Assert.That(stateId, Is.EqualTo(2u));
        }
    }
}
