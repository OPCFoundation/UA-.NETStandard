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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Tests
{
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CompilationOptionsTests
    {
        [Test]
        public void FromCSharpCompilationCapturesLanguageVersion()
        {
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var options = CompilationOptions.From(compilation);

            Assert.That(options.AssemblyName, Is.EqualTo("TestAssembly"));
            Assert.That(options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Debug));
            Assert.That(options.Platform, Is.EqualTo(Platform.AnyCpu));
        }

        [Test]
        public void FromCSharpCompilationWithReleaseOptimization()
        {
            CSharpCompilation compilation = CSharpCompilation.Create("ReleaseAssembly")
                .WithOptions(new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));

            var options = CompilationOptions.From(compilation);

            Assert.That(options.AssemblyName, Is.EqualTo("ReleaseAssembly"));
            Assert.That(options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        }

        [Test]
        public void CompilationOptionsRecordEquality()
        {
            var options1 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "TestAssembly",
                OptimizationLevel.Debug,
                Platform.AnyCpu);
            var options2 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "TestAssembly",
                OptimizationLevel.Debug,
                Platform.AnyCpu);

            Assert.That(options1, Is.EqualTo(options2));
        }

        [Test]
        public void CompilationOptionsRecordInequality()
        {
            var options1 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "Assembly1",
                OptimizationLevel.Debug,
                Platform.AnyCpu);
            var options2 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "Assembly2",
                OptimizationLevel.Debug,
                Platform.AnyCpu);

            Assert.That(options1, Is.Not.EqualTo(options2));
        }

        [TestCase(OptimizationLevel.Debug)]
        [TestCase(OptimizationLevel.Release)]
        public void FromCompilationPreservesOptimizationLevel(OptimizationLevel level)
        {
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .WithOptions(new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: level));

            var options = CompilationOptions.From(compilation);

            Assert.That(options.OptimizationLevel, Is.EqualTo(level));
        }

        [TestCase(Platform.AnyCpu)]
        [TestCase(Platform.X64)]
        [TestCase(Platform.X86)]
        public void FromCompilationPreservesPlatform(Platform platform)
        {
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .WithOptions(new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    platform: platform));

            var options = CompilationOptions.From(compilation);

            Assert.That(options.Platform, Is.EqualTo(platform));
        }

        [Test]
        public void CompilationOptionsToStringContainsAllFields()
        {
            var options = new CompilationOptions(
                LanguageVersion.CSharp13,
                "MyAssembly",
                OptimizationLevel.Release,
                Platform.X64);

            string str = options.ToString();

            Assert.That(str, Does.Contain("MyAssembly"));
            Assert.That(str, Does.Contain("Release"));
            Assert.That(str, Does.Contain("X64"));
        }

        [Test]
        public void CompilationOptionsGetHashCodeConsistentWithEquality()
        {
            var options1 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "TestAssembly",
                OptimizationLevel.Debug,
                Platform.AnyCpu);
            var options2 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "TestAssembly",
                OptimizationLevel.Debug,
                Platform.AnyCpu);

            Assert.That(options1.GetHashCode(), Is.EqualTo(options2.GetHashCode()));
        }
    }
}
