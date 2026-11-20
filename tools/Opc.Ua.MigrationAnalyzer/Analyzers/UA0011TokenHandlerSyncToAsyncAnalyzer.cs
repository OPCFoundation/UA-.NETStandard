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
            "Verify"
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0011_TokenHandlerSyncToAsync];

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
            var invocation = (IInvocationOperation)context.Operation;
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
