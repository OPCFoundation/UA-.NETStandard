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
    /// Builds and validates a <see cref="TypeSourceModel"/> during
    /// construction. Models are collected via <c>.Collect()</c> and
    /// emitted as a batch (one file per namespace) by
    /// <see cref="EmitBatch"/> to avoid conflicting extension methods.
    /// </summary>
    internal sealed record class DataTypeCompilation
    {
        /// <summary>
        /// The validated model, or null if structural validation failed.
        /// </summary>
        public TypeSourceModel Model { get; }

        /// <summary>
        /// The validated fields (empty for enums or on error).
        /// </summary>
        public IReadOnlyList<TypeFieldModel> ValidFields { get; }

        /// <summary>
        /// Diagnostics from field validation.
        /// </summary>
        public IReadOnlyList<TypeSourceGeneratorDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Location for diagnostic reporting.
        /// </summary>
        public Location Location { get; }

        /// <summary>
        /// True if the model has fatal errors.
        /// </summary>
        public bool HasErrors { get; }

        /// <summary>
        /// Structural error message (non-partial, no ctor).
        /// </summary>
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
                dataTypeAttr.GetValue(nameof(DataTypeAttribute.Namespace));
            string dataTypeId =
                dataTypeAttr.GetValue(nameof(DataTypeAttribute.DataTypeId));
            string binaryEncodingId =
                dataTypeAttr.GetValue(
                    nameof(DataTypeAttribute.BinaryEncodingId));
            string xmlEncodingId =
                dataTypeAttr.GetValue(nameof(DataTypeAttribute.XmlEncodingId));
            string jsonEncodingId =
                dataTypeAttr.GetValue(
                    nameof(DataTypeAttribute.JsonEncodingId));

            try
            {
                if (symbol.TypeKind == TypeKind.Enum)
                {
                    Model = BuildEnumModel(
                        symbol, dataTypeNamespace, dataTypeId,
                        binaryEncodingId, xmlEncodingId, jsonEncodingId);
                    ValidFields = System.Array.Empty<TypeFieldModel>();
                    Diagnostics =
                        System.Array.Empty<TypeSourceGeneratorDiagnostic>();
                    return;
                }

                bool isPartial = symbol.DeclaringSyntaxReferences
                    .Any(r => r.GetSyntax(cancellationToken)
                        is TypeDeclarationSyntax tds &&
                        tds.Modifiers.Any(SyntaxKind.PartialKeyword));
                if (!isPartial)
                {
                    HasErrors = true;
                    ErrorMessage =
                        "[DataType] class must be declared as partial.";
                    ValidFields = System.Array.Empty<TypeFieldModel>();
                    Diagnostics =
                        System.Array.Empty<TypeSourceGeneratorDiagnostic>();
                    return;
                }

                bool hasCtor = symbol.Constructors
                    .Any(c => c.Parameters.Length == 0);
                if (!hasCtor)
                {
                    HasErrors = true;
                    ErrorMessage =
                        "[DataType] class must have a parameterless ctor.";
                    ValidFields = System.Array.Empty<TypeFieldModel>();
                    Diagnostics =
                        System.Array.Empty<TypeSourceGeneratorDiagnostic>();
                    return;
                }

                Model = BuildClassModel(
                    symbol, dataTypeNamespace, dataTypeId,
                    binaryEncodingId, xmlEncodingId, jsonEncodingId,
                    cancellationToken);

                IReadOnlyList<TypeSourceGeneratorDiagnostic> diags =
                    TypeSourceGenerator.ValidateAndFilter(
                        Model, out IReadOnlyList<TypeFieldModel> valid);

                ValidFields = valid;
                Diagnostics = diags;
                HasErrors = diags.Any(d => d.IsError);
            }
            catch (System.Exception ex)
            {
                HasErrors = true;
                ErrorMessage =
                    $"[DataType] generator error for '{symbol.Name}': " +
                    $"{ex.GetType().Name}: {ex.Message}";
                ValidFields ??= System.Array.Empty<TypeFieldModel>();
                Diagnostics ??=
                    System.Array.Empty<TypeSourceGeneratorDiagnostic>();
            }
        }

        /// <summary>
        /// Emit a batch of compilations as one file per namespace.
        /// </summary>
        public static void EmitBatch(
            SourceProductionContext sourceContext,
            ImmutableArray<DataTypeCompilation> compilations,
            bool publicExtensions)
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

                foreach (TypeSourceGeneratorDiagnostic diag in comp.Diagnostics)
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

            IEnumerable<IGrouping<string, DataTypeCompilation>> validByNamespace =
                compilations
                    .Where(c => !c.HasErrors && c.Model != null)
                    .GroupBy(c => c.Model.Namespace);
            foreach (IGrouping<string, DataTypeCompilation> group in validByNamespace)
            {
                List<DataTypeCompilation> entries = [.. group];
                TypeSourceModel first = entries[0].Model;

                var allTypes = new List<TypeSourceModel>();
                var allActivators = new List<TypeSourceModel>();

                foreach (DataTypeCompilation comp in entries)
                {
                    TypeSourceModel model = comp.Model with
                    {
                        PublicExtensions = publicExtensions
                    };
                    if (model.IsEnum)
                    {
                        allActivators.Add(model);
                    }
                    else
                    {
                        allTypes.Add(model with { Fields = comp.ValidFields });
                        allActivators.Add(model);
                    }
                }

                string source = TypeSourceGenerator.GenerateBatch(
                    first.Namespace,
                    first.NamespaceSymbol,
                    first.NamespaceUri,
                    publicExtensions,
                    allTypes,
                    allActivators);

                sourceContext.AddSource(
                    first.NamespaceSymbol + ".Types.g.cs", source);
            }
        }

        private static TypeSourceModel BuildClassModel(
            INamedTypeSymbol symbol,
            string dataTypeNamespace,
            string dataTypeId,
            string binaryEncodingId,
            string xmlEncodingId,
            string jsonEncodingId,
            CancellationToken ct)
        {
            string ns = symbol.GetFullNamespace();
            bool baseTypeIsEncodeable = symbol.BaseType != null &&
                symbol.BaseType.Name != "Object" &&
                (symbol.BaseType.ImplementsInterface("IEncodeable") ||
                    symbol.BaseType.HasAttribute("DataTypeAttribute"));
            return new TypeSourceModel
            {
                ClassName = symbol.Name,
                Namespace = ns,
                NamespaceUri = ResolveNamespaceUri(
                    symbol, dataTypeNamespace, ns),
                NamespaceSymbol = ns.Replace(".", string.Empty),
                DataTypeId = dataTypeId,
                BinaryEncodingId = binaryEncodingId,
                XmlEncodingId = xmlEncodingId,
                JsonEncodingId = jsonEncodingId,
                IsRecord = symbol.IsRecord,
                IsEnum = false,
                IsSealed = symbol.IsSealed,
                IsDerived = baseTypeIsEncodeable,
                IsInternal =
                    symbol.DeclaredAccessibility == Accessibility.Internal ||
                    symbol.DeclaredAccessibility == Accessibility.NotApplicable,
                BaseTypeIsEncodeable = baseTypeIsEncodeable,
                HasManualClone = symbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Any(m => m.Name is "Clone" or "MemberwiseClone" &&
                        !m.IsImplicitlyDeclared),
                Fields = CollectFields(symbol, ct),
                BaseClassName = symbol.BaseType?.Name == "Object"
                    ? null : symbol.BaseType?.Name
            };
        }

        private static TypeSourceModel BuildEnumModel(
            INamedTypeSymbol symbol,
            string dataTypeNamespace,
            string dataTypeId,
            string binaryEncodingId,
            string xmlEncodingId,
            string jsonEncodingId)
        {
            string ns = symbol.GetFullNamespace();
            var members = new List<TypeEnumMember>();
            foreach (ISymbol member in symbol.GetMembers())
            {
                if (member is IFieldSymbol field && field.HasConstantValue)
                {
                    members.Add(new TypeEnumMember
                    {
                        Name = field.Name,
                        Value = field.ConstantValue?.ToString() ?? "0"
                    });
                }
            }

            return new TypeSourceModel
            {
                ClassName = symbol.Name,
                Namespace = ns,
                NamespaceUri = ResolveNamespaceUri(
                    symbol, dataTypeNamespace, ns),
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

        private static List<TypeFieldModel> CollectFields(
            INamedTypeSymbol symbol, CancellationToken ct)
        {
            // Get ALL non-abstract, non-static properties regardless of
            // accessibility for [DataTypeField] scanning.
            IPropertySymbol[] allProperties =
            [
                .. symbol.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(p => !p.IsAbstract &&
                        !p.IsStatic &&
                        !p.IsReadOnly)
            ];
            // Check if any property has [DataTypeField] — if so, use only those
            Tuple<IPropertySymbol, AttributeData>[] selectedPropsWithAttribute =
            [
                .. allProperties
                    .Select(p => Tuple.Create(p, p.GetAttributes()
                        .FirstOrDefault(a =>
                            a.AttributeClass?.Name == nameof(DataTypeFieldAttribute))))
                    .Where(p => p.Item2 != null)
            ];
            var fields = new List<TypeFieldModel>();
            int orderIndex = 0;
            if (selectedPropsWithAttribute.Length == 0)
            {
                // None annotated — auto-discover PUBLIC properties only
                foreach (IPropertySymbol prop in allProperties)
                {
                    if (prop.DeclaredAccessibility != Accessibility.Public)
                    {
                        continue;
                    }
                    ct.ThrowIfCancellationRequested();
                    fields.Add(CreateField(prop, prop.Name, orderIndex++, false));
                }
            }
            else
            {
                // Use only annotated properties (any accessibility)
                foreach (Tuple<IPropertySymbol, AttributeData> propWithAttribute
                    in selectedPropsWithAttribute)
                {
                    ct.ThrowIfCancellationRequested();
                    AttributeData dtfAttr = propWithAttribute.Item2;
                    IPropertySymbol prop = propWithAttribute.Item1;
                    orderIndex = dtfAttr.GetInteger(
                        nameof(DataTypeFieldAttribute.Order),
                        ++orderIndex);
                    string fieldName = dtfAttr.GetValue(
                        nameof(DataTypeFieldAttribute.Name))
                        ?? prop.Name;
                    fields.Add(CreateField(prop, fieldName, orderIndex, true, dtfAttr));
                }
            }
            fields.Sort((a, b) => a.Order.CompareTo(b.Order));
            return fields;
        }

        private static TypeFieldModel CreateField(
            IPropertySymbol prop, string fieldName,
            int order, bool hasDataTypeFieldAttr,
            AttributeData dtfAttr = null)
        {
            ITypeSymbol type = prop.Type;
            string shortName = type.Name;
            bool isNullable =
                prop.NullableAnnotation == NullableAnnotation.Annotated;
            bool isEnum = type.TypeKind == TypeKind.Enum;
            bool isEncodeable = !isEnum &&
                (type.ImplementsInterface(nameof(IEncodeable)) ||
                    type.HasAttribute(nameof(DataTypeAttribute)));
            bool isArray = false;
            bool isMatrix = false;
            string elementShortTypeName = null;
            string elementTypeName = null;
            ITypeSymbol encodeableType = type;

            if (shortName == "ArrayOf" &&
                type is INamedTypeSymbol arrayType &&
                arrayType.IsGenericType &&
                arrayType.TypeArguments.Length == 1)
            {
                isArray = true;
                ITypeSymbol elem = arrayType.TypeArguments[0];
                elementShortTypeName = elem.Name;
                elementTypeName = elem.GetFullyQualifiedTypeName();
                isEnum = elem.TypeKind == TypeKind.Enum;
                isEncodeable = !isEnum &&
                    (elem.ImplementsInterface(nameof(IEncodeable)) ||
                        elem.HasAttribute(nameof(DataTypeAttribute)));
                encodeableType = elem;
            }
            else if (shortName == "MatrixOf" &&
                type is INamedTypeSymbol matrixType &&
                matrixType.IsGenericType &&
                matrixType.TypeArguments.Length == 1)
            {
                isMatrix = true;
                ITypeSymbol elem = matrixType.TypeArguments[0];
                elementShortTypeName = elem.Name;
                elementTypeName = elem.GetFullyQualifiedTypeName();
                isEnum = elem.TypeKind == TypeKind.Enum;
                isEncodeable = !isEnum &&
                    (elem.ImplementsInterface(nameof(IEncodeable)) ||
                        elem.HasAttribute(nameof(DataTypeAttribute)));
                encodeableType = elem;
            }

            int structureHandling = 0;
            int defaultValueHandling = 0;
            if (dtfAttr != null)
            {
                foreach (KeyValuePair<string, TypedConstant> kvp in dtfAttr.NamedArguments)
                {
                    switch (kvp.Key)
                    {
                        case "StructureHandling" when kvp.Value.Value is int sh:
                            structureHandling = sh;
                            break;
                        case "DefaultValueHandling" when kvp.Value.Value is int dvh:
                            defaultValueHandling = dvh;
                            break;
                    }
                }
            }

            bool fieldTypeIsSealed = encodeableType.IsSealed;
            bool fieldTypeHasEncodeableBase =
                encodeableType is INamedTypeSymbol namedFieldType &&
                namedFieldType.BaseType != null &&
                namedFieldType.BaseType.Name != "Object" &&
                namedFieldType.BaseType.ImplementsInterface("IEncodeable");

            return new TypeFieldModel
            {
                PropertyName = prop.Name,
                FieldName = fieldName,
                TypeName = prop.Type.GetFullyQualifiedTypeName(),
                ShortTypeName = shortName,
                IsArray = isArray,
                IsMatrix = isMatrix,
                ElementShortTypeName = elementShortTypeName,
                ElementTypeName = elementTypeName,
                IsOptional = isNullable,
                IsEncodeable = isEncodeable,
                IsEnum = isEnum,
                Order = order,
                HasDataTypeFieldAttribute = hasDataTypeFieldAttr,
                StructureHandling = structureHandling,
                DefaultValueHandling = defaultValueHandling,
                FieldTypeIsSealed = fieldTypeIsSealed,
                FieldTypeHasEncodeableBase = fieldTypeHasEncodeableBase,
                IsInitOnly = HasInitOnlySetter(prop),
                BackingFieldName = HasInitOnlySetter(prop)
                    ? $"__{prop.Name}"
                    : null
            };
        }

        /// <summary>
        /// Detects whether a property has an init-only setter by
        /// checking both the semantic model (IsInitOnly) and the
        /// syntax tree (init keyword). The syntax check is needed
        /// for partial property definitions where the semantic
        /// model may not expose the init accessor.
        /// </summary>
        private static bool HasInitOnlySetter(IPropertySymbol prop)
        {
            if (prop.SetMethod?.IsInitOnly == true)
            {
                return true;
            }

            // For partial property definitions the semantic model
            // may not expose IsInitOnly. Fall back to syntax check.
            foreach (SyntaxReference syntaxRef in prop.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax propSyntax &&
                    propSyntax.AccessorList != null)
                {
                    foreach (AccessorDeclarationSyntax accessor in
                        propSyntax.AccessorList.Accessors)
                    {
                        if (accessor.Kind() == SyntaxKind.InitAccessorDeclaration)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
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
                    a.AttributeClass?.Name == nameof(DataContractAttribute));
            if (dcAttr != null)
            {
                string dcNs = dcAttr.GetValue(nameof(DataContractAttribute.Namespace));
                if (!string.IsNullOrEmpty(dcNs))
                {
                    return dcNs;
                }
            }

            return "urn:" + dotNetNamespace.ToLowerInvariant();
        }
    }
}
