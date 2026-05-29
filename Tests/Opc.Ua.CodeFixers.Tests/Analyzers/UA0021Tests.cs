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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.CodeFixers.Analyzers;

namespace Opc.Ua.CodeFixers.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0021 (CertificateValidator / CertificateValidationEventArgs structural rename).
    /// The rule is diagnostic-only; there is no accompanying code fix because the 1.6
    /// replacement is structural (event-based per-error accept handler -> async
    /// ValidateAsync returning CertificateValidationResult plus AcceptError callback).
    /// </summary>
    [TestFixture]
    public class UA0021Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnCertificateValidatorReferenceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(CertificateValidator v) { }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source);

            Diagnostic? ua0021 = diags.SingleOrDefault(d => d.Id == "UA0021");
            Assert.That(ua0021, Is.Not.Null);
            Assert.That(
                ua0021!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("CertificateValidator"));
        }

        [Test]
        public async Task ReportsDiagnosticOnCertificateValidationEventArgsReferenceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(CertificateValidationEventArgs e) { }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source);

            Diagnostic? ua0021 = diags.SingleOrDefault(d => d.Id == "UA0021");
            Assert.That(ua0021, Is.Not.Null);
            Assert.That(
                ua0021!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("CertificateValidationEventArgs"));
        }

        [Test]
        public async Task DoesNotReportOnReplacementTypesAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ICertificateManager m, ICertificateValidatorEx v, CertificateValidationResult r) { }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0021"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedSimilarlyNamedTypeAsync()
        {
            // A user-defined CertificateValidator in a non-Opc.Ua namespace, with no
            // 'using Opc.Ua;' anywhere — the symbol resolves to the user's own type
            // (not [Obsolete]) and the syntactic fallback is also gated by the using.
            const string source = """
                namespace Other.Pki
                {
                    public class CertificateValidator { }
                    public class CertificateValidationEventArgs { }
                    class C
                    {
                        static void M(CertificateValidator v, CertificateValidationEventArgs e) { }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0021"), Is.False);
        }

        [Test]
        public async Task ReportsExactlyTwoDiagnosticsForBothLegacyTypesAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(CertificateValidator v, CertificateValidationEventArgs e) { }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0021CertificateValidatorRenameAnalyzer(), source);

            Assert.That(diags.Count(d => d.Id == "UA0021"), Is.EqualTo(2));
        }
    }
}
