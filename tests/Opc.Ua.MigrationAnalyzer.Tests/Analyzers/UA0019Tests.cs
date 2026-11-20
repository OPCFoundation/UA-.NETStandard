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
    /// Tests for UA0019 (obsolete DataValue(StatusCode[,DateTimeUtc]) constructor).
    /// </summary>
    [TestFixture]
    public class UA0019Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnNewDataValueStatusCodeAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static DataValue M(StatusCode sc) => new DataValue(sc);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0019DataValueStatusCodeCtorAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0019"), Is.True);
        }

        [Test]
        public async Task ReportsDiagnosticOnNewDataValueStatusCodeDateTimeUtcAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static DataValue M(StatusCode sc, DateTimeUtc ts) => new DataValue(sc, ts);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0019DataValueStatusCodeCtorAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0019 = diags.SingleOrDefault(d => d.Id == "UA0019");
            Assert.That(ua0019, Is.Not.Null);
            Assert.That(
                ua0019!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("DateTimeUtc"));
        }

        [Test]
        public async Task DoesNotReportOnNewDataValueVariantAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static DataValue M(Variant v) => new DataValue(v);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0019DataValueStatusCodeCtorAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0019"), Is.False);
        }

        [Test]
        public async Task FixRewritesStatusCodeCtorToFromStatusCodeAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static DataValue M(StatusCode sc) => new DataValue(sc);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static DataValue M(StatusCode sc) => DataValue.FromStatusCode(sc);
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0019DataValueStatusCodeCtorAnalyzer(),
                new UA0019DataValueStatusCodeCtorCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }

        [Test]
        public async Task FixRewritesStatusCodeDateTimeUtcCtorToFromStatusCodeAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static DataValue M(StatusCode sc, DateTimeUtc ts) => new DataValue(sc, ts);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static DataValue M(StatusCode sc, DateTimeUtc ts) => DataValue.FromStatusCode(sc, ts);
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0019DataValueStatusCodeCtorAnalyzer(),
                new UA0019DataValueStatusCodeCtorCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
