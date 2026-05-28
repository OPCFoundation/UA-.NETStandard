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
using Opc.Ua.CodeFixers.Diagnostics;

namespace Opc.Ua.CodeFixers.Analyzers
{
    /// <summary>
    /// UA0007: Detect <c>new NodeId(string)</c> / <c>new ExpandedNodeId(string)</c>
    /// constructor calls and recommend the explicit <c>Parse</c> factory.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0007ObsoleteNodeIdStringCtorAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0007_ObsoleteNodeIdStringCtor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        }

        private static void AnalyzeObjectCreation(OperationAnalysisContext context)
        {
            IObjectCreationOperation creation = (IObjectCreationOperation)context.Operation;
            IMethodSymbol ctor = creation.Constructor;
            if (ctor is null || ctor.Parameters.Length != 1)
            {
                return;
            }

            INamedTypeSymbol containing = ctor.ContainingType;
            if (containing is null)
            {
                return;
            }
            string containingName = containing.ToDisplayString();
            if (containingName != "Opc.Ua.NodeId" && containingName != "Opc.Ua.ExpandedNodeId")
            {
                return;
            }

            if (ctor.Parameters[0].Type?.SpecialType != SpecialType.System_String)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0007_ObsoleteNodeIdStringCtor,
                creation.Syntax.GetLocation(),
                containing.Name));
        }
    }
}
