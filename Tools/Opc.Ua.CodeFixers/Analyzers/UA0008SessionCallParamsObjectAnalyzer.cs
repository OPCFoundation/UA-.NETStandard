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
using Opc.Ua.CodeFixers.Diagnostics;
using Opc.Ua.CodeFixers.Helpers;

namespace Opc.Ua.CodeFixers.Analyzers
{
    /// <summary>
    /// UA0008: Detect <c>ISession.Call</c> / <c>ISession.CallAsync</c>
    /// invocations whose variadic arguments are raw values instead of
    /// <c>Variant</c> instances, and recommend wrapping with
    /// <c>Variant.From(...)</c>.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0008SessionCallParamsObjectAnalyzer : DiagnosticAnalyzer
    {
        public const string MethodNameProperty = "MethodName";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0008_SessionCallParamsObject);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol sessionInterface = context.Compilation.GetTypeByMetadataName("Opc.Ua.ISession");
            INamedTypeSymbol variantType = context.Compilation.GetTypeByMetadataName("Opc.Ua.Variant");
            if (sessionInterface is null || variantType is null)
            {
                return;
            }
            context.RegisterSyntaxNodeAction(
                c => AnalyzeInvocation(c, sessionInterface, variantType),
                SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol sessionInterface,
            INamedTypeSymbol variantType)
        {
            InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return;
            }

            string methodName = memberAccess.Name.Identifier.ValueText;
            if (methodName != "Call" && methodName != "CallAsync")
            {
                return;
            }

            ITypeSymbol receiverType = context.SemanticModel
                .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
            if (receiverType is null || !receiverType.IsAssignableTo(sessionInterface))
            {
                return;
            }

            int firstVariadicIndex = methodName == "Call" ? 2 : 3;
            IReadOnlyList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
            if (args.Count <= firstVariadicIndex)
            {
                return;
            }

            bool anyNonVariant = false;
            for (int i = firstVariadicIndex; i < args.Count; i++)
            {
                ITypeSymbol argType = context.SemanticModel
                    .GetTypeInfo(args[i].Expression, context.CancellationToken).Type;
                if (argType is null || !SymbolEqualityComparer.Default.Equals(argType, variantType))
                {
                    anyNonVariant = true;
                    break;
                }
            }

            if (!anyNonVariant)
            {
                return;
            }

            ImmutableDictionary<string, string> properties = ImmutableDictionary<string, string>.Empty
                .Add(MethodNameProperty, methodName);

            string display = receiverType.Name + "." + methodName;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0008_SessionCallParamsObject,
                invocation.GetLocation(),
                properties,
                display));
        }
    }
}
