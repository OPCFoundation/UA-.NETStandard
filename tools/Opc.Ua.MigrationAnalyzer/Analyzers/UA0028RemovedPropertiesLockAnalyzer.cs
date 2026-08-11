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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0028: Detect use of <c>ApplicationConfiguration.PropertiesLock</c>, which 1.5.378
    /// exposed and 2.0 removed.
    /// </summary>
    /// <remarks>
    /// The property returned the properties dictionary itself, so the lock was the data it
    /// guarded. The member is gone in 2.0, so a consumer compiled against the new assemblies
    /// sees CS1061 rather than this diagnostic. Like
    /// <see cref="UA0025RemovedNodeDataLockAnalyzer"/>, the rule earns its keep on the
    /// migration path, where the analyzer package runs against sources still written for
    /// 1.5.378 and the member still resolves.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0028RemovedPropertiesLockAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0028_RemovedPropertiesLock];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Syntax rather than symbols: on the migration path the member no longer
            // resolves, so there is no symbol left to match on.
            context.RegisterSyntaxNodeAction(
                AnalyzeMemberAccess,
                SyntaxKind.SimpleMemberAccessExpression);
        }

        private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;

            if (memberAccess.Name.Identifier.ValueText != "PropertiesLock")
            {
                return;
            }

            ITypeSymbol? type = context.SemanticModel
                .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

            // The type is unresolvable on the migration path when the member has already
            // gone, so an unknown type is reported rather than skipped: the name is
            // distinctive enough that a false positive is unlikely.
            if (type is not null &&
                !TypeNames.DerivesFrom(type, "ApplicationConfiguration") &&
                type.Name != "ApplicationConfiguration")
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UA0028_RemovedPropertiesLock,
                    memberAccess.GetLocation()));
        }
    }
}
