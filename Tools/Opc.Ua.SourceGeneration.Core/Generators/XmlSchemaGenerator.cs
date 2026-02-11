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
using System.Xml;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates XML schema files from model designs.
    /// </summary>
    internal sealed class XmlSchemaGenerator : IGenerator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="XmlSchemaGenerator"/> class.
        /// </summary>
        public XmlSchemaGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Validate schema output after generation.
        /// </summary>
        public bool ValidateOutput { get; set; }

        /// <summary>
        /// Generates the XML schema file for the supplied nodes.
        /// </summary>
        public IEnumerable<Resource> Emit()
        {
            string namespacePrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string schemaFile = Path.Combine(m_context.OutputFolder, CoreUtils.Format(
                "{0}.Types.xsd",
                namespacePrefix));

            WriteTemplate_XmlSchema(schemaFile);

            if (ValidateOutput)
            {
                // Validate generated file
                var validator = new Schema.Xml.XmlSchemaValidator2(
                    m_context.FileSystem,
                    []);
                validator.Validate(schemaFile);
            }

            return [schemaFile.AsTextFileResource(namespacePrefix)];
        }

        private void WriteTemplate_XmlSchema(string fileName)
        {
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, XmlSchemaTemplates.File);

            if (!string.IsNullOrEmpty(m_context.ModelDesign.TargetNamespace.XmlNamespace))
            {
                template.AddReplacement(
                    Tokens.Namespace,
                    m_context.ModelDesign.TargetNamespace.XmlNamespace);
            }
            else
            {
                template.AddReplacement(
                    Tokens.Namespace,
                    m_context.ModelDesign.TargetNamespace.Value);
            }

            template.AddReplacement(Tokens.TargetVersion, m_context.ModelDesign.TargetVersion);
            template.AddReplacement(Tokens.ModelUri, m_context.ModelDesign.TargetNamespace.Value);
            template.AddReplacement(Tokens.TargetPublicationDate, XmlConvert.ToString(
                m_context.ModelDesign.TargetPublicationDate ?? DateTime.MinValue,
                XmlDateTimeSerializationMode.Utc));

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
                XmlSchemaTemplates.BuiltInTypes,
                [m_context.ModelDesign],
                LoadTemplate_DataType,
                WriteTemplate_DataType);

            template.AddReplacement(
                Tokens.ListOfTypes,
                [.. m_context.ModelDesign.GetNodeDesigns()],
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

            string uri = ns.Value;
            if (!string.IsNullOrEmpty(ns.XmlNamespace))
            {
                uri = ns.XmlNamespace;
            }

            if (context.Token == Tokens.XmlnsS0ListOfNamespaces)
            {
                if (ns.Value == Namespaces.OpcUa)
                {
                    return null;
                }

                context.Out.WriteLine(
                    "xmlns:{0}=\"{1}\"",
                    m_context.ModelDesign.Namespaces.GetXmlNamespacePrefix(ns.Value),
                    uri);

                return null;
            }

            context.Out.WriteLine("<xs:import namespace=\"{0}\" />", uri);

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
                    case DataTypes.RolePermissionType:
                    case DataTypes.DataTypeDefinition:
                    case DataTypes.StructureDefinition:
                    case DataTypes.StructureField:
                    case DataTypes.StructureType:
                    case DataTypes.EnumDefinition:
                    case DataTypes.EnumField:
                        break;
                    default:
                        return null;
                }
            }
#endif

            BasicDataType basicType = dataType.BasicDataType;

            if (basicType == BasicDataType.Enumeration)
            {
                var baseType = dataType.BaseTypeNode as DataTypeDesign;

                if (baseType?.SymbolicId == new XmlQualifiedName("OptionSet", Namespaces.OpcUa))
                {
                    return XmlSchemaTemplates.DerivedType;
                }

                return XmlSchemaTemplates.EnumeratedType;
            }
            else if (basicType == BasicDataType.UserDefined)
            {
                if (dataType.BaseTypeNode.SymbolicName.Name == "Union")
                {
                    return XmlSchemaTemplates.Union;
                }
                else if (dataType.BaseTypeNode.SymbolicName.Name == "Structure")
                {
                    return XmlSchemaTemplates.ComplexType;
                }
                else
                {
                    return XmlSchemaTemplates.DerivedType;
                }
            }

            return XmlSchemaTemplates.SimpleType;
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

            var baseType = dataType.BaseTypeNode as DataTypeDesign;

            if (baseType != null)
            {
                context.Template.AddReplacement(Tokens.BaseType, baseType.GetXmlDataType(
                    ValueRank.Scalar,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces));
            }

            context.Template.AddReplacement(Tokens.TypeName, dataType.SymbolicName.Name);

            if (dataType.BasicDataType == BasicDataType.Enumeration && dataType.IsOptionSet)
            {
                context.Template.AddReplacement(Tokens.XsRestrictionBaseType,
                    baseType.GetXmlDataType(
                        ValueRank.Scalar,
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces));
            }
            else
            {
                context.Template.AddReplacement(Tokens.XsRestrictionBaseType, "xs:string");
            }

            context.Template.AddReplacement(
                Tokens.Documentation,
                XmlSchemaTemplates.Documentation,
                [dataType],
                LoadTemplate_XmlDocumentation,
                WriteTemplate_XmlDocumentation);

            context.Template.AddReplacement(
                Tokens.CollectionType,
                XmlSchemaTemplates.CollectionType,
                [dataType],
                LoadTemplate_XmlCollectionType,
                WriteTemplate_XmlCollectionType);

            context.Template.AddReplacement(
                Tokens.ListOfFields,
                dataType.Fields,
                LoadTemplate_XmlTypeFields);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_XmlTypeFields(ILoadContext context)
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

            if (basicType == BasicDataType.Enumeration)
            {
                if (dataType.IsOptionSet)
                {
                    return null;
                }

                if (field.IdentifierInName)
                {
                    context.Out.WriteLine(
                        "<xs:enumeration value=\"{0}\" />",
                        field.Name);
                    return null;
                }

                context.Out.WriteLine(
                    "<xs:enumeration value=\"{0}_{1}\" />",
                    field.Name,
                    field.Identifier);
                return null;
            }

            basicType = field.DataTypeNode.BasicDataType;

            if (basicType == BasicDataType.XmlElement &&
                field.ValueRank == ValueRank.Scalar)
            {
                context.Out.WriteLine("<xs:element name=\"{0}\" minOccurs=\"0\" nillable=\"true\">", field.Name);
                context.Out.WriteLine("  <xs:complexType>");
                context.Out.WriteLine("    <xs:sequence>");
                context.Out.WriteLine("      <xs:any minOccurs=\"0\" processContents=\"lax\" />");
                context.Out.WriteLine("    </xs:sequence>");
                context.Out.WriteLine("  </xs:complexType>");
                context.Out.WriteLine("</xs:element>");
                return null;
            }

            if (field.ValueRank != ValueRank.Scalar)
            {
                string fieldDataType = field.DataTypeNode.GetXmlDataType(
                    field.ValueRank,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces);

                if (basicType == BasicDataType.UserDefined && field.AllowSubTypes)
                {
                    fieldDataType = "ua:ListOfExtensionObject";
                }

                context.Out.WriteLine(
                    "<xs:element name=\"{0}\" type=\"{1}\" minOccurs=\"0\" nillable=\"true\" />",
                    field.Name,
                    fieldDataType);
            }
            else
            {
                switch (basicType)
                {
                    case BasicDataType.String:
                    case BasicDataType.ByteString:
                    case BasicDataType.DiagnosticInfo:
                    case BasicDataType.ExpandedNodeId:
                    case BasicDataType.LocalizedText:
                    case BasicDataType.NodeId:
                    case BasicDataType.QualifiedName:
                    case BasicDataType.Structure:
                    case BasicDataType.DataValue:
                        context.Out.WriteLine(
                                "<xs:element name=\"{0}\" type=\"{1}\" minOccurs=\"0\" nillable=\"true\" />",
                                field.Name,
                                field.DataTypeNode.GetXmlDataType(
                                    field.ValueRank,
                                    m_context.ModelDesign.TargetNamespace.Value,
                                    m_context.ModelDesign.Namespaces));
                        break;
                    case BasicDataType.Guid:
                    case BasicDataType.StatusCode:
                        context.Out.WriteLine(
                                "<xs:element name=\"{0}\" type=\"{1}\" minOccurs=\"0\" />",
                                field.Name,
                                field.DataTypeNode.GetXmlDataType(
                                    field.ValueRank,
                                    m_context.ModelDesign.TargetNamespace.Value,
                                    m_context.ModelDesign.Namespaces));
                        break;
                    case BasicDataType.UserDefined:
                        string fieldDataType = field.DataTypeNode.GetXmlDataType(
                                field.ValueRank,
                                m_context.ModelDesign.TargetNamespace.Value,
                                m_context.ModelDesign.Namespaces);

                        if (field.AllowSubTypes)
                        {
                            fieldDataType = "ua:ExtensionObject";
                        }

                        context.Out.WriteLine(
                            "<xs:element name=\"{0}\" type=\"{1}\" minOccurs=\"0\" nillable=\"true\" />",
                            field.Name,
                            fieldDataType);
                        break;
                    default:
                        context.Out.WriteLine("<xs:element name=\"{0}\" type=\"{1}\" minOccurs=\"0\" />",
                                field.Name,
                                field.DataTypeNode.GetXmlDataType(
                                    field.ValueRank,
                                    m_context.ModelDesign.TargetNamespace.Value,
                                    m_context.ModelDesign.Namespaces));
                        break;
                }
            }

            return null;
        }

        private TemplateString LoadTemplate_XmlDocumentation(ILoadContext context)
        {
            if (context.Target is not DataTypeDesign dataType)
            {
                return null;
            }

            if (dataType.Description == null || dataType.Description.IsAutogenerated)
            {
                return null;
            }

            return context.TemplateString;
        }

        private bool WriteTemplate_XmlDocumentation(IWriteContext context)
        {
            if (context.Target is not DataTypeDesign dataType)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.Description, dataType.Description.Value);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_XmlCollectionType(ILoadContext context)
        {
            if (context.Target is not DataTypeDesign dataType)
            {
                return null;
            }

            if (dataType.NoArraysAllowed)
            {
                return null;
            }

            return context.TemplateString;
        }

        private bool WriteTemplate_XmlCollectionType(IWriteContext context)
        {
            if (context.Target is not DataTypeDesign dataType)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.TypeName, dataType.SymbolicName.Name);
            context.Template.AddReplacement(
                Tokens.Nillable,
                !dataType.BasicDataType.IsXmlNillable() ?
                    string.Empty : "nillable=\"true\" ");

            return context.Template.Render();
        }

        private readonly IGeneratorContext m_context;
    }
}
