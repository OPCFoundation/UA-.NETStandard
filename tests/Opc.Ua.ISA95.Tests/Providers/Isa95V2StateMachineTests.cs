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
using Opc.Ua.ISA95.Server.Providers;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Tests.Providers
{
    /// <summary>
    /// Verifies that the Job Control V2 receiver state machine is encoded exactly
    /// as OPC-10031-4 V2 (7.3.2) requires: a top-level entry with a null browse
    /// path and the top-level state number, plus an interrupted or ended sub-state
    /// entry whose browse path targets the generated <c>InterruptedSubstates</c> or
    /// <c>EndedSubstates</c> sub-state machine and whose state number is the
    /// generated sub-state number (1 or 2), never an out-of-band composite number.
    /// </summary>
    [TestFixture]
    public class Isa95V2StateMachineTests
    {
        [Test]
        public void SubstateBrowseNamesMatchTheGeneratedModel()
        {
            Assert.That(Isa95V2StateMachine.InterruptedSubstates,
                Is.EqualTo(V2.BrowseNames.InterruptedSubstates));
            Assert.That(Isa95V2StateMachine.EndedSubstates,
                Is.EqualTo(V2.BrowseNames.EndedSubstates));
        }

        [Test]
        public void SimpleStatesEncodeAsASingleTopLevelEntryWithNullPath()
        {
            AssertTopLevelOnly(Isa95JobCanonicalState.NotAllowedToStart, 1);
            AssertTopLevelOnly(Isa95JobCanonicalState.AllowedToStart, 2);
            AssertTopLevelOnly(Isa95JobCanonicalState.Running, 3);
            AssertTopLevelOnly(Isa95JobCanonicalState.Aborted, 6);
        }

        [Test]
        public void HeldEncodesAsInterruptedSubstateOne()
        {
            AssertComposite(
                Isa95JobCanonicalState.Held,
                topNumber: 4,
                topText: "Interrupted",
                substateMachine: V2.BrowseNames.InterruptedSubstates,
                substateNumber: 1,
                substateText: "Held");
        }

        [Test]
        public void SuspendedEncodesAsInterruptedSubstateTwo()
        {
            AssertComposite(
                Isa95JobCanonicalState.Suspended,
                topNumber: 4,
                topText: "Interrupted",
                substateMachine: V2.BrowseNames.InterruptedSubstates,
                substateNumber: 2,
                substateText: "Suspended");
        }

        [Test]
        public void CompletedEncodesAsEndedSubstateOne()
        {
            AssertComposite(
                Isa95JobCanonicalState.Completed,
                topNumber: 5,
                topText: "Ended",
                substateMachine: V2.BrowseNames.EndedSubstates,
                substateNumber: 1,
                substateText: "Completed");
        }

        [Test]
        public void ClosedEncodesAsEndedSubstateTwo()
        {
            AssertComposite(
                Isa95JobCanonicalState.Closed,
                topNumber: 5,
                topText: "Ended",
                substateMachine: V2.BrowseNames.EndedSubstates,
                substateNumber: 2,
                substateText: "Closed");
        }

        [Test]
        public void EveryCanonicalStateRoundTripsThroughTheStateArray()
        {
            foreach (Isa95JobCanonicalState state in new[]
            {
                Isa95JobCanonicalState.NotAllowedToStart,
                Isa95JobCanonicalState.AllowedToStart,
                Isa95JobCanonicalState.Running,
                Isa95JobCanonicalState.Held,
                Isa95JobCanonicalState.Suspended,
                Isa95JobCanonicalState.Completed,
                Isa95JobCanonicalState.Aborted,
                Isa95JobCanonicalState.Closed
            })
            {
                ArrayOf<V2.ISA95StateDataType> encoded = Isa95V2StateMachine.ToStateArray(state);
                Assert.That(Isa95V2StateMachine.FromStateArray(encoded), Is.EqualTo(state));
            }
        }

        [Test]
        public void NoStateEverUsesAnOutOfBandCompositeNumber()
        {
            foreach (Isa95JobCanonicalState state in new[]
            {
                Isa95JobCanonicalState.Held,
                Isa95JobCanonicalState.Suspended,
                Isa95JobCanonicalState.Completed,
                Isa95JobCanonicalState.Closed
            })
            {
                foreach (V2.ISA95StateDataType entry in Isa95V2StateMachine.ToStateArray(state))
                {
                    Assert.That(entry.StateNumber, Is.LessThanOrEqualTo(6u));
                    Assert.That(entry.StateNumber, Is.GreaterThanOrEqualTo(1u));
                }
            }
        }

        [Test]
        public void InterruptedTopLevelQueryMatchesBothSubstates()
        {
            ArrayOf<V2.ISA95StateDataType> query =
            [
                new V2.ISA95StateDataType
                {
                    BrowsePath = new RelativePath(),
                    StateNumber = 4,
                    StateText = new LocalizedText("Interrupted")
                }
            ];

            Assert.That(Isa95V2StateMachine.Matches(Isa95JobCanonicalState.Held, query), Is.True);
            Assert.That(Isa95V2StateMachine.Matches(Isa95JobCanonicalState.Suspended, query), Is.True);
            Assert.That(Isa95V2StateMachine.Matches(Isa95JobCanonicalState.Running, query), Is.False);
        }

        [Test]
        public void SuspendedSubstateQueryMatchesOnlySuspended()
        {
            ArrayOf<V2.ISA95StateDataType> query =
                Isa95V2StateMachine.ToStateArray(Isa95JobCanonicalState.Suspended);

            Assert.That(Isa95V2StateMachine.Matches(Isa95JobCanonicalState.Suspended, query), Is.True);
            Assert.That(Isa95V2StateMachine.Matches(Isa95JobCanonicalState.Held, query), Is.False);
        }

        private static void AssertTopLevelOnly(Isa95JobCanonicalState state, uint number)
        {
            ArrayOf<V2.ISA95StateDataType> encoded = Isa95V2StateMachine.ToStateArray(state);
            Assert.That(encoded.Count, Is.EqualTo(1));
            Assert.That(encoded[0].StateNumber, Is.EqualTo(number));
            AssertNullPath(encoded[0]);
        }

        private static void AssertComposite(
            Isa95JobCanonicalState state,
            uint topNumber,
            string topText,
            string substateMachine,
            uint substateNumber,
            string substateText)
        {
            ArrayOf<V2.ISA95StateDataType> encoded = Isa95V2StateMachine.ToStateArray(state);
            Assert.That(encoded.Count, Is.EqualTo(2));

            V2.ISA95StateDataType top = encoded[0];
            Assert.That(top.StateNumber, Is.EqualTo(topNumber));
            Assert.That(top.StateText.Text, Is.EqualTo(topText));
            AssertNullPath(top);

            V2.ISA95StateDataType sub = encoded[1];
            Assert.That(sub.StateNumber, Is.EqualTo(substateNumber));
            Assert.That(sub.StateText.Text, Is.EqualTo(substateText));
            Assert.That(sub.BrowsePath, Is.Not.Null);
            Assert.That(sub.BrowsePath.Elements.Count, Is.EqualTo(1));
            Assert.That(sub.BrowsePath.Elements[0].TargetName.Name, Is.EqualTo(substateMachine));
#pragma warning disable IDE0002 // Qualification selects the core UA identifier class, not ISA-95 generated identifiers.
            Assert.That(sub.BrowsePath.Elements[0].ReferenceTypeId,
                Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasSubStateMachine));
#pragma warning restore IDE0002
        }

        private static void AssertNullPath(V2.ISA95StateDataType entry)
        {
            // The specification requires a null top-level browse path; the generated
            // data type coerces null to an empty relative path with no elements.
            Assert.That(entry.BrowsePath == null || entry.BrowsePath.Elements.Count == 0, Is.True);
        }
    }
}
