// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    public class StackGeneratorTests
    {
        [DatapointSource]
        public OptimizationLevel[] OptimizationLevels = CompilerUtils.SupportedOptimizationLevels;

        [Theory]
        public void GenerateAndCompileTest(OptimizationLevel optimizationLevel)
        {
            var generator = new StackSourceGenerator();

            CSharpCompilation compilation = optimizationLevel.CreateCompilation("Opc.Ua.Test")
                .AddCode(
                    new Dictionary<string, string>()
                        .WithOpcUaCoreStubs(includeBaseEventTypeRecord: false),
                    LanguageVersion.Latest);

            // Create the driver the executes the generator. The mode is the
            // opt-in a project declares through the StackSourceGeneratorMode
            // MSBuild property; without it the generator stays silent.
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                [generator.AsSourceGenerator()],
                optionsProvider: new AnalyzerOptionsProvider(
                    new Dictionary<string, string>
                    {
                        ["build_property.stacksourcegeneratormode"] = "All"
                    }));
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

            // 17 generated sources: 16 baseline + 1 Opc.Ua.StateMachineIds.g.cs
            // emitted by the StateMachineIdsGenerator added in commit
            // 0eda2ad7a for every model that declares concrete
            // FiniteStateMachineType subtypes (the OPC UA stack model
            // declares ProgramStateMachineType, ShelvedStateMachineType,
            // FileTransferStateMachineType, etc.).
            Assert.That(generatorResult.GeneratedSources, Has.Length.EqualTo(17));
            Assert.That(generatorResult.Exception, Is.Null);
        }
    }
}
