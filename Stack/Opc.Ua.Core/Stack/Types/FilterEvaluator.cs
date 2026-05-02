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

using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// This class contains functions used to evaluate a ContentFilter and report the
    /// results of the evaluation.
    /// </summary>
    public sealed
#if NET8_0_OR_GREATER
        partial
#endif
        class FilterEvaluator
    {
        /// <summary>
        /// Create evaluator
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="context"></param>
        /// <param name="target"></param>
        public FilterEvaluator(ContentFilter filter, IFilterContext context, IFilterTarget target)
        {
            m_filter = filter;
            m_context = context;
            m_target = target;
            m_logger = context.Telemetry.CreateLogger<FilterEvaluator>();
        }

        /// <summary>
        /// Evaluates the first element in the ContentFilter. If the first or any
        /// subsequent element has dependent elements, the dependent elements are
        /// evaluated before the root element (recursive descent). Elements which
        /// are not linked (directly or indirectly) to the first element will not
        /// be evaluated (they have no influence on the result).
        /// </summary>
        /// <returns>Returns true, false or null.</returns>
        public bool Result
        {
            get
            {
                // check if nothing to do.
                if (m_filter.Elements.Count == 0)
                {
                    return true;
                }

                if (Evaluate(0).TryGet(out bool result))
                {
                    return result;
                }
                return false;
            }
        }

        /// <summary>
        /// Evaluates element at the specified index.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant Evaluate(int index)
        {
            // get the element to evaluate.
            ContentFilterElement element = m_filter.Elements[index];

            switch (element.FilterOperator)
            {
                case FilterOperator.And:
                    return And(element);
                case FilterOperator.Or:
                    return Or(element);
                case FilterOperator.Not:
                    return Not(element);
                case FilterOperator.Equals:
                    return Equals(element);
                case FilterOperator.GreaterThan:
                    return GreaterThan(element);
                case FilterOperator.GreaterThanOrEqual:
                    return GreaterThanOrEqual(element);
                case FilterOperator.LessThan:
                    return LessThan(element);
                case FilterOperator.LessThanOrEqual:
                    return LessThanOrEqual(element);
                case FilterOperator.Between:
                    return Between(element);
                case FilterOperator.InList:
                    return InList(element);
                case FilterOperator.Like:
                    return Like(element);
                case FilterOperator.IsNull:
                    return IsNull(element);
                case FilterOperator.Cast:
                    return Cast(element);
                case FilterOperator.OfType:
                    return OfType(element);
                case FilterOperator.InView:
                    return InView(element);
                case FilterOperator.RelatedTo:
                    return RelatedTo(element);
                case FilterOperator.BitwiseAnd:
                    return BitwiseAnd(element);
                case FilterOperator.BitwiseOr:
                    return BitwiseOr(element);
                default:
                    throw ServiceResultException.Unexpected(
                        $"FilterOperator {element.FilterOperator} is not recognized.");
            }
        }

        /// <summary>
        /// Returns the operands for the element.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static FilterOperand[] GetOperands(ContentFilterElement element, int expectedCount)
        {
            var operands = new FilterOperand[element.FilterOperands.Count];

            int ii = 0;

            foreach (ExtensionObject extension in element.FilterOperands)
            {
                if (extension.IsNull)
                {
                    throw ServiceResultException.Unexpected("FilterOperand is null.");
                }

                if (!extension.TryGetEncodeable(out FilterOperand? operand))
                {
                    throw ServiceResultException.Unexpected("FilterOperand is not supported.");
                }

                operands[ii++] = operand!;
            }

            if (expectedCount > 0 && expectedCount != operands.Length)
            {
                throw ServiceResultException.Unexpected(
                    "ContentFilterElement does not have the correct number of operands.");
            }

            return operands;
        }

        /// <summary>
        /// Returns the value for the element.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant GetValue(FilterOperand operand)
        {
            // return the contained value for literal operands.

            if (operand is LiteralOperand literal)
            {
                return literal.Value;
            }

            // must query the filter target for simple attribute operands.

            if (operand is SimpleAttributeOperand simpleAttribute)
            {
                return m_target.GetAttributeValue(
                    m_context,
                    simpleAttribute.TypeDefinitionId,
                    simpleAttribute.BrowsePath,
                    simpleAttribute.AttributeId,
                    simpleAttribute.ParsedIndexRange);
            }

            // must query the filter target for attribute operands.

            if (operand is AttributeOperand attribute)
            {
                // AttributeOperands only supported in advanced filter targets.

                if (m_target is not IAdvancedFilterTarget advancedTarget)
                {
                    return false;
                }

                return advancedTarget.GetRelatedAttributeValue(
                    m_context,
                    attribute.NodeId,
                    attribute.BrowsePath,
                    attribute.AttributeId,
                    attribute.ParsedIndexRange);
            }

            // recursively evaluate element operands.

            if (operand is ElementOperand element)
            {
                return Evaluate((int)element.Index);
            }

            // oops - Validate() was not called.
            throw ServiceResultException.Unexpected("FilterOperand is not supported.");
        }

        /// <summary>
        /// And FilterOperator
        /// </summary>
        private Variant And(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            // no need for further processing if first operand is false.
            bool lhsNil = !GetValue(operands[0]).TryGet(out bool lhs);
            if (!lhsNil && !lhs)
            {
                return false;
            }

            bool rhsNil = !GetValue(operands[1]).TryGet(out bool rhs);

            if (lhsNil)
            {
                if (rhsNil || rhs)
                {
                    return default;
                }
                return false;
            }

            if (rhsNil)
            {
                if (lhs)
                {
                    return default;
                }

                return false;
            }

            return lhs && rhs;
        }

        /// <summary>
        /// Or FilterOperator
        /// </summary>
        private Variant Or(ContentFilterElement element) // bool?
        {
            FilterOperand[] operands = GetOperands(element, 2);

            bool lhsNil = !GetValue(operands[0]).TryGet(out bool lhs);

            // no need for further processing if first operand is true.
            if (lhs)
            {
                return true;
            }

            bool rhsNil = !GetValue(operands[1]).TryGet(out bool rhs);

            if (lhsNil)
            {
                if (rhsNil || !rhs)
                {
                    return default;
                }

                return true;
            }

            if (rhsNil)
            {
                if (!lhs)
                {
                    return default;
                }

                return true;
            }

            return lhs || rhs;
        }

        /// <summary>
        /// Not FilterOperator
        /// </summary>
        private Variant Not(ContentFilterElement element) // bool?
        {
            FilterOperand[] operands = GetOperands(element, 1);

            bool rhsNil = !GetValue(operands[0]).TryGet(out bool rhs);

            if (rhsNil)
            {
                return default;
            }

            return !rhs;
        }

        /// <summary>
        /// BitwiseAnd FilterOperator
        /// </summary>
        private Variant BitwiseAnd(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant lhs = GetValue(operands[0]);
            Variant rhs = GetValue(operands[1]);

            return lhs & rhs;
        }

        /// <summary>
        /// BitwiseOr FilterOperator
        /// </summary>
        private Variant BitwiseOr(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant lhs = GetValue(operands[0]);
            Variant rhs = GetValue(operands[1]);

            return lhs | rhs;
        }

        /// <summary>
        /// Equals FilterOperator
        /// </summary>
        private Variant Equals(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant lhs = GetValue(operands[0]);
            Variant rhs = GetValue(operands[1]);

            if (lhs.TryGet(out string lhsString) && rhs.TryGet(out string rhsString))
            {
                return lhsString.Equals(rhsString, ContentFilter.EqualsOperatorDefaultStringComparison);
            }

            return lhs.ValueEquals(rhs);
        }

        /// <summary>
        /// GreaterThan FilterOperator
        /// </summary>
        private Variant GreaterThan(ContentFilterElement element) // bool?
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant lhs = GetValue(operands[0]);
            Variant rhs = GetValue(operands[1]);

            // return null if the types are not comparable.
            int compareResult = lhs.CompareTo(rhs);
            return compareResult is not int.MinValue and > 0;
        }

        /// <summary>
        /// GreaterThanOrEqual FilterOperator
        /// </summary>
        private Variant GreaterThanOrEqual(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant lhs = GetValue(operands[0]);
            Variant rhs = GetValue(operands[1]);

            // return null if the types are not comparable.
            int compareResult = lhs.CompareTo(rhs);
            return compareResult is not int.MinValue and >= 0;
        }

        /// <summary>
        /// LessThan FilterOperator
        /// </summary>
        private Variant LessThan(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant lhs = GetValue(operands[0]);
            Variant rhs = GetValue(operands[1]);

            // return null if the types are not comparable.
            int compareResult = lhs.CompareTo(rhs);
            return compareResult is not int.MinValue and < 0;
        }

        /// <summary>
        /// LessThanOrEqual FilterOperator
        /// </summary>
        private Variant LessThanOrEqual(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant lhs = GetValue(operands[0]);
            Variant rhs = GetValue(operands[1]);

            // return null if the types are not comparable.
            int compareResult = lhs.CompareTo(rhs);
            return compareResult is not int.MinValue and <= 0;
        }

        /// <summary>
        /// Between FilterOperator
        /// </summary>
        private Variant Between(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 3);

            Variant value = GetValue(operands[0]);
            Variant min = GetValue(operands[1]);
            Variant max = GetValue(operands[2]);

            // check if never in range no matter what happens with the upper bound.
            int minCompareResult = value.CompareTo(min);
            if (minCompareResult == int.MinValue)
            {
                // return null if the types are not comparable.
                return default;
            }

            if (minCompareResult < 0)
            {
                return false;
            }

            // check if never in range no matter what happens with the lower bound.
            int maxCompareResult = value.CompareTo(max);
            if (maxCompareResult == int.MinValue)
            {
                // return null if the types are not comparable.
                return default;
            }

            return maxCompareResult <= 0;
        }

        /// <summary>
        /// InList FilterOperator
        /// </summary>
        private Variant InList(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 0);

            Variant value = GetValue(operands[0]);

            // check for a match.
            for (int ii = 1; ii < operands.Length; ii++)
            {
                Variant rhs = GetValue(operands[ii]);

                if (value.TryGet(out string lhsString) && rhs.TryGet(out string rhsString))
                {
                    return lhsString.Equals(rhsString, ContentFilter.EqualsOperatorDefaultStringComparison);
                }

                if (value.ValueEquals(rhs))
                {
                    return true;
                }
            }

            // no match.
            return false;
        }

        /// <summary>
        /// Like FilterOperator
        /// </summary>
        private Variant Like(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant firstOperand = GetValue(operands[0]);
            string? lhs;
            if (firstOperand.TryGet(out LocalizedText firstOperandLocalizedText))
            {
                lhs = firstOperandLocalizedText.Text;
            }
            else
            {
                lhs = firstOperand.GetString();
            }

            Variant secondOperand = GetValue(operands[1]);
            string? rhs;
            if (secondOperand.TryGet(out LocalizedText secondOperandLocalizedText))
            {
                rhs = secondOperandLocalizedText.Text;
            }
            else
            {
                rhs = secondOperand.GetString();
            }

            // this operator requires strings.
            if (lhs == null || rhs == null)
            {
                return false;
            }

            return Match(lhs, rhs);
        }

        /// <summary>
        /// IsNull FilterOperator
        /// </summary>
        private Variant IsNull(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            Variant rhs = GetValue(operands[0]);

            return rhs.ValueIsDefaultOrNull;
        }

        /// <summary>
        /// Cast FilterOperator
        /// </summary>
        private Variant Cast(
            ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            // get the value to cast.
            Variant value = GetValue(operands[0]);

            if (value.IsNull)
            {
                return default;
            }

            // get the datatype to cast to.
            if (!GetValue(operands[1]).TryGet(out NodeId datatype))
            {
                return default;
            }

            BuiltInType targetType = TypeInfo.GetBuiltInType(datatype);

            if (targetType == BuiltInType.Null)
            {
                return default; // not supported
            }

            // convert the value.
            return value.ConvertTo(targetType);
        }

        /// <summary>
        /// OfType FilterOperator
        /// </summary>
        private Variant OfType(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            // get the desired type.
            if (!GetValue(operands[0]).TryGet(out NodeId typeDefinitionId) ||
                m_target == null)
            {
                return false;
            }
            // check the type.
            try
            {
                return m_target.IsTypeOf(m_context, typeDefinitionId);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// InView FilterOperator
        /// </summary>
        private Variant InView(ContentFilterElement element)
        {
            // views only supported in advanced filter targets.

            if (m_target is not IAdvancedFilterTarget advancedFilter)
            {
                return false;
            }

            FilterOperand[] operands = GetOperands(element, 1);

            // get the desired type.
            if (!GetValue(operands[0]).TryGet(out NodeId viewId) ||
                m_target == null)
            {
                return false;
            }

            // check the m_target.
            try
            {
                return advancedFilter.IsInView(m_context, viewId);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// RelatedTo FilterOperator
        /// </summary>
        private Variant RelatedTo(ContentFilterElement element)
        {
            return RelatedTo(element, default);
        }

        /// <summary>
        /// RelatedTo FilterOperator
        /// </summary>
        private bool RelatedTo(
            ContentFilterElement element,
            NodeId intermediateNodeId)
        {
            // RelatedTo only supported in advanced filter targets.

            if (m_target is not IAdvancedFilterTarget advancedTarget)
            {
                return false;
            }

            FilterOperand[] operands = GetOperands(element, 6);

            // get the type of the source.
            if (!GetValue(operands[0]).TryGet(out NodeId sourceTypeId))
            {
                return false;
            }

            // get the type of reference to follow.
            if (!GetValue(operands[2]).TryGet(out NodeId referenceTypeId))
            {
                return false;
            }

            // get the number of hops
            int? hops = 1;

            Variant hopsValue = GetValue(operands[3]);
            if (!hopsValue.IsNull)
            {
                hops = hopsValue.ConvertToInt32().GetInt32();
            }

            // get whether to include type definition subtypes.
            bool? includeTypeDefinitionSubtypes = true;

            Variant includeValue = GetValue(operands[4]);

            if (!includeValue.IsNull)
            {
                includeTypeDefinitionSubtypes = includeValue.ConvertToBoolean().GetBoolean(true);
            }

            // get whether to include reference type subtypes.
            bool? includeReferenceTypeSubtypes = true;

            includeValue = GetValue(operands[5]);

            if (!includeValue.IsNull)
            {
                includeReferenceTypeSubtypes = includeValue.ConvertToBoolean().GetBoolean(true);
            }

            NodeId targetTypeId;

            // check if elements are chained.

            if (operands[1] is ElementOperand chainedOperand)
            {
                if ( /*chainedOperand.Index < 0 ||*/
                    chainedOperand.Index >= m_filter.Elements.Count)
                {
                    return false;
                }

                ContentFilterElement chainedElement = m_filter.Elements[(int)chainedOperand.Index];

                // get the m_target type from the first operand of the chained element.
                if (chainedElement.FilterOperator == FilterOperator.RelatedTo)
                {
                    var nestedType = ExtensionObject.ToEncodeable(
                        chainedElement.FilterOperands[0]) as FilterOperand;

                    targetTypeId = GetValue(nestedType!).TryGet(out NodeId n) ? n : default;
                    if (targetTypeId.IsNull)
                    {
                        return false;
                    }

                    // find the nodes that meet the criteria in the first link of the chain.
                    IList<NodeId> nodeIds = advancedTarget.GetRelatedNodes(
                        m_context,
                        intermediateNodeId,
                        sourceTypeId,
                        targetTypeId,
                        referenceTypeId,
                        hops.Value,
                        includeTypeDefinitionSubtypes.Value,
                        includeReferenceTypeSubtypes.Value);

                    if (nodeIds == null || nodeIds.Count == 0)
                    {
                        return false;
                    }

                    // recursively follow the chain.
                    for (int ii = 0; ii < nodeIds.Count; ii++)
                    {
                        // one match is all that is required.
                        if (RelatedTo(chainedElement, nodeIds[ii]))
                        {
                            return true;
                        }
                    }

                    // no matches.
                    return false;
                }
            }

            // get the type of the m_target.
            targetTypeId = GetValue(operands[1]).TryGet(out NodeId n2) ? n2 : default;
            if (targetTypeId.IsNull)
            {
                return false;
            }

            // check the m_target.
            try
            {
                return advancedTarget.IsRelatedTo(
                    m_context,
                    intermediateNodeId,
                    sourceTypeId,
                    targetTypeId,
                    referenceTypeId,
                    hops.Value,
                    includeTypeDefinitionSubtypes.Value,
                    includeReferenceTypeSubtypes.Value);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if the target string matches the UA pattern string.
        /// The pattern string may include UA wildcards %_\[]!
        /// </summary>
        /// <param name="target">String to check for a pattern match.</param>
        /// <param name="pattern">Pattern to match with the target string.</param>
        /// <returns>true if the target string matches the pattern, otherwise false.</returns>
        private static bool Match(string target, string pattern)
        {
            string expression = pattern;

            // 1) Suppress unused regular expression characters with special meaning
            // the following characters have special meaning in a regular expression []\^$.|?*+()
            // the following characters are OPC UA wildcards %_\[]!
            // The specail meaning of the regular expression characters not coincident with the
            // OPC UA wildcards must be suppressed so as not to interfere with matching.
            // preceed all '^', '$', '.', '|', '?', '*', '+', '(', ')' with a '\'
            expression = SuppressUnusedCharacters.Replace(expression, "\\$1");

            // Replace all OPC UA wildcards with their regular expression equivalents
            // replace all '%' with ".+", except "\%"
            expression = ReplaceWildcards.Replace(expression, ".*");

            // replace all '_' with '.', except "\_"
            expression = ReplaceUnderscores.Replace(expression, ".");

            // replace all "[!" with "[^", except "\[!"
            expression = ReplaceBrackets.Replace(expression, "[^");

            return Regex.IsMatch(target, expression);
        }

#if NET8_0_OR_GREATER
        [GeneratedRegex("([\\^\\$\\.\\|\\?\\*\\+\\(\\)])", RegexOptions.Compiled)]
        private static partial Regex _SuppressUnusedCharacters();
        private static Regex SuppressUnusedCharacters => _SuppressUnusedCharacters();

        [GeneratedRegex("(?<!\\\\)%", RegexOptions.Compiled)]
        private static partial Regex _ReplaceWildcards();
        private static Regex ReplaceWildcards => _ReplaceWildcards();

        [GeneratedRegex("(?<!\\\\)_", RegexOptions.Compiled)]
        private static partial Regex _ReplaceUnderscores();
        private static Regex ReplaceUnderscores => _ReplaceUnderscores();

        [GeneratedRegex("(?<!\\\\)(\\[!)", RegexOptions.Compiled)]
        private static partial Regex _ReplaceBrackets();
        private static Regex ReplaceBrackets => _ReplaceBrackets();
#else
        private static Regex SuppressUnusedCharacters { get; }
            = new("([\\^\\$\\.\\|\\?\\*\\+\\(\\)])", RegexOptions.Compiled);
        private static Regex ReplaceWildcards { get; }
            = new("(?<!\\\\)%", RegexOptions.Compiled);
        private static Regex ReplaceUnderscores { get; }
            = new("(?<!\\\\)_", RegexOptions.Compiled);
        private static Regex ReplaceBrackets { get; }
            = new("(?<!\\\\)(\\[!)", RegexOptions.Compiled);
#endif
        private readonly ContentFilter m_filter;
        private readonly IFilterContext m_context;
        private readonly IFilterTarget m_target;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Content filter extensions
    /// </summary>
    public static class ContentFilterExtensions
    {
        /// <summary>
        /// Evaluates the first element in the ContentFilter. If the first or any
        /// subsequent element has dependent elements, the dependent elements are
        /// evaluated before the root element (recursive descent). Elements which
        /// are not linked (directly or indirectly) to the first element will not
        /// be evaluated (they have no influence on the result).
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="context">The context to use when evaluating the filter.
        /// </param>
        /// <param name="target">The target to use when evaluating elements that
        /// reference the type model.</param>
        /// <returns>Returns true, false or null.</returns>
        public static bool Evaluate(
            this ContentFilter filter,
            IFilterContext context,
            IFilterTarget target)
        {
            // check if nothing to do.
            var evaluator = new FilterEvaluator(filter, context, target);
            return evaluator.Result;
        }
    }
}
