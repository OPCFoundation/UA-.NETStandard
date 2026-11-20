/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0003: Detect <c>x == null</c> / <c>x != null</c> comparisons on
    /// built-in OPC UA types that became readonly structs in 2.0.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0003NullCheckOnStructTypeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0003_NullCheckOnStructType];

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
            context.RegisterOperationAction(c => AnalyzeBinary(c, symbols), OperationKind.Binary);
        }

        private static void AnalyzeBinary(OperationAnalysisContext context, UaSymbols symbols)
        {
            var op = (IBinaryOperation)context.Operation;
            if (op.OperatorKind is not BinaryOperatorKind.Equals and
                not BinaryOperatorKind.NotEquals)
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
            if (valueType is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return;
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
