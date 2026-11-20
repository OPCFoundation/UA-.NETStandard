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
    /// Tests for UA0012 (obsolete static CertificateFactory members).
    /// </summary>
    [TestFixture]
    public class UA0012Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnCertificateFactoryCreateAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M() => CertificateFactory.Create("CN=Test");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0012CertificateFactoryStaticToInstanceAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0012 = diags.SingleOrDefault(d => d.Id == "UA0012");
            Assert.That(ua0012, Is.Not.Null);
            Assert.That(
                ua0012!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("Create"));
        }

        [Test]
        public async Task ReportsDiagnosticOnCertificateFactoryCreateSigningRequestAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M() => CertificateFactory.CreateSigningRequest("CN=Test");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0012CertificateFactoryStaticToInstanceAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0012"), Is.True);
        }

        [Test]
        public async Task DoesNotReportOnDefaultCertificateFactoryInstanceCallAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M() => DefaultCertificateFactory.Instance.Create("CN=Test");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0012CertificateFactoryStaticToInstanceAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0012"), Is.False);
        }

        [Test]
        public async Task FixRewritesStaticCallToInstanceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object M(string s) => CertificateFactory.Create(s);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static object M(string s) => DefaultCertificateFactory.Instance.Create(s);
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0012CertificateFactoryStaticToInstanceAnalyzer(),
                new UA0012CertificateFactoryStaticToInstanceCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
