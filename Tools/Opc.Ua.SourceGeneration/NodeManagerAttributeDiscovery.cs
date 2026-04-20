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
    /// Discovered <c>[Opc.Ua.Server.Fluent.NodeManager]</c> binding plus
    /// the source location of the attribute, used to report friendly
    /// diagnostics back at the user's class.
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
            INamedTypeSymbol symbol = (INamedTypeSymbol)context.TargetSymbol;
            AttributeData attr = context.Attributes.FirstOrDefault();

            string namespaceUri = attr.GetValue(nameof(NodeManagerAttributeBinding.NamespaceUri));
            string design = attr.GetValue(nameof(NodeManagerAttributeBinding.Design));
            bool generateFactory = attr == null ||
                !attr.NamedArguments
                    .Any(p => p.Key == nameof(NodeManagerAttributeBinding.GenerateFactory) &&
                        p.Value.Value is bool b &&
                        !b);

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
                    GenerateFactory = generateFactory
                },
                Location = location,
                IsPartial = isPartial
            };
        }
    }
}
