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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Test generation and compilation
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    // [Parallelizable(ParallelScope.All)]
    public class ModelGeneratorTests
    {
        [DatapointSource]
        public OptimizationLevel[] OptimizationLevels =
            CompilerUtils.SupportedOptimizationLevels;

        [DatapointSource]
        public LanguageVersion[] LanguageVersions =
            CompilerUtils.SupportedLanguageVersions;

        [Theory]
        public void GenerateAndCompileDemoModelXmlTest(
            LanguageVersion languageVersion,
            OptimizationLevel optimizationLevel)
        {
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = optimizationLevel.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), languageVersion);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorVersion"] = "v105",
                    ["build_property.ModelSourceGeneratorExclude"] = "Draft",
                    ["build_property.ModelSourceGeneratorUseAllowSubtypes"] = "true",
                    // The test compilation includes Core stubs via
                    // WithOpcUaGeneratedStack() but no Server reference.
                    // Suppress fluent-builder emission so the generated
                    // code compiles standalone (matches model-only
                    // production csprojs).
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });

            // Create the driver the executes the generator
            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(languageVersion))
                .AddAdditionalTexts([EmbeddedText.From("DemoModel.xml")])
                .WithUpdatedAnalyzerConfigOptions(options)
                ;
            GeneratorRunResult generatorResult = GenerateAndCompile(driver, compilation);
            Assert.That(generatorResult.GeneratedSources, Has.Length.EqualTo(9));
        }

        [Theory]
        public void GenerateAndCompileDemoModelNodeSetsTest(
            LanguageVersion languageVersion,
            OptimizationLevel optimizationLevel)
        {
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = optimizationLevel.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), languageVersion);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorStartId"] = "1000",
                    // The test compilation includes Core stubs but no
                    // Server reference. Suppress fluent-builder
                    // emission so the generated code compiles standalone.
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });

            // Create the driver the executes the generator
            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(languageVersion))
                .AddAdditionalTexts([
                    EmbeddedText.From("DemoModel.NodeSet2.xml"),
                    EmbeddedText.From("Opc.Ua.Di.NodeSet2.xml")
                 ])
                .WithUpdatedAnalyzerConfigOptions(options)
                ;
            GeneratorRunResult generatorResult = GenerateAndCompile(driver, compilation);
            // 19 generated files: 9 per model (Constants, DataTypes,
            // Identifiers, ModelDependencies, NodeStates, NodeStates.ex,
            // NodeStates.i, TypeProxies, XmlSchemas) for DemoModel + 9
            // for DI, plus 1 DI.StateMachineIds.g.cs (DI declares Part
            // 16 FSM subtypes for the software-update facet; DemoModel
            // declares none, so it gets no StateMachineIds output).
            Assert.That(generatorResult.GeneratedSources, Has.Length.EqualTo(19));
        }

        [Theory]
        public void GenerateAndCompileIsa95JobControlNodeSet2Test(
            LanguageVersion languageVersion)
        {
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), languageVersion);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(languageVersion))
                .AddAdditionalTexts([EmbeddedText.From("Isa95JobControl.NodeSet2.xml")])
                .WithUpdatedAnalyzerConfigOptions(options);

            GeneratorRunResult generatorResult = GenerateAndCompile(driver, compilation);

            string generatedSources = string.Join(
                "\n",
                generatorResult.GeneratedSources.Select(source => source.SourceText.ToString()));
            Assert.That(generatedSources, Does.Contain("ISA95JobOrderDataType"));
        }

        [Test]
        public void Isa95JobControlNodeSet2FixtureIsComplete()
        {
            string nodeSet = EmbeddedText.From("Isa95JobControl.NodeSet2.xml")
                .GetText()!
                .ToString();
            Assert.That(nodeSet, Does.Contain("OPC Foundation MIT License 1.00"));

            XDocument document = XDocument.Parse(nodeSet);
            XNamespace ua = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
            XElement root = document.Root!;
            XElement[] models = [.. root.Element(ua + "Models")!.Elements(ua + "Model")];
            Assert.That(models, Has.Length.EqualTo(1));
            Assert.That(
                models[0].Attribute("ModelUri")!.Value,
                Is.EqualTo("http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/"));
            Assert.That(models[0].Attribute("Version")!.Value, Is.EqualTo("2.0.0"));
            Assert.That(
                models[0].Attribute("PublicationDate")!.Value,
                Is.EqualTo("2024-01-31T00:00:00Z"));

            XElement[] nodes = [.. root.Elements().Where(element =>
                element.Name.LocalName.StartsWith("UA", StringComparison.Ordinal))];
            Assert.That(nodes, Has.Length.EqualTo(258));
            Assert.That(root.Elements(ua + "UADataType").Count(), Is.EqualTo(11));
            Assert.That(root.Elements(ua + "UAObjectType").Count(), Is.EqualTo(8));
            Assert.That(root.Elements(ua + "UAVariable").Count(), Is.EqualTo(134));
            Assert.That(root.Elements(ua + "UAObject").Count(), Is.EqualTo(91));
            Assert.That(root.Elements(ua + "UAMethod").Count(), Is.EqualTo(14));

            Assert.That(
                root.Elements(ua + "UAVariable").Any(variable =>
                    variable.Attribute("NodeId")!.Value == "ns=1;i=6088" &&
                    variable.Attribute("BrowseName")!.Value == "1:MaxDownloadableJobOrders"),
                Is.True);
            Assert.That(
                root.Elements(ua + "UAVariable").Any(variable =>
                    variable.Attribute("NodeId")!.Value == "ns=1;i=6029" &&
                    variable.Attribute("BrowseName")!.Value == "StaticStringNodeIdPattern"),
                Is.True);

            var nodeIds = new HashSet<string>(
                nodes.Select(node => node.Attribute("NodeId")!.Value),
                StringComparer.Ordinal);
            string[] unresolvedSameNamespaceReferences =
            [
                .. document.Descendants(ua + "Reference")
                    .Select(reference => reference.Value.Trim())
                    .Where(reference =>
                        reference.StartsWith("ns=1;", StringComparison.Ordinal) &&
                        !nodeIds.Contains(reference))
                    .Distinct(StringComparer.Ordinal)
            ];
            Assert.That(unresolvedSameNamespaceReferences, Is.Empty);
        }

        [Theory]
        public void GenerateAndCompileTestDataDesignTest(
            LanguageVersion languageVersion)
        {
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), languageVersion);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorVersion"] = "v105",
                    ["build_property.ModelSourceGeneratorExclude"] = "Draft",
                    ["build_property.ModelSourceGeneratorUseAllowSubtypes"] = "true",
                    // The test compilation includes Core stubs but no
                    // Server reference. Suppress fluent-builder
                    // emission so the generated code compiles standalone.
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });

            // Create the driver that executes the generator
            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(languageVersion))
                .AddAdditionalTexts(
                [
                    EmbeddedText.From("TestDataDesign.xml"),
                    EmbeddedText.From("TestDataDesign.csv")
                ])
                .WithUpdatedAnalyzerConfigOptions(options)
                ;

            // There will be 120 errors due to missing Opc.Ua dll reference
            GeneratorRunResult generatorResult = GenerateAndCompile(driver, compilation);
            // Adding the HeaterStatus enum (used by HeaterStatusMatrix in
            // MatrixValueDataType to exercise the typed-enum matrix codegen
            // path) caused the generator to emit one additional output file
            // for TestDataDesign, bringing the total from 8 to 9 (the same
            // per-model count that DemoModel produces above).
            Assert.That(generatorResult.GeneratedSources, Has.Length.EqualTo(9));

            string testDataXmlSchema = ValidateXmlSchema(languageVersion, generatorResult);
            Assert.That(testDataXmlSchema,
                Does.Contain("<ua:Model ModelUri=\"http://test.org/UA/Data/\" Version=\"1.0.0\""));
        }

        [Theory]
        public void GenerateAndCompileModelDesignReferencingNodeSet2TypesTest(
            LanguageVersion languageVersion)
        {
            // Regression test for issue #3937: a ModelDesign describing an
            // object instance (CrossModelInstances.ModelDesign.xml) whose
            // TypeDefinition references an ObjectType defined in a separate
            // NodeSet2 AdditionalFile (CrossModelTypes.NodeSet2.xml). The
            // ModelDesign generation pass must receive the NodeSet2 input as a
            // dependency so the cross-model type reference resolves; otherwise
            // generation fails with MODELGEN003 ("The TypeDefinition reference
            // for node Widget1 is not the expected type: ObjectTypeDesign.").
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), languageVersion);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    // The test compilation includes Core stubs but no Server
                    // reference. Suppress fluent-builder emission so the
                    // generated code compiles standalone.
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });

            // Per-file metadata: the NodeSet2 declares the type model; the
            // ModelDesign declares the instance model that references it.
            options.TextOptions["CrossModelTypes.NodeSet2.xml"] =
                new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                        "http://test.org/UA/CrossModel/Types",
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "CrossModelTypes",
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "CrossModelTypes"
                };
            options.TextOptions["CrossModelInstances.ModelDesign.xml"] =
                new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                        "http://test.org/UA/CrossModel/Instances"
                };

            // Create the driver that executes the generator
            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(languageVersion))
                .AddAdditionalTexts(
                [
                    EmbeddedText.From("CrossModelTypes.NodeSet2.xml"),
                    EmbeddedText.From("CrossModelInstances.ModelDesign.xml")
                ])
                .WithUpdatedAnalyzerConfigOptions(options)
                ;

            // Before the fix the ModelDesign pass could not resolve the
            // NodeSet2-defined type and generation failed with MODELGEN003;
            // after the fix generation completes and the generated code compiles.
            GeneratorRunResult generatorResult = GenerateAndCompile(driver, compilation);

            // The instance output must reference the type class generated from
            // the NodeSet2 input. A resolved cross-model reference proves the
            // NodeSet2 input was wired into the ModelDesign pass as a dependency.
            string allSources = string.Join(
                "\n",
                generatorResult.GeneratedSources.Select(s => s.SourceText.ToString()));
            Assert.That(allSources,
                Does.Contain("CrossModelTypes.WidgetState"),
                "instance output should reference the generated NodeSet2 type class");
        }

        [Theory]
        public void GenerateAndCompileModelDesignReferencingNodeSet2TypesReversedInputOrderTest(
            LanguageVersion languageVersion)
        {
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>().WithOpcUaGeneratedStack(), languageVersion);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });

            options.TextOptions["CrossModelTypes.NodeSet2.xml"] =
                new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                        "http://test.org/UA/CrossModel/Types",
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "CrossModelTypes",
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "CrossModelTypes"
                };
            options.TextOptions["CrossModelInstances.ModelDesign.xml"] =
                new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                        "http://test.org/UA/CrossModel/Instances"
                };

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(languageVersion))
                // Ensure resolution is independent of AdditionalFiles order.
                .AddAdditionalTexts(
                [
                    EmbeddedText.From("CrossModelInstances.ModelDesign.xml"),
                    EmbeddedText.From("CrossModelTypes.NodeSet2.xml")
                ])
                .WithUpdatedAnalyzerConfigOptions(options)
                ;

            GeneratorRunResult generatorResult = GenerateAndCompile(driver, compilation);

            string allSources = string.Join(
                "\n",
                generatorResult.GeneratedSources.Select(s => s.SourceText.ToString()));
            Assert.That(allSources,
                Does.Contain("CrossModelTypes.WidgetState"),
                "cross-model type resolution should not depend on file order");
        }

        [Theory]
        public void NodeManagerBoundToNodeSet2TypesIsNotReportedUnmatchedWithModelDesign(
            LanguageVersion languageVersion)
        {
            // Regression for issue #3937 (follow-up comment): a [NodeManager]
            // bound to the NodeSet2 *types* model must not be reported as
            // unmatched (MODELGEN010) merely because the project also contains
            // a ModelDesign *instances* model. Binding resolution runs in two
            // passes; the NodeSet2 pass matches the binding, so the ModelDesign
            // pass must not false-positive it. Before the fix the ModelDesign
            // pass reported the binding it never saw as unmatched, which — with
            // TreatWarningsAsErrors — blocked the build.
            const string bindingSource =
                """
                namespace Opc.Ua.Server.Fluent
                {
                public sealed class NodeManagerAttribute : global::System.Attribute
                {
                public string NamespaceUri { get; set; }
                public string Design { get; set; }
                public bool GenerateFactory { get; set; }
                }
                }
                namespace CrossModelConsumer
                {
                [global::Opc.Ua.Server.Fluent.NodeManager(NamespaceUri = "http://test.org/UA/CrossModel/Types")]
                public partial class TypesNodeManager
                {
                }
                }
                """;
            (ImmutableArray<Diagnostic> diagnostics, GeneratorDriverRunResult runResult) =
                RunMixedModelGenerator(languageVersion, bindingSource);

            Assert.That(
                diagnostics.Where(d => d.Id == "MODELGEN010"),
                Is.Empty,
                "a [NodeManager] matched by the NodeSet2 pass must not be " +
                "reported as unmatched by the ModelDesign pass");

            string generated = string.Join(
                "\n",
                runResult.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));
            Assert.That(
                generated,
                Does.Contain("class TypesNodeManager"),
                "the matched [NodeManager] should generate a node manager partial");
        }

        [Theory]
        public void NodeManagerBoundToModelDesignInstancesIsNotReportedUnmatchedWithNodeSet2(
            LanguageVersion languageVersion)
        {
            // Symmetric case: a [NodeManager] bound to the ModelDesign
            // *instances* model must not be reported unmatched by the NodeSet2
            // pass. The ModelDesign pass matches it; aggregate reporting keeps
            // it silent.
            const string bindingSource =
                """
                namespace Opc.Ua.Server.Fluent
                {
                public sealed class NodeManagerAttribute : global::System.Attribute
                {
                public string NamespaceUri { get; set; }
                public string Design { get; set; }
                public bool GenerateFactory { get; set; }
                }
                }
                namespace CrossModelConsumer
                {
                [global::Opc.Ua.Server.Fluent.NodeManager(NamespaceUri = "http://test.org/UA/CrossModel/Instances")]
                public partial class InstancesNodeManager
                {
                }
                }
                """;
            (ImmutableArray<Diagnostic> diagnostics, GeneratorDriverRunResult runResult) =
                RunMixedModelGenerator(languageVersion, bindingSource);

            Assert.That(
                diagnostics.Where(d => d.Id == "MODELGEN010"),
                Is.Empty,
                "a [NodeManager] matched by the ModelDesign pass must not be " +
                "reported as unmatched by the NodeSet2 pass");

            string generated = string.Join(
                "\n",
                runResult.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));
            Assert.That(
                generated,
                Does.Contain("class InstancesNodeManager"),
                "the matched [NodeManager] should generate a node manager partial");
        }

        [Theory]
        public void SelectorlessNodeManagerAcrossTwoModelsReportsSingleAmbiguity(
            LanguageVersion languageVersion)
        {
            // A [NodeManager] with no NamespaceUri/Design selector cannot bind
            // when the project has multiple models. It must be reported exactly
            // once (aggregated across both passes, not once per pass) with an
            // ambiguity message that guides the user to add a NamespaceUri.
            const string bindingSource =
                """
                namespace Opc.Ua.Server.Fluent
                {
                public sealed class NodeManagerAttribute : global::System.Attribute
                {
                public string NamespaceUri { get; set; }
                public string Design { get; set; }
                public bool GenerateFactory { get; set; }
                }
                }
                namespace CrossModelConsumer
                {
                [global::Opc.Ua.Server.Fluent.NodeManager]
                public partial class AmbiguousNodeManager
                {
                }
                }
                """;
            (ImmutableArray<Diagnostic> diagnostics, _) =
                RunMixedModelGenerator(languageVersion, bindingSource);

            Diagnostic[] unmatched = [.. diagnostics.Where(d => d.Id == "MODELGEN010")];
            Assert.That(
                unmatched,
                Has.Length.EqualTo(1),
                "a selector-less binding must be reported once, not once per pass");
            Assert.That(
                unmatched[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("multiple models"),
                "the diagnostic should explain the ambiguity");
        }

        /// <summary>
        /// Run the model generator over the shared cross-model NodeSet2 +
        /// ModelDesign fixture plus a user-supplied source containing a
        /// <c>[NodeManager]</c> attribute, and return the generator
        /// diagnostics and run result. The output is intentionally not
        /// strictly compiled: a matched <c>[NodeManager]</c> emits Fluent
        /// node-manager code that references <c>Opc.Ua.Server</c> types not
        /// present in this model-only test compilation.
        /// </summary>
        private static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult)
            RunMixedModelGenerator(LanguageVersion languageVersion, string bindingSource)
        {
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release.CreateCompilation()
                .AddCode(new Dictionary<string, string>
                {
                    ["NodeManagerBinding.cs"] = bindingSource
                }.WithOpcUaGeneratedStack(), languageVersion);

            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });
            options.TextOptions["CrossModelTypes.NodeSet2.xml"] =
                new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                        "http://test.org/UA/CrossModel/Types",
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "CrossModelTypes",
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = "CrossModelTypes"
                };
            options.TextOptions["CrossModelInstances.ModelDesign.xml"] =
                new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                        "http://test.org/UA/CrossModel/Instances"
                };

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(languageVersion))
                .AddAdditionalTexts(
                [
                    EmbeddedText.From("CrossModelTypes.NodeSet2.xml"),
                    EmbeddedText.From("CrossModelInstances.ModelDesign.xml")
                ])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation _,
                out ImmutableArray<Diagnostic> diagnostics);

            return (diagnostics, driver.GetRunResult());
        }

        private static string ValidateXmlSchema(
            LanguageVersion languageVersion,
            GeneratorRunResult generatorResult)
        {
            // Get the XmlSchemas.g.cs generated source which is what we care about
            var xmlSchemaSource = generatorResult.GeneratedSources
                .Where(s => s.HintName.EndsWith("XmlSchemas.g.cs", StringComparison.Ordinal))
                .ToList();
            Assert.That(xmlSchemaSource, Has.Count.EqualTo(1));

            // Parse the generated source and verify it contains expected schema
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(xmlSchemaSource[0].SourceText);
            SyntaxNode root = syntaxTree.GetRoot();
            var classNodes = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => c.Identifier.Text == "XmlSchemas")
                .ToList();
            Assert.That(classNodes, Has.Count.EqualTo(1));
            ClassDeclarationSyntax classNode = classNodes[0];
            var properrtyNodes = classNode.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Where(f => f.Identifier.Text == "TypesXsd")
                .ToList();
            Assert.That(properrtyNodes, Has.Count.EqualTo(1));

            PropertyDeclarationSyntax propertyNode = properrtyNodes[0];
            LiteralExpressionSyntax stringLiteral = propertyNode.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .FirstOrDefault();
            Assert.That(stringLiteral, Is.Not.Null);
            // Verify that the getter contains the expected schema string
            var stringTokens = stringLiteral.ChildTokens().ToList();
            Assert.That(stringTokens, Has.Count.EqualTo(1));
            string stringLiteralText = stringTokens[0].ToFullString().Trim();
            if (stringLiteralText.EndsWith("u8", StringComparison.Ordinal))
            {
                Assert.That(languageVersion, Is.GreaterThanOrEqualTo(LanguageVersion.CSharp11));
#pragma warning disable IDE0057 // No range available
                stringLiteralText = stringLiteralText.Substring(0, stringLiteralText.Length - 2);
#pragma warning restore IDE0057 // No range available
            }
            stringLiteralText = stringLiteralText.Trim('"');
            if (languageVersion < LanguageVersion.CSharp11)
            {
                // Base 64 encoded string is split into multiple lines
                return Encoding.UTF8.GetString(
                    Convert.FromBase64String(stringLiteralText));
            }
            return stringLiteralText;
        }

        private static GeneratorRunResult GenerateAndCompile(
            GeneratorDriver driver,
            CSharpCompilation compilation)
        {
            // Run it
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(outputCompilation.SyntaxTrees.Count(), Is.GreaterThan(1));

            outputCompilation.GetDiagnostics().Check(
                TestContext.Out,
                out int errors,
                out int warnings);

            Assert.That(errors, Is.Zero, $"Compilation produced {errors} errors");
#if NETFRAMEWORK
            TestContext.Out.WriteLine($"Compilation produced {warnings} warnings");
#else
            Assert.That(warnings, Is.Zero, $"Compilation produced {warnings} warnings");
#endif
            // Get the results
            GeneratorDriverRunResult runResult = driver.GetRunResult();
            // Test the results
            Assert.That(runResult.GeneratedTrees, Is.Not.Empty);
            runResult.Diagnostics.Check(TestContext.Out, out errors, out warnings);
            Assert.That(errors, Is.Zero);
            TestContext.Out.WriteLine($"Run result produced {warnings} warnings");

            GeneratorRunResult generatorResult = runResult.Results[0];
            generatorResult.Diagnostics.Check(TestContext.Out, out errors, out warnings);
            Assert.That(errors, Is.Zero);
            TestContext.Out.WriteLine($"Generate run produced {warnings} warnings");

            Assert.That(generatorResult.Exception, Is.Null);
            return generatorResult;
        }
    }
}
