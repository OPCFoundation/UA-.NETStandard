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

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Compares two AAS values for the equivalence clause 6.4 defines: sameness
    /// in the xsd <em>value</em> space, not in the lexical space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the comparison the <c>AAS-LosslessRoundTrip</c> conformance unit
    /// is judged by, and getting it wrong in either direction defeats the
    /// point. Comparing lexically would report a conformant Server as broken
    /// the moment it emitted a canonical form — <c>"1.500000"</c> and
    /// <c>"1.5"</c> are the same <c>xs:decimal</c>, and <c>"1"</c> and
    /// <c>"true"</c> are the same <c>xs:boolean</c>. Comparing too loosely
    /// would let a Server corrupt a value and still pass: <c>"1.5"</c> and
    /// <c>"2.5"</c> are not equivalent, and clause 6.4's negative control
    /// exists to prove the comparison notices.
    /// </para>
    /// <para>
    /// Equivalence is decided by parsing both lexical forms into the OPC UA
    /// value clause 6.3.1 assigns to the declared type and comparing those,
    /// which is what "compared per XSD 1.1 Part 2" amounts to for the types
    /// this specification carries.
    /// </para>
    /// </remarks>
    public static class AasValueSpaceComparer
    {
        /// <summary>
        /// Compares two lexical forms of the same declared type for
        /// equivalence in the xsd value space.
        /// </summary>
        /// <remarks>
        /// Two absent values are equivalent, and an absent value is never
        /// equivalent to a present one — clause 6.1.5 keeps absence
        /// significant, so a comparison that treated an absent field as an
        /// empty string would erase the distinction the round trip is
        /// supposed to preserve.
        /// </remarks>
        /// <param name="left">The first lexical form, or <c>null</c> when absent.</param>
        /// <param name="right">The second lexical form, or <c>null</c> when absent.</param>
        /// <param name="valueType">The declared xsd type of both values.</param>
        /// <returns><c>true</c> when both denote the same element of the value space.</returns>
        public static bool AreEquivalent(
            string? left,
            string? right,
            AASDataTypeDefXsdDataType valueType)
        {
            if (left is null || right is null)
            {
                return left is null && right is null;
            }

            // The fast path is not merely an optimization: a type whose value
            // space is its lexical space, such as xs:string or the Gregorian
            // period types, has nothing further to decide.
            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                return true;
            }

            if (!AasLexicalCanonicalizer.TryCanonicalizeLexical(
                    left, valueType, out string? leftCanonical, out _) ||
                !AasLexicalCanonicalizer.TryCanonicalizeLexical(
                    right, valueType, out string? rightCanonical, out _))
            {
                // A value that does not parse has no place in the value space,
                // so it is equivalent only to an identical spelling, which the
                // ordinal comparison above already ruled out.
                return false;
            }

            return string.Equals(leftCanonical, rightCanonical, StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares two values already materialized into the OPC UA DataType
        /// clause 6.3.1 assigns.
        /// </summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <param name="valueType">The declared xsd type of both values.</param>
        /// <returns><c>true</c> when both denote the same element of the value space.</returns>
        public static bool AreEquivalent(
            in Variant left,
            in Variant right,
            AASDataTypeDefXsdDataType valueType)
        {
            if (left.IsNull || right.IsNull)
            {
                return left.IsNull && right.IsNull;
            }

            return AasLexicalCanonicalizer.TryCanonicalize(
                    left, valueType, out string? leftCanonical, out _) &&
                AasLexicalCanonicalizer.TryCanonicalize(
                    right, valueType, out string? rightCanonical, out _) &&
                string.Equals(leftCanonical, rightCanonical, StringComparison.Ordinal);
        }

        /// <summary>
        /// Reports whether a lexical form is already in the canonical form a
        /// serializer emits.
        /// </summary>
        /// <remarks>
        /// Clause 6.4's negative control asserts that rewriting a value into
        /// its canonical form is <em>not</em> reported as a difference. This is
        /// the predicate that distinguishes a legitimate rewrite from a
        /// corruption when explaining why two documents differ.
        /// </remarks>
        /// <param name="lexical">The lexical form.</param>
        /// <param name="valueType">The declared xsd type.</param>
        /// <returns><c>true</c> when the form is already canonical.</returns>
        public static bool IsCanonical(string? lexical, AASDataTypeDefXsdDataType valueType)
        {
            return lexical is not null &&
                AasLexicalCanonicalizer.TryCanonicalizeLexical(
                    lexical, valueType, out string? canonical, out _) &&
                string.Equals(lexical, canonical, StringComparison.Ordinal);
        }
    }
}
