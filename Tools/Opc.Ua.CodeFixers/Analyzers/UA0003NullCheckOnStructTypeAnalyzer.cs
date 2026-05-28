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
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Opc.Ua.CodeFixers.Diagnostics;
using Opc.Ua.CodeFixers.Helpers;

namespace Opc.Ua.CodeFixers.Analyzers
{
    /// <summary>
    /// UA0003: Detect <c>x == null</c> / <c>x != null</c> comparisons on
    /// built-in OPC UA types that became readonly structs in 2.0.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0003NullCheckOnStructTypeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0003_NullCheckOnStructType);

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
            context.RegisterOperationAction(c => AnalyzeBinary(c, symbols), OperationKind.Binary);
        }

        private static void AnalyzeBinary(OperationAnalysisContext context, UaSymbols symbols)
        {
            IBinaryOperation op = (IBinaryOperation)context.Operation;
            if (op.OperatorKind != BinaryOperatorKind.Equals &&
                op.OperatorKind != BinaryOperatorKind.NotEquals)
            {
                return;
            }

            IOperation valueOperand = GetValueOperandOrNull(op.LeftOperand, op.RightOperand);
            if (valueOperand is null)
            {
                return;
            }

            ITypeSymbol valueType = valueOperand.Type;
            if (valueType is null)
            {
                return;
            }
            if (valueType.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T &&
                valueType is INamedTypeSymbol named && named.TypeArguments.Length == 1)
            {
                valueType = named.TypeArguments[0];
            }

            if (!symbols.IsBuiltInStructType(valueType))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0003_NullCheckOnStructType,
                op.Syntax.GetLocation(),
                valueType.Name));
        }

        private static IOperation GetValueOperandOrNull(IOperation left, IOperation right)
        {
            if (IsNullLiteral(left))
            {
                return right;
            }
            if (IsNullLiteral(right))
            {
                return left;
            }
            return null;
        }

        private static bool IsNullLiteral(IOperation op)
        {
            while (op is IConversionOperation conv)
            {
                op = conv.Operand;
            }
            return op is ILiteralOperation lit &&
                lit.ConstantValue.HasValue &&
                lit.ConstantValue.Value is null;
        }
    }
}
