/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Dependency
{
    /// <summary>
    /// Kind of node carried in a <see cref="ModelDependencyV1"/>.
    /// </summary>
    public enum DependencyNodeKind : byte
    {
        /// <summary>
        /// Unknown — invalid in a well-formed dependency payload.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// ObjectType.
        /// </summary>
        ObjectType = 1,

        /// <summary>
        /// VariableType.
        /// </summary>
        VariableType = 2,

        /// <summary>
        /// ReferenceType.
        /// </summary>
        ReferenceType = 3,

        /// <summary>
        /// DataType.
        /// </summary>
        DataType = 4,

        /// <summary>
        /// Method (when carried as standalone declaration).
        /// </summary>
        Method = 5
    }

    /// <summary>
    /// Reduced view of a <c>DataTypeDesign</c> field carried in a dependency payload.
    /// </summary>
    public readonly record struct DependencyDataField
    {
        /// <summary>
        /// Field name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// DataType name (qualified).
        /// </summary>
        public string DataTypeName { get; }

        /// <summary>
        /// DataType namespace URI.
        /// </summary>
        public string DataTypeNamespace { get; }

        /// <summary>Value rank (<see cref="ValueRanks"/>).</summary>
        public int ValueRank { get; }

        /// <summary>
        /// True if the structure field is optional (encoding mask bit).
        /// Carried in the structure field trailer.
        /// </summary>
        public bool IsOptional { get; init; }

        /// <summary>
        /// True if the structure field allows subtypes of its data type
        /// (encoded as ExtensionObject). Carried in the structure field
        /// trailer.
        /// </summary>
        public bool AllowSubTypes { get; init; }

        /// <summary>
        /// The declared array dimensions ("0,0,0"), or <c>null</c>.
        /// Carried in the structure field trailer.
        /// </summary>
        public string? ArrayDimensions { get; init; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public DependencyDataField(string name, string dataTypeName, string dataTypeNamespace, int valueRank)
        {
            Name = name ?? string.Empty;
            DataTypeName = dataTypeName ?? string.Empty;
            DataTypeNamespace = dataTypeNamespace ?? string.Empty;
            ValueRank = valueRank;
        }
    }

    /// <summary>
    /// A method input or output argument carried in a dependency payload.
    /// </summary>
    public readonly record struct DependencyMethodArg
    {
        /// <summary>
        /// Argument name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// DataType name (qualified).
        /// </summary>
        public string DataTypeName { get; }

        /// <summary>
        /// DataType namespace URI.
        /// </summary>
        public string DataTypeNamespace { get; }

        /// <summary>
        /// Value rank.
        /// </summary>
        public int ValueRank { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public DependencyMethodArg(string name, string dataTypeName, string dataTypeNamespace, int valueRank)
        {
            Name = name ?? string.Empty;
            DataTypeName = dataTypeName ?? string.Empty;
            DataTypeNamespace = dataTypeNamespace ?? string.Empty;
            ValueRank = valueRank;
        }
    }

    /// <summary>
    /// Reduced view of an <c>InstanceDesign</c> child carried in a
    /// dependency payload type. Carries enough metadata for downstream
    /// generators to set <c>OveriddenNode</c> + <c>TypeDefinitionNode</c>
    /// / <c>DataTypeNode</c> on consumer's re-declared inherited
    /// members.
    /// </summary>
    public sealed class DependencyChild
    {
        /// <summary>
        /// Effective security attributes, absent in legacy payloads.
        /// </summary>
        public DependencySecurity? Security { get; set; }

        /// <summary>
        /// Browse name of the child.
        /// </summary>
        public string BrowseName { get; set; } = string.Empty;

        /// <summary>
        /// Namespace URI qualifying <see cref="BrowseName"/>. Empty when the
        /// declaring type's own namespace qualifies it. A child re-declared
        /// from a base type in another namespace keeps the namespace of its
        /// original declaration here, so a consumer that instantiates the
        /// derived type on its own still emits <c>0:EngineeringUnits</c>
        /// rather than re-qualifying it into the declaring model.
        /// </summary>
        public string BrowseNameNamespace { get; set; } = string.Empty;

        /// <summary>
        /// Symbolic name (often equal to BrowseName).
        /// </summary>
        public string SymbolicName { get; set; } = string.Empty;

        /// <summary>
        /// TypeDefinition name (for all kinds). Empty when not declared.
        /// </summary>
        public string TypeDefinitionName { get; set; } = string.Empty;

        /// <summary>
        /// TypeDefinition namespace URI.
        /// </summary>
        public string TypeDefinitionNamespace { get; set; } = string.Empty;

        /// <summary>
        /// DataType name (variables only). Empty when not applicable.
        /// </summary>
        public string DataTypeName { get; set; } = string.Empty;

        /// <summary>
        /// DataType namespace URI (variables only).
        /// </summary>
        public string DataTypeNamespace { get; set; } = string.Empty;

        /// <summary>
        /// Value rank (variables only), using the ModelDesign ValueRank values.
        /// </summary>
        public int ValueRank { get; set; }

        /// <summary>
        /// Modelling rule: 0=None, 1=Mandatory, 2=Optional, 3=OptionalPlaceholder,
        /// 4=MandatoryPlaceholder, 5=ExposesItsArray.
        /// </summary>
        public byte ModellingRule { get; set; }

        /// <summary>
        /// Instance kind: 1=Object 2=Variable 3=Property 4=Method.
        /// </summary>
        public byte InstanceKind { get; set; }

        /// <summary>
        /// Name of the reference type binding the child to its parent. Empty
        /// when the kind default applies (<c>HasProperty</c> for a property,
        /// <c>HasComponent</c> otherwise).
        /// </summary>
        public string ReferenceTypeName { get; set; } = string.Empty;

        /// <summary>
        /// Namespace URI of <see cref="ReferenceTypeName"/>.
        /// </summary>
        public string ReferenceTypeNamespace { get; set; } = string.Empty;

        /// <summary>
        /// ModelDesign access level value (variables only).
        /// </summary>
        public byte AccessLevel { get; set; }

        /// <summary>
        /// Whether <see cref="AccessLevel"/> was explicitly specified.
        /// </summary>
        public bool AccessLevelSpecified { get; set; }

        /// <summary>
        /// Exact OPC UA AccessLevel bitmask imported from NodeSet2.
        /// </summary>
        public uint? RawAccessLevel { get; set; }

        /// <summary>
        /// Exact OPC UA UserAccessLevel bitmask imported from NodeSet2.
        /// </summary>
        public uint? RawUserAccessLevel { get; set; }

        /// <summary>
        /// Minimum sampling interval (variables only).
        /// </summary>
        public int MinimumSamplingInterval { get; set; }

        /// <summary>
        /// Whether <see cref="MinimumSamplingInterval"/> was explicitly specified.
        /// </summary>
        public bool MinimumSamplingIntervalSpecified { get; set; }

        /// <summary>
        /// Whether historical data is collected (variables only).
        /// </summary>
        public bool Historizing { get; set; }

        /// <summary>
        /// Whether <see cref="Historizing"/> was explicitly specified.
        /// </summary>
        public bool HistorizingSpecified { get; set; }

        /// <summary>
        /// Serialized ModelDesign default value XML (variables only).
        /// </summary>
        public string? DefaultValueXml { get; set; }

        /// <summary>
        /// Whether the variable was imported from a NodeSet2 that declares no
        /// Value for it, so that its Value is null rather than the default
        /// value of its DataType (variables only). Carried as a variable flag
        /// that older readers ignore.
        /// </summary>
        public bool NodeSetValueIsNull { get; set; }

        /// <summary>
        /// The namespace table the namespace indexes in
        /// <see cref="DefaultValueXml"/> refer to in the producing model
        /// (index 0 is the OPC UA namespace): the producer's design
        /// namespaces, or the table of the NodeSet or dependency the value
        /// was decoded from. Null when the payload does not carry it (older
        /// producers); the consumer then resolves the indexes as before.
        /// Carried in the value namespace trailer.
        /// </summary>
        public IReadOnlyList<string>? DefaultValueNamespaceUris { get; set; }

        /// <summary>
        /// Input arguments (methods only).
        /// </summary>
        public IReadOnlyList<DependencyMethodArg> InputArguments { get; set; } = [];

        /// <summary>
        /// Output arguments (methods only).
        /// </summary>
        public IReadOnlyList<DependencyMethodArg> OutputArguments { get; set; } = [];

        /// <summary>
        /// Qualified method state identity used by the producing assembly.
        /// Empty for payloads without method identity and non-method children.
        /// </summary>
        public string MethodStateName { get; set; } = string.Empty;

        /// <summary>
        /// Namespace URI for <see cref="MethodStateName"/>.
        /// </summary>
        public string MethodStateNamespace { get; set; } = string.Empty;

        /// <summary>
        /// Qualified method declaration identity used on the wire.
        /// Empty for payloads without method identity and non-method children.
        /// </summary>
        public string MethodDeclarationName { get; set; } = string.Empty;

        /// <summary>
        /// Namespace URI for <see cref="MethodDeclarationName"/>.
        /// </summary>
        public string MethodDeclarationNamespace { get; set; } = string.Empty;

        /// <summary>
        /// Numeric NodeId identifier for the effective method declaration.
        /// </summary>
        public uint MethodDeclarationNumericId { get; set; }

        /// <summary>
        /// String NodeId identifier for the effective method declaration.
        /// </summary>
        public string? MethodDeclarationStringId { get; set; }

        /// <summary>
        /// Explicit instance-declaration descendants, empty for older payloads and leaf declarations.
        /// </summary>
        public ArrayOf<DependencyChild> Children { get; set; } = [];
    }

    /// <summary>
    /// Reduced view of a node entry carried in a dependency payload. One entry per
    /// node the producing assembly emits as a public type definition
    /// (objects/instances are not carried; consumers re-derive
    /// inheritance via <see cref="BaseTypeName"/>).
    /// </summary>
    public sealed class DependencyNode
    {
        /// <summary>
        /// Security metadata of the type declaration, absent in legacy payloads.
        /// </summary>
        public DependencySecurity? Security { get; set; }

        /// <summary>
        /// Symbolic name.
        /// </summary>
        public string SymbolicName { get; set; } = string.Empty;

        /// <summary>
        /// Symbolic namespace URI.
        /// </summary>
        public string SymbolicNamespace { get; set; } = string.Empty;

        /// <summary>
        /// Emitted C# class name (post-Type-suffix-stripping).
        /// </summary>
        public string ClassName { get; set; } = string.Empty;

        /// <summary>
        /// Node kind.
        /// </summary>
        public DependencyNodeKind Kind { get; set; }

        /// <summary>
        /// Base type name (null for root types).
        /// </summary>
        public string? BaseTypeName { get; set; }

        /// <summary>
        /// Base type namespace URI (null for root types).
        /// </summary>
        public string? BaseTypeNamespace { get; set; }

        /// <summary>
        /// Numeric NodeId (0 when not assigned).
        /// </summary>
        public uint NumericId { get; set; }

        /// <summary>
        /// Optional string NodeId.
        /// </summary>
        public string? StringId { get; set; }

        /// <summary>
        /// Optional Guid NodeId in the "D" format (null when not assigned).
        /// </summary>
        public string? GuidId { get; set; }

        /// <summary>
        /// Optional opaque NodeId as base64 (null when not assigned).
        /// </summary>
        public string? OpaqueId { get; set; }

        /// <summary>
        /// True when the type is abstract.
        /// </summary>
        public bool IsAbstract { get; set; }

        /// <summary>
        /// True when the entry represents an enumeration DataType.
        /// </summary>
        public bool IsEnumeration { get; set; }

        /// <summary>
        /// DataType restriction of a VariableType (null for every other kind).
        /// A consumer needs it to decide whether a variable typed by this
        /// VariableType requires a template parameter on its generated state
        /// class; without it the consumer cannot tell State&lt;T&gt; from the
        /// non-generic state and dereferences a null DataTypeNode.
        /// </summary>
        public string? DataTypeName { get; set; }

        /// <summary>
        /// Namespace URI of <see cref="DataTypeName"/>.
        /// </summary>
        public string? DataTypeNamespace { get; set; }

        /// <summary>
        /// ValueRank of a VariableType, or null when the VariableType does not
        /// specify one (ValueRankSpecified = false upstream).
        /// </summary>
        public int? ValueRank { get; set; }

        /// <summary>
        /// DataType fields (empty for non-DataType kinds).
        /// </summary>
        public IReadOnlyList<DependencyDataField> Fields { get; set; } = [];

        /// <summary>
        /// Declared instance children (empty for DataType / no-child types).
        /// </summary>
        public IReadOnlyList<DependencyChild> Children { get; set; } = [];
    }

    /// <summary>
    /// In-memory representation of a <c>ModelDependencyV1</c> payload.
    /// </summary>
    public sealed class ModelDependencyV1
    {
        /// <summary>
        /// The magic byte sequence.
        /// </summary>
        public static readonly byte[] Magic = [0xAA, 0xC7];

        /// <summary>
        /// The version byte for V1.
        /// </summary>
        public const byte Version = 1;

        /// <summary>
        /// Compression scheme: 1 = Deflate.
        /// </summary>
        public const byte CompressionDeflate = 1;

        private const byte kFluentAccessorsEmitted = 0x01;
        private const byte kFluentAccessorsKnown = 0x40;
        private const byte kMethodIdentityTrailer = 0x80;
        private const byte kMethodIdentityTrailerVersion = 1;
        private const byte kNodeIsAbstract = 0x01;
        private const byte kNodeIsEnumeration = 0x02;
        // The declaration trailer carries the DataType restriction of each
        // VariableType and the BrowseName namespace and ReferenceType of each
        // child, with a flag byte per node and per child.
        private const byte kDeclarationTrailer = 0x04;
        private const byte kDeclarationTrailerVersion = 1;
        private const byte kDeclarationDataType = 0x01;
        private const byte kDeclarationBrowseNameNamespace = 0x01;
        private const byte kDeclarationReferenceType = 0x02;
        private const byte kNestedChildrenTrailer = 0x02;
        private const byte kNestedChildrenTrailerVersion = 1;
        private const int kMaxNestedChildDepth = 100;
        private const int kMaxNestedChildren = 100_000;
        private const byte kExtendedIdentifierTrailer = 0x20;
        private const byte kExtendedIdentifierTrailerVersion = 1;
        private const byte kStructureFieldTrailer = 0x10;
        private const byte kStructureFieldTrailerVersion = 1;
        private const byte kValueNamespaceTrailer = 0x08;
        private const byte kValueNamespaceTrailerVersion = 1;
        private const byte kFieldIsOptional = 0x01;
        private const byte kFieldAllowSubTypes = 0x02;
        private const byte kAccessLevelSpecified = 0x01;
        private const byte kRawAccessLevel = 0x02;
        private const byte kRawUserAccessLevel = 0x04;
        private const byte kMinimumSamplingIntervalSpecified = 0x08;
        private const byte kHistorizingSpecified = 0x10;
        // A reader that predates this flag ignores it and keeps implying the
        // DataType default, which is what it did before.
        private const byte kNodeSetValueIsNull = 0x20;

        /// <summary>
        /// Identifies the independently versioned, length-prefixed security extension after the existing trailers.
        /// </summary>
        private const uint kSecurityTrailerMarker = 0x31434553;
        private const byte kSecurityTrailerVersion = 1;

        /// <summary>
        /// Namespace URI that qualifies the standard reference types.
        /// </summary>
        public const string OpcUaNamespaceUri = "http://opcfoundation.org/UA/";

        /// <summary>
        /// Reference type a child of the given instance kind is bound with when
        /// the declaring model does not name one. Mirrors the defaulting the
        /// validator applies to a locally declared instance, so a child
        /// materialised from a dependency payload is bound the same way as the
        /// identical child declared in the consuming model.
        /// </summary>
        /// <param name="instanceKind">
        /// Instance kind: 1=Object 2=Variable 3=Property 4=Method.
        /// </param>
        public static string GetDefaultReferenceTypeName(byte instanceKind)
        {
            return instanceKind == 3 ? "HasProperty" : "HasComponent";
        }

        /// <summary>
        /// The model URI this dependency payload describes.
        /// </summary>
        public string ModelUri { get; set; } = string.Empty;

        /// <summary>
        /// Whether the producing assembly emitted the per-ObjectType typed
        /// fluent accessor extension classes. <c>null</c> means the payload
        /// does not declare whether accessors exist.
        /// </summary>
        public bool? FluentAccessorsEmitted { get; set; }

        /// <summary>
        /// Nodes in the dependency payload.
        /// </summary>
        public List<DependencyNode> Nodes { get; } = [];

        /// <summary>
        /// Serialises the dependency payload to a base64-encoded
        /// Deflate-compressed payload suitable for embedding in
        /// <c>ModelDependencyAttribute</c>.
        /// </summary>
        public string ToBase64Payload()
        {
            using var raw = new MemoryStream();
            WriteUncompressed(raw);
            raw.Position = 0;

            using var output = new MemoryStream();
            // Header is uncompressed; payload below is Deflate-compressed.
            output.WriteByte(Magic[0]);
            output.WriteByte(Magic[1]);
            output.WriteByte(Version);
            output.WriteByte(CompressionDeflate);

            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                raw.CopyTo(deflate);
            }
            return Convert.ToBase64String(output.ToArray());
        }

        /// <summary>
        /// Reads a dependency payload from a base64-encoded payload.
        /// </summary>
        /// <returns>Null when the payload version is unrecognised or the
        /// magic does not match.</returns>
        public static ModelDependencyV1? FromBase64Payload(string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return null;
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return null;
            }
            if (bytes.Length < 4)
            {
                return null;
            }
            if (bytes[0] != Magic[0] || bytes[1] != Magic[1])
            {
                return null;
            }
            if (bytes[2] != Version)
            {
                return null;
            }
            if (bytes[3] != CompressionDeflate)
            {
                return null;
            }
            using var input = new MemoryStream(bytes, index: 4, count: bytes.Length - 4, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false);
            using var inflated = new MemoryStream();
            deflate.CopyTo(inflated);
            inflated.Position = 0;

            var result = new ModelDependencyV1();
            result.ReadUncompressed(inflated);
            return result;
        }

        private void WriteUncompressed(Stream destination)
        {
            using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
            WriteString(writer, ModelUri);
            writer.Write(Nodes.Count);
            foreach (DependencyNode node in Nodes)
            {
                WriteString(writer, node.SymbolicName);
                WriteString(writer, node.SymbolicNamespace);
                WriteString(writer, node.ClassName);
                writer.Write((byte)node.Kind);
                WriteNullableString(writer, node.BaseTypeName);
                WriteNullableString(writer, node.BaseTypeNamespace);
                writer.Write(node.NumericId);
                WriteNullableString(writer, node.StringId);
                byte flags = 0;
                if (node.IsAbstract)
                {
                    flags |= kNodeIsAbstract;
                }
                if (node.IsEnumeration)
                {
                    flags |= kNodeIsEnumeration;
                }
                writer.Write(flags);
                writer.Write(node.Fields.Count);
                foreach (DependencyDataField field in node.Fields)
                {
                    WriteString(writer, field.Name);
                    WriteString(writer, field.DataTypeName);
                    WriteString(writer, field.DataTypeNamespace);
                    writer.Write(field.ValueRank);
                }
                writer.Write(node.Children.Count);
                foreach (DependencyChild child in node.Children)
                {
                    WriteChild(writer, child);
                }
            }
            bool hasMethodIdentityTrailer = HasMethodIdentityTrailer();
            bool hasStructureFieldTrailer = HasStructureFieldTrailer();
            bool hasValueNamespaceTrailer = HasValueNamespaceTrailer();
            bool hasDeclarationTrailer = HasDeclarationTrailer();
            bool hasNestedChildrenTrailer = HasNestedChildrenTrailer();
            bool hasSecurityTrailer = HasSecurityTrailer();
            // The structure field, value namespace, declaration and nested-child trailers
            // are written after the extended identifier trailer, which is
            // always written with them: a reader that predates them only reads
            // the capabilities as "trailers present" (honouring the
            // FluentAccessorsKnown bit) when the method identity or the
            // extended identifier bit is set, and it stops reading after the
            // trailers it knows - so it never sees the new ones.
            bool hasExtendedIdentifierTrailer =
                hasStructureFieldTrailer ||
                hasValueNamespaceTrailer ||
                hasDeclarationTrailer ||
                hasNestedChildrenTrailer ||
                hasSecurityTrailer ||
                HasExtendedIdentifierTrailer();
            if (FluentAccessorsEmitted.HasValue ||
                hasMethodIdentityTrailer ||
                hasExtendedIdentifierTrailer)
            {
                byte capabilities = 0;
                if (FluentAccessorsEmitted.HasValue)
                {
                    capabilities |= kFluentAccessorsKnown;
                    if (FluentAccessorsEmitted.Value)
                    {
                        capabilities |= kFluentAccessorsEmitted;
                    }
                }
                if (hasMethodIdentityTrailer)
                {
                    capabilities |= kMethodIdentityTrailer;
                }
                if (hasExtendedIdentifierTrailer)
                {
                    capabilities |= kExtendedIdentifierTrailer;
                }
                if (hasStructureFieldTrailer)
                {
                    capabilities |= kStructureFieldTrailer;
                }
                if (hasValueNamespaceTrailer)
                {
                    capabilities |= kValueNamespaceTrailer;
                }
                if (hasDeclarationTrailer)
                {
                    capabilities |= kDeclarationTrailer;
                }
                if (hasNestedChildrenTrailer)
                {
                    capabilities |= kNestedChildrenTrailer;
                }
                writer.Write(capabilities);
                if (hasMethodIdentityTrailer)
                {
                    WriteMethodIdentityTrailer(writer);
                }
                if (hasExtendedIdentifierTrailer)
                {
                    WriteExtendedIdentifierTrailer(writer);
                }
                if (hasStructureFieldTrailer)
                {
                    WriteStructureFieldTrailer(writer);
                }
                if (hasValueNamespaceTrailer)
                {
                    WriteValueNamespaceTrailer(writer);
                }
                if (hasDeclarationTrailer)
                {
                    WriteDeclarationTrailer(writer);
                }
                if (hasNestedChildrenTrailer)
                {
                    WriteNestedChildrenTrailer(writer);
                }
                if (hasSecurityTrailer)
                {
                    WriteSecurityTrailer(writer);
                }
            }
        }

        private void ReadUncompressed(Stream source)
        {
            using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
            ModelUri = ReadString(reader);
            int typeCount = reader.ReadInt32();
            if (typeCount is < 0 or > 1_000_000)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid type count " + typeCount);
            }
            Nodes.Capacity = typeCount;
            for (int i = 0; i < typeCount; i++)
            {
                var node = new DependencyNode
                {
                    SymbolicName = ReadString(reader),
                    SymbolicNamespace = ReadString(reader),
                    ClassName = ReadString(reader),
                    Kind = (DependencyNodeKind)reader.ReadByte(),
                    BaseTypeName = ReadNullableString(reader),
                    BaseTypeNamespace = ReadNullableString(reader),
                    NumericId = reader.ReadUInt32(),
                    StringId = ReadNullableString(reader)
                };
                byte flags = reader.ReadByte();
                node.IsAbstract = (flags & kNodeIsAbstract) != 0;
                node.IsEnumeration = (flags & kNodeIsEnumeration) != 0;
                int fieldCount = reader.ReadInt32();
                if (fieldCount is < 0 or > 100_000)
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid field count " + fieldCount);
                }
                if (fieldCount > 0)
                {
                    var fields = new DependencyDataField[fieldCount];
                    for (int j = 0; j < fieldCount; j++)
                    {
                        fields[j] = new DependencyDataField(
                            ReadString(reader),
                            ReadString(reader),
                            ReadString(reader),
                            reader.ReadInt32());
                    }
                    node.Fields = fields;
                }
                int childCount = reader.ReadInt32();
                if (childCount is < 0 or > 100_000)
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid child count " + childCount);
                }
                if (childCount > 0)
                {
                    var children = new DependencyChild[childCount];
                    for (int j = 0; j < childCount; j++)
                    {
                        children[j] = ReadChild(reader);
                    }
                    node.Children = children;
                }
                Nodes.Add(node);
            }
            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                byte capabilities = reader.ReadByte();
                bool hasMethodIdentityTrailer =
                    (capabilities & kMethodIdentityTrailer) != 0;
                bool hasExtendedIdentifierTrailer =
                    (capabilities & kExtendedIdentifierTrailer) != 0;
                if (hasMethodIdentityTrailer || hasExtendedIdentifierTrailer)
                {
                    FluentAccessorsEmitted =
                        (capabilities & kFluentAccessorsKnown) != 0
                            ? (capabilities & kFluentAccessorsEmitted) != 0
                            : null;
                }
                else
                {
                    FluentAccessorsEmitted =
                        (capabilities & kFluentAccessorsEmitted) != 0;
                }
                // Trailers carry no length prefix, so a trailer of a version
                // this reader does not know cannot be skipped: its body is left
                // unread and whatever follows it cannot be located. Stop there
                // rather than parse the next trailer out of the middle of the
                // unknown one - which either assigned garbage identifiers or
                // threw and dropped the whole payload.
                bool trailersReadable = true;
                if (hasMethodIdentityTrailer &&
                    reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    trailersReadable = ReadMethodIdentityTrailer(reader);
                }
                if (trailersReadable &&
                    hasExtendedIdentifierTrailer &&
                    reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    trailersReadable = ReadExtendedIdentifierTrailer(reader);
                }
                // The later trailers are length prefixed: an unknown version
                // is skipped and the ones after it stay readable.
                if (trailersReadable &&
                    (capabilities & kStructureFieldTrailer) != 0 &&
                    reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    ReadStructureFieldTrailer(reader);
                }
                if (trailersReadable &&
                    (capabilities & kValueNamespaceTrailer) != 0 &&
                    reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    ReadValueNamespaceTrailer(reader);
                }
                if (trailersReadable &&
                    (capabilities & kDeclarationTrailer) != 0 &&
                    reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    ReadDeclarationTrailer(reader);
                }
                if (trailersReadable &&
                    (capabilities & kNestedChildrenTrailer) != 0)
                {
                    ReadNestedChildrenTrailer(reader);
                }
                if (trailersReadable &&
                    reader.BaseStream.Length - reader.BaseStream.Position >= sizeof(uint) &&
                    reader.ReadUInt32() == kSecurityTrailerMarker)
                {
                    ReadSecurityTrailer(reader);
                }
            }
        }

        /// <summary>
        /// Determines whether the optional security extension is required.
        /// </summary>
        private bool HasSecurityTrailer()
        {
            foreach (DependencyNode node in Nodes)
            {
                if (node.Security != null || HasChildSecurity(node.Children))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Finds security metadata in explicit descendants.
        /// </summary>
        private static bool HasChildSecurity(IEnumerable<DependencyChild> children)
        {
            foreach (DependencyChild child in children)
            {
                if (child.Security != null || HasChildSecurity(child.Children.ToArray() ?? []))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Writes security metadata after the legacy payload and its existing trailers.
        /// </summary>
        private void WriteSecurityTrailer(BinaryWriter writer)
        {
            using var body = new MemoryStream();
            using (var securityWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                securityWriter.Write(Nodes.Count);
                int remaining = kMaxNestedChildren;
                foreach (DependencyNode node in Nodes)
                {
                    WriteSecurity(securityWriter, node.Security);
                    WriteChildSecurity(securityWriter, node.Children, 1, ref remaining);
                }
            }
            writer.Write(kSecurityTrailerMarker);
            writer.Write(kSecurityTrailerVersion);
            writer.Write(checked((int)body.Length));
            writer.Write(body.GetBuffer(), 0, (int)body.Length);
        }

        /// <summary>
        /// Writes security metadata in the same declaration order as the nested-child trailer.
        /// </summary>
        private static void WriteChildSecurity(
            BinaryWriter writer,
            IReadOnlyList<DependencyChild> children,
            int depth,
            ref int remaining)
        {
            ValidateNestedChildren(children.Count, depth, ref remaining);
            writer.Write(children.Count);
            foreach (DependencyChild child in children)
            {
                WriteSecurity(writer, child.Security);
                WriteChildSecurity(writer, child.Children.ToArray() ?? [], depth + 1, ref remaining);
            }
        }

        /// <summary>
        /// Writes presence separately from false and zero.
        /// </summary>
        private static void WriteSecurity(BinaryWriter writer, DependencySecurity? security)
        {
            writer.Write(security != null);
            if (security == null)
            {
                return;
            }
            writer.Write(security.WriteAccess);
            writer.Write(security.UserWriteAccess.HasValue);
            if (security.UserWriteAccess.HasValue)
            {
                writer.Write(security.UserWriteAccess.Value);
            }
            WriteOptionalBoolean(writer, security.NonExecutable);
            WriteOptionalBoolean(writer, security.UserExecutable);
            WritePermissions(writer, security.RolePermissions);
            WritePermissions(writer, security.DefaultRolePermissions);
        }

        /// <summary>
        /// Writes an explicitly configured boolean.
        /// </summary>
        private static void WriteOptionalBoolean(BinaryWriter writer, bool? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue)
            {
                writer.Write(value.Value);
            }
        }

        /// <summary>
        /// Preserves absent ACLs, empty ACLs, and every entry's independent role identity.
        /// </summary>
        private static void WritePermissions(BinaryWriter writer, RolePermissionSet? permissions)
        {
            writer.Write(permissions != null);
            if (permissions == null)
            {
                return;
            }
            WriteNullableString(writer, permissions.Name);
            writer.Write(permissions.DoNotInheirit);
            writer.Write(permissions.RolePermission?.Length ?? -1);
            foreach (RolePermission permission in permissions.RolePermission ?? [])
            {
                WriteNullableString(writer, permission.Role?.Name);
                WriteNullableString(writer, permission.Role?.Namespace);
                WriteNullableString(writer, permission.RoleId);
                writer.Write(permission.Permission?.Length ?? -1);
                foreach (Permissions value in permission.Permission ?? [])
                {
                    writer.Write((int)value);
                }
            }
        }

        /// <summary>
        /// Rejects unreadable security metadata instead of treating it as an unrestricted legacy declaration.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// Malformed trailer metadata is detected and wrapped in an <see cref="InvalidOperationException"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The security metadata cannot be decoded and preserved.
        /// </exception>
        private void ReadSecurityTrailer(BinaryReader reader)
        {
            try
            {
                if (reader.ReadByte() != kSecurityTrailerVersion)
                {
                    throw new InvalidDataException("Unknown security trailer.");
                }
                int length = reader.ReadInt32();
                if (length < 0 || length != reader.BaseStream.Length - reader.BaseStream.Position)
                {
                    throw new InvalidDataException("Invalid security trailer length.");
                }
                using var body = new MemoryStream(reader.ReadBytes(length), writable: false);
                using var securityReader = new BinaryReader(body, Encoding.UTF8, leaveOpen: false);
                if (securityReader.ReadInt32() != Nodes.Count)
                {
                    throw new InvalidDataException("Invalid security node count.");
                }
                int remaining = kMaxNestedChildren;
                foreach (DependencyNode node in Nodes)
                {
                    node.Security = ReadSecurity(securityReader);
                    ReadChildSecurity(securityReader, node.Children, 1, ref remaining);
                }
                if (body.Position != body.Length)
                {
                    throw new InvalidDataException("Unexpected security trailer data.");
                }
            }
            catch (Exception ex) when (
                ex is InvalidDataException or IOException or ArgumentException or OverflowException)
            {
                // ModelDependencyReference deliberately tolerates malformed legacy
                // metadata. Security metadata must instead fail generation.
                throw new InvalidOperationException("ModelDependencyV1: cannot preserve security metadata.", ex);
            }
        }

        /// <summary>
        /// Restores metadata only when its shape matches the declarations already decoded.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// The encoded child count does not match the decoded declarations.
        /// </exception>
        private static void ReadChildSecurity(
            BinaryReader reader,
            IReadOnlyList<DependencyChild> children,
            int depth,
            ref int remaining)
        {
            ValidateNestedChildren(children.Count, depth, ref remaining);
            if (reader.ReadInt32() != children.Count)
            {
                throw new InvalidDataException("Invalid security child count.");
            }
            foreach (DependencyChild child in children)
            {
                child.Security = ReadSecurity(reader);
                ReadChildSecurity(reader, child.Children.ToArray() ?? [], depth + 1, ref remaining);
            }
        }

        /// <summary>
        /// Reads independent presence bits for user restrictions and method executability.
        /// </summary>
        private static DependencySecurity? ReadSecurity(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }
            return new DependencySecurity
            {
                WriteAccess = reader.ReadUInt32(),
                UserWriteAccess = reader.ReadBoolean() ? reader.ReadUInt32() : null,
                NonExecutable = reader.ReadBoolean() ? reader.ReadBoolean() : null,
                UserExecutable = reader.ReadBoolean() ? reader.ReadBoolean() : null,
                RolePermissions = ReadPermissions(reader),
                DefaultRolePermissions = ReadPermissions(reader)
            };
        }

        /// <summary>
        /// Reads ACLs without requiring generation-time role declarations.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// A permission count is outside the supported bounds, or a permission value is unknown.
        /// </exception>
        private static RolePermissionSet? ReadPermissions(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }
            var permissions = new RolePermissionSet
            {
                Name = ReadNullableString(reader)!,
                DoNotInheirit = reader.ReadBoolean()
            };
            int count = reader.ReadInt32();
            if (count is < -1 or > kMaxNestedChildren)
            {
                throw new InvalidDataException("Invalid security permission count.");
            }
            if (count >= 0)
            {
                permissions.RolePermission = new RolePermission[count];
                for (int index = 0; index < count; index++)
                {
                    string? name = ReadNullableString(reader);
                    string? uri = ReadNullableString(reader);
                    var permission = new RolePermission
                    {
                        Role = name != null ? new XmlQualifiedName(name, uri) : null!,
                        RoleId = ReadNullableString(reader)
                    };
                    int permissionCount = reader.ReadInt32();
                    if (permissionCount is < -1 or > kMaxNestedChildren)
                    {
                        throw new InvalidDataException("Invalid security permission value count.");
                    }
                    if (permissionCount >= 0)
                    {
                        permission.Permission = new Permissions[permissionCount];
                        for (int value = 0; value < permissionCount; value++)
                        {
                            permission.Permission[value] = (Permissions)reader.ReadInt32();
#if NET5_0_OR_GREATER
                            if (!Enum.IsDefined(permission.Permission[value]))
#else
                            if (!Enum.IsDefined(typeof(Permissions), permission.Permission[value]))
#endif
                            {
                                throw new InvalidDataException("Unknown security permission value.");
                            }
                        }
                    }
                    permissions.RolePermission[index] = permission;
                }
            }
            return permissions;
        }

        private static void WriteChild(BinaryWriter writer, DependencyChild child)
        {
            WriteString(writer, child.BrowseName);
            WriteString(writer, child.SymbolicName);
            WriteString(writer, child.TypeDefinitionName);
            WriteString(writer, child.TypeDefinitionNamespace);
            WriteString(writer, child.DataTypeName);
            WriteString(writer, child.DataTypeNamespace);
            writer.Write(child.ValueRank);
            writer.Write(child.ModellingRule);
            writer.Write(child.InstanceKind);
            byte variableFlags = 0;
            if (child.AccessLevelSpecified)
            {
                variableFlags |= kAccessLevelSpecified;
            }
            if (child.RawAccessLevel.HasValue)
            {
                variableFlags |= kRawAccessLevel;
            }
            if (child.RawUserAccessLevel.HasValue)
            {
                variableFlags |= kRawUserAccessLevel;
            }
            if (child.MinimumSamplingIntervalSpecified)
            {
                variableFlags |= kMinimumSamplingIntervalSpecified;
            }
            if (child.HistorizingSpecified)
            {
                variableFlags |= kHistorizingSpecified;
            }
            if (child.NodeSetValueIsNull)
            {
                variableFlags |= kNodeSetValueIsNull;
            }
            writer.Write(variableFlags);
            writer.Write(child.AccessLevel);
            writer.Write(child.RawAccessLevel.GetValueOrDefault());
            writer.Write(child.RawUserAccessLevel.GetValueOrDefault());
            writer.Write(child.MinimumSamplingInterval);
            writer.Write(child.Historizing);
            WriteNullableString(writer, child.DefaultValueXml);
            WriteMethodArguments(writer, child.InputArguments);
            WriteMethodArguments(writer, child.OutputArguments);
        }

        private static DependencyChild ReadChild(BinaryReader reader)
        {
            var child = new DependencyChild
            {
                BrowseName = ReadString(reader),
                SymbolicName = ReadString(reader),
                TypeDefinitionName = ReadString(reader),
                TypeDefinitionNamespace = ReadString(reader),
                DataTypeName = ReadString(reader),
                DataTypeNamespace = ReadString(reader),
                ValueRank = reader.ReadInt32(),
                ModellingRule = reader.ReadByte(),
                InstanceKind = reader.ReadByte()
            };
            byte flags = reader.ReadByte();
            child.AccessLevelSpecified = (flags & kAccessLevelSpecified) != 0;
            child.AccessLevel = reader.ReadByte();
            uint rawAccess = reader.ReadUInt32();
            child.RawAccessLevel = (flags & kRawAccessLevel) != 0 ? rawAccess : null;
            uint rawUserAccess = reader.ReadUInt32();
            child.RawUserAccessLevel = (flags & kRawUserAccessLevel) != 0 ? rawUserAccess : null;
            child.MinimumSamplingInterval = reader.ReadInt32();
            child.MinimumSamplingIntervalSpecified = (flags & kMinimumSamplingIntervalSpecified) != 0;
            child.Historizing = reader.ReadBoolean();
            child.HistorizingSpecified = (flags & kHistorizingSpecified) != 0;
            child.NodeSetValueIsNull = (flags & kNodeSetValueIsNull) != 0;
            child.DefaultValueXml = ReadNullableString(reader);
            if (child.DefaultValueXml != null)
            {
                ValidateDefaultValueXml(child.DefaultValueXml);
            }
            child.InputArguments = ReadMethodArguments(reader, "input");
            child.OutputArguments = ReadMethodArguments(reader, "output");
            return child;
        }

        private static void WriteMethodArguments(BinaryWriter writer, IReadOnlyList<DependencyMethodArg> arguments)
        {
            writer.Write(arguments.Count);
            foreach (DependencyMethodArg argument in arguments)
            {
                WriteString(writer, argument.Name);
                WriteString(writer, argument.DataTypeName);
                WriteString(writer, argument.DataTypeNamespace);
                writer.Write(argument.ValueRank);
            }
        }

        private static DependencyMethodArg[] ReadMethodArguments(BinaryReader reader, string kind)
        {
            int count = reader.ReadInt32();
            if (count is < 0 or > 100)
            {
                throw new InvalidDataException("ModelDependencyV1: invalid " + kind + " arg count " + count);
            }
            if (count == 0)
            {
                return [];
            }
            var arguments = new DependencyMethodArg[count];
            for (int index = 0; index < count; index++)
            {
                arguments[index] = new DependencyMethodArg(
                    ReadString(reader), ReadString(reader), ReadString(reader), reader.ReadInt32());
            }
            return arguments;
        }

        private bool HasNestedChildrenTrailer()
        {
            foreach (DependencyNode node in Nodes)
            {
                foreach (DependencyChild child in node.Children)
                {
                    if (child.Children.Count > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void WriteNestedChildrenTrailer(BinaryWriter writer)
        {
            using var body = new MemoryStream();
            using (var nested = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                int remaining = kMaxNestedChildren;
                nested.Write(Nodes.Count);
                foreach (DependencyNode node in Nodes)
                {
                    nested.Write(node.Children.Count);
                    foreach (DependencyChild child in node.Children)
                    {
                        WriteNestedChildren(nested, child.Children, 1, ref remaining);
                    }
                }
            }
            writer.Write(kNestedChildrenTrailerVersion);
            writer.Write(checked((int)body.Length));
            writer.Write(body.GetBuffer(), 0, (int)body.Length);
        }

        private static void WriteNestedChildren(
            BinaryWriter writer,
            ArrayOf<DependencyChild> children,
            int depth,
            ref int remaining)
        {
            ValidateNestedChildren(children.Count, depth, ref remaining);
            writer.Write(children.Count);
            foreach (DependencyChild child in children)
            {
                WriteChild(writer, child);
                WriteString(writer, child.BrowseNameNamespace);
                WriteString(writer, child.ReferenceTypeName);
                WriteString(writer, child.ReferenceTypeNamespace);
                WriteString(writer, child.MethodStateName);
                WriteString(writer, child.MethodStateNamespace);
                WriteString(writer, child.MethodDeclarationName);
                WriteString(writer, child.MethodDeclarationNamespace);
                writer.Write(child.MethodDeclarationNumericId);
                WriteNullableString(writer, child.MethodDeclarationStringId);
                IReadOnlyList<string>? uris = child.DefaultValueNamespaceUris;
                writer.Write(uris?.Count ?? -1);
                if (uris != null)
                {
                    foreach (string uri in uris)
                    {
                        WriteString(writer, uri);
                    }
                }
                WriteNestedChildren(writer, child.Children, depth + 1, ref remaining);
            }
        }

        private void ReadNestedChildrenTrailer(BinaryReader reader)
        {
            byte version = reader.ReadByte();
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException("ModelDependencyV1: invalid nested-child trailer length " + length);
            }
            if (version != kNestedChildrenTrailerVersion)
            {
                reader.BaseStream.Seek(length, SeekOrigin.Current);
                return;
            }
            using var body = new MemoryStream(reader.ReadBytes(length), writable: false);
            using var nested = new BinaryReader(body, Encoding.UTF8, leaveOpen: false);
            int nodeCount = nested.ReadInt32();
            if (nodeCount != Nodes.Count)
            {
                throw new InvalidDataException("ModelDependencyV1: invalid nested-child node count " + nodeCount);
            }
            int remaining = kMaxNestedChildren;
            foreach (DependencyNode node in Nodes)
            {
                int childCount = nested.ReadInt32();
                if (childCount != node.Children.Count)
                {
                    throw new InvalidDataException("ModelDependencyV1: invalid nested-child count " + childCount);
                }
                foreach (DependencyChild child in node.Children)
                {
                    child.Children = ReadNestedChildren(nested, 1, ref remaining);
                }
            }
            if (body.Position != body.Length)
            {
                throw new InvalidDataException("ModelDependencyV1: unexpected nested-child trailer data.");
            }
        }

        private static ArrayOf<DependencyChild> ReadNestedChildren(BinaryReader reader, int depth, ref int remaining)
        {
            int count = reader.ReadInt32();
            ValidateNestedChildren(count, depth, ref remaining);
            if (count == 0)
            {
                return [];
            }
            var children = new DependencyChild[count];
            for (int index = 0; index < count; index++)
            {
                DependencyChild child = ReadChild(reader);
                child.BrowseNameNamespace = ReadString(reader);
                child.ReferenceTypeName = ReadString(reader);
                child.ReferenceTypeNamespace = ReadString(reader);
                child.MethodStateName = ReadString(reader);
                child.MethodStateNamespace = ReadString(reader);
                child.MethodDeclarationName = ReadString(reader);
                child.MethodDeclarationNamespace = ReadString(reader);
                child.MethodDeclarationNumericId = reader.ReadUInt32();
                child.MethodDeclarationStringId = ReadNullableString(reader);
                int uriCount = reader.ReadInt32();
                if (uriCount is < -1 or 0 or > (ushort.MaxValue + 1))
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid nested value namespace count " + uriCount);
                }
                if (uriCount >= 0)
                {
                    var uris = new string[uriCount];
                    for (int uri = 0; uri < uriCount; uri++)
                    {
                        uris[uri] = ReadString(reader);
                    }
                    child.DefaultValueNamespaceUris = uris;
                }
                child.Children = ReadNestedChildren(reader, depth + 1, ref remaining);
                children[index] = child;
            }
            return ArrayOf.Wrapped(children);
        }

        private static void ValidateNestedChildren(int count, int depth, ref int remaining)
        {
            if (count < 0 || count > remaining || (count > 0 && depth > kMaxNestedChildDepth))
            {
                throw new InvalidDataException("ModelDependencyV1: nested-child count or depth limit exceeded.");
            }
            remaining -= count;
        }

        private bool HasDeclarationTrailer()
        {
            foreach (DependencyNode node in Nodes)
            {
                if (node.DataTypeName != null)
                {
                    return true;
                }
                foreach (DependencyChild child in node.Children)
                {
                    if (!string.IsNullOrEmpty(child.BrowseNameNamespace) ||
                        !string.IsNullOrEmpty(child.ReferenceTypeName))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Writes the declaration trailer: per node the DataType and ValueRank
        /// of a VariableType, and per child of that node the namespace of its
        /// BrowseName and the ReferenceType that binds it, each behind a flag
        /// byte. The body is length prefixed so that a reader can skip a
        /// later version.
        /// </summary>
        private void WriteDeclarationTrailer(BinaryWriter writer)
        {
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                bodyWriter.Write(Nodes.Count);
                foreach (DependencyNode node in Nodes)
                {
                    bool hasDataType = node.DataTypeName != null;
                    bodyWriter.Write(hasDataType ? kDeclarationDataType : (byte)0);
                    if (hasDataType)
                    {
                        WriteString(bodyWriter, node.DataTypeName ?? string.Empty);
                        WriteString(bodyWriter, node.DataTypeNamespace ?? string.Empty);
                        bodyWriter.Write(node.ValueRank.HasValue);
                        bodyWriter.Write(node.ValueRank ?? 0);
                    }
                    bodyWriter.Write(node.Children.Count);
                    foreach (DependencyChild child in node.Children)
                    {
                        bool hasBrowseNameNamespace =
                            !string.IsNullOrEmpty(child.BrowseNameNamespace);
                        bool hasReferenceType =
                            !string.IsNullOrEmpty(child.ReferenceTypeName);
                        byte childFlags = 0;
                        if (hasBrowseNameNamespace)
                        {
                            childFlags |= kDeclarationBrowseNameNamespace;
                        }
                        if (hasReferenceType)
                        {
                            childFlags |= kDeclarationReferenceType;
                        }
                        bodyWriter.Write(childFlags);
                        if (hasBrowseNameNamespace)
                        {
                            WriteString(bodyWriter, child.BrowseNameNamespace);
                        }
                        if (hasReferenceType)
                        {
                            WriteString(bodyWriter, child.ReferenceTypeName);
                            WriteString(bodyWriter, child.ReferenceTypeNamespace);
                        }
                    }
                }
            }
            writer.Write(kDeclarationTrailerVersion);
            writer.Write((int)body.Length);
            writer.Write(body.GetBuffer(), 0, (int)body.Length);
        }

        private void ReadDeclarationTrailer(BinaryReader reader)
        {
            byte trailerVersion = reader.ReadByte();
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid declaration trailer length " + length);
            }
            if (trailerVersion != kDeclarationTrailerVersion)
            {
                // Length prefixed: a later version is skipped as a whole.
                reader.BaseStream.Seek(length, SeekOrigin.Current);
                return;
            }

            int nodeCount = reader.ReadInt32();
            if (nodeCount != Nodes.Count)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid declaration node count " + nodeCount);
            }
            for (int i = 0; i < nodeCount; i++)
            {
                DependencyNode node = Nodes[i];
                byte nodeFlags = reader.ReadByte();
                if ((nodeFlags & kDeclarationDataType) != 0)
                {
                    node.DataTypeName = ReadString(reader);
                    node.DataTypeNamespace = ReadString(reader);
                    bool valueRankSpecified = reader.ReadBoolean();
                    int valueRank = reader.ReadInt32();
                    node.ValueRank = valueRankSpecified ? valueRank : null;
                }
                int childCount = reader.ReadInt32();
                if (childCount != node.Children.Count)
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid declaration child count " + childCount);
                }
                for (int j = 0; j < childCount; j++)
                {
                    DependencyChild child = node.Children[j];
                    byte childFlags = reader.ReadByte();
                    if ((childFlags & kDeclarationBrowseNameNamespace) != 0)
                    {
                        child.BrowseNameNamespace = ReadString(reader);
                    }
                    if ((childFlags & kDeclarationReferenceType) != 0)
                    {
                        child.ReferenceTypeName = ReadString(reader);
                        child.ReferenceTypeNamespace = ReadString(reader);
                    }
                }
            }
        }

        private bool HasValueNamespaceTrailer()
        {
            foreach (DependencyNode node in Nodes)
            {
                foreach (DependencyChild child in node.Children)
                {
                    if (child.DefaultValueNamespaceUris != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Writes the value namespace trailer: the distinct namespace tables
        /// the default values of the children refer to, then per node and
        /// child (in order) the index of the child's table, -1 for none. The
        /// body is length prefixed so that a reader can skip a later version.
        /// </summary>
        private void WriteValueNamespaceTrailer(BinaryWriter writer)
        {
            var tables = new List<IReadOnlyList<string>>();
            var tableIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                var childTables = new List<int>();
                foreach (DependencyNode node in Nodes)
                {
                    foreach (DependencyChild child in node.Children)
                    {
                        IReadOnlyList<string>? uris = child.DefaultValueNamespaceUris;
                        if (uris == null)
                        {
                            childTables.Add(-1);
                            continue;
                        }
                        string key = GetTableKey(uris);
                        if (!tableIndexes.TryGetValue(key, out int index))
                        {
                            index = tables.Count;
                            tables.Add(uris);
                            tableIndexes.Add(key, index);
                        }
                        childTables.Add(index);
                    }
                }

                bodyWriter.Write(tables.Count);
                foreach (IReadOnlyList<string> table in tables)
                {
                    bodyWriter.Write(table.Count);
                    foreach (string uri in table)
                    {
                        WriteString(bodyWriter, uri);
                    }
                }
                int next = 0;
                bodyWriter.Write(Nodes.Count);
                foreach (DependencyNode node in Nodes)
                {
                    bodyWriter.Write(node.Children.Count);
                    for (int j = 0; j < node.Children.Count; j++)
                    {
                        bodyWriter.Write(childTables[next++]);
                    }
                }
            }
            writer.Write(kValueNamespaceTrailerVersion);
            writer.Write((int)body.Length);
            writer.Write(body.GetBuffer(), 0, (int)body.Length);
        }

        /// <summary>
        /// An unambiguous key for a namespace table (length prefixed URIs).
        /// </summary>
        private static string GetTableKey(IReadOnlyList<string> uris)
        {
            var key = new StringBuilder();
            foreach (string uri in uris)
            {
                string value = uri ?? string.Empty;
                key.Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(value);
            }
            return key.ToString();
        }

        private void ReadValueNamespaceTrailer(BinaryReader reader)
        {
            byte trailerVersion = reader.ReadByte();
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid value namespace trailer length " + length);
            }
            if (trailerVersion != kValueNamespaceTrailerVersion)
            {
                // Length prefixed: a later version is skipped as a whole.
                reader.BaseStream.Seek(length, SeekOrigin.Current);
                return;
            }

            int tableCount = reader.ReadInt32();
            if (tableCount is < 0 or > 100_000)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid value namespace table count " + tableCount);
            }
            string[][] tables = new string[tableCount][];
            for (int i = 0; i < tableCount; i++)
            {
                int uriCount = reader.ReadInt32();
                if (uriCount is < 1 or > ushort.MaxValue + 1)
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid value namespace table size " + uriCount);
                }
                string[] uris = new string[uriCount];
                for (int k = 0; k < uriCount; k++)
                {
                    uris[k] = ReadString(reader);
                }
                tables[i] = uris;
            }

            int nodeCount = reader.ReadInt32();
            if (nodeCount != Nodes.Count)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid value namespace node count " + nodeCount);
            }
            for (int i = 0; i < nodeCount; i++)
            {
                DependencyNode node = Nodes[i];
                int childCount = reader.ReadInt32();
                if (childCount != node.Children.Count)
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid value namespace child count " + childCount);
                }
                for (int j = 0; j < childCount; j++)
                {
                    int index = reader.ReadInt32();
                    if (index < -1 || index >= tableCount)
                    {
                        throw new InvalidDataException(
                            "ModelDependencyV1: invalid value namespace table index " + index);
                    }
                    node.Children[j].DefaultValueNamespaceUris =
                        index < 0 ? null : tables[index];
                }
            }
        }

        private bool HasStructureFieldTrailer()
        {
            foreach (DependencyNode node in Nodes)
            {
                foreach (DependencyDataField field in node.Fields)
                {
                    if (field.IsOptional ||
                        field.AllowSubTypes ||
                        !string.IsNullOrEmpty(field.ArrayDimensions))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Writes the structure field trailer: the IsOptional, AllowSubTypes
        /// and ArrayDimensions of every field, in node and field order. The
        /// body is length prefixed so that a reader can skip a later version.
        /// </summary>
        private void WriteStructureFieldTrailer(BinaryWriter writer)
        {
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                bodyWriter.Write(Nodes.Count);
                foreach (DependencyNode node in Nodes)
                {
                    bodyWriter.Write(node.Fields.Count);
                    foreach (DependencyDataField field in node.Fields)
                    {
                        byte fieldFlags = 0;
                        if (field.IsOptional)
                        {
                            fieldFlags |= kFieldIsOptional;
                        }
                        if (field.AllowSubTypes)
                        {
                            fieldFlags |= kFieldAllowSubTypes;
                        }
                        bodyWriter.Write(fieldFlags);
                        WriteNullableString(
                            bodyWriter,
                            string.IsNullOrEmpty(field.ArrayDimensions) ? null : field.ArrayDimensions);
                    }
                }
            }
            writer.Write(kStructureFieldTrailerVersion);
            writer.Write((int)body.Length);
            writer.Write(body.GetBuffer(), 0, (int)body.Length);
        }

        private void ReadStructureFieldTrailer(BinaryReader reader)
        {
            byte trailerVersion = reader.ReadByte();
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid structure field trailer length " + length);
            }
            if (trailerVersion != kStructureFieldTrailerVersion)
            {
                // Length prefixed: a later version is skipped as a whole.
                reader.BaseStream.Seek(length, SeekOrigin.Current);
                return;
            }

            int nodeCount = reader.ReadInt32();
            if (nodeCount != Nodes.Count)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid structure field node count " + nodeCount);
            }
            for (int i = 0; i < nodeCount; i++)
            {
                DependencyNode node = Nodes[i];
                int fieldCount = reader.ReadInt32();
                if (fieldCount != node.Fields.Count)
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid structure field count " + fieldCount);
                }
                if (fieldCount == 0)
                {
                    continue;
                }
                var fields = new DependencyDataField[fieldCount];
                for (int j = 0; j < fieldCount; j++)
                {
                    byte fieldFlags = reader.ReadByte();
                    fields[j] = node.Fields[j] with
                    {
                        IsOptional = (fieldFlags & kFieldIsOptional) != 0,
                        AllowSubTypes = (fieldFlags & kFieldAllowSubTypes) != 0,
                        ArrayDimensions = ReadNullableString(reader)
                    };
                }
                node.Fields = fields;
            }
        }

        private bool HasExtendedIdentifierTrailer()
        {
            foreach (DependencyNode node in Nodes)
            {
                // Null, not empty, means "not assigned" for both: an empty
                // GuidId has to be written or it reads back as null.
                if (node.GuidId != null ||
                    node.OpaqueId != null)
                {
                    return true;
                }
            }
            return false;
        }

        private void WriteExtendedIdentifierTrailer(BinaryWriter writer)
        {
            writer.Write(kExtendedIdentifierTrailerVersion);
            writer.Write(Nodes.Count);
            foreach (DependencyNode node in Nodes)
            {
                WriteNullableString(writer, node.GuidId);
                WriteNullableString(writer, node.OpaqueId);
            }
        }

        /// <summary>
        /// Reads the extended identifier trailer. Returns false when the
        /// trailer has a version this reader does not know - its body is
        /// then left unread and no later trailer can be located.
        /// </summary>
        private bool ReadExtendedIdentifierTrailer(BinaryReader reader)
        {
            byte trailerVersion = reader.ReadByte();
            if (trailerVersion != kExtendedIdentifierTrailerVersion)
            {
                return false;
            }

            int nodeCount = reader.ReadInt32();
            if (nodeCount != Nodes.Count)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid extended identifier node count " + nodeCount);
            }
            for (int i = 0; i < nodeCount; i++)
            {
                Nodes[i].GuidId = ReadNullableString(reader);
                Nodes[i].OpaqueId = ReadNullableString(reader);
            }
            return true;
        }

        private bool HasMethodIdentityTrailer()
        {
            foreach (DependencyNode node in Nodes)
            {
                foreach (DependencyChild child in node.Children)
                {
                    // Every field the trailer carries, or a child whose only
                    // method identity is a namespace does not round-trip.
                    if (!string.IsNullOrEmpty(child.MethodStateName) ||
                        !string.IsNullOrEmpty(child.MethodStateNamespace) ||
                        !string.IsNullOrEmpty(child.MethodDeclarationName) ||
                        !string.IsNullOrEmpty(child.MethodDeclarationNamespace) ||
                        child.MethodDeclarationNumericId != 0 ||
                        !string.IsNullOrEmpty(child.MethodDeclarationStringId))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void WriteMethodIdentityTrailer(BinaryWriter writer)
        {
            writer.Write(kMethodIdentityTrailerVersion);
            writer.Write(Nodes.Count);
            foreach (DependencyNode node in Nodes)
            {
                writer.Write(node.Children.Count);
                foreach (DependencyChild child in node.Children)
                {
                    WriteString(writer, child.MethodStateName);
                    WriteString(writer, child.MethodStateNamespace);
                    WriteString(writer, child.MethodDeclarationName);
                    WriteString(writer, child.MethodDeclarationNamespace);
                    writer.Write(child.MethodDeclarationNumericId);
                    WriteNullableString(writer, child.MethodDeclarationStringId);
                }
            }
        }

        /// <summary>
        /// Reads the method identity trailer. Returns false when the trailer
        /// has a version this reader does not know - its body is then left
        /// unread and no later trailer can be located.
        /// </summary>
        private bool ReadMethodIdentityTrailer(BinaryReader reader)
        {
            byte trailerVersion = reader.ReadByte();
            if (trailerVersion != kMethodIdentityTrailerVersion)
            {
                return false;
            }

            int nodeCount = reader.ReadInt32();
            if (nodeCount != Nodes.Count)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid method identity node count " + nodeCount);
            }
            for (int i = 0; i < nodeCount; i++)
            {
                DependencyNode node = Nodes[i];
                int childCount = reader.ReadInt32();
                if (childCount != node.Children.Count)
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid method identity child count " + childCount);
                }
                for (int j = 0; j < childCount; j++)
                {
                    DependencyChild child = node.Children[j];
                    child.MethodStateName = ReadString(reader);
                    child.MethodStateNamespace = ReadString(reader);
                    child.MethodDeclarationName = ReadString(reader);
                    child.MethodDeclarationNamespace = ReadString(reader);
                    child.MethodDeclarationNumericId = reader.ReadUInt32();
                    child.MethodDeclarationStringId = ReadNullableString(reader);
                }
            }
            return true;
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value ?? string.Empty);
        }

        private static void ValidateDefaultValueXml(string value)
        {
            try
            {
                using var textReader = new StringReader(value);
                using var reader = XmlReader.Create(
                    textReader,
                    CoreUtils.DefaultXmlReaderSettings());
                var document = new XmlDocument();
                document.Load(reader);
                if (document.DocumentElement == null)
                {
                    throw new XmlException("Default value XML has no document element.");
                }
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException(
                    "ModelDependencyV1: invalid default value XML.",
                    ex);
            }
        }

        private static void WriteNullableString(BinaryWriter writer, string? value)
        {
            if (value == null)
            {
                writer.Write((byte)0);
            }
            else
            {
                writer.Write((byte)1);
                writer.Write(value);
            }
        }

        private static string ReadString(BinaryReader reader)
        {
            return reader.ReadString();
        }

        private static string? ReadNullableString(BinaryReader reader)
        {
            byte marker = reader.ReadByte();
            return marker == 0 ? null : reader.ReadString();
        }
    }
}
