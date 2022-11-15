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
using System.Text;

namespace Opc.Ua
{
    #region ContentFilter Class
    public partial class ContentFilter: IFormattable
    {
        #region IFormattable Members
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder buffer = new StringBuilder();

                for (int ii = 0; ii < this.Elements.Count; ii++)
                {
                    buffer.AppendFormat(formatProvider, "[{0}:{1}]", ii, this.Elements[ii]);
                }

                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region Overridden Methods
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
        #endregion
    
        /// <summary>
        /// Validates the ContentFilter.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>The result of validation.</returns>
        public Result Validate(FilterContext context)
        {
            Result result = new Result(null);
            
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
                    ServiceResult nullResult = ServiceResult.Create(
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
        /// <returns></returns>
        public ContentFilterElement Push(FilterOperator op, params object[] operands)
        { 
            // check if nothing more to do.
            if (operands == null || operands.Length == 0)
            {                        
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "ContentFilterElement does not have an operands.");
            }
            
            // create the element and set the operator.
            ContentFilterElement element = new ContentFilterElement();            
            element.FilterOperator = op;
            
            for (int ii = 0; ii < operands.Length; ii++)
            {
                // check if a FilterOperand was provided.
                FilterOperand filterOperand = operands[ii] as FilterOperand;
                
                if (filterOperand != null)
                {
                    element.FilterOperands.Add(new ExtensionObject(filterOperand));
                    continue;
                }
                
                // check for reference to another ContentFilterElement.
                ContentFilterElement existingElement = operands[ii] as ContentFilterElement;

                if (existingElement != null)
                {
                    int index = FindElementIndex(existingElement);

                    if (index == -1)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "ContentFilterElement is not part of the ContentFilter.");
                    }

                    ElementOperand operand = new ElementOperand();
                    operand.Index = (uint)index;

                    element.FilterOperands.Add(new ExtensionObject(operand));
                    continue;
                }

                // assume a literal operand.
                LiteralOperand literalOperand = new LiteralOperand();
                literalOperand.Value = new Variant(operands[ii]);
                element.FilterOperands.Add(new ExtensionObject(literalOperand));
            }

            // insert the new element at the begining of the list.
            m_elements.Insert(0, element);

            // re-number ElementOperands since all element were shifted up.
            for (int ii = 0; ii < m_elements.Count; ii++)
            {
                foreach (ExtensionObject extension in m_elements[ii].FilterOperands)
                {
                    if (extension != null)
                    {
                        ElementOperand operand = extension.Body as ElementOperand;

                        if (operand != null)
                        {
                            operand.Index++;
                        }
                    }                
                }
            }

            // return new element.
            return element;
        }

        /// <summary>
        /// Finds the index of the specified element.
        /// </summary>
        /// <param name="target">The targetto be found.</param>
        /// <returns>The index of the specified element.</returns>
        private int FindElementIndex(ContentFilterElement target)
        {
            for (int ii = 0; ii < m_elements.Count; ii++)
            {
                if (Object.ReferenceEquals(target, m_elements[ii]))
                {
                    return ii;
                }
            }

            return -1;
        }
        
        #region Result Class
        /// <summary>
        /// Stores the validation results for a ContentFilterElement.
        /// </summary>
        public class Result
        {
            #region Public Interface
            /// <summary>
            /// Initializes the object with a result code.
            /// </summary>
            /// <param name="status">The status.</param>
            public Result(ServiceResult status)
            {
                m_status = status;
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
            public ServiceResult Status
            {
                get { return m_status;  }
                set { m_status = value; }
            }
                     
            /// <summary>
            /// The result for each element.
            /// </summary>
            /// <value>The element results.</value>
            public List<ElementResult> ElementResults
            {
                get
                {
                    if (m_elementResults == null)
                    {
                        m_elementResults = new List<ElementResult>();
                    }

                    return m_elementResults;
                }
            }

            /// <summary>
            /// Converts the object to an ContentFilterResult.
            /// </summary>
            /// <param name="diagnosticsMasks">The diagnostics masks.</param>
            /// <param name="stringTable">The string table.</param>
            /// <returns></returns>
            public ContentFilterResult ToContextFilterResult(DiagnosticsMasks diagnosticsMasks, StringTable stringTable)
            {
                ContentFilterResult result = new ContentFilterResult();

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
                        elementResult2 = new ContentFilterElementResult();
                        elementResult2.StatusCode = StatusCodes.Good;

                        result.ElementResults.Add(elementResult2);
                        result.ElementDiagnosticInfos.Add(null);
                        continue;
                    }

                    error = true;
                                        
                    elementResult2 = elementResult.ToContentFilterElementResult(diagnosticsMasks, stringTable);
                    result.ElementResults.Add(elementResult2);
                    result.ElementDiagnosticInfos.Add(new DiagnosticInfo(elementResult.Status, diagnosticsMasks, false, stringTable));
                }

                if (!error)
                {
                    result.ElementResults.Clear();
                    result.ElementDiagnosticInfos.Clear();
                }
                    
                return result;
            }
            #endregion
            
            #region Private Fields
            private ServiceResult m_status;
            private List<ElementResult> m_elementResults;
            #endregion
        }
        #endregion
        
        #region ElementResult Class
        /// <summary>
        /// Stores the validation results for a ContentFilterElement.
        /// </summary>
        public class ElementResult
        {
            #region Public Interface
            /// <summary>
            /// Initializes the object with a result code.
            /// </summary>
            /// <param name="status">The status.</param>
            public ElementResult(ServiceResult status)
            {
                m_status = status;
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
            public ServiceResult Status
            {
                get { return m_status;  }
                set { m_status = value; }
            }
                     
            /// <summary>
            /// The result for each operand.
            /// </summary>
            /// <value>The operand results.</value>
            public List<ServiceResult> OperandResults
            {
                get
                {
                    if (m_operandResults == null)
                    {
                        m_operandResults = new List<ServiceResult>();
                    }

                    return m_operandResults;
                }
            }
            
            /// <summary>
            /// Converts the object to an ContentFilterElementResult.
            /// </summary>
            /// <param name="diagnosticsMasks">The diagnostics masks.</param>
            /// <param name="stringTable">The string table.</param>
            /// <returns></returns>
            public ContentFilterElementResult ToContentFilterElementResult(DiagnosticsMasks diagnosticsMasks, StringTable stringTable)
            {
                ContentFilterElementResult result = new ContentFilterElementResult();

                if (ServiceResult.IsGood(m_status))
                {
                    result.StatusCode = StatusCodes.Good;
                    return result;
                }
                
                result.StatusCode = m_status.StatusCode;

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
                        result.OperandDiagnosticInfos.Add(new DiagnosticInfo(operandResult, diagnosticsMasks, false, stringTable));

                    }                                        
                }

                return result;
            }
            #endregion
            
            #region Private Fields
            private ServiceResult m_status;
            private List<ServiceResult> m_operandResults;
            #endregion
        }
        #endregion
    }
    #endregion

    #region ContentFilterElement Class
    public partial class ContentFilterElement : IFormattable
    {
        #region IFormattable Members
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder buffer = new StringBuilder();

                buffer.AppendFormat(formatProvider, "<{0}", this.FilterOperator);

                for (int ii = 0; ii < this.FilterOperands.Count; ii++)
                {
                    if (this.FilterOperands[ii] != null)
                    {
                        buffer.AppendFormat(formatProvider, ", {0}", this.FilterOperands[ii].Body);
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
        #endregion

        #region Overridden Methods
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
        #endregion

        #region Public Members
        /// <summary>
        /// The ContentFilter that this Element is part of.
        /// </summary>
        /// <value>The parent.</value>
        public ContentFilter Parent
        {
            get { return m_parent; }
            internal set { this.m_parent = value; }
        } 

        /// <summary>
        /// Validates the content filter element.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="index">The index.</param>
        /// <returns>The results of the validation.</returns>
        public virtual ContentFilter.ElementResult Validate(FilterContext context, int index)
        {
            ContentFilter.ElementResult result = new ContentFilter.ElementResult(null);

            // check the number of operands.
            int operandCount = -1;

            switch (m_filterOperator)
            {
                case FilterOperator.Not:
                case FilterOperator.IsNull:
                case FilterOperator.InView:
                case FilterOperator.OfType:
                {
                    operandCount = 1;
                    break;
                }

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
                {
                    operandCount = 2;
                    break;
                }

                case FilterOperator.Between:
                {
                    operandCount = 3;
                    break;
                }

                case FilterOperator.RelatedTo:
                {
                    operandCount = 6;
                    break;
                }

                case FilterOperator.InList:
                {
                    operandCount = -1;
                    break;
                }

                default:
                {
                    break;
                }
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
            else
            {
                if (m_filterOperands.Count < 2)
                {                    
                    result.Status = ServiceResult.Create(
                        StatusCodes.BadEventFilterInvalid, 
                        "ContentFilterElement does not have the correct number of operands (Operator={0} OperandCount={1}).", 
                        m_filterOperator,
                        m_filterOperands.Count);

                    return result;
                }
            }

            // validate the operands.
            bool error = false;

            for (int ii = 0; ii < m_filterOperands.Count; ii++)
            {
                ServiceResult operandResult = null;

                ExtensionObject operand = m_filterOperands[ii];
                
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
                FilterOperand filterOperand = operand.Body  as FilterOperand;

                if (filterOperand == null)
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
            List<FilterOperand> operands = new List<FilterOperand>(FilterOperands.Count); 

            foreach (ExtensionObject extension in FilterOperands)
            {
                if (ExtensionObject.IsNull(extension))
                {
                    continue;
                }

                FilterOperand operand = extension.Body as FilterOperand;

                if (operand == null)
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

            string operand1 = (operands.Count > 0)?operands[0].ToString(nodeTable):null;
            string operand2 = (operands.Count > 1)?operands[1].ToString(nodeTable):null;
            string operand3 = (operands.Count > 2)?operands[2].ToString(nodeTable):null;

            StringBuilder buffer = new StringBuilder();

            switch (FilterOperator)
            {
                case FilterOperator.OfType:
                case FilterOperator.InView:
                case FilterOperator.IsNull:
                case FilterOperator.Not:
                {
                    buffer.AppendFormat("{0} '{1}'", FilterOperator, operand1);
                    break;
                }
                    
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
                {
                    buffer.AppendFormat("'{1}' {0} '{2}'", FilterOperator, operand1, operand2);
                    break;
                }
                    
                case FilterOperator.Between:
                {
                    buffer.AppendFormat("'{1}' <= '{0}' <= '{2}'", operand1, operand2, operand3);
                    break;
                }
                    
                case FilterOperator.Cast:
                {
                    buffer.AppendFormat("({1}){0}", operand1, operand2);
                    break;
                }
                    
                case FilterOperator.InList:
                {
                    buffer.AppendFormat("'{0}' in ", operand1);
                    buffer.Append('{');

                    for (int ii = 1; ii < operands.Count; ii++)
                    {
                        buffer.AppendFormat("'{0}'", operands[ii].ToString());
                        if (ii < operands.Count-1)
                        {
                            buffer.Append(", ");
                        }
                    }
                            
                    buffer.Append('}');
                    break;
                }
                    
                case FilterOperator.RelatedTo:
                {
                    buffer.AppendFormat("'{0}' ", operand1);
                    
                    string referenceType = operand2;

                    if (operands.Count > 1)
                    {
                        LiteralOperand literalOperand = operands[1] as LiteralOperand;

                        if (literalOperand != null)
                        {
                            INode node = nodeTable.Find(literalOperand.Value.Value as NodeId);

                            if (node != null)
                            {
                                referenceType = Utils.Format("{0}", node);
                            }
                        }
                    }
                    
                    buffer.AppendFormat("{0} '{1}'", referenceType, operand2);

                    if (operand3 != null)
                    {
                        buffer.AppendFormat("Hops='{0}'", operand3);
                    }

                    break;
                }
            }

            return buffer.ToString();
        }
        #endregion

        #region Private Fields
        private ContentFilter m_parent;
        #endregion
    }
    #endregion

    #region FilterOperand Class
    public partial class FilterOperand
    {
        #region Public Interface
        /// <summary>
        /// The ContentFilterElement this FilterOperand is contained in.
        /// The ContentFilterElement contains the operator and the operands
        /// so it defines the expression to be evaluated.
        /// </summary>
        /// <value>The parent element.</value>
        public ContentFilterElement Parent
        {
            get { return this.m_parent; }
            internal set { this.m_parent = value; }
        }

        /// <summary>
        /// Validates the operand.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="index">The index.</param>
        /// <returns>the result of the validation</returns>
        public virtual ServiceResult Validate(FilterContext context, int index)
        {
            return ServiceResult.Create(StatusCodes.BadEventFilterInvalid, "A sub-class of FilterOperand must be specified.");
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
        #endregion
        
        #region Private Fields
        private ContentFilterElement m_parent;
        #endregion
    }
    #endregion

    #region AttributeOperand Class
    public partial class AttributeOperand : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Constructs an operand from a value.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="browsePath">The browse path.</param>
        public AttributeOperand(
            NodeId nodeId,
            QualifiedName browsePath)
        {
            m_nodeId = nodeId;
            m_attributeId = Attributes.Value;

            m_browsePath = new RelativePath();

            RelativePathElement element = new RelativePathElement();

            element.ReferenceTypeId = ReferenceTypeIds.Aggregates;
            element.IsInverse = false;
            element.IncludeSubtypes = true;
            element.TargetName = browsePath;

            m_browsePath.Elements.Add(element);
        }

        /// <summary>
        /// Constructs an operand from a value.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="browsePaths">The browse paths.</param>
        public AttributeOperand(
            NodeId nodeId,
            IList<QualifiedName> browsePaths)
        {
            m_nodeId = nodeId;
            m_attributeId = Attributes.Value;
            m_browsePath = new RelativePath();

            for (int ii = 0; ii < browsePaths.Count; ii++)
            {
                RelativePathElement element = new RelativePathElement();

                element.ReferenceTypeId = ReferenceTypeIds.Aggregates;
                element.IsInverse = false;
                element.IncludeSubtypes = true;
                element.TargetName = browsePaths[ii];

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
            FilterContext  context, 
            ExpandedNodeId nodeId,
            RelativePath   relativePath)
        {
            m_nodeId      = ExpandedNodeId.ToNodeId(nodeId, context.NamespaceUris);
            m_browsePath  = relativePath;
            m_attributeId = Attributes.Value;
            m_indexRange  = null;
            m_alias       = null;
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
            FilterContext  context, 
            ExpandedNodeId typeDefinitionId,
            string         browsePath,
            uint           attributeId,
            string         indexRange)
        {
            m_nodeId      = ExpandedNodeId.ToNodeId(typeDefinitionId, context.NamespaceUris);
            m_browsePath  = RelativePath.Parse(browsePath, context.TypeTree);
            m_attributeId = attributeId;
            m_indexRange  = indexRange;
            m_alias       = null;                        
        }
        #endregion
        
        #region IFormattable Members
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder buffer = new StringBuilder();

                for (int ii = 0; ii < m_browsePath.Elements.Count; ii++)
                {
                    buffer.AppendFormat(formatProvider, "/{0}", m_browsePath.Elements[ii].TargetName);
                }

                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion
        
        #region Overridden Methods
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
        #endregion

        #region Public Properties
        /// <summary>
        /// Whether the operand has been validated.
        /// </summary>
        /// <value><c>true</c> if validated; otherwise, <c>false</c>.</value>
        /// <remarks>
        /// Set when Validate() is called.
        /// </remarks>
        public bool Validated
        {
            get { return m_validated; }
        }

        /// <summary>
        /// Stores the parsed form of the IndexRange parameter.
        /// </summary>
        /// <value>The parsed index range.</value>
        /// <remarks>
        /// Set when Validate() is called.
        /// </remarks>
        public NumericRange ParsedIndexRange
        {
            get { return m_parsedIndexRange; }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Validates the operand (sets the ParsedBrowsePath and ParsedIndexRange properties).
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="index">The index.</param>
        /// <returns>The result of the validation.</returns>
        public override ServiceResult Validate(FilterContext context, int index)
        {
            m_validated = false;

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
            if (!String.IsNullOrEmpty(m_indexRange))
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

            m_validated = true;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Converts an AttributeOperand to a displayable string.
        /// </summary>
        /// <param name="nodeTable">The node table.</param>
        /// <returns>AttributeOperand as a displayable string.</returns>
        public override string ToString(INodeTable nodeTable)
        {
            StringBuilder buffer = new StringBuilder();

            INode node = nodeTable.Find(m_nodeId);

            if (node != null)
            {
                buffer.AppendFormat("{0}", NodeId);
            }
            else
            {
                buffer.AppendFormat("{0}", NodeId);
            }
             
            if (!RelativePath.IsEmpty(BrowsePath))
            {
                buffer.AppendFormat("/{0}", BrowsePath.Format(nodeTable.TypeTree));
            }

            if (!String.IsNullOrEmpty(IndexRange))
            {
                buffer.AppendFormat("[{0}]", NumericRange.Parse(IndexRange));
            }

            if (!String.IsNullOrEmpty(Alias))
            {
                buffer.AppendFormat("- '{0}'", Alias);
            }
            
            return buffer.ToString();
        }
        #endregion
        
        #region Private Fields
        private bool m_validated;
        private NumericRange m_parsedIndexRange;
        #endregion
    }
    #endregion

    #region ElementOperand Class
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

        #region IFormattable Members
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return String.Format("[{0}]", m_index);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion
        
        #region Overridden Methods
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
        #endregion

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
        /// <param name="table">The table.</param>
        /// <returns>ElementOperand as a displayable string.</returns>
        public override string ToString(INodeTable table)
        {
            return Utils.Format("Element[{0}]", Index);
        }
    }
    #endregion

    #region LiteralOperand Class
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

        #region IFormattable Members
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return String.Format("{0}", m_value);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion
        
        #region Overridden Methods
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
        #endregion

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
        /// <param name="table">The table.</param>
        /// <returns>LiteralOperand as a displayable string.</returns>
        public override string ToString(INodeTable table)
        {
            ExpandedNodeId nodeId = Value.Value as ExpandedNodeId;
            
            if (nodeId == null)
            {
                nodeId = Value.Value as NodeId; 
            }

            if (nodeId != null)
            {
                INode node = table.Find(nodeId);

                if (node != null)
                {
                    return Utils.Format("{0} ({1})", node, nodeId);
                }
            }

            return Utils.Format("{0}", Value);
        }
    }
    #endregion
}
