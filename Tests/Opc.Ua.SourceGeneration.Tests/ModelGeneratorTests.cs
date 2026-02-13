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
                    ["build_property.ModelSourceGeneratorUseAllowSubtypes"] = "true"
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
            Assert.That(generatorResult.GeneratedSources.Length, Is.EqualTo(7));
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
                    ["build_property.ModelSourceGeneratorStartId"] = "1000"
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
            Assert.That(generatorResult.GeneratedSources.Length, Is.EqualTo(14));
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
                    ["build_property.ModelSourceGeneratorUseAllowSubtypes"] = "true"
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
            GeneratorRunResult generatorResult = GenerateAndCompile(driver, compilation, true);
            Assert.That(generatorResult.GeneratedSources.Length, Is.EqualTo(6));

            string testDataXmlSchema = ValidateXmlSchema(languageVersion, generatorResult);
            Assert.That(testDataXmlSchema,
                Does.Contain("<ua:Model ModelUri=\"http://test.org/UA/Data/\" Version=\"1.0.0\""));
        }

        private static string ValidateXmlSchema(
            LanguageVersion languageVersion,
            GeneratorRunResult generatorResult)
        {
            // Get the XmlSchemas.g.cs generated source which is what we care about
            var xmlSchemaSource = generatorResult.GeneratedSources
                .Where(s => s.HintName.EndsWith("XmlSchemas.g.cs", StringComparison.Ordinal))
                .ToList();
            Assert.That(xmlSchemaSource.Count, Is.EqualTo(1));

            // Parse the generated source and verify it contains expected schema
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(xmlSchemaSource[0].SourceText);
            SyntaxNode root = syntaxTree.GetRoot();
            var classNodes = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => c.Identifier.Text == "XmlSchemas")
                .ToList();
            Assert.That(classNodes.Count, Is.EqualTo(1));
            ClassDeclarationSyntax classNode = classNodes[0];
            var properrtyNodes = classNode.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Where(f => f.Identifier.Text == "TypesXsd")
                .ToList();
            Assert.That(properrtyNodes.Count, Is.EqualTo(1));

            PropertyDeclarationSyntax propertyNode = properrtyNodes[0];
            LiteralExpressionSyntax stringLiteral = propertyNode.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .FirstOrDefault();
            Assert.That(stringLiteral, Is.Not.Null);
            // Verify that the getter contains the expected schema string
            var stringTokens = stringLiteral.ChildTokens().ToList();
            Assert.That(stringTokens.Count, Is.EqualTo(1));
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
            CSharpCompilation compilation,
            bool filterLinkerAndReferenceErrors = false)
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
                out int warnings,
                filterLinkerAndReferenceErrors);

            Assert.That(errors, Is.EqualTo(0), $"Compilation produced {errors} errors");
#if NETFRAMEWORK
            TestContext.Out.WriteLine($"Compilation produced {warnings} warnings");
#else
            Assert.That(warnings, Is.EqualTo(0), $"Compilation produced {warnings} warnings");
#endif
            // Get the results
            GeneratorDriverRunResult runResult = driver.GetRunResult();
            // Test the results
            Assert.That(runResult.GeneratedTrees, Is.Not.Empty);
            runResult.Diagnostics.Check(TestContext.Out, out errors, out warnings);
            Assert.That(errors, Is.EqualTo(0));
            TestContext.Out.WriteLine($"Run result produced {warnings} warnings");

            GeneratorRunResult generatorResult = runResult.Results[0];
            generatorResult.Diagnostics.Check(TestContext.Out, out errors, out warnings);
            Assert.That(errors, Is.EqualTo(0));
            TestContext.Out.WriteLine($"Generate run produced {warnings} warnings");

            Assert.That(generatorResult.Exception, Is.Null);
            return generatorResult;
        }
    }
}
