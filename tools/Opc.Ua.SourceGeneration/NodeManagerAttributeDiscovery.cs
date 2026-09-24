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
    /// <remarks>
    /// This is the output of a <c>ForAttributeWithMetadataName</c> transform,
    /// which runs again on every compilation change. Every member therefore
    /// compares by value (no arrays, no <see cref="Microsoft.CodeAnalysis.Location"/>)
    /// so an unchanged attribute leaves the model generation cached.
    /// </remarks>
    internal sealed record class NodeManagerAttributeDiscovery
    {
        /// <summary>
        /// The pure binding payload that gets forwarded into the
        /// Core <c>GenerateCode</c> pipeline.
        /// </summary>
        public NodeManagerAttributeBinding Binding { get; init; }

        /// <summary>
        /// Location of the annotated class, used for diagnostics.
        /// </summary>
        public LocationInfo Location { get; init; }

        /// <summary>
        /// <c>true</c> when the user-authored target class is declared
        /// <c>partial</c>.
        /// </summary>
        public bool IsPartial { get; init; }

        /// <summary>
        /// Why the annotated class cannot host a generated node manager
        /// (nested or generic), or <c>null</c> when it can.
        /// </summary>
        public string UnsupportedReason { get; init; }

        /// <summary>
        /// Attribute expressions that Roslyn could not bind to constants.
        /// </summary>
        public EquatableArray<NodeManagerAttributeExpressionError> InvalidExpressions { get; init; }
            = EquatableArray<NodeManagerAttributeExpressionError>.Empty;

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

            string namespaceUri = attr.GetValue(nameof(NodeManagerAttributeBinding.NamespaceUri));
            string design = attr.GetValue(nameof(NodeManagerAttributeBinding.Design));
            string[] additionalNamespaceUris = attr.GetStringArray(
                nameof(NodeManagerAttributeBinding.AdditionalNamespaceUris));
            EquatableArray<NodeManagerAttributeExpressionError> invalidExpressions =
                GetInvalidExpressions(attr, context.SemanticModel, cancellationToken);
            bool generateFactory = IsNotDisabled(
                attr,
                nameof(NodeManagerAttributeBinding.GenerateFactory));
            bool generateDefaultConstructor = IsNotDisabled(
                attr,
                nameof(NodeManagerAttributeBinding.GenerateDefaultConstructor));

            string targetNamespace = symbol.GetFullNamespace();
            string targetClassName = symbol.Name;

            bool isPartial = symbol.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax(cancellationToken)
                    is TypeDeclarationSyntax tds &&
                    tds.Modifiers.Any(SyntaxKind.PartialKeyword));

            LocationInfo location = LocationInfo.From(symbol.Locations.FirstOrDefault());

            // The generated manager and factory are emitted as top-level
            // types of the class's namespace, so a nested or generic class
            // would get an unrelated companion type instead of its members.
            string unsupportedReason = symbol.ContainingType != null
                ? "it is nested in '" + symbol.ContainingType.ToDisplayString() + "'"
                : symbol.IsGenericType
                    ? "it is generic"
                    : null;

            return new NodeManagerAttributeDiscovery
            {
                Binding = new NodeManagerAttributeBinding
                {
                    TargetNamespace = targetNamespace,
                    TargetClassName = targetClassName,
                    NamespaceUri = namespaceUri,
                    Design = design,
                    GenerateFactory = generateFactory,
                    GenerateDefaultConstructor = generateDefaultConstructor,
                    // Equatable by content: a fresh array would make every
                    // run's binding unequal to the previous one.
                    AdditionalNamespaceUris = additionalNamespaceUris == null
                        ? null
                        : new EquatableArray<string>(additionalNamespaceUris)
                },
                Location = location,
                IsPartial = isPartial,
                UnsupportedReason = unsupportedReason,
                InvalidExpressions = invalidExpressions
            };
        }

        /// <summary>
        /// Reads a boolean named argument that defaults to <c>true</c>:
        /// the flag is off only when it is present and explicitly
        /// <c>false</c>.
        /// </summary>
        private static bool IsNotDisabled(AttributeData attribute, string name)
        {
            return attribute == null ||
                !attribute.NamedArguments
                    .Any(p => p.Key == name && p.Value.Value is bool b && !b);
        }

        private static EquatableArray<NodeManagerAttributeExpressionError> GetInvalidExpressions(
            AttributeData attribute,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (attribute?.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not
                AttributeSyntax attributeSyntax)
            {
                return EquatableArray<NodeManagerAttributeExpressionError>.Empty;
            }

            var errors = new List<NodeManagerAttributeExpressionError>();
            AddInvalidExpression(
                errors,
                attribute,
                attributeSyntax,
                semanticModel,
                nameof(NodeManagerAttributeBinding.NamespaceUri),
                isArray: false,
                cancellationToken);
            AddInvalidExpression(
                errors,
                attribute,
                attributeSyntax,
                semanticModel,
                nameof(NodeManagerAttributeBinding.AdditionalNamespaceUris),
                isArray: true,
                cancellationToken);
            return EquatableArray<NodeManagerAttributeExpressionError>.From(errors);
        }

        private static void AddInvalidExpression(
            List<NodeManagerAttributeExpressionError> errors,
            AttributeData attribute,
            AttributeSyntax attributeSyntax,
            SemanticModel semanticModel,
            string argumentName,
            bool isArray,
            CancellationToken cancellationToken)
        {
            KeyValuePair<string, TypedConstant> namedArgument = attribute.NamedArguments
                .FirstOrDefault(argument => argument.Key == argumentName);
            if (namedArgument.Key == null)
            {
                return;
            }
            TypedConstant value = namedArgument.Value;
            bool hasError = value.Kind == TypedConstantKind.Error ||
                (isArray &&
                    value.Kind == TypedConstantKind.Array &&
                    !value.IsNull &&
                    value.Values.Any(element => element.Kind == TypedConstantKind.Error));
            if (!hasError)
            {
                return;
            }

            AttributeArgumentSyntax argumentSyntax = attributeSyntax.ArgumentList?.Arguments
                .FirstOrDefault(argument =>
                    argument.NameEquals?.Name.Identifier.ValueText == argumentName);
            if (argumentSyntax == null)
            {
                return;
            }

            if (isArray)
            {
                ExpressionSyntax[] elementExpressions =
                    GetArrayElementExpressions(argumentSyntax.Expression);
                if (elementExpressions.Length > 0)
                {
                    int initialCount = errors.Count;
                    for (int ii = 0; ii < elementExpressions.Length; ii++)
                    {
                        ExpressionSyntax elementExpression = elementExpressions[ii];
                        bool isInvalid = namedArgument.Value.Kind == TypedConstantKind.Array &&
                            ii < namedArgument.Value.Values.Length
                                ? namedArgument.Value.Values[ii].Kind == TypedConstantKind.Error
                                : !semanticModel.GetConstantValue(
                                    elementExpression,
                                    cancellationToken).HasValue;
                        if (isInvalid)
                        {
                            errors.Add(new NodeManagerAttributeExpressionError(
                                argumentName,
                                elementExpression.ToString(),
                                LocationInfo.From(elementExpression.GetLocation())));
                        }
                    }
                    if (errors.Count > initialCount)
                    {
                        return;
                    }
                }
            }

            errors.Add(new NodeManagerAttributeExpressionError(
                argumentName,
                argumentSyntax.Expression.ToString(),
                LocationInfo.From(argumentSyntax.Expression.GetLocation())));
        }

        private static ExpressionSyntax[] GetArrayElementExpressions(ExpressionSyntax expression)
        {
            SeparatedSyntaxList<ExpressionSyntax>? expressions = expression switch
            {
                ArrayCreationExpressionSyntax array => array.Initializer?.Expressions,
                ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions,
                _ => null
            };
            return expressions.HasValue ? [.. expressions.Value] : [];
        }
    }

    /// <summary>
    /// An unresolved constant expression in a <c>[NodeManager]</c> attribute.
    /// </summary>
    internal sealed record class NodeManagerAttributeExpressionError(
        string ArgumentName,
        string Expression,
        LocationInfo Location);
}
