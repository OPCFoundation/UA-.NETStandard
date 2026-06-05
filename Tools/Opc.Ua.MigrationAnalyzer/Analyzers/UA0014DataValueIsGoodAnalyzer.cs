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
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0014: Replace the obsolete static <c>DataValue.IsGood(dv)</c> /
    /// <c>IsBad</c> / <c>IsUncertain</c> (+ <c>IsNotXxx</c>) helpers with the
    /// matching instance property on <c>DataValue</c>.
    /// </summary>
    /// <remarks>
    /// Detection: any <see cref="IInvocationOperation"/> that targets a static
    /// method named one of the six helpers on either <c>Opc.Ua.DataValue</c> or
    /// <c>Opc.Ua.DataValueExtensions</c>, with a single <c>DataValue</c> argument.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0014DataValueIsGoodAnalyzer : DiagnosticAnalyzer
    {
        private static readonly HashSet<string> s_targetNames =
        [
            "IsGood",
            "IsBad",
            "IsUncertain",
            "IsNotGood",
            "IsNotBad",
            "IsNotUncertain",
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0014_DataValueIsGoodStaticToInstance);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            IInvocationOperation invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;

            if (!method.IsStatic ||
                method.Parameters.Length != 1 ||
                !s_targetNames.Contains(method.Name))
            {
                return;
            }

            INamedTypeSymbol containing = method.ContainingType;
            if (containing is null)
            {
                return;
            }
            string containingName = containing.ToDisplayString();
            if (containingName != "Opc.Ua.DataValue" &&
                containingName != "Opc.Ua.DataValueExtensions")
            {
                return;
            }

            ITypeSymbol argType = method.Parameters[0].Type;
            if (argType is null || argType.ToDisplayString() != "Opc.Ua.DataValue")
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0014_DataValueIsGoodStaticToInstance,
                invocation.Syntax.GetLocation(),
                method.Name));
        }
    }
}
