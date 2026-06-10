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

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0019: Detect <c>new DataValue(StatusCode)</c> and
    /// <c>new DataValue(StatusCode, DateTimeUtc)</c> constructor calls and
    /// recommend the explicit <c>DataValue.FromStatusCode(...)</c> factory.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0019DataValueStatusCodeCtorAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0019_ObsoleteDataValueStatusCodeCtor];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        }

        private static void AnalyzeObjectCreation(OperationAnalysisContext context)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            IMethodSymbol ctor = creation.Constructor;
            if (ctor is null)
            {
                return;
            }

            INamedTypeSymbol containing = ctor.ContainingType;
            if (containing is null || containing.ToDisplayString() != "Opc.Ua.DataValue")
            {
                return;
            }

            if (ctor.Parameters.Length is < 1 or > 2)
            {
                return;
            }

            if (ctor.Parameters[0].Type?.ToDisplayString() != "Opc.Ua.StatusCode")
            {
                return;
            }

            string extra = string.Empty;
            if (ctor.Parameters.Length == 2)
            {
                if (ctor.Parameters[1].Type?.ToDisplayString() != "Opc.Ua.DateTimeUtc")
                {
                    return;
                }
                extra = ", DateTimeUtc";
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0019_ObsoleteDataValueStatusCodeCtor,
                creation.Syntax.GetLocation(),
                extra));
        }
    }
}
