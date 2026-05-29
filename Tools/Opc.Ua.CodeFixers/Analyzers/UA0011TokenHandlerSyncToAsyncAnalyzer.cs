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
    /// UA0011: Detect calls to the obsolete synchronous
    /// <c>Encrypt</c>/<c>Decrypt</c>/<c>Sign</c>/<c>Verify</c> members on
    /// <c>Opc.Ua.IUserIdentityTokenHandler</c> (or any implementing type)
    /// and recommend their async counterparts.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0011TokenHandlerSyncToAsyncAnalyzer : DiagnosticAnalyzer
    {
        private const string TokenHandlerFullName = "Opc.Ua.IUserIdentityTokenHandler";

        private static readonly HashSet<string> s_targetNames =
        [
            "Encrypt",
            "Decrypt",
            "Sign",
            "Verify",
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0011_TokenHandlerSyncToAsync);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol tokenHandler = context.Compilation.GetTypeByMetadataName(TokenHandlerFullName);
            if (tokenHandler is null)
            {
                return;
            }

            context.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, tokenHandler),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol tokenHandler)
        {
            IInvocationOperation invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;

            if (method is null || !s_targetNames.Contains(method.Name))
            {
                return;
            }

            bool isShim = method.IsOpcUaShim("UA0011");
            if (!isShim && !method.IsObsolete())
            {
                return;
            }

            if (!isShim)
            {
                INamedTypeSymbol containing = method.ContainingType;
                if (containing is null)
                {
                    return;
                }

                bool declaredOnHandler = SymbolEqualityComparer.Default.Equals(containing, tokenHandler);
                if (!declaredOnHandler)
                {
                    bool implementsHandler = false;
                    foreach (INamedTypeSymbol iface in containing.AllInterfaces)
                    {
                        if (SymbolEqualityComparer.Default.Equals(iface, tokenHandler))
                        {
                            implementsHandler = true;
                            break;
                        }
                    }
                    if (!implementsHandler)
                    {
                        return;
                    }
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0011_TokenHandlerSyncToAsync,
                invocation.Syntax.GetLocation(),
                method.Name));
        }
    }
}
