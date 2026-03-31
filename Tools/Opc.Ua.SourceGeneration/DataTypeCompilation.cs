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
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceProductionContext = SGF.SgfSourceProductionContext;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Handles [DataType]-annotated classes and enums.
    /// Builds a <see cref="DataTypeSourceModel"/> from Roslyn symbols
    /// and emits generated IEncodeable implementation via
    /// <see cref="DataTypeSourceGenerator"/>.
    /// </summary>
    internal sealed record class DataTypeCompilation
    {
        private const string DataTypeFieldAttributeName = "DataTypeFieldAttribute";

        /// <summary>
        /// Check whether the generator can handle the node.
        /// </summary>
        public static bool Handles(SyntaxNode node, CancellationToken ct)
        {
            return node is TypeDeclarationSyntax t && t.AttributeLists.Count > 0;
        }

        /// <summary>
        /// Create data type compilation from a Roslyn symbol.
        /// </summary>
        public DataTypeCompilation(
            GeneratorAttributeSyntaxContext context,
            CancellationToken cancellationToken)
        {
            m_cancellationToken = cancellationToken;
            m_symbol = (INamedTypeSymbol)context.TargetSymbol;
            m_location = m_symbol.Locations.FirstOrDefault();

            // Extract [DataType] attribute values
            AttributeData dataTypeAttr = context.Attributes.FirstOrDefault();
            m_dataTypeNamespace = GetNamedArgString(dataTypeAttr, "Namespace");
            m_dataTypeId = GetNamedArgString(dataTypeAttr, "DataTypeId");
            m_binaryEncodingId = GetNamedArgString(dataTypeAttr, "BinaryEncodingId");
            m_xmlEncodingId = GetNamedArgString(dataTypeAttr, "XmlEncodingId");
            m_jsonEncodingId = GetNamedArgString(dataTypeAttr, "JsonEncodingId");
        }

        /// <summary>
        /// Emit the generated code.
        /// </summary>
        public void Emit(SourceProductionContext sourceContext)
        {
            m_cancellationToken.ThrowIfCancellationRequested();

            if (m_symbol.TypeKind == TypeKind.Enum)
            {
                EmitEnum(sourceContext);
                return;
            }

            EmitClass(sourceContext);
        }

        private void EmitClass(SourceProductionContext sourceContext)
        {
            // Validate: must be partial
            bool isPartial = m_symbol.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax(m_cancellationToken) is ClassDeclarationSyntax cds &&
                    cds.Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                sourceContext.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.Exception,
                        m_location,
                        "[DataType] class must be declared as partial."));
                return;
            }

            // Validate: must have parameterless constructor
            bool hasCtor = m_symbol.Constructors.Any(c =>
                c.Parameters.Length == 0 && !c.IsImplicitlyDeclared == false) ||
                m_symbol.Constructors.Any(c => c.Parameters.Length == 0);
            if (!hasCtor)
            {
                sourceContext.ReportDiagnostic(
                    Diagnostic.Create(
                        SourceGenerator.Exception,
                        m_location,
                        "[DataType] class must have a parameterless constructor."));
                return;
            }

            DataTypeSourceModel model = BuildClassModel();
            string source = DataTypeSourceGenerator.Generate(model);
            sourceContext.AddSource($"{model.ClassName}.g.cs", source);
        }

        private void EmitEnum(SourceProductionContext sourceContext)
        {
            DataTypeSourceModel model = BuildEnumModel();
            string source = DataTypeSourceGenerator.Generate(model);
            sourceContext.AddSource($"{model.ClassName}.g.cs", source);
        }

        private DataTypeSourceModel BuildClassModel()
        {
            string ns = GetFullNamespace(m_symbol);
            string nsUri = ResolveNamespaceUri(ns);
            bool isRecord = m_symbol.IsRecord;
            List<DataTypeSourceField> fields = CollectFields();

            return new DataTypeSourceModel
            {
                ClassName = m_symbol.Name,
                Namespace = ns,
                NamespaceUri = nsUri,
                NamespaceSymbol = ns.Replace(".", string.Empty),
                DataTypeId = m_dataTypeId,
                BinaryEncodingId = m_binaryEncodingId,
                XmlEncodingId = m_xmlEncodingId,
                JsonEncodingId = m_jsonEncodingId,
                IsRecord = isRecord,
                IsEnum = false,
                Fields = fields,
                BaseClassName = m_symbol.BaseType?.Name == "Object" ? null : m_symbol.BaseType?.Name
            };
        }

        private DataTypeSourceModel BuildEnumModel()
        {
            string ns = GetFullNamespace(m_symbol);
            string nsUri = ResolveNamespaceUri(ns);

            var members = new List<DataTypeSourceEnumMember>();
            foreach (ISymbol member in m_symbol.GetMembers())
            {
                if (member is IFieldSymbol field && field.HasConstantValue)
                {
                    members.Add(new DataTypeSourceEnumMember
                    {
                        Name = field.Name,
                        Value = field.ConstantValue?.ToString() ?? "0"
                    });
                }
            }

            return new DataTypeSourceModel
            {
                ClassName = m_symbol.Name,
                Namespace = ns,
                NamespaceUri = nsUri,
                NamespaceSymbol = ns.Replace(".", string.Empty),
                DataTypeId = m_dataTypeId,
                BinaryEncodingId = m_binaryEncodingId,
                XmlEncodingId = m_xmlEncodingId,
                JsonEncodingId = m_jsonEncodingId,
                IsRecord = false,
                IsEnum = true,
                IsFlags = m_symbol.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "FlagsAttribute"),
                EnumMembers = members
            };
        }

        private List<DataTypeSourceField> CollectFields()
        {
            IPropertySymbol[] allProperties = m_symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => !p.IsAbstract && !p.IsStatic && !p.IsReadOnly &&
                            p.DeclaredAccessibility == Accessibility.Public)
                .ToArray();

            // Check if any property has [DataTypeField] — if so, use only those
            bool hasDataTypeFieldAttr = allProperties.Any(p =>
                p.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == DataTypeFieldAttributeName));

            IPropertySymbol[] selectedProps = hasDataTypeFieldAttr
                ? allProperties.Where(p =>
                    p.GetAttributes().Any(a =>
                        a.AttributeClass?.Name == DataTypeFieldAttributeName))
                    .ToArray()
                : allProperties;

            var fields = new List<DataTypeSourceField>();
            int orderIndex = 0;
            foreach (IPropertySymbol prop in selectedProps)
            {
                m_cancellationToken.ThrowIfCancellationRequested();

                AttributeData dtfAttr = prop.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == DataTypeFieldAttributeName);
                int order = dtfAttr != null
                    ? GetNamedArgInt(dtfAttr, "Order", orderIndex)
                    : orderIndex;
                string fieldName = dtfAttr != null
                    ? GetNamedArgString(dtfAttr, "Name") ?? prop.Name
                    : prop.Name;

                DataTypeSourceField field = CreateField(prop, fieldName, order);
                fields.Add(field);
                orderIndex++;
            }

            fields.Sort((a, b) => a.Order.CompareTo(b.Order));
            return fields;
        }

        private DataTypeSourceField CreateField(
            IPropertySymbol prop,
            string fieldName,
            int order)
        {
            ITypeSymbol type = prop.Type;
            string shortName = type.Name;
            bool isNullable = prop.NullableAnnotation == NullableAnnotation.Annotated;
            bool isEncodeable = ImplementsInterface(type, "IEncodeable");
            bool isEnum = type.TypeKind == TypeKind.Enum;
            bool isCollection = false;
            string elementTypeName = null;

            // Check for generic collections (List<T>, etc.)
            if (type is INamedTypeSymbol namedType &&
                namedType.IsGenericType &&
                (shortName == "List" || shortName == "IList" || shortName == "IReadOnlyList"))
            {
                isCollection = true;
                ITypeSymbol elemType = namedType.TypeArguments[0];
                elementTypeName = elemType.Name;
                shortName = elementTypeName;
            }

            // Resolve encoder/decoder method
            string encoderMethod = null;
            string decoderMethod = null;

            if (isEncodeable)
            {
                encoderMethod = "WriteEncodeable";
                decoderMethod = "ReadEncodeable";
            }
            else if (isEnum)
            {
                encoderMethod = "WriteEnumerated";
                decoderMethod = "ReadEnumerated";
            }
            else
            {
                var methods = DataTypeSourceGenerator.GetEncoderDecoderMethods(shortName);
                if (methods.HasValue)
                {
                    encoderMethod = isCollection
                        ? methods.Value.encoder.Replace("Write", "Write") + "Array"
                        : methods.Value.encoder;
                    decoderMethod = isCollection
                        ? methods.Value.decoder.Replace("Read", "Read") + "Array"
                        : methods.Value.decoder;
                }
                else
                {
                    encoderMethod = "WriteEncodeable";
                    decoderMethod = "ReadEncodeable";
                    isEncodeable = true;
                }
            }

            return new DataTypeSourceField
            {
                PropertyName = prop.Name,
                FieldName = fieldName,
                TypeName = GetFullyQualifiedTypeName(prop.Type),
                ShortTypeName = shortName,
                IsCollection = isCollection,
                ElementTypeName = elementTypeName,
                IsOptional = isNullable,
                IsEncodeable = isEncodeable,
                IsEnum = isEnum,
                Order = order,
                EncoderMethod = encoderMethod,
                DecoderMethod = decoderMethod
            };
        }

        private string ResolveNamespaceUri(string dotNetNamespace)
        {
            // 1. [DataType(Namespace = "...")]
            if (!string.IsNullOrEmpty(m_dataTypeNamespace))
            {
                return m_dataTypeNamespace;
            }

            // 2. [DataContract(Namespace = "...")]
            AttributeData dcAttr = m_symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == nameof(DataContractAttribute));
            if (dcAttr != null)
            {
                string dcNs = GetNamedArgString(dcAttr, "Namespace");
                if (!string.IsNullOrEmpty(dcNs))
                {
                    return dcNs;
                }
            }

            // 3. Fallback: "urn:" + lowercase .NET namespace
            return "urn:" + dotNetNamespace.ToLowerInvariant();
        }

        private static string GetFullNamespace(INamedTypeSymbol symbol)
        {
            var parts = new List<string>();
            INamespaceSymbol ns = symbol.ContainingNamespace;
            while (ns != null && !ns.IsGlobalNamespace)
            {
                parts.Insert(0, ns.Name);
                ns = ns.ContainingNamespace;
            }
            return string.Join(".", parts);
        }

        private static string GetFullyQualifiedTypeName(ITypeSymbol type)
        {
            return "global::" + type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
                    .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
        }

        private static bool ImplementsInterface(ITypeSymbol type, string interfaceName)
        {
            return type.AllInterfaces.Any(i => i.Name == interfaceName);
        }

        private static string GetNamedArgString(AttributeData attr, string name)
        {
            if (attr == null)
            {
                return null;
            }
            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp.Key == name && kvp.Value.Value is string s)
                {
                    return s;
                }
            }
            return null;
        }

        private static int GetNamedArgInt(AttributeData attr, string name, int defaultValue)
        {
            if (attr == null)
            {
                return defaultValue;
            }
            foreach (var kvp in attr.NamedArguments)
            {
                if (kvp.Key == name && kvp.Value.Value is int i)
                {
                    return i;
                }
            }
            return defaultValue;
        }

        private readonly CancellationToken m_cancellationToken;
        private readonly INamedTypeSymbol m_symbol;
        private readonly Location m_location;
        private readonly string m_dataTypeNamespace;
        private readonly string m_dataTypeId;
        private readonly string m_binaryEncodingId;
        private readonly string m_xmlEncodingId;
        private readonly string m_jsonEncodingId;
    }
}
