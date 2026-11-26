// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
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

        /// <summary>
        /// Replacing a host snapshot with identical contents must not regenerate models.
        /// </summary>
        [TestCase("DemoModel.xml")]
        [TestCase("DemoModel.csv")]
        public void EqualContentAdditionalFileStaysCached(string resourceName)
        {
            AdditionalText original = EmbeddedText.From(resourceName);
            (GeneratorDriver driver, CSharpCompilation compilation) = CreateDriver(string.Empty, []);
            driver = driver.AddAdditionalTexts([
                original,
                EmbeddedText.From(resourceName.EndsWith(".xml", System.StringComparison.Ordinal)
                    ? "DemoModel.csv" : "DemoModel.xml")]).RunGenerators(compilation);
            GeneratorRunResult before = driver.GetRunResult().Results[0];
            Assert.That(before.Exception, Is.Null);
            Assert.That(before.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
            Assert.That(before.GeneratedSources, Is.Not.Empty);

            var replacement = new SnapshotText(original.Path, original.GetText()!);
            driver = driver.ReplaceAdditionalText(original, replacement).RunGenerators(compilation);
            GeneratorRunResult after = driver.GetRunResult().Results[0];

            AssertCached(after, ModelSourceGenerator.TrackingNames.ModelCompilationInput);
            Assert.That(after.GeneratedSources.Select(s => s.SourceText.ToString()),
                Is.EqualTo(before.GeneratedSources.Select(s => s.SourceText.ToString())));
        }

        /// <summary>
        /// Changed contents and paths must still reach the model boundary.
        /// </summary>
        [TestCase("DemoModel.xml", false)]
        [TestCase("DemoModel.csv", false)]
        [TestCase("DemoModel.xml", true)]
        [TestCase("DemoModel.csv", true)]
        public void ChangedAdditionalFileInvalidatesModelInput(string resourceName, bool changePath)
        {
            AdditionalText original = EmbeddedText.From(resourceName);
            (GeneratorDriver driver, CSharpCompilation compilation) = CreateDriver(string.Empty, []);
            driver = driver.AddAdditionalTexts([
                original,
                EmbeddedText.From(resourceName.EndsWith(".xml", System.StringComparison.Ordinal)
                    ? "DemoModel.csv" : "DemoModel.xml")]).RunGenerators(compilation);
            SourceText text = original.GetText()!;
            var replacement = new SnapshotText(
                changePath ? "Changed/" + original.Path : original.Path,
                changePath ? text : text.Replace(text.Length, 0,
                    resourceName.EndsWith(".xml", System.StringComparison.Ordinal)
                        ? "\n<!-- changed -->" : "\nChanged,12345,Object"));

            driver = driver.ReplaceAdditionalText(original, replacement).RunGenerators(compilation);
            GeneratorRunResult result = driver.GetRunResult().Results[0];

            Assert.That(result.TrackedSteps[ModelSourceGenerator.TrackingNames.ModelCompilationInput]
                .SelectMany(step => step.Outputs).Select(output => output.Reason),
                Does.Contain(IncrementalStepRunReason.Modified));
        }

        /// <summary>
        /// Relevant per-file options invalidate otherwise identical XML input.
        /// </summary>
        [Test]
        public void ChangedPerFileOptionInvalidatesModelInput()
        {
            AdditionalText original = EmbeddedText.From("DemoModel.xml");
            (GeneratorDriver driver, CSharpCompilation compilation) = CreateDriver(string.Empty, []);
            driver = driver.AddAdditionalTexts([original]).RunGenerators(compilation);
            var options = new AnalyzerOptionsProvider([]);
            options.TextOptions[original.Path] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorIgnore"] = "true"
            };
            driver = driver.WithUpdatedAnalyzerConfigOptions(options).RunGenerators(compilation);

            Assert.That(driver.GetRunResult().Results[0]
                .TrackedSteps[ModelSourceGenerator.TrackingNames.ModelCompilationInput]
                .SelectMany(step => step.Outputs).Select(output => output.Reason),
                Does.Contain(IncrementalStepRunReason.Modified));
        }

        /// <summary>
        /// Identical WoT snapshots preserve both cached model work and conversion diagnostics.
        /// </summary>
        [Test]
        public void EqualContentWotFilePreservesCachedDiagnostics()
        {
            AdditionalText original = EmbeddedText.FromContent("Broken.tm.json", "{");
            (GeneratorDriver driver, CSharpCompilation compilation) = CreateDriver(string.Empty, []);
            driver = driver.AddAdditionalTexts([original]).RunGenerators(compilation);
            GeneratorRunResult before = driver.GetRunResult().Results[0];
            Assert.That(before.Diagnostics, Is.Not.Empty);

            var replacement = new SnapshotText(original.Path, original.GetText()!);
            driver = driver.ReplaceAdditionalText(original, replacement).RunGenerators(compilation);
            GeneratorRunResult after = driver.GetRunResult().Results[0];

            AssertCached(after, ModelSourceGenerator.TrackingNames.ModelCompilationInput);
            Assert.That(after.Diagnostics.Select(d => d.ToString()),
                Is.EqualTo(before.Diagnostics.Select(d => d.ToString())));
        }

        /// <summary>
        /// Capturing a failing file must leave read-error reporting inside the WoT conversion guard.
        /// </summary>
        [Test]
        public void UnreadableWotInputStillReportsConversionDiagnostic()
        {
            (GeneratorDriver driver, CSharpCompilation compilation) = CreateDriver(string.Empty, []);
            driver = driver.AddAdditionalTexts([new UnreadableWotText()]).RunGenerators(compilation);
            GeneratorRunResult result = driver.GetRunResult().Results[0];

            Assert.That(result.Exception, Is.Null);
            Assert.That(result.Diagnostics.Select(d => d.GetMessage(CultureInfo.InvariantCulture)),
                Has.Some.Contains("Cannot read the model."));
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

        /// <summary>
        /// Supplies a distinct SourceText instance for the same file contents.
        /// </summary>
        private sealed class SnapshotText : AdditionalText
        {
            /// <summary>
            /// Copies the supplied text into a separate host-style snapshot.
            /// </summary>
            internal SnapshotText(string path, SourceText text)
            {
                Path = path;
                char[] characters = new char[text.Length];
                text.CopyTo(0, characters, 0, characters.Length);
                m_text = SourceText.From(new string(characters), text.Encoding, text.ChecksumAlgorithm);
            }

            /// <inheritdoc/>
            public override string Path { get; }

            /// <inheritdoc/>
            public override SourceText GetText(CancellationToken cancellationToken = default)
            {
                return m_text;
            }

            private readonly SourceText m_text;
        }

        /// <summary>
        /// Models a host-side additional-file read failure.
        /// </summary>
        private sealed class UnreadableWotText : AdditionalText
        {
            /// <inheritdoc/>
            public override string Path => "Unreadable.tm.json";

            /// <inheritdoc/>
            public override SourceText GetText(CancellationToken cancellationToken = default)
            {
                throw new System.IO.IOException("Cannot read the model.");
            }
        }

        private static readonly CSharpParseOptions s_parseOptions = new CSharpParseOptions()
            .WithKind(SourceCodeKind.Regular)
            .WithLanguageVersion(LanguageVersion.CSharp13);
    }
}
