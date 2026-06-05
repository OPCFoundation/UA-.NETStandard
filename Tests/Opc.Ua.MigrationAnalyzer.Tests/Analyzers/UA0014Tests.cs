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
                .GetAnalyzerDiagnosticsAsync(new UA0014DataValueIsGoodAnalyzer(), source);

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
                .GetAnalyzerDiagnosticsAsync(new UA0014DataValueIsGoodAnalyzer(), source);

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
                .GetAnalyzerDiagnosticsAsync(new UA0014DataValueIsGoodAnalyzer(), source);

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
                .GetAnalyzerDiagnosticsAsync(new UA0014DataValueIsGoodAnalyzer(), source);

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
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
