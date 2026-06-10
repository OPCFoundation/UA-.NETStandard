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
    /// UA0004: Detect null-conditional access (<c>?.</c>) whose receiver is
    /// one of the now-struct OPC UA built-in types. Since the receiver can
    /// never be <c>null</c>, the <c>?.</c> is misleading.
    /// </summary>
    /// <remarks>
    /// Implemented as a syntax-node action so the analyzer still fires on
    /// migration code that no longer compiles (e.g. <c>nodeId?.X</c> where
    /// <c>NodeId</c> is now a non-nullable struct).
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0004ConditionalAccessOnStructAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0004_ConditionalAccessOnStructType];

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
            if (!symbols.ReferencesOpcUa)
            {
                return;
            }
            context.RegisterSyntaxNodeAction(
                c => AnalyzeConditionalAccess(c, symbols),
                SyntaxKind.ConditionalAccessExpression);
        }

        private static void AnalyzeConditionalAccess(SyntaxNodeAnalysisContext context, UaSymbols symbols)
        {
            var node = (ConditionalAccessExpressionSyntax)context.Node;
            ITypeSymbol receiverType = context.SemanticModel
                .GetTypeInfo(node.Expression, context.CancellationToken).Type;
            if (receiverType is null)
            {
                return;
            }

            if (!symbols.IsBuiltInStructType(receiverType))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0004_ConditionalAccessOnStructType,
                node.GetLocation(),
                receiverType.Name));
        }
    }
}
