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
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0022: Detect access to the legacy
    /// <c>ApplicationConfiguration.CertificateValidator</c> /
    /// <c>ServerBase.CertificateValidator</c> properties (removed in 2.0) and
    /// recommend the new <c>CertificateManager</c> property (type
    /// <c>ICertificateManager</c>).
    /// </summary>
    /// <remarks>
    /// Dual-mode detection mirrors UA0021:
    /// <list type="bullet">
    /// <item>Semantic path (<see cref="OperationKind.PropertyReference"/>) when
    /// the legacy <c>CertificateValidator</c> property is still present and
    /// marked <c>[Obsolete]</c>.</item>
    /// <item>Syntactic fallback (<see cref="SyntaxKind.SimpleMemberAccessExpression"/>)
    /// when the legacy property has been removed entirely and the call site no
    /// longer compiles — gated by <c>using Opc.Ua;</c> and a receiver whose
    /// (possibly error) type name still ends with
    /// <c>ApplicationConfiguration</c> / <c>ServerBase</c>.</item>
    /// </list>
    /// The receiver short name is passed as the <c>{0}</c> message arg.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0022CertificateValidatorPropertyRenameAnalyzer : DiagnosticAnalyzer
    {
        private const string OpcUaNamespace = "Opc.Ua";
        private const string CertificateValidatorPropertyName = "CertificateValidator";
        private const string ApplicationConfigurationTypeName = "ApplicationConfiguration";
        private const string ServerBaseTypeName = "ServerBase";

        private const string ApplicationConfigurationFullName =
            OpcUaNamespace + "." + ApplicationConfigurationTypeName;

        private const string ServerBaseFullName = OpcUaNamespace + "." + ServerBaseTypeName;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0022_CertificateValidatorPropertyRename];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(static startContext =>
            {
                INamedTypeSymbol? appConfig =
                    startContext.Compilation.GetTypeByMetadataName(ApplicationConfigurationFullName);
                INamedTypeSymbol? serverBase =
                    startContext.Compilation.GetTypeByMetadataName(ServerBaseFullName);

                startContext.RegisterOperationAction(
                    ctx => AnalyzePropertyReference(ctx, appConfig, serverBase),
                    OperationKind.PropertyReference);

                startContext.RegisterSyntaxNodeAction(
                    ctx => AnalyzeMemberAccessSyntactic(ctx, appConfig, serverBase),
                    SyntaxKind.SimpleMemberAccessExpression);
            });
        }

        private static void AnalyzePropertyReference(
            OperationAnalysisContext context,
            INamedTypeSymbol? appConfig,
            INamedTypeSymbol? serverBase)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;
            IPropertySymbol property = reference.Property;
            if (property is null || property.Name != CertificateValidatorPropertyName)
            {
                return;
            }

            INamedTypeSymbol containing = property.ContainingType;
            if (containing is null)
            {
                return;
            }

            string? receiverName = null;
            if (appConfig is not null && IsOrInheritsFrom(containing, appConfig))
            {
                receiverName = ApplicationConfigurationTypeName;
            }
            else if (serverBase is not null && IsOrInheritsFrom(containing, serverBase))
            {
                receiverName = ServerBaseTypeName;
            }

            if (receiverName is null)
            {
                return;
            }

            if (!property.IsObsolete())
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0022_CertificateValidatorPropertyRename,
                reference.Syntax.GetLocation(),
                receiverName));
        }

        private static void AnalyzeMemberAccessSyntactic(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol? appConfig,
            INamedTypeSymbol? serverBase)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;
            if (memberAccess.Name.Identifier.ValueText != CertificateValidatorPropertyName)
            {
                return;
            }

            // Skip if semantic path already matched (avoid double-fire).
            SymbolInfo symInfo = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
            if (symInfo.Symbol is IPropertySymbol resolved &&
                resolved.ContainingType is INamedTypeSymbol containingResolved)
            {
                string fullName = containingResolved.ToDisplayString();
                if (fullName is ApplicationConfigurationFullName or
                    ServerBaseFullName)
                {
                    // Semantic action handles it (and gates on [Obsolete]).
                    return;
                }
                if ((appConfig is not null && IsOrInheritsFrom(containingResolved, appConfig)) ||
                    (serverBase is not null && IsOrInheritsFrom(containingResolved, serverBase)))
                {
                    return;
                }
            }

            if (!HasOpcUaUsing(memberAccess))
            {
                return;
            }

            TypeInfo receiverType = context.SemanticModel.GetTypeInfo(
                memberAccess.Expression,
                context.CancellationToken);
            ITypeSymbol? type = receiverType.Type ?? receiverType.ConvertedType;
            string? receiverName = ClassifyReceiverName(type);
            if (receiverName is null)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0022_CertificateValidatorPropertyRename,
                memberAccess.GetLocation(),
                receiverName));
        }

        private static string? ClassifyReceiverName(ITypeSymbol? type)
        {
            if (type is null)
            {
                return null;
            }
            string name = type.Name;
            if (string.IsNullOrEmpty(name))
            {
                // Error symbol with no name — be lenient: examine the display string.
                string display = type.ToDisplayString();
                if (display.Contains(ApplicationConfigurationTypeName, StringComparison.Ordinal))
                {
                    return ApplicationConfigurationTypeName;
                }
                if (display.Contains(ServerBaseTypeName, StringComparison.Ordinal))
                {
                    return ServerBaseTypeName;
                }
                return null;
            }

            if (name.Contains(ApplicationConfigurationTypeName, StringComparison.Ordinal))
            {
                return ApplicationConfigurationTypeName;
            }
            if (name.Contains(ServerBaseTypeName, StringComparison.Ordinal))
            {
                return ServerBaseTypeName;
            }
            return null;
        }

        private static bool IsOrInheritsFrom(INamedTypeSymbol candidate, INamedTypeSymbol target)
        {
            SymbolEqualityComparer eq = SymbolEqualityComparer.Default;
            INamedTypeSymbol? current = candidate;
            while (current is not null)
            {
                if (eq.Equals(current, target))
                {
                    return true;
                }
                current = current.BaseType;
            }
            return false;
        }

        private static bool HasOpcUaUsing(SyntaxNode node)
        {
            for (SyntaxNode? current = node; current is not null; current = current.Parent)
            {
                if (current is BaseNamespaceDeclarationSyntax ns &&
                    ContainsOpcUaUsing(ns.Usings))
                {
                    return true;
                }

                if (current is CompilationUnitSyntax compilationUnit)
                {
                    return ContainsOpcUaUsing(compilationUnit.Usings);
                }
            }

            return false;
        }

        private static bool ContainsOpcUaUsing(SyntaxList<UsingDirectiveSyntax> usings)
        {
            foreach (UsingDirectiveSyntax @using in usings)
            {
                if (@using.Alias is not null || @using.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                {
                    continue;
                }
                if (@using.Name is null)
                {
                    continue;
                }
                string text = @using.Name.ToString();
                if (string.Equals(text, OpcUaNamespace, StringComparison.Ordinal) ||
                    text.StartsWith(OpcUaNamespace + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
