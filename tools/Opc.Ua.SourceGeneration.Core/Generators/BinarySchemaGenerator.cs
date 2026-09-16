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
using System.IO;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates binary schema files from model designs.
    /// </summary>
    internal sealed class BinarySchemaGenerator : IGenerator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BinarySchemaGenerator"/> class.
        /// </summary>
        public BinarySchemaGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Validate schema output after generation.
        /// </summary>
        public bool ValidateOutput { get; set; }

        /// <summary>
        /// Generates the binary schema file for the supplied nodes.
        /// </summary>
        public IEnumerable<Resource> Emit()
        {
            string namespacePrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string schemaFile = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format("{0}.Types.bsd", namespacePrefix));

            WriteTemplate_BinarySchema(schemaFile);

            if (ValidateOutput)
            {
                // Validate generated file
                var validator = new Schema.Binary.BinarySchemaValidator(m_context.FileSystem);
                validator.Validate(schemaFile);
            }

            return [schemaFile.AsTextFileResource(namespacePrefix)];
        }

        public void WriteTemplate_BinarySchema(string fileName)
        {
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, BinarySchemaTemplates.File);

            string targetNamespace = m_context.ModelDesign.TargetNamespace.Value;

            template.AddReplacement(Tokens.DictionaryUri, targetNamespace);

            template.AddReplacement(
                Tokens.XmlnsS0ListOfNamespaces,
                m_context.ModelDesign.Namespaces,
                LoadTemplate_Imports);

            template.AddReplacement(
                Tokens.Imports,
                m_context.ModelDesign.Namespaces,
                LoadTemplate_Imports);

            template.AddReplacement(
                Tokens.BuiltInTypes,
                BinarySchemaTemplates.BuiltInTypes,
                [m_context.ModelDesign],
                LoadTemplate_DataType,
                WriteTemplate_DataType);

            template.AddReplacement(
                Tokens.ListOfTypes,
                BinarySchemaTemplates.OpaqueType,
                GetListOfTypes(),
                LoadTemplate_DataType,
                WriteTemplate_DataType);

            template.Render();
        }

        private TemplateString LoadTemplate_Imports(ILoadContext context)
        {
            if (context.Target is not Namespace ns)
            {
                return null;
            }

            if (ns.Value == m_context.ModelDesign.TargetNamespace.Value)
            {
                return null;
            }

            if (context.Token == Tokens.XmlnsS0ListOfNamespaces)
            {
                if (ns.Value == Namespaces.OpcUa)
                {
                    return null;
                }

                context.Out.WriteLine(
                    """
                    xmlns:{0}="{1}"
                    """,
                    m_context.ModelDesign.Namespaces.GetXmlNamespacePrefix(ns.Value),
                    ns.Value);
                return null;
            }

            context.Out.WriteLine(
                "<opc:Import Namespace=\"{0}\" Location=\"{1}.BinarySchema.bsd\"/>",
                ns.Value,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(ns.Value));

            return null;
        }

        private TemplateString LoadTemplate_DataType(ILoadContext context)
        {
            if (context.Target is IModelDesign design)
            {
                if (design.TargetNamespace.Value == Namespaces.OpcUa)
                {
                    return context.TemplateString;
                }

                return null;
            }

            if (context.Target is not DataTypeDesign dataType)
            {
                return null;
            }

#if TRUE
            // don't write built-in types already in the template.
            if (dataType.NumericId < 256 &&
                dataType.SymbolicId.Namespace == Namespaces.OpcUa)
            {
                switch (dataType.NumericId)
                {
                    case DataTypes.PermissionType:
                    case DataTypes.AccessRestrictionType:
                    case DataTypes.RolePermissionType:
                    case DataTypes.StructureDefinition:
                    case DataTypes.StructureField:
                    case DataTypes.StructureType:
                    case DataTypes.EnumDefinition:
                    case DataTypes.EnumField:
                    case DataTypes.DataTypeDefinition:
                    case DataTypes.Enumeration:
                    case DataTypes.Union:
                        break;
                    default:
                        return null;
                }
            }
#endif

            if (dataType.Purpose == DataTypePurpose.CodeGenerator)
            {
                return null;
            }

            BasicDataType basicType = dataType.BasicDataType;

            if (basicType == BasicDataType.UserDefined)
            {
                return BinarySchemaTemplates.ComplexType;
            }

            if (basicType == BasicDataType.Enumeration)
            {
                return BinarySchemaTemplates.EnumeratedType;
            }

            return BinarySchemaTemplates.OpaqueType;
        }

        private bool WriteTemplate_DataType(IWriteContext context)
        {
            if (context.Target is IModelDesign design)
            {
                if (design.TargetNamespace.Value == Namespaces.OpcUa)
                {
                    return context.Template.Render();
                }

                return false;
            }

            if (context.Target is not DataTypeDesign dataType)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.TypeName, dataType.SymbolicName.Name);

            if (dataType.BasicDataType == BasicDataType.UserDefined)
            {
                context.Template.AddReplacement(Tokens.BaseType,
                    (dataType.BaseTypeNode as DataTypeDesign).GetBinaryDataType(
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces));
            }

            List<Parameter> fields = [];
            var parents = new Stack<DataTypeDesign>();

            for (DataTypeDesign parent = dataType;
                parent != null;
                parent = parent.BaseTypeNode as DataTypeDesign)
            {
                if (parent.Fields != null)
                {
                    parents.Push(parent);
                }
            }

            while (parents.Count > 0)
            {
                DataTypeDesign parent = parents.Pop();

                foreach (Parameter field in parent.Fields)
                {
                    if (m_context.ModelDesign.IsExcluded(field))
                    {
                        continue;
                    }

                    if (ReferenceEquals(dataType, parent))
                    {
                        fields.Add(field);
                        continue;
                    }

                    fields.Add(new Parameter
                    {
                        DataType = field.DataType,
                        DataTypeNode = field.DataTypeNode,
                        Description = field.Description,
                        Identifier = field.Identifier,
                        IdentifierInName = field.IdentifierInName,
                        IdentifierSpecified = field.IdentifierSpecified,
                        IsInherited = true,
                        Name = field.Name,
                        Parent = field.Parent,
                        ValueRank = field.ValueRank,
                        ArrayDimensions = field.ArrayDimensions,
                        AllowSubTypes = field.AllowSubTypes,
                        IsOptional = field.IsOptional,
                        BitMask = field.BitMask,
                        DefaultValue = field.DefaultValue,
                        ReleaseStatus = field.ReleaseStatus
                    });
                }
            }

            if (dataType.BasicDataType == BasicDataType.Enumeration)
            {
                uint lengthInBits = 32;
                bool isOptionSet = false;

                if (dataType.IsOptionSet)
                {
                    isOptionSet = true;

                    switch (dataType.BaseType.Name)
                    {
                        case "SByte":
                        case "Byte":
                            lengthInBits = 8;
                            break;
                        case "Int16":
                        case "UInt16":
                            lengthInBits = 16;
                            break;
                        case "Int32":
                        case "UInt32":
                            lengthInBits = 32;
                            break;
                        case "Int64":
                        case "UInt64":
                            lengthInBits = 64;
                            break;
                    }

                    fields.Insert(0, new Parameter
                    {
                        Name = "None",
                        Identifier = 0,
                        IdentifierSpecified = true,
                        DataType = fields[0].DataType,
                        DataTypeNode = fields[0].DataTypeNode,
                        Parent = fields[0].Parent
                    });
                }

                context.Template.AddReplacement(Tokens.LengthInBits, lengthInBits);
                context.Template.AddReplacement(
                    Tokens.IsOptionSet,
                    isOptionSet ? " IsOptionSet=\"true\"" : string.Empty);
            }

            context.Template.AddReplacement(
                Tokens.Documentation,
                [dataType],
                LoadTemplate_BinaryDocumentation);

            context.Template.AddReplacement(
                Tokens.ListOfFields,
                dataType.BasicDataType == BasicDataType.UserDefined
                    ? BuildStructureFields(dataType, fields)
                    : fields,
                LoadTemplate_Field);

            return context.Template.Render();
        }

        /// <summary>
        /// Expands the declared fields of a structure into the field sequence the
        /// binary encoding actually puts on the wire. A union is prefixed with its
        /// 32-bit <c>SwitchField</c> and every member selects on it; a structure
        /// with optional fields is prefixed with the presence bits of the encoding
        /// mask (padded to 32 bits) and every optional member selects on its bit.
        /// Without this the served DataTypeDictionary describes a different layout
        /// than the generated Encode/Decode produces.
        /// </summary>
        private static List<object> BuildStructureFields(
            DataTypeDesign dataType,
            List<Parameter> fields)
        {
            bool isUnion = dataType.IsUnion;
            var expanded = new List<object>(fields.Count + 2);

            if (isUnion)
            {
                expanded.Add(new BinaryField("SwitchField", "opc:UInt32"));
            }
            else
            {
                int optionalCount = 0;
                foreach (Parameter field in fields)
                {
                    if (field.IsOptional)
                    {
                        expanded.Add(new BinaryField(
                            field.Name + kSpecifiedSuffix, "opc:Bit"));
                        optionalCount++;
                    }
                }

                // Same limit the generated encoder works to: the mask is 32 bits
                // wide, so a 33rd optional field has no presence bit.
                if (optionalCount > kEncodingMaskBits)
                {
                    throw new InvalidOperationException(CoreUtils.Format(
                        "Data type '{0}' declares {1} optional fields. The binary encoding mask is only {2} bits wide.",
                        dataType.SymbolicName?.Name,
                        optionalCount,
                        kEncodingMaskBits));
                }

                if (optionalCount > 0 && optionalCount < kEncodingMaskBits)
                {
                    expanded.Add(new BinaryField(
                        "Reserved1",
                        "opc:Bit",
                        length: (uint)(kEncodingMaskBits - optionalCount)));
                }
            }

            for (int ii = 0; ii < fields.Count; ii++)
            {
                Parameter field = fields[ii];
                if (isUnion)
                {
                    expanded.Add(new BinaryField(
                        field,
                        "SwitchField",
                        switchValue: (uint)(ii + 1)));
                }
                else if (field.IsOptional)
                {
                    expanded.Add(new BinaryField(
                        field,
                        field.Name + kSpecifiedSuffix,
                        switchValue: null));
                }
                else
                {
                    expanded.Add(new BinaryField(field, null, null));
                }
            }

            return expanded;
        }

        private TemplateString LoadTemplate_Field(ILoadContext context)
        {
            if (context.Target is BinaryField binaryField)
            {
                if (binaryField.Field == null)
                {
                    // A synthetic wire-only field: the union selector, an encoding
                    // mask presence bit or the reserved padding of the mask.
                    context.Out.WriteLine(
                        "<opc:Field Name=\"{0}\" TypeName=\"{1}\"{2} />",
                        binaryField.Name.AsXmlAttributeValue(),
                        binaryField.TypeName,
                        binaryField.Length > 0
                            ? CoreUtils.Format(" Length=\"{0}\"", binaryField.Length)
                            : string.Empty);
                    return null;
                }

                WriteStructureField(context, binaryField);
                return null;
            }

            if (context.Target is not Parameter field)
            {
                return null;
            }

            if (field.Parent is not DataTypeDesign dataType)
            {
                return null;
            }

            if (dataType.BasicDataType == BasicDataType.Enumeration)
            {
                context.Out.WriteLine(
                    "<opc:EnumeratedValue Name=\"{0}\" Value=\"{1}\" />",
                    field.Name.AsXmlAttributeValue(),
                    field.Identifier);
            }

            return null;
        }

        /// <summary>
        /// Writes the <c>opc:Field</c> element(s) for one declared structure field,
        /// carrying the switch attributes that tie an optional or union member to
        /// the presence bit / selector that precedes it.
        /// </summary>
        private void WriteStructureField(ILoadContext context, BinaryField binaryField)
        {
            Parameter field = binaryField.Field;

            // The authored field name lands in XML attributes, so it has to be
            // escaped - a BrowseName may legally contain '&', '<' or a quote,
            // which would otherwise make the served dictionary non-well-formed.
            string fieldName = field.Name.AsXmlAttributeValue();

            string fieldDataType = field.DataTypeNode.GetBinaryDataType(
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces);

            if (field.AllowSubTypes)
            {
                fieldDataType = "ua:ExtensionObject";
            }

            string switchAttributes = string.Empty;
            if (!string.IsNullOrEmpty(binaryField.SwitchField))
            {
                switchAttributes = CoreUtils.Format(
                    " SwitchField=\"{0}\"",
                    binaryField.SwitchField.AsXmlAttributeValue());
                if (binaryField.SwitchValue.HasValue)
                {
                    switchAttributes += CoreUtils.Format(
                        " SwitchValue=\"{0}\"",
                        binaryField.SwitchValue.Value);
                }
            }

            if (field.ValueRank != ValueRank.Scalar)
            {
                context.Out.WriteLine(
                    "<opc:Field Name=\"NoOf{0}\" TypeName=\"opc:Int32\"{1} />",
                    fieldName,
                    switchAttributes);
                context.Out.WriteLine(
                    "<opc:Field Name=\"{0}\" TypeName=\"{1}\" LengthField=\"NoOf{0}\"{2} />",
                    fieldName,
                    fieldDataType,
                    switchAttributes);
                return;
            }

            if (field.IsInherited)
            {
                context.Out.WriteLine(
                    "<opc:Field Name=\"{0}\" TypeName=\"{1}\" SourceType=\"{2}\"{3} />",
                    fieldName,
                    fieldDataType,
                    (field.Parent as DataTypeDesign).GetBinaryDataType(
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces),
                    switchAttributes);
                return;
            }

            context.Out.WriteLine(
                "<opc:Field Name=\"{0}\" TypeName=\"{1}\"{2} />",
                fieldName,
                fieldDataType,
                switchAttributes);
        }

        /// <summary>
        /// One entry of a structure's binary field sequence: either a declared
        /// field together with the switch it selects on, or a wire-only field the
        /// binary encoding inserts (union selector, presence bit, mask padding).
        /// </summary>
        private sealed class BinaryField
        {
            public BinaryField(string name, string typeName, uint length = 0)
            {
                Name = name;
                TypeName = typeName;
                Length = length;
            }

            public BinaryField(Parameter field, string switchField, uint? switchValue)
            {
                Field = field;
                Name = field.Name;
                SwitchField = switchField;
                SwitchValue = switchValue;
            }

            public Parameter Field { get; }
            public string Name { get; }
            public string TypeName { get; }
            public uint Length { get; }
            public string SwitchField { get; }
            public uint? SwitchValue { get; }
        }

        /// <summary>
        /// Number of presence bits in the binary encoding mask of a structure
        /// with optional fields.
        /// </summary>
        private const int kEncodingMaskBits = 32;

        /// <summary>
        /// Suffix of the presence bit that gates an optional field.
        /// </summary>
        private const string kSpecifiedSuffix = "Specified";

        private TemplateString LoadTemplate_BinaryDocumentation(ILoadContext context)
        {
            if (context.Target is not DataTypeDesign dataType)
            {
                return null;
            }

            if (dataType.Description == null ||
                dataType.Description.IsAutogenerated)
            {
                return null;
            }

            context.Out.WriteLine(
                "<opc:Documentation>{0}</opc:Documentation>",
                dataType.Description.Value.AsXmlText());

            return context.TemplateString;
        }

        private IReadOnlyList<NodeDesign> GetListOfTypes()
        {
            return [.. m_context.ModelDesign.GetNodeDesigns()];
        }

        private readonly IGeneratorContext m_context;
    }
}
