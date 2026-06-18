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

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0010 (using/Dispose on identity types that are no longer
    /// IDisposable in 2.0). The analyzer is diagnostic-only: no code fix is
    /// registered.
    /// </summary>
    [TestFixture]
    public class UA0010Tests
    {
        [Test]
        public async Task ReportsOnUsingDeclarationOfCertificateIdentifierAsync()
        {
            const string source = """
                #pragma warning disable CS1674
                using Opc.Ua;
                class C
                {
                    static void M()
                    {
                        using var ci = new CertificateIdentifier();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0010RemoveDisposableAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0010"), Is.True,
                "Expected UA0010 to fire on 'using var ci = new CertificateIdentifier();'.");
        }

        [Test]
        public async Task ReportsOnUsingStatementOfUserIdentityAsync()
        {
            const string source = """
                #pragma warning disable CS1674
                using Opc.Ua;
                class C
                {
                    static void M()
                    {
                        using (var ui = new UserIdentity()) { }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0010RemoveDisposableAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0010"), Is.True,
                "Expected UA0010 to fire on 'using (var ui = new UserIdentity()) { }'.");
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedDisposableAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M()
                    {
                        using var ms = new System.IO.MemoryStream();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0010RemoveDisposableAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0010"), Is.False,
                "Unrelated IDisposable types must not trigger UA0010.");
        }

        [Test]
        public async Task DoesNotReportOnPlainDeclarationAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M()
                    {
                        var ci = new CertificateIdentifier();
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0010RemoveDisposableAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0010"), Is.False,
                "A plain variable declaration without 'using' must not trigger UA0010.");
        }
    }
}
