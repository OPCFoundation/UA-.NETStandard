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
    /// Tests for UA0003 (null comparison on now-struct built-in type).
    /// </summary>
    [TestFixture]
    public class UA0003Tests
    {
        [Test]
        public async Task ReportsOnNodeIdEqualsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => n == null;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0003 = diags.SingleOrDefault(d => d.Id == "UA0003");
            Assert.That(ua0003, Is.Not.Null);
            Assert.That(
                ua0003!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("NodeId"));
        }

        [Test]
        public async Task ReportsOnNullEqualsNodeIdAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => null == n;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0003"), Is.True);
        }

        [Test]
        public async Task ReportsOnLocalizedTextNotEqualsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(LocalizedText lt) => lt != null;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0003 = diags.SingleOrDefault(d => d.Id == "UA0003");
            Assert.That(ua0003, Is.Not.Null);
            Assert.That(
                ua0003!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("LocalizedText"));
        }

        [Test]
        public async Task DoesNotReportOnStringEqualsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(string s) => s == null;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0003"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnNullableNodeIdEqualsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId? n) => n == null;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0003"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnNullableLocalizedTextNotEqualsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(LocalizedText? value) => null != value;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0003"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnEqualsMethodCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => n.Equals(null);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0003NullCheckOnStructTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0003"), Is.False);
        }

        [Test]
        public async Task FixRewritesNodeIdEqualsNullToIsNullAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => n == null;
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static bool M(NodeId n) => n.IsNull;
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0003NullCheckOnStructTypeAnalyzer(),
                new UA0003NullCheckOnStructTypeCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }

        [Test]
        public async Task FixRewritesLocalizedTextNotEqualsNullToNotIsNullOrEmptyAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(LocalizedText lt) => lt != null;
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static bool M(LocalizedText lt) => !lt.IsNullOrEmpty;
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0003NullCheckOnStructTypeAnalyzer(),
                new UA0003NullCheckOnStructTypeCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
