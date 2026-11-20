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
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0001UtilsTraceToILoggerAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0001"), Is.False,
                "A user-defined static Trace on an unrelated class must not trigger UA0001.");
        }
    }
}
