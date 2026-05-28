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
    /// UA0020: Detect references to the obsolete
    /// <c>EncodeableFactory.GlobalFactory</c> static property (Form A) and
    /// instance <c>EncodeableFactory.Create()</c> calls (Form B) and
    /// recommend the 2.0 replacements.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0020EncodeableFactoryRenameAnalyzer : DiagnosticAnalyzer
    {
        public const string FormProperty = "Form";
        public const string FormGlobalFactory = "A";
        public const string FormCreate = "B";

        private const string EncodeableFactoryTypeName = "Opc.Ua.EncodeableFactory";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0020_EncodeableFactoryRename);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        }

        private static void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            IPropertyReferenceOperation reference = (IPropertyReferenceOperation)context.Operation;
            IPropertySymbol property = reference.Property;
            if (property is null || property.Name != "GlobalFactory" || !property.IsStatic)
            {
                return;
            }
            INamedTypeSymbol containing = property.ContainingType;
            if (containing is null || containing.ToDisplayString() != EncodeableFactoryTypeName)
            {
                return;
            }

            ImmutableDictionary<string, string> properties = ImmutableDictionary<string, string>.Empty
                .Add(FormProperty, FormGlobalFactory);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0020_EncodeableFactoryRename,
                reference.Syntax.GetLocation(),
                properties,
                "EncodeableFactory.GlobalFactory",
                "ServiceMessageContext.Factory"));
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            IInvocationOperation invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;
            if (method is null || method.Name != "Create" || method.IsStatic || method.Parameters.Length != 0)
            {
                return;
            }
            INamedTypeSymbol containing = method.ContainingType;
            if (containing is null || containing.ToDisplayString() != EncodeableFactoryTypeName)
            {
                return;
            }

            ImmutableDictionary<string, string> properties = ImmutableDictionary<string, string>.Empty
                .Add(FormProperty, FormCreate);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0020_EncodeableFactoryRename,
                invocation.Syntax.GetLocation(),
                properties,
                "EncodeableFactory.Create()",
                "Fork()"));
        }
    }
}
