/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.CodeFixer
{
    /// <summary>
    /// UA0010 code fix: intentionally diagnostic-only — removing the
    /// <c>using</c> may change variable scope, so we surface the warning and
    /// let the developer rework the lifecycle by hand.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0010RemoveDisposableCodeFix))]
    [Shared]
    public sealed class UA0010RemoveDisposableCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0010];

        public override FixAllProvider GetFixAllProvider()
        {
            return null;
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            // No automatic code action: removing the 'using' may change the
            // variable's scope/lifetime, which is not safe to do mechanically.
            return Task.CompletedTask;
        }
    }
}
