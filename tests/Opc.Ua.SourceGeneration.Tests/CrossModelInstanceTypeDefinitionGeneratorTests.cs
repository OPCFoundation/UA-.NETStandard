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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Regression tests for issue #4353 at the source generator driver
    /// level: an Object whose TypeDefinition is an ObjectType with
    /// children declared in a sibling AdditionalFile ModelDesign used to
    /// fail with MODELGEN003 / NullReferenceException in
    /// NodeStateGenerator while building the node state factories for
    /// the inherited children.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CrossModelInstanceTypeDefinitionGeneratorTests
    {
        /// <summary>
        /// Both designs are AdditionalFiles of the same compilation (the
        /// minimal repro of #4353) in both orderings. The generated
        /// instance factories reference the typed state classes emitted
        /// for the dependency model, so the combined output must compile.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void InstanceOfObjectTypeAcrossModelDesignAdditionalFiles(bool instanceModelFirst)
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
                    instanceModelFirst ? [modelB, modelA] : [modelA, modelB])
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
            Assert.That(generated, Does.Contain("CreateWidget1("),
                "The instance factory must be emitted.");
            Assert.That(generated, Does.Contain("CreateWidget1_Label("),
                "The factory for the property inherited from the " +
                "dependency type must be emitted.");
            Assert.That(generated, Does.Contain("global::Test.ModelA.WidgetState"),
                "The instance must use the typed state class generated " +
                "for the dependency ObjectType.");

            outputCompilation.GetDiagnostics().Check(
                TestContext.Out,
                out int errors,
                out _);
            Assert.That(errors, Is.Zero, $"Compilation produced {errors} errors");
        }

        /// <summary>
        /// The upstream model is available only through the generated
        /// ModelDependencyV1 metadata on a referenced assembly. Variable
        /// metadata and defaults must be identical to the AdditionalFiles path.
        /// </summary>
        [Test]
        public void InstanceMetadataAcrossReferencedAssemblyPayload()
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

            string generated = string.Join(
                "\n",
                consumerDriver.GetRunResult().Results[0].GeneratedSources
                    .Select(s => s.SourceText.ToString()));
            Assert.Multiple(() =>
            {
                Assert.That(generated, Does.Contain("CreateWidget1_Label("));
                Assert.That(generated, Does.Contain("CreateWidget1_OwnedLabels("));
                Assert.That(
                    generated,
                    Does.Contain(
                        "state.AccessLevel = global::Opc.Ua.AccessLevels.CurrentReadOrWrite;"));
                Assert.That(
                    generated,
                    Does.Contain(
                        "state.UserAccessLevel = global::Opc.Ua.AccessLevels.CurrentReadOrWrite;"));
                Assert.That(
                    generated,
                    Does.Contain("state.MinimumSamplingInterval = 250;"));
                Assert.That(generated, Does.Contain("state.Historizing = true;"));
                Assert.That(generated, Does.Contain("Unlabeled"));
                Assert.That(generated, Does.Contain("First"));
                Assert.That(generated, Does.Contain("Second"));
            });
        }

        private const string ModelADesign =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
              xmlns="http://test.org/UA/ModelA/"
              TargetNamespace="http://test.org/UA/ModelA/">
              <opc:Namespaces>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                <opc:Namespace Name="ModelA" Prefix="Test.ModelA">http://test.org/UA/ModelA/</opc:Namespace>
              </opc:Namespaces>
              <opc:ObjectType SymbolicName="WidgetType" BaseType="ua:BaseObjectType">
                <opc:Children>
                  <opc:Property SymbolicName="Label" DataType="ua:String" ValueRank="Scalar"
                    AccessLevel="ReadWrite" MinimumSamplingInterval="250" Historizing="true">
                    <opc:DefaultValue>
                      <uax:String>Unlabeled</uax:String>
                    </opc:DefaultValue>
                  </opc:Property>
                  <opc:Variable SymbolicName="OwnedLabels" DataType="ua:String" ValueRank="Array"
                    AccessLevel="ReadWrite">
                    <opc:DefaultValue>
                      <uax:ListOfString>
                        <uax:String>First</uax:String>
                        <uax:String>Second</uax:String>
                      </uax:ListOfString>
                    </opc:DefaultValue>
                  </opc:Variable>
                </opc:Children>
              </opc:ObjectType>
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
              <opc:Object SymbolicName="Widget1" TypeDefinition="s0:WidgetType">
                <opc:References>
                  <opc:Reference IsInverse="true">
                    <opc:ReferenceType>ua:Organizes</opc:ReferenceType>
                    <opc:TargetId>ua:ObjectsFolder</opc:TargetId>
                  </opc:Reference>
                </opc:References>
              </opc:Object>
            </opc:ModelDesign>
            """;
    }
}
