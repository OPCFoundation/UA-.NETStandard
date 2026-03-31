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

using System.Collections.Generic;
using System.Collections.Immutable;
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
    /// Handles a single [DataType]-annotated class or enum.
    /// Builds and validates a <see cref="DataTypeSourceModel"/> during
    /// construction. Models are collected via <c>.Collect()</c> and
    /// emitted as a batch (one file per namespace) by
    /// <see cref="EmitBatch"/> to avoid conflicting extension methods.
    /// </summary>
    internal sealed record class DataTypeCompilation
    {
        private const string DataTypeFieldAttributeName = "DataTypeFieldAttribute";

        /// <summary>
        /// The validated model, or null if structural validation failed.
        /// </summary>
        public DataTypeSourceModel Model { get; }

        /// <summary>
        /// The validated fields (empty for enums or on error).
        /// </summary>
        public IReadOnlyList<DataTypeSourceField> ValidFields { get; }

        /// <summary>
        /// Diagnostics from field validation.
        /// </summary>
        public IReadOnlyList<DataTypeSourceDiagnostic> Diagnostics { get; }

        /// <summary>Location for diagnostic reporting.</summary>
        public Location Location { get; }

        /// <summary>True if the model has fatal errors.</summary>
        public bool HasErrors { get; }

        /// <summary>Structural error message (non-partial, no ctor).</summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// Check whether the generator can handle the node.
        /// </summary>
        public static bool Handles(SyntaxNode node, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return node is TypeDeclarationSyntax t && t.AttributeLists.Count > 0;
        }

        /// <summary>
        /// Create data type compilation from a Roslyn symbol.
        /// Builds and validates the model eagerly.
        /// </summary>
        public DataTypeCompilation(
            GeneratorAttributeSyntaxContext context,
            CancellationToken cancellationToken)
        {
            INamedTypeSymbol symbol = (INamedTypeSymbol)context.TargetSymbol;
            Location = symbol.Locations.FirstOrDefault();

            AttributeData dataTypeAttr = context.Attributes.FirstOrDefault();
            string dataTypeNamespace =
                GetNamedArgString(dataTypeAttr, "Namespace");
            string dataTypeId =
                GetNamedArgString(dataTypeAttr, "DataTypeId");
            string binaryEncodingId =
                GetNamedArgString(dataTypeAttr, "BinaryEncodingId");
            string xmlEncodingId =
                GetNamedArgString(dataTypeAttr, "XmlEncodingId");
            string jsonEncodingId =
                GetNamedArgString(dataTypeAttr, "JsonEncodingId");

            if (symbol.TypeKind == TypeKind.Enum)
            {
                Model = BuildEnumModel(
                    symbol, dataTypeNamespace, dataTypeId,
                    binaryEncodingId, xmlEncodingId, jsonEncodingId);
                ValidFields = System.Array.Empty<DataTypeSourceField>();
                Diagnostics = System.Array.Empty<DataTypeSourceDiagnostic>();
                return;
            }

            bool isPartial = symbol.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax(cancellationToken)
                    is ClassDeclarationSyntax cds &&
                    cds.Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                HasErrors = true;
                ErrorMessage = "[DataType] class must be declared as partial.";
                ValidFields = System.Array.Empty<DataTypeSourceField>();
                Diagnostics = System.Array.Empty<DataTypeSourceDiagnostic>();
                return;
            }

            bool hasCtor = symbol.Constructors
                .Any(c => c.Parameters.Length == 0);
            if (!hasCtor)
            {
                HasErrors = true;
                ErrorMessage =
                    "[DataType] class must have a parameterless constructor.";
                ValidFields = System.Array.Empty<DataTypeSourceField>();
                Diagnostics = System.Array.Empty<DataTypeSourceDiagnostic>();
                return;
            }

            Model = BuildClassModel(
                symbol, dataTypeNamespace, dataTypeId,
                binaryEncodingId, xmlEncodingId, jsonEncodingId,
                cancellationToken);

            IReadOnlyList<DataTypeSourceDiagnostic> diags =
                DataTypeSourceGenerator.ValidateAndFilter(
                    Model, out IReadOnlyList<DataTypeSourceField> valid);

            ValidFields = valid;
            Diagnostics = diags;
            HasErrors = diags.Any(d => d.IsError);
        }

        /// <summary>
        /// Emit a batch of compilations as one file per namespace.
        /// </summary>
        public static void EmitBatch(
            SourceProductionContext sourceContext,
            ImmutableArray<DataTypeCompilation> compilations)
        {
            foreach (DataTypeCompilation comp in compilations)
            {
                if (comp.ErrorMessage != null)
                {
                    sourceContext.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.Exception,
                            comp.Location,
                            comp.ErrorMessage));
                }

                foreach (DataTypeSourceDiagnostic diag in comp.Diagnostics)
                {
                    sourceContext.ReportDiagnostic(
                        Diagnostic.Create(
                            diag.IsError
                                ? SourceGenerator.Exception
                                : SourceGenerator.GenericWarning,
                            comp.Location,
                            diag.Message));
                }
            }

            var validByNamespace = compilations
                .Where(c => !c.HasErrors && c.Model != null)
                .GroupBy(c => c.Model.Namespace);

            foreach (IGrouping<string, DataTypeCompilation> group
                in validByNamespace)
            {
                List<DataTypeCompilation> entries = group.ToList();
                DataTypeSourceModel first = entries[0].Model;

                var allTypes = new List<object>();
                var allActivators = new List<object>();
                var allRegistrations = new List<object>();

                foreach (DataTypeCompilation comp in entries)
                {
                    if (comp.Model.IsEnum)
                    {
                        allActivators.Add(comp.Model);
                        allRegistrations.Add(comp.Model);
                    }
                    else
                    {
                        allTypes.Add(new ClassBodyContext(
                            comp.Model, comp.ValidFields));
                        allActivators.Add(comp.Model);
                        allRegistrations.Add(comp.Model);
                    }
                }

                string source = DataTypeSourceGenerator.GenerateBatch(
                    first.Namespace,
                    first.NamespaceSymbol,
                    first.NamespaceUri,
                    allTypes,
                    allActivators,
                    allRegistrations);

                sourceContext.AddSource(
                    first.NamespaceSymbol + ".DataTypes.g.cs", source);
            }
        }

        private static DataTypeSourceModel BuildClassModel(
            INamedTypeSymbol symbol,
            string dataTypeNamespace,
            string dataTypeId,
            string binaryEncodingId,
            string xmlEncodingId,
            string jsonEncodingId,
            CancellationToken ct)
        {
            string ns = GetFullNamespace(symbol);
            string nsUri = ResolveNamespaceUri(
                symbol, dataTypeNamespace, ns);

            return new DataTypeSourceModel
            {
                ClassName = symbol.Name,
                Namespace = ns,
                NamespaceUri = nsUri,
                NamespaceSymbol = ns.Replace(".", string.Empty),
                DataTypeId = dataTypeId,
                BinaryEncodingId = binaryEncodingId,
                XmlEncodingId = xmlEncodingId,
                JsonEncodingId = jsonEncodingId,
                IsRecord = symbol.IsRecord,
                IsEnum = false,
                Fields = CollectFields(symbol, ct),
                BaseClassName = symbol.BaseType?.Name == "Object"
                    ? null : symbol.BaseType?.Name
            };
        }

        private static DataTypeSourceModel BuildEnumModel(
            INamedTypeSymbol symbol,
            string dataTypeNamespace,
            string dataTypeId,
            string binaryEncodingId,
            string xmlEncodingId,
            string jsonEncodingId)
        {
            string ns = GetFullNamespace(symbol);
            string nsUri = ResolveNamespaceUri(
                symbol, dataTypeNamespace, ns);

            var members = new List<DataTypeSourceEnumMember>();
            foreach (ISymbol member in symbol.GetMembers())
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
                ClassName = symbol.Name,
                Namespace = ns,
                NamespaceUri = nsUri,
                NamespaceSymbol = ns.Replace(".", string.Empty),
                DataTypeId = dataTypeId,
                BinaryEncodingId = binaryEncodingId,
                XmlEncodingId = xmlEncodingId,
                JsonEncodingId = jsonEncodingId,
                IsEnum = true,
                IsFlags = symbol.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "FlagsAttribute"),
                EnumMembers = members
            };
        }

        private static List<DataTypeSourceField> CollectFields(
            INamedTypeSymbol symbol, CancellationToken ct)
        {
            IPropertySymbol[] allProperties = symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => !p.IsAbstract && !p.IsStatic &&
                    !p.IsReadOnly &&
                    p.DeclaredAccessibility == Accessibility.Public)
                .ToArray();

            bool hasAttr = allProperties.Any(p =>
                p.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == DataTypeFieldAttributeName));

            IPropertySymbol[] selected = hasAttr
                ? allProperties.Where(p =>
                    p.GetAttributes().Any(a =>
                        a.AttributeClass?.Name ==
                            DataTypeFieldAttributeName))
                    .ToArray()
                : allProperties;

            var fields = new List<DataTypeSourceField>();
            int orderIndex = 0;
            foreach (IPropertySymbol prop in selected)
            {
                ct.ThrowIfCancellationRequested();
                AttributeData dtfAttr = prop.GetAttributes()
                    .FirstOrDefault(a =>
                        a.AttributeClass?.Name ==
                            DataTypeFieldAttributeName);
                int order = dtfAttr != null
                    ? GetNamedArgInt(dtfAttr, "Order", orderIndex)
                    : orderIndex;
                string fieldName = dtfAttr != null
                    ? GetNamedArgString(dtfAttr, "Name") ?? prop.Name
                    : prop.Name;

                fields.Add(CreateField(
                    prop, fieldName, order, dtfAttr != null));
                orderIndex++;
            }

            fields.Sort((a, b) => a.Order.CompareTo(b.Order));
            return fields;
        }

        private static DataTypeSourceField CreateField(
            IPropertySymbol prop, string fieldName,
            int order, bool hasDataTypeFieldAttr)
        {
            ITypeSymbol type = prop.Type;
            string shortName = type.Name;
            bool isNullable =
                prop.NullableAnnotation == NullableAnnotation.Annotated;
            bool isEncodeable =
                ImplementsInterface(type, "IEncodeable");
            bool isEnum = type.TypeKind == TypeKind.Enum;
            bool isArray = false;
            bool isMatrix = false;
            string elementShortTypeName = null;
            string elementTypeName = null;

            if (shortName == "ArrayOf" &&
                type is INamedTypeSymbol arrayType &&
                arrayType.IsGenericType &&
                arrayType.TypeArguments.Length == 1)
            {
                isArray = true;
                ITypeSymbol elem = arrayType.TypeArguments[0];
                elementShortTypeName = elem.Name;
                elementTypeName = GetFullyQualifiedTypeName(elem);
                isEncodeable =
                    ImplementsInterface(elem, "IEncodeable");
                isEnum = elem.TypeKind == TypeKind.Enum;
            }
            else if (shortName == "MatrixOf" &&
                type is INamedTypeSymbol matrixType &&
                matrixType.IsGenericType &&
                matrixType.TypeArguments.Length == 1)
            {
                isMatrix = true;
                ITypeSymbol elem = matrixType.TypeArguments[0];
                elementShortTypeName = elem.Name;
                elementTypeName = GetFullyQualifiedTypeName(elem);
                isEncodeable =
                    ImplementsInterface(elem, "IEncodeable");
                isEnum = elem.TypeKind == TypeKind.Enum;
            }

            return new DataTypeSourceField
            {
                PropertyName = prop.Name,
                FieldName = fieldName,
                TypeName = GetFullyQualifiedTypeName(prop.Type),
                ShortTypeName = shortName,
                IsArray = isArray,
                IsMatrix = isMatrix,
                ElementShortTypeName = elementShortTypeName,
                ElementTypeName = elementTypeName,
                IsOptional = isNullable,
                IsEncodeable = isEncodeable,
                IsEnum = isEnum,
                Order = order,
                HasDataTypeFieldAttribute = hasDataTypeFieldAttr
            };
        }

        private static string ResolveNamespaceUri(
            INamedTypeSymbol symbol, string dataTypeNamespace,
            string dotNetNamespace)
        {
            if (!string.IsNullOrEmpty(dataTypeNamespace))
            {
                return dataTypeNamespace;
            }

            AttributeData dcAttr = symbol.GetAttributes()
                .FirstOrDefault(a =>
                    a.AttributeClass?.Name ==
                        nameof(DataContractAttribute));
            if (dcAttr != null)
            {
                string dcNs = GetNamedArgString(dcAttr, "Namespace");
                if (!string.IsNullOrEmpty(dcNs))
                {
                    return dcNs;
                }
            }

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
                    .WithGlobalNamespaceStyle(
                        SymbolDisplayGlobalNamespaceStyle.Omitted));
        }

        private static bool ImplementsInterface(
            ITypeSymbol type, string interfaceName)
        {
            return type.AllInterfaces
                .Any(i => i.Name == interfaceName);
        }

        private static string GetNamedArgString(
            AttributeData attr, string name)
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

        private static int GetNamedArgInt(
            AttributeData attr, string name, int defaultValue)
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
    }
}
