/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// A filter to apply when monitoring data changes.
    /// </summary>
    public partial class DataChangeFilter
    {
        #region Public Methods
        /// <summary>
        /// Checks that the filter is valid.
        /// </summary>
        public ServiceResult Validate()
        {
            // check deadband type enumeration.
            if ((int)DeadbandType < (int)Opc.Ua.DeadbandType.None || (int)DeadbandType > (int)Opc.Ua.DeadbandType.Percent)
            {
                return ServiceResult.Create(
                    StatusCodes.BadDeadbandFilterInvalid, 
                    "Deadband type '{0}' is not recognized.", 
                    DeadbandType);
            }

            // check data change trigger enumeration.
            if ((int)Trigger < (int)DataChangeTrigger.Status  || (int)Trigger > (int)DataChangeTrigger.StatusValueTimestamp)
            {
                return ServiceResult.Create(
                    StatusCodes.BadDeadbandFilterInvalid, 
                    "Deadband trigger '{0}' is not recognized.", 
                    Trigger);
            }

            // deadband value must always be greater than 0.
            if (DeadbandValue < 0)
            {
                return ServiceResult.Create(
                    StatusCodes.BadDeadbandFilterInvalid, 
                    "Deadband value '{0}' cannot be less than zero.", 
                    DeadbandValue);
            }

            // deadband percentage must be less than 100.
            if ((int)DeadbandType == (int)Opc.Ua.DeadbandType.Percent)
            {
                if (DeadbandValue > 100)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadDeadbandFilterInvalid, 
                        "Percentage deadband value '{0}' cannot be greater than 100.", 
                        DeadbandValue);
                }
            }

            // passed initial validation.
            return ServiceResult.Good;
        }
        
        /// <summary>
        /// Returns the AbsoluteDeadband if the filter specifies one. Zero otherwize.
        /// </summary>
        public static double GetAbsoluteDeadband(MonitoringFilter filter)
        {
            DataChangeFilter datachangeFilter = filter as DataChangeFilter;

            if (datachangeFilter == null)
            {
                return 0.0;
            }

            if (datachangeFilter.DeadbandType != (uint)Opc.Ua.DeadbandType.Absolute)
            {
                return 0.0;
            }

            return datachangeFilter.DeadbandValue;
        }

        /// <summary>
        /// Returns the PercentageDeadband if the filter specifies one. Zero otherwize.
        /// </summary>
        public static double GetPercentageDeadband(MonitoringFilter filter)
        {
            DataChangeFilter datachangeFilter = filter as DataChangeFilter;

            if (datachangeFilter == null)
            {
                return 0.0;
            }

            if (datachangeFilter.DeadbandType != (uint)Opc.Ua.DeadbandType.Percent)
            {
                return 0.0;
            }

            return datachangeFilter.DeadbandValue;
        }
        #endregion
    }
    
    /// <summary>
    /// A filter to apply when monitoring event.
    /// </summary>
    public partial class EventFilter
    {
        /// <summary>
        /// Adds the specified property to the event filter.
        /// </summary>
        public void AddSelectClause(NodeId eventTypeId, QualifiedName propertyName)
        {
            SimpleAttributeOperand clause = new SimpleAttributeOperand();

            clause.TypeDefinitionId = eventTypeId;
            clause.AttributeId      = Attributes.Value;

            clause.BrowsePath.Add(propertyName);

            SelectClauses.Add(clause);
        }

        /// <summary>
        /// Adds the specified browse path to the event filter.
        /// </summary>
        public void AddSelectClause(NodeId eventTypeId, string browsePath, uint attributeId)
        {
            SimpleAttributeOperand clause = new SimpleAttributeOperand();

            clause.TypeDefinitionId = eventTypeId;
            clause.AttributeId      = attributeId;

            if (!String.IsNullOrEmpty(browsePath))
            {
                clause.BrowsePath = SimpleAttributeOperand.Parse(browsePath);
            }            

            SelectClauses.Add(clause);
        }

        /// <summary>
        /// Stores the validation results for a EventFilter.
        /// </summary>
        public class Result
        {
            #region Public Interface
            /// <summary>
            /// Initializes the object.
            /// </summary>
            public Result()
            {
            }

            /// <summary>
            /// Casts ServiceResult to an ElementResult.
            /// </summary>
            public static implicit operator Result(ServiceResult status)
            {
                Result result = new Result();
                result.Status = status;
                return result;
            }
            
            /// <summary>
            /// The result for the entire filter.
            /// </summary>
            public ServiceResult Status
            {
                get { return m_status;  }
                set { m_status = value; }
            }

            /// <summary>
            /// Returns a string containing the errors reported.
            /// </summary>
            public string GetLongString()
            {
                StringBuilder buffer = new StringBuilder();

                foreach (ServiceResult selectResult in SelectClauseResults)
                {
                    if (ServiceResult.IsBad(selectResult))
                    {
                        buffer.AppendFormat("Select Clause Error: {0}", selectResult.ToString());
                        buffer.AppendLine();
                    }
                }

                if (ServiceResult.IsBad(WhereClauseResult.Status))
                {                    
                    buffer.AppendFormat("Where Clause Error: {0}", WhereClauseResult.Status.ToString());
                    buffer.AppendLine();

                    foreach (ContentFilter.ElementResult elementResult in WhereClauseResult.ElementResults)
                    {
                        if (elementResult != null && ServiceResult.IsBad(elementResult.Status))
                        {
                            buffer.AppendFormat("Element Error: {0}", elementResult.Status.ToString());
                            buffer.AppendLine();

                            foreach (ServiceResult operandResult in elementResult.OperandResults)
                            {
                                if (ServiceResult.IsBad(operandResult))
                                {
                                    buffer.AppendFormat("Operand Error: {0}",operandResult.ToString());
                                    buffer.AppendLine();
                                }
                            }
                        }
                    }
                }

                return buffer.ToString();
            }
                     
            /// <summary>
            /// The result for each select clause.
            /// </summary>   
            public List<ServiceResult> SelectClauseResults
            {
                get
                {
                    if (m_selectClauseResults == null)
                    {
                        m_selectClauseResults = new List<ServiceResult>();
                    }

                    return m_selectClauseResults;
                }
            }

            /// <summary>
            /// The results for the where clause.
            /// </summary>
            public Opc.Ua.ContentFilter.Result WhereClauseResult
            {
                get
                {
                    return m_whereClauseResults;
                }

                internal set 
                {
                    m_whereClauseResults = value;
                }
            }
            
            /// <summary>
            /// Converts the object to an EventFilterResult.
            /// </summary>
            public EventFilterResult ToEventFilterResult(DiagnosticsMasks diagnosticsMasks, StringTable stringTable)
            {
                EventFilterResult result = new EventFilterResult();

                if (m_selectClauseResults != null && m_selectClauseResults.Count > 0)
                {
                    foreach (ServiceResult clauseResult in m_selectClauseResults)
                    {
                        if (ServiceResult.IsBad(clauseResult))
                        {
                            result.SelectClauseResults.Add(clauseResult.StatusCode);
                            result.SelectClauseDiagnosticInfos.Add(new DiagnosticInfo(clauseResult, diagnosticsMasks, false, stringTable));
                        }
                        else
                        {
                            result.SelectClauseResults.Add(StatusCodes.Good);
                            result.SelectClauseDiagnosticInfos.Add(null);
                        }
                    }
                }

                if (m_whereClauseResults != null)
                {
                    result.WhereClauseResult = m_whereClauseResults.ToContextFilterResult(diagnosticsMasks, stringTable);
                }

                return result;
            }
            #endregion
            
            #region Private Fields
            private ServiceResult m_status;
            private List<ServiceResult> m_selectClauseResults;
            private ContentFilter.Result m_whereClauseResults;
            #endregion
        }

        /// <summary>
        /// Validates the object.
        /// </summary>
        public Result Validate(FilterContext context)
        {
            Result result = new Result();

            // check for top level error.
            if (m_selectClauses == null || m_selectClauses.Count == 0)
            {
                result.Status = ServiceResult.Create(
                    StatusCodes.BadStructureMissing, 
                    "EventFilter does not specify any Select Clauses.");

                return result;
            }            

            if (m_whereClause == null)
            {
                result.Status = ServiceResult.Create(
                    StatusCodes.BadStructureMissing, 
                    "EventFilter does not specify any Where Clauses.");

                return result;
            }

            result.Status = ServiceResult.Good;

            // validate select clause.
            bool error = false;

            foreach (SimpleAttributeOperand clause in m_selectClauses)
            {
                ServiceResult clauseResult = null;

                // check for null.
                if (clause == null)
                {
                    clauseResult = ServiceResult.Create(
                        StatusCodes.BadStructureMissing, 
                        "EventFilterSelectClause cannot be null in EventFilter SelectClause.");

                    result.SelectClauseResults.Add(clauseResult);
                    error = true;
                    continue;
                }

                // validate clause.
                clauseResult = clause.Validate(context, 0);

                if (ServiceResult.IsBad(clauseResult))
                {
                    result.SelectClauseResults.Add(clauseResult);
                    error = true;
                    continue;
                }
                
                // clause ok.                    
                result.SelectClauseResults.Add(null);
            }

            if (error)
            {
                result.Status = StatusCodes.BadEventFilterInvalid;
            }
            else
            {
                result.SelectClauseResults.Clear();
            }
            
            // validate where clause.          
            result.WhereClauseResult = m_whereClause.Validate(context);

            if (ServiceResult.IsBad(result.WhereClauseResult.Status))
            {
                result.Status = StatusCodes.BadEventFilterInvalid;
            }

            return result;
        }
    }
        
    /// <summary>
    /// A clause that identifies a field to return with the event.
    /// </summary>
    public partial class SimpleAttributeOperand : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        public SimpleAttributeOperand(
            NodeId typeId,
            QualifiedName browsePath)
        {
            m_typeDefinitionId = typeId;
            m_browsePath       = new QualifiedNameCollection();
            m_attributeId      = Attributes.Value;
            m_indexRange       = null;

            m_browsePath.Add(browsePath);
        }

        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        public SimpleAttributeOperand(
            NodeId typeId,
            IList<QualifiedName> browsePath)
        {
            m_typeDefinitionId = typeId;
            m_browsePath       = new QualifiedNameCollection(browsePath);
            m_attributeId      = Attributes.Value;
            m_indexRange       = null;
        }

        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        public SimpleAttributeOperand(
            FilterContext        context, 
            ExpandedNodeId       typeId,
            IList<QualifiedName> browsePath)
        {
            m_typeDefinitionId = ExpandedNodeId.ToNodeId(typeId, context.NamespaceUris);
            m_browsePath       = new QualifiedNameCollection(browsePath);
            m_attributeId      = Attributes.Value;
            m_indexRange       = null;
        }
        
        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        public SimpleAttributeOperand(
            FilterContext  context, 
            ExpandedNodeId typeDefinitionId,
            string         browsePath,
            uint           attributeId,
            string         indexRange)
        {
            m_typeDefinitionId = ExpandedNodeId.ToNodeId(typeDefinitionId, context.NamespaceUris);
            m_browsePath       = Parse(browsePath);
            m_attributeId      = attributeId;
            m_indexRange       = indexRange;                
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

                for (int ii = 0; ii < m_browsePath.Count; ii++)
                {
                    buffer.AppendFormat(formatProvider, "/{0}", m_browsePath[ii]);
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
        public override ServiceResult Validate(FilterContext context, int index)
        {
            m_validated = false;

            // verify attribute id.
            if (!Attributes.IsValid(m_attributeId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadAttributeIdInvalid, 
                    "SimpleAttributeOperand does not specify a valid AttributeId ({0}).", 
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
                        "SimpleAttributeOperand does not specify a valid BrowsePath ({0}).", 
                        m_indexRange);
                }

                if (m_attributeId != Attributes.Value)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadIndexRangeInvalid, 
                        "SimpleAttributeOperand specifies an IndexRange for an Attribute other than Value ({0}).", 
                        m_attributeId);
                }
            }

            m_validated = true;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Converts an AttributeOperand to a displayable string.
        /// </summary>
        public override string ToString(INodeTable nodeTable)
        {
            StringBuilder buffer = new StringBuilder();

            INode node = nodeTable.Find(TypeDefinitionId);

            if (node != null)
            {
                buffer.AppendFormat("{0}", TypeDefinitionId);
            }
            else
            {
                buffer.AppendFormat("{0}", TypeDefinitionId);
            }
             
            if (BrowsePath != null && BrowsePath.Count > 0)
            {
                buffer.AppendFormat("{0}", Format(BrowsePath));
            }

            if (!String.IsNullOrEmpty(IndexRange))
            {
                buffer.AppendFormat("[{0}]", NumericRange.Parse(IndexRange));
            }
            
            return buffer.ToString();
        }
        #endregion

        #region Static Methods
        /// <summary>
        /// Formats a browse path.
        /// </summary>
        public static string Format(IList<QualifiedName> browsePath)
        {
            if (browsePath == null || browsePath.Count == 0)
            {
                return String.Empty;
            }

            StringBuilder buffer = new StringBuilder();

            for (int ii = 0; ii < browsePath.Count; ii++)
            {
                QualifiedName browseName = browsePath[ii];

                if (QualifiedName.IsNull(browseName))
                {
                    throw ServiceResultException.Create(StatusCodes.BadBrowseNameInvalid, "BrowseName cannot be null");
                }

                buffer.Append('/');
                
                if (browseName.NamespaceIndex != 0)
                {
                    buffer.AppendFormat("{0}:", browseName.NamespaceIndex);
                }

                for (int jj = 0; jj < browseName.Name.Length; jj++)
                {
                    char ch = browseName.Name[jj];

                    if (ch == '&' || ch == '/')
                    {
                        buffer.Append('&');
                    }

                    buffer.Append(ch);
                }
            }

            return buffer.ToString();
        }
                
        /// <summary>
        /// Parses a browse path.
        /// </summary>
        public static QualifiedNameCollection Parse(string browsePath)
        {
            QualifiedNameCollection browseNames = new QualifiedNameCollection();

            if (String.IsNullOrEmpty(browsePath))
            {
                return browseNames;
            }
                       
            StringBuilder buffer = new StringBuilder();

            bool escaped = false;

            for (int ii = 0; ii < browsePath.Length; ii++)
            {
                char ch = browsePath[ii];

                if (escaped)
                {
                    buffer.Append(ch);
                    escaped = false;
                    continue;
                }

                if (ch == '&')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '/')
                {
                    if (buffer.Length > 0)
                    {
                        QualifiedName browseName = QualifiedName.Parse(buffer.ToString());
                        browseNames.Add(browseName);
                    }

                    buffer.Length = 0;
                    continue;
                }

                buffer.Append(ch);
            }

            if (buffer.Length > 0)
            {
                QualifiedName browseName = QualifiedName.Parse(buffer.ToString());
                browseNames.Add(browseName);
            }

            return browseNames;
        }
        #endregion
        
        #region Private Fields
        private bool m_validated;
        private NumericRange m_parsedIndexRange;
        #endregion              
    }
}
