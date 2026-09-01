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
    /// Regression tests for issue #4332 at the source generator driver
    /// level: a ModelDesign structure that subtypes a structure from
    /// another ModelDesign must be generatable both when the upstream
    /// design is a sibling AdditionalFile and when it is supplied by a
    /// referenced assembly's [assembly: ModelDependency] payload.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CrossModelStructureSubtypeGeneratorTests
    {
        /// <summary>
        /// Both designs are AdditionalFiles of the same compilation (the
        /// minimal repro of #4332). Both orderings used to fail: the
        /// derived-first order with a NullReferenceException in the binary
        /// schema generator, the base-first order with "The BaseType
        /// reference ... is not the expected type" while loading the
        /// downstream design as a dependency of the upstream target.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void StructureSubtypeAcrossModelDesignAdditionalFiles(bool derivedModelFirst)
        {
            var generator = new ModelSourceGenerator();

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                    ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
                });

            AdditionalText modelA = EmbeddedText.Create("A/ModelA.xml", ModelADesign);
            AdditionalText modelB = EmbeddedText.Create("B/ModelB.xml", ModelBDesign);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts(
                    derivedModelFirst ? [modelB, modelA] : [modelA, modelB])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(
                diagnostics,
                Is.Empty,
                string.Join("\n", diagnostics.Select(d => d.ToString())));

            string generated = string.Join(
                "\n",
                driver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));
            Assert.That(generated, Does.Contain("class BaseStruct"));
            Assert.That(generated, Does.Contain(
                "class DerivedStruct : global::Test.ModelA.BaseStruct"));

            outputCompilation.GetDiagnostics().Check(
                TestContext.Out,
                out int errors,
                out _);
            Assert.That(errors, Is.Zero, $"Compilation produced {errors} errors");
        }

        /// <summary>
        /// The cross-assembly flow of docs/ModelDependencies.md: the
        /// upstream design is compiled into a referenced assembly whose
        /// [assembly: ModelDependency] payload supplies its types; the
        /// consumer compilation only carries the downstream design. The
        /// design-file pass used to ignore referenced payloads entirely,
        /// failing with "The BaseType reference for node DerivedStruct is
        /// not the expected type: DataTypeDesign".
        /// </summary>
        [Test]
        public void StructureSubtypeAcrossReferencedAssemblyPayload()
        {
            var generator = new ModelSourceGenerator();

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                    ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
                });
            var parseOptions = new CSharpParseOptions()
                .WithKind(SourceCodeKind.Regular)
                .WithLanguageVersion(LanguageVersion.CSharp11);

            // Producer: generate the upstream model into its own assembly,
            // which carries the ModelDependency payload for ModelA.
            CSharpCompilation producer = OptimizationLevel.Release
                .CreateCompilation("ModelAProducer")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11);

            GeneratorDriver producerDriver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(parseOptions)
                .AddAdditionalTexts([EmbeddedText.Create("A/ModelA.xml", ModelADesign)])
                .WithUpdatedAnalyzerConfigOptions(options);

            producerDriver.RunGeneratorsAndUpdateCompilation(
                producer,
                out Compilation producerOutput,
                out ImmutableArray<Diagnostic> producerDiagnostics);

            Assert.That(
                producerDiagnostics,
                Is.Empty,
                string.Join("\n", producerDiagnostics.Select(d => d.ToString())));
            Assert.That(
                producerOutput.SyntaxTrees
                    .Select(t => t.ToString())
                    .Any(t => t.Contains("ModelDependencyAttribute", StringComparison.Ordinal)),
                Is.True,
                "Producer did not emit the model dependency metadata.");

            // Consumer: only the downstream design file plus a reference to
            // the producer assembly.
            CSharpCompilation consumer = OptimizationLevel.Release
                .CreateCompilation("ModelBConsumer")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11)
                .AddReferences(((CSharpCompilation)producerOutput).ToMetadataReference());

            GeneratorDriver consumerDriver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(parseOptions)
                .AddAdditionalTexts([EmbeddedText.Create("B/ModelB.xml", ModelBDesign)])
                .WithUpdatedAnalyzerConfigOptions(options);

            consumerDriver = consumerDriver.RunGeneratorsAndUpdateCompilation(
                consumer,
                out _,
                out ImmutableArray<Diagnostic> consumerDiagnostics);

            Assert.That(
                consumerDiagnostics,
                Is.Empty,
                string.Join("\n", consumerDiagnostics.Select(d => d.ToString())));

            // The generated code is not compiled here: the consumer
            // compilation carries its own generated-stack stubs, which
            // would be ambiguous with the producer reference's stubs.
            string generated = string.Join(
                "\n",
                consumerDriver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));
            Assert.That(generated, Does.Contain(
                "class DerivedStruct : global::Test.ModelA.BaseStruct"));
            Assert.That(
                generated,
                Does.Not.Contain("class BaseStruct"),
                "The upstream type is supplied by the referenced assembly " +
                "and must not be re-emitted.");
        }

        private const string ModelADesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/ModelA/"
              TargetNamespace="http://test.org/UA/ModelA/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="ModelA" Prefix="Test.ModelA">http://test.org/UA/ModelA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="BaseStruct" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Make" DataType="ua:String" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private const string ModelBDesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:s0="http://test.org/UA/ModelA/"
              xmlns="http://test.org/UA/ModelB/"
              TargetNamespace="http://test.org/UA/ModelB/">
              <opc:Namespaces>
                <opc:Namespace Name="ModelB" Prefix="Test.ModelB">http://test.org/UA/ModelB/</opc:Namespace>
                <opc:Namespace Name="ModelA" Prefix="Test.ModelA">http://test.org/UA/ModelA/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="DerivedStruct" BaseType="s0:BaseStruct">
                <opc:Fields>
                  <opc:Field Name="Extra" DataType="ua:UInt32" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;
    }
}
