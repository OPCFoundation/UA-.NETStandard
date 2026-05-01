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
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// A filter to apply when monitoring data changes.
    /// </summary>
    public partial class DataChangeFilter
    {
        /// <summary>
        /// Checks that the filter is valid.
        /// </summary>
        public ServiceResult Validate()
        {
            // check deadband type enumeration.
            if ((int)DeadbandType is < ((int)Ua.DeadbandType.None) or > ((int)Ua.DeadbandType.Percent))
            {
                return ServiceResult.Create(
                    StatusCodes.BadDeadbandFilterInvalid,
                    "Deadband type '{0}' is not recognized.",
                    DeadbandType);
            }

            // check data change trigger enumeration.
            if ((int)Trigger is < ((int)DataChangeTrigger.Status) or > ((int)DataChangeTrigger.StatusValueTimestamp))
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
            if ((int)DeadbandType == (int)Ua.DeadbandType.Percent && DeadbandValue > 100)
            {
                return ServiceResult.Create(
                    StatusCodes.BadDeadbandFilterInvalid,
                    "Percentage deadband value '{0}' cannot be greater than 100.",
                    DeadbandValue);
            }

            // passed initial validation.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns the AbsoluteDeadband if the filter specifies one. Zero otherwize.
        /// </summary>
        public static double GetAbsoluteDeadband(MonitoringFilter filter)
        {
            if (filter is not DataChangeFilter datachangeFilter)
            {
                return 0.0;
            }

            if (datachangeFilter.DeadbandType != (uint)Ua.DeadbandType.Absolute)
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
            if (filter is not DataChangeFilter datachangeFilter)
            {
                return 0.0;
            }

            if (datachangeFilter.DeadbandType != (uint)Ua.DeadbandType.Percent)
            {
                return 0.0;
            }

            return datachangeFilter.DeadbandValue;
        }

        /// <summary>
        /// Checks if the two values are equal based on this DataChangeFilter.
        /// </summary>
        public bool AreEqual(
            Variant value1,
            Variant value2,
            double range)
        {
            // check if equal
            if (value1.Equals(value2))
            {
                return true;
            }

            Ua.DeadbandType actualDeadbandType = (Ua.DeadbandType)(int)DeadbandType;
            BuiltInType builtInType = value1.TypeInfo.BuiltInType;
            int valueRank = value1.TypeInfo.ValueRank;

            // handle scalars.
            if (valueRank == ValueRanks.Scalar)
            {
                switch (builtInType)
                {
                    case BuiltInType.Double:
                        if (value1.TryGetValue(out double d1) && value2.TryGetValue(out double d2))
                        {
                            if (double.IsNaN(d1) || double.IsNaN(d2))
                            {
                                return false;
                            }

                            if (d1 == d2)
                            {
                                return true;
                            }

                            if (actualDeadbandType == Ua.DeadbandType.None)
                            {
                                return false;
                            }

                            return !ExceedsDeadband(d1, d2, actualDeadbandType, DeadbandValue, range);
                        }
                        return false;
                    case BuiltInType.Float:
                        if (value1.TryGetValue(out float f1) && value2.TryGetValue(out float f2))
                        {
                            if (float.IsNaN(f1) || float.IsNaN(f2))
                            {
                                return false;
                            }

                            if (f1 == f2)
                            {
                                return true;
                            }

                            if (actualDeadbandType == Ua.DeadbandType.None)
                            {
                                return false;
                            }

                            return !ExceedsDeadband(f1, f2, actualDeadbandType, DeadbandValue, range);
                        }
                        return false;
                    case BuiltInType.XmlElement:
                        if (value1.AsBoxedObject() is System.Xml.XmlElement xml1 && value2.AsBoxedObject() is System.Xml.XmlElement xml2)
                        {
                            return xml1 == xml2;
                        }
                        return false;
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Int32:
                    case BuiltInType.UInt32:
                    case BuiltInType.Int64:
                    case BuiltInType.UInt64:
                    case BuiltInType.Int16:
                    case BuiltInType.UInt16:
                    case BuiltInType.Byte:
                    case BuiltInType.SByte:
                        // Other numeric types check deadband if applicable
                        if (actualDeadbandType != Ua.DeadbandType.None)
                        {
                            return !ExceedsDeadband(value1, value2, actualDeadbandType, DeadbandValue, range);
                        }
                        return false;
                }

                return false;
            }

            // handle arrays.
            switch (builtInType)
            {
                case BuiltInType.Double:
                {
                    if (value1.TryGetValue(out ArrayOf<double> dArray1) &&
                        value2.TryGetValue(out ArrayOf<double> dArray2))
                    {
                        if (dArray1.Count != dArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < dArray1.Count; ii++)
                        {
                            double d1 = dArray1[ii];
                            double d2 = dArray2[ii];

                            if (double.IsNaN(d1) || double.IsNaN(d2))
                            {
                                return false;
                            }

                            if (d1 != d2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(d1, d2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.Float:
                {
                    if (value1.TryGetValue(out ArrayOf<float> fArray1) &&
                        value2.TryGetValue(out ArrayOf<float> fArray2))
                    {
                        if (fArray1.Count != fArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < fArray1.Count; ii++)
                        {
                            float f1 = fArray1[ii];
                            float f2 = fArray2[ii];

                            if (float.IsNaN(f1) || float.IsNaN(f2))
                            {
                                return false;
                            }

                            if (f1 != f2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(f1, f2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.Variant:
                {
                    if (value1.TryGetValue(out ArrayOf<Variant> vArray1) &&
                        value2.TryGetValue(out ArrayOf<Variant> vArray2))
                    {
                        if (vArray1.Count != vArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < vArray1.Count; ii++)
                        {
                            if (!AreEqual(vArray1[ii], vArray2[ii], range))
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.Int16:
                {
                    if (value1.TryGetValue(out ArrayOf<short> iArray1) &&
                        value2.TryGetValue(out ArrayOf<short> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            short s1 = iArray1[ii];
                            short s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.UInt16:
                {
                    if (value1.TryGetValue(out ArrayOf<ushort> iArray1) &&
                        value2.TryGetValue(out ArrayOf<ushort> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            ushort s1 = iArray1[ii];
                            ushort s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.Int32:
                {
                    if (value1.TryGetValue(out ArrayOf<int> iArray1) &&
                        value2.TryGetValue(out ArrayOf<int> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            int s1 = iArray1[ii];
                            int s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.UInt32:
                {
                    if (value1.TryGetValue(out ArrayOf<uint> iArray1) &&
                        value2.TryGetValue(out ArrayOf<uint> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            uint s1 = iArray1[ii];
                            uint s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.Int64:
                {
                    if (value1.TryGetValue(out ArrayOf<long> iArray1) &&
                        value2.TryGetValue(out ArrayOf<long> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            long s1 = iArray1[ii];
                            long s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.UInt64:
                {
                    if (value1.TryGetValue(out ArrayOf<ulong> iArray1) &&
                        value2.TryGetValue(out ArrayOf<ulong> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            ulong s1 = iArray1[ii];
                            ulong s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.Byte:
                {
                    if (value1.TryGetValue(out ArrayOf<byte> iArray1) &&
                        value2.TryGetValue(out ArrayOf<byte> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            byte s1 = iArray1[ii];
                            byte s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.SByte:
                {
                    if (value1.TryGetValue(out ArrayOf<sbyte> iArray1) &&
                        value2.TryGetValue(out ArrayOf<sbyte> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            sbyte s1 = iArray1[ii];
                            sbyte s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.ByteString:
                {
                    if (value1.TryGetValue(out ArrayOf<ByteString> iArray1) &&
                        value2.TryGetValue(out ArrayOf<ByteString> iArray2))
                    {
                        if (iArray1.Count != iArray2.Count)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < iArray1.Count; ii++)
                        {
                            ByteString s1 = iArray1[ii];
                            ByteString s2 = iArray2[ii];

                            if (s1 != s2)
                            {
                                if (actualDeadbandType == Ua.DeadbandType.None ||
                                    ExceedsDeadband(s1, s2, actualDeadbandType, DeadbandValue, range))
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    return false;
                }
                case BuiltInType.XmlElement:
                {
                    if (value1.AsBoxedObject() is System.Xml.XmlElement[] xArray1 &&
                        value2.AsBoxedObject() is System.Xml.XmlElement[] xArray2)
                    {
                        if (xArray1.Length != xArray2.Length)
                        {
                            return false;
                        }

                        for (int ii = 0; ii < xArray1.Length; ii++)
                        {
                            System.Xml.XmlElement xml1 = xArray1[ii];
                            System.Xml.XmlElement xml2 = xArray2[ii];

                            if (xml1 != xml2)
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the deadband was exceeded.
        /// </summary>
        private static bool ExceedsDeadband(
            Variant value1,
            Variant value2,
            Ua.DeadbandType deadbandType,
            double deadband,
            double range)
        {
            // cannot convert doubles safely to decimals.
            if (value1.TryGetValue(out double x))
            {
                return ExceedsDeadband(x, value2.GetDouble(), deadbandType, deadband, range);
            }

            try
            {
                if (!value1.TryGetValueDecimal(out decimal decimal1) ||
                   !value2.TryGetValueDecimal(out decimal decimal2))
                {
                    throw new InvalidOperationException("Failed to check for deadband");
                }
                decimal baseline = 1;

                if (deadbandType == Ua.DeadbandType.Percent)
                {
                    baseline = ((decimal)range) / 100;
                }

                if (baseline > 0 && Math.Abs((decimal1 - decimal2) / baseline) <= (decimal)deadband)
                {
                    return false;
                }
            }
            catch
            {
                // treat all conversion errors as evidence that the deadband was exceeded.
            }

            return true;
        }

        /// <summary>
        /// Returns true if the deadband was exceeded.
        /// </summary>
        private static bool ExceedsDeadband(
            double value1,
            double value2,
            Ua.DeadbandType deadbandType,
            double deadband,
            double range)
        {
            double baseline = 1;

            if (deadbandType == Ua.DeadbandType.Percent)
            {
                baseline = range / 100;
            }

            return baseline <= 0 || Math.Abs((value1 - value2) / baseline) > deadband;
        }
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
            var clause = new SimpleAttributeOperand
            {
                TypeDefinitionId = eventTypeId,
                AttributeId = Attributes.Value
            };

            clause.BrowsePath = clause.BrowsePath.AddItem(propertyName);

            SelectClauses = SelectClauses.AddItem(clause);
        }

        /// <summary>
        /// Adds the specified browse path to the event filter.
        /// </summary>
        public void AddSelectClause(NodeId eventTypeId, string browsePath, uint attributeId)
        {
            var clause = new SimpleAttributeOperand
            {
                TypeDefinitionId = eventTypeId,
                AttributeId = attributeId
            };

            if (!string.IsNullOrEmpty(browsePath))
            {
                clause.BrowsePath = SimpleAttributeOperand.Parse(browsePath);
            }

            SelectClauses = SelectClauses.AddItem(clause);
        }

        /// <summary>
        /// Stores the validation results for a EventFilter.
        /// </summary>
        public class Result
        {
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
                return new Result { Status = status };
            }

            /// <summary>
            /// The result for the entire filter.
            /// </summary>
            public ServiceResult Status { get; set; }

            /// <summary>
            /// Returns a string containing the errors reported.
            /// </summary>
            public string GetLongString()
            {
                var buffer = new StringBuilder();

                foreach (ServiceResult selectResult in SelectClauseResults)
                {
                    if (ServiceResult.IsBad(selectResult))
                    {
                        buffer.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "Select Clause Error: {0}",
                            selectResult.ToString())
                            .AppendLine();
                    }
                }

                if (ServiceResult.IsBad(WhereClauseResult.Status))
                {
                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "Where Clause Error: {0}",
                        WhereClauseResult.Status.ToString())
                        .AppendLine();

                    foreach (ContentFilter.ElementResult elementResult in WhereClauseResult
                        .ElementResults)
                    {
                        if (elementResult != null && ServiceResult.IsBad(elementResult.Status))
                        {
                            buffer.AppendFormat(
                                CultureInfo.InvariantCulture,
                                "Element Error: {0}",
                                elementResult.Status.ToString())
                                .AppendLine();

                            foreach (ServiceResult operandResult in elementResult.OperandResults)
                            {
                                if (ServiceResult.IsBad(operandResult))
                                {
                                    buffer.AppendFormat(
                                        CultureInfo.InvariantCulture,
                                        "Operand Error: {0}",
                                        operandResult.ToString())
                                        .AppendLine();
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
            public List<ServiceResult> SelectClauseResults => m_selectClauseResults ??= [];

            /// <summary>
            /// The results for the where clause.
            /// </summary>
            public ContentFilter.Result WhereClauseResult { get; internal set; }

            /// <summary>
            /// Converts the object to an EventFilterResult.
            /// </summary>
            public EventFilterResult ToEventFilterResult(
                DiagnosticsMasks diagnosticsMasks,
                StringTable stringTable,
                ILogger logger)
            {
                var result = new EventFilterResult();

                if (m_selectClauseResults != null && m_selectClauseResults.Count > 0)
                {
                    foreach (ServiceResult clauseResult in m_selectClauseResults)
                    {
                        if (ServiceResult.IsBad(clauseResult))
                        {
                            result.SelectClauseResults = result.SelectClauseResults.AddItem(clauseResult.StatusCode);
                            result.SelectClauseDiagnosticInfos = result.SelectClauseDiagnosticInfos.AddItem(
                                new DiagnosticInfo(
                                    clauseResult,
                                    diagnosticsMasks,
                                    false,
                                    stringTable,
                                    logger));
                        }
                        else
                        {
                            result.SelectClauseResults =
                                result.SelectClauseResults.AddItem(StatusCodes.Good);
                            result.SelectClauseDiagnosticInfos =
                                result.SelectClauseDiagnosticInfos.AddItem(null);
                        }
                    }
                }

                if (WhereClauseResult != null)
                {
                    result.WhereClauseResult = WhereClauseResult.ToContextFilterResult(
                        diagnosticsMasks,
                        stringTable,
                        logger);
                }

                return result;
            }

            private List<ServiceResult> m_selectClauseResults;
        }

        /// <summary>
        /// Validates the object.
        /// </summary>
        public Result Validate(IFilterContext context)
        {
            var result = new Result();

            // check for top level error.
            if (m_selectClauses.IsEmpty)
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
        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        public SimpleAttributeOperand(NodeId typeId, QualifiedName browsePath)
        {
            m_typeDefinitionId = typeId;
            m_browsePath = [browsePath];
            m_attributeId = Attributes.Value;
            m_indexRange = null;
        }

        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        public SimpleAttributeOperand(NodeId typeId, ArrayOf<QualifiedName> browsePath)
        {
            m_typeDefinitionId = typeId;
            m_browsePath = browsePath;
            m_attributeId = Attributes.Value;
            m_indexRange = null;
        }

        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        public SimpleAttributeOperand(
            IFilterContext context,
            ExpandedNodeId typeId,
            ArrayOf<QualifiedName> browsePath)
        {
            m_typeDefinitionId = ExpandedNodeId.ToNodeId(typeId, context.NamespaceUris);
            m_browsePath = browsePath;
            m_attributeId = Attributes.Value;
            m_indexRange = null;
        }

        /// <summary>
        /// Creates an operand that references a component/property of a type.
        /// </summary>
        public SimpleAttributeOperand(
            IFilterContext context,
            ExpandedNodeId typeDefinitionId,
            string browsePath,
            uint attributeId,
            string indexRange)
        {
            m_typeDefinitionId = ExpandedNodeId.ToNodeId(typeDefinitionId, context.NamespaceUris);
            m_browsePath = Parse(browsePath);
            m_attributeId = attributeId;
            m_indexRange = indexRange;
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

                for (int ii = 0; ii < m_browsePath.Count; ii++)
                {
                    buffer.AppendFormat(formatProvider, "/{0}", m_browsePath[ii]);
                }

                return buffer.ToString();
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Whether the operand has been validated.
        /// </summary>
        /// <remarks>
        /// Set when Validate() is called.
        /// </remarks>
        public bool Validated { get; private set; }

        /// <summary>
        /// Stores the parsed form of the IndexRange parameter.
        /// </summary>
        /// <remarks>
        /// Set when Validate() is called.
        /// </remarks>
        public NumericRange ParsedIndexRange => m_parsedIndexRange;

        /// <summary>
        /// Validates the operand (sets the ParsedBrowsePath and ParsedIndexRange properties).
        /// </summary>
        public override ServiceResult Validate(IFilterContext context, int index)
        {
            Validated = false;

            // verify attribute id.
            if (!Attributes.IsValid(m_attributeId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadAttributeIdInvalid,
                    "SimpleAttributeOperand does not specify a valid AttributeId ({0}).",
                    m_attributeId);
            }

            // initialize as empty.
            m_parsedIndexRange = default;

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

            Validated = true;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Converts an AttributeOperand to a displayable string.
        /// </summary>
        public override string ToString(INodeTable nodeTable)
        {
            var buffer = new StringBuilder();

            INode node = nodeTable.Find(TypeDefinitionId);

            if (node != null)
            {
                // TODO: why is if and else the same codepath
                buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", TypeDefinitionId);
            }
            else
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", TypeDefinitionId);
            }

            if (!BrowsePath.IsEmpty)
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", Format(BrowsePath));
            }

            if (!string.IsNullOrEmpty(IndexRange))
            {
                buffer.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "[{0}]",
                    NumericRange.Parse(IndexRange));
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Formats a browse path.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static string Format(ArrayOf<QualifiedName> browsePath)
        {
            if (browsePath.IsEmpty)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder();

            for (int ii = 0; ii < browsePath.Count; ii++)
            {
                QualifiedName browseName = browsePath[ii];

                if (browseName.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameInvalid,
                        "BrowseName cannot be null");
                }

                buffer.Append('/');

                if (browseName.NamespaceIndex != 0)
                {
                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0}:",
                        browseName.NamespaceIndex);
                }

                for (int jj = 0; jj < browseName.Name.Length; jj++)
                {
                    char ch = browseName.Name[jj];

                    if (ch is '&' or '/')
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
        public static ArrayOf<QualifiedName> Parse(string browsePath)
        {
            var browseNames = new List<QualifiedName>();

            if (string.IsNullOrEmpty(browsePath))
            {
                return browseNames;
            }

            var buffer = new StringBuilder();

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
                        var browseName = QualifiedName.Parse(buffer.ToString());
                        browseNames.Add(browseName);
                    }

                    buffer.Length = 0;
                    continue;
                }

                buffer.Append(ch);
            }

            if (buffer.Length > 0)
            {
                var browseName = QualifiedName.Parse(buffer.ToString());
                browseNames.Add(browseName);
            }

            return browseNames;
        }

        private NumericRange m_parsedIndexRange;
    }
}
