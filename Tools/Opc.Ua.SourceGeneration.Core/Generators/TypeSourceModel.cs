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
    internal sealed record class TypeSourceModel
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
        /// OPC UA namespace URI (from [DataType].Namespace,
        /// [DataContract].Namespace, or fallback to
        /// "urn:" + dotnet namespace lowered).
        /// </summary>
        public string NamespaceUri { get; set; }

        /// <summary>
        /// The dot-stripped namespace for use in extension
        /// method names (e.g. "OpcUaGdsServer").
        /// </summary>
        public string NamespaceSymbol { get; set; }

        /// <summary>
        /// Data type id string (e.g. "i=395", "s=MyType",
        /// or null for auto).
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
        /// True if the base class implements IEncodeable
        /// (or has [DataType] attribute).
        /// </summary>
        public bool BaseTypeIsEncodeable { get; set; }

        /// <summary>
        /// True if the user class is sealed.
        /// </summary>
        public bool IsSealed { get; set; }

        /// <summary>
        /// True if the user class derives from a type that implements
        /// IEncodeable (meaning generated methods must use override).
        /// </summary>
        public bool IsDerived { get; set; }

        /// <summary>
        /// True if the user class is declared internal (not public).
        /// </summary>
        public bool IsInternal { get; set; }

        /// <summary>
        /// True if the user's partial class already defines a
        /// Clone() or MemberwiseClone() method (the generator
        /// should skip emitting these).
        /// </summary>
        public bool HasManualClone { get; set; }

        /// <summary>
        /// If true, the generated extension methods are public.
        /// If false (default), they are internal.
        /// </summary>
        public bool PublicExtensions { get; set; }

        /// <summary>
        /// Ordered list of fields to encode/decode.
        /// </summary>
        public IReadOnlyList<TypeFieldModel> Fields { get; set; }
            = Array.Empty<TypeFieldModel>();

        /// <summary>
        /// For enums, the list of enum members.
        /// </summary>
        public IReadOnlyList<TypeEnumMember> EnumMembers { get; set; }
            = Array.Empty<TypeEnumMember>();
    }

    /// <summary>
    /// Represents a single field of a source-annotated data type.
    /// Contains only the information extracted from Roslyn symbols.
    /// The generator resolves encoder/decoder methods from the type info.
    /// </summary>
    internal sealed record class TypeFieldModel
    {
        /// <summary>
        /// The C# property name.
        /// </summary>
        public string PropertyName { get; set; }

        /// <summary>
        /// The serialized field name (from [DataTypeField].Name
        /// or defaults to PropertyName).
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// The fully qualified C# type name (e.g.
        /// "global::System.String", "global::Opc.Ua.NodeId").
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Short type name without namespace prefix (e.g.
        /// "String", "NodeId", "Int32").
        /// </summary>
        public string ShortTypeName { get; set; }

        /// <summary>
        /// True if the field type is ArrayOf&lt;T&gt;.
        /// </summary>
        public bool IsArray { get; set; }

        /// <summary>
        /// True if the field type is MatrixOf&lt;T&gt;.
        /// </summary>
        public bool IsMatrix { get; set; }

        /// <summary>
        /// The element type short name for ArrayOf/MatrixOf
        /// (e.g. "Int32", "NodeId").
        /// </summary>
        public string ElementShortTypeName { get; set; }

        /// <summary>
        /// The element fully qualified type name for
        /// ArrayOf/MatrixOf.
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
        /// The serialization order from [DataTypeField].Order
        /// or declaration order.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// True if this field was annotated with [DataTypeField].
        /// Used to determine if validation failures should be
        /// errors vs warnings.
        /// </summary>
        public bool HasDataTypeFieldAttribute { get; set; }

        /// <summary>
        /// Structure handling from [DataTypeField].
        /// 0 = Auto, 1 = Inline, 2 = ExtensionObject.
        /// </summary>
        public int StructureHandling { get; set; }

        /// <summary>
        /// True if the field's encodeable type is sealed and has
        /// no IEncodeable base type (auto-detected when
        /// StructureHandling is Auto).
        /// </summary>
        public bool FieldTypeIsSealed { get; set; }

        /// <summary>
        /// True if the field's encodeable type derives from
        /// another IEncodeable (has a non-trivial IEncodeable base).
        /// </summary>
        public bool FieldTypeHasEncodeableBase { get; set; }

        /// <summary>
        /// Controls default value handling during encode/decode.
        /// 0 = Exclude, 1 = Emit, 2 = SetIfMissing, 3 = Include.
        /// </summary>
        public int DefaultValueHandling { get; set; }

        /// <summary>
        /// True if the property uses an init-only setter and is
        /// declared as partial. The generator will emit a private
        /// backing field and a partial property implementation so
        /// that Decode() can assign to the backing field directly.
        /// </summary>
        public bool IsInitOnly { get; set; }

        /// <summary>
        /// The name of the generated backing field for init-only
        /// partial properties (e.g. "__DisplayName").
        /// Null when <see cref="IsInitOnly"/> is false.
        /// </summary>
        public string BackingFieldName { get; set; }

        /// <summary>
        /// The default value initializer expression for init-only
        /// partial properties (e.g. "\"MonitoredItem\"" or "true").
        /// Captured from the defining declaration's initializer.
        /// Null when there is no initializer.
        /// </summary>
        public string DefaultInitializer { get; set; }
    }

    /// <summary>
    /// Represents an enum member for a source-annotated enum type.
    /// </summary>
    internal sealed record class TypeEnumMember
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
