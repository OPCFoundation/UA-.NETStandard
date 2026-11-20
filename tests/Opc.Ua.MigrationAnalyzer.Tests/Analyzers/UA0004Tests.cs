/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Analyzers;
using Opc.Ua.MigrationAnalyzer.CodeFixer;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0004 (null-conditional access on now-struct built-in type).
    /// </summary>
    [TestFixture]
    public class UA0004Tests
    {
        [Test]
        public async Task ReportsOnNodeIdConditionalAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(NodeId nodeId) => nodeId?.NamespaceIndex;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0004ConditionalAccessOnStructAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0004 = diags.SingleOrDefault(d => d.Id == "UA0004");
            Assert.That(ua0004, Is.Not.Null, "Expected UA0004 to fire on nodeId?.NamespaceIndex.");
            Assert.That(
                ua0004!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("NodeId"));
        }

        [Test]
        public async Task ReportsOnDataValueConditionalAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(DataValue dv) => dv?.IsGood;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0004ConditionalAccessOnStructAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0004"), Is.True,
                "Expected UA0004 to fire on dv?.IsGood.");
        }

        [Test]
        public async Task DoesNotReportOnStringConditionalAccessAsync()
        {
            const string source = """
                class C
                {
                    static object M(string s) => s?.Length;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0004ConditionalAccessOnStructAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0004"), Is.False,
                "Conditional access on string must not trigger UA0004.");
        }

        [Test]
        public async Task DoesNotReportOnPlainMemberAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(NodeId nodeId) => nodeId.NamespaceIndex;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0004ConditionalAccessOnStructAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0004"), Is.False,
                "Plain member access without '?.' must not trigger UA0004.");
        }

        [Test]
        public async Task FixRewritesConditionalAccessToDirectAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(NodeId nodeId) => nodeId?.NamespaceIndex;
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static object M(NodeId nodeId) => nodeId.NamespaceIndex;
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0004ConditionalAccessOnStructAnalyzer(),
                new UA0004ConditionalAccessOnStructCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
