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
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
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
        private readonly ContentFilter m_filter;
        private readonly IFilterContext m_context;
        private readonly IFilterTarget m_target;
        private readonly ILogger m_logger;

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

                bool? result = Evaluate(0) as bool?;
                return result ?? false;
            }
        }

        /// <summary>
        /// Evaluates element at the specified index.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object Evaluate(int index)
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
                if (ExtensionObject.IsNull(extension))
                {
                    throw ServiceResultException.Unexpected("FilterOperand is null.");
                }

                if (extension.Body is not FilterOperand operand)
                {
                    throw ServiceResultException.Unexpected("FilterOperand is not supported.");
                }

                operands[ii++] = operand;
            }

            if (expectedCount > 0 && expectedCount != operands.Length)
            {
                throw ServiceResultException.Unexpected(
                    "ContentFilterElement does not have the correct number of operands.");
            }

            return operands;
        }

        /// <summary>
        /// Returns the operands necessary for the BitwiseAnd and BitwiseOr operations
        /// </summary>
        private Tuple<object, object> GetBitwiseOperands(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(operands[0]);
            object rhs = GetValue(operands[1]);

            if (lhs == null || rhs == null)
            {
                return Tuple.Create<object, object>(null, null);
            }

            if (!IsIntegerType(GetBuiltInType(lhs)) || !IsIntegerType(GetBuiltInType(rhs)))
            {
                return Tuple.Create<object, object>(null, null);
            }

            DoImplicitConversion(ref lhs, ref rhs);

            return Tuple.Create(lhs, rhs);
        }

        /// <summary>
        /// Returns the value for the element.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object GetValue(FilterOperand operand)
        {
            // return the contained value for literal operands.

            if (operand is LiteralOperand literal)
            {
                return literal.Value.Value;
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
        /// Returns the BuiltInType type for the value.
        /// </summary>
        private static BuiltInType GetBuiltInType(object value)
        {
            if (value == null)
            {
                return BuiltInType.Null;
            }

            // return the type of the element for array values.
            Type systemType = value.GetType();

            if (value is Array)
            {
                systemType = systemType.GetElementType();
            }

            if (systemType == typeof(bool))
            {
                return BuiltInType.Boolean;
            }
            if (systemType == typeof(sbyte))
            {
                return BuiltInType.SByte;
            }
            if (systemType == typeof(byte))
            {
                return BuiltInType.Byte;
            }
            if (systemType == typeof(short))
            {
                return BuiltInType.Int16;
            }
            if (systemType == typeof(ushort))
            {
                return BuiltInType.UInt16;
            }
            if (systemType == typeof(int))
            {
                return BuiltInType.Int32;
            }
            if (systemType == typeof(uint))
            {
                return BuiltInType.UInt32;
            }
            if (systemType == typeof(long))
            {
                return BuiltInType.Int64;
            }
            if (systemType == typeof(ulong))
            {
                return BuiltInType.UInt64;
            }
            if (systemType == typeof(float))
            {
                return BuiltInType.Float;
            }
            if (systemType == typeof(double))
            {
                return BuiltInType.Double;
            }
            if (systemType == typeof(string))
            {
                return BuiltInType.String;
            }
            if (systemType == typeof(DateTime))
            {
                return BuiltInType.DateTime;
            }
            if (systemType == typeof(Guid) || systemType == typeof(Uuid))
            {
                return BuiltInType.Guid;
            }
            if (systemType == typeof(byte[]))
            {
                return BuiltInType.ByteString;
            }
            if (systemType == typeof(XmlElement))
            {
                return BuiltInType.XmlElement;
            }
            if (systemType == typeof(NodeId))
            {
                return BuiltInType.NodeId;
            }
            if (systemType == typeof(ExpandedNodeId))
            {
                return BuiltInType.ExpandedNodeId;
            }
            if (systemType == typeof(StatusCode))
            {
                return BuiltInType.StatusCode;
            }
            if (systemType == typeof(DiagnosticInfo))
            {
                return BuiltInType.DiagnosticInfo;
            }
            if (systemType == typeof(QualifiedName))
            {
                return BuiltInType.QualifiedName;
            }
            if (systemType == typeof(LocalizedText))
            {
                return BuiltInType.LocalizedText;
            }
            if (systemType == typeof(ExtensionObject))
            {
                return BuiltInType.ExtensionObject;
            }
            if (systemType == typeof(DataValue))
            {
                return BuiltInType.DataValue;
            }
            if (systemType == typeof(Variant))
            {
                return BuiltInType.Variant;
            }
            if (systemType == typeof(object))
            {
                return BuiltInType.Variant;
            }

            if (systemType.GetTypeInfo().IsEnum)
            {
                return BuiltInType.Enumeration;
            }

            // not a recognized type.
            return BuiltInType.Null;
        }

        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        private static BuiltInType GetBuiltInType(NodeId datatypeId)
        {
            if (datatypeId == null ||
                datatypeId.NamespaceIndex != 0 ||
                datatypeId.IdType != IdType.Numeric)
            {
                return BuiltInType.Null;
            }

            return (BuiltInType)Enum.ToObject(typeof(BuiltInType), datatypeId.Identifier);
        }

        /// <summary>
        /// Returns the data type precedence for the value.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static int GetDataTypePrecedence(BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Double:
                    return 18;
                case BuiltInType.Float:
                    return 17;
                case BuiltInType.Int64:
                    return 16;
                case BuiltInType.UInt64:
                    return 15;
                case BuiltInType.Int32:
                    return 14;
                case BuiltInType.UInt32:
                    return 13;
                case BuiltInType.StatusCode:
                    return 12;
                case BuiltInType.Int16:
                    return 11;
                case BuiltInType.UInt16:
                    return 10;
                case BuiltInType.SByte:
                    return 9;
                case BuiltInType.Byte:
                    return 8;
                case BuiltInType.Boolean:
                    return 7;
                case BuiltInType.Guid:
                    return 6;
                case BuiltInType.String:
                    return 5;
                case BuiltInType.ExpandedNodeId:
                    return 4;
                case BuiltInType.NodeId:
                    return 3;
                case BuiltInType.LocalizedText:
                    return 2;
                case BuiltInType.QualifiedName:
                    return 1;
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    return 0;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {type} encountered.");
            }
        }

        /// <summary>
        /// Implicitly converts the values according to their data type precedence.
        /// </summary>
        private void DoImplicitConversion(ref object value1, ref object value2)
        {
            BuiltInType type1 = GetBuiltInType(value1);
            BuiltInType type2 = GetBuiltInType(value2);

            int precedence1 = GetDataTypePrecedence(type1);
            int precedence2 = GetDataTypePrecedence(type2);

            // nothing to do if already the same.
            if (precedence1 == precedence2)
            {
                return;
            }

            // convert to the value with higher precedence.
            if (precedence1 > precedence2)
            {
                value2 = Cast(value2, type2, type1);
            }
            else
            {
                value1 = Cast(value1, type1, type2);
            }
        }

        /// <summary>
        /// Returns true if the values are equal.
        /// </summary>
        private static bool IsEqual(object value1, object value2)
        {
            if (value1 == null || value2 == null)
            {
                return value1 == null && value2 == null;
            }

            if (value1.GetType() != value2.GetType())
            {
                return false;
            }

            //check for strings
            if (value1 is string string1)
            {
                if (value2 is not string string2)
                {
                    return false;
                }
                return string1.Equals(string2, ContentFilter.EqualsOperatorDefaultStringComparison);
            }

            return Utils.IsEqual(value1, value2);
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

        /// <summary>
        /// Returns true if the type is Integer, otherwise returns false
        /// </summary>
        /// <param name="aType">The type to check against</param>
        /// <returns>true if the type is Integer, otherwise returns false</returns>
        private static bool IsIntegerType(BuiltInType aType)
        {
            return aType
                is BuiltInType.Byte
                    or BuiltInType.SByte
                    or BuiltInType.Int16
                    or BuiltInType.UInt16
                    or BuiltInType.Int32
                    or BuiltInType.UInt32
                    or BuiltInType.Int64
                    or BuiltInType.UInt64;
        }

        /// <summary>
        /// Converts a value to a Boolean
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToBoolean(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                bool[] output = new bool[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (bool)Cast(array.GetValue(ii), BuiltInType.Boolean);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Boolean:
                    return (bool)value;
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                    return Convert.ToBoolean((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToBoolean((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToBoolean((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToBoolean((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToBoolean((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToBoolean((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToBoolean((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToBoolean((float)value);
                case BuiltInType.Double:
                    return Convert.ToBoolean((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToBoolean((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a SByte
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToSByte(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                sbyte[] output = new sbyte[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (sbyte)Cast(array.GetValue(ii), BuiltInType.SByte);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.SByte:
                    return (sbyte)value;
                case BuiltInType.Boolean:
                    return Convert.ToSByte((bool)value);
                case BuiltInType.Byte:
                    return Convert.ToSByte((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToSByte((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToSByte((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToSByte((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToSByte((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToSByte((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToSByte((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToSByte((float)value);
                case BuiltInType.Double:
                    return Convert.ToSByte((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToSByte((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a Byte
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        private static object ToByte(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array)
            {
                throw new NotImplementedException(
                    "Arrays of Byte not supported. Use ByteString instead.");
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Byte:
                    return (byte)value;
                case BuiltInType.Boolean:
                    return Convert.ToByte((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToByte((sbyte)value);
                case BuiltInType.Int16:
                    return Convert.ToByte((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToByte((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToByte((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToByte((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToByte((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToByte((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToByte((float)value);
                case BuiltInType.Double:
                    return Convert.ToByte((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToByte((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a Int16
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToInt16(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                short[] output = new short[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (short)Cast(array.GetValue(ii), BuiltInType.Int16);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Int16:
                    return (short)value;
                case BuiltInType.Boolean:
                    return Convert.ToInt16((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToInt16((sbyte)value);
                case BuiltInType.Byte:
                    return Convert.ToInt16((byte)value);
                case BuiltInType.UInt16:
                    return Convert.ToInt16((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToInt16((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToInt16((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToInt16((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToInt16((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToInt16((float)value);
                case BuiltInType.Double:
                    return Convert.ToInt16((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToInt16((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a UInt16
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToUInt16(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                ushort[] output = new ushort[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (ushort)Cast(array.GetValue(ii), BuiltInType.UInt16);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.UInt16:
                    return (ushort)value;
                case BuiltInType.Boolean:
                    return Convert.ToUInt16((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToUInt16((sbyte)value);
                case BuiltInType.Byte:
                    return Convert.ToUInt16((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToUInt16((short)value);
                case BuiltInType.Int32:
                    return Convert.ToUInt16((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToUInt16((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToUInt16((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToUInt16((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToUInt16((float)value);
                case BuiltInType.Double:
                    return Convert.ToUInt16((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToUInt16((string)value);
                case BuiltInType.StatusCode:
                    var code = (StatusCode)value;
                    return (ushort)(code.CodeBits >> 16);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a Int32
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToInt32(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                int[] output = new int[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (int)Cast(array.GetValue(ii), BuiltInType.Int32);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Int32:
                    return (int)value;
                case BuiltInType.Boolean:
                    return Convert.ToInt32((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToInt32((sbyte)value);
                case BuiltInType.Byte:
                    return Convert.ToInt32((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToInt32((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToInt32((ushort)value);
                case BuiltInType.UInt32:
                    return Convert.ToInt32((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToInt32((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToInt32((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToInt32((float)value);
                case BuiltInType.Double:
                    return Convert.ToInt32((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToInt32((string)value);
                case BuiltInType.StatusCode:
                    return Convert.ToInt32(((StatusCode)value).Code);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a UInt32
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToUInt32(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                uint[] output = new uint[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (uint)Cast(array.GetValue(ii), BuiltInType.UInt32);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.UInt32:
                    return (uint)value;
                case BuiltInType.Boolean:
                    return Convert.ToUInt32((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToUInt32((sbyte)value);
                case BuiltInType.Byte:
                    return Convert.ToUInt32((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToUInt32((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToUInt32((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToUInt32((int)value);
                case BuiltInType.Int64:
                    return Convert.ToUInt32((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToUInt32((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToUInt32((float)value);
                case BuiltInType.Double:
                    return Convert.ToUInt32((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToUInt32((string)value);
                case BuiltInType.StatusCode:
                    return Convert.ToUInt32(((StatusCode)value).Code);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a Int64
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToInt64(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                long[] output = new long[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (long)Cast(array.GetValue(ii), BuiltInType.Int64);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Int64:
                    return (long)value;
                case BuiltInType.Boolean:
                    return Convert.ToInt64((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToInt64((sbyte)value);
                case BuiltInType.Byte:
                    return Convert.ToInt64((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToInt64((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToInt64((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToInt64((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToInt64((uint)value);
                case BuiltInType.UInt64:
                    return Convert.ToInt64((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToInt64((float)value);
                case BuiltInType.Double:
                    return Convert.ToInt64((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToInt64((string)value);
                case BuiltInType.StatusCode:
                    return Convert.ToInt64(((StatusCode)value).Code);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a UInt64
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToUInt64(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                ulong[] output = new ulong[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (ulong)Cast(array.GetValue(ii), BuiltInType.UInt64);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.UInt64:
                    return (ulong)value;
                case BuiltInType.Boolean:
                    return Convert.ToUInt64((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToUInt64((sbyte)value);
                case BuiltInType.Byte:
                    return Convert.ToUInt64((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToUInt64((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToUInt64((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToUInt64((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToUInt64((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToUInt64((long)value);
                case BuiltInType.Float:
                    return Convert.ToUInt64((float)value);
                case BuiltInType.Double:
                    return Convert.ToUInt64((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToUInt64((string)value);
                case BuiltInType.StatusCode:
                    return Convert.ToUInt64(((StatusCode)value).Code);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a Float
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToFloat(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                float[] output = new float[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (float)Cast(array.GetValue(ii), BuiltInType.Float);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Float:
                    return (float)value;
                case BuiltInType.Boolean:
                    return Convert.ToSingle((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToSingle((sbyte)value);
                case BuiltInType.Byte:
                    return Convert.ToSingle((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToSingle((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToSingle((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToSingle((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToSingle((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToSingle((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToSingle((ulong)value);
                case BuiltInType.Double:
                    return Convert.ToSingle((double)value);
                case BuiltInType.String:
                    return XmlConvert.ToSingle((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a Double
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToDouble(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                double[] output = new double[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (double)Cast(array.GetValue(ii), BuiltInType.Double);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Double:
                    return (double)value;
                case BuiltInType.Boolean:
                    return Convert.ToDouble((bool)value);
                case BuiltInType.SByte:
                    return Convert.ToDouble((sbyte)value);
                case BuiltInType.Byte:
                    return Convert.ToDouble((byte)value);
                case BuiltInType.Int16:
                    return Convert.ToDouble((short)value);
                case BuiltInType.UInt16:
                    return Convert.ToDouble((ushort)value);
                case BuiltInType.Int32:
                    return Convert.ToDouble((int)value);
                case BuiltInType.UInt32:
                    return Convert.ToDouble((uint)value);
                case BuiltInType.Int64:
                    return Convert.ToDouble((long)value);
                case BuiltInType.UInt64:
                    return Convert.ToDouble((ulong)value);
                case BuiltInType.Float:
                    return Convert.ToDouble((float)value);
                case BuiltInType.String:
                    return XmlConvert.ToDouble((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a String
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToString(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                string[] output = new string[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (string)Cast(array.GetValue(ii), BuiltInType.String);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.String:
                    return (string)value;
                case BuiltInType.Boolean:
                    return XmlConvert.ToString((bool)value);
                case BuiltInType.SByte:
                    return XmlConvert.ToString((sbyte)value);
                case BuiltInType.Byte:
                    return XmlConvert.ToString((byte)value);
                case BuiltInType.Int16:
                    return XmlConvert.ToString((short)value);
                case BuiltInType.UInt16:
                    return XmlConvert.ToString((ushort)value);
                case BuiltInType.Int32:
                    return XmlConvert.ToString((int)value);
                case BuiltInType.UInt32:
                    return XmlConvert.ToString((uint)value);
                case BuiltInType.Int64:
                    return XmlConvert.ToString((long)value);
                case BuiltInType.UInt64:
                    return XmlConvert.ToString((ulong)value);
                case BuiltInType.Float:
                    return XmlConvert.ToString((float)value);
                case BuiltInType.Double:
                    return XmlConvert.ToString((double)value);
                case BuiltInType.DateTime:
                    return XmlConvert.ToString(
                        (DateTime)value,
                        XmlDateTimeSerializationMode.Unspecified);
                case BuiltInType.Guid:
                    return ((Guid)value).ToString();
                case BuiltInType.NodeId:
                    return ((NodeId)value).ToString();
                case BuiltInType.ExpandedNodeId:
                    return ((ExpandedNodeId)value).ToString();
                case BuiltInType.LocalizedText:
                    return ((LocalizedText)value).Text;
                case BuiltInType.QualifiedName:
                    return ((QualifiedName)value).ToString();
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return DBNull.Value;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a DateTime
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToDateTime(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                var output = new DateTime[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (DateTime)Cast(array.GetValue(ii), BuiltInType.DateTime);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.DateTime:
                    return (DateTime)value;
                case BuiltInType.String:
                    return XmlConvert.ToDateTimeOffset((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a Guid
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToGuid(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                var output = new Guid[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (Guid)Cast(array.GetValue(ii), BuiltInType.Guid);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Guid:
                    return (Guid)value;
                case BuiltInType.String:
                    return new Guid((string)value);
                case BuiltInType.ByteString:
                    return new Guid((byte[])value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a ByteString
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToByteString(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                byte[][] output = new byte[array.Length][];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (byte[])Cast(array.GetValue(ii), BuiltInType.ByteString);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.ByteString:
                    return (byte[])value;
                case BuiltInType.Guid:
                    return ((Guid)value).ToByteArray();
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a NodeId
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToNodeId(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                var output = new NodeId[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (NodeId)Cast(array.GetValue(ii), BuiltInType.NodeId);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.NodeId:
                    return value is NodeId n ? n : default;
                case BuiltInType.ExpandedNodeId:
                    return (NodeId)(ExpandedNodeId)value;
                case BuiltInType.String:
                    return NodeId.Parse((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a ExpandedNodeId
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToExpandedNodeId(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                var output = new ExpandedNodeId[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (ExpandedNodeId)Cast(
                        array.GetValue(ii),
                        BuiltInType.ExpandedNodeId);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.ExpandedNodeId:
                    return (ExpandedNodeId)value;
                case BuiltInType.NodeId:
                    return (ExpandedNodeId)(NodeId)value;
                case BuiltInType.String:
                    return ExpandedNodeId.Parse((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a StatusCode
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToStatusCode(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                var output = new StatusCode[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (StatusCode)Cast(array.GetValue(ii), BuiltInType.StatusCode);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.StatusCode:
                    return (StatusCode)value;
                case BuiltInType.UInt16:
                    uint code = Convert.ToUInt32((ushort)value);
                    code <<= 16;
                    return (StatusCode)code;
                case BuiltInType.Int32:
                    return (StatusCode)Convert.ToUInt32((int)value);
                case BuiltInType.UInt32:
                    return (StatusCode)(uint)value;
                case BuiltInType.Int64:
                    return (StatusCode)Convert.ToUInt32((long)value);
                case BuiltInType.UInt64:
                    return (StatusCode)Convert.ToUInt32((ulong)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a QualifiedName
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToQualifiedName(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                var output = new QualifiedName[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (QualifiedName)Cast(array.GetValue(ii), BuiltInType.QualifiedName);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.QualifiedName:
                    return (QualifiedName)value;
                case BuiltInType.String:
                    return QualifiedName.Parse((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Converts a value to a LocalizedText
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object ToLocalizedText(object value, BuiltInType sourceType)
        {
            // check for array conversions.

            if (value is Array array)
            {
                var output = new LocalizedText[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (LocalizedText)Cast(array.GetValue(ii), BuiltInType.LocalizedText);
                }

                return output;
            }

            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.LocalizedText:
                    return (LocalizedText)value;
                case BuiltInType.String:
                    return new LocalizedText((string)value);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unknown built in type {sourceType} encountered.");
            }
        }

        /// <summary>
        /// Casts a value to the specified m_target type.
        /// </summary>
        private object Cast(object source, BuiltInType targetType)
        {
            BuiltInType sourceType = GetBuiltInType(source);

            if (sourceType == BuiltInType.Null)
            {
                return null;
            }

            return Cast(source, sourceType, targetType);
        }

        /// <summary>
        /// Casts a value to the specified m_target type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private object Cast(object source, BuiltInType sourceType, BuiltInType targetType)
        {
            // null always casts to null.
            if (source == null)
            {
                return null;
            }

            // extract the value from a Variant if specified.
            if (source is Variant variant)
            {
                return Cast(variant.Value, targetType);
            }

            // call the appropriate function if a conversion is supported for the m_target type.
            try
            {
                switch (targetType)
                {
                    case BuiltInType.Boolean:
                        return ToBoolean(source, sourceType);
                    case BuiltInType.SByte:
                        return ToSByte(source, sourceType);
                    case BuiltInType.Byte:
                        return ToByte(source, sourceType);
                    case BuiltInType.Int16:
                        return ToInt16(source, sourceType);
                    case BuiltInType.UInt16:
                        return ToUInt16(source, sourceType);
                    case BuiltInType.Int32:
                        return ToInt32(source, sourceType);
                    case BuiltInType.UInt32:
                        return ToUInt32(source, sourceType);
                    case BuiltInType.Int64:
                        return ToInt64(source, sourceType);
                    case BuiltInType.UInt64:
                        return ToUInt64(source, sourceType);
                    case BuiltInType.Float:
                        return ToFloat(source, sourceType);
                    case BuiltInType.Double:
                        return ToDouble(source, sourceType);
                    case BuiltInType.String:
                        return ToString(source, sourceType);
                    case BuiltInType.DateTime:
                        return ToDateTime(source, sourceType);
                    case BuiltInType.Guid:
                        return ToGuid(source, sourceType);
                    case BuiltInType.ByteString:
                        return ToByteString(source, sourceType);
                    case BuiltInType.NodeId:
                        return ToNodeId(source, sourceType);
                    case BuiltInType.ExpandedNodeId:
                        return ToExpandedNodeId(source, sourceType);
                    case BuiltInType.StatusCode:
                        return ToStatusCode(source, sourceType);
                    case BuiltInType.QualifiedName:
                        return ToQualifiedName(source, sourceType);
                    case BuiltInType.LocalizedText:
                        return ToLocalizedText(source, sourceType);
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        // conversion not supported.
                        return null;
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unknown built in type {sourceType} encountered.");
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "Error converting a {SourceType} (Value={Value}) to {TargetType}.",
                    sourceType,
                    source,
                    targetType);

                return null;
            }
        }

        /// <summary>
        /// And FilterOperator
        /// </summary>
        private bool? And(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            bool? lhs = GetValue(operands[0]) as bool?;

            // no need for further processing if first operand is false.
            if (lhs == false)
            {
                return false;
            }

            bool? rhs = GetValue(operands[1]) as bool?;

            if (lhs == null)
            {
                if (rhs is null or true)
                {
                    return null;
                }

                return false;
            }

            if (rhs == null)
            {
                if (lhs is true)
                {
                    return null;
                }

                return false;
            }

            return lhs.Value && rhs.Value;
        }

        /// <summary>
        /// Or FilterOperator
        /// </summary>
        private bool? Or(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            bool? lhs = GetValue(operands[0]) as bool?;

            // no need for further processing if first operand is true.
            if (lhs == true)
            {
                return true;
            }

            bool? rhs = GetValue(operands[1]) as bool?;

            if (lhs == null)
            {
                if (rhs is null or false)
                {
                    return null;
                }

                return true;
            }

            if (rhs == null)
            {
                if (lhs is false)
                {
                    return null;
                }

                return true;
            }

            return lhs.Value || rhs.Value;
        }

        /// <summary>
        /// Not FilterOperator
        /// </summary>
        private bool? Not(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            bool? rhs = GetValue(operands[0]) as bool?;

            if (rhs == null)
            {
                return null;
            }

            return !rhs.Value;
        }

        /// <summary>
        /// Equals FilterOperator
        /// </summary>
        private bool Equals(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(operands[0]);
            object rhs = GetValue(operands[1]);

            DoImplicitConversion(ref lhs, ref rhs);

            return IsEqual(lhs, rhs);
        }

        /// <summary>
        /// GreaterThan FilterOperator
        /// </summary>
        private bool? GreaterThan(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(operands[0]);
            object rhs = GetValue(operands[1]);

            DoImplicitConversion(ref lhs, ref rhs);

            if (lhs is IComparable l && rhs is IComparable r)
            {
                return l.CompareTo(r) > 0;
            }

            // return null if the types are not comparable.
            return null;
        }

        /// <summary>
        /// GreaterThanOrEqual FilterOperator
        /// </summary>
        private bool? GreaterThanOrEqual(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(operands[0]);
            object rhs = GetValue(operands[1]);

            DoImplicitConversion(ref lhs, ref rhs);

            if (lhs is IComparable l && rhs is IComparable r)
            {
                return l.CompareTo(r) >= 0;
            }

            // return null if the types are not comparable.
            return null;
        }

        /// <summary>
        /// LessThan FilterOperator
        /// </summary>
        private bool? LessThan(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(operands[0]);
            object rhs = GetValue(operands[1]);

            DoImplicitConversion(ref lhs, ref rhs);

            if (lhs is IComparable l && rhs is IComparable r)
            {
                return l.CompareTo(r) < 0;
            }

            // return null if the types are not comparable.
            return null;
        }

        /// <summary>
        /// LessThanOrEqual FilterOperator
        /// </summary>
        private bool? LessThanOrEqual(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(operands[0]);
            object rhs = GetValue(operands[1]);

            DoImplicitConversion(ref lhs, ref rhs);

            if (lhs is IComparable l && rhs is IComparable r)
            {
                return l.CompareTo(r) <= 0;
            }

            // return null if the types are not comparable.
            return null;
        }

        /// <summary>
        /// Between FilterOperator
        /// </summary>
        private bool? Between(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 3);

            object value = GetValue(operands[0]);

            object min = GetValue(operands[1]);
            object max = GetValue(operands[2]);

            // the min and max could be different data types so the implicit conversion must be done twice.
            object lhs = value;
            DoImplicitConversion(ref lhs, ref min);

            bool? result = null;

            if (lhs is IComparable l1 && min is IComparable m1)
            {
                // check if never in range no matter what happens with the upper bound.
                if (l1.CompareTo(m1) < 0)
                {
                    return false;
                }

                result = true;
            }

            lhs = value;
            DoImplicitConversion(ref lhs, ref max);

            if (lhs is IComparable l2 && max is IComparable m2)
            {
                // check if never in range no matter what happens with the lower bound.
                if (l2.CompareTo(m2) > 0)
                {
                    return false;
                }

                // can't determine if in range if lower bound could not be resolved.
                return result != null;
            }

            // return null if the types are not comparable.
            return null;
        }

        /// <summary>
        /// InList FilterOperator
        /// </summary>
        private bool? InList(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 0);

            object value = GetValue(operands[0]);

            // check for a match.
            for (int ii = 1; ii < operands.Length; ii++)
            {
                object lhs = value;
                object rhs = GetValue(operands[ii]);

                DoImplicitConversion(ref lhs, ref rhs);

                if (IsEqual(lhs, rhs))
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
        private bool Like(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object firstOperand = GetValue(operands[0]);
            string lhs;
            if (firstOperand is LocalizedText firstOperandLocalizedText)
            {
                lhs = firstOperandLocalizedText.Text;
            }
            else
            {
                lhs = firstOperand as string;
            }

            object secondOperand = GetValue(operands[1]);
            string rhs;
            if (secondOperand is LocalizedText secondOperandLocalizedText)
            {
                rhs = secondOperandLocalizedText.Text;
            }
            else
            {
                rhs = secondOperand as string;
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
        private bool IsNull(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            object rhs = GetValue(operands[0]);

            return rhs == null;
        }

        /// <summary>
        /// Cast FilterOperator
        /// </summary>
        private object Cast(
            ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            // get the value to cast.
            object value = GetValue(operands[0]);

            if (value == null)
            {
                return null;
            }

            // get the datatype to cast to.
            if (GetValue(operands[1]) is not NodeId datatype)
            {
                return null;
            }

            BuiltInType targetType = GetBuiltInType(datatype);

            // cast the value.
            return Cast(value, targetType);
        }

        /// <summary>
        /// OfType FilterOperator
        /// </summary>
        private bool OfType(ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            // get the desired type.
            if (GetValue(operands[0]) is not NodeId typeDefinitionId || m_target == null)
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
        private bool InView(ContentFilterElement element)
        {
            // views only supported in advanced filter targets.

            if (m_target is not IAdvancedFilterTarget advancedFilter)
            {
                return false;
            }

            FilterOperand[] operands = GetOperands(element, 1);

            // get the desired type.
            if (GetValue(operands[0]) is not NodeId viewId || m_target == null)
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
        private bool RelatedTo(ContentFilterElement element)
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
            if (GetValue(operands[0]) is not NodeId sourceTypeId)
            {
                return false;
            }

            // get the type of reference to follow.
            if (GetValue(operands[2]) is not NodeId referenceTypeId)
            {
                return false;
            }

            // get the number of hops
            int? hops = 1;

            object hopsValue = GetValue(operands[3]);

            if (hopsValue != null)
            {
                hops = Cast(hopsValue, BuiltInType.Int32) as int? ?? (int?)1;
            }

            // get whether to include type definition subtypes.
            bool? includeTypeDefinitionSubtypes = true;

            object includeValue = GetValue(operands[4]);

            if (includeValue != null)
            {
                includeTypeDefinitionSubtypes = Cast(includeValue, BuiltInType.Boolean) as bool? ??
                    (bool?)true;
            }

            // get whether to include reference type subtypes.
            bool? includeReferenceTypeSubtypes = true;

            includeValue = GetValue(operands[5]);

            if (includeValue != null)
            {
                includeReferenceTypeSubtypes = Cast(includeValue, BuiltInType.Boolean) as bool? ??
                    (bool?)true;
            }

            NodeId targetTypeId = null;

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

                    targetTypeId = GetValue(nestedType) is NodeId n ? n : default;

                    if (targetTypeId == null)
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
            targetTypeId = GetValue(operands[1]) is NodeId n2 ? n2 : default;

            if (targetTypeId == null)
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
        /// BitwiseAnd FilterOperator
        /// </summary>
        private object BitwiseAnd(ContentFilterElement element)
        {
            (object lhs, object rhs) = GetBitwiseOperands(element);
            if (lhs == null || rhs == null)
            {
                return null;
            }

            Type systemType = lhs.GetType();
            if (systemType == typeof(byte))
            {
                return (byte)lhs & (byte)rhs;
            }
            if (systemType == typeof(sbyte))
            {
                return (sbyte)lhs & (sbyte)rhs;
            }
            if (systemType == typeof(short))
            {
                return (short)lhs & (short)rhs;
            }
            if (systemType == typeof(ushort))
            {
                return (ushort)lhs & (ushort)rhs;
            }
            if (systemType == typeof(int))
            {
                return (int)lhs & (int)rhs;
            }
            if (systemType == typeof(uint))
            {
                return (uint)lhs & (uint)rhs;
            }
            if (systemType == typeof(long))
            {
                return (long)lhs & (long)rhs;
            }
            if (systemType == typeof(ulong))
            {
                return (ulong)lhs & (ulong)rhs;
            }
            return null;
        }

        /// <summary>
        /// BitwiseOr FilterOperator
        /// </summary>
        private object BitwiseOr(ContentFilterElement element)
        {
            (object lhs, object rhs) = GetBitwiseOperands(element);
            if (lhs == null || rhs == null)
            {
                return null;
            }

            Type systemType = lhs.GetType();
            if (systemType == typeof(byte))
            {
                return (byte)lhs | (byte)rhs;
            }
            if (systemType == typeof(sbyte))
            {
                return (sbyte)lhs | (sbyte)rhs;
            }
            if (systemType == typeof(short))
            {
                return (short)lhs | (short)rhs;
            }
            if (systemType == typeof(ushort))
            {
                return (ushort)lhs | (ushort)rhs;
            }
            if (systemType == typeof(int))
            {
                return (int)lhs | (int)rhs;
            }
            if (systemType == typeof(uint))
            {
                return (uint)lhs | (uint)rhs;
            }
            if (systemType == typeof(long))
            {
                return (long)lhs | (long)rhs;
            }
            if (systemType == typeof(ulong))
            {
                return (ulong)lhs | (ulong)rhs;
            }
            return null;
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
