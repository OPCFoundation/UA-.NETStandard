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
