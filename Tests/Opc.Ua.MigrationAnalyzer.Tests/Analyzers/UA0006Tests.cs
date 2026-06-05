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
    /// Tests for UA0006 (obsolete Variant(object|DateTime|Guid|byte[]) constructors).
    /// </summary>
    [TestFixture]
    public class UA0006Tests
    {
        [Test]
        public async Task ReportsOnNewVariantObjectAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static Variant M() => new Variant((object)42);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0006ObsoleteVariantCtorAnalyzer(), source);

            Diagnostic? ua0006 = diags.SingleOrDefault(d => d.Id == "UA0006");
            Assert.That(ua0006, Is.Not.Null);
            Assert.That(
                ua0006!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("object"));
        }

        [Test]
        public async Task ReportsOnNewVariantDateTimeAsync()
        {
            const string source = """
                using System;
                using Opc.Ua;
                class C
                {
                    static Variant M() => new Variant(DateTime.UtcNow);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0006ObsoleteVariantCtorAnalyzer(), source);

            Diagnostic? ua0006 = diags.SingleOrDefault(d => d.Id == "UA0006");
            Assert.That(ua0006, Is.Not.Null);
            Assert.That(
                ua0006!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("DateTime"));
        }

        [Test]
        public async Task ReportsOnNewVariantGuidAsync()
        {
            const string source = """
                using System;
                using Opc.Ua;
                class C
                {
                    static Variant M() => new Variant(Guid.NewGuid());
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0006ObsoleteVariantCtorAnalyzer(), source);

            Diagnostic? ua0006 = diags.SingleOrDefault(d => d.Id == "UA0006");
            Assert.That(ua0006, Is.Not.Null);
            Assert.That(
                ua0006!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("Guid"));
        }

        [Test]
        public async Task ReportsOnNewVariantByteArrayAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static Variant M() => new Variant(new byte[] { 1, 2, 3 });
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0006ObsoleteVariantCtorAnalyzer(), source);

            Diagnostic? ua0006 = diags.SingleOrDefault(d => d.Id == "UA0006");
            Assert.That(ua0006, Is.Not.Null);
            Assert.That(
                ua0006!.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("byte[]"));
        }

        [Test]
        public async Task DoesNotReportOnNewVariantIntAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static Variant M() => new Variant(42);
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0006ObsoleteVariantCtorAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0006"), Is.False);
        }

        [Test]
        public async Task DoesNotReportOnNewVariantStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static Variant M() => new Variant("hello");
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0006ObsoleteVariantCtorAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0006"), Is.False);
        }

        [Test]
        public async Task FixRewritesDateTimeCtorToVariantFromDateTimeUtcAsync()
        {
            const string source = """
                using System;
                using Opc.Ua;
                class C
                {
                    static Variant M() => new Variant(DateTime.UtcNow);
                }
                """;
            const string expected = """
                using System;
                using Opc.Ua;
                class C
                {
                    static Variant M() => Variant.From(new DateTimeUtc(DateTime.UtcNow));
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0006ObsoleteVariantCtorAnalyzer(),
                new UA0006ObsoleteVariantCtorCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }

        [Test]
        public async Task FixRewritesByteArrayCtorToVariantFromToByteStringAsync()
        {
            const string source = """
                using Opc.Ua;
                class C
                {
                    static Variant M(byte[] arr) => new Variant(arr);
                }
                """;
            const string expected = """
                using Opc.Ua;
                class C
                {
                    static Variant M(byte[] arr) => Variant.From(arr.ToByteString());
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0006ObsoleteVariantCtorAnalyzer(),
                new UA0006ObsoleteVariantCtorCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }
    }
}
