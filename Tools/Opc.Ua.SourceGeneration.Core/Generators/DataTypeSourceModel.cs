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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Plain data model representing a source-annotated [DataType] type.
    /// Populated from Roslyn symbols in DataTypeCompilation, consumed by
    /// DataTypeSourceGenerator in the Core project without Roslyn dependency.
    /// </summary>
    internal sealed class DataTypeSourceModel
    {
        /// <summary>
        /// The C# class/enum name.
        /// </summary>
        public string ClassName { get; set; }

        /// <summary>
        /// Fully qualified .NET namespace (e.g. "Opc.Ua.Gds.Server").
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// OPC UA namespace URI (from [DataType].Namespace, [DataContract].Namespace,
        /// or fallback to "urn:" + dotnet namespace lowered).
        /// </summary>
        public string NamespaceUri { get; set; }

        /// <summary>
        /// The dot-stripped namespace for use in extension method names
        /// (e.g. "OpcUaGdsServer").
        /// </summary>
        public string NamespaceSymbol { get; set; }

        /// <summary>
        /// Data type id string (e.g. "i=395", "s=MyType", or null for auto).
        /// </summary>
        public string DataTypeId { get; set; }

        /// <summary>
        /// Binary encoding id string, or null.
        /// </summary>
        public string BinaryEncodingId { get; set; }

        /// <summary>
        /// XML encoding id string, or null.
        /// </summary>
        public string XmlEncodingId { get; set; }

        /// <summary>
        /// JSON encoding id string, or null.
        /// </summary>
        public string JsonEncodingId { get; set; }

        /// <summary>
        /// True if the type is a C# record type.
        /// </summary>
        public bool IsRecord { get; set; }

        /// <summary>
        /// True if the type is an enum (or flags enum).
        /// </summary>
        public bool IsEnum { get; set; }

        /// <summary>
        /// True if the type is a flags enum (OptionSet).
        /// </summary>
        public bool IsFlags { get; set; }

        /// <summary>
        /// Name of the base class (null if no relevant base class).
        /// </summary>
        public string BaseClassName { get; set; }

        /// <summary>
        /// Ordered list of fields to encode/decode.
        /// </summary>
        public IReadOnlyList<DataTypeSourceField> Fields { get; set; } = Array.Empty<DataTypeSourceField>();

        /// <summary>
        /// For enums, the list of enum members.
        /// </summary>
        public IReadOnlyList<DataTypeSourceEnumMember> EnumMembers { get; set; } = Array.Empty<DataTypeSourceEnumMember>();
    }

    /// <summary>
    /// Represents a single field of a source-annotated data type.
    /// </summary>
    internal sealed class DataTypeSourceField
    {
        /// <summary>
        /// The C# property name.
        /// </summary>
        public string PropertyName { get; set; }

        /// <summary>
        /// The serialized field name (from [DataTypeField].Name or defaults to PropertyName).
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// The fully qualified C# type name (e.g. "global::System.String", "global::Opc.Ua.NodeId").
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Short type name without namespace prefix (e.g. "String", "NodeId", "Int32").
        /// </summary>
        public string ShortTypeName { get; set; }

        /// <summary>
        /// The OPC UA built-in type, or null if it's a complex/encodeable type.
        /// </summary>
        public string BuiltInType { get; set; }

        /// <summary>
        /// True if this field is a collection (List, Array, etc.).
        /// </summary>
        public bool IsCollection { get; set; }

        /// <summary>
        /// The element type name for collections.
        /// </summary>
        public string ElementTypeName { get; set; }

        /// <summary>
        /// True if the field is nullable / optional.
        /// </summary>
        public bool IsOptional { get; set; }

        /// <summary>
        /// True if the field type implements IEncodeable.
        /// </summary>
        public bool IsEncodeable { get; set; }

        /// <summary>
        /// True if the field type is an enum.
        /// </summary>
        public bool IsEnum { get; set; }

        /// <summary>
        /// The serialization order from [DataTypeField].Order or declaration order.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// The IEncoder method to call (e.g. "WriteString", "WriteInt32", "WriteEncodeable").
        /// </summary>
        public string EncoderMethod { get; set; }

        /// <summary>
        /// The IDecoder method to call (e.g. "ReadString", "ReadInt32", "ReadEncodeable").
        /// </summary>
        public string DecoderMethod { get; set; }

        /// <summary>
        /// Default value expression for initialization (e.g. "null", "0", "string.Empty").
        /// </summary>
        public string DefaultValue { get; set; }
    }

    /// <summary>
    /// Represents an enum member for a source-annotated enum type.
    /// </summary>
    internal sealed class DataTypeSourceEnumMember
    {
        /// <summary>
        /// The member name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The member value as a string (e.g. "0", "1", "0x01").
        /// </summary>
        public string Value { get; set; }
    }
}
