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
            m_states = new ElementState[filter.Elements.Count];
            m_values = new Variant[filter.Elements.Count];
            m_likeSteps = ContentFilter.MaxLikeMatchSteps;
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
            return evaluator.EvaluateElement(0).TryGetValue(out bool selected) &&
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

        /// <summary>
        /// Evaluates one element at most once per result.
        /// </summary>
        /// <remarks>
        /// Several operands may refer to the same element, so evaluating an
        /// element again for every reference costs a number of evaluations
        /// exponential in the number of elements: each element of a chain of
        /// <c>And(Element(i+1), Element(i+1))</c> doubles the work of the one
        /// before it. An element's value depends only on the result under
        /// evaluation, so it is kept and every later reference reads it. The
        /// same record answers a cycle, which <c>Validate()</c> does not refuse:
        /// an element reached again while it is still being evaluated has no
        /// value and is NULL.
        /// </remarks>
        private Variant EvaluateElement(int index)
        {
            if (index < 0 || index >= m_states.Length)
            {
                return Variant.Null;
            }
            switch (m_states[index])
            {
                case ElementState.Evaluated:
                    return m_values[index];
                case ElementState.Evaluating:
                    return Variant.Null;
            }

            m_states[index] = ElementState.Evaluating;
            Variant value = EvaluateOperator(m_filter.Elements[index]);
            m_values[index] = value;
            m_states[index] = ElementState.Evaluated;
            return value;
        }

        private Variant EvaluateOperator(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element);
            return element.FilterOperator switch
            {
                FilterOperator.Equals => Equal(operands),
                FilterOperator.IsNull => Variant.From(Value(operands, 0).IsNull),
                FilterOperator.GreaterThan => Ordered(operands,static c => c > 0),
                FilterOperator.LessThan => Ordered(operands,static c => c < 0),
                FilterOperator.GreaterThanOrEqual => Ordered(operands,static c => c >= 0),
                FilterOperator.LessThanOrEqual => Ordered(operands,static c => c <= 0),
                FilterOperator.Like => Like(operands),
                FilterOperator.Not => Not(operands),
                FilterOperator.Between => Between(operands),
                FilterOperator.InList => InList(operands),
                FilterOperator.And => And(operands),
                FilterOperator.Or => Or(operands),
                FilterOperator.OfType => OfType(operands),
                FilterOperator.BitwiseAnd => Bitwise(operands,and: true),
                FilterOperator.BitwiseOr => Bitwise(operands,and: false),
                FilterOperator.InView or FilterOperator.RelatedTo => Variant.From(false),
                _ => Variant.Null
            };
        }

        private Variant Equal(FilterOperand[] operands)
        {
            Variant left = Value(operands, 0);
            Variant right = Value(operands, 1);
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

        private Variant Ordered(FilterOperand[] operands,Func<int, bool> accept)
        {
            Variant left = Value(operands, 0);
            Variant right = Value(operands, 1);
            if (left.IsNull || right.IsNull)
            {
                return Variant.Null;
            }

            // §7.7.3: the operator "resolves to FALSE if no implicit conversion
            // is available and the operands are of different types".
            return Variant.From(TryCompare(left, right, out int result) && accept(result));
        }

        private Variant Between(FilterOperand[] operands)
        {
            Variant value = Value(operands, 0);
            Variant low = Value(operands, 1);
            Variant high = Value(operands, 2);
            if (value.IsNull || low.IsNull || high.IsNull)
            {
                return Variant.Null;
            }
            return Variant.From(
                TryCompare(value, low, out int aboveLow) && aboveLow >= 0 &&
                TryCompare(value, high, out int belowHigh) && belowHigh <= 0);
        }

        private Variant InList(FilterOperand[] operands)
        {
            Variant value = Value(operands, 0);
            if (value.IsNull)
            {
                return Variant.Null;
            }
            // InList is TRUE if any Equals is TRUE; a null candidate makes its
            // Equals NULL, so without a match the element is NULL (FALSE OR NULL).
            bool nullCandidate = false;
            for (int ii = 1; ii < operands.Length; ii++)
            {
                Variant candidate = Value(operands, ii);
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

        private Variant Like(FilterOperand[] operands)
        {
            Variant value = Value(operands, 0);
            Variant pattern = Value(operands, 1);
            if (value.IsNull || pattern.IsNull)
            {
                return Variant.Null;
            }
            string? text = AsText(value);
            string? patternText = AsText(pattern);
            if (text == null || patternText == null)
            {
                return Variant.From(false);
            }

            // The Like elements of one evaluation share a budget of matching
            // steps, as they do in the stack's evaluator: parsing costs a step
            // per pattern character and matching up to the product of both
            // lengths. An element that runs out of it is NULL, like any other
            // element that cannot be evaluated.
            if (patternText.Length > m_likeSteps)
            {
                m_likeSteps = 0;
                return Variant.Null;
            }
            m_likeSteps -= patternText.Length;
            if (!LikePattern.TryParse(patternText, out LikePattern? likePattern))
            {
                return Variant.From(false);
            }
            return likePattern.TryMatch(text, ref m_likeSteps, out bool isMatch)
                ? Variant.From(isMatch)
                : Variant.Null;
        }

        private Variant Not(FilterOperand[] operands)
        {
            return Value(operands, 0).TryGetValue(out bool value)
                ? Variant.From(!value)
                : Variant.Null;
        }

        // Tables 123 and 124: FALSE dominates AND, TRUE dominates OR, and
        // anything else involving NULL stays NULL.
        private Variant And(FilterOperand[] operands)
        {
            bool? left = AsBoolean(Value(operands, 0));
            if (left == false)
            {
                return Variant.From(false);
            }
            bool? right = AsBoolean(Value(operands, 1));
            if (right == false)
            {
                return Variant.From(false);
            }
            return left == true && right == true ? Variant.From(true) : Variant.Null;
        }

        private Variant Or(FilterOperand[] operands)
        {
            bool? left = AsBoolean(Value(operands, 0));
            if (left == true)
            {
                return Variant.From(true);
            }
            bool? right = AsBoolean(Value(operands, 1));
            if (right == true)
            {
                return Variant.From(true);
            }
            return left == false && right == false ? Variant.From(false) : Variant.Null;
        }

        private Variant OfType(FilterOperand[] operands)
        {
            return Variant.From(
                Value(operands, 0).TryGetValue(out NodeId typeDefinitionId) &&
                m_target.IsTypeOf(m_context, typeDefinitionId));
        }

        private Variant Bitwise(FilterOperand[] operands,bool and)
        {
            Variant left = Value(operands, 0);
            Variant right = Value(operands, 1);
            if (left.IsNull || right.IsNull)
            {
                return Variant.Null;
            }
            return and ? left & right : left | right;
        }

        private Variant Value(FilterOperand[] operands, int index)
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
                ElementOperand element => element.Index < (uint)m_states.Length
                    ? EvaluateElement((int)element.Index)
                    : Variant.Null,
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
        private readonly ElementState[] m_states;
        private readonly Variant[] m_values;
        private int m_likeSteps;

        private enum ElementState : byte
        {
            NotEvaluated,
            Evaluating,
            Evaluated
        }
    }
}
