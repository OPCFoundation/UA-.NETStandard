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

namespace Opc.Ua.Machinery.Server.Results
{
    /// <summary>
    /// Evaluates the <c>filter</c> of <c>GetResultIdListFiltered</c> against
    /// one stored result with the NULL semantics of OPC 10000-4 §7.7.3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most result metadata fields are optional, so whether a comparison with
    /// an unset field matches decides what a filter returns. OPC 10000-4
    /// §7.7.3 is explicit: "Operands may contain null values (i.e. values
    /// which do not exist). When this happens, the element always evaluates to
    /// NULL (unless the IsNull operator has been specified)", and NULL combines
    /// through <c>And</c>, <c>Or</c> and <c>Not</c> by Tables 123 and 124.
    /// </para>
    /// <para>
    /// A stored result is a structure, not a node, and whether one of its
    /// optional fields is unset depends on the field's encoding-mask bit, which
    /// the stack's node-oriented <see cref="FilterEvaluator"/> cannot see. This
    /// evaluator resolves those fields and applies the same NULL rules as the
    /// stack's evaluator; the conversion and matching rules come from the stack
    /// (<see cref="Variant.ValueEquals"/>, <see cref="Variant.CompareTo(Variant)"/>,
    /// <see cref="LikePattern"/>).
    /// </para>
    /// <para>
    /// A stored result is not a node, so the operators that need one —
    /// <c>InView</c> and <c>RelatedTo</c> — are FALSE, <c>Cast</c> is NULL, and
    /// an <c>AttributeOperand</c> resolves to null.
    /// </para>
    /// </remarks>
    internal sealed class MachineryResultFilterEvaluator
    {
        private MachineryResultFilterEvaluator(
            ContentFilter filter,
            IFilterContext context,
            IFilterTarget target)
        {
            m_filter = filter;
            m_context = context;
            m_target = target;
        }

        /// <summary>
        /// Returns whether <paramref name="filter"/> selects the result
        /// <paramref name="target"/> presents. An empty filter selects
        /// everything; a filter that evaluates to NULL selects nothing.
        /// </summary>
        /// <param name="filter">A filter that passed <c>ContentFilter.Validate</c>.</param>
        /// <param name="context">The filter context.</param>
        /// <param name="target">The result to evaluate.</param>
        public static bool Evaluate(
            ContentFilter filter,
            IFilterContext context,
            IFilterTarget target)
        {
            if (filter.Elements.Count == 0)
            {
                return true;
            }
            var evaluator = new MachineryResultFilterEvaluator(filter, context, target);
            return evaluator.EvaluateElement(0, depth: 0).TryGetValue(out bool selected) &&
                selected;
        }

        /// <summary>
        /// Compares two non-null values for ordering: strings ordinally, a
        /// <c>LocalizedText</c> by its text, numbers across their built-in
        /// types, and everything else only against the same built-in type.
        /// </summary>
        /// <returns><see langword="false"/> when the two have no order.</returns>
        internal static bool TryCompare(Variant left, Variant right, out int result)
        {
            result = 0;
            if (left.TryGetValue(out string leftText) && right.TryGetValue(out string rightText))
            {
                result = Math.Sign(string.CompareOrdinal(leftText, rightText));
                return true;
            }
            if (left.TryGetValue(out LocalizedText leftLocalized) &&
                right.TryGetValue(out LocalizedText rightLocalized))
            {
                result = Math.Sign(string.CompareOrdinal(leftLocalized.Text, rightLocalized.Text));
                return true;
            }
            BuiltInType leftType = left.TypeInfo.BuiltInType;
            BuiltInType rightType = right.TypeInfo.BuiltInType;
            if (leftType != rightType &&
                !(TypeInfo.IsNumericType(leftType) && TypeInfo.IsNumericType(rightType)))
            {
                return false;
            }
            int compared = left.CompareTo(right);
            if (compared == int.MinValue)
            {
                return false;
            }
            result = Math.Sign(compared);
            return true;
        }

        private Variant EvaluateElement(int index, int depth)
        {
            // Validate() refuses a dangling ElementOperand but not a cycle;
            // no acyclic filter nests deeper than it has elements.
            if (index < 0 || index >= m_filter.Elements.Count || depth > m_filter.Elements.Count)
            {
                return Variant.Null;
            }

            ContentFilterElement element = m_filter.Elements[index];
            FilterOperand[] operands = GetOperands(element);
            return element.FilterOperator switch
            {
                FilterOperator.Equals => Equal(operands, depth),
                FilterOperator.IsNull => Variant.From(Value(operands, 0, depth).IsNull),
                FilterOperator.GreaterThan => Ordered(operands, depth, static c => c > 0),
                FilterOperator.LessThan => Ordered(operands, depth, static c => c < 0),
                FilterOperator.GreaterThanOrEqual => Ordered(operands, depth, static c => c >= 0),
                FilterOperator.LessThanOrEqual => Ordered(operands, depth, static c => c <= 0),
                FilterOperator.Like => Like(operands, depth),
                FilterOperator.Not => Not(operands, depth),
                FilterOperator.Between => Between(operands, depth),
                FilterOperator.InList => InList(operands, depth),
                FilterOperator.And => And(operands, depth),
                FilterOperator.Or => Or(operands, depth),
                FilterOperator.OfType => OfType(operands, depth),
                FilterOperator.BitwiseAnd => Bitwise(operands, depth, and: true),
                FilterOperator.BitwiseOr => Bitwise(operands, depth, and: false),
                FilterOperator.InView or FilterOperator.RelatedTo => Variant.From(false),
                _ => Variant.Null
            };
        }

        private Variant Equal(FilterOperand[] operands, int depth)
        {
            Variant left = Value(operands, 0, depth);
            Variant right = Value(operands, 1, depth);
            if (left.IsNull || right.IsNull)
            {
                return Variant.Null;
            }
            if (left.TryGetValue(out string leftText) && right.TryGetValue(out string rightText))
            {
                return Variant.From(string.Equals(
                    leftText,
                    rightText,
                    ContentFilter.EqualsOperatorDefaultStringComparison));
            }
            return Variant.From(left.ValueEquals(right));
        }

        private Variant Ordered(FilterOperand[] operands, int depth, Func<int, bool> accept)
        {
            Variant left = Value(operands, 0, depth);
            Variant right = Value(operands, 1, depth);
            if (left.IsNull || right.IsNull)
            {
                return Variant.Null;
            }

            // §7.7.3: the operator "resolves to FALSE if no implicit conversion
            // is available and the operands are of different types".
            return Variant.From(TryCompare(left, right, out int result) && accept(result));
        }

        private Variant Between(FilterOperand[] operands, int depth)
        {
            Variant value = Value(operands, 0, depth);
            Variant low = Value(operands, 1, depth);
            Variant high = Value(operands, 2, depth);
            if (value.IsNull || low.IsNull || high.IsNull)
            {
                return Variant.Null;
            }
            return Variant.From(
                TryCompare(value, low, out int aboveLow) && aboveLow >= 0 &&
                TryCompare(value, high, out int belowHigh) && belowHigh <= 0);
        }

        private Variant InList(FilterOperand[] operands, int depth)
        {
            Variant value = Value(operands, 0, depth);
            if (value.IsNull)
            {
                return Variant.Null;
            }
            // InList is TRUE if any Equals is TRUE; a null candidate makes its
            // Equals NULL, so without a match the element is NULL (FALSE OR NULL).
            bool nullCandidate = false;
            for (int ii = 1; ii < operands.Length; ii++)
            {
                Variant candidate = Value(operands, ii, depth);
                if (candidate.IsNull)
                {
                    nullCandidate = true;
                    continue;
                }
                bool match = value.TryGetValue(out string text) &&
                    candidate.TryGetValue(out string candidateText)
                    ? string.Equals(
                        text,
                        candidateText,
                        ContentFilter.EqualsOperatorDefaultStringComparison)
                    : value.ValueEquals(candidate);
                if (match)
                {
                    return Variant.From(true);
                }
            }
            return nullCandidate ? Variant.Null : Variant.From(false);
        }

        private Variant Like(FilterOperand[] operands, int depth)
        {
            Variant value = Value(operands, 0, depth);
            Variant pattern = Value(operands, 1, depth);
            if (value.IsNull || pattern.IsNull)
            {
                return Variant.Null;
            }
            string? text = AsText(value);
            string? patternText = AsText(pattern);
            return Variant.From(
                text != null && patternText != null && LikePattern.IsMatch(text, patternText));
        }

        private Variant Not(FilterOperand[] operands, int depth)
        {
            return Value(operands, 0, depth).TryGetValue(out bool value)
                ? Variant.From(!value)
                : Variant.Null;
        }

        // Tables 123 and 124: FALSE dominates AND, TRUE dominates OR, and
        // anything else involving NULL stays NULL.
        private Variant And(FilterOperand[] operands, int depth)
        {
            bool? left = AsBoolean(Value(operands, 0, depth));
            if (left == false)
            {
                return Variant.From(false);
            }
            bool? right = AsBoolean(Value(operands, 1, depth));
            if (right == false)
            {
                return Variant.From(false);
            }
            return left == true && right == true ? Variant.From(true) : Variant.Null;
        }

        private Variant Or(FilterOperand[] operands, int depth)
        {
            bool? left = AsBoolean(Value(operands, 0, depth));
            if (left == true)
            {
                return Variant.From(true);
            }
            bool? right = AsBoolean(Value(operands, 1, depth));
            if (right == true)
            {
                return Variant.From(true);
            }
            return left == false && right == false ? Variant.From(false) : Variant.Null;
        }

        private Variant OfType(FilterOperand[] operands, int depth)
        {
            return Variant.From(
                Value(operands, 0, depth).TryGetValue(out NodeId typeDefinitionId) &&
                m_target.IsTypeOf(m_context, typeDefinitionId));
        }

        private Variant Bitwise(FilterOperand[] operands, int depth, bool and)
        {
            Variant left = Value(operands, 0, depth);
            Variant right = Value(operands, 1, depth);
            if (left.IsNull || right.IsNull)
            {
                return Variant.Null;
            }
            return and ? left & right : left | right;
        }

        private Variant Value(FilterOperand[] operands, int index, int depth)
        {
            if (index >= operands.Length)
            {
                return Variant.Null;
            }
            return operands[index] switch
            {
                LiteralOperand literal => literal.Value,
                SimpleAttributeOperand attribute => m_target.GetAttributeValue(
                    m_context,
                    attribute.TypeDefinitionId,
                    attribute.BrowsePath,
                    attribute.AttributeId,
                    attribute.ParsedIndexRange),
                ElementOperand element => EvaluateElement((int)element.Index, depth + 1),
                _ => Variant.Null
            };
        }

        private static FilterOperand[] GetOperands(ContentFilterElement element)
        {
            var operands = new FilterOperand[element.FilterOperands.Count];
            for (int ii = 0; ii < operands.Length; ii++)
            {
                operands[ii] = element.FilterOperands[ii].TryGetValue(out FilterOperand? operand)
                    ? operand!
                    : new LiteralOperand(Variant.Null);
            }
            return operands;
        }

        private static bool? AsBoolean(Variant value)
        {
            return value.TryGetValue(out bool result) ? result : null;
        }

        private static string? AsText(Variant value)
        {
            if (value.TryGetValue(out LocalizedText localized))
            {
                return localized.Text;
            }
            return value.TryGetValue(out string text) ? text : null;
        }

        private readonly ContentFilter m_filter;
        private readonly IFilterContext m_context;
        private readonly IFilterTarget m_target;
    }
}
