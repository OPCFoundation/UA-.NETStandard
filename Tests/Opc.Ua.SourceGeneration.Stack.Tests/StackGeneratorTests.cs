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
            var host = new StackSourceGeneratorHoist(generator);

            CSharpCompilation compilation = optimizationLevel.CreateCompilation("Opc.Ua.Test")
                .AddCode(new Dictionary<string, string>().WithOpcUaCoreStubs(), LanguageVersion.Latest);

            // Create the driver the executes the generator
            GeneratorDriver driver = CSharpGeneratorDriver.Create(host);
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

            Assert.That(generatorResult.GeneratedSources.Length, Is.EqualTo(13));
            Assert.That(generatorResult.Exception, Is.Null);
        }
    }
}
