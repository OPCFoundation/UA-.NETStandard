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
    /// A NodeSet2 reference whose target is an instance of a model that is
    /// supplied by a referenced assembly (DI's DeviceSet organizing a device)
    /// was dropped: the dependency payload of a referenced assembly carries
    /// types only, so the importer could not resolve the target and skipped
    /// the reference without a diagnostic.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodesetExternalReferenceTargetTests
    {
        private const string DeviceSetReference =
            "state.AddReference(global::Opc.Ua.NodeId.Create(35u, global::Opc.Ua.Namespaces.OpcUa, " +
            "context.NamespaceUris), true, global::Opc.Ua.NodeId.Create(5001u, " +
            "global::DI.Namespaces.DI, context.NamespaceUris));";

        [Test]
        public void ReferenceToInstanceOfReferencedModelIsKept()
        {
            (string generated, ImmutableArray<Diagnostic> diagnostics) = GenerateAgainstReferencedDi();

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, diagnostics));
                Assert.That(
                    ExtractMethodBody(generated, "CreatePump1"),
                    Does.Contain(DeviceSetReference));
            });
        }

        [Test]
        public void ReferenceToInstanceOfAdditionalFileModelIsKept()
        {
            CSharpCompilation compilation = ModelDependencyScannerTests
                .CreateStackCompilation("DeviceSetPumpWithDi");
            GeneratorDriver driver = CreateDriver(
                EmbeddedText.From("Opc.Ua.Di.NodeSet2.xml"),
                EmbeddedText.From("DeviceSetPump.NodeSet2.xml"));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics);
            string generated = string.Join(
                Environment.NewLine,
                driver.GetRunResult().Results[0].GeneratedSources
                    .Where(s => s.HintName.StartsWith("DeviceSetPump.", StringComparison.Ordinal))
                    .Select(s => s.SourceText.ToString()));

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Concat(output.GetDiagnostics())
                        .Where(d => d.Severity == DiagnosticSeverity.Error),
                    Is.Empty);
                Assert.That(
                    ExtractMethodBody(generated, "CreatePump1"),
                    Does.Contain(DeviceSetReference));
            });
        }

        [Test]
        public void ExternalTargetsWithCollidingNamesKeepTheirOwnNodeIds()
        {
            CSharpCompilation consumer = ModelDependencyScannerTests
                .CreateStackCompilation("ExternalTargetCollisions")
                .AddReferences(s_diProducer.Value.ToMetadataReference());
            GeneratorDriver driver = CreateDriver(EmbeddedText.From("ExternalTargetCollisions.NodeSet2.xml"));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                consumer,
                out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics);
            string generated = string.Join(
                Environment.NewLine,
                driver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Concat(output.GetDiagnostics())
                        .Where(d => d.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, diagnostics.Concat(output.GetDiagnostics())));
                Assert.That(
                    ExtractMethodBody(generated, "CreateNumericTarget"),
                    Does.Contain(OrganizedBy("5001u")));
                Assert.That(
                    ExtractMethodBody(generated, "CreateStringTarget"),
                    Does.Contain(OrganizedBy("\"5001\"")));
                Assert.That(
                    ExtractMethodBody(generated, "CreateDottedTarget"),
                    Does.Contain(OrganizedBy("\"A.B\"")));
                Assert.That(
                    ExtractMethodBody(generated, "CreateSlashedTarget"),
                    Does.Contain(OrganizedBy("\"A/B\"")));
            });
        }

        [Test]
        public void ExternalTargetOfNamespaceListedOnlyInNamespaceUrisUsesUriLiteral()
        {
            CSharpCompilation consumer = ModelDependencyScannerTests
                .CreateStackCompilation("ExternalTargetNamespaceUriOnly")
                .AddReferences(s_diProducer.Value.ToMetadataReference());
            GeneratorDriver driver = CreateDriver(
                EmbeddedText.From("ExternalTargetNamespaceUriOnly.NodeSet2.xml"));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                consumer,
                out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics);
            string generated = string.Join(
                Environment.NewLine,
                driver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));
            IEnumerable<Diagnostic> all = diagnostics.Concat(output.GetDiagnostics());

            Assert.Multiple(() =>
            {
                Assert.That(
                    all.Where(d => d.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, all));
                Assert.That(
                    ExtractMethodBody(generated, "CreateUnlistedTarget"),
                    Does.Contain(
                        "state.AddReference(global::Opc.Ua.NodeId.Create(35u, global::Opc.Ua.Namespaces.OpcUa, " +
                        "context.NamespaceUris), true, global::Opc.Ua.NodeId.Create(5001u, " +
                        "\"http://opcfoundation.org/UA/DI/\", context.NamespaceUris));"));
            });
        }

        [Test]
        public void GeneratedReferenceToInstanceOfReferencedModelCompiles()
        {
            CSharpCompilation consumer = ModelDependencyScannerTests
                .CreateStackCompilation("DeviceSetPumpCompiles")
                .AddReferences(s_diProducer.Value.ToMetadataReference());
            GeneratorDriver driver = CreateDriver(EmbeddedText.From("DeviceSetPump.NodeSet2.xml"));
            driver.RunGeneratorsAndUpdateCompilation(
                consumer,
                out Compilation output,
                out _);

            Assert.That(
                output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, output.GetDiagnostics()));
        }

        private static (string Generated, ImmutableArray<Diagnostic> Diagnostics) GenerateAgainstReferencedDi()
        {
            CSharpCompilation consumer = ModelDependencyScannerTests
                .CreateStackCompilation("DeviceSetPump")
                .AddReferences(s_diProducer.Value.ToMetadataReference());
            GeneratorDriver driver = CreateDriver(EmbeddedText.From("DeviceSetPump.NodeSet2.xml"));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                consumer,
                out _,
                out ImmutableArray<Diagnostic> diagnostics);
            string generated = string.Join(
                Environment.NewLine,
                driver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));
            return (generated, diagnostics);
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
                .CreateStackCompilation("DeviceSetPumpDiProducer");
            GeneratorDriver driver = CreateDriver(EmbeddedText.From("Opc.Ua.Di.NodeSet2.xml"));
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation output,
                out ImmutableArray<Diagnostic> diagnostics);
            Assert.That(
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics));
            return (CSharpCompilation)output;
        }

        private static string OrganizedBy(string identifier)
        {
            return "state.AddReference(global::Opc.Ua.NodeId.Create(35u, global::Opc.Ua.Namespaces.OpcUa, " +
                "context.NamespaceUris), true, global::Opc.Ua.NodeId.Create(" + identifier + ", " +
                "global::DI.Namespaces.DI, context.NamespaceUris));";
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
