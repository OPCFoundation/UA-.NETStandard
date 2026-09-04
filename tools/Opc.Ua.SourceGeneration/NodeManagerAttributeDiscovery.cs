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

using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Discovered node-authoring binding plus the source locations used to
    /// infer its runtime kind and report diagnostics at the user's class.
    /// </summary>
    internal sealed record class NodeManagerAttributeDiscovery
    {
        /// <summary>
        /// The pure binding payload that gets forwarded into the
        /// Core <c>GenerateCode</c> pipeline.
        /// </summary>
        public NodeManagerAttributeBinding Binding { get; init; }

        /// <summary>
        /// Location of the attribute application, used for diagnostics.
        /// </summary>
        public Location Location { get; init; }

        /// <summary>
        /// <c>true</c> when the user-authored target class is declared
        /// <c>partial</c>.
        /// </summary>
        public bool IsPartial { get; init; }

        /// <summary>
        /// Location of the user-authored graph Configure implementation.
        /// </summary>
        public Location GraphConfigureLocation { get; init; }

        /// <summary>
        /// Location of the user-authored manager Configure implementation.
        /// </summary>
        public Location ManagerConfigureLocation { get; init; }

        /// <summary>
        /// Whether both canonical untyped Configure implementations exist.
        /// </summary>
        public bool HasConflictingConfigureMethods =>
            GraphConfigureLocation != null && ManagerConfigureLocation != null;

        /// <summary>
        /// Predicate used by <see cref="SyntaxProvider.ForAttributeWithMetadataName"/>.
        /// </summary>
        public static bool Handles(SyntaxNode node, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return node is ClassDeclarationSyntax c && c.AttributeLists.Count > 0;
        }

        /// <summary>
        /// Build a discovery record from the syntax-provider context.
        /// </summary>
        public static NodeManagerAttributeDiscovery Create(
            GeneratorAttributeSyntaxContext context,
            CancellationToken cancellationToken)
        {
            var symbol = (INamedTypeSymbol)context.TargetSymbol;
            AttributeData attr = context.Attributes.FirstOrDefault();
            Compilation compilation = context.SemanticModel.Compilation;

            string namespaceUri = attr.GetValue(nameof(NodeManagerAttributeBinding.NamespaceUri));
            string design = attr.GetValue(nameof(NodeManagerAttributeBinding.Design));
            string[] additionalNamespaceUris = attr.GetStringArray(
                nameof(NodeManagerAttributeBinding.AdditionalNamespaceUris));
            Location graphConfigureLocation = FindConfigureImplementation(
                symbol,
                compilation,
                kGraphBuilderMetadataName,
                cancellationToken);
            Location managerConfigureLocation = FindConfigureImplementation(
                symbol,
                compilation,
                kManagerBuilderMetadataName,
                cancellationToken);
            NodeAuthoringKind authoringKind =
                graphConfigureLocation != null && managerConfigureLocation != null
                    ? NodeAuthoringKind.None
                    : graphConfigureLocation != null
                        ? NodeAuthoringKind.NodeSource
                        : NodeAuthoringKind.NodeManager;
            bool generateFactory = authoringKind == NodeAuthoringKind.NodeManager &&
                (attr == null ||
                !attr.NamedArguments
                    .Any(p => p.Key == nameof(NodeManagerAttributeBinding.GenerateFactory) &&
                        p.Value.Value is bool b &&
                        !b));

            string targetNamespace = symbol.GetFullNamespace();
            string targetClassName = symbol.Name;

            bool isPartial = symbol.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax(cancellationToken)
                    is TypeDeclarationSyntax tds &&
                    tds.Modifiers.Any(SyntaxKind.PartialKeyword));

            Location location = symbol.Locations.FirstOrDefault() ?? Location.None;

            return new NodeManagerAttributeDiscovery
            {
                Binding = new NodeManagerAttributeBinding
                {
                    TargetNamespace = targetNamespace,
                    TargetClassName = targetClassName,
                    NamespaceUri = namespaceUri,
                    Design = design,
                    GenerateFactory = generateFactory,
                    AuthoringKind = authoringKind,
                    AdditionalNamespaceUris = additionalNamespaceUris
                },
                Location = location,
                IsPartial = isPartial,
                GraphConfigureLocation = graphConfigureLocation,
                ManagerConfigureLocation = managerConfigureLocation
            };
        }

        private static Location FindConfigureImplementation(
            INamedTypeSymbol type,
            Compilation compilation,
            string parameterTypeMetadataName,
            CancellationToken cancellationToken)
        {
            INamedTypeSymbol parameterType =
                compilation.GetTypeByMetadataName(parameterTypeMetadataName);
            if (parameterType == null)
            {
                return null;
            }

            foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration)
                {
                    continue;
                }

                SemanticModel semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
                foreach (MethodDeclarationSyntax method in declaration.Members
                    .OfType<MethodDeclarationSyntax>())
                {
                    if (method.Identifier.ValueText != "Configure" ||
                        !method.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                        method.Modifiers.Any(SyntaxKind.StaticKeyword) ||
                        method.TypeParameterList != null ||
                        method.ParameterList.Parameters.Count != 1 ||
                        method.ParameterList.Parameters[0].Modifiers.Count != 0 ||
                        (method.Body == null && method.ExpressionBody == null))
                    {
                        continue;
                    }

                    if (semanticModel.GetDeclaredSymbol(method, cancellationToken)
                            is IMethodSymbol methodSymbol &&
                        methodSymbol.ReturnsVoid &&
                        SymbolEqualityComparer.Default.Equals(
                            methodSymbol.Parameters[0].Type,
                            parameterType))
                    {
                        return method.Identifier.GetLocation();
                    }
                }
            }

            return null;
        }

        private const string kGraphBuilderMetadataName =
            "Opc.Ua.Server.Nodes.INodeGraphBuilder";
        private const string kManagerBuilderMetadataName =
            "Opc.Ua.Server.Fluent.INodeManagerBuilder";
    }
}
