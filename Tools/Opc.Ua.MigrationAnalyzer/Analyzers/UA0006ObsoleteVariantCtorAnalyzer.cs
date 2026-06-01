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
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0006: Detect <c>new Variant(object|DateTime|Guid|byte[])</c> obsolete
    /// constructor calls and recommend the generic <c>Variant.From&lt;T&gt;</c>
    /// factory together with the appropriate wrapper type.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0006ObsoleteVariantCtorAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0006_ObsoleteVariantCtor);

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
            if (containing is null || containing.ToDisplayString() != "Opc.Ua.Variant")
            {
                return;
            }

            if (!ctor.IsObsolete())
            {
                return;
            }

            ITypeSymbol argType = ctor.Parameters[0].Type;
            string label = GetLabel(argType);
            if (label is null)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0006_ObsoleteVariantCtor,
                creation.Syntax.GetLocation(),
                label));
        }

        private static string GetLabel(ITypeSymbol type)
        {
            if (type is null)
            {
                return null;
            }
            switch (type.SpecialType)
            {
                case SpecialType.System_Object:
                    return "object";
                case SpecialType.System_DateTime:
                    return "DateTime";
            }
            if (type.ToDisplayString() == "System.Guid")
            {
                return "Guid";
            }
            if (type is IArrayTypeSymbol arr &&
                arr.ElementType?.SpecialType == SpecialType.System_Byte)
            {
                return "byte[]";
            }
            return null;
        }
    }
}
