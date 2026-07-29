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

using System.Collections.Generic;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Encodes and decodes the Job Control V2 receiver state machine as the array
    /// of <see cref="V2.ISA95StateDataType"/> defined by OPC-10031-4 V2 (7.3.2).
    /// <para>
    /// The array always begins with a top-level entry whose
    /// <see cref="V2.ISA95StateDataType.BrowsePath"/> carries no elements (the
    /// generated data type coerces the specification's null top-level path to an
    /// empty <see cref="RelativePath"/>) and whose
    /// <see cref="V2.ISA95StateDataType.StateNumber"/> is the number of the
    /// top-level receiver state (1 <c>NotAllowedToStart</c>, 2
    /// <c>AllowedToStart</c>, 3 <c>Running</c>, 4 <c>Interrupted</c>, 5
    /// <c>Ended</c>, 6 <c>Aborted</c>). The interrupted and ended states carry an
    /// additional sub-state entry whose <c>BrowsePath</c> is a
    /// <see cref="RelativePath"/> that targets the <c>InterruptedSubstates</c> or
    /// <c>EndedSubstates</c> sub-state machine, and whose <c>StateNumber</c> is the
    /// number of the current sub-state (1 <c>Held</c>/<c>Completed</c>, 2
    /// <c>Suspended</c>/<c>Closed</c>) as declared by the generated state machine.
    /// </para>
    /// Decoding is driven by the browse path of the sub-state entry and the
    /// sub-state number, never by out-of-band composite numbers.
    /// </summary>
    internal static class Isa95V2StateMachine
    {
        /// <summary>
        /// The top-level receiver state number for the <c>NotAllowedToStart</c>
        /// state.
        /// </summary>
        public const uint NotAllowedToStartNumber = 1;

        /// <summary>
        /// The top-level receiver state number for the <c>AllowedToStart</c> state.
        /// </summary>
        public const uint AllowedToStartNumber = 2;

        /// <summary>
        /// The top-level receiver state number for the <c>Running</c> state.
        /// </summary>
        public const uint RunningNumber = 3;

        /// <summary>
        /// The top-level receiver state number for the <c>Interrupted</c> state.
        /// </summary>
        public const uint InterruptedNumber = 4;

        /// <summary>
        /// The top-level receiver state number for the <c>Ended</c> state.
        /// </summary>
        public const uint EndedNumber = 5;

        /// <summary>
        /// The top-level receiver state number for the <c>Aborted</c> state.
        /// </summary>
        public const uint AbortedNumber = 6;

        /// <summary>
        /// The sub-state number shared by <c>Held</c> and <c>Completed</c>.
        /// </summary>
        public const uint FirstSubstateNumber = 1;

        /// <summary>
        /// The sub-state number shared by <c>Suspended</c> and <c>Closed</c>.
        /// </summary>
        public const uint SecondSubstateNumber = 2;

        /// <summary>
        /// The browse name of the interrupted sub-state machine.
        /// </summary>
        public const string InterruptedSubstates = V2.BrowseNames.InterruptedSubstates;

        /// <summary>
        /// The browse name of the ended sub-state machine.
        /// </summary>
        public const string EndedSubstates = V2.BrowseNames.EndedSubstates;

        /// <summary>
        /// Gets the top-level receiver state number and text for a canonical state.
        /// </summary>
        public static (uint Number, string Text) TopLevel(Isa95JobCanonicalState state)
        {
            return state switch
            {
                Isa95JobCanonicalState.NotAllowedToStart => (NotAllowedToStartNumber, "NotAllowedToStart"),
                Isa95JobCanonicalState.AllowedToStart => (AllowedToStartNumber, "AllowedToStart"),
                Isa95JobCanonicalState.Running => (RunningNumber, "Running"),
                Isa95JobCanonicalState.Loaded => (RunningNumber, "Running"),
                Isa95JobCanonicalState.Held => (InterruptedNumber, "Interrupted"),
                Isa95JobCanonicalState.Suspended => (InterruptedNumber, "Interrupted"),
                Isa95JobCanonicalState.Completed => (EndedNumber, "Ended"),
                Isa95JobCanonicalState.Closed => (EndedNumber, "Ended"),
                Isa95JobCanonicalState.Aborted => (AbortedNumber, "Aborted"),
                Isa95JobCanonicalState.Error => (AbortedNumber, "Aborted"),
                _ => (NotAllowedToStartNumber, "NotAllowedToStart")
            };
        }

        /// <summary>
        /// Builds the Job Control V2 state array for a canonical state.
        /// </summary>
        public static ArrayOf<V2.ISA95StateDataType> ToStateArray(Isa95JobCanonicalState state)
        {
            (uint number, string text) = TopLevel(state);
            var states = new List<V2.ISA95StateDataType> { TopLevelEntry(number, text) };
            switch (state)
            {
                case Isa95JobCanonicalState.Held:
                    states.Add(SubstateEntry(InterruptedSubstates, FirstSubstateNumber, "Held"));
                    break;
                case Isa95JobCanonicalState.Suspended:
                    states.Add(SubstateEntry(InterruptedSubstates, SecondSubstateNumber, "Suspended"));
                    break;
                case Isa95JobCanonicalState.Completed:
                    states.Add(SubstateEntry(EndedSubstates, FirstSubstateNumber, "Completed"));
                    break;
                case Isa95JobCanonicalState.Closed:
                    states.Add(SubstateEntry(EndedSubstates, SecondSubstateNumber, "Closed"));
                    break;
            }
            return states.ToArrayOf();
        }

        /// <summary>
        /// Derives a canonical state from a Job Control V2 state array by inspecting
        /// the sub-state browse path and number, falling back to the top-level state
        /// number. An interrupted or ended top-level entry without a sub-state entry
        /// collapses to <c>Held</c> or <c>Completed</c> respectively.
        /// </summary>
        public static Isa95JobCanonicalState FromStateArray(ArrayOf<V2.ISA95StateDataType> state)
        {
            Isa95JobCanonicalState? substate = null;
            Isa95JobCanonicalState? topLevel = null;
            foreach (V2.ISA95StateDataType entry in state)
            {
                string? substateMachine = SubstateMachineName(entry.BrowsePath);
                if (string.Equals(substateMachine, InterruptedSubstates, System.StringComparison.Ordinal))
                {
                    substate = entry.StateNumber == SecondSubstateNumber
                        ? Isa95JobCanonicalState.Suspended
                        : Isa95JobCanonicalState.Held;
                }
                else if (string.Equals(substateMachine, EndedSubstates, System.StringComparison.Ordinal))
                {
                    substate = entry.StateNumber == SecondSubstateNumber
                        ? Isa95JobCanonicalState.Closed
                        : Isa95JobCanonicalState.Completed;
                }
                else
                {
                    topLevel = FromTopLevelNumber(entry.StateNumber);
                }
            }
            return substate ?? topLevel ?? Isa95JobCanonicalState.NotAllowedToStart;
        }

        /// <summary>
        /// Determines whether a candidate canonical state satisfies a Job Control V2
        /// state query. A query that specifies only a top-level interrupted or ended
        /// state (with no sub-state entry) matches either of that group's sub-states.
        /// </summary>
        public static bool Matches(Isa95JobCanonicalState candidate, ArrayOf<V2.ISA95StateDataType> query)
        {
            uint topLevelNumber = 0;
            bool hasSubstate = false;
            Isa95JobCanonicalState substate = default;
            foreach (V2.ISA95StateDataType entry in query)
            {
                string? substateMachine = SubstateMachineName(entry.BrowsePath);
                if (string.Equals(substateMachine, InterruptedSubstates, System.StringComparison.Ordinal))
                {
                    hasSubstate = true;
                    substate = entry.StateNumber == SecondSubstateNumber
                        ? Isa95JobCanonicalState.Suspended
                        : Isa95JobCanonicalState.Held;
                }
                else if (string.Equals(substateMachine, EndedSubstates, System.StringComparison.Ordinal))
                {
                    hasSubstate = true;
                    substate = entry.StateNumber == SecondSubstateNumber
                        ? Isa95JobCanonicalState.Closed
                        : Isa95JobCanonicalState.Completed;
                }
                else if (entry.StateNumber is >= NotAllowedToStartNumber and <= AbortedNumber)
                {
                    topLevelNumber = entry.StateNumber;
                }
            }

            if (hasSubstate)
            {
                return candidate == substate;
            }
            if (topLevelNumber == 0)
            {
                return false;
            }
            return TopLevel(candidate).Number == topLevelNumber;
        }

        /// <summary>
        /// Gets the top-level receiver state number carried by the first meaningful
        /// entry of a Job Control V2 state query, or zero when none is valid.
        /// </summary>
        public static uint QueryTopLevelNumber(ArrayOf<V2.ISA95StateDataType> query)
        {
            foreach (V2.ISA95StateDataType entry in query)
            {
                if (SubstateMachineName(entry.BrowsePath) == null &&
                    entry.StateNumber is >= NotAllowedToStartNumber and <= AbortedNumber)
                {
                    return entry.StateNumber;
                }
            }
            return 0;
        }

        private static Isa95JobCanonicalState FromTopLevelNumber(uint number)
        {
            return number switch
            {
                NotAllowedToStartNumber => Isa95JobCanonicalState.NotAllowedToStart,
                AllowedToStartNumber => Isa95JobCanonicalState.AllowedToStart,
                RunningNumber => Isa95JobCanonicalState.Running,
                InterruptedNumber => Isa95JobCanonicalState.Held,
                EndedNumber => Isa95JobCanonicalState.Completed,
                AbortedNumber => Isa95JobCanonicalState.Aborted,
                _ => Isa95JobCanonicalState.NotAllowedToStart
            };
        }

        private static V2.ISA95StateDataType TopLevelEntry(uint number, string text)
        {
            return new V2.ISA95StateDataType
            {
                BrowsePath = new RelativePath(),
                StateText = new LocalizedText(text),
                StateNumber = number
            };
        }

        private static V2.ISA95StateDataType SubstateEntry(string substateMachine, uint number, string text)
        {
#pragma warning disable IDE0002 // Qualification selects the core UA identifier class, not ISA-95 generated identifiers.
            return new V2.ISA95StateDataType
            {
                BrowsePath = new RelativePath(
                    Opc.Ua.ReferenceTypeIds.HasSubStateMachine,
                    new QualifiedName(substateMachine)),
                StateText = new LocalizedText(text),
                StateNumber = number
            };
#pragma warning restore IDE0002
        }

        private static string? SubstateMachineName(RelativePath? browsePath)
        {
            if (browsePath == null || browsePath.Elements.Count == 0)
            {
                return null;
            }
            return browsePath.Elements[0]?.TargetName.Name;
        }
    }
}
