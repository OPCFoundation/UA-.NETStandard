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
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Handles a single [DataType]-annotated class or enum.
    /// Builds and validates a <see cref="TypeSourceModel"/> during
    /// construction. Models are collected via <c>.Collect()</c> and
    /// emitted as a batch (one file per namespace) by
    /// <see cref="EmitBatch"/> to avoid conflicting extension methods.
    /// </summary>
    /// <remarks>
    /// This is the output of a <c>ForAttributeWithMetadataName</c> transform,
    /// which runs again on every compilation change. Every member compares by
    /// value (lists are <see cref="EquatableArray{T}"/>, the location is a
    /// <see cref="LocationInfo"/>) so an unchanged type leaves the emitted
    /// sources cached and does not pin old syntax trees.
    /// </remarks>
    internal sealed record class DataTypeCompilation
    {
        /// <summary>
        /// The validated model, or null if structural validation failed.
        /// </summary>
        public TypeSourceModel Model { get; }

        /// <summary>
        /// The validated fields (empty for enums or on error).
        /// </summary>
        public EquatableArray<TypeFieldModel> ValidFields { get; }

        /// <summary>
        /// Diagnostics from field validation.
        /// </summary>
        public EquatableArray<(bool IsError, string Message)> Diagnostics { get; }

        /// <summary>
        /// Location for diagnostic reporting.
        /// </summary>
        public LocationInfo Location { get; }

        /// <summary>
        /// Why the annotated type cannot be generated (a struct, a generic
        /// type, a non-partial or inaccessible containing type), or
        /// <c>null</c>.
        /// </summary>
        public string UnsupportedReason { get; }

        /// <summary>
        /// True if the model has fatal errors.
        /// </summary>
        public bool HasErrors { get; }

        /// <summary>
        /// Structural error message (non-partial, no ctor).
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// The annotated type name used for diagnostics.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// An explicitly supplied namespace expression that Roslyn could not resolve.
        /// </summary>
        public string UnresolvedNamespaceExpression { get; } = string.Empty;

        /// <summary>
        /// The encodeable base type whose data type definition cannot be
        /// resolved (MODELGEN038), or empty.
        /// </summary>
        public string UnresolvedBaseDefinition { get; } = string.Empty;

        /// <summary>
        /// Check whether the generator can handle the node.
        /// </summary>
        public static bool Handles(SyntaxNode node, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // BaseTypeDeclarationSyntax rather than TypeDeclarationSyntax: an
            // enum declaration is not a TypeDeclarationSyntax, and [DataType] on
            // an enum is supported (see BuildEnumModel).
            return node is BaseTypeDeclarationSyntax t && t.AttributeLists.Count > 0;
        }

        /// <summary>
        /// Create data type compilation from a Roslyn symbol.
        /// Builds and validates the model eagerly.
        /// </summary>
        public DataTypeCompilation(
            GeneratorAttributeSyntaxContext context,
            CancellationToken cancellationToken)
        {
            var symbol = (INamedTypeSymbol)context.TargetSymbol;
            TypeName = symbol.ToDisplayString();
            Location = LocationInfo.From(symbol.Locations.FirstOrDefault());
            ValidFields = EquatableArray<TypeFieldModel>.Empty;
            Diagnostics = EquatableArray<(bool, string)>.Empty;

            UnsupportedReason = GetUnsupportedReason(symbol, cancellationToken);
            if (UnsupportedReason != null)
            {
                HasErrors = true;
                return;
            }

            AttributeData dataTypeAttr = context.Attributes.FirstOrDefault();
            AttributeArgumentSyntax unresolvedNamespaceArgument =
                GetUnresolvedNamespaceArgument(dataTypeAttr, cancellationToken);
            if (unresolvedNamespaceArgument != null)
            {
                Location = LocationInfo.From(unresolvedNamespaceArgument.GetLocation());
                UnresolvedNamespaceExpression =
                    unresolvedNamespaceArgument.Expression.ToString();
                HasErrors = true;
                return;
            }

            string dataTypeNamespace =
                dataTypeAttr.GetValue(nameof(DataTypeAttribute.Namespace));
            string dataTypeId =
                dataTypeAttr.GetValue(nameof(DataTypeAttribute.DataTypeId));
            string binaryEncodingId =
                dataTypeAttr.GetValue(
                    nameof(DataTypeAttribute.BinaryEncodingId));
            string xmlEncodingId =
                dataTypeAttr.GetValue(nameof(DataTypeAttribute.XmlEncodingId));

            try
            {
                if (symbol.TypeKind == TypeKind.Enum)
                {
                    Model = BuildEnumModel(
                        symbol, dataTypeNamespace, dataTypeId,
                        binaryEncodingId, xmlEncodingId);
                    return;
                }

                if (!IsPartial(symbol, cancellationToken))
                {
                    HasErrors = true;
                    ErrorMessage =
                        "[DataType] class must be declared as partial.";
                    return;
                }

                // The namespace-level activator calls the parameterless
                // constructor, so it must be reachable from there.
                bool hasCtor = symbol.InstanceConstructors
                    .Any(c => c.Parameters.Length == 0 &&
                        c.DeclaredAccessibility is Accessibility.Public or
                            Accessibility.Internal or
                            Accessibility.ProtectedOrInternal);
                if (!hasCtor)
                {
                    HasErrors = true;
                    ErrorMessage =
                        "[DataType] class must have a public or internal parameterless ctor.";
                    return;
                }

                Model = BuildClassModel(
                    symbol, dataTypeNamespace, dataTypeId,
                    binaryEncodingId, xmlEncodingId,
                    cancellationToken);

                IReadOnlyList<TypeSourceGeneratorDiagnostic> diags =
                    TypeSourceGenerator.ValidateAndFilter(
                        Model, out IReadOnlyList<TypeFieldModel> valid);

                // Encode writes the base fields first, which the definition
                // can only describe through the base type's definition.
                if (Model.IsDerived && Model.BaseDefinitionActivator == null)
                {
                    UnresolvedBaseDefinition = symbol.BaseType.ToDisplayString();
                }

                ValidFields = EquatableArray<TypeFieldModel>.From(valid);
                Diagnostics = EquatableArray<(bool, string)>.From(
                    diags.Select(d => (d.IsError, d.Message)));
                HasErrors = diags.Any(d => d.IsError);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Cancellation must reach the driver; caching it as a model
                // error would keep reporting it until an input changes.
                HasErrors = true;
                // The annotated type is reported as the diagnostic's first
                // argument, so the message itself carries only the failure.
                ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        private static bool IsPartial(INamedTypeSymbol symbol, CancellationToken cancellationToken)
        {
            return symbol.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax(cancellationToken)
                    is TypeDeclarationSyntax tds &&
                    tds.Modifiers.Any(SyntaxKind.PartialKeyword));
        }

        /// <summary>
        /// Returns why the generated members cannot be attached to the
        /// annotated type, or <c>null</c> when they can.
        /// </summary>
        /// <remarks>
        /// The generated declaration is a <c>partial class</c> or
        /// <c>partial record class</c>, so a struct cannot be completed. The
        /// activator and registration are emitted at namespace level, so a
        /// generic type (or a type nested in one) has no closed type to
        /// activate, and a nested type must be reachable from its namespace.
        /// A nested type is emitted inside partial declarations of its
        /// containing types, which therefore must be partial themselves.
        /// </remarks>
        private static string GetUnsupportedReason(
            INamedTypeSymbol symbol,
            CancellationToken cancellationToken)
        {
            if (symbol.TypeKind == TypeKind.Struct)
            {
                return "[DataType] is not supported on a struct; declare the type as a " +
                    "partial class or partial record class";
            }
            if (symbol.IsGenericType)
            {
                return "[DataType] is not supported on a generic type or a type nested in a " +
                    "generic type";
            }
            // The generated code is emitted into the type's namespace, which
            // the global namespace cannot be written as.
            if (symbol.ContainingNamespace?.IsGlobalNamespace != false)
            {
                return "[DataType] is not supported on a type in the global namespace; " +
                    "declare the type in a namespace";
            }
            // An abstract class cannot be created by the generated activator.
            if (symbol.TypeKind == TypeKind.Class && symbol.IsAbstract && !symbol.IsStatic)
            {
                return "[DataType] is not supported on an abstract class, which the " +
                    "generated activator cannot create";
            }
            for (INamedTypeSymbol type = symbol; type != null; type = type.ContainingType)
            {
                // A file-local type is not visible outside its file, so the
                // generated partial declaration would declare a different type.
                if (type.IsFileLocal)
                {
                    return "'" + type.ToDisplayString() + "' is file-local, which generated " +
                        "code in another file cannot complete or reach";
                }
                if (type.DeclaredAccessibility is Accessibility.Private or
                    Accessibility.Protected or
                    Accessibility.ProtectedAndInternal)
                {
                    return "'" + type.ToDisplayString() + "' must be public or internal so " +
                        "the generated activator can reach the [DataType] type";
                }
                // An enum gets no generated declaration, so only a class
                // needs its containing types to be partial.
                if (!ReferenceEquals(type, symbol) &&
                    symbol.TypeKind != TypeKind.Enum &&
                    !IsPartial(type, cancellationToken))
                {
                    return "the containing type '" + type.ToDisplayString() +
                        "' must be declared partial";
                }
            }
            return null;
        }

        /// <summary>
        /// True if the type and all its containing types are declared public,
        /// so the namespace-level activator can be public as well.
        /// </summary>
        private static bool IsEffectivelyPublic(INamedTypeSymbol symbol)
        {
            for (INamedTypeSymbol type = symbol; type != null; type = type.ContainingType)
            {
                if (type.DeclaredAccessibility != Accessibility.Public)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Fill in the nesting of a nested type: the partial declarations of
        /// its containing types, its qualified name and a unique identifier
        /// for the namespace-level activator.
        /// </summary>
        private static TypeSourceModel WithNesting(
            TypeSourceModel model,
            INamedTypeSymbol symbol)
        {
            if (symbol.ContainingType == null)
            {
                return model;
            }
            var declarations = new List<string>();
            var names = new List<string>();
            for (INamedTypeSymbol type = symbol.ContainingType;
                type != null;
                type = type.ContainingType)
            {
                declarations.Insert(0, GetPartialDeclaration(type));
                names.Insert(0, type.Name);
            }
            names.Add(symbol.Name);
            return model with
            {
                ContainingTypeDeclarations = new EquatableArray<string>([.. declarations]),
                TypeReference = symbol.GetFullyQualifiedTypeName(),
                SymbolName = string.Join("_", names),
                // Two nested types of the same name in one namespace URI
                // must not share the default DataTypeId and XML name.
                QualifiedName = string.Join(".", names),
                AccessModifier = symbol.DeclaredAccessibility switch
                {
                    Accessibility.Internal => "internal",
                    Accessibility.ProtectedOrInternal => "protected internal",
                    _ => "public"
                }
            };
        }

        /// <summary>
        /// A partial declaration of a containing type, without accessibility
        /// (a partial part may omit it) and without base list.
        /// </summary>
        private static string GetPartialDeclaration(INamedTypeSymbol type)
        {
            var modifiers = new List<string>();
            if (type.IsStatic)
            {
                modifiers.Add("static");
            }
            if (type.TypeKind == TypeKind.Struct && type.IsReadOnly)
            {
                modifiers.Add("readonly");
            }
            if (type.IsRefLikeType)
            {
                modifiers.Add("ref");
            }
            modifiers.Add("partial");
            modifiers.Add(type.TypeKind switch
            {
                TypeKind.Struct when type.IsRecord => "record struct",
                TypeKind.Struct => "struct",
                TypeKind.Interface => "interface",
                _ when type.IsRecord => "record class",
                _ => "class"
            });
            modifiers.Add(type.Name);
            return string.Join(" ", modifiers);
        }

        /// <summary>
        /// Report the diagnostics of a batch of compilations. The locations
        /// are re-created in the syntax trees of <paramref name="compilation"/>
        /// so that <c>#pragma warning</c>, per-file severity configuration and
        /// <c>#line</c> mapping apply to them. Kept apart from
        /// <see cref="EmitBatch"/> so the source output does not depend on the
        /// compilation and stays cached.
        /// </summary>
        public static void ReportDiagnostics(
            SourceProductionContext sourceContext,
            ImmutableArray<DataTypeCompilation> compilations,
            Compilation compilation)
        {
            foreach (DataTypeCompilation comp in compilations)
            {
                if (comp.UnsupportedReason == null &&
                    comp.UnresolvedNamespaceExpression.Length == 0 &&
                    comp.ErrorMessage == null &&
                    comp.Diagnostics.Count == 0 &&
                    comp.UnresolvedBaseDefinition.Length == 0)
                {
                    continue;
                }
                Location location = comp.Location.ToLocation(compilation);
                if (comp.UnresolvedBaseDefinition.Length > 0)
                {
                    sourceContext.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.DataTypeBaseDefinitionUnresolved,
                            location,
                            comp.TypeName,
                            comp.UnresolvedBaseDefinition));
                }
                if (comp.UnsupportedReason != null)
                {
                    sourceContext.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.DataTypeUnsupportedTarget,
                            location,
                            comp.TypeName,
                            comp.UnsupportedReason));
                    continue;
                }

                if (comp.UnresolvedNamespaceExpression.Length > 0)
                {
                    sourceContext.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.DataTypeNamespaceUnresolved,
                            location,
                            comp.TypeName,
                            comp.UnresolvedNamespaceExpression));
                    continue;
                }

                // MODELGEN003 takes two arguments ("... '{0}': {1}"); supplying
                // only one leaves the message rendered as the raw template.
                if (comp.ErrorMessage != null)
                {
                    sourceContext.ReportDiagnostic(
                        Diagnostic.Create(
                            SourceGenerator.Exception,
                            location,
                            comp.TypeName,
                            comp.ErrorMessage));
                }

                foreach ((bool isError, string message) in comp.Diagnostics)
                {
                    sourceContext.ReportDiagnostic(
                        isError
                            ? Diagnostic.Create(
                                SourceGenerator.Exception,
                                location,
                                comp.TypeName,
                                message)
                            : Diagnostic.Create(
                                SourceGenerator.GenericWarning,
                                location,
                                message));
                }
            }
        }

        /// <summary>
        /// Emit a batch of compilations as one file per namespace. The
        /// diagnostics are reported by <see cref="ReportDiagnostics"/>.
        /// </summary>
        public static void EmitBatch(
            SourceProductionContext sourceContext,
            ImmutableArray<DataTypeCompilation> compilations,
            bool publicExtensions)
        {
            // Roslyn compares hint names case-insensitively, so namespaces
            // differing only in case ("Acme.Types" and "Acme.types") would
            // claim the same hint name and the second AddSource would throw.
            var hintNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<DataTypeCompilation> valid = [.. compilations
                .Where(c => !c.HasErrors && c.Model != null)];
            Dictionary<string, (string Namespace, string SymbolName)> renamed =
                GetUniqueSymbolNames(valid);
            IEnumerable<IGrouping<string, DataTypeCompilation>> validByNamespace =
                valid.GroupBy(c => c.Model.Namespace);
            foreach (IGrouping<string, DataTypeCompilation> group in validByNamespace)
            {
                List<DataTypeCompilation> entries = [.. group];
                TypeSourceModel first = entries[0].Model;

                var allTypes = new List<TypeSourceModel>();
                var allActivators = new List<TypeSourceModel>();

                foreach (DataTypeCompilation comp in entries)
                {
                    TypeSourceModel model = WithUniqueSymbolNames(
                        comp.Model with
                        {
                            PublicExtensions = publicExtensions
                        },
                        renamed);
                    if (model.IsEnum)
                    {
                        allActivators.Add(model);
                    }
                    else
                    {
                        TypeSourceModel withValidFields =
                            model with { Fields = comp.ValidFields };
                        allTypes.Add(withValidFields);
                        allActivators.Add(withValidFields);
                    }
                }

                string source = TypeSourceGenerator.GenerateBatch(
                    first.Namespace,
                    first.NamespaceSymbol,
                    first.NamespaceUri,
                    publicExtensions,
                    allTypes,
                    allActivators);

                // Keyed on the namespace itself, not on NamespaceSymbol: the
                // latter has the dots stripped to form a C# identifier, so
                // "A.BC" and "AB.C" would claim the same hint name and the
                // second AddSource would fail the generator.
                string hintName = first.Namespace + ".Types.g.cs";
                for (int ii = 2; !hintNames.Add(hintName); ii++)
                {
                    hintName = first.Namespace + ".Types" +
                        ii.ToString(CultureInfo.InvariantCulture) + ".g.cs";
                }
                sourceContext.AddSource(hintName, source);
            }
        }

        /// <summary>
        /// Makes the activator names unique within each namespace. A nested
        /// type is named after its nesting chain joined with '_', which can
        /// equal the name of a top-level type (<c>Models.Foo</c> and
        /// <c>Models_Foo</c>) or of another nested type (<c>A.B_C</c> and
        /// <c>A_B.C</c>). Top-level types keep their names; a colliding nested
        /// type gets a numeric suffix, assigned in the order of the fully
        /// qualified type names so it does not depend on the input order.
        /// </summary>
        /// <returns>The new symbol names by fully qualified type name.</returns>
        private static Dictionary<string, (string Namespace, string SymbolName)> GetUniqueSymbolNames(
            List<DataTypeCompilation> compilations)
        {
            var renamed = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (IGrouping<string, TypeSourceModel> group in compilations
                .Select(c => c.Model)
                .GroupBy(m => m.Namespace, StringComparer.Ordinal))
            {
                var taken = new HashSet<string>(
                    group.Where(m => m.SymbolName == null).Select(m => m.ClassName),
                    StringComparer.Ordinal);
                foreach (TypeSourceModel nested in group
                    .Where(m => m.SymbolName != null)
                    .OrderBy(m => m.TypeReference, StringComparer.Ordinal))
                {
                    string name = nested.SymbolName;
                    for (int ii = 2; !taken.Add(name); ii++)
                    {
                        name = nested.SymbolName + "_" + ii.ToString(CultureInfo.InvariantCulture);
                    }
                    if (name != nested.SymbolName)
                    {
                        renamed[nested.TypeReference] = (nested.Namespace, name);
                    }
                }
            }
            return renamed;
        }

        /// <summary>
        /// Applies the renames of <see cref="GetUniqueSymbolNames"/> to a
        /// model and to the activator of its base type.
        /// </summary>
        private static TypeSourceModel WithUniqueSymbolNames(
            TypeSourceModel model,
            Dictionary<string, (string Namespace, string SymbolName)> renamed)
        {
            if (renamed.Count == 0)
            {
                return model;
            }
            if (model.TypeReference != null &&
                renamed.TryGetValue(model.TypeReference, out (string _, string SymbolName) self))
            {
                model = model with { SymbolName = self.SymbolName };
            }
            if (model.BaseDefinitionActivator != null &&
                model.BaseTypeReference != null &&
                renamed.TryGetValue(model.BaseTypeReference, out (string Namespace, string SymbolName) baseName))
            {
                model = model with
                {
                    BaseDefinitionActivator =
                        "global::" + baseName.Namespace + "." + baseName.SymbolName + "Activator"
                };
            }
            return model;
        }

        private static TypeSourceModel BuildClassModel(
            INamedTypeSymbol symbol,
            string dataTypeNamespace,
            string dataTypeId,
            string binaryEncodingId,
            string xmlEncodingId,
            CancellationToken ct)
        {
            string ns = symbol.GetFullNamespace();
            bool baseTypeIsEncodeable = symbol.BaseType != null &&
                symbol.BaseType.Name != "Object" &&
                (symbol.BaseType.ImplementsInterface("IEncodeable") ||
                    symbol.BaseType.HasAttribute("DataTypeAttribute"));
            return WithNesting(new TypeSourceModel
            {
                ClassName = symbol.Name,
                Namespace = ns,
                NamespaceUri = ResolveNamespaceUri(
                    symbol, dataTypeNamespace, ns),
                NamespaceSymbol = ns.Replace(".", string.Empty),
                DataTypeId = dataTypeId,
                BinaryEncodingId = binaryEncodingId,
                XmlEncodingId = xmlEncodingId,
                ContainingTypeDeclarations = EquatableArray<string>.Empty,
                IsEffectivelyPublic = IsEffectivelyPublic(symbol),
                EnumMembers = EquatableArray<TypeEnumMember>.Empty,
                IsRecord = symbol.IsRecord,
                IsEnum = false,
                IsSealed = symbol.IsSealed,
                IsDerived = baseTypeIsEncodeable,
                IsInternal =
                    symbol.DeclaredAccessibility is Accessibility.Internal or
                    Accessibility.NotApplicable,
                BaseTypeIsEncodeable = baseTypeIsEncodeable,
                HasManualClone = symbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Any(m => m.Name is "Clone" or "MemberwiseClone" &&
                        !m.IsImplicitlyDeclared),
                Fields = EquatableArray<TypeFieldModel>.From(CollectFields(symbol, ct)),
                BaseClassName = symbol.BaseType?.Name == "Object"
                    ? null : symbol.BaseType?.Name,
                BaseDefinitionActivator = baseTypeIsEncodeable
                    ? ResolveBaseDefinitionActivator(symbol.BaseType)
                    : null,
                BaseTypeReference = baseTypeIsEncodeable
                    ? symbol.BaseType.GetFullyQualifiedTypeName()
                    : null
            };
        }

        /// <summary>
        /// Resolves the activator of an encodeable base type that exposes the
        /// base type's data type definition: the <c>{Name}Activator</c>
        /// emitted next to every [DataType] type and every model generated
        /// structure. A [DataType] base in the same compilation is not
        /// visible yet (its activator is generated in this run) and is
        /// resolved by convention.
        /// </summary>
        private static string ResolveBaseDefinitionActivator(INamedTypeSymbol baseType)
        {
            if (baseType == null || baseType.IsGenericType)
            {
                return null;
            }
            string ns = baseType.GetFullNamespace();
            string activatorName = baseType.Name + "Activator";
            string activator = string.IsNullOrEmpty(ns)
                ? "global::" + activatorName
                : "global::" + ns + "." + activatorName;
            if (baseType.HasAttribute("DataTypeAttribute"))
            {
                return activator;
            }
            INamedTypeSymbol existing = baseType.ContainingNamespace?
                .GetTypeMembers(activatorName)
                .FirstOrDefault();
            if (existing != null &&
                existing.ImplementsInterface("IDataTypeDefinitionSource") &&
                existing.GetMembers("Instance").Any(m => m.IsStatic))
            {
                return activator;
            }
            return null;
        }

        private static TypeSourceModel BuildEnumModel(
            INamedTypeSymbol symbol,
            string dataTypeNamespace,
            string dataTypeId,
            string binaryEncodingId,
            string xmlEncodingId)
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
                        // Invariant: the generator parses the value with the
                        // invariant culture, and a culture with U+2212 as
                        // negative sign would otherwise make it unparsable.
                        Value = field.ConstantValue is null
                            ? "0"
                            : Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture)
                    });
                }
            }

            return WithNesting(new TypeSourceModel
            {
                ClassName = symbol.Name,
                Namespace = ns,
                NamespaceUri = ResolveNamespaceUri(
                    symbol, dataTypeNamespace, ns),
                NamespaceSymbol = ns.Replace(".", string.Empty),
                DataTypeId = dataTypeId,
                BinaryEncodingId = binaryEncodingId,
                XmlEncodingId = xmlEncodingId,
                ContainingTypeDeclarations = EquatableArray<string>.Empty,
                IsEffectivelyPublic = IsEffectivelyPublic(symbol),
                Fields = EquatableArray<TypeFieldModel>.Empty,
                IsEnum = true,
                IsFlags = symbol.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "FlagsAttribute"),
                EnumMembers = EquatableArray<TypeEnumMember>.From(members)
            }, symbol);
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
            // null if the constructor can assign any member.
            HashSet<string> ctorAssigned = GetConstructorAssignedMembers(symbol, ct);
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
                    fields.Add(CreateField(prop, prop.Name, orderIndex++, false, ctorAssigned));
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
                    fields.Add(CreateField(prop, fieldName, orderIndex, true, ctorAssigned, dtfAttr));
                }
            }
            fields.Sort((a, b) => a.Order.CompareTo(b.Order));
            return fields;
        }

        private static TypeFieldModel CreateField(
            IPropertySymbol prop, string fieldName,
            int order, bool hasDataTypeFieldAttr,
            HashSet<string> ctorAssigned,
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

            string dataTypeNodeId = ResolveFieldDataTypeNodeId(
                isArray || isMatrix ? elementShortTypeName : shortName,
                isEncodeable,
                isEnum);

            // A field whose value equals the type default (OPC 10000-6 Table 1)
            // may be omitted on encode (DefaultValueHandling.Exclude), and a
            // missing field keeps the value the constructor assigned on decode
            // (a deliberate leniency, so configuration files may leave fields
            // out). Both only agree with a conformant peer, which decodes a
            // missing field as the type default (5.4.1, 5.3.5), when the
            // declared default is the type default: no initializer (or a
            // default literal) that no constructor overrides. Any other
            // declared default makes the field always encoded.
            string defaultValueLiteral = null;
            bool hasNonConstantInitializer = false;
            ExpressionSyntax initializer = GetPropertyInitializerSyntax(prop);
            bool defaultKnown = ctorAssigned != null && !ctorAssigned.Contains(prop.Name);
            if (defaultKnown && !IsAutoProperty(prop))
            {
                // A property with accessor bodies is initialized through its
                // backing field: the field's initializer is the default.
                IFieldSymbol backingField = GetBackingField(prop);
                defaultKnown = backingField != null &&
                    !ctorAssigned.Contains(backingField.Name);
                initializer = defaultKnown ? GetFieldInitializerSyntax(backingField) : null;
            }
            if (!defaultKnown)
            {
                hasNonConstantInitializer = true;
            }
            else if (initializer != null && !IsDefaultLiteral(initializer))
            {
                if (!isArray && !isMatrix && !isEnum && !isEncodeable &&
                    s_literalComparableTypes.Contains(shortName) &&
                    IsSimpleLiteral(initializer))
                {
                    defaultValueLiteral = initializer.ToString();
                }
                else if (shortName == "String" && IsStringEmpty(initializer))
                {
                    defaultValueLiteral = "\"\"";
                }
                else
                {
                    hasNonConstantInitializer = true;
                }
            }

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
                DataTypeNodeId = dataTypeNodeId,
                StructureHandling = structureHandling,
                DefaultValueHandling = defaultValueHandling,
                DefaultValueLiteral = defaultValueLiteral,
                HasNonConstantInitializer = hasNonConstantInitializer,
                FieldTypeIsSealed = fieldTypeIsSealed,
                FieldTypeHasEncodeableBase = fieldTypeHasEncodeableBase,
                IsInitOnly = HasInitOnlySetter(prop),
                BackingFieldName = HasInitOnlySetter(prop)
                    ? $"__{prop.Name}"
                    : null,
                DefaultInitializer = HasInitOnlySetter(prop)
                    ? GetPropertyInitializer(prop)
                    : null
            };
        }

        /// <summary>
        /// Resolves the OPC UA DataType NodeId expression for a field so a
        /// non-null StructureField.DataType can be emitted. OPC UA built-in
        /// types resolve to the exact fixed numeric DataType identifier
        /// (mirrors the canonical map in <c>TypeInfo.GetDataTypeId</c>).
        /// Complex (IEncodeable) and enum field types cannot be resolved to a
        /// concrete DataType NodeId from the attribute model alone (no
        /// companion DataType NodeId is carried on the field type), so they
        /// fall back to the Structure (i=22) / Enumeration (i=29) DataType
        /// respectively. This is a best-effort fallback; the definition is
        /// always non-null which is the hard contract.
        /// The expression uses an inline <c>NodeId</c> literal (rather than a
        /// <c>DataTypeIds</c> constant) so the generated code only depends on
        /// Opc.Ua.Types and not on the Opc.Ua.Core.Types assembly, matching
        /// the attribute generator's existing inline-literal convention.
        /// </summary>
        private static string ResolveFieldDataTypeNodeId(
            string shortTypeName, bool isEncodeable, bool isEnum)
        {
            if (shortTypeName != null &&
                s_builtInDataTypeIdMap.TryGetValue(shortTypeName, out uint id))
            {
                return FormatNodeIdLiteral(id);
            }
            if (isEnum)
            {
                // Enumeration (i=29)
                return FormatNodeIdLiteral(29);
            }
            if (isEncodeable)
            {
                // Structure (i=22)
                return FormatNodeIdLiteral(22);
            }
            // Last-resort fallback for an unexpected/unsupported scalar type;
            // BaseDataType (i=24) keeps the emitted definition non-null.
            return FormatNodeIdLiteral(24);
        }

        /// <summary>
        /// Formats a namespace-zero numeric DataType identifier as an inline
        /// <c>NodeId</c> literal expression.
        /// </summary>
        internal static string FormatNodeIdLiteral(uint identifier)
        {
            return $"new global::Opc.Ua.NodeId({identifier}u)";
        }

        /// <summary>
        /// Maps OPC UA built-in C# short type names to the namespace-zero
        /// numeric DataType identifier. Mirrors the canonical BuiltInType to
        /// DataTypeId mapping in <c>TypeInfo.GetDataTypeId</c>.
        /// </summary>
        private static readonly Dictionary<string, uint> s_builtInDataTypeIdMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Boolean"] = 1,
                ["bool"] = 1,
                ["SByte"] = 2,
                ["Byte"] = 3,
                ["Int16"] = 4,
                ["short"] = 4,
                ["UInt16"] = 5,
                ["ushort"] = 5,
                ["Int32"] = 6,
                ["int"] = 6,
                ["UInt32"] = 7,
                ["uint"] = 7,
                ["Int64"] = 8,
                ["long"] = 8,
                ["UInt64"] = 9,
                ["ulong"] = 9,
                ["Single"] = 10,
                ["float"] = 10,
                ["Double"] = 11,
                ["String"] = 12,
                ["DateTime"] = 13,
                ["DateTimeUtc"] = 13,
                ["Guid"] = 14,
                ["Uuid"] = 14,
                ["ByteString"] = 15,
                ["XmlElement"] = 16,
                ["NodeId"] = 17,
                ["ExpandedNodeId"] = 18,
                ["StatusCode"] = 19,
                ["QualifiedName"] = 20,
                ["LocalizedText"] = 21,
                ["ExtensionObject"] = 22,
                ["DataValue"] = 23,
                ["Variant"] = 24,
                ["DiagnosticInfo"] = 25
            };

        /// <summary>
        /// Extracts the default value initializer expression from a
        /// partial property definition (e.g. the "= true" part of
        /// "public partial bool Foo { get; init; } = true;").
        /// Returns null if no initializer is present.
        /// </summary>
        private static string GetPropertyInitializer(IPropertySymbol prop)
        {
            return GetPropertyInitializerSyntax(prop)?.ToString();
        }

        private static ExpressionSyntax GetPropertyInitializerSyntax(IPropertySymbol prop)
        {
            foreach (SyntaxReference syntaxRef in prop.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax propSyntax &&
                    propSyntax.Initializer != null)
                {
                    return propSyntax.Initializer.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// The names of the members the constructor the activator runs (the
        /// parameterless one) assigns, or <c>null</c> if it can assign any
        /// member: it chains to another constructor or calls a method of the
        /// instance. The analysis is syntactic and over-approximates, which
        /// only keeps more fields on the wire.
        /// </summary>
        private static HashSet<string> GetConstructorAssignedMembers(
            INamedTypeSymbol symbol,
            CancellationToken ct)
        {
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            IMethodSymbol ctor = symbol.InstanceConstructors
                .FirstOrDefault(c => c.Parameters.Length == 0 && !c.IsImplicitlyDeclared);
            if (ctor == null)
            {
                return assigned;
            }
            foreach (SyntaxReference reference in ctor.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(ct) is not ConstructorDeclarationSyntax declaration)
                {
                    continue;
                }
                if (declaration.Initializer != null &&
                    declaration.Initializer.IsKind(SyntaxKind.ThisConstructorInitializer))
                {
                    return null;
                }
                SyntaxNode body = (SyntaxNode)declaration.Body ?? declaration.ExpressionBody;
                if (body == null)
                {
                    continue;
                }
                foreach (SyntaxNode node in body.DescendantNodesAndSelf())
                {
                    ExpressionSyntax target = node switch
                    {
                        AssignmentExpressionSyntax assignment => assignment.Left,
                        PrefixUnaryExpressionSyntax prefix when
                            prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                            prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                        PostfixUnaryExpressionSyntax postfix when
                            postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                            postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                        ArgumentSyntax argument when
                            !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
                        _ => null
                    };
                    if (target != null)
                    {
                        foreach (IdentifierNameSyntax name in target
                            .DescendantNodesAndSelf()
                            .OfType<IdentifierNameSyntax>())
                        {
                            assigned.Add(name.Identifier.ValueText);
                        }
                    }
                    else if (node is InvocationExpressionSyntax invocation &&
                        IsInstanceCall(invocation.Expression))
                    {
                        return null;
                    }
                }
            }
            return assigned;

            // A call of an unqualified or this-qualified method (other than
            // nameof) can assign any member, e.g. an Initialize() helper.
            static bool IsInstanceCall(ExpressionSyntax expression)
            {
                return expression switch
                {
                    IdentifierNameSyntax name => name.Identifier.ValueText != "nameof",
                    GenericNameSyntax => true,
                    MemberAccessExpressionSyntax memberAccess =>
                        memberAccess.Expression is ThisExpressionSyntax,
                    _ => false
                };
            }
        }

        /// <summary>
        /// True if no declaration of the property has accessor bodies, so
        /// the property initializer is its default value.
        /// </summary>
        private static bool IsAutoProperty(IPropertySymbol prop)
        {
            foreach (SyntaxReference syntaxRef in prop.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax propSyntax ||
                    propSyntax.ExpressionBody != null ||
                    propSyntax.AccessorList == null ||
                    propSyntax.AccessorList.Accessors.Any(a =>
                        a.Body != null || a.ExpressionBody != null))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The instance field a property getter returns unchanged
        /// (<c>get => m_x;</c>, <c>get { return m_x; }</c> or
        /// <c>=> this.m_x</c>), or <c>null</c>.
        /// </summary>
        private static IFieldSymbol GetBackingField(IPropertySymbol prop)
        {
            foreach (SyntaxReference syntaxRef in prop.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax propSyntax)
                {
                    continue;
                }
                ExpressionSyntax returned = propSyntax.ExpressionBody?.Expression;
                AccessorDeclarationSyntax getter = propSyntax.AccessorList?.Accessors
                    .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                if (returned == null && getter != null)
                {
                    returned = getter.ExpressionBody?.Expression ??
                        (getter.Body?.Statements is { Count: 1 } statements &&
                            statements[0] is ReturnStatementSyntax ret
                                ? ret.Expression
                                : null);
                }
                string name = returned switch
                {
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    MemberAccessExpressionSyntax
                    {
                        Expression: ThisExpressionSyntax,
                        Name: IdentifierNameSyntax identifier
                    } => identifier.Identifier.ValueText,
                    _ => null
                };
                if (name != null)
                {
                    return prop.ContainingType.GetMembers(name)
                        .OfType<IFieldSymbol>()
                        .FirstOrDefault(f => !f.IsStatic && !f.IsConst);
                }
            }
            return null;
        }

        private static ExpressionSyntax GetFieldInitializerSyntax(IFieldSymbol field)
        {
            foreach (SyntaxReference syntaxRef in field.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is VariableDeclaratorSyntax declarator &&
                    declarator.Initializer != null)
                {
                    return declarator.Initializer.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// True for an initializer that assigns the CLR default anyway
        /// (<c>null</c>, <c>default</c>, <c>null!</c>).
        /// </summary>
        private static bool IsDefaultLiteral(ExpressionSyntax expression)
        {
            if (expression is PostfixUnaryExpressionSyntax postfix &&
                postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                expression = postfix.Operand;
            }
            return expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                expression.IsKind(SyntaxKind.DefaultLiteralExpression);
        }

        /// <summary>
        /// True for a self-contained literal (number, string, boolean,
        /// optionally negated) that can be compared against in generated
        /// code without depending on the user's usings.
        /// </summary>
        private static bool IsSimpleLiteral(ExpressionSyntax expression)
        {
            if (expression is PrefixUnaryExpressionSyntax prefix &&
                (prefix.IsKind(SyntaxKind.UnaryMinusExpression) ||
                    prefix.IsKind(SyntaxKind.UnaryPlusExpression)))
            {
                expression = prefix.Operand;
                return expression.IsKind(SyntaxKind.NumericLiteralExpression);
            }
            return expression.IsKind(SyntaxKind.NumericLiteralExpression) ||
                expression.IsKind(SyntaxKind.StringLiteralExpression) ||
                expression.IsKind(SyntaxKind.TrueLiteralExpression) ||
                expression.IsKind(SyntaxKind.FalseLiteralExpression);
        }

        /// <summary>
        /// True for <c>string.Empty</c> (in any of its spellings).
        /// </summary>
        private static bool IsStringEmpty(ExpressionSyntax expression)
        {
            return expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "Empty" &&
                memberAccess.Expression.ToString() is
                    "string" or "String" or "System.String" or "global::System.String";
        }

        /// <summary>
        /// Property types a literal initializer can be compared with.
        /// </summary>
        private static readonly HashSet<string> s_literalComparableTypes =
            new(StringComparer.Ordinal)
            {
                "Boolean",
                "SByte",
                "Byte",
                "Int16",
                "UInt16",
                "Int32",
                "UInt32",
                "Int64",
                "UInt64",
                "Single",
                "Double",
                "String"
            };

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

        private static AttributeArgumentSyntax GetUnresolvedNamespaceArgument(
            AttributeData attribute,
            CancellationToken cancellationToken)
        {
            if (attribute?.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not
                AttributeSyntax attributeSyntax)
            {
                return null;
            }

            AttributeArgumentSyntax namespaceArgument = attributeSyntax.ArgumentList?.Arguments
                .FirstOrDefault(argument =>
                    argument.NameEquals?.Name.Identifier.ValueText ==
                    nameof(DataTypeAttribute.Namespace));
            if (namespaceArgument == null)
            {
                return null;
            }

            foreach (KeyValuePair<string, TypedConstant> namedArgument in
                attribute.NamedArguments)
            {
                if (namedArgument.Key == nameof(DataTypeAttribute.Namespace))
                {
                    return namedArgument.Value.Kind == TypedConstantKind.Error
                        ? namespaceArgument
                        : null;
                }
            }

            return namespaceArgument;
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
