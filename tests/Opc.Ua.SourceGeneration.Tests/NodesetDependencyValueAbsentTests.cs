/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// A Variable that a NodeSet2 declares without a Value has a null Value.
    /// An instance inherits such a child from its type; when the type's model
    /// is supplied by a referenced assembly, the child comes from the
    /// dependency payload, which did not carry that the Value is absent, so
    /// the inherited child fell back to the default value of its DataType.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodesetDependencyValueAbsentTests
    {
        private const string NullValue = "baseState.WrappedValue = global::Opc.Ua.Variant.Null;";

        [Test]
        public void InheritedChildWithoutValueIsNullWhenTheTypeModelIsAnAdditionalFile()
        {
            string generated = Generate(
                ModelDependencyScannerTests.CreateStackCompilation("InheritedValueAbsentWithDi"),
                EmbeddedText.From("Opc.Ua.Di.NodeSet2.xml"),
                EmbeddedText.From("InheritedValueAbsent.NodeSet2.xml"));

            AssertRevisionCounterIsNull(generated);
        }

        [Test]
        public void InheritedChildWithoutValueIsNullWhenTheTypeModelIsAReferencedAssembly()
        {
            string generated = Generate(
                ModelDependencyScannerTests
                    .CreateStackCompilation("InheritedValueAbsent")
                    .AddReferences(s_diProducer.Value.ToMetadataReference()),
                EmbeddedText.From("InheritedValueAbsent.NodeSet2.xml"));

            AssertRevisionCounterIsNull(generated);
        }

        private static void AssertRevisionCounterIsNull(string generated)
        {
            string body = ExtractMethodBody(generated, "CreateInheritingPump_RevisionCounter");

            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain(NullValue));
                Assert.That(body, Does.Not.Contain("(int)0"));
            });
        }

        private static string Generate(CSharpCompilation compilation, params AdditionalText[] texts)
        {
            GeneratorDriver driver = CreateDriver(texts).RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(
                diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics.Concat(output.GetDiagnostics())));
            return string.Join(
                Environment.NewLine,
                driver.GetRunResult().Results[0].GeneratedSources
                    .Where(s => s.HintName.StartsWith("InheritedValueAbsent.", StringComparison.Ordinal))
                    .Select(s => s.SourceText.ToString()));
        }

        private static GeneratorDriver CreateDriver(params AdditionalText[] texts)
        {
            return CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([.. texts])
                .WithUpdatedAnalyzerConfigOptions(new AnalyzerOptionsProvider(
                    new Dictionary<string, string>
                    {
                        ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                    }));
        }

        private static CSharpCompilation CreateDiProducer()
        {
            CSharpCompilation compilation = ModelDependencyScannerTests
                .CreateStackCompilation("InheritedValueAbsentDiProducer");
            CreateDriver(EmbeddedText.From("Opc.Ua.Di.NodeSet2.xml")).RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics));
            return (CSharpCompilation)output;
        }

        private static string ExtractMethodBody(string code, string methodName)
        {
            Match match = Regex.Match(
                code,
                @"\s" + Regex.Escape(methodName) + @"\(\s*this\s");
            Assert.That(match.Success, Is.True, $"Method {methodName} was not generated.");
            int end = code.IndexOf("return state;", match.Index, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(match.Index));
            return code[match.Index..end];
        }

        private static readonly Lazy<CSharpCompilation> s_diProducer = new(CreateDiProducer);
    }
}
