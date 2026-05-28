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
    /// UA0012: Detect calls to obsolete static <c>CertificateFactory</c> members
    /// and recommend the <c>DefaultCertificateFactory.Instance</c> singleton.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0012CertificateFactoryStaticToInstanceAnalyzer : DiagnosticAnalyzer
    {
        private static readonly HashSet<string> s_targetNames =
        [
            "Create",
            "CreateCertificate",
            "CreateSigningRequest",
            "RevokeCertificate",
            "CreateCertificateWithPEMPrivateKey",
            "CreateCertificateWithPrivateKey",
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0012_CertificateFactoryStaticToInstance);

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
            if (containing is null || containing.ToDisplayString() != "Opc.Ua.CertificateFactory")
            {
                return;
            }

            if (!method.IsObsolete())
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0012_CertificateFactoryStaticToInstance,
                invocation.Syntax.GetLocation(),
                method.Name));
        }
    }
}
