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
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Server.Tests.StateMachines
{
    /// <summary>
    /// Unit tests for the per-trigger guard sugar on
    /// <see cref="StateMachineBuilder{TState}"/> — covers
    /// <c>WhenTransition</c>, <c>WhenCause</c>, <c>WhenEnter</c>,
    /// and <c>WhenExit</c>. Each guard composes with the others
    /// through the shared registration-order pipeline.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class StateMachineBuilderGuardTests
    {
        private ServerSystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_context = StateMachineTestFixtures.CreateContext();
        }

        [Test]
        public void WhenTransitionVetoesWhenPredicateReturnsFalse()
        {
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .WhenTransition(10, (ctx, m) => false)
                .StateMachine;

            ServiceResult result = sm.DoTransition(m_context, 10, 0, default, []);

            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That((uint)result.Code,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(CurrentStateId(sm), Is.EqualTo(1u));
        }

        [Test]
        public void WhenTransitionAllowsWhenPredicateReturnsTrue()
        {
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .WhenTransition(10, (ctx, m) => true)
                .StateMachine;

            ServiceResult result = sm.DoTransition(m_context, 10, 0, default, []);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(CurrentStateId(sm), Is.EqualTo(2u));
        }

        [Test]
        public void WhenTransitionIgnoresOtherTransitions()
        {
            // A guard scoped to transition 10 must not affect
            // transition 20.
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(2)
                .WhenTransition(10, (ctx, m) => false)
                .StateMachine;

            ServiceResult result = sm.DoTransition(m_context, 20, 0, default, []);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(CurrentStateId(sm), Is.EqualTo(1u));
        }

        [Test]
        public void WhenCauseScopesToCauseId()
        {
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .WhenCause(100, (ctx, m) => false)
                .StateMachine;

            // Cause 100 → transition 10 (per BuildOnOffMachine).
            ServiceResult denied = sm.DoTransition(m_context, 10, 100, default, []);
            Assert.That(ServiceResult.IsBad(denied), Is.True);

            // causeId=0 means "no cause" (machine-driven transition);
            // DoTransition skips the cause-permission check entirely
            // for causeId=0 and the When* guard's predicate is bypassed
            // because the registered cause id doesn't match.
            ServiceResult allowed = sm.DoTransition(m_context, 10, 0, default, []);
            Assert.That(ServiceResult.IsGood(allowed), Is.True);
        }

        [Test]
        public void WhenEnterScopesToToState()
        {
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .WhenEnter(2, (ctx, m) => false)
                .StateMachine;

            // Transition 10 enters state 2 → veto.
            ServiceResult denied = sm.DoTransition(m_context, 10, 0, default, []);
            Assert.That(ServiceResult.IsBad(denied), Is.True);
            Assert.That(CurrentStateId(sm), Is.EqualTo(1u));
        }

        [Test]
        public void WhenEnterIsDefinitionModeOnly()
        {
            // First produce a definition-mode machine, then adopt it
            // via For (lifecycle mode).
            FluentFiniteStateMachineState sm = BuildOnOffMachine().StateMachine;
            StateMachineBuilder<FluentFiniteStateMachineState> lifecycle =
                StateMachineBuilder.For(sm, m_context);

            Assert.That(() => lifecycle.WhenEnter(2, (ctx, m) => true),
                Throws.InvalidOperationException);
        }

        [Test]
        public void WhenExitScopesToFromState()
        {
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(2)
                .WhenExit(2, (ctx, m) => false)
                .StateMachine;

            // Transition 20 leaves state 2 → veto.
            ServiceResult denied = sm.DoTransition(m_context, 20, 0, default, []);
            Assert.That(ServiceResult.IsBad(denied), Is.True);
            Assert.That(CurrentStateId(sm), Is.EqualTo(2u));
        }

        [Test]
        public void MultipleGuardsComposeWithAnd()
        {
            int firstCalls = 0;
            int secondCalls = 0;
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .WhenTransition(10, (ctx, m) => { firstCalls++; return true; })
                .WhenTransition(10, (ctx, m) => { secondCalls++; return false; })
                .StateMachine;

            ServiceResult result = sm.DoTransition(m_context, 10, 0, default, []);

            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(firstCalls, Is.EqualTo(1),
                "first guard runs first (registration order)");
            Assert.That(secondCalls, Is.EqualTo(1),
                "second guard runs after the first passes");
        }

        [Test]
        public void FirstFailingGuardWinsForDenyStatus()
        {
            // Specific deny status from the failing guard flows through
            // DoTransition's return.
            FluentFiniteStateMachineState sm = BuildOnOffMachine()
                .WithInitialState(1)
                .WhenTransition(10, (ctx, m) => false,
                    denyStatus: StatusCodes.BadInvalidState)
                .StateMachine;

            ServiceResult result = sm.DoTransition(m_context, 10, 0, default, []);

            Assert.That((uint)result.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void GuardsRejectNullPredicate()
        {
            StateMachineBuilder<FluentFiniteStateMachineState> b = BuildOnOffMachine();
            Assert.That(() => b.WhenTransition(10, null!),
                Throws.ArgumentNullException);
            Assert.That(() => b.WhenCause(100, null!),
                Throws.ArgumentNullException);
            Assert.That(() => b.WhenEnter(2, null!),
                Throws.ArgumentNullException);
            Assert.That(() => b.WhenExit(1, null!),
                Throws.ArgumentNullException);
        }

        private StateMachineBuilder<FluentFiniteStateMachineState> BuildOnOffMachine()
        {
            return StateMachineTestFixtures.NewBuilder(m_context)
                .AddState(1, "Off", isInitial: true)
                .AddState(2, "On")
                .AddTransition(10, "OffToOn", from: 1, to: 2)
                .AddTransition(20, "OnToOff", from: 2, to: 1)
                .OnCause(100, from: 1, transition: 10)
                .OnCause(200, from: 2, transition: 20);
        }

        private static uint CurrentStateId(FiniteStateMachineState sm)
        {
            if (sm.CurrentState?.Id?.Value is { } id &&
                !id.IsNull &&
                id.TryGetValue(out uint stateId))
            {
                return stateId;
            }
            return 0;
        }
    }
}
