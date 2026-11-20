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
    /// UA0018: Detect reads of the obsolete <c>Certificate</c> getter on
    /// <c>CertificateIdentifier</c>-family types and recommend
    /// <c>CertificateIdentifierResolver.ResolveAsync</c>.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0018CertificateIdentifierCertificateAnalyzer : DiagnosticAnalyzer
    {
        private const string CertificatePropertyName = "Certificate";
        private const string CertificateIdentifierTypeSuffix = "CertificateIdentifier";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0018_CertificateIdentifierCertificateGetter];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
        }

        private static void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;
            IPropertySymbol property = reference.Property;
            if (property is null || property.Name != CertificatePropertyName)
            {
                return;
            }

            bool isShim = property.IsOpcUaShim("UA0018");
            if (!isShim)
            {
                if (!property.IsObsolete())
                {
                    return;
                }

                INamedTypeSymbol containing = property.ContainingType;
                if (containing is null)
                {
                    return;
                }

                string typeName = containing.Name;
                if (typeName is null ||
                    !(typeName == CertificateIdentifierTypeSuffix ||
                        typeName.EndsWith(CertificateIdentifierTypeSuffix, System.StringComparison.Ordinal) ||
                        typeName.Contains(CertificateIdentifierTypeSuffix)))
                {
                    return;
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0018_CertificateIdentifierCertificateGetter,
                reference.Syntax.GetLocation()));
        }
    }
}
