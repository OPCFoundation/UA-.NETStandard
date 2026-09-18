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
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// This class contains functions used to evaluate a ContentFilter and report the
    /// results of the evaluation.
    /// </summary>
    public sealed class FilterEvaluator
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
        /// evaluated before the root element without recursive descent. Elements which
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

                if (m_filter.Elements.Count > ContentFilter.MaxElementCount)
                {
                    throw new ServiceResultException(StatusCodes.BadContentFilterInvalid);
                }

                m_results = new Variant[m_filter.Elements.Count];
                m_evaluated = new bool[m_filter.Elements.Count];
                m_leftValues = new Variant[m_filter.Elements.Count];
                m_leftEvaluated = new bool[m_filter.Elements.Count];
                var dependenciesResolved = new bool[m_filter.Elements.Count];
                var operandsByIndex = new FilterOperand[m_filter.Elements.Count][];
                var pending = new Stack<(int Index, int OperandIndex, bool ValueRequired)>();
                pending.Push((0, 0, true));

                // Resume each element after its higher-index dependencies. RelatedTo
                // chains share operands, but their results depend on the intermediate node.
                while (pending.Count != 0)
                {
                    (int index, int operandIndex, bool valueRequired) = pending.Pop();
                    if (m_evaluated[index] || (!valueRequired && dependenciesResolved[index]))
                    {
                        continue;
                    }
                    m_currentIndex = index;
                    ContentFilterElement element = m_filter.Elements[index];
                    if (element == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadContentFilterInvalid);
                    }
                    FilterOperand[] operands = operandsByIndex[index] ??= GetOperands(element, 0);
                    if (operandIndex == 1 &&
                        element.FilterOperator is FilterOperator.And or FilterOperator.Or &&
                        GetLeftValue(operands[0]).TryGetValue(out bool left) &&
                        (element.FilterOperator == FilterOperator.And ? !left : left))
                    {
                        operandIndex = operands.Length;
                    }
                    if (!dependenciesResolved[index] && operandIndex < operands.Length)
                    {
                        pending.Push((index, operandIndex + 1, valueRequired));
                        if (operands[operandIndex] is ElementOperand dependency)
                        {
                            bool relatedChain = element.FilterOperator == FilterOperator.RelatedTo && operandIndex == 1;
                            if (dependency.Index <= index || dependency.Index >= m_filter.Elements.Count)
                            {
                                if (!relatedChain)
                                {
                                    throw new ServiceResultException(StatusCodes.BadContentFilterInvalid);
                                }
                                continue;
                            }
                            int dependencyIndex = (int)dependency.Index;
                            bool dependencyValueRequired = !relatedChain ||
                                m_filter.Elements[dependencyIndex]?.FilterOperator != FilterOperator.RelatedTo;
                            pending.Push((dependencyIndex, 0, dependencyValueRequired));
                        }
                        continue;
                    }
                    dependenciesResolved[index] = true;
                    if (valueRequired)
                    {
                        m_results[index] = Evaluate(index);
                        m_evaluated[index] = true;
                    }
                }
                return m_results[0].TryGetValue(out bool result) && result;
            }
        }

        /// <summary>
        /// Evaluates element at the specified index.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Variant Evaluate(int index)
        {
            if ((uint)index >= (uint)m_filter.Elements.Count)
            {
                throw ServiceResultException.Unexpected(
                    "ElementOperand references an element that does not exist.");
            }

            if (m_evaluated != null && m_evaluated[index])
            {
                return m_results![index];
            }

            // get the element to evaluate.
            ContentFilterElement element = m_filter.Elements[index];

            Variant result = element.FilterOperator switch
            {
                FilterOperator.And => And(element),
                FilterOperator.Or => Or(element),
                FilterOperator.Not => Not(element),
                FilterOperator.Equals => Equals(element),
                FilterOperator.GreaterThan => GreaterThan(element),
                FilterOperator.GreaterThanOrEqual => GreaterThanOrEqual(element),
                FilterOperator.LessThan => LessThan(element),
                FilterOperator.LessThanOrEqual => LessThanOrEqual(element),
                FilterOperator.Between => Between(element),
                FilterOperator.InList => InList(element),
                FilterOperator.Like => Like(element),
                FilterOperator.IsNull => IsNull(element),
                FilterOperator.Cast => Cast(element),
                FilterOperator.OfType => OfType(element),
                FilterOperator.InView => InView(element),
                FilterOperator.RelatedTo => RelatedTo(element),
                FilterOperator.BitwiseAnd => BitwiseAnd(element),
                FilterOperator.BitwiseOr => BitwiseOr(element),
                _ => throw ServiceResultException.Unexpected(
                    $"FilterOperator {element.FilterOperator} is not recognized.")
            };

            if (m_results != null)
            {
                m_results[index] = result;
                m_evaluated![index] = true;
            }

            return result;
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

                if (!extension.TryGetValue(out FilterOperand? operand))
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

            if (operand is ElementOperand element)
            {
                if (element.Index <= m_currentIndex ||
                    element.Index >= m_filter.Elements.Count ||
                    m_evaluated == null ||
                    !m_evaluated[(int)element.Index])
                {
                    throw new ServiceResultException(StatusCodes.BadContentFilterInvalid);
                }
                return m_results![(int)element.Index];
            }

            // oops - Validate() was not called.
            throw ServiceResultException.Unexpected("FilterOperand is not supported.");
        }

        private Variant GetLeftValue(FilterOperand operand)
        {
            if (!m_leftEvaluated![m_currentIndex])
            {
                m_leftValues![m_currentIndex] = GetValue(operand);
                m_leftEvaluated[m_currentIndex] = true;
            }
            return m_leftValues![m_currentIndex];
        }

        /// <summary>
        /// And FilterOperator
        /// </summary>
        private Variant And(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            // no need for further processing if first operand is false.
            bool lhsNil = !GetLeftValue(operands[0]).TryGetValue(out bool lhs);
            if (!lhsNil && !lhs)
            {
                return false;
            }

            bool rhsNil = !GetValue(operands[1]).TryGetValue(out bool rhs);

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

            bool lhsNil = !GetLeftValue(operands[0]).TryGetValue(out bool lhs);

            // no need for further processing if first operand is true.
            if (lhs)
            {
                return true;
            }

            bool rhsNil = !GetValue(operands[1]).TryGetValue(out bool rhs);

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

            bool rhsNil = !GetValue(operands[0]).TryGetValue(out bool rhs);

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

            if (lhs.TryGetValue(out string lhsString) && rhs.TryGetValue(out string rhsString))
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

                if (value.TryGetValue(out string lhsString) && rhs.TryGetValue(out string rhsString))
                {
                    // a non-matching string operand only rules out this operand,
                    // not the rest of the list.
                    if (lhsString.Equals(
                        rhsString,
                        ContentFilter.EqualsOperatorDefaultStringComparison))
                    {
                        return true;
                    }
                    continue;
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
        /// Like FilterOperator (OPC 10000-4 §7.7.3). The pattern syntax and the
        /// whole-string, case-sensitive match are implemented by
        /// <see cref="LikePattern"/>.
        /// </summary>
        /// <remarks>
        /// The operator resolves to FALSE if an operand cannot be resolved to a
        /// string. A pattern that is not a valid search string is treated the
        /// same way: it matches nothing. A literal pattern operand is already
        /// rejected with Bad_FilterOperandInvalid when the filter is validated
        /// (<see cref="ContentFilterElement.Validate"/>); patterns that are only
        /// known at evaluation time (for example from an AttributeOperand)
        /// can only be handled here.
        /// </remarks>
        private Variant Like(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            Variant firstOperand = GetValue(operands[0]);
            string? lhs;
            if (firstOperand.TryGetValue(out LocalizedText firstOperandLocalizedText))
            {
                lhs = firstOperandLocalizedText.Text;
            }
            else
            {
                lhs = firstOperand.GetString();
            }

            Variant secondOperand = GetValue(operands[1]);
            string? rhs;
            if (secondOperand.TryGetValue(out LocalizedText secondOperandLocalizedText))
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

            return LikePattern.IsMatch(lhs, rhs);
        }

        /// <summary>
        /// IsNull FilterOperator
        /// </summary>
        private Variant IsNull(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            Variant rhs = GetValue(operands[0]);

            return rhs.IsNull;
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
            if (!GetValue(operands[1]).TryGetValue(out NodeId datatype))
            {
                return default;
            }

            BuiltInType targetType = TypeInfo.GetBuiltInType(datatype);

            if (targetType == BuiltInType.Null)
            {
                return default; // not supported
            }

            return ConvertValue(value, targetType);
        }

        private Variant ConvertValue(Variant value, BuiltInType targetType)
        {
            try
            {
                return value.ConvertTo(targetType);
            }
            catch (Exception ex) when (
                ex is InvalidCastException or
                FormatException or
                OverflowException or
                ServiceResultException or
                ArgumentException or
                NullReferenceException)
            {
                m_logger.ConversionFailed(ex, targetType);
                return default;
            }
        }

        /// <summary>
        /// OfType FilterOperator
        /// </summary>
        private Variant OfType(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            // get the desired type.
            if (!GetValue(operands[0]).TryGetValue(out NodeId typeDefinitionId) ||
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
            if (!GetValue(operands[0]).TryGetValue(out NodeId viewId) ||
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
            if (m_target is not IAdvancedFilterTarget advancedTarget)
            {
                return false;
            }

            int rootIndex = m_currentIndex;
            var pending = new Stack<(int Index, NodeId IntermediateNodeId)>();
            var visited = new HashSet<(int Index, NodeId IntermediateNodeId)>();
            pending.Push((rootIndex, default));
            try
            {
                while (pending.Count != 0)
                {
                    (int index, NodeId intermediateNodeId) = pending.Pop();
                    if (!visited.Add((index, intermediateNodeId)))
                    {
                        continue;
                    }
                    m_currentIndex = index;
                    FilterOperand[] operands = GetOperands(m_filter.Elements[index], 6);
                    if (!GetValue(operands[0]).TryGetValue(out NodeId sourceTypeId) ||
                        !GetValue(operands[2]).TryGetValue(out NodeId referenceTypeId))
                    {
                        continue;
                    }

                    Variant hopsValue = GetValue(operands[3]);
                    Variant typeSubtypesValue = GetValue(operands[4]);
                    Variant referenceSubtypesValue = GetValue(operands[5]);
                    int hops = 1;
                    bool typeSubtypes = false;
                    bool referenceSubtypes = false;
                    if ((!hopsValue.IsNull &&
                            !ConvertValue(hopsValue, BuiltInType.Int32).TryGetValue(out hops)) ||
                        (!typeSubtypesValue.IsNull &&
                            !ConvertValue(typeSubtypesValue, BuiltInType.Boolean).TryGetValue(out typeSubtypes)) ||
                        (!referenceSubtypesValue.IsNull &&
                            !ConvertValue(referenceSubtypesValue, BuiltInType.Boolean).TryGetValue(out referenceSubtypes)))
                    {
                        continue;
                    }

                    if (operands[1] is ElementOperand chained)
                    {
                        if (chained.Index <= index || chained.Index >= m_filter.Elements.Count)
                        {
                            continue;
                        }
                        int chainedIndex = (int)chained.Index;
                        ContentFilterElement chainedElement = m_filter.Elements[chainedIndex];
                        if (chainedElement.FilterOperator == FilterOperator.RelatedTo)
                        {
                            FilterOperand[] chainedOperands = GetOperands(chainedElement, 6);
                            if (!GetValue(chainedOperands[0]).TryGetValue(out NodeId chainedTypeId) ||
                                chainedTypeId.IsNull)
                            {
                                continue;
                            }
                            IList<NodeId> nodeIds = advancedTarget.GetRelatedNodes(
                                m_context, intermediateNodeId, sourceTypeId, chainedTypeId,
                                referenceTypeId, hops, typeSubtypes, referenceSubtypes);
                            if (nodeIds != null)
                            {
                                for (int ii = nodeIds.Count - 1; ii >= 0; ii--)
                                {
                                    pending.Push((chainedIndex, nodeIds[ii]));
                                }
                            }
                            continue;
                        }
                    }

                    if (GetValue(operands[1]).TryGetValue(out NodeId targetTypeId) &&
                        !targetTypeId.IsNull &&
                        advancedTarget.IsRelatedTo(
                            m_context, intermediateNodeId, sourceTypeId, targetTypeId,
                            referenceTypeId, hops, typeSubtypes, referenceSubtypes))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex) when (
                ex is not OutOfMemoryException and not StackOverflowException and
                    not AccessViolationException and not OperationCanceledException)
            {
                m_logger.TargetEvaluationFailed(ex);
                return false;
            }
            finally
            {
                m_currentIndex = rootIndex;
            }
        }

        private readonly ContentFilter m_filter;
        private readonly IFilterContext m_context;
        private readonly IFilterTarget m_target;
        private readonly ILogger m_logger;
        private Variant[]? m_results;
        private bool[]? m_evaluated;
        private Variant[]? m_leftValues;
        private bool[]? m_leftEvaluated;
        private int m_currentIndex;
    }

    /// <summary>
    /// Content filter extensions
    /// </summary>
    public static class ContentFilterExtensions
    {
        /// <summary>
        /// Evaluates the first element in the ContentFilter. If the first or any
        /// subsequent element has dependent elements, the dependent elements are
        /// evaluated before the root element without recursive descent. Elements which
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

    internal static partial class FilterEvaluatorLog
    {
        [LoggerMessage(EventId = CoreEventIds.FilterEvaluator + 0, Level = LogLevel.Debug,
            Message = "Content filter conversion to {TargetType} failed.")]
        public static partial void ConversionFailed(this ILogger logger, Exception exception, BuiltInType targetType);

        [LoggerMessage(EventId = CoreEventIds.FilterEvaluator + 1, Level = LogLevel.Warning,
            Message = "Content filter target evaluation failed.")]
        public static partial void TargetEvaluationFailed(this ILogger logger, Exception exception);
    }
}
