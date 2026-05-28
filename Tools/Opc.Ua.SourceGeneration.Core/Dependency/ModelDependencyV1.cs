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

#nullable enable

namespace Opc.Ua.SourceGeneration.Dependency
{
    /// <summary>
    /// Kind of node carried in a <see cref="ModelDependencyV1"/>.
    /// </summary>
    public enum DependencyNodeKind : byte
    {
        /// <summary>Unknown — invalid in a well-formed dependency payload.</summary>
        Unknown = 0,
        /// <summary>ObjectType.</summary>
        ObjectType = 1,
        /// <summary>VariableType.</summary>
        VariableType = 2,
        /// <summary>ReferenceType.</summary>
        ReferenceType = 3,
        /// <summary>DataType.</summary>
        DataType = 4,
        /// <summary>Method (when carried as standalone declaration).</summary>
        Method = 5,
    }

    /// <summary>
    /// Reduced view of a <c>DataTypeDesign</c> field carried in a dependency payload.
    /// </summary>
    public readonly struct DependencyDataField
    {
        /// <summary>Field name.</summary>
        public string Name { get; }
        /// <summary>DataType name (qualified).</summary>
        public string DataTypeName { get; }
        /// <summary>DataType namespace URI.</summary>
        public string DataTypeNamespace { get; }
        /// <summary>Value rank (<see cref="ValueRanks"/>).</summary>
        public int ValueRank { get; }

        /// <summary>Constructor.</summary>
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
    public readonly struct DependencyMethodArg
    {
        /// <summary>Argument name.</summary>
        public string Name { get; }
        /// <summary>DataType name (qualified).</summary>
        public string DataTypeName { get; }
        /// <summary>DataType namespace URI.</summary>
        public string DataTypeNamespace { get; }
        /// <summary>Value rank.</summary>
        public int ValueRank { get; }

        /// <summary>Constructor.</summary>
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
        /// <summary>Browse name of the child.</summary>
        public string BrowseName { get; set; } = string.Empty;
        /// <summary>Symbolic name (often equal to BrowseName).</summary>
        public string SymbolicName { get; set; } = string.Empty;
        /// <summary>TypeDefinition name (for all kinds). Empty when not declared.</summary>
        public string TypeDefinitionName { get; set; } = string.Empty;
        /// <summary>TypeDefinition namespace URI.</summary>
        public string TypeDefinitionNamespace { get; set; } = string.Empty;
        /// <summary>DataType name (variables only). Empty when not applicable.</summary>
        public string DataTypeName { get; set; } = string.Empty;
        /// <summary>DataType namespace URI (variables only).</summary>
        public string DataTypeNamespace { get; set; } = string.Empty;
        /// <summary>Value rank (variables only; <c>Scalar = 0</c> per <c>Opc.Ua.ValueRanks</c>).</summary>
        public int ValueRank { get; set; }
        /// <summary>Modelling rule (0=None, 1=Mandatory, 2=Optional, 3=OptionalPlaceholder, 4=MandatoryPlaceholder, 5=ExposesItsArray).</summary>
        public byte ModellingRule { get; set; }
        /// <summary>Instance kind: 1=Object 2=Variable 3=Property 4=Method.</summary>
        public byte InstanceKind { get; set; }
        /// <summary>Input arguments (methods only).</summary>
        public IReadOnlyList<DependencyMethodArg> InputArguments { get; set; } = Array.Empty<DependencyMethodArg>();
        /// <summary>Output arguments (methods only).</summary>
        public IReadOnlyList<DependencyMethodArg> OutputArguments { get; set; } = Array.Empty<DependencyMethodArg>();
    }

    /// <summary>
    /// Reduced view of a node entry carried in a dependency payload. One entry per
    /// node the producing assembly emits as a public type definition
    /// (objects/instances are not carried; consumers re-derive
    /// inheritance via <see cref="BaseTypeName"/>).
    /// </summary>
    public sealed class DependencyNode
    {
        /// <summary>Symbolic name.</summary>
        public string SymbolicName { get; set; } = string.Empty;
        /// <summary>Symbolic namespace URI.</summary>
        public string SymbolicNamespace { get; set; } = string.Empty;
        /// <summary>Emitted C# class name (post-Type-suffix-stripping).</summary>
        public string ClassName { get; set; } = string.Empty;
        /// <summary>Node kind.</summary>
        public DependencyNodeKind Kind { get; set; }
        /// <summary>Base type name (null for root types).</summary>
        public string? BaseTypeName { get; set; }
        /// <summary>Base type namespace URI (null for root types).</summary>
        public string? BaseTypeNamespace { get; set; }
        /// <summary>Numeric NodeId (0 when not assigned).</summary>
        public uint NumericId { get; set; }
        /// <summary>Optional string NodeId.</summary>
        public string? StringId { get; set; }
        /// <summary>True when the type is abstract.</summary>
        public bool IsAbstract { get; set; }
        /// <summary>True when the entry represents an enumeration DataType.</summary>
        public bool IsEnumeration { get; set; }
        /// <summary>DataType fields (empty for non-DataType kinds).</summary>
        public IReadOnlyList<DependencyDataField> Fields { get; set; } = Array.Empty<DependencyDataField>();
        /// <summary>Declared instance children (empty for DataType / no-child types).</summary>
        public IReadOnlyList<DependencyChild> Children { get; set; } = Array.Empty<DependencyChild>();
    }

    /// <summary>
    /// In-memory representation of a <c>ModelDependencyV1</c> payload.
    /// </summary>
    public sealed class ModelDependencyV1
    {
        /// <summary>The magic byte sequence.</summary>
        public static readonly byte[] Magic = [0xAA, 0xC7];

        /// <summary>The version byte for V1.</summary>
        public const byte Version = 1;

        /// <summary>Compression scheme: 1 = Deflate.</summary>
        public const byte CompressionDeflate = 1;

        /// <summary>The model URI this dependency payload describes.</summary>
        public string ModelUri { get; set; } = string.Empty;

        /// <summary>Nodes in the dependency payload.</summary>
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
            if (string.IsNullOrEmpty(base64)) { return null; }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return null;
            }
            if (bytes.Length < 4) { return null; }
            if (bytes[0] != Magic[0] || bytes[1] != Magic[1]) { return null; }
            if (bytes[2] != Version) { return null; }
            if (bytes[3] != CompressionDeflate) { return null; }

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
                if (node.IsAbstract) { flags |= 0x01; }
                if (node.IsEnumeration) { flags |= 0x02; }
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
                    WriteString(writer, child.BrowseName);
                    WriteString(writer, child.SymbolicName);
                    WriteString(writer, child.TypeDefinitionName);
                    WriteString(writer, child.TypeDefinitionNamespace);
                    WriteString(writer, child.DataTypeName);
                    WriteString(writer, child.DataTypeNamespace);
                    writer.Write(child.ValueRank);
                    writer.Write(child.ModellingRule);
                    writer.Write(child.InstanceKind);
                    writer.Write(child.InputArguments.Count);
                    foreach (DependencyMethodArg a in child.InputArguments)
                    {
                        WriteString(writer, a.Name);
                        WriteString(writer, a.DataTypeName);
                        WriteString(writer, a.DataTypeNamespace);
                        writer.Write(a.ValueRank);
                    }
                    writer.Write(child.OutputArguments.Count);
                    foreach (DependencyMethodArg a in child.OutputArguments)
                    {
                        WriteString(writer, a.Name);
                        WriteString(writer, a.DataTypeName);
                        WriteString(writer, a.DataTypeNamespace);
                        writer.Write(a.ValueRank);
                    }
                }
            }
        }

        private void ReadUncompressed(Stream source)
        {
            using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
            ModelUri = ReadString(reader);
            int typeCount = reader.ReadInt32();
            if (typeCount < 0 || typeCount > 1_000_000)
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
                    StringId = ReadNullableString(reader),
                };
                byte flags = reader.ReadByte();
                node.IsAbstract = (flags & 0x01) != 0;
                node.IsEnumeration = (flags & 0x02) != 0;
                int fieldCount = reader.ReadInt32();
                if (fieldCount < 0 || fieldCount > 100_000)
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
                if (childCount < 0 || childCount > 100_000)
                {
                    throw new InvalidDataException(
                        "ModelDependencyV1: invalid child count " + childCount);
                }
                if (childCount > 0)
                {
                    var children = new DependencyChild[childCount];
                    for (int j = 0; j < childCount; j++)
                    {
                        var c = new DependencyChild
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
                        int inCount = reader.ReadInt32();
                        if (inCount < 0 || inCount > 100)
                        {
                            throw new InvalidDataException(
                                "ModelDependencyV1: invalid input arg count " + inCount);
                        }
                        if (inCount > 0)
                        {
                            var args = new DependencyMethodArg[inCount];
                            for (int k = 0; k < inCount; k++)
                            {
                                args[k] = new DependencyMethodArg(
                                    ReadString(reader),
                                    ReadString(reader),
                                    ReadString(reader),
                                    reader.ReadInt32());
                            }
                            c.InputArguments = args;
                        }
                        int outCount = reader.ReadInt32();
                        if (outCount < 0 || outCount > 100)
                        {
                            throw new InvalidDataException(
                                "ModelDependencyV1: invalid output arg count " + outCount);
                        }
                        if (outCount > 0)
                        {
                            var args = new DependencyMethodArg[outCount];
                            for (int k = 0; k < outCount; k++)
                            {
                                args[k] = new DependencyMethodArg(
                                    ReadString(reader),
                                    ReadString(reader),
                                    ReadString(reader),
                                    reader.ReadInt32());
                            }
                            c.OutputArguments = args;
                        }
                        children[j] = c;
                    }
                    node.Children = children;
                }
                Nodes.Add(node);
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value ?? string.Empty);
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
