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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates data type model classes
    /// </summary>
    internal sealed class DataTypeGenerator : IGenerator
    {
        public DataTypeGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_messageContext = ServiceMessageContext.CreateEmpty(context.Telemetry);
            m_logger = context.Telemetry.CreateLogger<DataTypeGenerator>();
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            List<DataTypeDesign> datatypes = GetDataTypes();
            if (datatypes.Count == 0)
            {
                return [];
            }
            string nsPrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string fileName = Path.Combine(m_context.OutputFolder, CoreUtils.Format(
                "{0}.DataTypes.g.cs",
                nsPrefix));
            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);

            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, DataTypeTemplates.File);
            template.AddReplacement(
                Tokens.NamespacePrefix,
                nsPrefix);
            template.AddReplacement(
                Tokens.Namespace,
                nsPrefix.Replace(".", string.Empty, StringComparison.Ordinal));
            template.AddReplacement(
                Tokens.NamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    m_context.ModelDesign.TargetNamespace.Value));
            template.AddReplacement(
                Tokens.ListOfTypes,
                datatypes,
                LoadTemplate_ListOfTypes,
                WriteTemplate_ListOfTypes);
            template.AddReplacement(
                Tokens.ListOfPooledExtensions,
                datatypes,
                LoadTemplate_ListOfPooledExtensions,
                WriteTemplate_ListOfTypes);
            template.AddReplacement(
                Tokens.ListOfDataTypeDefinitions,
                datatypes,
                LoadTemplate_ListOfDataTypeDefinitions,
                WriteTemplate_ListOfDataTypeDefinitions);
            template.AddReplacement(
                Tokens.ListOfTypeActivators,
                datatypes,
                LoadTemplate_ListOfActivatorClasses,
                WriteTemplate_ListOfDataTypeActivators);
            template.AddReplacement(
                Tokens.ListOfActivatorRegistrations,
                datatypes,
                LoadTemplate_ListOfActivatorRegistrations,
                WriteTemplate_ListOfDataTypeActivators);
            template.Render();

            Resource initializers = EmbedInitializers();
            if (initializers != null)
            {
                return [fileName.AsTextFileResource(), initializers];
            }
            return [fileName.AsTextFileResource()];
        }

        private TemplateString LoadTemplate_ListOfActivatorClasses(ILoadContext context)
        {
            if (context.Target is not DataTypeDesign datatype)
            {
                return null;
            }
            if (datatype.BasicDataType == BasicDataType.UserDefined &&
                datatype.IsStructure &&
                !datatype.IsAbstract)
            {
                // Types that live in the Opc.Ua.Types library don't get
                // the IPooledEncodeable partial emitted (they're not
                // generated in this assembly) so they can't satisfy the
                // PooledEncodeableType<T> constraint. Use the plain
                // EncodeableType<T> for them.
                return datatype.IsPartOfOpcUaTypesLibrary()
                    ? DataTypeTemplates.StructureActivatorClass
                    : DataTypeTemplates.PooledStructureActivatorClass;
            }
            if (datatype.BasicDataType == BasicDataType.Enumeration &&
                datatype.IsEnumeration &&
                !datatype.IsOptionSet)
            {
                return DataTypeTemplates.EnumerationActivatorClass;
            }
            return null;
        }

        /// <summary>
        /// Selector for the supplemental <c>partial class</c> body that
        /// implements <see cref="IPooledEncodeable"/> on concrete
        /// structure types. All concrete non-abstract structures are
        /// poolable — this includes service request/response types and
        /// notification payload types.
        /// </summary>
        private TemplateString LoadTemplate_ListOfPooledExtensions(ILoadContext context)
        {
            if (context.Target is not DataTypeDesign datatype ||
                datatype.IsPartOfOpcUaTypesLibrary())
            {
                return null;
            }
            if (datatype.BasicDataType == BasicDataType.UserDefined &&
                datatype.IsStructure &&
                !datatype.IsAbstract)
            {
                return HasPoolableBase(datatype)
                    ? DataTypeTemplates.DerivedPooledExtensionClass
                    : DataTypeTemplates.PooledExtensionClass;
            }
            return null;
        }

        /// <summary>
        /// Returns true when the data type derives from another
        /// concrete structure type that will itself receive a pooled
        /// extension in this compilation — meaning the sentinel field,
        /// <c>Reuse()</c> and <c>ClearPooledSentinel()</c> are
        /// inherited from that base and the derived type must use
        /// <c>new</c> to hide them. Walks up the inheritance chain
        /// to find the first non-abstract ancestor that is a
        /// generated structure (not in the Opc.Ua.Types library).
        /// </summary>
        private static bool HasPoolableBase(DataTypeDesign datatype)
        {
            var current = datatype.BaseTypeNode as DataTypeDesign;
            while (current is not null)
            {
                if (current.BasicDataType == BasicDataType.Structure)
                {
                    return false;
                }
                if (current.BasicDataType == BasicDataType.UserDefined &&
                    current.IsStructure &&
                    !current.IsAbstract &&
                    !current.IsPartOfOpcUaTypesLibrary())
                {
                    return true;
                }

                current = current.BaseTypeNode as DataTypeDesign;
            }
            return false;
        }

        private TemplateString LoadTemplate_ListOfActivatorRegistrations(ILoadContext context)
        {
            if (context.Target is not DataTypeDesign datatype)
            {
                return null;
            }
            if (datatype.BasicDataType == BasicDataType.UserDefined &&
                datatype.IsStructure &&
                !datatype.IsAbstract)
            {
                return DataTypeTemplates.StructureActivatorRegistration;
            }
            if (datatype.BasicDataType == BasicDataType.Enumeration &&
                datatype.IsEnumeration &&
                !datatype.IsOptionSet)
            {
                return DataTypeTemplates.EnumerationActivatorRegistration;
            }
            return null;
        }

        private bool WriteTemplate_ListOfDataTypeActivators(IWriteContext context)
        {
            if (context.Target is not DataTypeDesign dataType)
            {
                return false;
            }
            context.Template.AddReplacement(Tokens.ClassName, dataType.SymbolicName.Name);
            context.Template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                dataType.SymbolicName.Name,
                m_logger);
            context.Template.AddReplacement(
                Tokens.XmlNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    dataType.SymbolicName.Namespace));
            AddEncodingIdReplacements(context, dataType);
            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfDataTypeDefinitions(ILoadContext context)
        {
            if (context.Target is not DataTypeDesign datatype)
            {
                return null;
            }
            if (datatype.BasicDataType == BasicDataType.UserDefined &&
                datatype.IsStructure)
            {
                return DataTypeTemplates.StructureDefinition;
            }
            if (datatype.BasicDataType == BasicDataType.Enumeration &&
                datatype.IsEnumeration)
            {
                return DataTypeTemplates.EnumDefinition;
            }
            return null;
        }

        private bool WriteTemplate_ListOfDataTypeDefinitions(IWriteContext context)
        {
            if (context.Target is not DataTypeDesign dataType)
            {
                return false;
            }
            context.Template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                dataType.SymbolicName.Name,
                m_logger);
            context.Template.AddReplacement(Tokens.ClassName, dataType.SymbolicName.Name);

            if (dataType.BasicDataType != BasicDataType.UserDefined)
            {
                // Enumeration definition
                context.Template.AddReplacement(
                    Tokens.IsOptionSet,
                    dataType.IsOptionSet);
                context.Template.AddReplacement(
                    Tokens.ListOfFields,
                    DataTypeTemplates.EnumField,
                    dataType.Fields ?? [],
                    WriteTemplate_ListOfEnumDefinitionFields);
            }
            else
            {
                // Structure definition
                StructureType structureType = StructureType.Structure;
                if (dataType.IsUnion)
                {
                    structureType = StructureType.Union;
                }
                foreach (Parameter field in dataType.Fields ?? [])
                {
                    if (field.IsOptional)
                    {
                        structureType = StructureType.StructureWithOptionalFields;
                        break;
                    }
                    if (field.AllowSubTypes)
                    {
                        if (dataType.IsUnion)
                        {
                            structureType = StructureType.UnionWithSubtypedValues;
                            break;
                        }
                        structureType = StructureType.StructureWithSubtypedValues;
                        break;
                    }
                }
                List<Parameter> fields = [];
                context.Template.AddReplacement(
                    Tokens.BaseType,
                    dataType.BaseTypeNode.GetNodeIdAsCode(
                        m_context.ModelDesign.Namespaces,
                        kNamespaceTableContextVariable));
                context.Template.AddReplacement(
                    Tokens.FirstExplicitFieldIndex,
                    CollectStructureDefinitionFields(dataType, ref structureType, fields));
                context.Template.AddReplacement(
                    Tokens.StructureType,
                    structureType);
                context.Template.AddReplacement(
                    Tokens.ListOfFields,
                    DataTypeTemplates.StructureField,
                    fields.ToDictionary(f => f, _ => structureType),
                    WriteTemplate_ListOfStructureDefinitionFields);

                static int CollectStructureDefinitionFields(
                    DataTypeDesign dataType,
                    ref StructureType structureType,
                    List<Parameter> fields)
                {
                    if (dataType == null || dataType.Fields == null)
                    {
                        return fields.Count;
                    }
                    if (dataType.BaseTypeNode is DataTypeDesign baseType)
                    {
                        CollectStructureDefinitionFields(
                            baseType,
                            ref structureType,
                            fields);
                    }

                    int start = fields.Count;
                    foreach (Parameter field in dataType.Fields)
                    {
                        if (field.IsOptional)
                        {
                            // inherit optional fields flag if derived structure
                            // contains no optional fields
                            structureType = StructureType.StructureWithOptionalFields;
                        }
                        fields.Add(field);
                    }
                    return start;
                }
            }
            return context.Template.Render();
        }

        private bool WriteTemplate_ListOfEnumDefinitionFields(IWriteContext context)
        {
            if (context.Target is not Parameter field ||
                field.Parent is not DataTypeDesign dataType)
            {
                return false;
            }

            if (dataType.IsOptionSet)
            {
                long bit = 1;
                int value = 0;

                while (field.Identifier > 0 && bit <= long.MaxValue)
                {
                    if ((bit & (long)field.Identifier) != 0)
                    {
                        break;
                    }

                    bit <<= 1;
                    value++;
                }
                context.Template.AddReplacement(
                    Tokens.ValueCode,
                    value);
            }
            else
            {
                context.Template.AddReplacement(
                    Tokens.ValueCode,
                    (long)field.Identifier);
            }

            context.Template.AddReplacement(
                Tokens.FieldName,
                field.Name.AsStringLiteral());
            context.Template.AddReplacement(
                Tokens.DisplayName,
                field.Name.GetLocalizedTextAsCode());
            context.Template.AddReplacement(
                Tokens.Description,
                field.Description.GetLocalizedTextAsCode(true));
            return context.Template.Render();
        }

        private bool WriteTemplate_ListOfStructureDefinitionFields(IWriteContext context)
        {
            if (context.Target is not KeyValuePair<Parameter, StructureType> kvp)
            {
                return false;
            }
            StructureType structureType = kvp.Value;
            Parameter field = kvp.Key;

            context.Template.AddReplacement(
                Tokens.FieldName,
                field.Name.AsStringLiteral());
            context.Template.AddReplacement(
                Tokens.DataType,
                GetNodeIdConstantForDataType(field, m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(
                Tokens.ValueRank,
                field.ValueRank.GetValueRankAsCode(field.ArrayDimensions));
            context.Template.AddReplacement(
                Tokens.ArrayDimensions,
                field.ValueRank.GetArrayDimensionsAsCode(field.ArrayDimensions) ?? "default");
            if (structureType == StructureType.StructureWithOptionalFields)
            {
                context.Template.AddReplacement(Tokens.IsOptional, field.IsOptional);
            }
            else if (structureType is
                StructureType.StructureWithSubtypedValues or
                StructureType.UnionWithSubtypedValues)
            {
                context.Template.AddReplacement(Tokens.IsOptional, field.AllowSubTypes);
            }
            else
            {
                context.Template.AddReplacement(Tokens.IsOptional, false);
            }
            context.Template.AddReplacement(
                Tokens.Description,
                field.Description.GetLocalizedTextAsCode(true));
            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfTypes(ILoadContext context)
        {
            if (context.Target is not DataTypeDesign datatype ||
                datatype.IsPartOfOpcUaTypesLibrary())
            {
                return null;
            }

            switch (datatype.BasicDataType)
            {
                case BasicDataType.Structure:
                    return null;
                case BasicDataType.UserDefined:
                    if (datatype.IsUnion)
                    {
                        return DataTypeTemplates.UnionClass;
                    }
                    if (datatype.HasFields && datatype.Fields.Any(x => x.IsOptional))
                    {
                        if (datatype.IsDerivedDataType())
                        {
                            return DataTypeTemplates.DerivedClassWithOptionalFields;
                        }

                        return DataTypeTemplates.ClassWithOptionalFields;
                    }

                    if (!datatype.IsDerivedDataType())
                    {
                        return DataTypeTemplates.Class;
                    }

                    return DataTypeTemplates.DerivedClass;
                case BasicDataType.Enumeration:
                    var baseType = datatype.BaseTypeNode as DataTypeDesign;

                    if (baseType?.SymbolicId ==
                        new XmlQualifiedName("OptionSet", Namespaces.OpcUa))
                    {
                        return DataTypeTemplates.DerivedClass;
                    }

                    return DataTypeTemplates.Enumeration;
                default:
                    if (datatype.IsOptionSet)
                    {
                        return DataTypeTemplates.Enumeration;
                    }
                    return null;
            }
        }

        private bool WriteTemplate_ListOfTypes(IWriteContext context)
        {
            if (context.Target is not DataTypeDesign dataType)
            {
                return false;
            }
            context.Template.AddReplacement(
                Tokens.ExtraInterfaces,
                dataType.Service == null ?
                    string.Empty :
                    dataType.IsServiceResponse ?
                        "global::Opc.Ua.IServiceResponse, " :
                        "global::Opc.Ua.IServiceRequest, ");

            Parameter[] fields = GetFields(dataType);

            context.Template.AddReplacement(
                Tokens.NodeClass,
                dataType.GetNodeClassAsString());
            context.Template.AddReplacement(
                Tokens.Description,
                dataType.Description != null ? dataType.Description.Value : string.Empty);
            context.Template.AddReplacement(
                Tokens.TypeName,
                dataType.SymbolicName.Name);
            context.Template.AddReplacement(
                Tokens.NamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    dataType.SymbolicName.Namespace));
            context.Template.AddReplacement(
                Tokens.NamespacePrefix,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(
                    dataType.SymbolicId.Namespace));
            context.Template.AddReplacement(
                Tokens.XmlNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantForXmlNamespace(
                    dataType.SymbolicId.Namespace));

            context.Template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                dataType.SymbolicName.Name,
                m_logger);
            context.Template.AddReplacement(
                Tokens.ClassName,
                dataType.SymbolicName.Name);
            context.Template.AddReplacement(
                Tokens.BrowseNameNamespacePrefix,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(
                    dataType.SymbolicName.Namespace));
            context.Template.AddReplacement(
                Tokens.BrowseNameNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    dataType.SymbolicName.Namespace));

            context.Template.AddReplacement(
                Tokens.BaseType,
                dataType.GetBaseClassName(m_context.ModelDesign.Namespaces));
            context.Template.AddReplacement(
                Tokens.BaseTypeNamespacePrefix,
                m_context.ModelDesign.Namespaces.GetNamespacePrefix(
                    dataType.BaseTypeNode.SymbolicId.Namespace));
            context.Template.AddReplacement(
                Tokens.BaseTypeNamespaceUri,
                m_context.ModelDesign.Namespaces.GetConstantSymbolForNamespace(
                    dataType.BaseTypeNode.SymbolicId.Namespace));

            List<Parameter> completeListOfFields = null;
            bool hasAncestorWithOptionalFields = false;

            if (dataType.IsStructure)
            {
                List<DataTypeDesign> inheritanceTree = [dataType];
                var parentDataType = dataType.BaseTypeNode as DataTypeDesign;

                while (parentDataType != null &&
                    parentDataType.SymbolicId != new XmlQualifiedName("Structure", Namespaces.OpcUa) &&
                    parentDataType.SymbolicId != new XmlQualifiedName("Union", Namespaces.OpcUa))
                {
                    inheritanceTree.Add(parentDataType);
                    if (parentDataType.HasFields &&
                        parentDataType.Fields != null &&
                        parentDataType.Fields.Any(f => f.IsOptional))
                    {
                        hasAncestorWithOptionalFields = true;
                    }
                    parentDataType = parentDataType.BaseTypeNode as DataTypeDesign;
                }

                completeListOfFields = [];

                for (int ii = inheritanceTree.Count - 1; ii >= 0; ii--)
                {
                    foreach (object field in GetFields(inheritanceTree[ii]))
                    {
                        var parameter = (Parameter)field;

                        if (parameter.IsOptional)

                        {
                            completeListOfFields.Add(parameter);
                        }
                    }
                }
            }

            context.Template.AddReplacement(
                Tokens.EncodingMaskModifier,
                hasAncestorWithOptionalFields ? "new " : string.Empty);

            // TODO: context.Template.AddReplacement(
            // TODO:     Tokens.IsAbstract,
            // TODO:     dataType.IsAbstract ? "abstract " : string.Empty);

            if (!dataType.IsOptionSet)
            {
                context.Template.AddReplacement(Tokens.Flags, string.Empty);
                context.Template.AddReplacement(Tokens.BasicType, string.Empty);
            }
            else
            {
                context.Template.AddReplacement(Tokens.Flags, "[global::System.FlagsAttribute]");
                context.Template.AddReplacement(
                    Tokens.BasicType,
                    CoreUtils.Format(" : global::System.{0}", dataType.BaseType.Name));

                var baseType = dataType.BaseTypeNode as DataTypeDesign;

                List<Parameter> clone = [];

                if (baseType?.SymbolicId != new XmlQualifiedName("OptionSet", Namespaces.OpcUa))
                {
                    var first = (Parameter)fields.GetValue(0);

                    clone.Add(new Parameter
                    {
                        Name = "None",
                        Identifier = 0,
                        IdentifierSpecified = true,
                        DataTypeNode = first.DataTypeNode,
                        DataType = first.DataType,
                        Parent = first.Parent,
                        Description = new Schema.Model.LocalizedText
                        {
                            Value = "No value specified."
                        }
                    });

                    clone.AddRange(fields.Cast<Parameter>());
                }

                fields = [.. clone];
            }

            AddEncodingIdReplacements(context, dataType);

            context.Template.AddReplacement(
                Tokens.ListOfSwitchFields,
                fields,
                LoadTemplate_ListOfSwitchFields);

            context.Template.AddReplacement(
                Tokens.ListOfEncodingMaskFields,
                completeListOfFields?.ToArray() ?? fields,
                LoadTemplate_ListOfEncodingMaskFields);

            context.Template.AddReplacement(
                Tokens.ListOfEncodedFields,
                fields,
                LoadTemplate_ListOfEncodedFields);

            context.Template.AddReplacement(
                Tokens.ListOfDecodedFields,
                fields,
                LoadTemplate_ListOfDecodedFields);

            context.Template.AddReplacement(
                Tokens.ListOfComparedFields,
                fields,
                LoadTemplate_ListOfComparedFields);

            context.Template.AddReplacement(
                Tokens.ListOfClonedFields,
                fields,
                LoadTemplate_ListOfClonedFields);

            context.Template.AddReplacement(
                Tokens.ListOfChildHashes,
                DataTypeTemplates.HashProperty,
                fields,
                WriteTemplate_ListOfProperties);

            context.Template.AddReplacement(
                Tokens.ListOfAppendStringFields,
                DataTypeTemplates.AddPropertyToStringBuilder,
                fields,
                WriteTemplate_ListOfProperties);

            context.Template.AddReplacement(
                Tokens.ListOfSwitchFieldNames,
                fields,
                LoadTemplate_ListOfSwitchFields);

            context.Template.AddReplacement(
                Tokens.ListOfEncodingMaskFieldNames,
                completeListOfFields?.ToArray() ?? fields,
                LoadTemplate_ListOfEncodingMaskFields);

            context.Template.AddReplacement(
                Tokens.ListOfFieldInitializers,
                fields,
                LoadTemplate_ListOfFieldInitializers);

            context.Template.AddReplacement(
                Tokens.ListOfFieldResets,
                fields,
                LoadTemplate_ListOfFieldResets);

            context.Template.AddReplacement(
                Tokens.ListOfFields,
                fields,
                LoadTemplate_ListOfFields);

            context.Template.AddReplacement(
                Tokens.ListOfProperties,
                fields,
                LoadTemplate_ListOfProperties,
                WriteTemplate_ListOfProperties);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ListOfFields(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            context.Out.WriteLine(
                "private {0} {1};",
                field.DataTypeNode.GetDotNetTypeName(
                    field.ValueRank,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    nullable: NullableAnnotation.NullableExceptDataTypes,
                    useMatrixTypeInsteadOfVariant: field.DataTypeNode.SupportsMatrixOf()),
                field.GetChildFieldName());

            return null;
        }

        private TemplateString LoadTemplate_ListOfSwitchFields(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            var dataType = (DataTypeDesign)field.Parent;

            int index = context.Index + 1;
            bool isLast = index == dataType.Fields.Length;

            if (context.Token == Tokens.ListOfSwitchFieldNames)
            {
                context.Out.Write('"');
                context.Out.Write(field.Name);
                context.Out.Write('"');
            }
            else
            {
                context.Out.Write(field.Name);
                context.Out.Write(" = ");
                context.Out.Write(index.ToString(CultureInfo.InvariantCulture));
            }
            if (!isLast)
            {
                context.Out.Write(",");
            }
            context.Out.WriteLine();
            return null;
        }

        private TemplateString LoadTemplate_ListOfEncodingMaskFields(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            int index = context.Index;
            if (field.IsOptional)
            {
                if (context.Token == Tokens.ListOfEncodingMaskFieldNames)
                {
                    context.Out.Write('"');
                    context.Out.Write(field.Name);
                    context.Out.Write('"');
                }
                else
                {
                    context.Out.Write(field.Name);
                    context.Out.Write(" = 0x{0:X}", 1 << index);
                }
                context.Out.WriteLine(",");
            }
            return null;
        }

        private TemplateString LoadTemplate_ListOfEncodedFields(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            var dataType = (DataTypeDesign)field.Parent;
            bool isUnion = dataType.IsUnion;

            if (isUnion)
            {
                context.Out.WriteLine($"case {dataType.SymbolicName.Name}Fields.{field.Name}:");
                context.Out.WriteLine("{");
            }

            if (field.IsOptional)
            {
                context.Out.WriteLine(
                    $"if ((EncodingMask & (uint){dataType.SymbolicName.Name}Fields.{field.Name}) != 0) ");
            }

            string functionName = field.DataTypeNode.BasicDataType.ToString();
            string fieldName = isUnion ? $"fieldName ?? \"{field.Name}\"" : $"\"{field.Name}\"";

            if (field.ValueRank == ValueRank.OneOrMoreDimensions &&
                field.DataTypeNode.SupportsMatrixOf())
            {
                EmitMatrixWriteCall(context, field, fieldName);
                if (isUnion)
                {
                    context.Out.WriteLine("break;");
                    context.Out.WriteLine("}");
                }
                return null;
            }

            switch (field.DataTypeNode.BasicDataType)
            {
                case BasicDataType.Number:
                case BasicDataType.Integer:
                case BasicDataType.UInteger:
                case BasicDataType.BaseDataType:
                    functionName = "Variant";
                    break;
                case BasicDataType.Structure:
                    functionName = "ExtensionObject";
                    break;
                case BasicDataType.Enumeration:
                    if (field.DataType == new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                    {
                        functionName = "Int32";
                        break;
                    }

                    if (field.DataTypeNode.IsOptionSet)
                    {
                        if (field.DataTypeNode.BaseTypeNode.SymbolicId ==
                            new XmlQualifiedName("OptionSet", Namespaces.OpcUa))
                        {
                            functionName = "Encodeable";
                            break;
                        }

                        var baseTypeNode = (DataTypeDesign)field.DataTypeNode.BaseTypeNode;
                        functionName = baseTypeNode.BasicDataType.ToString();
                        break;
                    }

                    functionName = "Enumerated";

                    if (field.ValueRank == ValueRank.Array)
                    {
                        context.Out.WriteLine(
                            "encoder.WriteEnumeratedArray({0}, {1});",
                            fieldName,
                            field.Name);
                        if (isUnion)
                        {
                            context.Out.WriteLine("break;");
                            context.Out.WriteLine("}");
                        }

                        return null;
                    }

                    break;
                case BasicDataType.UserDefined:
                    if (!field.AllowSubTypes)
                    {
                        // Write as encodeable as we do not allow sub type values
                        functionName = "Encodeable";
                        if (field.ValueRank == ValueRank.Array)
                        {
                            context.Out.WriteLine(
                                "encoder.WriteEncodeableArray({0}, {1});",
                                fieldName,
                                field.Name);
                            if (isUnion)
                            {
                                context.Out.WriteLine("break;");
                                context.Out.WriteLine("}");
                            }
                            return null;
                        }
                        break;
                    }

                    // Write as array

                    if (field.ValueRank == ValueRank.Array)
                    {
                        context.Out.WriteLine(
                            "encoder.WriteEncodeableArrayAsExtensionObjects({0}, {1});",
                            fieldName,
                            field.Name);
                        if (isUnion)
                        {
                            context.Out.WriteLine("break;");
                            context.Out.WriteLine("}");
                        }

                        return null;
                    }

                    // Write as scalar

                    if (field.ValueRank == ValueRank.Scalar)
                    {
                        context.Out.WriteLine(
                            "encoder.WriteEncodeableAsExtensionObject({0}, {1});",
                            fieldName,
                            field.Name);

                        if (isUnion)
                        {
                            context.Out.WriteLine("break;");
                            context.Out.WriteLine("}");
                        }

                        return null;
                    }

                    // Matrix is intercepted at the top of the method when
                    // SupportsMatrixOf is true. Everything else (e.g. exotic
                    // ValueRank values that are neither Scalar nor Array) falls
                    // through to the Variant fallback below.

                    break;
            }

            if (field.ValueRank == ValueRank.Array)

            {
                functionName += "Array";
            }
            else if (field.ValueRank != ValueRank.Scalar)
            {
                functionName = "Variant";
            }

            context.Out.Write($"encoder.Write{functionName}({fieldName}, {field.Name}");

            context.Out.WriteLine(");");

            if (isUnion)
            {
                context.Out.WriteLine("break;");
                context.Out.WriteLine("}");
            }
            return null;
        }

        private TemplateString LoadTemplate_ListOfDecodedFields(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            var dataType = (DataTypeDesign)field.Parent;
            bool isUnion = dataType.IsUnion;
            if (isUnion)
            {
                context.Out.WriteLine($"case {dataType.SymbolicName.Name}Fields.{field.Name}:");
                context.Out.WriteLine("{");
            }

            if (field.IsOptional)
            {
                context.Out.WriteLine(
                    $"if ((EncodingMask & (uint){dataType.SymbolicName.Name}Fields.{field.Name}) != 0) ");
            }

            string valueName = field.Name;
            string fieldName = isUnion ? $"fieldName ?? \"{field.Name}\"" : $"\"{field.Name}\"";

            if (field.ValueRank == ValueRank.OneOrMoreDimensions &&
                field.DataTypeNode.SupportsMatrixOf())
            {
                EmitMatrixReadCall(context, field, valueName, fieldName);
                if (isUnion)
                {
                    context.Out.WriteLine("break;");
                    context.Out.WriteLine("}");
                }
                return null;
            }

            string typeName = field.ValueRank == ValueRank.Array ? "Array" : string.Empty;
            string functionName;
            switch (field.DataTypeNode.BasicDataType)
            {
                case BasicDataType.Number:
                case BasicDataType.Integer:
                case BasicDataType.UInteger:
                case BasicDataType.BaseDataType:
                    functionName = "Variant" + typeName;
                    break;
                case BasicDataType.Structure:
                    functionName = "ExtensionObject" + typeName;
                    break;
                case BasicDataType.Enumeration:
                    if (field.DataType ==
                        new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                    {
                        functionName = "Int32" + typeName;
                        break;
                    }

                    if (field.DataTypeNode.IsOptionSet)
                    {
                        if (field.DataTypeNode.BaseTypeNode.SymbolicId ==
                            new XmlQualifiedName("OptionSet", Namespaces.OpcUa))
                        {
                            functionName = CoreUtils.Format(
                                "Encodeable{0}<{1}>",
                                typeName,
                                field.DataTypeNode.GetDotNetTypeName(
                                    ValueRank.Scalar,
                                    m_context.ModelDesign.TargetNamespace.Value,
                                    m_context.ModelDesign.Namespaces,
                                    nullable: NullableAnnotation.NonNullable));
                            break;
                        }

                        var fdt = (DataTypeDesign)field.DataTypeNode.BaseTypeNode;
                        functionName = fdt.BasicDataType.ToString() + typeName;
                        break;
                    }

                    functionName = CoreUtils.Format(
                        "Enumerated{0}<{1}>",
                        typeName,
                        field.DataTypeNode.GetDotNetTypeName(
                            ValueRank.Scalar,
                            m_context.ModelDesign.TargetNamespace.Value,
                            m_context.ModelDesign.Namespaces,
                            nullable: NullableAnnotation.NonNullable));
                    break;
                case BasicDataType.UserDefined:
                    if (!field.AllowSubTypes)
                    {
                        // Read as encodeable as we do not allow subtype values
                        functionName = CoreUtils.Format(
                            "Encodeable{0}<{1}>",
                            typeName,
                            field.DataTypeNode.GetDotNetTypeName(
                            ValueRank.Scalar,
                            m_context.ModelDesign.TargetNamespace.Value,
                            m_context.ModelDesign.Namespaces,
                            nullable: NullableAnnotation.NonNullable));
                        break;
                    }

                    context.Out.Write($"{valueName} = ");
                    string elementName = field.DataTypeNode.GetDotNetTypeName(
                        ValueRank.Scalar,
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces,
                        nullable: NullableAnnotation.NonNullable);

                    // Read array

                    if (field.ValueRank == ValueRank.Array)
                    {
                        context.Out.WriteLine(
                            $"decoder.ReadEncodeableArrayAsExtensionObjects<{elementName}>({fieldName});");
                        if (isUnion)
                        {
                            context.Out.WriteLine("break;");
                            context.Out.WriteLine("}");
                        }

                        return null;
                    }

                    // Read scalar

                    if (field.ValueRank == ValueRank.Scalar)
                    {
                        context.Out.WriteLine(
                            $"decoder.ReadEncodeableAsExtensionObject<{elementName}>({fieldName});");
                        if (isUnion)
                        {
                            context.Out.WriteLine("break;");
                            context.Out.WriteLine("}");
                        }

                        return null;
                    }

                    // Matrix or other non-scalar, non-array
                    functionName = "Variant";
                    break;
                default:
                    functionName = field.DataTypeNode.BasicDataType.ToString() + typeName;
                    break;
            }
            if (field.ValueRank is not ValueRank.Scalar and not ValueRank.Array)
            {
                functionName = "Variant";
            }
            context.Out.WriteLine("{0} = decoder.Read{1}({2});", valueName, functionName, fieldName);
            if (isUnion)
            {
                context.Out.WriteLine("break;");
                context.Out.WriteLine("}");
            }

            return null;
        }

        /// <summary>
        /// Emit the encoder call for a structure field whose ValueRank is
        /// <see cref="ValueRank.OneOrMoreDimensions"/>. The field is
        /// generated as a typed <c>MatrixOf&lt;T&gt;</c> and either passes
        /// through a dedicated <c>WriteEncodeableMatrix</c> call (for
        /// concrete <see cref="IEncodeable"/> matrices) or is packed into a
        /// <see cref="Variant"/> via <c>Variant.From</c> /
        /// <c>Variant.FromStructure</c> before being written through
        /// <c>WriteVariant</c>.
        /// </summary>
        private static void EmitMatrixWriteCall(
            ILoadContext context,
            Parameter field,
            string fieldName)
        {
            if (IsConcreteEncodeableMatrix(field))
            {
                context.Out.WriteLine(
                    "encoder.WriteEncodeableMatrix({0}, {1});",
                    fieldName,
                    field.Name);
                return;
            }

            if (field.DataTypeNode.BasicDataType == BasicDataType.UserDefined &&
                !field.DataTypeNode.IsEnumeration)
            {
                // UserDefined structure with AllowSubTypes - wrap as Variant
                // of extension objects via FromStructure.
                context.Out.WriteLine(
                    "encoder.WriteVariant({0}, global::Opc.Ua.Variant.FromStructure({1}));",
                    fieldName,
                    field.Name);
                return;
            }

            // Primitives, enumerations, Structure (ExtensionObject),
            // Number/Integer/UInteger/BaseDataType (Variant) all flow
            // through the typed Variant.From overloads.
            context.Out.WriteLine(
                "encoder.WriteVariant({0}, global::Opc.Ua.Variant.From({1}));",
                fieldName,
                field.Name);
        }

        /// <summary>
        /// Emit the decoder call for a structure field whose ValueRank is
        /// <see cref="ValueRank.OneOrMoreDimensions"/>. Mirrors
        /// <see cref="EmitMatrixWriteCall"/>.
        /// </summary>
        private void EmitMatrixReadCall(
            ILoadContext context,
            Parameter field,
            string valueName,
            string fieldName)
        {
            if (IsConcreteEncodeableMatrix(field))
            {
                string elementName = field.DataTypeNode.GetDotNetTypeName(
                    ValueRank.Scalar,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    nullable: NullableAnnotation.NonNullable);
                context.Out.WriteLine(
                    "{0} = decoder.ReadEncodeableMatrix<{1}>({2});",
                    valueName,
                    elementName,
                    fieldName);
                return;
            }

            string getter = GetMatrixVariantGetter(field);
            context.Out.WriteLine(
                "{0} = decoder.ReadVariant({1}).{2};",
                valueName,
                fieldName,
                getter);
        }

        /// <summary>
        /// Returns true if the field should be encoded as a concrete
        /// <c>WriteEncodeableMatrix</c> / <c>ReadEncodeableMatrix</c> call
        /// (i.e. user-defined structure without <c>AllowSubTypes</c>, or an
        /// <c>OptionSet</c> whose base type is the abstract
        /// <c>OptionSet</c> structure).
        /// </summary>
        private static bool IsConcreteEncodeableMatrix(Parameter field)
        {
            DataTypeDesign type = field.DataTypeNode;
            if (field.AllowSubTypes)
            {
                return false;
            }
            if (type.BasicDataType == BasicDataType.UserDefined &&
                !type.IsEnumeration)
            {
                return true;
            }
            if (type.BasicDataType == BasicDataType.Enumeration &&
                type.IsOptionSet &&
                type.BaseTypeNode?.SymbolicId ==
                    new XmlQualifiedName("OptionSet", Namespaces.OpcUa))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the <see cref="Variant"/> matrix getter expression for the
        /// given matrix-typed field. Caller composes this with
        /// <c>decoder.ReadVariant(...)</c>.
        /// </summary>
        private string GetMatrixVariantGetter(Parameter field)
        {
            DataTypeDesign type = field.DataTypeNode;
            BasicDataType basic = type.BasicDataType;

            // UserDefined structure with AllowSubTypes - decode through
            // GetStructureMatrix<T> which unwraps extension objects.
            if (basic == BasicDataType.UserDefined && !type.IsEnumeration)
            {
                string elementName = type.GetDotNetTypeName(
                    ValueRank.Scalar,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    nullable: NullableAnnotation.NonNullable);
                return CoreUtils.Format(
                    "GetStructureMatrix<{0}>()", elementName);
            }

            // User-defined typed enum.
            if (basic == BasicDataType.UserDefined && type.IsEnumeration)
            {
                string elementName = type.GetDotNetTypeName(
                    ValueRank.Scalar,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    nullable: NullableAnnotation.NonNullable);
                return CoreUtils.Format(
                    "GetEnumerationMatrix<{0}>()", elementName);
            }

            if (basic == BasicDataType.Enumeration)
            {
                // The well-known abstract "Enumeration" data type maps to
                // MatrixOf<int> in the generated code.
                if (type.SymbolicId ==
                    new XmlQualifiedName("Enumeration", Namespaces.OpcUa))
                {
                    return "GetInt32Matrix()";
                }
                // OptionSet whose base is a primitive integer (UInt32 etc.)
                // is represented as a matrix of that primitive type.
                if (type.IsOptionSet &&
                    type.BaseTypeNode is DataTypeDesign optionSetBase)
                {
                    return CoreUtils.Format(
                        "Get{0}Matrix()", optionSetBase.BasicDataType);
                }
                // Typed enumeration.
                string elementName = type.GetDotNetTypeName(
                    ValueRank.Scalar,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    nullable: NullableAnnotation.NonNullable);
                return CoreUtils.Format(
                    "GetEnumerationMatrix<{0}>()", elementName);
            }

            if (basic == BasicDataType.Structure)
            {
                return "GetExtensionObjectMatrix()";
            }

            if (basic is BasicDataType.BaseDataType
                or BasicDataType.Number
                or BasicDataType.Integer
                or BasicDataType.UInteger)
            {
                return "GetVariantMatrix()";
            }

            // Primitive built-in types: Boolean, SByte, Byte, Int16, UInt16,
            // Int32, UInt32, Int64, UInt64, Float, Double, String, DateTime,
            // Guid, ByteString, XmlElement, NodeId, ExpandedNodeId,
            // StatusCode, QualifiedName, LocalizedText, DataValue.
            return CoreUtils.Format("Get{0}Matrix()", basic);
        }

        private TemplateString LoadTemplate_ListOfComparedFields(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            var dataType = (DataTypeDesign)field.Parent;
            if (dataType.IsUnion)
            {
                context.Out.WriteLine($"case {dataType.SymbolicName.Name}Fields.{field.Name}:");
                context.Out.WriteLine("{");
            }

            if (field.IsOptional)
            {
                context.Out.WriteLine(
                    $"if ((EncodingMask & (uint){dataType.SymbolicName.Name}Fields.{field.Name}) != 0) ");
            }

            if (IsFloatingPointScalar(field) ||
                !field.DataTypeNode.IsDotNetEqualityComparable(field.ValueRank))
            {
                context.Out.WriteLine(
                    "if (!global::Opc.Ua.CoreUtils.IsEqual({0}, value.{0}))",
                    field.GetChildFieldName());
            }
            else
            {
                context.Out.WriteLine(
                    "if ({0} != value.{0})",
                    field.GetChildFieldName());
            }
            context.Out.WriteLine("{");
            context.Out.WriteLine("    return false;");
            context.Out.WriteLine("}");

            if (dataType.IsUnion)
            {
                context.Out.WriteLine("break;");
                context.Out.WriteLine("}");
            }

            return null;

            // The != operator is not reflexive for scalar floating point values:
            // NaN != NaN is true, which would make a decoded value compare unequal
            // to itself. Route those through CoreUtils.IsEqual (which uses the
            // NaN-aware IEquatable comparer). Array / matrix / Variant forms already
            // compare NaN-safely via ArrayOf/MatrixOf/Variant.
            static bool IsFloatingPointScalar(Parameter field) =>
                field.ValueRank == ValueRank.Scalar &&
                field.DataTypeNode != null &&
                (field.DataTypeNode.BasicDataType == BasicDataType.Float ||
                    field.DataTypeNode.BasicDataType == BasicDataType.Double);
        }

        private TemplateString LoadTemplate_ListOfClonedFields(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            var dataType = (DataTypeDesign)field.Parent;
            if (dataType.IsUnion)
            {
                context.Out.WriteLine($"case {dataType.SymbolicName.Name}Fields.{field.Name}:");
                context.Out.WriteLine("{");
            }

            if (field.IsOptional)
            {
                context.Out.WriteLine(
                    $"if ((EncodingMask & (uint){dataType.SymbolicName.Name}Fields.{field.Name}) != 0) ");
            }

            if (field.DataTypeNode.NeedsCloning())
            {
                context.Out.WriteLine("clone.{0} = ({1})global::Opc.Ua.CoreUtils.Clone(this.{0});",
                    field.GetChildFieldName(),
                    field.DataTypeNode.GetDotNetTypeName(
                        field.ValueRank,
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces,
                        nullable: NullableAnnotation.NullableExceptDataTypes,
                        useMatrixTypeInsteadOfVariant: field.DataTypeNode.SupportsMatrixOf()));
            }
            else
            {
                context.Out.WriteLine("clone.{0} = this.{0};",
                    field.GetChildFieldName());
            }

            if (dataType.IsUnion)
            {
                context.Out.WriteLine("break;");
                context.Out.WriteLine("}");
            }

            return null;
        }

        private TemplateString LoadTemplate_ListOfFieldInitializers(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            string value = field.DataTypeNode.GetValueAsCode(
                field.ValueRank,
                field.DefaultValue,
                null,
                false,
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces,
                m_messageContext,
                () => AddXmlInitializerForComplexValue(
                    field,
                    field.ValueRank,
                    field.DataTypeNode,
                    field.DefaultValue));

            context.Out.WriteLine("{0} = {1};", field.GetChildFieldName(), value);
            return null;
        }

        /// <summary>
        /// Emits one assignment per declared field in the form
        /// <c>m_field = default;</c>. Used by the
        /// <c>PooledExtensionClass</c> template to reset all fields
        /// before returning the instance to its activator's pool.
        /// <c>default</c> covers reference types (assigns <c>null</c>),
        /// value types (zero), and <see cref="ArrayOf{T}"/> /
        /// <c>ReadOnlyMemory&lt;T&gt;</c>-backed structs (drops the
        /// backing reference).
        /// </summary>
        private TemplateString LoadTemplate_ListOfFieldResets(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            context.Out.WriteLine("{0} = default;", field.GetChildFieldName());
            return null;
        }

        private TemplateString LoadTemplate_ListOfProperties(ILoadContext context)
        {
            if (context.Target is not Parameter field)
            {
                return null;
            }
            var dataType = field.Parent as DataTypeDesign;

            if (dataType.BasicDataType != BasicDataType.Enumeration)
            {
                if (field.DataTypeNode.BasicDataType == BasicDataType.UserDefined ||
                    field.ValueRank == ValueRank.Array)
                {
                    if (field.AllowSubTypes ||
                        (field.ValueRank != ValueRank.Array &&
                            field.ValueRank != ValueRank.Scalar))
                    {
                        return DataTypeTemplates.ScalarProperty;
                    }
                    return DataTypeTemplates.ArrayProperty;
                }
                return DataTypeTemplates.ScalarProperty;
            }
            return DataTypeTemplates.EnumerationValue;
        }

        private bool WriteTemplate_ListOfProperties(IWriteContext context)
        {
            if (context.Target is not Parameter field)
            {
                return false;
            }
            const bool isRequired = false;
            var dataType = (DataTypeDesign)field.Parent;
            bool emitDefaultValue =
                !field.DataTypeNode.IsDotNetReferenceType(field.ValueRank);

            context.Template.AddReplacement(
                Tokens.Description,
                field.Description != null ? field.Description.Value : string.Empty);
            context.Template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                field.Name,
                m_logger);
            context.Template.AddReplacement(
                Tokens.EnumerationName,
                field.EnsureUniqueEnumName());
            context.Template.AddReplacement(
                Tokens.TypeName,
                field.DataTypeNode.GetDotNetTypeName(
                field.ValueRank,
                m_context.ModelDesign.TargetNamespace.Value,
                m_context.ModelDesign.Namespaces,
                nullable: NullableAnnotation.NullableExceptDataTypes,
                useMatrixTypeInsteadOfVariant: field.DataTypeNode.SupportsMatrixOf()));
            context.Template.AddReplacement(
                Tokens.FieldName,
                field.GetChildFieldName());
            context.Template.AddReplacement(
                Tokens.IsRequired,
                isRequired ? "true" : "false");
            context.Template.AddReplacement(
                Tokens.EmitDefaultValue,
                emitDefaultValue ? "true" : "false");
            context.Template.AddReplacement(
                Tokens.FieldIndex,
                CoreUtils.Format("{0}", context.Index + 1));
            context.Template.AddReplacement(
                Tokens.DefaultValue,
                field.DataTypeNode.GetValueAsCode(
                    field.ValueRank,
                    null,
                    null,
                    false,
                    m_context.ModelDesign.TargetNamespace.Value,
                    m_context.ModelDesign.Namespaces,
                    m_messageContext,
                    () => AddXmlInitializerForComplexValue(
                        field,
                        field.ValueRank,
                        field.DataTypeNode,
                        field.DefaultValue)));
            context.Template.AddReplacement(
                Tokens.Identifier,
                field.Identifier.ToString(CultureInfo.InvariantCulture));

            if (field.IdentifierInName)

            {
                context.Template.AddReplacement(Tokens.XmlIdentifier, field.Name);
            }
            else
            {
                context.Template.AddReplacement(Tokens.XmlIdentifier,
                    CoreUtils.Format("{0}_{1}", field.Name, field.Identifier));
            }

            if (field.Name == "NodeId" &&
                dataType.BaseTypeNode.SymbolicName.Name == BrowseNames.HistoryUpdateDetails)
            {
                context.Template.AddReplacement(
                    Tokens.AccessorSymbol,
                    "public override");
            }
            else
            {
                context.Template.AddReplacement(
                    Tokens.AccessorSymbol,
                    "public");
            }

            return context.Template.Render();
        }

        private void AddEncodingIdReplacements(IWriteContext context, DataTypeDesign dataType)
        {
            Dictionary<string, string> encodings = new()
            {
                { Tokens.BinaryEncodingId,
                    CoreUtils.Format("{0}_Encoding_DefaultBinary", dataType.SymbolicName.Name) },
                { Tokens.XmlEncodingId,
                    CoreUtils.Format("{0}_Encoding_DefaultXml", dataType.SymbolicName.Name) }
            };
            foreach (KeyValuePair<string, string> kv in encodings)
            {
                // bool isEncodingPartOfModel = m_context.ModelDesign.TryFindNode(
                //     new XmlQualifiedName(kv.Value, dataType.SymbolicName.Namespace),
                //     kv.Key,
                //     "HasEncoding",
                //     out NodeDesign encodingNode);
                bool isEncodingPartOfModel = m_context.ModelDesign.Nodes.Any(x =>
                    x.SymbolicId.Name == kv.Value &&
                    x.SymbolicId.Namespace == dataType.SymbolicName.Namespace);
                if (!isEncodingPartOfModel)
                {
                    context.Template.AddReplacement(
                        kv.Key,
                        "global::Opc.Ua.NodeId.Null");
                }
                else
                {
                    context.Template.AddReplacement(
                        kv.Key,
                        CoreUtils.Format("ObjectIds.{0}", kv.Value));
                }
            }
        }

        private Parameter[] GetFields(DataTypeDesign dataType)
        {
            List<Parameter> fields = [];

            if (dataType.Fields == null)

            {
                return [.. fields];
            }
            foreach (Parameter child in dataType.Fields)
            {
                if (!m_context.ModelDesign.IsExcluded(child))
                {
                    fields.Add(child);
                }
            }

            return [.. fields];
        }

        private List<DataTypeDesign> GetDataTypes()
        {
            List<DataTypeDesign> datatypes = [];
            foreach (NodeDesign node in m_context.ModelDesign.GetNodeDesigns())
            {
                if (node is DataTypeDesign dataTypeDesign)
                {
                    datatypes.Add(dataTypeDesign);
                }
            }
            return datatypes;
        }

        private string GetNodeIdConstantForDataType(
            Parameter field,
            Namespace[] namespaceUris)
        {
            if (!m_context.ModelDesign.UseAllowSubtypes)
            {
                DataTypeDesign dataType = m_context.ModelDesign.FindNode<DataTypeDesign>(
                    field.DataType,
                    field.Name,
                    "DataType");
                return dataType.GetNodeIdAsCode(namespaceUris, kNamespaceTableContextVariable);
            }
            return field.DataTypeNode.GetNodeIdAsCode(namespaceUris, kNamespaceTableContextVariable);
        }

        private string AddXmlInitializerForComplexValue(
            Parameter field,
            ValueRank valueRank,
            DataTypeDesign dataType,
            System.Xml.XmlElement element)
        {
            string xml = element?.OuterXml;
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }
            string resourceName = CoreUtils.Format(
                "Values.{0}_{1}",
                dataType.SymbolicId.Name,
                field.Name);
            string uniqueName = resourceName;
            for (int i = 0; i < 1000; i++)
            {
                if (m_initializers.TryAdd(uniqueName, new TextResource(uniqueName, xml)))
                {
                    // Get code to create the variant from the XML resource reference
                    // TODO: Need to remove ambient message context usage here
                    return dataType.GetVariantValueFromXmlAsCode(
                        valueRank,
                        m_context.ModelDesign.TargetNamespace.Value,
                        m_context.ModelDesign.Namespaces,
                        uniqueName,
                        "global::Opc.Ua.AmbientMessageContext.CurrentContext");
                }
                uniqueName = resourceName + i;
            }
            throw new InvalidOperationException(
                $"Unexpected duplicate resource name {resourceName}. " +
                "This happens if more than 1000 resources have the same base name.");
        }

        /// <summary>
        /// Embed all initializers as source code
        /// </summary>
        private Resource EmbedInitializers()
        {
            if (m_initializers.Count == 0)
            {
                return null;
            }
            var initializers = new ResourceGenerator(m_context);
            return initializers.Embed(
                m_context.ModelDesign.TargetNamespace.Prefix,
                "DataTypes.i",
                internalAccess: true,
                [.. m_initializers.Values]);
        }

        private const string kNamespaceTableContextVariable = "namespaceUris";

        private readonly Dictionary<string, Resource> m_initializers = [];
        private readonly IServiceMessageContext m_messageContext;
        private readonly IGeneratorContext m_context;
        private readonly Microsoft.Extensions.Logging.ILogger m_logger;
    }
}
