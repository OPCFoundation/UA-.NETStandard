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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using Opc.Ua.SourceGeneration;
using Opc.Ua.Types;

namespace Opc.Ua.Schema.Model
{
    /// <summary>
    /// Defines where to add nullable annotations
    /// </summary>
    internal enum NullableAnnotation
    {
        /// <summary>
        /// Types should be non nullable
        /// </summary>
        NonNullable,

        /// <summary>
        /// Types are nullable except for data types
        /// </summary>
        NullableExceptDataTypes,

        /// <summary>
        /// All types are nullable
        /// </summary>
        Nullable
    }

    /// <summary>
    /// Dotnet code generation support.
    /// </summary>
    internal static class ModelDesignExtensions
    {
        /// <summary>
        /// Get basic data type from data type design.
        /// </summary>
        /// <param name="dataType"></param>
        /// <returns></returns>
        public static BasicDataType DetermineBasicDataType(this DataTypeDesign dataType)
        {
            if (dataType == null)
            {
                return BasicDataType.BaseDataType;
            }

            if (dataType.IsOptionSet)
            {
                return BasicDataType.Enumeration;
            }

            // check if it is a built in data type.
            if (dataType.IsBasicDataType(out BasicDataType basicDataType))
            {
                return basicDataType;
            }

            // recursively search hierarchy if not base data type yet
            BasicDataType basicType = DetermineBasicDataType(
                dataType.BaseTypeNode as DataTypeDesign);

            // data type is user defined if a sub-type of structure.
            if (basicType == BasicDataType.Structure)
            {
                return BasicDataType.UserDefined;
            }

            return basicType;
        }

        /// <summary>
        /// Get type node state class name
        /// </summary>
        public static string GetNodeStateClassName(
            this TypeDesign type,
            string targetNamespace,
            Namespace[] namespaces)
        {
            string className = type.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces);
            return CoreUtils.Format("{0}State", className);
        }

        /// <summary>
        /// Returns the class name to use when creating an instance of the type.
        /// </summary>
        public static string GetNodeStateClassName(
            this InstanceDesign instance,
            string targetNamespace,
            Namespace[] namespaces)
        {
            if (instance is MethodDesign method)
            {
                string className;
                if (method.TypeDefinition != null)
                {
                    className = method.TypeDefinition.AsFullyQualifiedTypeSymbol(namespaces);

                    if (className.EndsWith("MethodType", StringComparison.Ordinal))
                    {
                        className = className[..^"MethodType".Length];
                    }
                    else if (className.EndsWith("Type", StringComparison.Ordinal))
                    {
                        className = className[..^"Type".Length];
                    }
                }
                else
                {
                    className = method.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces);

                    if (className.EndsWith("MethodType", StringComparison.Ordinal))
                    {
                        className = className[..^"MethodType".Length];
                    }
                }

                if (method.HasArguments)
                {
                    return CoreUtils.Format("{0}MethodState", className);
                }

                return "global::Opc.Ua.MethodState";
            }

            if (instance is not VariableDesign variable)
            {
                return CoreUtils.Format(
                    "{0}State",
                    GetClassName(instance.TypeDefinitionNode, namespaces));
            }

            var variableType = instance.TypeDefinitionNode as VariableTypeDesign;

            // check if the variable type restricted the datatype to eliminate the
            // need for a template parameter.
            if (variableType.DataTypeNode.IsTemplateParameterRequired(variableType.ValueRank))
            {
                return CoreUtils.Format(
                    "{0}State",
                    GetClassName(variableType, namespaces));
            }

            // check if the variable instance did not restrict the datatype.
            if (!variable.DataTypeNode.IsTemplateParameterRequired(variable.ValueRank))
            {
                return CoreUtils.Format(
                    "{0}State",
                    GetClassName(variableType, namespaces));
            }

            // instance restricted the datatype but the type did not not.
            string scalarName = GetDotNetTypeName(
                variable.DataTypeNode,
                targetNamespace,
                namespaces,
                nullable: NullableAnnotation.NonNullable,
                true);

            if (variable.ValueRank == ValueRank.Array)
            {
                return CoreUtils.Format(
                    "{0}State<{1}[]>",
                    GetClassName(variableType, namespaces),
                    scalarName);
            }

            if (IsIndeterminateType(variable))
            {
                return $"{GetClassName(variableType, namespaces)}State";
            }

            // add hack for TwoStateDiscreteType which always has to be bool.
            if (variableType.SymbolicName ==
                new XmlQualifiedName("TwoStateDiscreteType", Namespaces.OpcUa))
            {
                return $"{GetClassName(variableType, namespaces)}State";
            }

            return CoreUtils.Format(
                "{0}State<{1}>",
                GetClassName(variableType, namespaces),
                scalarName);
        }

        /// <summary>
        /// Returns the class name to use when creating an instance of the type.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static string GetBaseClassName(this TypeDesign type, Namespace[] namespaces)
        {
            if (type?.BaseTypeNode?.SymbolicName == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type is not DataTypeDesign dataType ||
                dataType.BaseTypeNode is not DataTypeDesign dtd)
            {
                return type.BaseTypeNode.GetClassName(namespaces);
            }

            if (dtd.BasicDataType == BasicDataType.Structure)
            {
                return "global::Opc.Ua.IEncodeable";
            }

            return dtd.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces);
        }

        /// <summary>
        /// Fully qualify a symbol
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static string AsFullyQualifiedTypeSymbol(
            this XmlQualifiedName symbol,
            Namespace[] namespaces,
            string alternativeName = null)
        {
            string name = alternativeName ?? symbol?.Name;
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Symbol name is empty");
            }
            string ns = namespaces.GetNamespacePrefix(symbol?.Namespace);
            if (string.IsNullOrEmpty(ns))
            {
                return name;
            }
            return CoreUtils.Format("global::{0}.{1}", ns, name);
        }

        /// <summary>
        /// Returns the class name to use when creating an instance of the type.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool IsDerivedDataType(this TypeDesign type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (type is not DataTypeDesign ||
                type.BaseTypeNode is not DataTypeDesign dtd)
            {
                return true;
            }
            return dtd.BasicDataType != BasicDataType.Structure;
        }

        /// <summary>
        /// Returns a qualifier for the namespace to use in code.
        /// </summary>
        public static string GetNamespacePrefix(this Namespace[] namespaces, string namespaceUri)
        {
            if (namespaces != null)
            {
                foreach (Namespace ns in namespaces)
                {
                    if (ns.Value == namespaceUri)
                    {
                        return CoreUtils.Format("{0}", ns.Prefix);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a constant string for the namespace uri.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static string GetConstantSymbolForNamespace(
            this Namespace[] namespaces,
            string namespaceUri)
        {
            if (namespaces is null)
            {
                throw new ArgumentNullException(nameof(namespaces));
            }
            Namespace ns = namespaces.GetNamespace(namespaceUri);
            if (ns == null)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(ns.Name))
            {
                throw new ArgumentException("Found namespace has no name");
            }
            if (string.IsNullOrWhiteSpace(ns.Prefix)) // TODO: should this work?
            {
                return CoreUtils.Format("Namespaces.{0}", ns.Name);
            }
            return CoreUtils.Format("global::{0}.Namespaces.{1}", ns.Prefix, ns.Name);
        }

        public static bool IsOverriddenWithSameClass(
            this InstanceDesign instance,
            string targetNamespace,
            Namespace[] namespaces)
        {
            if (instance is null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            if (!instance.IsOverridden())
            {
                return false;
            }

            string x = instance.GetMergedInstance().GetNodeStateClassName(targetNamespace, namespaces);
            string y = instance.OveriddenNode.GetNodeStateClassName(targetNamespace, namespaces);

            if (y.StartsWith("BaseDataVariableState<", StringComparison.Ordinal) &&
                x.StartsWith("BaseDataVariableState<", StringComparison.Ordinal))
            {
                return true;
            }

            return x == y;
        }

        private static bool IsIndeterminateType(this InstanceDesign instance)
        {
            if (instance is VariableDesign variable)
            {
                if (variable.ValueRank is not ValueRank.Scalar and not ValueRank.Array)
                {
                    return true;
                }

                if (variable.DataType ==
                    new XmlQualifiedName(BrowseNames.Enumeration, Namespaces.OpcUa))
                {
                    return true;
                }
            }

            return false;
        }

        public static string EnsureUniqueEnumName(this Parameter target)
        {
            if (target?.Parent is DataTypeDesign dt && dt.HasFields && dt.Fields != null)
            {
                HashSet<string> names = [];

                foreach (Parameter field in dt.Fields)
                {
                    int count = 1;
                    string check = field.Name;

                    if (names.Contains(check))
                    {
                        check = field.Name + "_" + field.Identifier;
                    }

                    while (names.Contains(check))
                    {
                        check = field.Name + "_v" + count++;
                    }

                    if (target.Identifier == field.Identifier)
                    {
                        return check;
                    }

                    names.Add(check);
                }
            }

            return target?.Name;
        }

        /// <summary>
        /// Returns the field name of a child node.
        /// </summary>
        public static string GetChildFieldName(this Parameter field)
        {
            if (string.IsNullOrEmpty(field?.Name))
            {
                return string.Empty;
            }
            return field.Name.ToSafeSymbolName(true, "m_");
        }

        /// <summary>
        /// Returns the field name of a child node.
        /// </summary>
        public static string GetChildFieldName(this InstanceDesign instance)
        {
            if (string.IsNullOrEmpty(instance?.SymbolicName?.Name))
            {
                return string.Empty;
            }

            string name = instance.SymbolicName.Name.ToSafeSymbolName(true, "m_");

            if (instance is MethodDesign)
            {
                return CoreUtils.Format("{0}Method", name);
            }

            return name;
        }

        /// <summary>
        /// Fixes class names for nodes.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static string GetClassName(this TypeDesign node, Namespace[] namespaces)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node is DataTypeDesign)
            {
                return node.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces);
            }

            if (node is ObjectTypeDesign objectType &&
                objectType.ClassName == "ObjectSource")
            {
                return "global::Opc.Ua.BaseObject";
            }

            if (node is VariableTypeDesign variableType &&
                variableType.ClassName is "DataVariable" or "BaseVariable")
            {
                return "global::Opc.Ua.BaseDataVariable";
            }

            if (!string.IsNullOrEmpty(node.ClassName))
            {
                return node.SymbolicName.AsFullyQualifiedTypeSymbol(
                    namespaces, node.ClassName);
            }
            return node.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces);
        }

        /// <summary>
        /// Returns the NodeClass of a Node
        /// </summary>
        public static string GetNodeClassAsString(this NodeDesign node)
        {
            if (node is VariableDesign)
            {
                return "Variable";
            }

            if (node is VariableTypeDesign)
            {
                return "VariableType";
            }

            if (node is ObjectDesign)
            {
                return "Object";
            }

            if (node is ObjectTypeDesign)
            {
                return "ObjectType";
            }

            if (node is ReferenceTypeDesign)
            {
                return "ReferenceType";
            }

            if (node is DataTypeDesign)
            {
                return "DataType";
            }

            if (node is MethodDesign)
            {
                return "Method";
            }

            if (node is ViewDesign)
            {
                return "View";
            }

            return "Node";
        }

        /// <summary>
        /// Maps the event notifier flag onto a string.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static string GetEventNotifierAsCode(this ObjectTypeDesign objectType)
        {
            if (objectType is null)
            {
                throw new ArgumentNullException(nameof(objectType));
            }
            if (objectType.SupportsEvents)
            {
                return "global::Opc.Ua.EventNotifiers.SubscribeToEvents";
            }
            return "global::Opc.Ua.EventNotifiers.None";
        }

        /// <summary>
        /// Maps the value rank enumeration onto a string.
        /// </summary>
        public static string GetValueRankAsCode(this ValueRank valueRank, string arrayDimensions)
        {
            switch (valueRank)
            {
                case ValueRank.Array:
                    return "global::Opc.Ua.ValueRanks.OneDimension";
                case ValueRank.Scalar:
                    return "global::Opc.Ua.ValueRanks.Scalar";
                case ValueRank.Any:
                case ValueRank.ScalarOrArray:
                    return "global::Opc.Ua.ValueRanks.Any";
                case ValueRank.ScalarOrOneDimension:
                    return "global::Opc.Ua.ValueRanks.ScalarOrOneDimension";
                case ValueRank.OneOrMoreDimensions:
                    if (string.IsNullOrWhiteSpace(arrayDimensions))
                    {
                        return "global::Opc.Ua.ValueRanks.OneOrMoreDimensions";
                    }

                    // TODO: "is ,,, not considered 3 dim?

                    string[] dimensions = arrayDimensions.Split([','], StringSplitOptions.RemoveEmptyEntries);
                    int dims = dimensions.Length + 1;
                    if (dims == 1)
                    {
                        return "global::Opc.Ua.ValueRanks.OneDimension";
                    }
                    if (dims == 2)
                    {
                        return "global::Opc.Ua.ValueRanks.TwoDimensions";
                    }
                    return CoreUtils.Format("{0}", dims);
            }

            return "global::Opc.Ua.ValueRanks.Any";
        }

        /// <summary>
        /// Maps the MinimumSamplingInterval onto a constant.
        /// </summary>
        public static string GetMinimumSamplingIntervalAsCode(this VariableTypeDesign variableType)
        {
            return variableType.MinimumSamplingInterval switch
            {
                -1 => "global::Opc.Ua.MinimumSamplingIntervals.Indeterminate",
                0 => "global::Opc.Ua.MinimumSamplingIntervals.Continuous",
                _ => variableType.MinimumSamplingInterval.ToString(CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Whether this is a reference type and implicitly nullable
        /// </summary>
        public static bool IsDotNetReferenceType(
            this DataTypeDesign dataType,
            ValueRank valueRank)
        {
            return !IsDotNetValueType(dataType, valueRank);
        }

        /// <summary>
        /// Whether the data type supports equality comparison in .NET.
        /// </summary>
        public static bool IsDotNetEqualityComparable(
            this DataTypeDesign dataType,
            ValueRank valueRank)
        {
            if (IsDotNetValueType(dataType, valueRank))
            {
                return true;
            }
            if (valueRank == ValueRank.Scalar)
            {
                switch (dataType.BasicDataType)
                {
                    case BasicDataType.String:
                        // Use equality for strings
                        return true;
                    default:
                        return false;
                }
            }
            return false;
        }

        /// <summary>
        /// If the data type is a value type in .NET.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool IsDotNetValueType(
            this DataTypeDesign dataType,
            ValueRank valueRank)
        {
            if (dataType is null)
            {
                throw new ArgumentNullException(nameof(dataType));
            }
            if (valueRank != ValueRank.Scalar)
            {
                return false;
            }
            switch (dataType.BasicDataType)
            {
                case BasicDataType.Boolean:
                case BasicDataType.SByte:
                case BasicDataType.Byte:
                case BasicDataType.Int16:
                case BasicDataType.UInt16:
                case BasicDataType.Int32:
                case BasicDataType.UInt32:
                case BasicDataType.Int64:
                case BasicDataType.UInt64:
                case BasicDataType.Float:
                case BasicDataType.Double:
                case BasicDataType.DateTime:
                case BasicDataType.Guid:
                case BasicDataType.NodeId:
                case BasicDataType.ExpandedNodeId:
                case BasicDataType.QualifiedName:
                case BasicDataType.LocalizedText:
                case BasicDataType.StatusCode:
                case BasicDataType.Structure: // Extension object
                case BasicDataType.BaseDataType: // Variant
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a template parameter is required.
        /// </summary>
        public static string GetValueAsCode(
            this DataTypeDesign dataType,
            ValueRank valueRank,
            XmlElement defaultValue,
            object decodedValue,
            bool useVariantForObject,
            string targetNamespace,
            Namespace[] namespaces,
            IServiceMessageContext context,
            Func<string> onUnknownElement = null,
            bool dataTypeQuirk = false)
        {
            TypeInfo decodedValueType = default;

            if (decodedValue == null && defaultValue != null)
            {
                using var decoder = new XmlDecoder(defaultValue, context);
                decodedValue = decoder.ReadVariantContents(out decodedValueType);
            }

            if (valueRank == ValueRank.Array)
            {
                return dataType.GetArrayValueAsCode(
                    defaultValue,
                    decodedValue,
                    targetNamespace,
                    namespaces,
                    onUnknownElement);
            }

            if (valueRank == ValueRank.Scalar &&
                dataType.BasicDataType != BasicDataType.BaseDataType)
            {
                return dataType.GetScalarValueAsCode(
                    defaultValue,
                    decodedValue,
                    decodedValueType,
                    useVariantForObject,
                    targetNamespace,
                    namespaces,
                    onUnknownElement,
                    dataTypeQuirk);
            }

            return onUnknownElement?.Invoke() ?? "default";
        }

        /// <summary>
        /// Get default for a scalar type
        /// </summary>
        internal static string GetScalarValueAsCode(
            this DataTypeDesign dataType,
            XmlElement defaultValue,
            object decodedValue,
            TypeInfo decodedValueType,
            bool useVariantForObject,
            string targetNamespace,
            Namespace[] namespaces,
            Func<string> onUnknownElement = null,
            bool dataTypeQuirk = false)
        {
            // Note that we return a typed value since we initialize a object/variant
            switch (dataType.BasicDataType)
            {
                case BasicDataType.Boolean:
                    if (decodedValue is not bool boolValue)
                    {
                        boolValue = false;
                    }

                    // If decoded value was passed, but no type info, set to concrete
                    // type so that we correctly handle the default value below.
                    // Otherwise fall to the back compat path that inverts the value.
                    else if (decodedValueType.IsUnknown)
                    {
                        decodedValueType = TypeInfo.Scalars.Boolean;
                    }
                    if (dataTypeQuirk &&
                        (defaultValue == null || decodedValueType != TypeInfo.Scalars.Boolean))
                    {
                        // this is technically a bug but the potential for side effects is
                        // so large that it is better to leave as is.
                        return boolValue ? "false" : "true";
                    }

                    return boolValue ? "true" : "false";
                case BasicDataType.SByte:
                    if (decodedValue is not sbyte sbyteValue)
                    {
                        sbyteValue = 0;
                    }
                    return CoreUtils.Format("(sbyte){0}", sbyteValue);
                case BasicDataType.Byte:
                    if (decodedValue is not byte byteValue)
                    {
                        byteValue = 0;
                    }
                    return CoreUtils.Format("(byte){0}", byteValue);
                case BasicDataType.Int16:
                    if (decodedValue is not short shortValue)
                    {
                        shortValue = 0;
                    }
                    return CoreUtils.Format("(short){0}", shortValue);
                case BasicDataType.UInt16:
                    if (decodedValue is not ushort ushortValue)
                    {
                        ushortValue = 0;
                    }
                    return CoreUtils.Format("(ushort){0}", ushortValue);
                case BasicDataType.Int32:
                    if (decodedValue is not int intValue)
                    {
                        intValue = 0;
                    }
                    return CoreUtils.Format("(int){0}", intValue);
                case BasicDataType.UInt32:
                    if (decodedValue is not uint uintValue)
                    {
                        uintValue = 0;
                    }
                    return CoreUtils.Format("(uint){0}", uintValue);
                case BasicDataType.Integer:
                case BasicDataType.Int64:
                    if (decodedValue is not long longValue)
                    {
                        longValue = 0;
                    }
                    return CoreUtils.Format("(long){0}", longValue);
                case BasicDataType.UInteger:
                case BasicDataType.UInt64:
                    if (decodedValue is not ulong ulongValue)
                    {
                        ulongValue = 0;
                    }
                    return CoreUtils.Format("(ulong){0}", ulongValue);
                case BasicDataType.Float:
                    if (decodedValue is not float floatValue)
                    {
                        floatValue = 0;
                    }
                    return CoreUtils.Format("(float){0}", floatValue);
                case BasicDataType.Number:
                case BasicDataType.Double:
                    if (decodedValue is not double doubleValue)
                    {
                        doubleValue = 0;
                    }
                    return CoreUtils.Format("(double){0}", doubleValue);
                case BasicDataType.String:
                    if (decodedValue is not string stringValue)
                    {
                        return "null";
                    }
                    return stringValue.AsStringLiteral();
                case BasicDataType.DateTime:
                    if (decodedValue is not DateTime dateTimeValue ||
                        dateTimeValue == DateTime.MinValue)
                    {
                        return "global::System.DateTime.MinValue";
                    }
                    return CoreUtils.Format(
                        "global::System.DateTime.ParseExact(\"{0:yyyy-MM-dd HH:mm:ss}\", \"yyyy-MM-dd HH:mm:ss\", global::System.Globalization.CultureInfo.InvariantCulture)",
                        dateTimeValue);
                case BasicDataType.Guid:
                    if (decodedValue is Uuid uuid)
                    {
                        decodedValue = uuid.Guid;
                    }
                    if (decodedValue is not Guid guidValue ||
                        guidValue == Guid.Empty)
                    {
                        return "global::Opc.Ua.Uuid.Empty";
                    }
                    return CoreUtils.Format(
                        "global::Opc.Ua.Uuid.Parse(\"{0}\")",
                        guidValue);
                case BasicDataType.ByteString:
                    if (decodedValue is not byte[] byteStringValue)
                    {
                        return "default(byte[])";
                    }
                    return CoreUtils.Format(
                        "CoreUtils.FromHexString(\"{0}\")",
                        CoreUtils.ToHexString(byteStringValue));
                case BasicDataType.NodeId:
                    if (decodedValue is not NodeId nodeId ||
                        nodeId.IsNullNodeId)
                    {
                        return "global::Opc.Ua.NodeId.Null";
                    }
                    if (nodeId.NamespaceIndex == 0 ||
                        nodeId.NamespaceIndex >= namespaces.Length)
                    {
                        return CoreUtils.Format(
                            "global::Opc.Ua.NodeId.Parse(\"{0}\")",
                            nodeId);
                    }
                    var absoluteId = new ExpandedNodeId(
                        nodeId,
                        namespaces[nodeId.NamespaceIndex].Value);
                    return CoreUtils.Format(
                        "ExpandedNodeId.Parse(\"{0}\", context.NamespaceUris)",
                        absoluteId);
                case BasicDataType.ExpandedNodeId:
                    if (decodedValue is not ExpandedNodeId expandedNodeId ||
                        expandedNodeId.IsNull)
                    {
                        return "global::Opc.Ua.ExpandedNodeId.Null";
                    }
                    return CoreUtils.Format(
                        "global::Opc.Ua.ExpandedNodeId.Parse(\"{0}\")",
                        expandedNodeId);
                case BasicDataType.QualifiedName:
                    if (decodedValue is not QualifiedName qualifiedName ||
                        qualifiedName.IsNullQn)
                    {
                        return "global::Opc.Ua.QualifiedName.Null";
                    }
                    return CoreUtils.Format(
                        "global::Opc.Ua.QualifiedName.Parse(\"{0}\")",
                        qualifiedName);
                case BasicDataType.LocalizedText:
                    if (decodedValue is not Ua.LocalizedText localizedText ||
                        localizedText.IsNullOrEmpty)
                    {
                        return "global::Opc.Ua.LocalizedText.Null";
                    }
                    return CoreUtils.Format(
                        "new global::Opc.Ua.LocalizedText({0}, {1})",
                        localizedText.Locale.AsStringLiteral(),
                        localizedText.Text.AsStringLiteral());
                case BasicDataType.StatusCode:
                    if (decodedValue is not StatusCode statusCode ||
                        statusCode.Code == 0)
                    {
                        return "default(global::Opc.Ua.StatusCode)";
                    }
                    return CoreUtils.Format(
                        "(global::Opc.Ua.StatusCode.StatusCode){0}",
                        statusCode);
                case BasicDataType.Enumeration:
                    if (dataType.BaseTypeNode?.SymbolicId ==
                        new XmlQualifiedName("OptionSet", Namespaces.OpcUa))
                    {
                        return CoreUtils.Format("new {0}()",
                            dataType.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces));
                    }
                    if (!dataType.IsOptionSet && dataType.Fields?.Length > 0)
                    {
                        if (decodedValue is int enumValue)
                        {
                            return CoreUtils.Format("({0}){1}",
                                dataType.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces),
                                enumValue);
                        }
                        if (decodedValue is null)
                        {
                            // Enum type declared - use first field as default
                            return CoreUtils.Format("{0}.{1}",
                                dataType.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces),
                                dataType.Fields[0].Name);
                        }
                        // TODO: change to initialize enum with the value
                        return onUnknownElement?.Invoke() ??
                            CoreUtils.Format("default({0})",
                                dataType.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces));
                    }
                    // Default to enum with value 0 which should be "none" for option set
                    return onUnknownElement?.Invoke() ?? "default";
                case BasicDataType.DataValue:
                    return "new global::Opc.Ua.DataValue()";
                case BasicDataType.UserDefined:
                    if (useVariantForObject && defaultValue == null)
                    {
                        // Ensure a variant can be initialized correcty with
                        // type info
                        return CoreUtils.Format(
                            "new {0}()",
                            GetDotNetTypeName(
                                dataType,
                                ValueRank.Scalar,
                                targetNamespace,
                                namespaces,
                                nullable: NullableAnnotation.NonNullable));
                    }
                    return onUnknownElement?.Invoke() ?? "default";
                default:
                    return onUnknownElement?.Invoke() ?? "default";
            }
        }

        /// <summary>
        /// Get default for a array type
        /// </summary>
        internal static string GetArrayValueAsCode(
            this DataTypeDesign dataType,
            XmlElement defaultValue,
            object decodedValue,
            string targetNamespace,
            Namespace[] namespaces,
            Func<string> onUnknownElement = null)
        {
            if ((decodedValue is not null &&
                (decodedValue is not IList l || l.Count != 0) &&
                (decodedValue is not Array a || a.Length != 0)) ||
                (decodedValue is null && defaultValue is not null))
            {
                // TODO: change to initialize simple arrays with values
                return onUnknownElement?.Invoke() ?? "default";
            }
            // Emit Array.Empty<T>() for empty arrays
            switch (dataType.BasicDataType)
            {
                case BasicDataType.Boolean:
                    return "global::System.Array.Empty<bool>()";
                case BasicDataType.SByte:
                    return "global::System.Array.Empty<sbyte>()";
                case BasicDataType.Byte:
                    return "global::System.Array.Empty<byte>()";
                case BasicDataType.Int16:
                    return "global::System.Array.Empty<short>()";
                case BasicDataType.UInt16:
                    return "global::System.Array.Empty<ushort>()";
                case BasicDataType.Int32:
                    return "global::System.Array.Empty<int>()";
                case BasicDataType.UInt32:
                    return "global::System.Array.Empty<uint>()";
                case BasicDataType.Int64:
                    return "global::System.Array.Empty<long>()";
                case BasicDataType.UInt64:
                    return "global::System.Array.Empty<ulong>()";
                case BasicDataType.Float:
                    return "global::System.Array.Empty<float>()";
                case BasicDataType.Double:
                    return "global::System.Array.Empty<double>()";
                case BasicDataType.String:
                    return "global::System.Array.Empty<string>()";
                case BasicDataType.Guid:
                    return "global::System.Array.Empty<global::Opc.Ua.Uuid>()";
                case BasicDataType.DateTime:
                    return "global::System.Array.Empty<global::System.DateTime>()";
                case BasicDataType.ByteString:
                    return "global::System.Array.Empty<byte[]>()";
                case BasicDataType.XmlElement:
                    return "global::System.Array.Empty<global::System.Xml.XmlElement>()";
                case BasicDataType.NodeId:
                    return "global::System.Array.Empty<global::Opc.Ua.NodeId>()";
                case BasicDataType.ExpandedNodeId:
                    return "global::System.Array.Empty<global::Opc.Ua.ExpandedNodeId>()";
                case BasicDataType.QualifiedName:
                    return "global::System.Array.Empty<global::Opc.Ua.QualifiedName>()";
                case BasicDataType.LocalizedText:
                    return "global::System.Array.Empty<global::Opc.Ua.LocalizedText>()";
                case BasicDataType.StatusCode:
                    return "global::System.Array.Empty<global::Opc.Ua.StatusCode>()";
                case BasicDataType.DataValue:
                    return "global::System.Array.Empty<global::Opc.Ua.DataValue>()";
                case BasicDataType.Structure:
                    return "global::System.Array.Empty<global::Opc.Ua.ExtensionObject>()";
                case BasicDataType.Enumeration:
                case BasicDataType.UserDefined:
                    return CoreUtils.Format(
                        "global::System.Array.Empty<{0}>()",
                        GetDotNetTypeName(
                            dataType,
                            ValueRank.Scalar,
                            targetNamespace,
                            namespaces,
                            nullable: NullableAnnotation.NonNullable));
                case BasicDataType.DiagnosticInfo:
                    return "global::System.Array.Empty<global::Opc.Ua.DiagnosticInfo>()";
                case BasicDataType.Number:
                case BasicDataType.Integer:
                case BasicDataType.UInteger:
                case BasicDataType.BaseDataType:
                    return "global::System.Array.Empty<global::Opc.Ua.Variant>()";
                default:
                    return "default";
            }
        }

        /// <summary>
        /// Get code string to load a variant from XML resource stream.
        /// </summary>
        /// <returns></returns>
        public static string GetVariantFromXmlCode(
            this DataTypeDesign dataType,
            ValueRank valueRank,
            string targetNamespace,
            Namespace[] namespaces,
            string xmlStreamResource,
            string contextVariable = null)
        {
            contextVariable ??= "context";
            switch (dataType.BasicDataType)
            {
                case BasicDataType.BaseDataType:
                    return CoreUtils.Format(
                        "global::Opc.Ua.Variant.FromXml({0}AsStream, {1})",
                        xmlStreamResource,
                        contextVariable);
                case BasicDataType.UserDefined:
                case BasicDataType.Structure:
                case BasicDataType.Enumeration:
                    string dataTypeString = dataType.GetDotNetTypeName(
                        targetNamespace,
                        namespaces,
                        NullableAnnotation.NonNullable,
                        false);
                    return CoreUtils.Format(
                        "global::Opc.Ua.Variant.FromXml({0}AsStream, {1}).Get{2}{3}<{4}>()",
                        xmlStreamResource,
                        contextVariable,
                        dataType.BasicDataType == BasicDataType.Enumeration ? "Enumeration" : "Structure",
                        valueRank == ValueRank.Array ? "Array" : string.Empty,
                        dataTypeString);
                default:
                    return CoreUtils.Format(
                        "global::Opc.Ua.Variant.FromXml({0}AsStream, {1}).Get{2}{3}()",
                        xmlStreamResource,
                        contextVariable,
                        dataType.BasicDataType.ToString(),
                        valueRank == ValueRank.Array ? "Array" : string.Empty);
            }
        }

        /// <summary>
        /// Returns the data type for a method argument.
        /// </summary>
        public static string GetMethodArgumentTypeAsCode(
            this DataTypeDesign datatype,
            ValueRank valueRank,
            string targetNamespace,
            Namespace[] namespaces,
            bool isOptional)
        {
            string typeName = GetDotNetTypeName(
                datatype,
                targetNamespace,
                namespaces,
                nullable: isOptional ? NullableAnnotation.Nullable : NullableAnnotation.NonNullable,
                false); // Use raw arguments

            if (typeName is "global::Opc.Ua.IEncodeable" or "global::Opc.Ua.IEncodeable?")
            {
                typeName = "global::Opc.Ua.ExtensionObject";
            }
            if (valueRank == ValueRank.Array)
            {
                if (typeName is "object" or "object?")
                {
                    typeName = "global::Opc.Ua.Variant";
                }

                return typeName + "[]";
            }

            if (valueRank == ValueRank.Scalar)
            {
                return typeName;
            }

            return "object";
        }

        /// <summary>
        /// Returns true if the type is nullable
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool IsXmlNillable(this BasicDataType type)
        {
            switch (type)
            {
                case BasicDataType.Boolean:
                case BasicDataType.SByte:
                case BasicDataType.Byte:
                case BasicDataType.Int16:
                case BasicDataType.UInt16:
                case BasicDataType.Int32:
                case BasicDataType.UInt32:
                case BasicDataType.Int64:
                case BasicDataType.UInt64:
                case BasicDataType.Float:
                case BasicDataType.Double:
                case BasicDataType.StatusCode:
                case BasicDataType.Enumeration:
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns system type for a basic data type.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static string GetDotNetTypeName(
            this DataTypeDesign datatype,
            string targetNamespace,
            Namespace[] namespaces,
            NullableAnnotation nullable = NullableAnnotation.NonNullable,
            bool useBuiltInWrapperTypes = false)
        {
            switch (datatype.BasicDataType)
            {
                case BasicDataType.Boolean:
                    return "bool";
                case BasicDataType.SByte:
                    return "sbyte";
                case BasicDataType.Byte:
                    return "byte";
                case BasicDataType.Int16:
                    return "short";
                case BasicDataType.UInt16:
                    return "ushort";
                case BasicDataType.Int32:
                    return "int";
                case BasicDataType.UInt32:
                    return "uint";
                case BasicDataType.Int64:
                    return "long";
                case BasicDataType.UInt64:
                    return "ulong";
                case BasicDataType.Float:
                    return "float";
                case BasicDataType.Double:
                    return "double";
                case BasicDataType.String:
                    return nullable == NullableAnnotation.NonNullable ?
                        "string" :
                        "string?";
                case BasicDataType.DateTime:
                    return "global::System.DateTime";
                case BasicDataType.Guid:
                    return "global::Opc.Ua.Uuid";
                case BasicDataType.ByteString:
                    return nullable == NullableAnnotation.NonNullable ?
                        "byte[]" :
                        "byte[]?";
                case BasicDataType.XmlElement:
                    return "global::System.Xml.XmlElement";
                case BasicDataType.NodeId:
                    return "global::Opc.Ua.NodeId";
                case BasicDataType.ExpandedNodeId:
                    return "global::Opc.Ua.ExpandedNodeId";
                case BasicDataType.StatusCode:
                    return "global::Opc.Ua.StatusCode";
                case BasicDataType.DiagnosticInfo:
                    return nullable == NullableAnnotation.NonNullable ?
                        "global::Opc.Ua.DiagnosticInfo" :
                        "global::Opc.Ua.DiagnosticInfo?";
                case BasicDataType.QualifiedName:
                    return "global::Opc.Ua.QualifiedName";
                case BasicDataType.LocalizedText:
                    return "global::Opc.Ua.LocalizedText";
                case BasicDataType.DataValue:
                    return "global::Opc.Ua.DataValue";
                case BasicDataType.Structure:
                    if (!useBuiltInWrapperTypes)
                    {
                        // return nullable == NullableAnnotation.NonNullable ?
                        //     "global::Opc.Ua.IEncodeable" :
                        //     "global::Opc.Ua.IEncodeable?";
                    }
                    return "global::Opc.Ua.ExtensionObject";
                case BasicDataType.Enumeration:
                    if (datatype.SymbolicId ==
                        new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                    {
                        return "int";
                    }

                    if (datatype.IsOptionSet)
                    {
                        return GetDotNetTypeName(
                            (DataTypeDesign)datatype.BaseTypeNode,
                            targetNamespace,
                            namespaces,
                            nullable,  // Should it always be non nullable?
                            useBuiltInWrapperTypes);
                    }
                    return datatype.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces);
                case BasicDataType.UserDefined:
                    string typeName = datatype.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces);
                    if (datatype.IsEnumeration)
                    {
                        return typeName;
                    }
                    // All of these are always set to default type in properties when null
                    // is passed therefore we want to maintain non nullability no matter
                    return nullable != NullableAnnotation.Nullable ? typeName : typeName + "?";
            }
            if (!useBuiltInWrapperTypes)
            {
                return nullable == NullableAnnotation.NonNullable ? "object" : "object?";
            }
            return "global::Opc.Ua.Variant";
        }

        /// <summary>
        /// Returns the type name for the data type with the provided value rank.
        /// </summary>
        public static string GetDotNetTypeName(
            this DataTypeDesign dataType,
            ValueRank valueRank,
            string targetNamespace,
            Namespace[] namespaces,
            NullableAnnotation nullable = NullableAnnotation.NonNullable,
            bool useArrayTypeInsteadOfCollection = false)
        {
            if (valueRank == ValueRank.Scalar)
            {
                return GetDotNetTypeName(
                    dataType,
                    targetNamespace,
                    namespaces,
                    nullable,
                    true);
            }

            if (valueRank == ValueRank.Array)
            {
                // Leave collections always non nullable even though they can
                // serialized as null value. But properties are always init
                // as collection never null
                // return !nullable ? typeName : typeName + "?";
                if (!useArrayTypeInsteadOfCollection)
                {
                    string collectionType = GetCollectionType();
                    if (collectionType != null)
                    {
                        return collectionType;
                    }
                }
                string typeName = GetDotNetTypeName(
                    dataType,
                    targetNamespace,
                    namespaces,
                    nullable,
                    true);
                if (typeName != null)
                {
                    return CoreUtils.Format("{0}[]", typeName);
                }
            }

            return "global::Opc.Ua.Variant";

            string GetCollectionType()
            {
                switch (dataType.BasicDataType)
                {
                    case BasicDataType.Boolean:
                        return "global::Opc.Ua.BooleanCollection";
                    case BasicDataType.SByte:
                        return "global::Opc.Ua.SByteCollection";
                    case BasicDataType.Byte:
                        return "global::Opc.Ua.ByteCollection";
                    case BasicDataType.Int16:
                        return "global::Opc.Ua.Int16Collection";
                    case BasicDataType.UInt16:
                        return "global::Opc.Ua.UInt16Collection";
                    case BasicDataType.Int32:
                        return "global::Opc.Ua.Int32Collection";
                    case BasicDataType.UInt32:
                        return "global::Opc.Ua.UInt32Collection";
                    case BasicDataType.Int64:
                        return "global::Opc.Ua.Int64Collection";
                    case BasicDataType.UInt64:
                        return "global::Opc.Ua.UInt64Collection";
                    case BasicDataType.Float:
                        return "global::Opc.Ua.FloatCollection";
                    case BasicDataType.Double:
                        return "global::Opc.Ua.DoubleCollection";
                    case BasicDataType.String:
                        return "global::Opc.Ua.StringCollection";
                    case BasicDataType.DateTime:
                        return "global::Opc.Ua.DateTimeCollection";
                    case BasicDataType.Guid:
                        return "global::Opc.Ua.UuidCollection";
                    case BasicDataType.ByteString:
                        return "global::Opc.Ua.ByteStringCollection";
                    case BasicDataType.XmlElement:
                        return "global::Opc.Ua.XmlElementCollection";
                    case BasicDataType.NodeId:
                        return "global::Opc.Ua.NodeIdCollection";
                    case BasicDataType.ExpandedNodeId:
                        return "global::Opc.Ua.ExpandedNodeIdCollection";
                    case BasicDataType.StatusCode:
                        return "global::Opc.Ua.StatusCodeCollection";
                    case BasicDataType.DiagnosticInfo:
                        return "global::Opc.Ua.DiagnosticInfoCollection";
                    case BasicDataType.QualifiedName:
                        return "global::Opc.Ua.QualifiedNameCollection";
                    case BasicDataType.LocalizedText:
                        return "global::Opc.Ua.LocalizedTextCollection";
                    case BasicDataType.DataValue:
                        return "global::Opc.Ua.DataValueCollection";
                    case BasicDataType.Number:
                    case BasicDataType.Integer:
                    case BasicDataType.UInteger:
                    case BasicDataType.BaseDataType:
                        return "global::Opc.Ua.VariantCollection";
                    case BasicDataType.Structure:
                        return "global::Opc.Ua.ExtensionObjectCollection";
                    case BasicDataType.Enumeration:
                        if (dataType.SymbolicId ==
                            new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                        {
                            return "global::Opc.Ua.Int32Collection";
                        }

                        if (dataType.IsOptionSet ||
                            dataType.BaseType !=
                                new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                        {
                            return GetDotNetTypeName(
                                (DataTypeDesign)dataType.BaseTypeNode,
                                valueRank,
                                targetNamespace,
                                namespaces,
                                nullable: NullableAnnotation.NonNullable);
                        }
                        goto case BasicDataType.UserDefined;
                    case BasicDataType.UserDefined:
                        if (!dataType.NoArraysAllowed)
                        {
                            return CoreUtils.Format("{0}Collection",
                                dataType.SymbolicName.AsFullyQualifiedTypeSymbol(namespaces));
                        }
                        // No collection type generate, return null to fallback to array
                        break;

                }
                return null; // Default to variant
            }
        }

        /// <summary>
        /// Whether a template parameter is required.
        /// </summary>
        public static bool IsTemplateParameterRequired(
            this DataTypeDesign dataType,
            ValueRank valueRank)
        {
            if (dataType.BasicDataType
                is not BasicDataType.BaseDataType
                and not BasicDataType.Number
                and not BasicDataType.UInteger
                and not BasicDataType.Integer &&
                valueRank
                    is not ValueRank.OneOrMoreDimensions
                    and not ValueRank.ScalarOrOneDimension
                    and not ValueRank.ScalarOrArray)
            {
                return true;
            }
            if (valueRank == ValueRank.Array)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Is overridden instance.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool IsOverridden(this InstanceDesign instance)
        {
            if (instance is null)
            {
                throw new ArgumentNullException(nameof(instance));
            }
            return instance.OveriddenNode != null &&
                instance.ModellingRule != ModellingRule.None &&
                instance.ModellingRule != ModellingRule.ExposesItsArray &&
                instance.ModellingRule != ModellingRule.MandatoryPlaceholder &&
                instance.ModellingRule != ModellingRule.OptionalPlaceholder;
        }

        /// <summary>
        /// Returns the merged instance for an overriden node.
        /// </summary>
        public static InstanceDesign GetMergedInstance(this InstanceDesign instance)
        {
            for (NodeDesign parent = instance.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.Parent == null && parent.Hierarchy != null)
                {
                    string relativePath = instance.SymbolicId.Name;

                    int index = relativePath.IndexOf('_', StringComparison.Ordinal);

                    if (index != -1)
                    {
                        relativePath = relativePath[(index + 1)..];
                    }

                    if (parent.Hierarchy.Nodes.TryGetValue(relativePath,
                        out HierarchyNode hierarchyNode) &&
                        hierarchyNode.Instance is InstanceDesign instanceDesign)
                    {
                        return instanceDesign;
                    }

                    break;
                }
            }

            return instance;
        }

        /// <summary>
        /// Returns a name qualified with a namespace prefix.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static string GetPrefixedName(this XmlQualifiedName qname, List<string> namespaceUris)
        {
            if (qname.IsNull())
            {
                return string.Empty;
            }

            if (qname.Namespace == Namespaces.OpcUaBuiltInTypes)
            {
                return CoreUtils.Format("ua:{0}", qname.Name);
            }

            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            int index = namespaceUris.IndexOf(qname.Namespace);

            if (index > 0)
            {
                return CoreUtils.Format("s{0}:{1}", index - 1, qname.Name);
            }

            return qname.Name;
        }

        /// <summary>
        /// Returns the data type to use for the value of a variable or the argument of a method.
        /// </summary>
        public static string GetBinaryDataType(
            this DataTypeDesign dataType,
            string targetNamespace,
            Namespace[] namespaces)
        {
            switch (dataType.BasicDataType)
            {
                case BasicDataType.Boolean:
                    return "opc:Boolean";
                case BasicDataType.SByte:
                    return "opc:SByte";
                case BasicDataType.Byte:
                    return "opc:Byte";
                case BasicDataType.Int16:
                    return "opc:Int16";
                case BasicDataType.UInt16:
                    return "opc:UInt16";
                case BasicDataType.Int32:
                    return "opc:Int32";
                case BasicDataType.UInt32:
                    return "opc:UInt32";
                case BasicDataType.Int64:
                    return "opc:Int64";
                case BasicDataType.UInt64:
                    return "opc:UInt64";
                case BasicDataType.Float:
                    return "opc:Float";
                case BasicDataType.Double:
                    return "opc:Double";
                case BasicDataType.String:
                    return "opc:CharArray";
                case BasicDataType.DateTime:
                    return "opc:DateTime";
                case BasicDataType.Guid:
                    return "opc:Guid";
                case BasicDataType.ByteString:
                    return "opc:ByteString";
                case BasicDataType.XmlElement:
                    return "ua:XmlElement";
                case BasicDataType.NodeId:
                    return "ua:NodeId";
                case BasicDataType.ExpandedNodeId:
                    return "ua:ExpandedNodeId";
                case BasicDataType.StatusCode:
                    return "ua:StatusCode";
                case BasicDataType.DiagnosticInfo:
                    return "ua:DiagnosticInfo";
                case BasicDataType.QualifiedName:
                    return "ua:QualifiedName";
                case BasicDataType.LocalizedText:
                    return "ua:LocalizedText";
                case BasicDataType.DataValue:
                    return "ua:DataValue";
                case BasicDataType.Number:
                case BasicDataType.Integer:
                case BasicDataType.UInteger:
                case BasicDataType.BaseDataType:
                    return "ua:Variant";
                default:
                    if (dataType.SymbolicName ==
                        new XmlQualifiedName("Structure", Namespaces.OpcUa))
                    {
                        return CoreUtils.Format("ua:ExtensionObject");
                    }

                    if (dataType.SymbolicName ==
                        new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                    {
                        if (dataType.IsOptionSet)
                        {
                            return GetBinaryDataType(
                                (DataTypeDesign)dataType.BaseTypeNode,
                                targetNamespace,
                                namespaces);
                        }

                        return CoreUtils.Format("opc:Int32");
                    }

                    string prefix = "tns";

                    if (dataType.SymbolicName.Namespace != targetNamespace)
                    {
                        if (dataType.SymbolicName.Namespace == Namespaces.OpcUa)
                        {
                            prefix = "ua";
                        }
                        else
                        {
                            prefix = GetXmlNamespacePrefix(
                                namespaces,
                                dataType.SymbolicName.Namespace);
                        }
                    }
                    return CoreUtils.Format("{0}:{1}", prefix, dataType.SymbolicName.Name);
            }
        }

        /// <summary>
        /// Returns the data type to use for the value of a variable or the argument of a method.
        /// </summary>
        public static string GetXmlDataType(
            this DataTypeDesign dataType,
            ValueRank valueRank,
            string targetNamespace,
            Namespace[] namespaces)
        {
            if (valueRank != ValueRank.Scalar)
            {
                switch (dataType.BasicDataType)
                {
                    case BasicDataType.Boolean:
                        return "ua:ListOfBoolean";
                    case BasicDataType.SByte:
                        return "ua:ListOfSByte";
                    case BasicDataType.Byte:
                        return "ua:ListOfByte";
                    case BasicDataType.Int16:
                        return "ua:ListOfInt16";
                    case BasicDataType.UInt16:
                        return "ua:ListOfUInt16";
                    case BasicDataType.Int32:
                        return "ua:ListOfInt32";
                    case BasicDataType.UInt32:
                        return "ua:ListOfUInt32";
                    case BasicDataType.Int64:
                        return "ua:ListOfInt64";
                    case BasicDataType.UInt64:
                        return "ua:ListOfUInt64";
                    case BasicDataType.Float:
                        return "ua:ListOfFloat";
                    case BasicDataType.Double:
                        return "ua:ListOfDouble";
                    case BasicDataType.String:
                        return "ua:ListOfString";
                    case BasicDataType.DateTime:
                        return "ua:ListOfDateTime";
                    case BasicDataType.Guid:
                        return "ua:ListOfGuid";
                    case BasicDataType.ByteString:
                        return "ua:ListOfByteString";
                    case BasicDataType.XmlElement:
                        return "ua:ListOfXmlElement";
                    case BasicDataType.NodeId:
                        return "ua:ListOfNodeId";
                    case BasicDataType.ExpandedNodeId:
                        return "ua:ListOfExpandedNodeId";
                    case BasicDataType.StatusCode:
                        return "ua:ListOfStatusCode";
                    case BasicDataType.DiagnosticInfo:
                        return "ua:ListOfDiagnosticInfo";
                    case BasicDataType.QualifiedName:
                        return "ua:ListOfQualifiedName";
                    case BasicDataType.LocalizedText:
                        return "ua:ListOfLocalizedText";
                    case BasicDataType.DataValue:
                        return "ua:ListOfDataValue";
                    case BasicDataType.Number:
                    case BasicDataType.Integer:
                    case BasicDataType.UInteger:
                    case BasicDataType.BaseDataType:
                        return "ua:ListOfVariant";
                    default:
                        if (dataType.SymbolicName ==
                            new XmlQualifiedName("Structure", Namespaces.OpcUa))
                        {
                            return CoreUtils.Format("ua:ListOfExtensionObject");
                        }

                        if (dataType.SymbolicName ==
                            new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                        {
                            if (dataType.IsOptionSet)
                            {
                                return GetXmlDataType(
                                    (DataTypeDesign)dataType.BaseTypeNode,
                                    valueRank,
                                    targetNamespace,
                                    namespaces);
                            }

                            return CoreUtils.Format("ua:ListOfInt32");
                        }

                        string prefix = "tns";

                        if (dataType.SymbolicName.Namespace != targetNamespace)
                        {
                            if (dataType.SymbolicName.Namespace == Namespaces.OpcUa)
                            {
                                if (dataType.SymbolicName.Name == "Enumeration")
                                {
                                    if (dataType.IsOptionSet)
                                    {
                                        return GetXmlDataType(
                                            (DataTypeDesign)dataType.BaseTypeNode,
                                            valueRank,
                                            targetNamespace,
                                            namespaces);
                                    }

                                    return CoreUtils.Format("ua:ListOfInt32");
                                }

                                prefix = "ua";
                            }
                            else
                            {
                                prefix = GetXmlNamespacePrefix(namespaces, dataType.SymbolicName.Namespace);
                            }
                        }

                        return CoreUtils.Format("{0}:ListOf{1}", prefix, dataType.SymbolicName.Name);
                }
            }

            switch (dataType.BasicDataType)
            {
                case BasicDataType.Boolean:
                    return "xs:boolean";
                case BasicDataType.SByte:
                    return "xs:byte";
                case BasicDataType.Byte:
                    return "xs:unsignedByte";
                case BasicDataType.Int16:
                    return "xs:short";
                case BasicDataType.UInt16:
                    return "xs:unsignedShort";
                case BasicDataType.Int32:
                    return "xs:int";
                case BasicDataType.UInt32:
                    return "xs:unsignedInt";
                case BasicDataType.Int64:
                    return "xs:long";
                case BasicDataType.UInt64:
                    return "xs:unsignedLong";
                case BasicDataType.Float:
                    return "xs:float";
                case BasicDataType.Double:
                    return "xs:double";
                case BasicDataType.String:
                    return "xs:string";
                case BasicDataType.DateTime:
                    return "xs:dateTime";
                case BasicDataType.Guid:
                    return "ua:Guid";
                case BasicDataType.ByteString:
                    return "xs:base64Binary";
                case BasicDataType.XmlElement:
                    return "ua:XmlElement";
                case BasicDataType.NodeId:
                    return "ua:NodeId";
                case BasicDataType.ExpandedNodeId:
                    return "ua:ExpandedNodeId";
                case BasicDataType.StatusCode:
                    return "ua:StatusCode";
                case BasicDataType.DiagnosticInfo:
                    return "ua:DiagnosticInfo";
                case BasicDataType.QualifiedName:
                    return "ua:QualifiedName";
                case BasicDataType.LocalizedText:
                    return "ua:LocalizedText";
                case BasicDataType.DataValue:
                    return "ua:DataValue";
                case BasicDataType.Number:
                case BasicDataType.Integer:
                case BasicDataType.UInteger:
                case BasicDataType.BaseDataType:
                    return "ua:Variant";
                default:
                    if (dataType.SymbolicName ==
                        new XmlQualifiedName("Structure", Namespaces.OpcUa))
                    {
                        return CoreUtils.Format("ua:ExtensionObject");
                    }

                    if (dataType.SymbolicName ==
                        new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                    {
                        if (dataType.IsOptionSet)
                        {
                            return GetXmlDataType(
                                (DataTypeDesign)dataType.BaseTypeNode,
                                valueRank,
                                targetNamespace,
                                namespaces);
                        }

                        return CoreUtils.Format("xs:int");
                    }

                    string prefix = null;

                    if (dataType.SymbolicName.Namespace != targetNamespace)
                    {
                        if (dataType.SymbolicName.Namespace == Namespaces.OpcUa)
                        {
                            if (dataType.SymbolicName.Name == "Enumeration")
                            {
                                if (dataType.IsOptionSet)
                                {
                                    return GetXmlDataType(
                                        (DataTypeDesign)dataType.BaseTypeNode,
                                        valueRank,
                                        targetNamespace,
                                        namespaces);
                                }

                                return CoreUtils.Format("xs:int");
                            }

                            prefix = "ua";
                        }
                        else
                        {
                            prefix = GetXmlNamespacePrefix(namespaces, dataType.SymbolicName.Namespace);
                        }
                    }

                    return CoreUtils.Format("{0}:{1}", prefix ?? "tns", dataType.SymbolicName.Name);
            }
        }

        /// <summary>
        /// Returns a constant for the namespace uri.
        /// </summary>
        public static Namespace GetNamespace(this Namespace[] namespaces, string namespaceUri)
        {
            if (namespaces != null)
            {
                foreach (Namespace ns in namespaces)
                {
                    if (ns.Value == namespaceUri)
                    {
                        return ns;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a constant for the namespace uri.
        /// </summary>
        public static string GetConstantForXmlNamespace(this Namespace[] namespaces, string namespaceUri)
        {
            Namespace ns = GetNamespace(namespaces, namespaceUri);
            if (ns != null)
            {
                if (!string.IsNullOrEmpty(ns.XmlNamespace))
                {
                    return CoreUtils.Format("{1}.Namespaces.{0}Xsd", ns.Name, ns.Prefix);
                }

                return CoreUtils.Format("{1}.Namespaces.{0}", ns.Name, ns.Prefix);
            }
            return null;
        }

        /// <summary>
        /// Returns the XML prefix for the specified namespace.
        /// </summary>
        public static string GetXmlNamespacePrefix(this Namespace[] namespaces, string namespaceUri)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            if (namespaces != null)
            {
                for (int ii = 0; ii < namespaces.Length; ii++)
                {
                    if (namespaces[ii].Value == namespaceUri)
                    {
                        if (string.IsNullOrEmpty(namespaces[ii].XmlPrefix))
                        {
                            return CoreUtils.Format("s{0}", ii);
                        }

                        return namespaces[ii].XmlPrefix;
                    }
                }
            }

            return null;
        }

        public static bool IsMethodTypeDesign(this NodeDesign node)
        {
            if (node?.SymbolicId is null)
            {
                return false;
            }

            if (node is not MethodDesign)
            {
                return false;
            }

            string symbol = node.SymbolicId.Name;

            int index = symbol.IndexOf('_', StringComparison.Ordinal);

            if (index > 0)
            {
                symbol = symbol[..index];
            }

            return symbol.EndsWith("MethodType", StringComparison.Ordinal);
        }

        public static bool IsPartOfOpcUaTypesLibrary(this DataTypeDesign dataType)
        {
            if (dataType != null &&
                dataType.SymbolicId.Namespace == Namespaces.OpcUa)
            {
                switch (dataType.NumericId)
                {
                    case DataTypes.AccessRestrictionType:
                    case DataTypes.ReferenceDescription:
                    case DataTypes.AttributeWriteMask:
                    case DataTypes.Argument:
                    case DataTypes.IdType:
                    case DataTypes.RolePermissionType:
                    case DataTypes.PermissionType:
                    case DataTypes.ViewDescription:
                    case DataTypes.BrowseDescription:
                    case DataTypes.StructureDefinition:
                    case DataTypes.StructureType:
                    case DataTypes.StructureField:
                    case DataTypes.InstanceNode:
                    case DataTypes.ReferenceTypeNode:
                    case DataTypes.ReferenceNode:
                    case DataTypes.DataTypeDefinition:
                    case DataTypes.EnumDefinition:
                    case DataTypes.EnumField:
                    case DataTypes.EnumValueType:
                    case DataTypes.RelativePath:
                    case DataTypes.BrowseDirection:
                    case DataTypes.RelativePathElement:
                    case DataTypes.NodeClass:
                    case DataTypes.Node:
                    case DataTypes.ViewNode:
                    case DataTypes.ObjectNode:
                    case DataTypes.MethodNode:
                    case DataTypes.TypeNode:
                    case DataTypes.ObjectTypeNode:
                    case DataTypes.DataTypeNode:
                    case DataTypes.VariableTypeNode:
                    case DataTypes.VariableNode:
                        return true;
                }
            }
            return false;
        }

        public static Export.ReleaseStatus ToNodeSetReleaseStatus(
            this ReleaseStatus releaseStatus)
        {
            switch (releaseStatus)
            {
                case ReleaseStatus.Deprecated:
                    return Export.ReleaseStatus.Deprecated;
                case ReleaseStatus.RC:
                case ReleaseStatus.Draft:
                    return Export.ReleaseStatus.Draft;
                default:
                    return Export.ReleaseStatus.Released;
            }
        }

        /// <summary>
        /// Maps the access level enumeration onto a code string.
        /// </summary>
        public static string GetAccessLevelAsCode(this AccessLevel accessLevel)
        {
            return accessLevel switch
            {
                AccessLevel.Read => "global::Opc.Ua.AccessLevels.CurrentRead",
                AccessLevel.Write => "global::Opc.Ua.AccessLevels.CurrentWrite",
                AccessLevel.ReadWrite => "global::Opc.Ua.AccessLevels.CurrentReadOrWrite",
                AccessLevel.HistoryRead => "global::Opc.Ua.AccessLevels.HistoryRead",
                AccessLevel.HistoryWrite => "global::Opc.Ua.AccessLevels.HistoryWrite",
                AccessLevel.HistoryReadWrite => "global::Opc.Ua.AccessLevels.HistoryReadOrWrite",
                _ => "global::Opc.Ua.AccessLevels.None"
            };
        }

        public static string GetLocalizedTextAsCode(this string localizedText)
        {
            return GetLocalizedTextAsCode(new LocalizedText
            {
                Value = localizedText
            }, noAutoGen: false);
        }

        public static string GetLocalizedTextAsCode(this LocalizedText localizedText, bool noAutoGen = false)
        {
            if (localizedText?.Value != null && (!noAutoGen || !localizedText.IsAutogenerated))
            {
                return CoreUtils.Format(
                    "new global::Opc.Ua.LocalizedText({0}, string.Empty, {1})",
                    localizedText.Key.AsStringLiteral(),
                    localizedText.Value.Trim().AsStringLiteral());
            }
            return "global::Opc.Ua.LocalizedText.Null";
        }

        /// <summary>
        /// Maps the array dimensions onto code that creates the array.
        /// </summary>
        public static string GetArrayDimensionsAsCode(this ValueRank valueRank, string arrayDimensions)
        {
            if (valueRank is < 0 and not ValueRank.OneOrMoreDimensions)
            {
                return null;
            }

            if (string.IsNullOrEmpty(arrayDimensions))
            {
                if (valueRank == ValueRank.Array)
                {
                    return "new uint[] { 0 }";
                }

                return null;
            }

            var tokens = arrayDimensions
                .Split([','], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            if (tokens.Count == 0)
            {
                return null;
            }

            return CoreUtils.Format(
                "new uint[] {{ {0} }}",
                string.Join(", ", tokens.Select(t =>
                {
                    if (uint.TryParse(t, out uint val))
                    {
                        return val.ToString(CultureInfo.InvariantCulture);
                    }
                    return "0";
                })));
        }

        public static string GetAccessRestrictionsAsCode(
            this AccessRestrictions restrictions,
            bool restrictionsSpecified)
        {
            if (!restrictionsSpecified)
            {
                return null;
            }
            return restrictions switch
            {
                AccessRestrictions.SigningRequired =>
                    "global::Opc.Ua.AccessRestrictionType.SigningRequired",
                AccessRestrictions.EncryptionRequired =>
                    "global::Opc.Ua.AccessRestrictionType.EncryptionRequired",
                AccessRestrictions.SessionRequired =>
                    "global::Opc.Ua.AccessRestrictionType.SessionRequired",
                AccessRestrictions.SessionWithSigningRequired =>
                    "global::Opc.Ua.AccessRestrictionType.SigningRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.SessionRequired",
                AccessRestrictions.SessionWithEncryptionRequired =>
                    "global::Opc.Ua.AccessRestrictionType.EncryptionRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.SessionRequired",
                AccessRestrictions.SigningAndApplyToBrowseRequired =>
                    "global::Opc.Ua.AccessRestrictionType.SigningRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.ApplyRestrictionsToBrowse",
                AccessRestrictions.EncryptionAndApplyToBrowseRequired =>
                    "global::Opc.Ua.AccessRestrictionType.EncryptionRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.ApplyRestrictionsToBrowse",
                AccessRestrictions.SessionAndApplyToBrowseRequired =>
                    "global::Opc.Ua.AccessRestrictionType.SessionRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.ApplyRestrictionsToBrowse",
                AccessRestrictions.SessionWithSigningAndApplyToBrowseRequired =>
                    "global::Opc.Ua.AccessRestrictionType.SigningRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.SessionRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.ApplyRestrictionsToBrowse",
                AccessRestrictions.SessionWithEncryptionAndApplyToBrowseRequired =>
                    "global::Opc.Ua.AccessRestrictionType.EncryptionRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.SessionRequired | " +
                    "global::Opc.Ua.AccessRestrictionType.ApplyRestrictionsToBrowse",
                _ => null
            };
        }

        public static string GetPermissionTypeAsCode(this Permissions[] permissions)
        {
            if (permissions == null || permissions.Length == 0)
            {
                return "global::Opc.Ua.PermissionType.None";
            }

            var parts = new HashSet<string>();
            foreach (Permissions p in permissions)
            {
                switch (p)
                {
                    case Permissions.All:
                        AddPermissionStrings(Permissions.WriteAttribute, parts);
                        AddPermissionStrings(Permissions.WriteRolePermissions, parts);
                        AddPermissionStrings(Permissions.WriteHistorizing, parts);
                        AddPermissionStrings(Permissions.Write, parts);
                        AddPermissionStrings(Permissions.InsertHistory, parts);
                        AddPermissionStrings(Permissions.ModifyHistory, parts);
                        AddPermissionStrings(Permissions.DeleteHistory, parts);
                        AddPermissionStrings(Permissions.Call, parts);
                        AddPermissionStrings(Permissions.AddReference, parts);
                        AddPermissionStrings(Permissions.RemoveReference, parts);
                        AddPermissionStrings(Permissions.DeleteNode, parts);
                        AddPermissionStrings(Permissions.AddNode, parts);
                        goto case Permissions.AllRead;
                    case Permissions.AllRead:
                        AddPermissionStrings(Permissions.Browse, parts);
                        AddPermissionStrings(Permissions.Read, parts);
                        AddPermissionStrings(Permissions.ReadHistory, parts);
                        AddPermissionStrings(Permissions.ReceiveEvents, parts);
                        AddPermissionStrings(Permissions.ReadRolePermissions, parts);
                        break;
                    default:
                        AddPermissionStrings(p, parts);
                        break;
                }
            }
            return parts.Count > 0 ?
                string.Join(" | ", parts) :
                "global::Opc.Ua.PermissionType.None";

            static void AddPermissionStrings(Permissions p, HashSet<string> parts)
            {
                string part = p switch
                {
                    Permissions.Browse =>
                        "global::Opc.Ua.PermissionType.Browse",
                    Permissions.Read =>
                        "global::Opc.Ua.PermissionType.Read",
                    Permissions.Write =>
                        "global::Opc.Ua.PermissionType.Write",
                    Permissions.Call =>
                        "global::Opc.Ua.PermissionType.Call",
                    Permissions.ReadHistory =>
                        "global::Opc.Ua.PermissionType.ReadHistory",
                    Permissions.InsertHistory =>
                        "global::Opc.Ua.PermissionType.InsertHistory",
                    Permissions.ModifyHistory =>
                        "global::Opc.Ua.PermissionType.ModifyHistory",
                    Permissions.DeleteHistory =>
                        "global::Opc.Ua.PermissionType.DeleteHistory",
                    Permissions.ReceiveEvents =>
                        "global::Opc.Ua.PermissionType.ReceiveEvents",
                    Permissions.AddNode =>
                        "global::Opc.Ua.PermissionType.AddNode",
                    Permissions.DeleteNode =>
                        "global::Opc.Ua.PermissionType.DeleteNode",
                    Permissions.AddReference =>
                        "global::Opc.Ua.PermissionType.AddReference",
                    Permissions.RemoveReference =>
                        "global::Opc.Ua.PermissionType.RemoveReference",
                    Permissions.ReadRolePermissions =>
                        "global::Opc.Ua.PermissionType.ReadRolePermissions",
                    Permissions.WriteRolePermissions =>
                        "global::Opc.Ua.PermissionType.WriteRolePermissions",
                    Permissions.WriteAttribute =>
                        "global::Opc.Ua.PermissionType.WriteAttribute",
                    Permissions.WriteHistorizing =>
                        "global::Opc.Ua.PermissionType.WriteHistorizing",
                    _ => null
                };
                if (part != null)
                {
                    parts.Add(part);
                }
            }
        }

        public static string GetNodeIdAsCode(
            this NodeDesign node,
            Namespace[] namespaces,
            string namespaceTableVariable)
        {
            string identifier;
            if (node == null)
            {
                return "global::Opc.Ua.NodeId.Null";
            }
            if (node.NumericIdSpecified)
            {
                identifier = CoreUtils.Format("{0}u", node.NumericId);
            }
            else if (!string.IsNullOrEmpty(node.StringId))
            {
                identifier = node.StringId.AsStringLiteral();
            }
            else
            {
                uint? numericId = FindNumericIdentifier(node);
                if (numericId.HasValue)
                {
                    identifier = CoreUtils.Format("{0}u", numericId.Value);
                }
                else
                {
                    if (string.IsNullOrEmpty(node.SymbolicId.Name))
                    {
                        return "global::Opc.Ua.NodeId.Null";
                    }
                    identifier = node.SymbolicId.Name.AsStringLiteral();
                }
            }
            return CoreUtils.Format(
                "global::Opc.Ua.NodeId.Create({0}, {1}, {2})",
                identifier,
                namespaces.GetConstantSymbolForNamespace(
                    node.SymbolicId.Namespace),
                namespaceTableVariable);
        }

        public static string GetExpandedNodeIdAsCode(
            this NodeDesign node,
            Namespace[] namespaces)
        {
            string identifier;
            if (node == null)
            {
                return "global::Opc.Ua.ExpandedNodeId.Null";
            }
            if (node.NumericIdSpecified)
            {
                identifier = CoreUtils.Format("{0}u", node.NumericId);
            }
            else if (!string.IsNullOrEmpty(node.StringId))
            {
                identifier = node.StringId.AsStringLiteral();
            }
            else
            {
                uint? numericId = FindNumericIdentifier(node);
                if (numericId.HasValue)
                {
                    identifier = CoreUtils.Format("{0}u", numericId.Value);
                }
                else
                {
                    if (string.IsNullOrEmpty(node.SymbolicId.Name))
                    {
                        return "global::Opc.Ua.ExpandedNodeId.Null";
                    }
                    identifier = node.SymbolicId.Name.AsStringLiteral();
                }
            }
            return CoreUtils.Format(
                "new global::Opc.Ua.ExpandedNodeId({0}, {1})",
                identifier,
                namespaces.GetConstantSymbolForNamespace(
                    node.SymbolicId.Namespace));
        }

        public static uint? FindNumericIdentifier(this NodeDesign node)
        {
            uint? numericId = node.NumericIdSpecified ? node.NumericId : null;
            for (NodeDesign parent = node.Parent;
                !numericId.HasValue && parent != null;
                parent = parent.Parent)
            {
                if (parent.Hierarchy == null)
                {
                    continue;
                }
                string browsePath = node.SymbolicId.Name;
                string parentPath = parent.SymbolicId.Name;
                if (browsePath.StartsWith(parentPath, StringComparison.InvariantCulture) &&
                    browsePath[parentPath.Length] == NodeDesign.PathChar)
                {
                    browsePath = browsePath[(parentPath.Length + 1)..];
                }

                if (parent.Hierarchy.Nodes.TryGetValue(browsePath, out HierarchyNode instance) &&
                    instance.Instance.NumericId != 0)
                {
                    numericId = instance.Instance.NumericId;
                }
            }
            return numericId;
        }

        public static string GetNodeIdConstant(
            this IModelDesign design,
            XmlQualifiedName symbolidId,
            string type,
            string namespaceTableVariable)
        {
            if (symbolidId == null)
            {
                return "global::Opc.Ua.NodeId.Null";
            }

            NodeDesign node = design.FindNode(
                symbolidId,
                symbolidId.Name,
                type);
            if (node == null)
            {
                return "global::Opc.Ua.NodeId.Null";
            }

            return node.GetNodeIdAsCode(design.Namespaces, namespaceTableVariable);
        }

        public static void SetDefaultValue(
            this VariableTypeDesign node,
            object value,
            IServiceMessageContext context)
        {
            if (value != null)
            {
                node.DefaultValue = AsXmlElement(value, context);
                node.DecodedValue = value;
            }
            else
            {
                node.DefaultValue = null;
                node.DecodedValue = null;
            }
        }

        public static void SetDefaultValue(
            this VariableDesign node,
            object value,
            IServiceMessageContext context)
        {
            if (value != null)
            {
                node.DefaultValue = AsXmlElement(value, context);
                node.DecodedValue = value;
            }
            else
            {
                node.DefaultValue = null;
                node.DecodedValue = null;
            }
        }

        private static XmlElement AsXmlElement(object value, IServiceMessageContext context)
        {
            if (value != null)
            {
                using var encoder = new XmlEncoder(context);
                var variant = new Variant(value);
                encoder.WriteVariantContents(variant.AsBoxedObject(), variant.TypeInfo);
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadInnerXml(encoder.CloseAndReturnText());
                return xmlDoc.DocumentElement;
            }
            return null;
        }

        private static bool IsBasicDataType(
            this DataTypeDesign dataType,
            out BasicDataType basicDataType)
        {
            if (dataType.SymbolicName.Namespace == "http://opcfoundation.org/UA/")
            {
#if NET8_0_OR_GREATER
                foreach (string name in Enum.GetNames<BasicDataType>())
#else
                foreach (string name in Enum.GetNames(typeof(BasicDataType)))
#endif
                {
                    if (name == dataType.SymbolicName.Name)
                    {
#if NET8_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                        basicDataType = Enum.Parse<BasicDataType>(
                            dataType.SymbolicName.Name);
#else
                        basicDataType = (BasicDataType)Enum.Parse(
                            typeof(BasicDataType),
                            dataType.SymbolicName.Name);
#endif
                        return true;
                    }
                }
            }
            basicDataType = default;
            return false;
        }
    }
}
