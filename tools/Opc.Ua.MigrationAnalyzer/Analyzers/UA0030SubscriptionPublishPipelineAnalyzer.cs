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
    /// UA0030: Detect use of the publish-pipeline members that the server
    /// <c>ISubscription</c> exposed in 1.5.378 and of the <c>SessionPublishQueue</c>
    /// type, both server-internal in 2.0.
    /// </summary>
    /// <remarks>
    /// The members are gone in 2.0, so a consumer compiled against the new assemblies sees
    /// CS1061 rather than this diagnostic. The rule earns its keep on the migration path,
    /// where the analyzer package runs against sources still written for 1.5.378.
    /// The receiver check matches simple type names, so a hand-written client-side
    /// <c>ISubscription</c> shim with a <c>Publish</c> or <c>Acknowledge</c> member could
    /// see a spurious hit — the same tradeoff UA0024 accepts.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0030SubscriptionPublishPipelineAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0030_SubscriptionPublishPipelineInternalized];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Syntax rather than symbols: on the migration path the member no longer
            // resolves, so there is no symbol left to match on.
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
            context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
        }

        private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;

            string memberName = memberAccess.Name.Identifier.ValueText;
            string? guidance = GuidanceFor(memberName);
            if (guidance is null)
            {
                return;
            }

            ITypeSymbol? type = context.SemanticModel
                .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

            if (type is null || !TypeNames.IsOrImplements(type, "ISubscription"))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UA0030_SubscriptionPublishPipelineInternalized,
                    memberAccess.GetLocation(),
                    $"ISubscription.{memberName}",
                    guidance));
        }

        /// <summary>
        /// Flags references to the <c>SessionPublishQueue</c> type, which is internal in 2.0.
        /// </summary>
        private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
        {
            var identifier = (IdentifierNameSyntax)context.Node;
            if (identifier.Identifier.ValueText != "SessionPublishQueue")
            {
                return;
            }

            // Skip the right side of a member access (covered above when relevant) and
            // suppress unrelated same-name types when the symbol still resolves.
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
                ReferenceEquals(memberAccess.Name, identifier))
            {
                return;
            }

            SymbolInfo symbolInfo = context.SemanticModel
                .GetSymbolInfo(identifier, context.CancellationToken);
            ISymbol? symbol = symbolInfo.Symbol ??
                (symbolInfo.CandidateSymbols.Length > 0 ? symbolInfo.CandidateSymbols[0] : null);

            if (symbol is not null &&
                symbol.ContainingNamespace?.ToDisplayString() != "Opc.Ua.Server")
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UA0030_SubscriptionPublishPipelineInternalized,
                    identifier.GetLocation(),
                    "SessionPublishQueue",
                    "the queue is part of the server-internal publish pipeline; use the Publish " +
                    "service and the ISubscriptionManager surface instead of driving the queue directly"));
        }

        /// <summary>
        /// Returns the migration guidance for a removed member, or <c>null</c> when the
        /// name is not one of the removed <c>ISubscription</c> members.
        /// </summary>
        private static string? GuidanceFor(string memberName)
        {
            return memberName switch
            {
                "ItemReadyToPublish" or "ItemNotificationsAvailable" =>
                    "remove the call; the member was a no-op since 1.5.x and was deleted",
                "SessionClosed" =>
                    "session teardown releases subscriptions through " +
                    "ISubscriptionManager.SessionClosingAsync; custom subscriptions derive from Subscription",
                "PublishTimerExpired" or "PublishTimeout" or "SubscriptionTransferred" or
                "AvailableSequenceNumbersForRetransmission" or "QueueOverflowHandler" or
                "Acknowledge" or "Publish" =>
                    "the publish pipeline is server-internal; use the Publish/Republish services " +
                    "and the ISubscriptionManager surface, and derive custom subscriptions from Subscription",
                _ => null
            };
        }
    }
}
