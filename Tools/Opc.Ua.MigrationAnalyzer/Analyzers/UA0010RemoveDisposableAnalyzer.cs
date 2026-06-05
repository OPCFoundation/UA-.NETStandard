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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0010: Detect <c>using</c> declarations / statements whose variable
    /// type is one of the OPC UA identity types that are no longer
    /// <see cref="System.IDisposable"/> in 2.0
    /// (<c>CertificateIdentifier</c>, <c>UserIdentity</c>, or any
    /// implementation of <c>IUserIdentityTokenHandler</c>).
    /// </summary>
    /// <remarks>
    /// Detection is purely syntactic: the analyzer looks at
    /// <see cref="UsingStatementSyntax"/> and <see cref="LocalDeclarationStatementSyntax"/>
    /// nodes that carry the <c>using</c> keyword and resolves the declared
    /// variable's type through the <see cref="SemanticModel"/>.
    /// <para>
    /// Form B (direct <c>Dispose()</c> invocation) is intentionally not
    /// implemented here: the stub OPC UA types are not <c>IDisposable</c>,
    /// so any explicit <c>Dispose()</c> call would not compile and there is
    /// no operation to bind against in the analyzer test surface.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0010RemoveDisposableAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0010_RemoveDisposable);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            Dictionary<Compilation, UaSymbols> cache = [];
            UaSymbols symbols = UaSymbols.For(context.Compilation, cache);
            if (!symbols.ReferencesOpcUa)
            {
                return;
            }
            if (symbols.CertificateIdentifierType is null &&
                symbols.UserIdentityType is null &&
                symbols.UserIdentityTokenHandlerType is null)
            {
                return;
            }

            context.RegisterSyntaxNodeAction(
                c => AnalyzeUsingStatement(c, symbols),
                SyntaxKind.UsingStatement);
            context.RegisterSyntaxNodeAction(
                c => AnalyzeLocalDeclaration(c, symbols),
                SyntaxKind.LocalDeclarationStatement);
        }

        private static void AnalyzeUsingStatement(SyntaxNodeAnalysisContext context, UaSymbols symbols)
        {
            UsingStatementSyntax usingStmt = (UsingStatementSyntax)context.Node;
            if (usingStmt.Declaration is { } declaration)
            {
                ReportIfMatch(context, symbols, declaration, usingStmt.GetLocation());
            }
            else if (usingStmt.Expression is { } expression)
            {
                ITypeSymbol type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
                Report(context, symbols, type, usingStmt.GetLocation());
            }
        }

        private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context, UaSymbols symbols)
        {
            LocalDeclarationStatementSyntax local = (LocalDeclarationStatementSyntax)context.Node;
            if (local.UsingKeyword.IsKind(SyntaxKind.None))
            {
                return;
            }
            ReportIfMatch(context, symbols, local.Declaration, local.GetLocation());
        }

        private static void ReportIfMatch(
            SyntaxNodeAnalysisContext context,
            UaSymbols symbols,
            VariableDeclarationSyntax declaration,
            Location location)
        {
            ITypeSymbol declaredType = context.SemanticModel
                .GetTypeInfo(declaration.Type, context.CancellationToken).Type;

            // var: resolve from a variable initializer if available.
            if (declaredType is null || declaredType.TypeKind == TypeKind.Error)
            {
                foreach (VariableDeclaratorSyntax variable in declaration.Variables)
                {
                    if (variable.Initializer?.Value is { } init)
                    {
                        ITypeSymbol initType = context.SemanticModel
                            .GetTypeInfo(init, context.CancellationToken).Type;
                        if (initType != null)
                        {
                            declaredType = initType;
                            break;
                        }
                    }
                }
            }

            Report(context, symbols, declaredType, location);
        }

        private static void Report(
            SyntaxNodeAnalysisContext context,
            UaSymbols symbols,
            ITypeSymbol type,
            Location location)
        {
            if (type is null)
            {
                return;
            }
            if (IsTargetType(type, symbols, out string typeName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UA0010_RemoveDisposable,
                    location,
                    typeName));
            }
        }

        private static bool IsTargetType(ITypeSymbol type, UaSymbols symbols, out string name)
        {
            name = null;
            if (symbols.CertificateIdentifierType != null &&
                SymbolEqualityComparer.Default.Equals(type, symbols.CertificateIdentifierType))
            {
                name = symbols.CertificateIdentifierType.Name;
                return true;
            }
            if (symbols.UserIdentityType != null &&
                SymbolEqualityComparer.Default.Equals(type, symbols.UserIdentityType))
            {
                name = symbols.UserIdentityType.Name;
                return true;
            }
            INamedTypeSymbol tokenHandler = symbols.UserIdentityTokenHandlerType;
            if (tokenHandler != null)
            {
                if (SymbolEqualityComparer.Default.Equals(type, tokenHandler))
                {
                    name = tokenHandler.Name;
                    return true;
                }
                foreach (INamedTypeSymbol iface in type.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(iface, tokenHandler))
                    {
                        name = type.Name;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
