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
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.CodeFixers.Analyzers;

namespace Opc.Ua.CodeFixers.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0001 (Utils.Trace / Utils.LogX -> ILogger). Diagnostic-only;
    /// no code fix is shipped — the replacement requires an ILogger instance.
    /// </summary>
    [TestFixture]
    public class UA0001Tests
    {
        [Test]
        public async Task ReportsDiagnosticOnUtilsTraceAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M() => Utils.Trace("hello");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source);

            Diagnostic? ua0001 = diags.SingleOrDefault(d => d.Id == "UA0001");
            Assert.That(ua0001, Is.Not.Null, "Expected UA0001 to fire on Utils.Trace(...).");
            Assert.That(
                ua0001!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Utils.Trace"));
        }

        [Test]
        public async Task ReportsDiagnosticOnUtilsLogErrorAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M() => Utils.LogError("error: {0}", 42);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source);

            Diagnostic? ua0001 = diags.SingleOrDefault(d => d.Id == "UA0001");
            Assert.That(ua0001, Is.Not.Null, "Expected UA0001 to fire on Utils.LogError(...).");
            Assert.That(
                ua0001!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Utils.LogError"));
        }

        [Test]
        public async Task ReportsDiagnosticOnUtilsLogInformationAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M() => Utils.LogInformation("info");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0001"), Is.True,
                "Expected UA0001 to fire on Utils.LogInformation(...).");
        }

        [Test]
        public async Task DoesNotReportOnInstanceILoggerCallAsync()
        {
            const string source = """
                using Microsoft.Extensions.Logging;
                class C
                {
                    static void M(ILogger logger) => logger.LogInformation("info");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0001"), Is.False,
                "Instance ILogger.LogInformation must not trigger UA0001.");
        }

        [Test]
        public async Task DoesNotReportOnUnrelatedStaticUtilsTraceAsync()
        {
            const string source = """
                static class MyUtils
                {
                    public static void Trace(string s) { }
                }
                class C
                {
                    static void M() => MyUtils.Trace("x");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0001"), Is.False,
                "A user-defined static Trace on an unrelated class must not trigger UA0001.");
        }
    }
}
