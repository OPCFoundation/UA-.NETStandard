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

// UA0001 is diagnostic-only. The replacement requires an ILogger instance
// obtained from ITelemetryContext.CreateLogger<T>() which the analyzer cannot
// synthesize automatically. A code fix would require the host type to expose
// an ILogger field or an ITelemetryContext from which to derive one.

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
    /// UA0001: Flag calls to the obsolete static <c>Opc.Ua.Utils.Trace</c> and
    /// <c>Opc.Ua.Utils.LogX</c> helpers. Replacement requires an
    /// <c>ILogger</c> from an <c>ITelemetryContext</c> which the analyzer
    /// cannot synthesize, so this rule ships diagnostic-only (no code fix).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0001UtilsTraceToILoggerAnalyzer : DiagnosticAnalyzer
    {
        private static readonly HashSet<string> s_targetNames =
        [
            "Trace",
            "LogError",
            "LogWarning",
            "LogInformation",
            "LogDebug",
            "LogTrace",
            "LogCritical",
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0001_UtilsTraceToILogger);

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

            if (!method.IsStatic || !s_targetNames.Contains(method.Name))
            {
                return;
            }

            INamedTypeSymbol containing = method.ContainingType;
            if (containing is null || containing.ToDisplayString() != "Opc.Ua.Utils")
            {
                return;
            }

            if (!method.IsObsolete())
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0001_UtilsTraceToILogger,
                invocation.Syntax.GetLocation(),
                "Utils." + method.Name));
        }
    }
}
