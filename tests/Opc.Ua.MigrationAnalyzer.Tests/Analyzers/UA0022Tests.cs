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
    /// Tests for UA0022 (ApplicationConfiguration.CertificateValidator /
    /// ServerBase.CertificateValidator property rename to CertificateManager).
    /// </summary>
    [TestFixture]
    public class UA0022Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnApplicationConfigurationCertificateValidatorAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object? M(ApplicationConfiguration cfg) => cfg.CertificateValidator;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0022CertificateValidatorPropertyRenameAnalyzer(), source).ConfigureAwait(false);

            Diagnostic? ua0022 = diags.SingleOrDefault(d => d.Id == "UA0022");
            Assert.That(ua0022, Is.Not.Null,
                "Expected UA0022 to fire on ApplicationConfiguration.CertificateValidator access.");
            Assert.That(
                ua0022!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("ApplicationConfiguration"));
        }

        [Test]
        public async Task ReportsDiagnosticOnServerBaseCertificateValidatorAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object? M(ServerBase sb) => sb.CertificateValidator;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0022CertificateValidatorPropertyRenameAnalyzer(), source).ConfigureAwait(false);

            Diagnostic? ua0022 = diags.SingleOrDefault(d => d.Id == "UA0022");
            Assert.That(ua0022, Is.Not.Null,
                "Expected UA0022 to fire on ServerBase.CertificateValidator access.");
            Assert.That(
                ua0022!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("ServerBase"));
        }

        [Test]
        public async Task DoesNotReportOnApplicationConfigurationCertificateManagerAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object? M(ApplicationConfiguration cfg) => cfg.CertificateManager;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0022CertificateValidatorPropertyRenameAnalyzer(), source).ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0022"), Is.False,
                "Modern CertificateManager access must not trigger UA0022.");
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedTypeCertificateValidatorPropertyAsync()
        {
            // No 'using Opc.Ua;' and the type name is unrelated; both detection paths
            // must remain silent.
            const string source = """
                namespace Other.Pki
                {
                    public class Foo
                    {
                        public int CertificateValidator { get; }
                    }
                    class C
                    {
                        static int M(Foo f) => f.CertificateValidator;
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0022CertificateValidatorPropertyRenameAnalyzer(), source).ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0022"), Is.False,
                "Unrelated CertificateValidator property on a non-Opc.Ua type must not trigger UA0022.");
        }

        [Test]
        public async Task FixRewritesCertificateValidatorToCertificateManagerAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object? M(ApplicationConfiguration cfg) => cfg.CertificateValidator;
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static object? M(ApplicationConfiguration cfg) => cfg.CertificateManager;
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0022CertificateValidatorPropertyRenameAnalyzer(),
                new UA0022CertificateValidatorPropertyRenameCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
