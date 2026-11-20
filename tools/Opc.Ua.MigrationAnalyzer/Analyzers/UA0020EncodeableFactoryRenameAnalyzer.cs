/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
    /// UA0020: Detect references to the obsolete
    /// <c>EncodeableFactory.GlobalFactory</c> static property (Form A) and
    /// instance <c>EncodeableFactory.Create()</c> calls (Form B) and
    /// recommend the 2.0 replacements.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0020EncodeableFactoryRenameAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// Public surface preserved for source compatibility; values delegate to the
        /// shared WellKnownProperties (also linked into the CodeFixes assembly).
        /// </summary>
        public const string FormProperty = WellKnownProperties.Form;
        public const string FormGlobalFactory = WellKnownProperties.FormGlobalFactory;
        public const string FormCreate = WellKnownProperties.FormCreate;

        private const string EncodeableFactoryTypeName = "Opc.Ua.EncodeableFactory";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0020_EncodeableFactoryRename];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        }

        private static void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;
            IPropertySymbol property = reference.Property;
            if (property is null || property.Name != "GlobalFactory")
            {
                return;
            }

            bool isShim = property.IsOpcUaShim("UA0020");
            if (!isShim)
            {
                if (!property.IsStatic)
                {
                    return;
                }
                INamedTypeSymbol containing = property.ContainingType;
                if (containing is null || containing.ToDisplayString() != EncodeableFactoryTypeName)
                {
                    return;
                }
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
            var invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;
            if (method is null || method.Name != "Create")
            {
                return;
            }

            bool isShim = method.IsOpcUaShim("UA0020");
            if (!isShim)
            {
                if (method.IsStatic || method.Parameters.Length != 0)
                {
                    return;
                }
                INamedTypeSymbol containing = method.ContainingType;
                if (containing is null || containing.ToDisplayString() != EncodeableFactoryTypeName)
                {
                    return;
                }
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
