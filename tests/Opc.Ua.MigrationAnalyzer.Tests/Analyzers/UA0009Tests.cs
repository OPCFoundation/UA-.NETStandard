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
                .GetAnalyzerDiagnosticsAsync(new UA0009DataContractToDataTypeAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0009DataContractToDataTypeAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0009DataContractToDataTypeAnalyzer(), source)
                .ConfigureAwait(false);

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
                .GetAnalyzerDiagnosticsAsync(new UA0009DataContractToDataTypeAnalyzer(), source)
                .ConfigureAwait(false);

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
                source).ConfigureAwait(false);

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
                source).ConfigureAwait(false);

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
