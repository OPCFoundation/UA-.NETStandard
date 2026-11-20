/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
    /// Tests for UA0014 (DataValue.IsGood static helper -> instance property).
    /// </summary>
    [TestFixture]
    public class UA0014Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnStaticDataValueIsGoodCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(DataValue dv) => DataValue.IsGood(dv);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0014DataValueIsGoodAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0014 = diags.SingleOrDefault(d => d.Id == "UA0014");
            Assert.That(ua0014, Is.Not.Null, "Expected UA0014 to fire on DataValue.IsGood(dv).");
            Assert.That(ua0014!.GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("IsGood"));
        }

        [Test]
        public async Task ReportsDiagnosticOnDataValueExtensionsIsBadCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(DataValue dv) => DataValueExtensions.IsBad(dv);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0014DataValueIsGoodAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0014"),
                "Expected UA0014 to fire on DataValueExtensions.IsBad(dv).");
        }

        [Test]
        public async Task DoesNotReportOnInstancePropertyAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(DataValue dv) => dv.IsGood;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0014DataValueIsGoodAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0014"), Is.False,
                "Instance property access dv.IsGood must not trigger UA0014.");
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedSingleArgStaticCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool IsGood(DataValue dv) => false;
                    static bool M(DataValue dv) => IsGood(dv);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0014DataValueIsGoodAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0014"), Is.False,
                "A user-defined static IsGood on an unrelated class must not trigger UA0014.");
        }

        [Test]
        public async Task FixRewritesStaticCallToInstancePropertyAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(DataValue dv) => DataValue.IsGood(dv);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static bool M(DataValue dv) => dv.IsGood;
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0014DataValueIsGoodAnalyzer(),
                new UA0014DataValueIsGoodCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
