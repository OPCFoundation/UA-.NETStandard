/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates the closed ECM primitive-field publication copies, not an application copier registry.
    /// </summary>
    internal static class EcmFieldCopyGeneration
    {
        /// <summary>
        /// Adds ECM-only copy generation when the model assembly explicitly enables it.
        /// </summary>
        internal static void Register(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.AdditionalTextsProvider.Collect().Combine(context.AnalyzerConfigOptionsProvider),
                static (production, input) =>
                {
                    if (IsEnabled(input.Right.GlobalOptions))
                    {
                        SourceGenerator.Guard(production, () => Emit(production, input.Left));
                    }
                });
        }

        /// <summary>
        /// Reads the explicit first-party model opt-in.
        /// </summary>
        internal static bool IsEnabled(AnalyzerConfigOptions options)
        {
            return options.TryGetValue("build_property.ModelSourceGeneratorEcmFieldCopies", out string? value) &&
                bool.TryParse(value, out bool enabled) && enabled;
        }

        /// <summary>
        /// Verifies the complete compiled type, including code emitted by other generators.
        /// </summary>
        internal static bool IsEligible(INamedTypeSymbol type, ImmutableArray<string> fields)
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract ||
                type.BaseType?.SpecialType != SpecialType.System_Object ||
                type.DeclaringSyntaxReferences.Length != 1 ||
                !type.DeclaringSyntaxReferences[0].SyntaxTree.FilePath.EndsWith(
                    "Opc.Ua.ECM.DataTypes.g.cs", StringComparison.Ordinal))
            {
                return false;
            }
            ImmutableArray<IFieldSymbol> storage = [.. type.GetMembers().OfType<IFieldSymbol>()];
            if (storage.Length != fields.Length ||
                storage.Any(field => field.IsStatic || field.Type.SpecialType != SpecialType.System_Single))
            {
                return false;
            }
            var copiedFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in fields)
            {
                ImmutableArray<ISymbol> members = type.GetMembers(name);
                if (members.Length != 1 || members[0] is not IPropertySymbol property ||
                    property.IsStatic || property.Type.SpecialType != SpecialType.System_Single ||
                    property.GetMethod is not { IsVirtual: false, DeclaredAccessibility: Accessibility.Public } ||
                    property.SetMethod is not { IsVirtual: false, DeclaredAccessibility: Accessibility.Public })
                {
                    return false;
                }
                if (property.DeclaringSyntaxReferences.Length != 1 ||
                    property.DeclaringSyntaxReferences[0].GetSyntax() is not PropertyDeclarationSyntax syntax ||
                    syntax.AccessorList is not { Accessors.Count: 2 } accessors)
                {
                    return false;
                }
                AccessorDeclarationSyntax? getter = accessors.Accessors.FirstOrDefault(
                    accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                AccessorDeclarationSyntax? setter = accessors.Accessors.FirstOrDefault(
                    accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration));
                if (getter?.ExpressionBody?.Expression is not IdentifierNameSyntax read ||
                    setter?.ExpressionBody?.Expression is not AssignmentExpressionSyntax write ||
                    !write.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    write.Left is not IdentifierNameSyntax left ||
                    write.Right is not IdentifierNameSyntax right ||
                    left.Identifier.ValueText != read.Identifier.ValueText || right.Identifier.ValueText != "value" ||
                    !storage.Any(field => field.Name == read.Identifier.ValueText) ||
                    !copiedFields.Add(read.Identifier.ValueText))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks the published model shapes before emitting primitive-only assignments.
        /// </summary>
        private static void Emit(SourceProductionContext context, ImmutableArray<AdditionalText> texts)
        {
            AdditionalText? input = texts.SingleOrDefault(text => text.Path.EndsWith(
                "Opc.Ua.ECM.NodeSet2.xml", StringComparison.OrdinalIgnoreCase));
            if (input == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidShape, Location.None, "The ECM NodeSet2 input is required."));
                return;
            }
            using var reader = XmlReader.Create(
                new StringReader(input.GetText(context.CancellationToken)?.ToString() ?? string.Empty),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            XElement? root = XDocument.Load(reader).Root;
            XNamespace ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
            if (root == null || root.Name != ns + "UANodeSet" ||
                root.Element(ns + "NamespaceUris")?.Elements(ns + "Uri").FirstOrDefault()?.Value !=
                    "http://opcfoundation.org/UA/ECM/")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidShape, Location.None, "The ECM model must occupy namespace index one."));
                return;
            }
            foreach ((string name, ImmutableArray<string> names) in Shapes)
            {
                XElement? node = root.Elements(ns + "UADataType").SingleOrDefault(
                    element => (string?)element.Attribute("BrowseName") == "1:" + name);
                XElement? definition = node?.Element(ns + "Definition");
                XElement[] fields = definition?.Elements(ns + "Field").ToArray() ?? [];
                XElement[] parents = node?.Element(ns + "References")?.Elements(ns + "Reference")
                    .Where(reference => (string?)reference.Attribute("ReferenceType") is "HasSubtype" or "i=45" &&
                        (string?)reference.Attribute("IsForward") == "false").ToArray() ?? [];
                if (node == null || definition == null ||
                    (string?)node.Attribute("IsAbstract") is "true" or "1" ||
                    (string?)definition.Attribute("IsUnion") is "true" or "1" ||
                    parents.Length != 1 || parents[0].Value != "i=22" ||
                    fields.Length != names.Length ||
                    fields.Where((field, index) =>
                        (string?)field.Attribute("Name") != names[index] ||
                        (string?)field.Attribute("DataType") is not ("Float" or "i=10") ||
                        (string?)field.Attribute("ValueRank") is not (null or "-1") ||
                        (string?)field.Attribute("IsOptional") is "true" or "1" ||
                        (string?)field.Attribute("AllowSubTypes") is "true" or "1").Any())
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidShape, Location.None,
                        name + " must contain only its three required scalar Float fields."));
                    return;
                }
            }
            var source = new StringBuilder(CodeTemplates.CodeHeader).Append(
                "\r\nnamespace Opc.Ua.ECM\r\n{\r\n" +
                "    /// <summary>\r\n    /// Copies the verified ECM primitive structures.\r\n    /// </summary>\r\n" +
                "    internal static class EcmPublicationCopies\r\n    {\r\n");
            foreach ((string name, ImmutableArray<string> fields) in Shapes)
            {
                source.Append(
                    "        /// <summary>\r\n        /// Copies an exact generated primitive structure.\r\n" +
                    "        /// </summary>\r\n        internal static ").Append(name)
                    .Append(" Copy(").Append(name).Append(" source)\r\n        {\r\n")
                    .Append("            if (source is null)\r\n            {\r\n")
                    .Append("                throw new global::System.ArgumentNullException(nameof(source));\r\n")
                    .Append("            }\r\n            if (source.GetType() != typeof(").Append(name)
                    .Append("))\r\n            {\r\n")
                    .Append("                throw new global::Opc.Ua.ServiceResultException(\r\n")
                    .Append("                    global::Opc.Ua.StatusCodes.BadNotSupported);\r\n")
                    .Append("            }\r\n            return new ").Append(name).Append("\r\n            {\r\n");
                foreach (string field in fields)
                {
                    source.Append("                ").Append(field).Append(" = source.").Append(field)
                        .Append(",\r\n");
                }
                source.Append("            };\r\n        }\r\n");
            }
            source.Append("    }\r\n}\r\n");
            context.AddSource("Opc.Ua.ECM.PublicationCopies.g.cs", source.ToString());
        }

        /// <summary>
        /// A model or final compiled type cannot support the closed primitive-field copy.
        /// </summary>
        internal static readonly DiagnosticDescriptor InvalidShape = new(
            "MODELGEN039", "ECM publication copy is not eligible", "{0}", SourceGenerator.Name,
            DiagnosticSeverity.Error, isEnabledByDefault: true,
            customTags: [WellKnownDiagnosticTags.CompilationEnd]);

        /// <summary>
        /// The complete set of structures eligible for the first-party ECM copy.
        /// </summary>
        internal static readonly ImmutableArray<(string Name, ImmutableArray<string> Fields)> Shapes =
        [
            ("AcPeDataType", ["L1", "L2", "L3"]),
            ("AcPpDataType", ["L1L2", "L2L3", "L3L1"])
        ];
    }
}
