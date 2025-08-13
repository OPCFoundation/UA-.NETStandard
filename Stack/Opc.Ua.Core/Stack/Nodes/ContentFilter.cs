/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Opc.Ua
{
    public partial class ContentFilter : IFormattable
    {
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="string"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                var buffer = new StringBuilder();

                for (int ii = 0; ii < Elements.Count; ii++)
                {
                    buffer.AppendFormat(formatProvider, "[{0}:{1}]", ii, Elements[ii]);
                }

                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Validates the ContentFilter.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>The result of validation.</returns>
        public Result Validate(FilterContext context)
        {
            var result = new Result(null);

            // check for empty filter.
            if (m_elements == null || m_elements.Count == 0)
            {
                return result;
            }

            bool error = false;

            for (int ii = 0; ii < m_elements.Count; ii++)
            {
                ContentFilterElement element = m_elements[ii];

                // check for null.
                if (element == null)
                {
                    var nullResult = ServiceResult.Create(
                        StatusCodes.BadStructureMissing,
                        "ContentFilterElement is null (Index={0}).",
                        ii);

                    result.ElementResults.Add(new ElementResult(nullResult));
                    error = true;
                    continue;
                }

                element.Parent = this;

                // validate element.
                ElementResult elementResult = element.Validate(context, ii);

                if (ServiceResult.IsBad(elementResult.Status))
                {
                    result.ElementResults.Add(elementResult);
                    error = true;
                    continue;
                }

                result.ElementResults.Add(null);
            }

            // ensure the global error code.
            if (error)
            {
                result.Status = StatusCodes.BadContentFilterInvalid;
            }
            else
            {
                result.ElementResults.Clear();
            }

            return result;
        }

        /// <summary>
        /// Pushes a new element onto the stack.
        /// </summary>
        /// <param name="op">The filter operator.</param>
        /// <param name="operands">The operands.</param>
        /// <exception cref="ServiceResultException"></exception>
        public ContentFilterElement Push(FilterOperator op, params object[] operands)
        {
            // check if nothing more to do.
            if (operands == null || operands.Length == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "ContentFilterElement does not have an operands.");
            }

            // create the element and set the operator.
            var element = new ContentFilterElement { FilterOperator = op };

            for (int ii = 0; ii < operands.Length; ii++)
            {
                // check if a FilterOperand was provided.

                if (operands[ii] is FilterOperand filterOperand)
                {
                    element.FilterOperands.Add(new ExtensionObject(filterOperand));
                    continue;
                }

                // check for reference to another ContentFilterElement.

                if (operands[ii] is ContentFilterElement existingElement)
                {
                    int index = FindElementIndex(existingElement);

                    if (index == -1)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidArgument,
                            "ContentFilterElement is not part of the ContentFilter.");
                    }

                    var operand = new ElementOperand { Index = (uint)index };

                    element.FilterOperands.Add(new ExtensionObject(operand));
                    continue;
                }

                // assume a literal operand.
                var literalOperand = new LiteralOperand { Value = new Variant(operands[ii]) };
                element.FilterOperands.Add(new ExtensionObject(literalOperand));
            }

            // insert the new element at the begining of the list.
            m_elements.Insert(0, element);

            // re-number ElementOperands since all element were shifted up.
            for (int ii = 0; ii < m_elements.Count; ii++)
            {
                foreach (ExtensionObject extension in m_elements[ii].FilterOperands)
                {
                    if (extension != null && extension.Body is ElementOperand operand)
                    {
                        operand.Index++;
                    }
                }
            }

            // return new element.
            return element;
        }

        /// <summary>
        /// Finds the index of the specified element.
        /// </summary>
        /// <param name="target">The target to be found.</param>
        /// <returns>The index of the specified element.</returns>
        private int FindElementIndex(ContentFilterElement target)
        {
            for (int ii = 0; ii < m_elements.Count; ii++)
            {
                if (ReferenceEquals(target, m_elements[ii]))
                {
                    return ii;
                }
            }

            return -1;
        }

        /// <summary>
        /// Stores the validation results for a ContentFilterElement.
        /// </summary>
        public class Result
        {
            /// <summary>
            /// Initializes the object with a result code.
            /// </summary>
            /// <param name="status">The status.</param>
            public Result(ServiceResult status)
            {
                Status = status;
            }

            /// <summary>
            /// Casts ServiceResult to an ElementResult.
            /// </summary>
            /// <param name="status">The status.</param>
            /// <returns>The result of the conversion.</returns>
            public static implicit operator Result(ServiceResult status)
            {
                return new Result(status);
            }

            /// <summary>
            /// The result for the entire filter.
            /// </summary>
            /// <value>The status.</value>
            public ServiceResult Status { get; set; }

            /// <summary>
            /// The result for each element.
            /// </summary>
            /// <value>The element results.</value>
            public List<ElementResult> ElementResults => m_elementResults ??= [];

            /// <summary>
            /// Converts the object to an ContentFilterResult.
            /// </summary>
            /// <param name="diagnosticsMasks">The diagnostics masks.</param>
            /// <param name="stringTable">The string table.</param>
            public ContentFilterResult ToContextFilterResult(
                DiagnosticsMasks diagnosticsMasks,
                StringTable stringTable)
            {
                var result = new ContentFilterResult();

                if (m_elementResults == null || m_elementResults.Count == 0)
                {
                    return result;
                }

                bool error = false;

                foreach (ElementResult elementResult in m_elementResults)
                {
                    ContentFilterElementResult elementResult2 = null;

                    if (elementResult == null || ServiceResult.IsGood(elementResult.Status))
                    {
                        elementResult2 = new ContentFilterElementResult
                        {
                            StatusCode = StatusCodes.Good
                        };

                        result.ElementResults.Add(elementResult2);
                        result.ElementDiagnosticInfos.Add(null);
                        continue;
                    }

                    error = true;

                    elementResult2 = elementResult.ToContentFilterElementResult(
                        diagnosticsMasks,
                        stringTable);
                    result.ElementResults.Add(elementResult2);
                    result.ElementDiagnosticInfos.Add(
                        new DiagnosticInfo(
                            elementResult.Status,
                            diagnosticsMasks,
                            false,
                            stringTable));
                }

                if (!error)
                {
                    result.ElementResults.Clear();
                    result.ElementDiagnosticInfos.Clear();
                }

                return result;
            }

            private List<ElementResult> m_elementResults;
        }

        /// <summary>
        /// Stores the validation results for a ContentFilterElement.
        /// </summary>
        public class ElementResult
        {
            /// <summary>
            /// Initializes the object with a result code.
            /// </summary>
            /// <param name="status">The status.</param>
            public ElementResult(ServiceResult status)
            {
                Status = status;
            }

            /// <summary>
            /// Casts ServiceResult to an ElementResult.
            /// </summary>
            /// <param name="status">The status.</param>
            /// <returns>The result of the conversion.</returns>
            public static implicit operator ElementResult(ServiceResult status)
            {
                return new ElementResult(status);
            }

            /// <summary>
            /// The result for the entire element.
            /// </summary>
            /// <value>The status.</value>
            public ServiceResult Status { get; set; }

            /// <summary>
            /// The result for each operand.
            /// </summary>
            /// <value>The operand results.</value>
            public List<ServiceResult> OperandResults => m_operandResults ??= [];

            /// <summary>
            /// Converts the object to an ContentFilterElementResult.
            /// </summary>
            /// <param name="diagnosticsMasks">The diagnostics masks.</param>
            /// <param name="stringTable">The string table.</param>
            public ContentFilterElementResult ToContentFilterElementResult(
                DiagnosticsMasks diagnosticsMasks,
                StringTable stringTable)
            {
                var result = new ContentFilterElementResult();

                if (ServiceResult.IsGood(Status))
                {
                    result.StatusCode = StatusCodes.Good;
                    return result;
                }

                result.StatusCode = Status.StatusCode;

                if (m_operandResults.Count == 0)
                {
                    return result;
                }

                foreach (ServiceResult operandResult in m_operandResults)
                {
                    if (ServiceResult.IsGood(operandResult))
                    {
                        result.OperandStatusCodes.Add(StatusCodes.Good);
                        result.OperandDiagnosticInfos.Add(null);
                    }
                    else
                    {
                        result.OperandStatusCodes.Add(operandResult.StatusCode);
                        result.OperandDiagnosticInfos.Add(
                            new DiagnosticInfo(
                                operandResult,
                                diagnosticsMasks,
                                false,
                                stringTable));
                    }
                }

                return result;
            }

            private List<ServiceResult> m_operandResults;
        }
    }

    public partial class ContentFilterElement : IFormattable
    {
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="string"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                var buffer = new StringBuilder();

                buffer.AppendFormat(formatProvider, "<{0}", FilterOperator);

                for (int ii = 0; ii < FilterOperands.Count; ii++)
                {
                    if (FilterOperands[ii] != null)
                    {
                        buffer.AppendFormat(formatProvider, ", {0}", FilterOperands[ii].Body);
                    }
                    else
                    {
                        buffer.AppendFormat(formatProvider, ", (null)");
                    }
                }

                buffer.AppendFormat(formatProvider, ">");

                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// The ContentFilter that this Element is part of.
        /// </summary>
        /// <value>The parent.</value>
        public ContentFilter Parent { get; internal set; }

        /// <summary>
        /// Validates the content filter element.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="index">The index.</param>
        /// <returns>The results of the validation.</returns>
        public virtual ContentFilter.ElementResult Validate(FilterContext context, int index)
        {
            var result = new ContentFilter.ElementResult(null);

            // check the number of operands.
            int operandCount = -1;

            switch (m_filterOperator)
            {
                case FilterOperator.Not:
                case FilterOperator.IsNull:
                case FilterOperator.InView:
                case FilterOperator.OfType:
                    operandCount = 1;
                    break;
                case FilterOperator.And:
                case FilterOperator.Or:
                case FilterOperator.Equals:
                case FilterOperator.GreaterThan:
                case FilterOperator.GreaterThanOrEqual:
                case FilterOperator.LessThan:
                case FilterOperator.LessThanOrEqual:
                case FilterOperator.Like:
                case FilterOperator.Cast:
                case FilterOperator.BitwiseAnd:
                case FilterOperator.BitwiseOr:
                    operandCount = 2;
                    break;
                case FilterOperator.Between:
                    operandCount = 3;
                    break;
                case FilterOperator.RelatedTo:
                    operandCount = 6;
                    break;
                case FilterOperator.InList:
                    operandCount = -1;
                    break;
            }

            if (operandCount != -1)
            {
                if (operandCount != m_filterOperands.Count)
                {
                    result.Status = ServiceResult.Create(
                        StatusCodes.BadEventFilterInvalid,
                        "ContentFilterElement does not have the correct number of operands (Operator={0} OperandCount={1}).",
                        m_filterOperator,
                        operandCount);

                    return result;
                }
            }
            else if (m_filterOperands.Count < 2)
            {
                result.Status = ServiceResult.Create(
                    StatusCodes.BadEventFilterInvalid,
                    "ContentFilterElement does not have the correct number of operands (Operator={0} OperandCount={1}).",
                    m_filterOperator,
                    m_filterOperands.Count);

                return result;
            }

            // validate the operands.
            bool error = false;

            for (int ii = 0; ii < m_filterOperands.Count; ii++)
            {
                ExtensionObject operand = m_filterOperands[ii];

                ServiceResult operandResult;
                // check for null.
                if (ExtensionObject.IsNull(operand))
                {
                    operandResult = ServiceResult.Create(
                        StatusCodes.BadEventFilterInvalid,
                        "The FilterOperand cannot be Null.");

                    result.OperandResults.Add(operandResult);
                    error = true;
                    continue;
                }

                // check that the extension object contains a filter operand.

                if (operand.Body is not FilterOperand filterOperand)
                {
                    operandResult = ServiceResult.Create(
                        StatusCodes.BadEventFilterInvalid,
                        "The FilterOperand is not a supported type ({0}).",
                        operand.Body.GetType());

                    result.OperandResults.Add(operandResult);
                    error = true;
                    continue;
                }

                // validate the operand.
                filterOperand.Parent = this;
                operandResult = filterOperand.Validate(context, index);

                if (ServiceResult.IsBad(operandResult))
                {
                    result.OperandResults.Add(operandResult);
                    error = true;
                    continue;
                }

                result.OperandResults.Add(null);
            }

            // ensure the global error code.
            if (error)
            {
                result.Status = StatusCodes.BadContentFilterInvalid;
            }
            else
            {
                result.OperandResults.Clear();
            }

            return result;
        }

        /// <summary>
        /// Returns the operands for the element.
        /// </summary>
        /// <returns>The list of operands for the element.</returns>
        public List<FilterOperand> GetOperands()
        {
            var operands = new List<FilterOperand>(FilterOperands.Count);

            foreach (ExtensionObject extension in FilterOperands)
            {
                if (ExtensionObject.IsNull(extension))
                {
                    continue;
                }

                if (extension.Body is not FilterOperand operand)
                {
                    continue;
                }

                operands.Add(operand);
            }

            return operands;
        }

        /// <summary>
        /// Sets the operands for the element.
        /// </summary>
        /// <param name="operands">The list of the operands.</param>
        public void SetOperands(IEnumerable<FilterOperand> operands)
        {
            FilterOperands.Clear();

            if (operands == null)
            {
                return;
            }

            foreach (FilterOperand operand in operands)
            {
                if (operand == null)
                {
                    continue;
                }

                FilterOperands.Add(new ExtensionObject(operand));
            }
        }

        /// <summary>
        /// Converts an ContentFilterElement to a displayable string.
        /// </summary>
        /// <param name="nodeTable">The node table.</param>
        /// <returns>ContentFilterElement as a displayable string.</returns>
        public virtual string ToString(INodeTable nodeTable)
        {
            List<FilterOperand> operands = GetOperands();

            string operand1 = operands.Count > 0 ? operands[0].ToString(nodeTable) : null;
            string operand2 = operands.Count > 1 ? operands[1].ToString(nodeTable) : null;
            string operand3 = operands.Count > 2 ? operands[2].ToString(nodeTable) : null;

            var buffer = new StringBuilder();

            switch (FilterOperator)
            {
                case FilterOperator.OfType:
                case FilterOperator.InView:
                case FilterOperator.IsNull:
                case FilterOperator.Not:
                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0} '{1}'",
                        FilterOperator,
                        operand1);
                    break;
                case FilterOperator.And:
                case FilterOperator.Equals:
                case FilterOperator.GreaterThan:
                case FilterOperator.GreaterThanOrEqual:
                case FilterOperator.LessThan:
                case FilterOperator.LessThanOrEqual:
                case FilterOperator.Like:
                case FilterOperator.Or:
                case FilterOperator.BitwiseAnd:
                case FilterOperator.BitwiseOr:
                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "'{1}' {0} '{2}'",
                        FilterOperator,
                        operand1,
                        operand2);
                    break;
                case FilterOperator.Between:
                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "'{1}' <= '{0}' <= '{2}'",
                        operand1,
                        operand2,
                        operand3);
                    break;
                case FilterOperator.Cast:
                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "({1}){0}",
                        operand1,
                        operand2);
                    break;
                case FilterOperator.InList:
                    buffer.AppendFormat(CultureInfo.InvariantCulture, "'{0}' in ", operand1)
                        .Append('{');

                    for (int ii = 1; ii < operands.Count; ii++)
                    {
                        buffer.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "'{0}'",
                            operands[ii].ToString());
                        if (ii < operands.Count - 1)
                        {
                            buffer.Append(", ");
                        }
                    }

                    buffer.Append('}');
                    break;
                case FilterOperator.RelatedTo:
                    buffer.AppendFormat(CultureInfo.InvariantCulture, "'{0}' ", operand1);

                    string referenceType = operand2;

                    if (operands.Count > 1 && operands[1] is LiteralOperand literalOperand)
                    {
                        INode node = nodeTable.Find(literalOperand.Value.Value as NodeId);

                        if (node != null)
                        {
                            referenceType = Utils.Format("{0}", node);
                        }
                    }

                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0} '{1}'",
                        referenceType,
                        operand2);

                    if (operand3 != null)
                    {
                        buffer.AppendFormat(CultureInfo.InvariantCulture, "Hops='{0}'", operand3);
                    }

                    break;
            }

            return buffer.ToString();
        }
    }

    public partial class FilterOperand
    {
        /// <summary>
        /// The ContentFilterElement this FilterOperand is contained in.
        /// The ContentFilterElement contains the operator and the operands
        /// so it defines the expression to be evaluated.
        /// </summary>
        /// <value>The parent element.</value>
        public ContentFilterElement Parent { get; internal set; }

        /// <summary>
        /// Validates the operand.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="index">The index.</param>
        /// <returns>the result of the validation</returns>
        public virtual ServiceResult Validate(FilterContext context, int index)
        {
            return ServiceResult.Create(
                StatusCodes.BadEventFilterInvalid,
                "A sub-class of FilterOperand must be specified.");
        }

        /// <summary>
        /// Converts an FilterOperand to a displayable string.
        /// </summary>
        /// <param name="nodeTable">The node table.</param>
        /// <returns>ContentFilterElement as a displayable string.</returns>
        public virtual string ToString(INodeTable nodeTable)
        {
            return Utils.Format("{0}", this);
        }
    }

    public partial class AttributeOperand : IFormattable
    {
        /// <summary>
        /// Constructs an operand from a value.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="browsePath">The browse path.</param>
        public AttributeOperand(NodeId nodeId, QualifiedName browsePath)
        {
            m_nodeId = nodeId;
            m_attributeId = Attributes.Value;

            m_browsePath = new RelativePath();

            var element = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.Aggregates,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = browsePath
            };

            m_browsePath.Elements.Add(element);
        }

        /// <summary>
        /// Constructs an operand from a value.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="browsePaths">The browse paths.</param>
        public AttributeOperand(NodeId nodeId, IList<QualifiedName> browsePaths)
        {
            m_nodeId = nodeId;
            m_attributeId = Attributes.Value;
            m_browsePath = new RelativePath();

            for (int ii = 0; ii < browsePaths.Count; ii++)
            {
                var element = new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.Aggregates,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = browsePaths[ii]
                };

                m_browsePath.Elements.Add(element);
            }
        }

        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="relativePath">The relative path.</param>
        public AttributeOperand(
            FilterContext context,
            ExpandedNodeId nodeId,
            RelativePath relativePath)
        {
            m_nodeId = ExpandedNodeId.ToNodeId(nodeId, context.NamespaceUris);
            m_browsePath = relativePath;
            m_attributeId = Attributes.Value;
            m_indexRange = null;
            m_alias = null;
        }

        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="typeDefinitionId">The type definition identifier.</param>
        /// <param name="browsePath">The browse path.</param>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <param name="indexRange">The index range.</param>
        public AttributeOperand(
            FilterContext context,
            ExpandedNodeId typeDefinitionId,
            string browsePath,
            uint attributeId,
            string indexRange)
        {
            m_nodeId = ExpandedNodeId.ToNodeId(typeDefinitionId, context.NamespaceUris);
            m_browsePath = RelativePath.Parse(browsePath, context.TypeTree);
            m_attributeId = attributeId;
            m_indexRange = indexRange;
            m_alias = null;
        }

        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="string"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                var buffer = new StringBuilder();

                for (int ii = 0; ii < m_browsePath.Elements.Count; ii++)
                {
                    buffer.AppendFormat(
                        formatProvider,
                        "/{0}",
                        m_browsePath.Elements[ii].TargetName);
                }

                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Whether the operand has been validated.
        /// </summary>
        /// <value><c>true</c> if validated; otherwise, <c>false</c>.</value>
        /// <remarks>
        /// Set when Validate() is called.
        /// </remarks>
        public bool Validated { get; private set; }

        /// <summary>
        /// Stores the parsed form of the IndexRange parameter.
        /// </summary>
        /// <value>The parsed index range.</value>
        /// <remarks>
        /// Set when Validate() is called.
        /// </remarks>
        public NumericRange ParsedIndexRange => m_parsedIndexRange;

        /// <summary>
        /// Validates the operand (sets the ParsedBrowsePath and ParsedIndexRange properties).
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="index">The index.</param>
        /// <returns>The result of the validation.</returns>
        public override ServiceResult Validate(FilterContext context, int index)
        {
            Validated = false;

            // verify that the operand refers to a node in the type model.
            if (!context.TypeTree.IsKnown(m_nodeId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadTypeDefinitionInvalid,
                    "AttributeOperand does not have a known TypeDefinitionId ({0}).",
                    m_nodeId);
            }

            // verify attribute id.
            if (!Attributes.IsValid(m_attributeId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadAttributeIdInvalid,
                    "AttributeOperand does not specify a valid AttributeId ({0}).",
                    m_attributeId);
            }

            // initialize as empty.
            m_parsedIndexRange = NumericRange.Empty;

            // parse the index range.
            if (!string.IsNullOrEmpty(m_indexRange))
            {
                try
                {
                    m_parsedIndexRange = NumericRange.Parse(m_indexRange);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(
                        e,
                        StatusCodes.BadIndexRangeInvalid,
                        "AttributeOperand does not specify a valid BrowsePath ({0}).",
                        m_indexRange);
                }

                if (m_attributeId != Attributes.Value)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadIndexRangeInvalid,
                        "AttributeOperand specifies an IndexRange for an Attribute other than Value ({0}).",
                        m_attributeId);
                }
            }

            Validated = true;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Converts an AttributeOperand to a displayable string.
        /// </summary>
        /// <param name="nodeTable">The node table.</param>
        /// <returns>AttributeOperand as a displayable string.</returns>
        public override string ToString(INodeTable nodeTable)
        {
            var buffer = new StringBuilder();

            INode node = nodeTable.Find(m_nodeId);

            if (node != null)
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", NodeId);
            }
            else
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", NodeId);
            }

            if (!RelativePath.IsEmpty(BrowsePath))
            {
                buffer.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "/{0}",
                    BrowsePath.Format(nodeTable.TypeTree));
            }

            if (!string.IsNullOrEmpty(IndexRange))
            {
                buffer.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "[{0}]",
                    NumericRange.Parse(IndexRange));
            }

            if (!string.IsNullOrEmpty(Alias))
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, "- '{0}'", Alias);
            }

            return buffer.ToString();
        }

        private NumericRange m_parsedIndexRange;
    }

    public partial class ElementOperand : IFormattable
    {
        /// <summary>
        /// Constructs an operand from a value.
        /// </summary>
        /// <param name="index">The index.</param>
        public ElementOperand(uint index)
        {
            m_index = index;
        }

        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="string"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "[{0}]", m_index);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Validates the operand.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="index">The index.</param>
        /// <returns>The result of the validation</returns>
        public override ServiceResult Validate(FilterContext context, int index)
        {
            if (index < 0)
            {
                return ServiceResult.Create(
                    StatusCodes.BadFilterOperandInvalid,
                    "ElementOperand specifies an Index that is less than zero ({0}).",
                    index);
            }

            if (m_index <= index)
            {
                return ServiceResult.Create(
                    StatusCodes.BadFilterOperandInvalid,
                    "ElementOperand references an element that precedes it in the ContentFilter.",
                    m_index);
            }

            if (m_index >= Parent.Parent.Elements.Count)
            {
                return ServiceResult.Create(
                    StatusCodes.BadFilterOperandInvalid,
                    "ElementOperand references an element that does not exist.",
                    m_index);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Converts an ElementOperand to a displayable string.
        /// </summary>
        /// <param name="nodeTable">The table.</param>
        /// <returns>ElementOperand as a displayable string.</returns>
        public override string ToString(INodeTable nodeTable)
        {
            return Utils.Format("Element[{0}]", Index);
        }
    }

    public partial class LiteralOperand : IFormattable
    {
        /// <summary>
        /// Constructs an operand from a value.
        /// </summary>
        /// <param name="value">The value.</param>
        public LiteralOperand(object value)
        {
            m_value = new Variant(value);
        }

        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="string"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return string.Format(formatProvider, "{0}", m_value);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Validates the operand.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="index">The index.</param>
        /// <returns>The result of the validation</returns>
        public override ServiceResult Validate(FilterContext context, int index)
        {
            if (m_value.Value == null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadEventFilterInvalid,
                    "LiteralOperand specifies a null Value.");
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Converts an LiteralOperand to a displayable string.
        /// </summary>
        /// <param name="nodeTable">The table.</param>
        /// <returns>LiteralOperand as a displayable string.</returns>
        public override string ToString(INodeTable nodeTable)
        {
            ExpandedNodeId nodeId = Value.Value as ExpandedNodeId ??
                (ExpandedNodeId)(Value.Value as NodeId);

            if (nodeId != null)
            {
                INode node = nodeTable.Find(nodeId);

                if (node != null)
                {
                    return Utils.Format("{0} ({1})", node, nodeId);
                }
            }

            return Utils.Format("{0}", Value);
        }
    }
}
