/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
    /// Tests for UA0002 (removed &lt;Type&gt;Collection wrappers).
    /// </summary>
    [TestFixture]
    public class UA0002Tests
    {
        private static bool IsUserDiagnostic(Diagnostic d)
        {
            return d.Id == "UA0002" && d.Location.SourceTree?.FilePath == "Test.cs";
        }

        [Test]
        public async Task ReportsDiagnosticOnInt32CollectionVariableDeclarationAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M() { Int32Collection x = new Int32Collection(); }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0002RemovedCollectionTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Diagnostic? ua0002 = diags.FirstOrDefault(IsUserDiagnostic);
            Assert.That(ua0002, Is.Not.Null,
                "Expected UA0002 to fire on the Int32Collection variable declaration.");
            Assert.That(
                ua0002!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("Int32Collection"));
        }

        [Test]
        public async Task ReportsDiagnosticOnNodeIdCollectionParameterAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static void M(NodeIdCollection ids) { _ = ids; }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0002RemovedCollectionTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(IsUserDiagnostic), Is.True,
                "Expected UA0002 to fire on the NodeIdCollection parameter type.");
        }

        [Test]
        public async Task DoesNotReportOnListOfIntAsync()
        {
            const string source = """
                using System.Collections.Generic;
                class C
                {
                    static void M() { List<int> x = new List<int>(); _ = x; }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0002RemovedCollectionTypeAnalyzer(), source)
                .ConfigureAwait(false);

            Assert.That(diags.Any(IsUserDiagnostic), Is.False,
                "List<int> must not trigger UA0002.");
        }

        [Test]
        public async Task FixRewritesInt32CollectionDeclarationAndCreationAsync()
        {
            const string source = """
                using System.Collections.Generic;
                using Opc.Ua;
                class C
                {
                    static void M() { Int32Collection x = new Int32Collection(); _ = x; }
                }
                """;
            const string expected = """
                using System.Collections.Generic;
                using Opc.Ua;
                class C
                {
                    static void M() { List<int> x = new List<int>(); _ = x; }
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0002RemovedCollectionTypeAnalyzer(),
                new UA0002RemovedCollectionTypeCodeFix(),
                source).ConfigureAwait(false);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
