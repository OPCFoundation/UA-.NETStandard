/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
                Platform.AnyCpu,
                OutputKind.DynamicallyLinkedLibrary);
            var options2 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "TestAssembly",
                OptimizationLevel.Debug,
                Platform.AnyCpu,
                OutputKind.DynamicallyLinkedLibrary);

            Assert.That(options1, Is.EqualTo(options2));
        }

        [Test]
        public void CompilationOptionsRecordInequality()
        {
            var options1 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "Assembly1",
                OptimizationLevel.Debug,
                Platform.AnyCpu,
                OutputKind.DynamicallyLinkedLibrary);
            var options2 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "Assembly2",
                OptimizationLevel.Debug,
                Platform.AnyCpu,
                OutputKind.DynamicallyLinkedLibrary);

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
                Platform.X64,
                OutputKind.DynamicallyLinkedLibrary);

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
                Platform.AnyCpu,
                OutputKind.DynamicallyLinkedLibrary);
            var options2 = new CompilationOptions(
                LanguageVersion.CSharp13,
                "TestAssembly",
                OptimizationLevel.Debug,
                Platform.AnyCpu,
                OutputKind.DynamicallyLinkedLibrary);

            Assert.That(options1.GetHashCode(), Is.EqualTo(options2.GetHashCode()));
        }
    }
}
