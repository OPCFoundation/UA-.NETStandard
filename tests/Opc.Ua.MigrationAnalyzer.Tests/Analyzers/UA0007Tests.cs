// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Analyzers;
using Opc.Ua.MigrationAnalyzer.CodeFixer;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0007 (obsolete NodeId/ExpandedNodeId string constructor).
    /// </summary>
    [TestFixture]
    public class UA0007Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnNewNodeIdStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static NodeId M() => new NodeId("ns=1;i=42");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0007ObsoleteNodeIdStringCtorAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0007 = diags.SingleOrDefault(d => d.Id == "UA0007");
            Assert.That(ua0007, Is.Not.Null);
            Assert.That(
                ua0007!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("NodeId"));
        }

        [Test]
        public async Task ReportsDiagnosticOnNewExpandedNodeIdStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static ExpandedNodeId M() => new ExpandedNodeId("ns=1;i=42");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0007ObsoleteNodeIdStringCtorAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0007 = diags.SingleOrDefault(d => d.Id == "UA0007");
            Assert.That(ua0007, Is.Not.Null);
            Assert.That(
                ua0007!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("ExpandedNodeId"));
        }

        [Test]
        public async Task DoesNotReportOnNewNodeIdUintAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static NodeId M() => new NodeId(42u);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0007ObsoleteNodeIdStringCtorAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0007"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnNewNodeIdUintNamespaceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static NodeId M() => new NodeId(42u, (ushort)0);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0007ObsoleteNodeIdStringCtorAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0007"), Is.False);
        }

        [Test]
        public async Task FixRewritesStringCtorToParseAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static NodeId M(string s) => new NodeId(s);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static NodeId M(string s) => NodeId.Parse(s);
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0007ObsoleteNodeIdStringCtorAnalyzer(),
                new UA0007ObsoleteNodeIdStringCtorCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
