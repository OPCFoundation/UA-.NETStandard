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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests for <see cref="ReferencedModelDependencyScanner"/> and the
    /// generator-side override resolution + transitive dependency
    /// suppression triggered by referenced assemblies.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ModelDependencyScannerTests
    {
        [Test]
        public void ScanReturnsEmptyWhenAttributeTypeNotFound()
        {
            CSharpCompilation compilation = CSharpCompilation.Create("Empty",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            ImmutableArray<ModelDependencyReference> result =
                ReferencedModelDependencyScanner.Scan(compilation);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ScanReturnsEmptyWhenNullCompilation()
        {
            ImmutableArray<ModelDependencyReference> result =
                ReferencedModelDependencyScanner.Scan(null);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ScanReadsAttributesFromReferencedCompilation()
        {
            CSharpCompilation producer = OptimizationLevel.Release.CreateCompilation("Producer")
                .AddCode(new Dictionary<string, string>
                {
                    ["AssemblyAttributes.cs"] =
                        "[assembly: global::Opc.Ua.ModelDependencyAttribute(" +
                        "\"urn:test:Producer\", \"TestProducer\", \"1.0\", \"2024-06-01\")]" +
                        Environment.NewLine +
                        "[assembly: global::Opc.Ua.ModelDependencyAttribute(" +
                        "\"urn:test:Upstream\", \"TestUpstream\", null, null)]"
                }, LanguageVersion.CSharp11);

            ImmutableArray<Diagnostic> diags = producer.GetDiagnostics();
            Assert.That(diags.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty, "Producer compilation must compile");

            CSharpCompilation consumer = OptimizationLevel.Release.CreateCompilation("Consumer")
                .AddReferences(producer.ToMetadataReference());

            ImmutableArray<ModelDependencyReference> result =
                ReferencedModelDependencyScanner.Scan(consumer);

            Assert.That(result, Has.Length.EqualTo(2));
            ModelDependencyReference producerEntry = result
                .Single(r => r.ModelUri == "urn:test:Producer");
            Assert.That(producerEntry.Prefix, Is.EqualTo("TestProducer"));
            Assert.That(producerEntry.Version, Is.EqualTo("1.0"));
            Assert.That(producerEntry.PublicationDate, Is.EqualTo("2024-06-01"));
            Assert.That(producerEntry.AssemblyName, Is.EqualTo("Producer"));
            Assert.That(producerEntry.IsValid, Is.True);

            ModelDependencyReference upstream = result
                .Single(r => r.ModelUri == "urn:test:Upstream");
            Assert.That(upstream.Prefix, Is.EqualTo("TestUpstream"));
            Assert.That(upstream.Version, Is.Empty);
            Assert.That(upstream.PublicationDate, Is.Empty);
        }

        [Test]
        public void ScanIgnoresAttributesWithEmptyUriOrPrefix()
        {
            CSharpCompilation producer = OptimizationLevel.Release.CreateCompilation("Producer")
                .AddCode(new Dictionary<string, string>
                {
                    ["AssemblyAttributes.cs"] =
                        "[assembly: global::Opc.Ua.ModelDependencyAttribute(" +
                        "\"\", \"NoUri\", null, null)]" + Environment.NewLine +
                        "[assembly: global::Opc.Ua.ModelDependencyAttribute(" +
                        "\"urn:test:NoPrefix\", \"\", null, null)]"
                }, LanguageVersion.CSharp11);

            CSharpCompilation consumer = OptimizationLevel.Release.CreateCompilation("Consumer")
                .AddReferences(producer.ToMetadataReference());

            ImmutableArray<ModelDependencyReference> result =
                ReferencedModelDependencyScanner.Scan(consumer);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void EmittedAssemblyContainsModelDependencyAttribute()
        {
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorVersion"] = "v105",
                    ["build_property.ModelSourceGeneratorExclude"] = "Draft"
                });

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([EmbeddedText.From("DemoModel.xml")])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out _,
                out _);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            GeneratorRunResult generatorResult = runResult.Results[0];

            GeneratedSourceResult dependencyFile = generatorResult.GeneratedSources
                .Single(s => s.HintName.EndsWith(
                    ".ModelDependencies.g.cs", System.StringComparison.Ordinal));
            string text = dependencyFile.SourceText.ToString();

            Assert.That(text, Does.StartWith("\uFEFF// <auto-generated />")
                .Or.StartWith("// <auto-generated />"));
            Assert.That(text, Does.Contain(
                "[assembly: global::Opc.Ua.ModelDependencyAttribute("));
            Assert.That(text, Does.Contain("\"urn:opcfoundation.org:2024-01:DemoModel\""));
            Assert.That(text, Does.Contain("\"DemoModel\""));
            // OpcUa namespace is implicit; must not be re-emitted as a dependency.
            Assert.That(text, Does.Not.Contain("\"http://opcfoundation.org/UA/\""));
        }

        [Test]
        public void OverrideResolutionSilentlySkipsLocalGeneration()
        {
            // Arrange: a producer assembly that already declares the DemoModel
            // model URI under the same prefix used by the local DemoModel.xml.
            CSharpCompilation producer = OptimizationLevel.Release.CreateCompilation("Producer")
                .AddCode(new Dictionary<string, string>
                {
                    ["AssemblyAttributes.cs"] =
                        "[assembly: global::Opc.Ua.ModelDependencyAttribute(" +
                        "\"urn:opcfoundation.org:2024-01:DemoModel\", \"DemoModel\", " +
                        "\"1.0\", \"2024-01-01\")]"
                }, LanguageVersion.CSharp11);

            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddReferences(producer.ToMetadataReference())
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorVersion"] = "v105",
                    ["build_property.ModelSourceGeneratorExclude"] = "Draft"
                });

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([EmbeddedText.From("DemoModel.xml")])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out _,
                out _);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            GeneratorRunResult generatorResult = runResult.Results[0];

            // No local sources should be generated for the overridden model
            // (no DataTypes / NodeIds / Constants / Schemas / etc.).
            Assert.That(generatorResult.GeneratedSources, Is.Empty,
                "Override resolution must silently skip local generation when " +
                "a referenced assembly already declares the same model URI " +
                "under the same C# prefix");
        }
    }
}
