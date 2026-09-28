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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Runs the generator twice with an edit that does not touch its inputs and
    /// verifies the pipeline stages stay cached, so the IDE does not regenerate
    /// every model (or every [DataType] file) on each keystroke.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class IncrementalCachingTests
    {
        private const string kNodeManagerSource =
            """
            namespace Opc.Ua.Server.Fluent
            {
            public sealed class NodeManagerAttribute : global::System.Attribute
            {
            public string NamespaceUri { get; set; }
            public string Design { get; set; }
            public bool GenerateFactory { get; set; }
            public string[] AdditionalNamespaceUris { get; set; }
            }
            }
            namespace CachingConsumer
            {
            [global::Opc.Ua.Server.Fluent.NodeManager(
                NamespaceUri = "http://test.org/UA/Caching",
                AdditionalNamespaceUris = new[] { "http://test.org/UA/Caching/Instances" })]
            public partial class CachingNodeManager
            {
            }
            }
            """;

        private const string kDataTypeSource =
            """
            using Opc.Ua;

            namespace CachingConsumer.Types
            {
                [DataType]
                public partial class CachedType
                {
                    public string Name { get; set; }
                    public int Count { get; set; }
                }

                [DataType]
                public enum CachedEnum
                {
                    First = 0,
                    Second = 1
                }
            }
            """;

        /// <summary>
        /// Regression: the [NodeManager] discovery carried a fresh
        /// AdditionalNamespaceUris array and a Location, so it never compared
        /// equal and every keystroke re-ran the full model generation.
        /// </summary>
        [Test]
        public void NodeManagerBindingStaysCachedAcrossUnrelatedEdit()
        {
            (GeneratorDriver driver, CSharpCompilation compilation) =
                CreateDriver(kNodeManagerSource, []);
            driver = driver.RunGenerators(compilation);

            driver = driver.RunGenerators(compilation.AddSyntaxTrees(
                ParseUnrelatedTree("class Unrelated { }")));

            GeneratorRunResult result = driver.GetRunResult().Results[0];
            AssertCached(result, ModelSourceGenerator.TrackingNames.NodeManagerBindings);
            AssertCached(result, ModelSourceGenerator.TrackingNames.ModelCompilationInput);
        }

        /// <summary>
        /// Editing the manager's own file without moving the class must not
        /// invalidate the binding either: the location is a value snapshot,
        /// not a reference to the (new) syntax tree.
        /// </summary>
        [Test]
        public void NodeManagerBindingStaysCachedAcrossEditAfterTheClass()
        {
            (GeneratorDriver driver, CSharpCompilation compilation) =
                CreateDriver(kNodeManagerSource, []);
            driver = driver.RunGenerators(compilation);

            SyntaxTree oldTree = compilation.SyntaxTrees
                .First(t => t.FilePath == "Source.cs");
            SyntaxTree newTree = oldTree.WithChangedText(
                oldTree.GetText().Replace(
                    oldTree.GetText().Length, 0, "\n// trailing edit\n"));
            driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(oldTree, newTree));

            GeneratorRunResult result = driver.GetRunResult().Results[0];
            AssertCached(result, ModelSourceGenerator.TrackingNames.NodeManagerBindings);
            AssertCached(result, ModelSourceGenerator.TrackingNames.ModelCompilationInput);
        }

        /// <summary>
        /// Regression: the Exclude option was a fresh List compared by
        /// reference, so any analyzer config change re-ran the whole model
        /// generation even when no generator option changed.
        /// </summary>
        [Test]
        public void ModelOptionsStayCachedWhenAnalyzerConfigIsReevaluated()
        {
            var globalOptions = new Dictionary<string, string>
            {
                ["build_property.ModelSourceGeneratorExclude"] = "Draft;Deprecated"
            };
            (GeneratorDriver driver, CSharpCompilation compilation) =
                CreateDriver(kNodeManagerSource, globalOptions);
            driver = driver.RunGenerators(compilation);

            driver = driver.WithUpdatedAnalyzerConfigOptions(new AnalyzerOptionsProvider(
                new Dictionary<string, string>(globalOptions)
                {
                    ["build_property.SomeOtherTool"] = "true"
                }));
            driver = driver.RunGenerators(compilation);

            GeneratorRunResult result = driver.GetRunResult().Results[0];
            AssertCached(result, ModelSourceGenerator.TrackingNames.ModelCompilationOptions);
            AssertCached(result, ModelSourceGenerator.TrackingNames.ModelCompilationInput);
        }

        /// <summary>
        /// Regression: the [DataType] pipeline value held lists, a Location
        /// and the Core diagnostics by reference, so every keystroke
        /// re-emitted every [DataType] namespace file.
        /// </summary>
        [Test]
        public void DataTypeCompilationStaysCachedAcrossUnrelatedEdit()
        {
            (GeneratorDriver driver, CSharpCompilation compilation) =
                CreateDriver(kDataTypeSource, []);
            driver = driver.RunGenerators(compilation);
            Assert.That(
                driver.GetRunResult().Results[0].GeneratedSources,
                Has.Length.EqualTo(1));

            driver = driver.RunGenerators(compilation.AddSyntaxTrees(
                ParseUnrelatedTree("class Unrelated { }")));

            GeneratorRunResult result = driver.GetRunResult().Results[0];
            AssertCached(result, ModelSourceGenerator.TrackingNames.DataTypeCompilations);
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
        }

        private static void AssertCached(GeneratorRunResult result, string stepName)
        {
            Assert.That(
                result.TrackedSteps.TryGetValue(
                    stepName,
                    out ImmutableArray<IncrementalGeneratorRunStep> steps),
                Is.True,
                $"step '{stepName}' was not tracked");
            IncrementalStepRunReason[] reasons = [.. steps
                .SelectMany(step => step.Outputs)
                .Select(output => output.Reason)];
            Assert.That(reasons, Is.Not.Empty, $"step '{stepName}' produced no output");
            Assert.That(
                reasons,
                Is.All.AnyOf(IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged),
                $"step '{stepName}' must stay cached across an edit that does not affect it");
        }

        private static (GeneratorDriver Driver, CSharpCompilation Compilation) CreateDriver(
            string source,
            Dictionary<string, string> globalOptions)
        {
            CSharpCompilation compilation = OptimizationLevel.Release
                .CreateCompilation("IncrementalCaching")
                .AddCode(
                    new Dictionary<string, string> { ["Source.cs"] = source }
                        .WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp13);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                [new ModelSourceGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: s_parseOptions,
                optionsProvider: new AnalyzerOptionsProvider(globalOptions),
                driverOptions: new GeneratorDriverOptions(
                    IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));
            return (driver, compilation);
        }

        private static SyntaxTree ParseUnrelatedTree(string source)
        {
            return CSharpSyntaxTree.ParseText(source, s_parseOptions, "Unrelated.cs");
        }

        private static readonly CSharpParseOptions s_parseOptions = new CSharpParseOptions()
            .WithKind(SourceCodeKind.Regular)
            .WithLanguageVersion(LanguageVersion.CSharp13);
    }
}
