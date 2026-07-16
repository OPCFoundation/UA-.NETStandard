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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0005: Detect call sites passing a <c>byte[]</c> argument where the
    /// resolved parameter type is now <c>Opc.Ua.ByteString</c>.
    /// </summary>
    /// <remarks>
    /// Implemented as a syntax-node action so the analyzer fires on migration
    /// code that no longer compiles (the <c>byte[]</c>-to-<c>ByteString</c>
    /// conversion is gone).
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0005ByteArrayToByteStringAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0005_ByteArrayWhereByteStringExpected];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            Dictionary<Compilation, UaSymbols> cache = [];
            var symbols = UaSymbols.For(context.Compilation, cache);
            if (!symbols.ReferencesOpcUa || symbols.ByteStringType is null)
            {
                return;
            }
            context.RegisterSyntaxNodeAction(
                c => AnalyzeInvocation(c, symbols),
                SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, UaSymbols symbols)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            SymbolInfo info = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
            IMethodSymbol method = info.Symbol as IMethodSymbol
                ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (method is null)
            {
                return;
            }

            INamedTypeSymbol byteStringType = symbols.ByteStringType;
            SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
            for (int i = 0; i < arguments.Count; i++)
            {
                ArgumentSyntax arg = arguments[i];
                IParameterSymbol parameter = i < method.Parameters.Length ? method.Parameters[i] : null;
                if (parameter is null)
                {
                    continue;
                }
                if (!SymbolEqualityComparer.Default.Equals(parameter.Type, byteStringType))
                {
                    continue;
                }

                ITypeSymbol valueType = context.SemanticModel
                    .GetTypeInfo(arg.Expression, context.CancellationToken).Type;
                if (valueType is not IArrayTypeSymbol array ||
                    array.ElementType.SpecialType != SpecialType.System_Byte)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UA0005_ByteArrayWhereByteStringExpected,
                    arg.GetLocation(),
                    method.Name));
            }
        }
    }
}
