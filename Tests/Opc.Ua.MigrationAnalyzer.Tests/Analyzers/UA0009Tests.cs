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
using Opc.Ua.MigrationAnalyzer.Analyzers;
using Opc.Ua.MigrationAnalyzer.CodeFixes;

namespace Opc.Ua.MigrationAnalyzer.Tests.Analyzers
{
    /// <summary>
    /// Tests for UA0009 ([DataContract]/[DataMember] -> [DataType]/[DataTypeField]).
    /// </summary>
    [TestFixture]
    public class UA0009Tests
    {
        [Test]
        public async Task ReportsOnDataContractClassWithDataMemberPropertyAsync()
        {
            const string source = """
                using System.Runtime.Serialization;
                namespace Test
                {
                    [DataContract]
                    public class Foo
                    {
                        [DataMember]
                        public int X { get; set; }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0009DataContractToDataTypeAnalyzer(), source);

            Diagnostic? ua0009 = diags.SingleOrDefault(d => d.Id == "UA0009");
            Assert.That(ua0009, Is.Not.Null, "Expected UA0009 on [DataContract] + [DataMember] class.");
            Assert.That(
                ua0009!.GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Foo"));
        }

        [Test]
        public async Task ReportsOnDataContractClassWithoutParseExtensionUseAsync()
        {
            // The simplified detection flags any candidate class regardless of whether
            // ApplicationConfiguration.ParseExtension/UpdateExtension is invoked in the
            // same compilation. This test pins that behaviour.
            const string source = """
                using System.Runtime.Serialization;
                namespace Test
                {
                    [DataContract(Name = "Foo")]
                    public class Foo
                    {
                        [DataMember(Order = 1)]
                        public int X { get; set; }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0009DataContractToDataTypeAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0009"), Is.True,
                "Expected UA0009 even when no ParseExtension call is present.");
        }

        [Test]
        public async Task DoesNotReportWhenDataMemberIsOnFieldOnlyAsync()
        {
            const string source = """
                using System.Runtime.Serialization;
                namespace Test
                {
                    [DataContract]
                    public class Foo
                    {
                        [DataMember]
                        public int X;
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0009DataContractToDataTypeAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0009"), Is.False,
                "Field-only [DataMember] must not trigger UA0009 under simplified detection.");
        }

        [Test]
        public async Task DoesNotReportWhenNoDataMemberPresentAsync()
        {
            const string source = """
                using System.Runtime.Serialization;
                namespace Test
                {
                    [DataContract]
                    public class Foo
                    {
                        public int X { get; set; }
                    }
                }
                """;

            ImmutableArray<Diagnostic> diags = await AnalyzerHarness
                .GetAnalyzerDiagnosticsAsync(new UA0009DataContractToDataTypeAnalyzer(), source);

            Assert.That(diags.Any(d => d.Id == "UA0009"), Is.False,
                "[DataContract] alone (no [DataMember]) must not trigger UA0009.");
        }

        [Test]
        public async Task FixReplacesDataContractAndDataMemberAttributesAsync()
        {
            const string source = """
                using System.Runtime.Serialization;
                using Opc.Ua;
                namespace Test
                {
                    [DataContract]
                    public partial class Foo
                    {
                        [DataMember]
                        public int X { get; set; }
                    }
                }
                """;
            const string expected = """
                using System.Runtime.Serialization;
                using Opc.Ua;
                namespace Test
                {
                    [DataType]
                    public partial class Foo
                    {
                        [DataTypeField]
                        public int X { get; set; }
                    }
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0009DataContractToDataTypeAnalyzer(),
                new UA0009DataContractToDataTypeCodeFix(),
                source);

            Assert.That(fixedSource, Is.EqualTo(expected));
        }

        [Test]
        public async Task FixAddsPartialModifierAndUsingOpcUaWhenMissingAsync()
        {
            const string source = """
                using System.Runtime.Serialization;
                namespace Test
                {
                    [DataContract]
                    public class Foo
                    {
                        [DataMember]
                        public int X { get; set; }
                    }
                }
                """;

            string fixedSource = await AnalyzerHarness.ApplyFixAsync(
                new UA0009DataContractToDataTypeAnalyzer(),
                new UA0009DataContractToDataTypeCodeFix(),
                source);

            Assert.That(fixedSource, Does.Contain("using Opc.Ua;"),
                "Fix must add 'using Opc.Ua;' when missing.");
            Assert.That(fixedSource, Does.Contain("partial class Foo"),
                "Fix must add the 'partial' modifier to the class.");
            Assert.That(fixedSource, Does.Contain("[DataType]"),
                "Fix must rewrite [DataContract] to [DataType].");
            Assert.That(fixedSource, Does.Contain("[DataTypeField]"),
                "Fix must rewrite [DataMember] to [DataTypeField].");
        }
    }
}
