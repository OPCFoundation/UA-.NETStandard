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
                .GetAnalyzerDiagnosticsAsync(new UA0012CertificateFactoryStaticToInstanceAnalyzer(), source);

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
                .GetAnalyzerDiagnosticsAsync(new UA0012CertificateFactoryStaticToInstanceAnalyzer(), source);

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
                .GetAnalyzerDiagnosticsAsync(new UA0012CertificateFactoryStaticToInstanceAnalyzer(), source);

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
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
