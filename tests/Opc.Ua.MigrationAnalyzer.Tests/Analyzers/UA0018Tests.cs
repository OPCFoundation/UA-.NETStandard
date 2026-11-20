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

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0018 (obsolete CertificateIdentifier.Certificate getter).
    /// </summary>
    [TestFixture]
    public class UA0018Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnCertificateGetterReadAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object? M(CertificateIdentifierWithObsoleteCertificate id)
                    {
                #pragma warning disable CS0618
                        var c = id.Certificate;
                #pragma warning restore CS0618
                        return c;
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0018CertificateIdentifierCertificateAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0018 = diags.SingleOrDefault(d => d.Id == "UA0018");
            Assert.That(ua0018, Is.Not.Null, "Expected UA0018 to fire on id.Certificate read.");
            Assert.That(
                ua0018!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("CertificateIdentifier"));
        }

        [Test]
        public async Task ReportsDiagnosticOnCertificateGetterInConditionAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static bool M(CertificateIdentifierWithObsoleteCertificate id)
                    {
                #pragma warning disable CS0618
                        if (id.Certificate != null) { return true; }
                #pragma warning restore CS0618
                        return false;
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0018CertificateIdentifierCertificateAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0018"), Is.True,
                "Expected UA0018 to fire on id.Certificate in a null comparison.");
        }

        [Test]
        public async Task DoesNotReportOnSubjectNamePropertyAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static string? M(CertificateIdentifier id) => id.SubjectName;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0018CertificateIdentifierCertificateAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0018"), Is.False,
                "id.SubjectName must not trigger UA0018.");
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedTypeCertificatePropertyAsync()
        {
            const string source = """
                class Other
                {
                    public object? Certificate => null;
                }
                class C
                {
                    static object? M(Other o) => o.Certificate;
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0018CertificateIdentifierCertificateAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0018"), Is.False,
                "Certificate property on an unrelated type must not trigger UA0018.");
        }

        [Test]
        public async Task ReportsDiagnosticOnShimPropertyAccessAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static object? M(CertificateIdentifierShimHost host)
                    {
                #pragma warning disable CS0618
                        return host.Certificate;
                #pragma warning restore CS0618
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(
                    new UA0018CertificateIdentifierCertificateAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0018"), Is.True,
                "Expected UA0018 to fire on a property carrying [OpcUaShim(\"UA0018\")].");
        }
    }
}
