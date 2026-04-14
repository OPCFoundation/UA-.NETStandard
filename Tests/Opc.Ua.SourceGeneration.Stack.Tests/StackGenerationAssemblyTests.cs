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

namespace Opc.Ua.SourceGeneration.Tests
{
    [TestFixture]
    [Category("SourceGenerator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class StackGenerationAssemblyTests
    {
        [Test]
        public void GeneratorWithUnsupportedAssemblyNameReportsError()
        {
            var generator = new StackSourceGenerator();
            var host = new StackSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Debug
                .CreateCompilation("UnknownAssembly")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaCoreStubs(),
                    LanguageVersion.Latest);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            GeneratorRunResult generatorResult = runResult.Results[0];

            Assert.That(generatorResult.Exception, Is.Null);
            Assert.That(generatorResult.Diagnostics.Any(d =>
                d.Id == "STACKGEN001"), Is.True);
        }

        [Test]
        public void GeneratorWithOpcUaAssemblyNameProducesModels()
        {
            var generator = new StackSourceGenerator();
            var host = new StackSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Debug
                .CreateCompilation("Opc.Ua")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaCoreStubs(),
                    LanguageVersion.Latest);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            GeneratorRunResult generatorResult = runResult.Results[0];

            Assert.That(generatorResult.Exception, Is.Null);
            Assert.That(generatorResult.GeneratedSources, Is.Not.Empty);
        }

        [Test]
        public void GeneratorWithOpcUaCoreAssemblyNameProducesStack()
        {
            var generator = new StackSourceGenerator();
            var host = new StackSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Debug
                .CreateCompilation("Opc.Ua.Core")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaCoreStubs(),
                    LanguageVersion.Latest);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            GeneratorRunResult generatorResult = runResult.Results[0];

            Assert.That(generatorResult.Exception, Is.Null);
            Assert.That(generatorResult.GeneratedSources, Is.Not.Empty);
        }

        [Test]
        public void GeneratorWithOldLanguageVersionReportsError()
        {
            var generator = new StackSourceGenerator();
            var host = new StackSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Debug
                .CreateCompilation("Opc.Ua.Test")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaCoreStubs(),
                    LanguageVersion.CSharp12);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            GeneratorRunResult generatorResult = runResult.Results[0];

            Assert.That(generatorResult.Exception, Is.Null);
            Assert.That(generatorResult.Diagnostics.Any(d =>
                d.Id == "STACKGEN001"), Is.True);
            Assert.That(generatorResult.GeneratedSources, Is.Empty);
        }

        [Test]
        public void GeneratorWithReleaseOptimizationProducesOutput()
        {
            var generator = new StackSourceGenerator();
            var host = new StackSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release
                .CreateCompilation("Opc.Ua.Test")
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaCoreStubs(),
                    LanguageVersion.Latest);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            GeneratorRunResult generatorResult = runResult.Results[0];

            Assert.That(generatorResult.Exception, Is.Null);
            Assert.That(generatorResult.GeneratedSources, Is.Not.Empty);
        }
    }
}
