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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The outcome of resolving a document's type binding against the local
    /// context, per the table in WoT Binding Section 5.2.1.
    /// </summary>
    internal enum WotTypeBindingOutcome
    {
        /// <summary>
        /// The document declares no type binding. The projected node keeps the
        /// default type its Thing Model projects.
        /// </summary>
        None,

        /// <summary>
        /// The binding resolved to exactly one type.
        /// </summary>
        Bound,

        /// <summary>
        /// The document is invalid: the two forms disagree, the name is
        /// ambiguous with nothing to settle it, or the resolved type is of the
        /// wrong NodeClass.
        /// </summary>
        Invalid,

        /// <summary>
        /// The binding names a type the local context does not hold. Section
        /// 5.2.1 fails the projection rather than falling back to
        /// <c>BaseObjectType</c>, because a silently mistyped node is worse
        /// than a reported failure.
        /// </summary>
        Unresolved
    }

    /// <summary>
    /// The type binding a document declares, after resolution.
    /// </summary>
    internal sealed class WotTypeBinding
    {
        /// <summary>
        /// Initializes a resolved binding.
        /// </summary>
        private WotTypeBinding(
            WotTypeBindingOutcome outcome,
            string? nodeId,
            string? detail,
            bool isAmbiguous = false)
        {
            Outcome = outcome;
            NodeId = nodeId;
            Detail = detail;
            IsAmbiguous = isAmbiguous;
        }

        /// <summary>
        /// Gets the outcome.
        /// </summary>
        public WotTypeBindingOutcome Outcome { get; }

        /// <summary>
        /// Gets the bound type's NodeId when <see cref="Outcome"/> is
        /// <see cref="WotTypeBindingOutcome.Bound"/>.
        /// </summary>
        public string? NodeId { get; }

        /// <summary>
        /// Gets the human-readable reason for an <c>Invalid</c> or
        /// <c>Unresolved</c> outcome.
        /// </summary>
        public string? Detail { get; }

        /// <summary>
        /// Gets whether an <c>Invalid</c> outcome is specifically an ambiguous
        /// name, as opposed to the other ways Section 5.2.1 makes a document
        /// invalid. The two are separate outcomes there and so are reported
        /// with separate diagnostic codes.
        /// </summary>
        public bool IsAmbiguous { get; }

        /// <summary>
        /// The document declares no binding.
        /// </summary>
        public static WotTypeBinding None { get; } =
            new WotTypeBinding(WotTypeBindingOutcome.None, null, null);

        /// <summary>
        /// The binding resolved to <paramref name="nodeId"/>.
        /// </summary>
        public static WotTypeBinding Bound(string nodeId)
        {
            return new WotTypeBinding(WotTypeBindingOutcome.Bound, nodeId, null);
        }

        /// <summary>
        /// The document is invalid, for the stated reason.
        /// </summary>
        public static WotTypeBinding Invalid(string detail)
        {
            return new WotTypeBinding(WotTypeBindingOutcome.Invalid, null, detail);
        }

        /// <summary>
        /// The document is invalid because a name is ambiguous and nothing
        /// settles it.
        /// </summary>
        public static WotTypeBinding Ambiguous(string detail)
        {
            return new WotTypeBinding(
                WotTypeBindingOutcome.Invalid, null, detail, isAmbiguous: true);
        }

        /// <summary>
        /// The binding names a type the local context does not hold.
        /// </summary>
        public static WotTypeBinding Unresolved(string detail)
        {
            return new WotTypeBinding(WotTypeBindingOutcome.Unresolved, null, detail);
        }
    }
}
