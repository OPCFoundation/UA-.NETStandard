// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            [DiagnosticDescriptors.UA0006_ObsoleteVariantCtor];

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
