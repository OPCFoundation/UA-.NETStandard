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
using Opc.Ua.CodeFixers.Helpers;

namespace Opc.Ua.CodeFixers.Analyzers
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
            ImmutableArray.Create(DiagnosticDescriptors.UA0018_CertificateIdentifierCertificateGetter);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
        }

        private static void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            IPropertyReferenceOperation reference = (IPropertyReferenceOperation)context.Operation;
            IPropertySymbol property = reference.Property;
            if (property is null || property.Name != CertificatePropertyName)
            {
                return;
            }

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

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0018_CertificateIdentifierCertificateGetter,
                reference.Syntax.GetLocation()));
        }
    }
}
