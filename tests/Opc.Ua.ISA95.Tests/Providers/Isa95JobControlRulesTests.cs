// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.ISA95.JobControl;

namespace Opc.Ua.ISA95.Tests.Providers
{
    [TestFixture]
    public class Isa95JobControlRulesTests
    {
        [TestCase(Isa95JobCanonicalState.NotAllowedToStartWaiting, Isa95JobOperation.MarkReady,
            Isa95JobCanonicalState.NotAllowedToStartReady)]
        [TestCase(Isa95JobCanonicalState.AllowedToStartReady, Isa95JobOperation.Load,
            Isa95JobCanonicalState.AllowedToStartLoaded)]
        [TestCase(Isa95JobCanonicalState.NotAllowedToStartReady, Isa95JobOperation.MarkNotReady,
            Isa95JobCanonicalState.NotAllowedToStartWaiting)]
        [TestCase(Isa95JobCanonicalState.AllowedToStartLoaded, Isa95JobOperation.Unload,
            Isa95JobCanonicalState.AllowedToStartReady)]
        [TestCase(Isa95JobCanonicalState.AllowedToStartLoaded, Isa95JobOperation.MarkNotReady,
            Isa95JobCanonicalState.AllowedToStartWaiting)]
        [TestCase(Isa95JobCanonicalState.Completed, Isa95JobOperation.Close, Isa95JobCanonicalState.Closed)]
        public void SubstateTransitionsUpdateTheOrderWithoutAnEvent(
            Isa95JobCanonicalState current,
            Isa95JobOperation operation,
            Isa95JobCanonicalState expected)
        {
            Isa95JobDecision result = Isa95JobControlRules.Evaluate(current, operation, Isa95JobControlVersion.V2);
            Assert.That(result, Is.EqualTo(new Isa95JobDecision(
                Isa95JobMutation.Update, expected, Isa95JobReturnStatusBits.Success, false)));
        }

        [Test]
        public void V1StartDoesNotBypassReadinessOnV2Orders()
        {
            Isa95JobDecision store = Isa95JobControlRules.Evaluate(
                null, Isa95JobOperation.Store, Isa95JobControlVersion.V2);
            Isa95JobDecision start = Isa95JobControlRules.Evaluate(
                store.State, Isa95JobOperation.Start, Isa95JobControlVersion.V1);
            Assert.That(start.State, Is.EqualTo(Isa95JobCanonicalState.AllowedToStartWaiting));
            Isa95JobDecision begin = Isa95JobControlRules.Evaluate(
                start.State, Isa95JobOperation.BeginExecution, Isa95JobControlVersion.V2);
            Assert.That(begin.ReturnStatus, Is.EqualTo(Isa95JobReturnStatusBits.InvalidStatus));
            Assert.That(begin.Mutation, Is.EqualTo(Isa95JobMutation.None));
        }

        [Test]
        public void StartAndRevokeStartKeepPreparationSubstate()
        {
            Isa95JobDecision start = Isa95JobControlRules.Evaluate(
                Isa95JobCanonicalState.NotAllowedToStartLoaded, Isa95JobOperation.Start, Isa95JobControlVersion.V2);
            Assert.That(start.State, Is.EqualTo(Isa95JobCanonicalState.AllowedToStartLoaded));
            Assert.That(start.PublishStatusEvent, Is.True);
            Isa95JobDecision revoke = Isa95JobControlRules.Evaluate(
                start.State, Isa95JobOperation.RevokeStart, Isa95JobControlVersion.V2);
            Assert.That(revoke.State, Is.EqualTo(Isa95JobCanonicalState.NotAllowedToStartLoaded));
        }
    }
}
