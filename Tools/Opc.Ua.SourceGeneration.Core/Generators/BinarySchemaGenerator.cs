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
                fields,
                LoadTemplate_Field);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_Field(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }

            if (field.Parent is not DataTypeDesign dataType)
            {
                return null;
            }

            BasicDataType basicType = dataType.BasicDataType;

            string fieldDataType = field.DataTypeNode.GetBinaryDataType(
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces);

            if (field.AllowSubTypes)
            {
                fieldDataType = "ua:ExtensionObject";
            }

            if (basicType == BasicDataType.Enumeration)
            {
                context.Out.WriteLine(
                    "<opc:EnumeratedValue Name=\"{0}\" Value=\"{1}\" />",
                    field.Name,
                    field.Identifier);
                return null;
            }

            if (field.ValueRank != ValueRank.Scalar)
            {
                context.Out.WriteLine(
                    "<opc:Field Name=\"NoOf{0}\" TypeName=\"opc:Int32\" />",
                    field.Name);
                context.Out.WriteLine(
                    "<opc:Field Name=\"{0}\" TypeName=\"{1}\" LengthField=\"NoOf{0}\" />",
                    field.Name,
                    fieldDataType);
                return null;
            }
            if (field.IsInherited)
            {
                context.Out.WriteLine(
                    "<opc:Field Name=\"{0}\" TypeName=\"{1}\" SourceType=\"{2}\" />",
                    field.Name,
                    fieldDataType,
                    (field.Parent as DataTypeDesign).GetBinaryDataType(
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces));
            }
            else
            {
                context.Out.WriteLine(
                    "<opc:Field Name=\"{0}\" TypeName=\"{1}\" />",
                    field.Name,
                    fieldDataType);
            }

            return null;
        }

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
                dataType.Description.Value);

            return context.TemplateString;
        }

        private IReadOnlyList<NodeDesign> GetListOfTypes()
        {
            return [.. m_context.ModelDesign.GetNodeDesigns()];
        }

        private readonly IGeneratorContext m_context;
    }
}
