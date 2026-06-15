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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Tests.StateMachine
{
    /// <summary>
    /// Exhaustive coverage for the <see cref="PubSubStateMachine"/>
    /// transition table and parent / child propagation rules per OPC UA
    /// Part 14 §6.2.1 (PubSubState), §9.1.10 (Enable / Disable rejection
    /// preconditions), and §9.1.3.5 (RemoveConnection: children must be
    /// disabled before the parent itself transitions to Disabled).
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.1", Summary = "PubSubState enum and transition model")]
    [TestSpec("9.1.10", Summary = "Enable / Disable / state report rules")]
    [TestSpec("9.1.3.5", Summary = "Disable children before parent on removal")]
    public class PubSubStateMachineTests
    {
        private static PubSubStateMachine NewMachine(
            string name = "M",
            PubSubComponentKind kind = PubSubComponentKind.Connection)
            => new(name, kind, NullLogger.Instance);

        [Test]
        public void Constructor_SeedsDisabledStateAndStatusCode()
        {
            PubSubStateMachine sut = NewMachine();
            Assert.Multiple(() =>
            {
                Assert.That(sut.State, Is.EqualTo(PubSubState.Disabled));
                Assert.That(sut.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(sut.Parent, Is.Null);
                Assert.That(sut.Children, Is.Empty);
                Assert.That(sut.ComponentName, Is.EqualTo("M"));
                Assert.That(sut.ComponentKind, Is.EqualTo(PubSubComponentKind.Connection));
            });
        }

        [Test]
        public void Constructor_RejectsNullName()
        {
            Assert.That(
                () => new PubSubStateMachine(null!, PubSubComponentKind.Connection, NullLogger.Instance),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Constructor_RejectsEmptyName()
        {
            Assert.That(
                () => new PubSubStateMachine(string.Empty, PubSubComponentKind.Connection, NullLogger.Instance),
                Throws.ArgumentException);
        }

        [Test]
        public void Constructor_RejectsNullLogger()
        {
            Assert.That(
                () => new PubSubStateMachine("M", PubSubComponentKind.Connection, null!),
                Throws.ArgumentNullException);
        }

        // ------------------------------------------------------------------
        // Enable (Disabled -> PreOperational) — Part 14 §9.1.10.2
        // ------------------------------------------------------------------

        [Test]
        [TestSpec("9.1.10.2", Summary = "Enable from Disabled is allowed")]
        public void TryEnable_FromDisabled_TransitionsToPreOperational()
        {
            PubSubStateMachine sut = NewMachine();
            PubSubStateChangedEventArgs? captured = null;
            sut.StateChanged += (_, e) => captured = e;

            bool result = sut.TryEnable();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(sut.State, Is.EqualTo(PubSubState.PreOperational));
                Assert.That(sut.StatusCode, Is.EqualTo((StatusCode)StatusCodes.GoodCallAgain));
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.PreviousState, Is.EqualTo(PubSubState.Disabled));
                Assert.That(captured.NewState, Is.EqualTo(PubSubState.PreOperational));
                Assert.That(captured.Reason, Is.EqualTo(PubSubStateTransitionReason.ByMethod));
                Assert.That(captured.ComponentName, Is.EqualTo("M"));
                Assert.That(captured.ComponentKind, Is.EqualTo(PubSubComponentKind.Connection));
            });
        }

        [Test]
        [TestSpec("9.1.10.2", Summary = "Enable from PreOperational/Operational/Paused/Error is rejected")]
        public void TryEnable_FromNonDisabledStates_IsRejected(
            [Values(
                PubSubState.PreOperational,
                PubSubState.Operational,
                PubSubState.Paused,
                PubSubState.Error)]
            PubSubState startState)
        {
            PubSubStateMachine sut = SetupInState(startState);
            int events = 0;
            sut.StateChanged += (_, _) => events++;

            bool result = sut.TryEnable();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.False);
                Assert.That(sut.State, Is.EqualTo(startState));
                Assert.That(events, Is.Zero);
            });
        }

        // ------------------------------------------------------------------
        // PreOperational -> Operational (and Error -> Operational recovery)
        // ------------------------------------------------------------------

        [Test]
        public void TryMarkOperational_FromPreOperational_Transitions()
        {
            PubSubStateMachine sut = SetupInState(PubSubState.PreOperational);
            Assert.Multiple(() =>
            {
                Assert.That(sut.TryMarkOperational(), Is.True);
                Assert.That(sut.State, Is.EqualTo(PubSubState.Operational));
                Assert.That(sut.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            });
        }

        [Test]
        public void TryMarkOperational_FromError_RecoversToOperational()
        {
            PubSubStateMachine sut = SetupInState(PubSubState.Error);
            Assert.Multiple(() =>
            {
                Assert.That(sut.TryMarkOperational(PubSubStateTransitionReason.FromError), Is.True);
                Assert.That(sut.State, Is.EqualTo(PubSubState.Operational));
            });
        }

        [Test]
        public void TryMarkOperational_FromDisabledOrPaused_IsRejected(
            [Values(PubSubState.Disabled, PubSubState.Paused)] PubSubState startState)
        {
            PubSubStateMachine sut = SetupInState(startState);
            bool result = sut.TryMarkOperational();
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.False);
                Assert.That(sut.State, Is.EqualTo(startState));
            });
        }

        [Test]
        [TestSpec("9.1.10", Summary = "TryMarkOperational from Operational is rejected (strict transition)")]
        public void TryMarkOperational_FromOperational_IsRejected_AndNoEventFires()
        {
            // The allowed source set for MarkOperational is {PreOperational, Error}.
            // Operational is NOT in that set, so the call is rejected. This is
            // intentional: idempotent same-state re-assertion is not part of the
            // public API; callers must observe State first.
            PubSubStateMachine sut = SetupInState(PubSubState.Operational);
            int events = 0;
            sut.StateChanged += (_, _) => events++;
            Assert.Multiple(() =>
            {
                Assert.That(sut.TryMarkOperational(), Is.False);
                Assert.That(sut.State, Is.EqualTo(PubSubState.Operational));
                Assert.That(events, Is.Zero);
            });
        }

        // ------------------------------------------------------------------
        // Pause / Resume
        // ------------------------------------------------------------------

        [Test]
        public void TryPause_FromOperational_Transitions()
        {
            PubSubStateMachine sut = SetupInState(PubSubState.Operational);
            Assert.That(sut.TryPause(), Is.True);
            Assert.That(sut.State, Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void TryPause_FromPreOperational_Transitions()
        {
            PubSubStateMachine sut = SetupInState(PubSubState.PreOperational);
            Assert.That(sut.TryPause(), Is.True);
            Assert.That(sut.State, Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void TryPause_FromDisabledOrError_IsRejected(
            [Values(PubSubState.Disabled, PubSubState.Error)] PubSubState startState)
        {
            PubSubStateMachine sut = SetupInState(startState);
            Assert.That(sut.TryPause(), Is.False);
            Assert.That(sut.State, Is.EqualTo(startState));
        }

        [Test]
        public void TryResume_FromPaused_TransitionsToOperational()
        {
            PubSubStateMachine sut = SetupInState(PubSubState.Paused);
            Assert.That(sut.TryResume(), Is.True);
            Assert.That(sut.State, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void TryResume_FromAnyOtherState_IsRejected(
            [Values(
                PubSubState.Disabled,
                PubSubState.PreOperational,
                PubSubState.Operational,
                PubSubState.Error)]
            PubSubState startState)
        {
            PubSubStateMachine sut = SetupInState(startState);
            Assert.That(sut.TryResume(), Is.False);
            Assert.That(sut.State, Is.EqualTo(startState));
        }

        // ------------------------------------------------------------------
        // Fault / Error path
        // ------------------------------------------------------------------

        [Test]
        public void TryFault_FromAnyNonDisabledState_MovesToError(
            [Values(
                PubSubState.PreOperational,
                PubSubState.Operational,
                PubSubState.Paused,
                PubSubState.Error)]
            PubSubState startState)
        {
            PubSubStateMachine sut = SetupInState(startState);
            bool result = sut.TryFault(StatusCodes.BadCommunicationError);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(sut.State, Is.EqualTo(PubSubState.Error));
                Assert.That(sut.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadCommunicationError));
            });
        }

        [Test]
        public void TryFault_FromDisabled_IsRejected()
        {
            PubSubStateMachine sut = NewMachine();
            bool result = sut.TryFault(StatusCodes.BadCommunicationError);
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.False);
                Assert.That(sut.State, Is.EqualTo(PubSubState.Disabled));
            });
        }

        // ------------------------------------------------------------------
        // Disable (Part 14 §9.1.10.3)
        // ------------------------------------------------------------------

        [Test]
        [TestSpec("9.1.10.3", Summary = "Disable from already-Disabled is rejected")]
        public void TryDisable_FromAlreadyDisabled_IsRejected()
        {
            PubSubStateMachine sut = NewMachine();
            bool result = sut.TryDisable();
            Assert.That(result, Is.False);
        }

        [Test]
        public void TryDisable_FromAnyNonDisabledState_TransitionsToDisabled(
            [Values(
                PubSubState.PreOperational,
                PubSubState.Operational,
                PubSubState.Paused,
                PubSubState.Error)]
            PubSubState startState)
        {
            PubSubStateMachine sut = SetupInState(startState);
            Assert.That(sut.TryDisable(), Is.True);
            Assert.That(sut.State, Is.EqualTo(PubSubState.Disabled));
        }

        // ------------------------------------------------------------------
        // Parent / Child cascade — Part 14 §9.1.3.5
        // ------------------------------------------------------------------

        [Test]
        [TestSpec("9.1.3.5", Summary = "Children disabled before parent on cascading Disable")]
        public void TryDisable_DisablesChildrenBeforeSelf_InOrder()
        {
            PubSubStateMachine parent = SetupInState(PubSubState.Operational, "parent");
            PubSubStateMachine child1 = SetupInState(PubSubState.Operational, "child1");
            PubSubStateMachine child2 = SetupInState(PubSubState.Operational, "child2");
            parent.AttachChild(child1);
            parent.AttachChild(child2);

            var observed = new List<(string Component, PubSubState State, PubSubStateTransitionReason Reason)>();
            EventHandler<PubSubStateChangedEventArgs> handler = (_, e) =>
                observed.Add((e.ComponentName, e.NewState, e.Reason));
            parent.StateChanged += handler;
            child1.StateChanged += handler;
            child2.StateChanged += handler;

            bool result = parent.TryDisable();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(parent.State, Is.EqualTo(PubSubState.Disabled));
                Assert.That(child1.State, Is.EqualTo(PubSubState.Disabled));
                Assert.That(child2.State, Is.EqualTo(PubSubState.Disabled));
                Assert.That(observed, Has.Count.EqualTo(3));
                // Children disabled before parent.
                Assert.That(observed[0].Component, Is.EqualTo("child1"));
                Assert.That(observed[0].Reason, Is.EqualTo(PubSubStateTransitionReason.ByParent));
                Assert.That(observed[1].Component, Is.EqualTo("child2"));
                Assert.That(observed[1].Reason, Is.EqualTo(PubSubStateTransitionReason.ByParent));
                Assert.That(observed[2].Component, Is.EqualTo("parent"));
                Assert.That(observed[2].Reason, Is.EqualTo(PubSubStateTransitionReason.ByMethod));
            });
        }

        [Test]
        public void TryDisable_RemovedReason_PropagatesRemovedToChildren()
        {
            PubSubStateMachine parent = SetupInState(PubSubState.Operational, "p");
            PubSubStateMachine child = SetupInState(PubSubState.Operational, "c");
            parent.AttachChild(child);

            PubSubStateTransitionReason? childReason = null;
            child.StateChanged += (_, e) => childReason = e.Reason;

            parent.TryDisable(PubSubStateTransitionReason.Removed);

            Assert.That(childReason, Is.EqualTo(PubSubStateTransitionReason.Removed));
        }

        [Test]
        public void TryPauseCascade_PausesAllPausableChildrenThenSelf()
        {
            PubSubStateMachine parent = SetupInState(PubSubState.Operational, "p");
            PubSubStateMachine child1 = SetupInState(PubSubState.Operational, "c1");
            PubSubStateMachine child2 = SetupInState(PubSubState.Operational, "c2");
            parent.AttachChild(child1);
            parent.AttachChild(child2);

            Assert.That(parent.TryPauseCascade(), Is.True);
            Assert.That(parent.State, Is.EqualTo(PubSubState.Paused));
            Assert.That(child1.State, Is.EqualTo(PubSubState.Paused));
            Assert.That(child2.State, Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void TryPauseCascade_RecursesIntoGrandchildren()
        {
            PubSubStateMachine app = SetupInState(PubSubState.Operational, "app");
            PubSubStateMachine conn = SetupInState(PubSubState.Operational, "conn");
            PubSubStateMachine group = SetupInState(PubSubState.Operational, "group");
            app.AttachChild(conn);
            conn.AttachChild(group);

            app.TryPauseCascade();

            Assert.That(group.State, Is.EqualTo(PubSubState.Paused));
            Assert.That(conn.State, Is.EqualTo(PubSubState.Paused));
            Assert.That(app.State, Is.EqualTo(PubSubState.Paused));
        }

        [Test]
        public void AttachChild_NullChild_Throws()
        {
            PubSubStateMachine parent = NewMachine();
            Assert.That(() => parent.AttachChild(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void AttachChild_SelfReference_Throws()
        {
            PubSubStateMachine sut = NewMachine();
            Assert.That(() => sut.AttachChild(sut), Throws.InvalidOperationException);
        }

        [Test]
        public void AttachChild_DoubleParent_Throws()
        {
            PubSubStateMachine parent1 = NewMachine("p1");
            PubSubStateMachine parent2 = NewMachine("p2");
            PubSubStateMachine child = NewMachine("c");
            parent1.AttachChild(child);
            Assert.That(() => parent2.AttachChild(child), Throws.InvalidOperationException);
        }

        [Test]
        public void DetachChild_RemovesParentLink()
        {
            PubSubStateMachine parent = NewMachine("p");
            PubSubStateMachine child = NewMachine("c");
            parent.AttachChild(child);
            parent.DetachChild(child);
            Assert.That(child.Parent, Is.Null);
            Assert.That(parent.Children, Is.Empty);
        }

        [Test]
        public void DetachChild_OfUnknownChild_IsNoOp()
        {
            PubSubStateMachine parent = NewMachine("p");
            PubSubStateMachine other = NewMachine("o");
            Assert.That(() => parent.DetachChild(other), Throws.Nothing);
            Assert.That(other.Parent, Is.Null);
        }

        [Test]
        public void DetachChild_NullArgument_Throws()
        {
            PubSubStateMachine parent = NewMachine("p");
            Assert.That(() => parent.DetachChild(null!), Throws.ArgumentNullException);
        }

        // ------------------------------------------------------------------
        // Removal / disposed semantics
        // ------------------------------------------------------------------

        [Test]
        public void MarkRemoved_DisablesAndDetachesFromParent()
        {
            PubSubStateMachine parent = NewMachine("p");
            PubSubStateMachine child = SetupInState(PubSubState.Operational, "c");
            parent.AttachChild(child);

            child.MarkRemoved();

            Assert.Multiple(() =>
            {
                Assert.That(child.State, Is.EqualTo(PubSubState.Disabled));
                Assert.That(parent.Children, Is.Empty);
            });
        }

        [Test]
        public void MarkRemoved_IsIdempotent()
        {
            PubSubStateMachine sut = SetupInState(PubSubState.Operational);
            sut.MarkRemoved();
            Assert.That(() => sut.MarkRemoved(), Throws.Nothing);
        }

        [Test]
        public void AttachChild_AfterMarkRemoved_Throws()
        {
            PubSubStateMachine sut = NewMachine();
            sut.MarkRemoved();
            PubSubStateMachine child = NewMachine("c");
            Assert.That(() => sut.AttachChild(child), Throws.InvalidOperationException);
        }

        [Test]
        public void Transition_AfterMarkRemoved_Throws()
        {
            PubSubStateMachine sut = NewMachine();
            sut.MarkRemoved();
            Assert.That(() => sut.TryEnable(), Throws.InvalidOperationException);
        }

        // ------------------------------------------------------------------
        // Diagnostics: StateChanged handler exceptions must not destabilise
        // ------------------------------------------------------------------

        [Test]
        public void StateChanged_HandlerException_IsSwallowedAndStateRemains()
        {
            PubSubStateMachine sut = NewMachine();
            sut.StateChanged += (_, _) => throw new InvalidOperationException("bad listener");
            Assert.That(() => sut.TryEnable(), Throws.Nothing);
            Assert.That(sut.State, Is.EqualTo(PubSubState.PreOperational));
        }

        // ------------------------------------------------------------------
        // DefaultStatusCodeFor utility
        // ------------------------------------------------------------------

        public static IEnumerable<TestCaseData> DefaultStatusCodeFor_TestCases()
        {
            yield return new TestCaseData(PubSubState.Operational, (StatusCode)StatusCodes.Good);
            yield return new TestCaseData(PubSubState.Paused, (StatusCode)StatusCodes.GoodNoData);
            yield return new TestCaseData(PubSubState.PreOperational, (StatusCode)StatusCodes.GoodCallAgain);
            yield return new TestCaseData(PubSubState.Error, (StatusCode)StatusCodes.BadInternalError);
            yield return new TestCaseData(PubSubState.Disabled, (StatusCode)StatusCodes.BadInvalidState);
        }

        [Test]
        [TestCaseSource(nameof(DefaultStatusCodeFor_TestCases))]
        public void DefaultStatusCodeFor_KnownState_ReturnsCanonicalStatus(
            PubSubState state, StatusCode expected)
        {
            StatusCode code = PubSubStateMachine.DefaultStatusCodeFor(state);
            Assert.That(code, Is.EqualTo(expected));
        }

        [Test]
        public void DefaultStatusCodeFor_OutOfRangeState_ReturnsBadUnexpected()
        {
            StatusCode code = PubSubStateMachine.DefaultStatusCodeFor((PubSubState)99);
            Assert.That(code, Is.EqualTo((StatusCode)StatusCodes.BadUnexpectedError));
        }

        // ------------------------------------------------------------------
        // PubSubStateChangedEventArgs constructor argument guards
        // ------------------------------------------------------------------

        [Test]
        public void EventArgs_NullComponentName_Throws()
        {
            Assert.That(
                () => new PubSubStateChangedEventArgs(
                    null!,
                    PubSubComponentKind.Connection,
                    PubSubState.Disabled,
                    PubSubState.PreOperational,
                    PubSubStateTransitionReason.ByMethod,
                    StatusCodes.Good),
                Throws.ArgumentNullException);
        }

        [Test]
        public void EventArgs_ValidArguments_ExposesAllProperties()
        {
            var evt = new PubSubStateChangedEventArgs(
                "C",
                PubSubComponentKind.DataSetReader,
                PubSubState.Operational,
                PubSubState.Error,
                PubSubStateTransitionReason.Fatal,
                StatusCodes.BadCommunicationError);

            Assert.Multiple(() =>
            {
                Assert.That(evt.ComponentName, Is.EqualTo("C"));
                Assert.That(evt.ComponentKind, Is.EqualTo(PubSubComponentKind.DataSetReader));
                Assert.That(evt.PreviousState, Is.EqualTo(PubSubState.Operational));
                Assert.That(evt.NewState, Is.EqualTo(PubSubState.Error));
                Assert.That(evt.Reason, Is.EqualTo(PubSubStateTransitionReason.Fatal));
                Assert.That(evt.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadCommunicationError));
            });
        }

        // ------------------------------------------------------------------
        // Threading sanity: concurrent transitions never corrupt state
        // ------------------------------------------------------------------

        [Test]
        public async Task ConcurrentTransitions_LeaveMachineInConsistentState()
        {
            PubSubStateMachine sut = SetupInState(PubSubState.Operational);
            var tasks = new List<Task>();
            for (int i = 0; i < 32; i++)
            {
                tasks.Add(Task.Run(() => sut.TryPause()));
                tasks.Add(Task.Run(() => sut.TryResume()));
                tasks.Add(Task.Run(() => sut.TryFault(StatusCodes.BadCommunicationError)));
                tasks.Add(Task.Run(() => sut.TryMarkOperational(PubSubStateTransitionReason.FromError)));
            }
            await Task.WhenAll(tasks);
            // Final state must be one of the four reachable states; never Disabled (we didn't disable).
            Assert.That(
                sut.State,
                Is.AnyOf(
                    PubSubState.Operational,
                    PubSubState.Paused,
                    PubSubState.Error));
        }

        private static PubSubStateMachine SetupInState(
            PubSubState target,
            string name = "M",
            PubSubComponentKind kind = PubSubComponentKind.Connection)
        {
            var sut = new PubSubStateMachine(name, kind, NullLogger.Instance);
            switch (target)
            {
                case PubSubState.Disabled:
                    break;
                case PubSubState.PreOperational:
                    Assert.That(sut.TryEnable(), Is.True);
                    break;
                case PubSubState.Operational:
                    Assert.That(sut.TryEnable(), Is.True);
                    Assert.That(sut.TryMarkOperational(), Is.True);
                    break;
                case PubSubState.Paused:
                    Assert.That(sut.TryEnable(), Is.True);
                    Assert.That(sut.TryMarkOperational(), Is.True);
                    Assert.That(sut.TryPause(), Is.True);
                    break;
                case PubSubState.Error:
                    Assert.That(sut.TryEnable(), Is.True);
                    Assert.That(sut.TryFault(StatusCodes.BadCommunicationError), Is.True);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }
            return sut;
        }
    }
}
