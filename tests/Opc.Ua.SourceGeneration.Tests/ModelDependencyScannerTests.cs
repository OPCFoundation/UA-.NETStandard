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
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using Opc.Ua.SourceGeneration.Dependency;

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
            var compilation = CSharpCompilation.Create("Empty",
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
                        "\"\", \"NoUri\", null, null)]" +
                        Environment.NewLine +
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

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp11);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorVersion"] = "v105",
                    ["build_property.ModelSourceGeneratorExclude"] = "Draft"
                });

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
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
                    ".ModelDependencies.g.cs", StringComparison.Ordinal));
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
        public void DeclarationBackedMethodIdentityCompilesAcrossAssemblies()
        {
            CSharpCompilation producer = GenerateModelCompilation(
                "DeclarationBackedMethodProducer",
                "DeclarationBackedMethod.NodeSet2.xml",
                "DeclarationBackedMethod");
            CSharpCompilation consumerBase =
                CreateStackCompilation("DeclarationBackedMethodConsumer")
                    .AddReferences(producer.ToMetadataReference());

            (GeneratorRunResult result, Compilation output, ImmutableArray<Diagnostic> diagnostics) = RunModelGenerator(
                consumerBase,
                "DeclarationBackedMethodConsumer.NodeSet2.xml",
                "DeclarationBackedMethodConsumer");

            ImmutableArray<Diagnostic> outputDiagnostics = output.GetDiagnostics();
            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Where(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, diagnostics));
                Assert.That(
                    outputDiagnostics.Where(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, outputDiagnostics));
            });

            string generated = string.Join(
                Environment.NewLine,
                result.GeneratedSources.Select(source => source.SourceText.ToString()));
            Assert.Multiple(() =>
            {
                Assert.That(
                    generated,
                    Does.Contain(
                        "global::DeclarationBackedMethod.AdjustDeclarationMethodState"));
                Assert.That(
                    generated,
                    Does.Not.Contain("global::DeclarationBackedMethod.AdjustMethodState"));
            });
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

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
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

        [Test]
        public void FluentAccessorsOnlyEmitsAccessorsAgainstReferencedStateTypes()
        {
            CSharpCompilation producer = s_producerWithoutAccessors.Value;
            (GeneratorRunResult generatorResult, Compilation outputCompilation,
                ImmutableArray<Diagnostic> generatorDiagnostics) =
                RunFluentAccessorsOnly(producer);

            Assert.That(
                generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                "Fluent-accessors-only generation must not report errors.");
            Assert.That(generatorResult.GeneratedSources, Has.Length.EqualTo(1));

            GeneratedSourceResult source = generatorResult.GeneratedSources.Single();
            string text = source.SourceText.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(source.HintName, Does.EndWith(".FluentBuilders.g.cs"));
                Assert.That(text, Does.Contain(
                    "public static partial class RestrictedObjectStateComponents"));
                Assert.That(text, Does.Contain(
                    "global::DemoModel.RestrictedObjectState"));
                Assert.That(text, Does.Not.Match(
                    @"internal\s+interface\s+I\w+NodeManagerBuilder"));
                Assert.That(text, Does.Not.Match(
                    @"public\s+partial\s+class\s+RestrictedObjectState\b"));
                Assert.That(generatorResult.GeneratedSources.Select(s => s.HintName),
                    Has.None.EndsWith(".Constants.g.cs"));
                Assert.That(generatorResult.GeneratedSources.Select(s => s.HintName),
                    Has.None.EndsWith(".NodeStates.g.cs"));
                Assert.That(generatorResult.GeneratedSources.Select(s => s.HintName),
                    Has.None.EndsWith(".ModelDependencies.g.cs"));
            });

            ImmutableArray<Diagnostic> outputDiagnostics = outputCompilation.GetDiagnostics();
            Assert.That(
                outputDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, outputDiagnostics));
        }

        [Test]
        public void FluentAccessorsOnlyRejectsProducerThatAlreadyEmittedAccessors()
        {
            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) =
                RunFluentAccessorsOnly(s_producerWithAccessors.Value);

            Diagnostic diagnostic = diagnostics.Single(d => d.Id == "MODELGEN014");
            string message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            Assert.Multiple(() =>
            {
                Assert.That(result.GeneratedSources, Is.Empty);
                Assert.That(message, Does.Contain(DemoModelUri));
                Assert.That(message, Does.Contain("DemoModel"));
                Assert.That(message,
                    Does.Contain("already provides generated fluent accessors"));
            });
        }

        [Test]
        public void FluentAccessorsOnlyProviderPreventsDownstreamSecondEmission()
        {
            (GeneratorRunResult firstResult, Compilation firstCompilation,
                ImmutableArray<Diagnostic> firstDiagnostics) = RunFluentAccessorsOnly(
                    s_producerWithoutAccessors.Value,
                    assemblyName: "AccessorProvider");
            Assert.That(
                firstDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(firstResult.GeneratedSources, Has.Length.EqualTo(1));

            MetadataReference accessorProvider = firstCompilation.ToMetadataReference();
            CSharpCompilation probe = CreateStackCompilation("AccessorProviderProbe")
                .AddReferences(accessorProvider);
            ModelFluentAccessorProviderReference provider =
                ReferencedFluentAccessorProviderScanner.Scan(probe)
                    .Single(reference => reference.ModelUri == DemoModelUri);
            Assert.That(provider.Prefix, Is.EqualTo("DemoModel"));

            (GeneratorRunResult secondResult, _, ImmutableArray<Diagnostic> secondDiagnostics) = RunFluentAccessorsOnly(
                s_producerWithoutAccessors.Value,
                additionalReferences: [accessorProvider],
                assemblyName: "DownstreamConsumer");

            Diagnostic diagnostic = secondDiagnostics.Single(d => d.Id == "MODELGEN014");
            Assert.Multiple(() =>
            {
                Assert.That(secondResult.GeneratedSources, Is.Empty);
                Assert.That(
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    Does.Contain("already provides generated fluent accessors"));
            });
        }

        [Test]
        public void VendorRoboticsSubtypeExtensionCompilesAgainstReferencedModelAndAccessorProvider()
        {
            CSharpCompilation roboticsModel = CreateGeneratedRoboticsModelProducer();
            CSharpCompilation roboticsAccessorProvider =
                CreateGeneratedRoboticsAccessorProvider(roboticsModel);

            CSharpCompilation vendorBase = CreateStackCompilation("VendorRobotics")
                .AddReferences(
                    s_diProducer.Value.ToMetadataReference(),
                    roboticsModel.ToMetadataReference(),
                    roboticsAccessorProvider.ToMetadataReference());

            (GeneratorRunResult result, Compilation outputCompilation,
                ImmutableArray<Diagnostic> diagnostics) = RunVendorRoboticsGenerator(vendorBase);

            ImmutableArray<Diagnostic> outputDiagnostics = outputCompilation.GetDiagnostics();
            string generated = string.Join(
                Environment.NewLine,
                result.GeneratedSources.Select(source => source.SourceText.ToString()));

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, diagnostics));
                Assert.That(
                    outputDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, outputDiagnostics));
                Assert.That(
                    generated,
                    Does.Match(
                        @"public\s+partial\s+class\s+VendorMotionDeviceState\s*:\s*" +
                        @"global::Opc\.Ua\.Robotics\.MotionDeviceState"));
                Assert.That(
                    generated,
                    Does.Match(
                        @"public\s+partial\s+class\s+VendorAxisState\s*:\s*" +
                        @"global::Opc\.Ua\.Robotics\.AxisState"));
                Assert.That(generated, Does.Contain("namespace Vendor.Robotics"));
                Assert.That(generated, Does.Contain("public static partial class ObjectTypeIds"));
                Assert.That(generated, Does.Contain("VendorMotionDeviceType"));
                Assert.That(generated, Does.Contain("VendorAxisType"));
                Assert.That(
                    generated,
                    Does.Match(@"public\s+static\s+partial\s+class\s+VendorMotionDeviceStateComponents\b"));
                Assert.That(
                    generated,
                    Does.Match(@"public\s+static\s+partial\s+class\s+VendorAxisStateComponents\b"));
                Assert.That(generated, Does.Not.Match(@"public\s+partial\s+class\s+MotionDeviceState\b"));
                Assert.That(generated, Does.Not.Match(@"public\s+partial\s+class\s+AxisState\b"));
                Assert.That(
                    generated,
                    Does.Not.Match(@"public\s+static\s+partial\s+class\s+MotionDeviceStateComponents\b"));
                Assert.That(
                    generated,
                    Does.Not.Match(@"public\s+static\s+partial\s+class\s+AxisStateComponents\b"));
            });
        }

        [Test]
        public void PayloadlessTransitiveReexportDoesNotWinOrRejectSelfProducer()
        {
            CSharpCompilation reexport = CreateModelMetadataAssembly(
                "TransitiveReexport",
                prefix: "DemoModel.Transitive",
                version: "999.0",
                payload: null);

            (GeneratorRunResult normalResult, _, ImmutableArray<Diagnostic> normalDiagnostics) = RunFluentAccessorsOnly(
                s_producerWithoutAccessors.Value,
                additionalReferences: [reexport.ToMetadataReference()],
                fluentAccessorsOnly: false,
                assemblyName: "NormalConsumer");
            Assert.Multiple(() =>
            {
                Assert.That(
                    normalDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                    Is.Empty);
                Assert.That(normalResult.GeneratedSources, Is.Empty,
                    "Normal duplicate suppression should select the payload-bearing producer.");
            });

            (GeneratorRunResult accessorResult, _, ImmutableArray<Diagnostic> accessorDiagnostics) =
                RunFluentAccessorsOnly(
                    s_producerWithoutAccessors.Value,
                    additionalReferences: [reexport.ToMetadataReference()],
                    assemblyName: "AccessorConsumer");
            Assert.Multiple(() =>
            {
                Assert.That(
                    accessorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                    Is.Empty);
                Assert.That(accessorResult.GeneratedSources, Has.Length.EqualTo(1));
            });
        }

        [Test]
        public void CorruptCandidateDoesNotAbortNormalGeneration()
        {
            string validPayload = new ModelDependencyV1
            {
                ModelUri = DemoModelUri,
                FluentAccessorsEmitted = false
            }.ToBase64Payload();
            CSharpCompilation validProducer = CreateModelMetadataAssembly(
                "ValidProducer",
                prefix: "DemoModel",
                version: "1.05.03",
                payload: validPayload);
            CSharpCompilation corruptCandidate = CreateModelMetadataAssembly(
                "CorruptCandidate",
                prefix: "DemoModel",
                version: "0.0",
                payload: Convert.ToBase64String([0xAA, 0xC7, 0x01, 0x01, 0xFF]));

            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) = RunFluentAccessorsOnly(
                validProducer,
                additionalReferences: [corruptCandidate.ToMetadataReference()],
                fluentAccessorsOnly: false,
                assemblyName: "NormalConsumer");

            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                    Is.Empty);
                Assert.That(result.GeneratedSources, Is.Empty,
                    "The valid payload-bearing producer should suppress duplicate generation.");
            });
        }

        [Test]
        public void FluentAccessorsOnlyRejectsCorruptSamePrefixProvider()
        {
            CSharpCompilation corruptProvider = CreateModelMetadataAssembly(
                "CorruptProvider",
                prefix: "DemoModel",
                version: "0.0",
                payload: "not!valid!base64!@@");

            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) = RunFluentAccessorsOnly(
                s_producerWithoutAccessors.Value,
                additionalReferences: [corruptProvider.ToMetadataReference()],
                assemblyName: "ConservativeConsumer");

            Diagnostic diagnostic = diagnostics.Single(d => d.Id == "MODELGEN014");
            Assert.Multiple(() =>
            {
                Assert.That(result.GeneratedSources, Is.Empty);
                Assert.That(
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    Does.Contain("malformed model dependency payload"));
            });
        }

        [Test]
        public void MultipleActualProducersRejectUnknownAccessorCapability()
        {
            string unknownCapabilityPayload = new ModelDependencyV1
            {
                ModelUri = DemoModelUri
            }.ToBase64Payload();
            CSharpCompilation unknownCapabilityProducer = CreateModelMetadataAssembly(
                "UnknownCapabilityProducer",
                prefix: "DemoModel",
                version: "999.0",
                payload: unknownCapabilityPayload);

            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) = RunFluentAccessorsOnly(
                s_producerWithoutAccessors.Value,
                additionalReferences: [unknownCapabilityProducer.ToMetadataReference()],
                assemblyName: "ConservativeConsumer");

            Diagnostic diagnostic = diagnostics.Single(d => d.Id == "MODELGEN014");
            Assert.Multiple(() =>
            {
                Assert.That(result.GeneratedSources, Is.Empty);
                Assert.That(
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    Does.Contain("unknown legacy fluent-accessor capability"));
            });
        }

        [Test]
        public void FluentAccessorsOnlyRejectsMissingReferencedModel()
        {
            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) =
                RunFluentAccessorsOnly(producer: null, includeDiNodeSet: true);

            Diagnostic[] modelDiagnostics =
                [.. diagnostics.Where(d => d.Id == "MODELGEN014")];
            Diagnostic diagnostic = modelDiagnostics
                .First(d => d.Id == "MODELGEN014" &&
                    d.GetMessage(CultureInfo.InvariantCulture).Contains(
                        DemoModelUri,
                        StringComparison.Ordinal));
            string message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            Assert.Multiple(() =>
            {
                Assert.That(result.GeneratedSources, Is.Empty);
                Assert.That(modelDiagnostics, Has.Length.EqualTo(2),
                    "Both invalid local models should be diagnosed.");
                Assert.That(message,
                    Does.Contain("no payload-bearing referenced model producer"));
                Assert.That(message, Does.Contain("DemoModel.NodeSet2.xml"));
            });
        }

        [Test]
        public void FluentAccessorsOnlyRejectsMismatchedReferencedPrefix()
        {
            const string consumerPrefix = "DemoModel.Consumer";
            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) = RunFluentAccessorsOnly(
                s_producerWithoutAccessors.Value,
                consumerPrefix: consumerPrefix);

            Diagnostic diagnostic = diagnostics.Single(d => d.Id == "MODELGEN014");
            string message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            Assert.Multiple(() =>
            {
                Assert.That(result.GeneratedSources, Is.Empty);
                Assert.That(message, Does.Contain(consumerPrefix));
                Assert.That(message,
                    Does.Contain("supplies prefix 'DemoModel'"));
            });
        }

        [TestCase("ModelSourceGeneratorOmitFluentApi")]
        [TestCase("ModelSourceGeneratorGenerateNodeManager")]
        public void FluentAccessorsOnlyRejectsInvalidOptionCombinations(string incompatibleOption)
        {
            var optionOverrides = new Dictionary<string, string>
            {
                ["build_property." + incompatibleOption] = "true"
            };

            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) = RunFluentAccessorsOnly(
                s_producerWithoutAccessors.Value,
                optionOverrides: optionOverrides);

            Diagnostic diagnostic = diagnostics.Single(d => d.Id == "MODELGEN015");
            string message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            Assert.Multiple(() =>
            {
                Assert.That(result.GeneratedSources, Is.Empty);
                Assert.That(message, Does.Contain(incompatibleOption));
            });
        }

        private static (GeneratorRunResult Result, Compilation OutputCompilation,
            ImmutableArray<Diagnostic> Diagnostics) RunFluentAccessorsOnly(
                CSharpCompilation producer,
                string consumerPrefix = null,
                bool includeDiNodeSet = false,
                IReadOnlyDictionary<string, string> optionOverrides = null,
                IReadOnlyList<MetadataReference> additionalReferences = null,
                bool fluentAccessorsOnly = true,
                string assemblyName = "Consumer")
        {
            CSharpCompilation compilation = CreateStackCompilation(assemblyName);
            if (producer != null)
            {
                compilation = compilation.AddReferences(
                    s_diProducer.Value.ToMetadataReference(),
                    producer.ToMetadataReference());
            }
            if (additionalReferences != null)
            {
                compilation = compilation.AddReferences(additionalReferences);
            }

            var globalOptions = new Dictionary<string, string>
            {
                ["build_property.ModelSourceGeneratorVersion"] = "v105",
                ["build_property.ModelSourceGeneratorExclude"] = "Draft"
            };
            if (fluentAccessorsOnly)
            {
                globalOptions["build_property.ModelSourceGeneratorFluentAccessorsOnly"] = "true";
            }
            if (optionOverrides != null)
            {
                foreach (KeyValuePair<string, string> option in optionOverrides)
                {
                    globalOptions[option.Key] = option.Value;
                }
            }
            var options = new AnalyzerOptionsProvider(globalOptions);
            if (!string.IsNullOrEmpty(consumerPrefix))
            {
                options.TextOptions["DemoModel.NodeSet2.xml"] = new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = consumerPrefix
                };
            }

            var additionalTexts = new List<AdditionalText>
            {
                EmbeddedText.From("DemoModel.NodeSet2.xml")
            };
            if (includeDiNodeSet)
            {
                additionalTexts.Add(EmbeddedText.From("Opc.Ua.Di.NodeSet2.xml"));
            }

            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([.. additionalTexts])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            return (driver.GetRunResult().Results[0], outputCompilation, diagnostics);
        }

        private static (GeneratorRunResult Result, Compilation OutputCompilation,
            ImmutableArray<Diagnostic> Diagnostics) RunVendorRoboticsGenerator(
                CSharpCompilation compilation)
        {
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorVersion"] = "v105",
                    ["build_property.ModelSourceGeneratorStartId"] = "9000",
                    ["build_property.ModelSourceGeneratorUseAllowSubtypes"] = "true"
                });
            options.TextOptions["VendorRobotics.NodeSet2.xml"] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "Vendor.Robotics"
            };

            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([EmbeddedText.From("VendorRobotics.NodeSet2.xml")])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            return (driver.GetRunResult().Results[0], outputCompilation, diagnostics);
        }

        private static CSharpCompilation GenerateModelCompilation(
            string assemblyName,
            string nodeSetResource,
            string prefix)
        {
            CSharpCompilation compilation = CreateStackCompilation(assemblyName);
            (GeneratorRunResult _, Compilation output, ImmutableArray<Diagnostic> diagnostics) =
                RunModelGenerator(compilation, nodeSetResource, prefix);
            ImmutableArray<Diagnostic> outputDiagnostics = output.GetDiagnostics();
            Assert.Multiple(() =>
            {
                Assert.That(
                    diagnostics.Where(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, diagnostics));
                Assert.That(
                    outputDiagnostics.Where(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error),
                    Is.Empty,
                    string.Join(Environment.NewLine, outputDiagnostics));
            });
            return (CSharpCompilation)output;
        }

        private static (GeneratorRunResult Result, Compilation OutputCompilation,
            ImmutableArray<Diagnostic> Diagnostics) RunModelGenerator(
                CSharpCompilation compilation,
                string nodeSetResource,
                string prefix)
        {
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorStartId"] = "5000",
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });
            options.TextOptions[nodeSetResource] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = prefix
            };

            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([EmbeddedText.From(nodeSetResource)])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);
            return (driver.GetRunResult().Results[0], outputCompilation, diagnostics);
        }

        private static CSharpCompilation CreateGeneratedRoboticsModelProducer()
        {
            CSharpCompilation compilation = CreateStackCompilation("RoboticsProducer")
                .AddReferences(s_diProducer.Value.ToMetadataReference());
            compilation = compilation.WithOptions(
                compilation.Options.WithSpecificDiagnosticOptions(
                    new Dictionary<string, ReportDiagnostic>
                    {
                        ["CS0108"] = ReportDiagnostic.Suppress
                    }));
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorVersion"] = "v105",
                    ["build_property.ModelSourceGeneratorStartId"] = "2000",
                    ["build_property.ModelSourceGeneratorUseAllowSubtypes"] = "true",
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });
            options.TextOptions["Opc.Ua.IA.NodeSet2.xml"] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "Opc.Ua.IA"
            };
            options.TextOptions["Opc.Ua.Robotics.NodeSet2.xml"] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "Opc.Ua.Robotics"
            };

            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts(
                [
                    CreateRoboticsModelText("Opc.Ua.IA.NodeSet2.xml"),
                    CreateRoboticsModelText("Opc.Ua.Robotics.NodeSet2.xml")
                ])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(
                diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics));
            Assert.That(
                outputCompilation.GetDiagnostics().Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, outputCompilation.GetDiagnostics()));
            return (CSharpCompilation)outputCompilation;
        }

        private static CSharpCompilation CreateGeneratedRoboticsAccessorProvider(
            CSharpCompilation roboticsModel)
        {
            CSharpCompilation compilation = CreateStackCompilation("RoboticsAccessorProvider")
                .AddReferences(
                    s_diProducer.Value.ToMetadataReference(),
                    roboticsModel.ToMetadataReference());
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorVersion"] = "v105",
                    ["build_property.ModelSourceGeneratorUseAllowSubtypes"] = "true",
                    ["build_property.ModelSourceGeneratorFluentAccessorsOnly"] = "true"
                });
            options.TextOptions["Opc.Ua.Robotics.NodeSet2.xml"] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "Opc.Ua.Robotics"
            };

            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([CreateRoboticsModelText("Opc.Ua.Robotics.NodeSet2.xml")])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(
                diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics));
            Assert.That(
                outputCompilation.GetDiagnostics().Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, outputCompilation.GetDiagnostics()));
            return (CSharpCompilation)outputCompilation;
        }

        private static StringAdditionalText CreateRoboticsModelText(string fileName)
        {
            string repositoryRoot = FindRepositoryRoot();
            string path = Path.Combine(
                repositoryRoot,
                "src",
                "Opc.Ua.Robotics",
                "Model",
                fileName);
            return new StringAdditionalText(fileName, File.ReadAllText(path));
        }

        private static string FindRepositoryRoot()
        {
            string assemblyDirectory =
                Path.GetDirectoryName(typeof(ModelDependencyScannerTests).Assembly.Location)
                ?? throw new InvalidOperationException("Test assembly directory was not found.");
            var directory = new DirectoryInfo(assemblyDirectory);
            for (int i = 0; i < 5; i++)
            {
                directory = directory.Parent
                    ?? throw new InvalidOperationException("Repository root was not found.");
            }
            return directory.FullName;
        }

        private static CSharpCompilation CreateGeneratedDemoModelProducer(bool omitFluentApi)
        {
            CSharpCompilation compilation = CreateStackCompilation(
                    omitFluentApi ? "ProducerWithoutAccessors" : "ProducerWithAccessors")
                .AddReferences(s_diProducer.Value.ToMetadataReference());
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorStartId"] = "1000",
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] =
                        omitFluentApi ? "true" : "false"
                });

            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([EmbeddedText.From("DemoModel.NodeSet2.xml")])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            GeneratorRunResult result = driver.GetRunResult().Results[0];
            Assert.That(
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                "Generated producer must not report errors.");
            Assert.That(
                outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, outputCompilation.GetDiagnostics()));
            Assert.That(
                result.GeneratedSources.Any(s => s.HintName.EndsWith(
                    ".FluentBuilders.g.cs",
                    StringComparison.Ordinal)),
                Is.EqualTo(!omitFluentApi));

            CSharpCompilation probe = CreateStackCompilation("ProducerProbe")
                .AddReferences(
                    s_diProducer.Value.ToMetadataReference(),
                    outputCompilation.ToMetadataReference());
            ModelDependencyReference dependency = ReferencedModelDependencyScanner.Scan(probe)
                .Single(r => r.ModelUri == DemoModelUri);
            Assert.That(
                dependency.GetDependency()?.FluentAccessorsEmitted,
                Is.EqualTo(!omitFluentApi));
            ModelFluentAccessorProviderReference[] providers =
                [.. ReferencedFluentAccessorProviderScanner.Scan(probe)
                    .Where(reference => reference.ModelUri == DemoModelUri)];
            Assert.That(providers, Has.Length.EqualTo(omitFluentApi ? 0 : 1));

            return (CSharpCompilation)outputCompilation;
        }

        private static CSharpCompilation CreateModelMetadataAssembly(
            string assemblyName,
            string prefix,
            string version,
            string payload)
        {
            string payloadLiteral = payload == null ? "null" : "\"" + payload + "\"";
            string source =
                "[assembly: global::Opc.Ua.ModelDependencyAttribute(" +
                "\"" + DemoModelUri + "\", " +
                "\"" + prefix + "\", " +
                "\"" + version + "\", " +
                "\"2099-01-01\", " +
                "\"DemoModel\", " +
                payloadLiteral + ")]";
            CSharpCompilation compilation = CreateStackCompilation(assemblyName)
                .AddCode(new Dictionary<string, string>
                {
                    ["ModelMetadata.cs"] = source
                }, LanguageVersion.CSharp11);
            Assert.That(
                compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, compilation.GetDiagnostics()));
            return compilation;
        }

        private static CSharpCompilation CreateGeneratedDiProducer()
        {
            CSharpCompilation compilation = CreateStackCompilation("DiProducer");
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorStartId"] = "1000",
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });

            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.CSharp11))
                .AddAdditionalTexts([EmbeddedText.From("Opc.Ua.Di.NodeSet2.xml")])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                "Generated DI producer must not report errors.");
            Assert.That(
                outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, outputCompilation.GetDiagnostics()));
            return (CSharpCompilation)outputCompilation;
        }

        private static CSharpCompilation CreateStackCompilation(string assemblyName)
        {
            CSharpCompilation compilation =
                OptimizationLevel.Release.CreateCompilation(assemblyName);
            return compilation
                .WithOptions(compilation.Options.WithGeneralDiagnosticOption(
                    ReportDiagnostic.Error))
                .AddReferences(GetStackReferences());
        }

        private static IEnumerable<MetadataReference> GetStackReferences()
        {
            string assemblyDirectory =
                Path.GetDirectoryName(typeof(ModelDependencyScannerTests).Assembly.Location)
                ?? throw new InvalidOperationException("Test assembly directory was not found.");
            var directory = new DirectoryInfo(assemblyDirectory);
            string targetFramework = directory.Name;
#if NET_STANDARD_TESTS
            // TFM skew: this test assembly is compiled as net8.0, but on the
            // .NETStandard 2.1 test leg the stack (Opc.Ua.Server and its
            // dependencies) is compiled as netstandard2.1. Resolve the stack
            // references from the netstandard2.1 output rather than the test's
            // own target framework folder, which does not exist on that leg.
            targetFramework = "netstandard2.1";
#endif
            string configuration = directory.Parent?.Name
                ?? throw new InvalidOperationException("Test configuration directory was not found.");
            for (int i = 0; i < 5; i++)
            {
                directory = directory.Parent
                    ?? throw new InvalidOperationException("Repository root was not found.");
            }
            string serverBin = Path.Combine(
                directory.FullName,
                "src",
                "Opc.Ua.Server",
                "bin",
                configuration);
            string serverOutput = ResolveServerOutputFolder(serverBin, targetFramework);
            return Directory.EnumerateFiles(serverOutput, "Opc.Ua*.dll")
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    "Opc.Ua.Types.dll",
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => MetadataReference.CreateFromFile(path));
        }

        private static string ResolveServerOutputFolder(string serverBin, string testTargetFramework)
        {
            if (!Directory.Exists(serverBin))
            {
                throw new InvalidOperationException(
                    $"Opc.Ua.Server output folder was not found at '{serverBin}'. "
                    + "Build Opc.Ua.Server before running these tests.");
            }

            // The Opc.Ua.Server project may be built for a different target framework
            // than the test assembly (e.g. netstandard2.1 while the tests run as net8.0).
            // Probe the actual bin folder for whichever TFM subfolder exists instead of
            // assuming the test's TFM. Prefer an exact match, then well-known fallbacks.
            var candidates = Directory
                .EnumerateDirectories(serverBin)
                .Select(path => new DirectoryInfo(path))
                .ToList();

            DirectoryInfo match =
                candidates.Find(d => string.Equals(
                    d.Name, testTargetFramework, StringComparison.OrdinalIgnoreCase))
                ?? candidates.Find(d => string.Equals(
                    d.Name, "netstandard2.1", StringComparison.OrdinalIgnoreCase))
                ?? candidates.Find(d => string.Equals(
                    d.Name, "netstandard2.0", StringComparison.OrdinalIgnoreCase))
                ?? candidates
                    .Select(d => new FileInfo(Path.Combine(d.FullName, "Opc.Ua.Server.dll")))
                    .Where(f => f.Exists)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.Directory)
                    .FirstOrDefault();

            if (match == null)
            {
                throw new InvalidOperationException(
                    $"No Opc.Ua.Server build output was found under '{serverBin}'.");
            }

            return match.FullName;
        }

        private const string DemoModelUri = "urn:opcfoundation.org:2024-01:DemoModel";

        private static readonly Lazy<CSharpCompilation> s_diProducer =
            new(CreateGeneratedDiProducer);

        private static readonly Lazy<CSharpCompilation> s_producerWithoutAccessors =
            new(() => CreateGeneratedDemoModelProducer(omitFluentApi: true));

        private static readonly Lazy<CSharpCompilation> s_producerWithAccessors =
            new(() => CreateGeneratedDemoModelProducer(omitFluentApi: false));

        private sealed class StringAdditionalText : AdditionalText
        {
            public StringAdditionalText(string path, string text)
            {
                Path = path;
                m_text = SourceText.From(text);
            }

            public override string Path { get; }

            public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
            {
                return m_text;
            }

            private readonly SourceText m_text;
        }
    }
}
