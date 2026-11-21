// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Analyzers;
using Opc.Ua.MigrationAnalyzer.CodeFixer;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0005 (byte[] passed where ByteString is now expected).
    /// </summary>
    [TestFixture]
    public class UA0005Tests
    {
        [Test]
        public async Task ReportsOnByteArrayArgumentWhereByteStringIsExpectedAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller(byte[] arr) => M(arr);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0005ByteArrayToByteStringAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0005 = diags.SingleOrDefault(d => d.Id == "UA0005");
            Assert.That(ua0005, Is.Not.Null, "Expected UA0005 to fire when byte[] is passed to a ByteString parameter.");
            Assert.That(
                ua0005!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("M"));
        }

        [Test]
        public async Task DoesNotReportWhenArgumentAlreadyToByteStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller(byte[] arr) => M(arr.ToByteString());
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0005ByteArrayToByteStringAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0005"), Is.False,
                "Calling .ToByteString() at the call site must not trigger UA0005.");
        }

        [Test]
        public async Task DoesNotReportWhenByteArrayOverloadBindsAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void Caller(byte[] arr) => ByteStringApi.Process(arr);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0005ByteArrayToByteStringAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0005"), Is.False,
                "When the byte[] overload binds the rule must not fire.");
        }

        [Test]
        public async Task DoesNotReportOnDefaultLiteralAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller() => M(default);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0005ByteArrayToByteStringAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(d => d.Id == "UA0005"), Is.False,
                "A 'default' literal has type ByteString — UA0005 must not fire.");
        }

        [Test]
        public async Task FixAppendsToByteStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller(byte[] arr) => M(arr);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static void M(ByteString b) { }
                    static void Caller(byte[] arr) => M(arr.ToByteString());
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0005ByteArrayToByteStringAnalyzer(),
                new UA0005ByteArrayToByteStringCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
