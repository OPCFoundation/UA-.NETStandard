/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// This class contains functions used to evaluate a ContentFilter and report the
    /// results of the evaluation.
    /// </summary>
    public partial class ContentFilter
    {
        #region Public functions
        /// <summary>
        /// Evaluates the first element in the ContentFilter. If the first or any 
        /// subsequent element has dependent elements, the dependent elements are 
        /// evaluated before the root element (recursive descent). Elements which 
        /// are not linked (directly or indirectly) to the first element will not 
        /// be evaluated (they have no influence on the result). 
        /// </summary>
        /// <param name="context">The context to use when evaluating the filter.</param>
        /// <param name="target">The target to use when evaluating elements that reference the type model.</param>
        /// <returns>Returns true, false or null.</returns>
        public bool Evaluate(FilterContext context, IFilterTarget target)
        {
            // check if nothing to do.
            if (this.Elements.Count == 0)
            {
                return true;
            }

            bool? result = Evaluate(context, target, 0) as bool?;

            if (result == null)
            {
                return false;
            }

            return result.Value;
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Evaluates element at the specified index.
        /// </summary>
        private object Evaluate(FilterContext context, IFilterTarget target, int index)
        {
            // get the element to evaluate.
            ContentFilterElement element = Elements[index];

            switch (element.FilterOperator)
            {
                case FilterOperator.And: 
                {
                    return And(context, target, element);
                }
                    
                case FilterOperator.Or: 
                {
                    return Or(context, target, element);
                }

                case FilterOperator.Not: 
                {
                    return Not(context, target, element);
                }

                case FilterOperator.Equals: 
                {
                    return Equals(context, target, element);
                }

                case FilterOperator.GreaterThan: 
                {
                    return GreaterThan(context, target, element);
                }

                case FilterOperator.GreaterThanOrEqual: 
                {
                    return GreaterThanOrEqual(context, target, element);
                }

                case FilterOperator.LessThan: 
                {
                    return LessThan(context, target, element);
                }

                case FilterOperator.LessThanOrEqual: 
                {
                    return LessThanOrEqual(context, target, element);
                }

                case FilterOperator.Between: 
                {
                    return Between(context, target, element);
                }

                case FilterOperator.InList: 
                {
                    return InList(context, target, element);
                }

                case FilterOperator.Like: 
                {
                    return Like(context, target, element);
                }                    

                case FilterOperator.IsNull: 
                {
                    return IsNull(context, target, element);
                }             

                case FilterOperator.Cast: 
                {
                    return Cast(context, target, element);
                }

                case FilterOperator.OfType: 
                {
                    return OfType(context, target, element);
                }

                case FilterOperator.InView: 
                {
                    return InView(context, target, element);
                }

                case FilterOperator.RelatedTo: 
                {
                    return RelatedTo(context, target, element);
                }
            }
                        
            throw new ServiceResultException(StatusCodes.BadUnexpectedError, "FilterOperator is not recognized.");
        }

        /// <summary>
        /// Returns the operands for the element.
        /// </summary>
        private FilterOperand[] GetOperands(ContentFilterElement element, int expectedCount)
        {
            FilterOperand[] operands = new FilterOperand[element.FilterOperands.Count]; 

            int ii = 0;

            foreach (ExtensionObject extension in element.FilterOperands)
            {
                if (ExtensionObject.IsNull(extension))
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError, "FilterOperand is null.");
                }

                FilterOperand operand = extension.Body as FilterOperand;

                if (operand == null)
                {
                    throw new ServiceResultException(StatusCodes.BadUnexpectedError, "FilterOperand is not supported.");
                }
               
                operands[ii++] = operand;
            }

            if (expectedCount > 0 && expectedCount != operands.Length)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "ContentFilterElement does not have the correct number of operands.");
            }

            return operands;
        }

        /// <summary>
        /// Returns the value for the element.
        /// </summary>
        private object GetValue(FilterContext context, FilterOperand operand, IFilterTarget target)
        {
            // return the contained value for literal operands.
            LiteralOperand literal = operand as LiteralOperand;

            if (literal != null)
            {
                return literal.Value.Value;
            }

            // must query the filter target for simple attribute operands.
            SimpleAttributeOperand simpleAttribute = operand as SimpleAttributeOperand;

            if (simpleAttribute != null)
            {
                return target.GetAttributeValue(
                    context,
                    simpleAttribute.TypeDefinitionId,
                    simpleAttribute.BrowsePath,
                    simpleAttribute.AttributeId,
                    simpleAttribute.ParsedIndexRange);
            }

            // must query the filter target for attribute operands.
            AttributeOperand attribute = operand as AttributeOperand;

            if (attribute != null)
            {
                // AttributeOperands only supported in advanced filter targets.
                IAdvancedFilterTarget advancedTarget = target as IAdvancedFilterTarget;

                if (advancedTarget == null)
                {
                    return false;
                }

                return advancedTarget.GetRelatedAttributeValue(
                    context,
                    attribute.NodeId,
                    attribute.BrowsePath,
                    attribute.AttributeId,
                    attribute.ParsedIndexRange);
            }
            
            // recursively evaluate element operands.
            ElementOperand element = operand as ElementOperand;

            if (element != null)
            {
                return Evaluate(context, target, (int)element.Index);
            }
                    
            // oops - Validate() was not called.
            throw new ServiceResultException(StatusCodes.BadUnexpectedError, "FilterOperand is not supported.");
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

			if (systemType == typeof(bool))            { return BuiltInType.Boolean;         }
			if (systemType == typeof(sbyte))           { return BuiltInType.SByte;           }
			if (systemType == typeof(byte))            { return BuiltInType.Byte;            }
			if (systemType == typeof(short))           { return BuiltInType.Int16;           }
			if (systemType == typeof(ushort))          { return BuiltInType.UInt16;          }
			if (systemType == typeof(int))             { return BuiltInType.Int32;           }
			if (systemType == typeof(uint))            { return BuiltInType.UInt32;          }
			if (systemType == typeof(long))            { return BuiltInType.Int64;           }
			if (systemType == typeof(ulong))           { return BuiltInType.UInt64;          }
			if (systemType == typeof(float))           { return BuiltInType.Float;           }
			if (systemType == typeof(double))          { return BuiltInType.Double;          }
			if (systemType == typeof(string))          { return BuiltInType.String;          }
			if (systemType == typeof(DateTime))        { return BuiltInType.DateTime;        }
			if (systemType == typeof(Guid))            { return BuiltInType.Guid;            }
			if (systemType == typeof(Uuid))            { return BuiltInType.Guid;            }
			if (systemType == typeof(byte[]))          { return BuiltInType.ByteString;      }
			if (systemType == typeof(XmlElement))      { return BuiltInType.XmlElement;      }
			if (systemType == typeof(NodeId))          { return BuiltInType.NodeId;          }
			if (systemType == typeof(ExpandedNodeId))  { return BuiltInType.ExpandedNodeId;  }
			if (systemType == typeof(StatusCode))      { return BuiltInType.StatusCode;      } 
		    if (systemType == typeof(DiagnosticInfo))  { return BuiltInType.DiagnosticInfo;  }
		    if (systemType == typeof(QualifiedName))   { return BuiltInType.QualifiedName;   }
		    if (systemType == typeof(LocalizedText))   { return BuiltInType.LocalizedText;   }
			if (systemType == typeof(ExtensionObject)) { return BuiltInType.ExtensionObject; }
			if (systemType == typeof(DataValue))       { return BuiltInType.DataValue;       }
			if (systemType == typeof(Variant))         { return BuiltInType.Variant;         }
			if (systemType == typeof(object))          { return BuiltInType.Variant;         }

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
            if (datatypeId == null || datatypeId.NamespaceIndex != 0 || datatypeId.IdType != IdType.Numeric)
            {
                return BuiltInType.Null;
            }

            return (BuiltInType)Enum.ToObject(typeof(BuiltInType), datatypeId.Identifier);
        }

        /// <summary>
        /// Returns the data type precedence for the value.
        /// </summary>
        private static int GetDataTypePrecedence(BuiltInType type)
        {           
            switch (type)
            {
                case BuiltInType.Double:         { return 18; }
                case BuiltInType.Float:          { return 17; }
                case BuiltInType.Int64:          { return 16; }
                case BuiltInType.UInt64:         { return 15; }
                case BuiltInType.Int32:          { return 14; }
                case BuiltInType.UInt32:         { return 13; }
                case BuiltInType.StatusCode:     { return 12; }
                case BuiltInType.Int16:          { return 11; }
                case BuiltInType.UInt16:         { return 10; }
                case BuiltInType.SByte:          { return 9;  }
                case BuiltInType.Byte:           { return 8;  }
                case BuiltInType.Boolean:        { return 7;  }
                case BuiltInType.Guid:           { return 6;  }
                case BuiltInType.String:         { return 5;  }
                case BuiltInType.ExpandedNodeId: { return 4;  }
                case BuiltInType.NodeId:         { return 3;  }
                case BuiltInType.LocalizedText:  { return 2;  }
                case BuiltInType.QualifiedName:  { return 1;  }
            }

            return 0;
        }
                              
        /// <summary>
        /// Implicitly converts the values according to their data type precedence.
        /// </summary>
        private static void DoImplicitConversion(ref object value1, ref object value2)
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

            if (value1 is DBNull || value2 is DBNull)
            {
                return value1 is DBNull && value2 is DBNull;
            }

            if (value1.GetType() != value2.GetType())
            {
                return false;
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
            expression = Regex.Replace(expression, "([\\^\\$\\.\\|\\?\\*\\+\\(\\)])", "\\$1", RegexOptions.Compiled);
            
            // 2) Replace all OPC UA wildcards with their regular expression equivalents
            // replace all '%' with ".+", except "\%"
            expression = Regex.Replace(expression, "(?<!\\\\)%", ".*", RegexOptions.Compiled);
            
            // replace all '_' with '.', except "\_"
            expression = Regex.Replace(expression, "(?<!\\\\)_", ".", RegexOptions.Compiled);
            
            // replace all "[!" with "[^", except "\[!"
            expression = Regex.Replace(expression, "(?<!\\\\)(\\[!)", "[^", RegexOptions.Compiled);
            
            return Regex.IsMatch(target, expression);
        }
        #endregion

        #region Casting
        /// <summary>
        /// Converts a value to a Boolean
        /// </summary>
        private static object ToBoolean(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (bool)value; 
                }
                    
                case BuiltInType.SByte:   return Convert.ToBoolean((byte)value);
                case BuiltInType.Byte:    return Convert.ToBoolean((byte)value);
                case BuiltInType.Int16:   return Convert.ToBoolean((short)value);
                case BuiltInType.UInt16:  return Convert.ToBoolean((ushort)value);
                case BuiltInType.Int32:   return Convert.ToBoolean((int)value);
                case BuiltInType.UInt32:  return Convert.ToBoolean((uint)value);
                case BuiltInType.Int64:   return Convert.ToBoolean((long)value);
                case BuiltInType.UInt64:  return Convert.ToBoolean((ulong)value);
                case BuiltInType.Float:   return Convert.ToBoolean((float)value);
                case BuiltInType.Double:  return Convert.ToBoolean((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToBoolean((string)value); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }

        /// <summary>
        /// Converts a value to a SByte
        /// </summary>
        private static object ToSByte(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (sbyte)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToSByte((bool)value);
                case BuiltInType.Byte:    return Convert.ToSByte((byte)value);
                case BuiltInType.Int16:   return Convert.ToSByte((short)value);
                case BuiltInType.UInt16:  return Convert.ToSByte((ushort)value);
                case BuiltInType.Int32:   return Convert.ToSByte((int)value);
                case BuiltInType.UInt32:  return Convert.ToSByte((uint)value);
                case BuiltInType.Int64:   return Convert.ToSByte((long)value);
                case BuiltInType.UInt64:  return Convert.ToSByte((ulong)value);
                case BuiltInType.Float:   return Convert.ToSByte((float)value);
                case BuiltInType.Double:  return Convert.ToSByte((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToSByte((string)value); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }

        /// <summary>
        /// Converts a value to a Byte
        /// </summary>
        private static object ToByte(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                throw new NotImplementedException("Arrays of Byte not supported. Use ByteString instead.");
            }
            
            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.Byte:
                {
                    return (byte)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToByte((bool)value);
                case BuiltInType.SByte:   return Convert.ToByte((sbyte)value);
                case BuiltInType.Int16:   return Convert.ToByte((short)value);
                case BuiltInType.UInt16:  return Convert.ToByte((ushort)value);
                case BuiltInType.Int32:   return Convert.ToByte((int)value);
                case BuiltInType.UInt32:  return Convert.ToByte((uint)value);
                case BuiltInType.Int64:   return Convert.ToByte((long)value);
                case BuiltInType.UInt64:  return Convert.ToByte((ulong)value);
                case BuiltInType.Float:   return Convert.ToByte((float)value);
                case BuiltInType.Double:  return Convert.ToByte((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToByte((string)value); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }
        
        /// <summary>
        /// Converts a value to a Int16
        /// </summary>
        private static object ToInt16(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (short)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToInt16((bool)value);
                case BuiltInType.SByte:   return Convert.ToInt16((sbyte)value);
                case BuiltInType.Byte:    return Convert.ToInt16((byte)value);
                case BuiltInType.UInt16:  return Convert.ToInt16((ushort)value);
                case BuiltInType.Int32:   return Convert.ToInt16((int)value);
                case BuiltInType.UInt32:  return Convert.ToInt16((uint)value);
                case BuiltInType.Int64:   return Convert.ToInt16((long)value);
                case BuiltInType.UInt64:  return Convert.ToInt16((ulong)value);
                case BuiltInType.Float:   return Convert.ToInt16((float)value);
                case BuiltInType.Double:  return Convert.ToInt16((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToInt16((string)value); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }

        /// <summary>
        /// Converts a value to a UInt16
        /// </summary>
        private static object ToUInt16(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (ushort)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToUInt16((bool)value);
                case BuiltInType.SByte:   return Convert.ToUInt16((sbyte)value);
                case BuiltInType.Byte:    return Convert.ToUInt16((byte)value);
                case BuiltInType.Int16:   return Convert.ToUInt16((short)value);
                case BuiltInType.Int32:   return Convert.ToUInt16((int)value);
                case BuiltInType.UInt32:  return Convert.ToUInt16((uint)value);
                case BuiltInType.Int64:   return Convert.ToUInt16((long)value);
                case BuiltInType.UInt64:  return Convert.ToUInt16((ulong)value);
                case BuiltInType.Float:   return Convert.ToUInt16((float)value);
                case BuiltInType.Double:  return Convert.ToUInt16((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToUInt16((string)value); 
                }

                case BuiltInType.StatusCode:
                {
                    StatusCode code = (StatusCode)value;
                    return  (ushort)(code.CodeBits>>16); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }
        
        /// <summary>
        /// Converts a value to a Int32
        /// </summary>
        private static object ToInt32(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (int)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToInt32((bool)value);
                case BuiltInType.SByte:   return Convert.ToInt32((sbyte)value);
                case BuiltInType.Byte:    return Convert.ToInt32((byte)value);
                case BuiltInType.Int16:   return Convert.ToInt32((short)value);
                case BuiltInType.UInt16:  return Convert.ToInt32((ushort)value);
                case BuiltInType.UInt32:  return Convert.ToInt32((uint)value);
                case BuiltInType.Int64:   return Convert.ToInt32((long)value);
                case BuiltInType.UInt64:  return Convert.ToInt32((ulong)value);
                case BuiltInType.Float:   return Convert.ToInt32((float)value);
                case BuiltInType.Double:  return Convert.ToInt32((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToInt32((string)value); 
                }

                case BuiltInType.StatusCode:
                {
                    return Convert.ToInt32(((StatusCode)value).Code); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }

        /// <summary>
        /// Converts a value to a UInt32
        /// </summary>
        private static object ToUInt32(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (uint)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToUInt32((bool)value);
                case BuiltInType.SByte:   return Convert.ToUInt32((sbyte)value);
                case BuiltInType.Byte:    return Convert.ToUInt32((byte)value);
                case BuiltInType.Int16:   return Convert.ToUInt32((short)value);
                case BuiltInType.UInt16:  return Convert.ToUInt32((ushort)value);
                case BuiltInType.Int32:   return Convert.ToUInt32((int)value);
                case BuiltInType.Int64:   return Convert.ToUInt32((long)value);
                case BuiltInType.UInt64:  return Convert.ToUInt32((ulong)value);
                case BuiltInType.Float:   return Convert.ToUInt32((float)value);
                case BuiltInType.Double:  return Convert.ToUInt32((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToUInt32((string)value); 
                }

                case BuiltInType.StatusCode:
                {
                    return Convert.ToUInt32(((StatusCode)value).Code); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }
        
        /// <summary>
        /// Converts a value to a Int64
        /// </summary>
        private static object ToInt64(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (long)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToInt64((bool)value);
                case BuiltInType.SByte:   return Convert.ToInt64((sbyte)value);
                case BuiltInType.Byte:    return Convert.ToInt64((byte)value);
                case BuiltInType.Int16:   return Convert.ToInt64((short)value);
                case BuiltInType.UInt16:  return Convert.ToInt64((ushort)value);
                case BuiltInType.Int32:   return Convert.ToInt64((int)value);
                case BuiltInType.UInt32:  return Convert.ToInt64((uint)value);
                case BuiltInType.UInt64:  return Convert.ToInt64((ulong)value);
                case BuiltInType.Float:   return Convert.ToInt64((float)value);
                case BuiltInType.Double:  return Convert.ToInt64((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToInt64((string)value); 
                }

                case BuiltInType.StatusCode:
                {
                    return Convert.ToInt64(((StatusCode)value).Code); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }        
                 
        /// <summary>
        /// Converts a value to a UInt64
        /// </summary>
        private static object ToUInt64(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (ulong)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToUInt64((bool)value);
                case BuiltInType.SByte:   return Convert.ToUInt64((sbyte)value);
                case BuiltInType.Byte:    return Convert.ToUInt64((byte)value);
                case BuiltInType.Int16:   return Convert.ToUInt64((short)value);
                case BuiltInType.UInt16:  return Convert.ToUInt64((ushort)value);
                case BuiltInType.Int32:   return Convert.ToUInt64((int)value);
                case BuiltInType.UInt32:  return Convert.ToUInt64((uint)value);
                case BuiltInType.Int64:   return Convert.ToUInt64((long)value);
                case BuiltInType.Float:   return Convert.ToUInt64((float)value);
                case BuiltInType.Double:  return Convert.ToUInt64((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToUInt64((string)value); 
                }

                case BuiltInType.StatusCode:
                {
                    return Convert.ToUInt64(((StatusCode)value).Code); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }        

        /// <summary>
        /// Converts a value to a Float
        /// </summary>
        private static object ToFloat(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (float)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToSingle((bool)value);
                case BuiltInType.SByte:   return Convert.ToSingle((sbyte)value);
                case BuiltInType.Byte:    return Convert.ToSingle((byte)value);
                case BuiltInType.Int16:   return Convert.ToSingle((short)value);
                case BuiltInType.UInt16:  return Convert.ToSingle((ushort)value);
                case BuiltInType.Int32:   return Convert.ToSingle((int)value);
                case BuiltInType.UInt32:  return Convert.ToSingle((uint)value);
                case BuiltInType.Int64:   return Convert.ToSingle((long)value);
                case BuiltInType.UInt64:  return Convert.ToSingle((ulong)value);
                case BuiltInType.Double:  return Convert.ToSingle((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToSingle((string)value); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }        
                
        /// <summary>
        /// Converts a value to a Double
        /// </summary>
        private static object ToDouble(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (double)value; 
                }
                    
                case BuiltInType.Boolean: return Convert.ToDouble((bool)value);
                case BuiltInType.SByte:   return Convert.ToDouble((sbyte)value);
                case BuiltInType.Byte:    return Convert.ToDouble((byte)value);
                case BuiltInType.Int16:   return Convert.ToDouble((short)value);
                case BuiltInType.UInt16:  return Convert.ToDouble((ushort)value);
                case BuiltInType.Int32:   return Convert.ToDouble((int)value);
                case BuiltInType.UInt32:  return Convert.ToDouble((uint)value);
                case BuiltInType.Int64:   return Convert.ToDouble((long)value);
                case BuiltInType.UInt64:  return Convert.ToDouble((ulong)value);
                case BuiltInType.Float:   return Convert.ToDouble((float)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToDouble((string)value); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }        

        /// <summary>
        /// Converts a value to a String
        /// </summary>
        private static object ToString(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                String[] output = new String[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (String)Cast(array.GetValue(ii), BuiltInType.String);
                }

                return output;
            }
            
            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.String:
                {
                    return (string)value;
                }

                case BuiltInType.Boolean:
                {
                    return XmlConvert.ToString((bool)value);
                }

                case BuiltInType.SByte:
                {
                    return XmlConvert.ToString((sbyte)value); 
                }

                case BuiltInType.Byte:
                {
                    return XmlConvert.ToString((byte)value); 
                }

                case BuiltInType.Int16:
                {
                    return XmlConvert.ToString((short)value); 
                }

                case BuiltInType.UInt16:
                {
                    return XmlConvert.ToString((ushort)value); 
                }

                case BuiltInType.Int32:
                {
                    return XmlConvert.ToString((int)value); 
                }

                case BuiltInType.UInt32:
                {
                    return XmlConvert.ToString((uint)value); 
                }

                case BuiltInType.Int64:
                {
                    return XmlConvert.ToString((long)value); 
                }

                case BuiltInType.UInt64:
                {
                    return XmlConvert.ToString((ulong)value); 
                }

                case BuiltInType.Float:
                {
                    return XmlConvert.ToString((float)value); 
                }

                case BuiltInType.Double:
                {
                    return XmlConvert.ToString((double)value); 
                }

                case BuiltInType.DateTime:
                {
                    return XmlConvert.ToString((DateTime)value, XmlDateTimeSerializationMode.Unspecified); 
                }

                case BuiltInType.Guid:
                {
                    return ((Guid)value).ToString(); 
                }

                case BuiltInType.NodeId:
                {
                    return ((NodeId)value).ToString(); 
                }

                case BuiltInType.ExpandedNodeId:
                {
                    return ((ExpandedNodeId)value).ToString(); 
                }

                case BuiltInType.LocalizedText:
                {
                    return ((LocalizedText)value).Text; 
                }

                case BuiltInType.QualifiedName:
                {
                    return ((QualifiedName)value).ToString(); 
                }
            }
            
            // conversion not supported.
            return DBNull.Value;
        }

        /// <summary>
        /// Converts a value to a DateTime
        /// </summary>
        private static object ToDateTime(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                DateTime[] output = new DateTime[array.Length];

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
                {
                    return (DateTime)value; 
                }

                case BuiltInType.String:
                {
                    return XmlConvert.ToDateTimeOffset((string) value); 
                }
            }
            
            // conversion not supported.
            return null;
        }

        /// <summary>
        /// Converts a value to a Guid
        /// </summary>
        private static object ToGuid(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                Guid[] output = new Guid[array.Length];

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
                {
                    return (Guid)value; 
                }

                case BuiltInType.String:
                {
                    return new Guid((string)value); 
                }

                case BuiltInType.ByteString:
                {
                    return new Guid((byte[])value); 
                }
            }
            
            // conversion not supported.
            return null;
        }

        /// <summary>
        /// Converts a value to a ByteString
        /// </summary>
        private static object ToByteString(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
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
                {
                    return (byte[])value; 
                }

                case BuiltInType.Guid:
                {
                    return ((Guid)value).ToByteArray(); 
                }
            }
            
            // conversion not supported.
            return null;
        }

        /// <summary>
        /// Converts a value to a NodeId
        /// </summary>
        private static object ToNodeId(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                NodeId[] output = new NodeId[array.Length];

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
                {
                    return (NodeId)value; 
                }

                case BuiltInType.ExpandedNodeId:
                {
                    return (NodeId)(ExpandedNodeId)value; 
                }

                case BuiltInType.String:
                {
                    return NodeId.Parse((string)value);
                }
            }
            
            // conversion not supported.
            return null;
        }

        /// <summary>
        /// Converts a value to a ExpandedNodeId
        /// </summary>
        private static object ToExpandedNodeId(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                ExpandedNodeId[] output = new ExpandedNodeId[array.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    output[ii] = (ExpandedNodeId)Cast(array.GetValue(ii), BuiltInType.ExpandedNodeId);
                }

                return output;
            }
            
            // handle for supported conversions.
            switch (sourceType)
            {
                case BuiltInType.ExpandedNodeId:
                {
                    return (ExpandedNodeId)value; 
                }

                case BuiltInType.NodeId:
                {
                    return (ExpandedNodeId)(NodeId)value; 
                }

                case BuiltInType.String:
                {
                    return ExpandedNodeId.Parse((string)value);
                }
            }
            
            // conversion not supported.
            return null;
        }

        /// <summary>
        /// Converts a value to a StatusCode
        /// </summary>
        private static object ToStatusCode(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                StatusCode[] output = new StatusCode[array.Length];

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
                {
                    return (StatusCode)value; 
                }

                case BuiltInType.UInt16:
                {
                    uint code = Convert.ToUInt32((ushort)value);
                    code <<= 16;
                    return (StatusCode)code; 
                }

                case BuiltInType.Int32:
                {
                    return (StatusCode)Convert.ToUInt32((int)value); 
                }               

                case BuiltInType.UInt32:
                {
                    return (StatusCode)(uint)value; 
                }                     
                    
                case BuiltInType.Int64:
                {
                    return (StatusCode)Convert.ToUInt32((long)value); 
                }

                case BuiltInType.UInt64:
                {
                    return (StatusCode)Convert.ToUInt32((ulong)value); 
                }
            }
            
            // conversion not supported.
            return null;
        }
        
        /// <summary>
        /// Converts a value to a QualifiedName
        /// </summary>
        private static object ToQualifiedName(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                QualifiedName[] output = new QualifiedName[array.Length];

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
                {
                    return (QualifiedName)value; 
                }

                case BuiltInType.String:
                {
                    return QualifiedName.Parse((string)value);
                }
            }
            
            // conversion not supported.
            return null;
        }

        /// <summary>
        /// Converts a value to a LocalizedText
        /// </summary>
        private static object ToLocalizedText(object value, BuiltInType sourceType)
        {            
            // check for array conversions.
            Array array = value as Array;

            if (array != null)
            {
                LocalizedText[] output = new LocalizedText[array.Length];

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
                {
                    return (LocalizedText)value; 
                }

                case BuiltInType.String:
                {
                    return new LocalizedText((string)value);
                }
            }

            // conversion not supported.
            return null;
        }
        
        /// <summary>
        /// Casts a value to the specified target type.
        /// </summary>
        private static object Cast(object source, BuiltInType targetType)
        {
            BuiltInType sourceType = GetBuiltInType(source);

            if (sourceType == BuiltInType.Null)
            {
                return null;
            }

            return Cast(source, sourceType, targetType); 
        }

        /// <summary>
        /// Casts a value to the specified target type.
        /// </summary>
        private static object Cast(object source, BuiltInType sourceType, BuiltInType targetType)
        {
            // null always casts to null.
            if (source == null)
            {
                return null;
            }

            // extract the value from a Variant if specified.
            if (source is Variant)
            {
                return Cast(((Variant)source).Value, targetType);
            }

            // call the appropriate function if a conversion is supported for the target type.
            try
            {
                switch (targetType)
                {
                    case BuiltInType.Boolean:        return ToBoolean(source, sourceType);                
                    case BuiltInType.SByte:          return ToSByte(source, sourceType);
                    case BuiltInType.Byte:           return ToByte(source, sourceType);
                    case BuiltInType.Int16:          return ToInt16(source, sourceType);
                    case BuiltInType.UInt16:         return ToUInt16(source, sourceType);
                    case BuiltInType.Int32:          return ToInt32(source, sourceType);
                    case BuiltInType.UInt32:         return ToUInt32(source, sourceType);
                    case BuiltInType.Int64:          return ToInt64(source, sourceType);
                    case BuiltInType.UInt64:         return ToUInt64(source, sourceType);
                    case BuiltInType.Float:          return ToFloat(source, sourceType);
                    case BuiltInType.Double:         return ToDouble(source, sourceType);
                    case BuiltInType.String:         return ToString(source, sourceType);
                    case BuiltInType.DateTime:       return ToDateTime(source, sourceType);
                    case BuiltInType.Guid:           return ToGuid(source, sourceType);
                    case BuiltInType.ByteString:     return ToByteString(source, sourceType);
                    case BuiltInType.NodeId:         return ToNodeId(source, sourceType);
                    case BuiltInType.ExpandedNodeId: return ToExpandedNodeId(source, sourceType);
                    case BuiltInType.StatusCode:     return ToStatusCode(source, sourceType);
                    case BuiltInType.QualifiedName:  return ToQualifiedName(source, sourceType);
                    case BuiltInType.LocalizedText:  return ToLocalizedText(source, sourceType);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Error converting a {1} (Value={0}) to {2}.", source, sourceType, targetType);
            }

            // conversion not supported.
            return null;
        }
        #endregion

        #region FilterOperator Implementations
        /// <summary>
        /// And FilterOperator
        /// </summary>
        private bool? And(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            bool? lhs = GetValue(context, operands[0], target) as bool?;

            // no need for further processing if first operand is false.
            if (lhs != null && !lhs.Value)
            {
                return false;
            }

            bool? rhs = GetValue(context, operands[1], target) as bool?;
            
            if (lhs == null)
            {
                if (rhs == null || rhs == true)
                {
                    return null;
                }
                else
                {
                    return false;
                }
            }
            
            if (rhs == null)
            {
                if (lhs == null || lhs == true)
                {
                    return null;
                }
                else
                {
                    return false;
                }
            }

            return lhs.Value && rhs.Value;
        }
        
        /// <summary>
        /// Or FilterOperator
        /// </summary>
        private bool? Or(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            bool? lhs = GetValue(context, operands[0], target) as bool?;

            // no need for further processing if first operand is true.
            if (lhs != null && lhs.Value)
            {
                return true;
            }

            bool? rhs = GetValue(context, operands[1], target) as bool?;
            
            if (lhs == null)
            {
                if (rhs == null || rhs == false)
            {
                return null;
            }
                else
                {
                    return true;
                }
            }

            if (rhs == null)
            {
                if (lhs == null || lhs == false)
                {
                    return null;
                }
                else
                {
                    return true;
                }
            }
            
            return lhs.Value || rhs.Value;
        }
        
        /// <summary>
        /// Not FilterOperator
        /// </summary>
        private bool? Not(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            bool? rhs = GetValue(context, operands[0], target) as bool?;
            
            if (rhs == null)
            {
                return null;
            }
            
            return !rhs.Value;
        }
        
        /// <summary>
        /// Equals FilterOperator
        /// </summary>
        private bool Equals(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(context, operands[0], target);
            object rhs = GetValue(context, operands[1], target);

            DoImplicitConversion(ref lhs, ref rhs);

            return IsEqual(lhs, rhs);
        }
        
        /// <summary>
        /// GreaterThan FilterOperator
        /// </summary>
        private bool? GreaterThan(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(context, operands[0], target);
            object rhs = GetValue(context, operands[1], target);

            DoImplicitConversion(ref lhs, ref rhs);

            if (lhs is IComparable && rhs is IComparable)
            {
                return ((IComparable)lhs).CompareTo(rhs) > 0;
            }

            // return null if the types are not comparable.
            return null;
        }
        
        /// <summary>
        /// GreaterThanOrEqual FilterOperator
        /// </summary>
        private bool? GreaterThanOrEqual(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(context, operands[0], target);
            object rhs = GetValue(context, operands[1], target);

            DoImplicitConversion(ref lhs, ref rhs);

            if (lhs is IComparable && rhs is IComparable)
            {
                return ((IComparable)lhs).CompareTo(rhs) >= 0;
            }

            // return null if the types are not comparable.
            return null;
        }
                
        /// <summary>
        /// LessThan FilterOperator
        /// </summary>
        private bool? LessThan(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(context, operands[0], target);
            object rhs = GetValue(context, operands[1], target);

            DoImplicitConversion(ref lhs, ref rhs);

            if (lhs is IComparable && rhs is IComparable)
            {
                return ((IComparable)lhs).CompareTo(rhs) < 0;
            }

            // return null if the types are not comparable.
            return null;
        }
        
        /// <summary>
        /// LessThanOrEqual FilterOperator
        /// </summary>
        private bool? LessThanOrEqual(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object lhs = GetValue(context, operands[0], target);
            object rhs = GetValue(context, operands[1], target);

            DoImplicitConversion(ref lhs, ref rhs);

            if (lhs is IComparable && rhs is IComparable)
            {
                return ((IComparable)lhs).CompareTo(rhs) <= 0;
            }

            // return null if the types are not comparable.
            return null;
        }
                
        /// <summary>
        /// Between FilterOperator
        /// </summary>
        private bool? Between(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 3);

            object value = GetValue(context, operands[0], target);

            object min = GetValue(context, operands[1], target);
            object max = GetValue(context, operands[2], target);
            
            // the min and max could be different data types so the implicit conversion must be done twice.
            object lhs = value;
            DoImplicitConversion(ref lhs, ref min);

            bool? result = null;
            
            if (lhs is IComparable && min is IComparable)
            {
                // check if never in range no matter what happens with the upper bound.
                if (((IComparable)lhs).CompareTo(min) < 0)
                {
                    return false;
                }

                result = true;
            }
            
            lhs = value;
            DoImplicitConversion(ref lhs, ref max);
            
            if (lhs is IComparable && max is IComparable)
            {
                // check if never in range no matter what happens with the lower bound.
                if (((IComparable)lhs).CompareTo(max) > 0)
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
        private bool? InList(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 0);

            object value = GetValue(context, operands[0], target);

            // check for a match.
            for (int ii = 1; ii < operands.Length; ii++)
            {                
                object lhs = value;
                object rhs = GetValue(context, operands[ii], target);

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
        private bool Like(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);

            object firstOperand = GetValue(context, operands[0], target);
            string lhs;
            LocalizedText firstOperandLocalizedText = firstOperand as LocalizedText;
            if (firstOperandLocalizedText != null)
            {
                lhs = firstOperandLocalizedText.Text;
            }
            else
            {
                lhs = firstOperand as string;
            }
            
            object secondOperand = GetValue(context, operands[1], target);
            string rhs;
            LocalizedText secondOperandLocalizedText = secondOperand as LocalizedText;
            if (secondOperandLocalizedText != null)
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
          
            return Match((string)lhs, (string)rhs);
        }
        
        /// <summary>
        /// IsNull FilterOperator
        /// </summary>
        private bool IsNull(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);

            object rhs = GetValue(context, operands[0], target);
            
            if (rhs == null)
            {
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Cast FilterOperator
        /// </summary>
        private object Cast(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 2);
            
            // get the value to cast.
            object value = GetValue(context, operands[0], target);

            if (value == null)
            {
                return null;
            }

            // get the datatype to cast to.
            NodeId datatype = GetValue(context, operands[1], target) as NodeId;

            if (datatype == null)
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
        private bool OfType(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            FilterOperand[] operands = GetOperands(element, 1);
            
            // get the desired type.
            NodeId typeDefinitionId = GetValue(context, operands[0], target) as NodeId;

            if (typeDefinitionId == null || target == null)
            {
                return false;
            }

            // check the type.
            try
            {
                return target.IsTypeOf(context, typeDefinitionId);
            }
            catch
            {
                return false;
            }
        }
                
        /// <summary>
        /// InView FilterOperator
        /// </summary>
        private bool InView(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            // views only supported in advanced filter targets.
            IAdvancedFilterTarget advancedFilter = target as IAdvancedFilterTarget;

            if (advancedFilter == null)
            {
                return false;
            }

            FilterOperand[] operands = GetOperands(element, 1);
            
            // get the desired type.
            NodeId viewId = GetValue(context, operands[0], target) as NodeId;

            if (viewId == null || target == null)
            {
                return false;
            }

            // check the target.
            try
            {
                return advancedFilter.IsInView(context, viewId);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// RelatedTo FilterOperator
        /// </summary>
        private bool RelatedTo(FilterContext context, IFilterTarget target, ContentFilterElement element)
        {
            return RelatedTo(context, target, element, null);
        }

        /// <summary>
        /// RelatedTo FilterOperator
        /// </summary>
        private bool RelatedTo(FilterContext context, IFilterTarget target, ContentFilterElement element, NodeId intermediateNodeId)
        {
            // RelatedTo only supported in advanced filter targets.
            IAdvancedFilterTarget advancedTarget = target as IAdvancedFilterTarget;

            if (advancedTarget == null)
            {
                return false;
            }

            FilterOperand[] operands = GetOperands(element, 6);
            
            // get the type of the source.
            NodeId sourceTypeId = GetValue(context, operands[0], target) as NodeId;

            if (sourceTypeId == null)
            {
                return false;
            }
                        
            // get the type of reference to follow.
            NodeId referenceTypeId = GetValue(context, operands[2], target) as NodeId;

            if (referenceTypeId == null)
            {
                return false;
            }
            
            // get the number of hops
            int? hops = 1;

            object hopsValue = GetValue(context, operands[3], target);

            if (hopsValue != null)
            {
                hops = Cast(hopsValue, BuiltInType.Int32) as int?;

                if (hops == null)
                {
                    hops = 1;
                }
            }

            // get whether to include type definition subtypes.
            bool? includeTypeDefinitionSubtypes = true;

            object includeValue = GetValue(context, operands[4], target);

            if (includeValue != null)
            {
                includeTypeDefinitionSubtypes = Cast(includeValue, BuiltInType.Boolean) as bool?;

                if (includeTypeDefinitionSubtypes == null)
                {
                    includeTypeDefinitionSubtypes = true;
                }
            }

            // get whether to include reference type subtypes.
            bool? includeReferenceTypeSubtypes = true;

            includeValue = GetValue(context, operands[5], target);

            if (includeValue != null)
            {
                includeReferenceTypeSubtypes = Cast(includeValue, BuiltInType.Boolean) as bool?;

                if (includeReferenceTypeSubtypes == null)
                {
                    includeReferenceTypeSubtypes = true;
                }
            }

            NodeId targetTypeId = null;

            // check if elements are chained.
            ElementOperand chainedOperand = operands[1] as ElementOperand;

            if (chainedOperand != null)
            {
                if (chainedOperand.Index < 0 || chainedOperand.Index >= Elements.Count)
                {
                    return false;
                }

                ContentFilterElement chainedElement = Elements[(int)chainedOperand.Index];

                // get the target type from the first operand of the chained element.
                if (chainedElement.FilterOperator == FilterOperator.RelatedTo)
                {
                    FilterOperand nestedType = ExtensionObject.ToEncodeable(chainedElement.FilterOperands[0]) as FilterOperand;

                    targetTypeId = GetValue(context, nestedType, target) as NodeId;

                    if (targetTypeId == null)
                    {
                        return false;
                    }

                    // find the nodes that meet the criteria in the first link of the chain.
                    IList<NodeId> nodeIds = advancedTarget.GetRelatedNodes(
                        context,
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
                        if (RelatedTo(context, target, chainedElement, nodeIds[ii]))
                        {
                            return true;
                        }
                    }

                    // no matches.
                    return false;
                }
            }
            
            // get the type of the target.
            if (targetTypeId == null)
            {
                targetTypeId = GetValue(context, operands[1], target) as NodeId;

                if (targetTypeId == null)
                {
                    return false;
                }
            }

            // check the target.            
            try
            {
                bool relatedTo = advancedTarget.IsRelatedTo(
                    context,
                    intermediateNodeId,
                    sourceTypeId,
                    targetTypeId,
                    referenceTypeId,
                    hops.Value,
                    includeTypeDefinitionSubtypes.Value,
                    includeReferenceTypeSubtypes.Value);

                return relatedTo;
            }
            catch
            {
                return false;
            }
        }
        #endregion
    }
}
