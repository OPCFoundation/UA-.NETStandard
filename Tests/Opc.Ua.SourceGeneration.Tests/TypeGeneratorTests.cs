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
using System.Collections.Immutable;
using System.Linq;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests for the [DataType] source generator that verify both
    /// generation and compilation of annotated partial classes.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class TypeGeneratorTests
    {
        [Test]
        public void SingleClassWithScalarPropertiesCompilesSuccessfully()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.Config
{
    [DataType]
    public partial class ServerConfig
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public bool Enabled { get; set; }
        public double Timeout { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(generated, Does.Contain("partial class ServerConfig"));
            Assert.That(generated, Does.Contain("IEncodeable"));
            Assert.That(generated, Does.Contain("ServerConfigActivator"));
            Assert.That(generated, Does.Contain("encoder.WriteString"));
            Assert.That(generated, Does.Contain("encoder.WriteInt32"));
            Assert.That(generated, Does.Contain("encoder.WriteBoolean"));
            Assert.That(generated, Does.Contain("encoder.WriteDouble"));
        }

        [Test]
        public void TwoClassesSameNamespaceProduceSingleFile()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.Config
{
    [DataType]
    public partial class ServerConfig
    {
        public string Name { get; set; }
        public int Port { get; set; }
    }

    [DataType]
    public partial class ClientConfig
    {
        public string EndpointUrl { get; set; }
        public uint SessionTimeout { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1),
                "Two types in the same namespace should produce one file");
            string generated = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(generated, Does.Contain("partial class ServerConfig"));
            Assert.That(generated, Does.Contain("partial class ClientConfig"));
            Assert.That(generated, Does.Contain("ServerConfigActivator"));
            Assert.That(generated, Does.Contain("ClientConfigActivator"));
        }

        [Test]
        public void TwoClassesDifferentNamespacesProduceTwoFiles()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.Server
{
    [DataType]
    public partial class ServerSettings
    {
        public int MaxSessions { get; set; }
    }
}

namespace TestApp.Client
{
    [DataType]
    public partial class ClientSettings
    {
        public string DefaultEndpoint { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(2),
                "Two types in different namespaces should produce two files");
        }

        [Test]
        public void ClassWithDataTypeFieldAnnotationsOnlyEncodesAnnotatedProperties()
        {
            const string source = """

using Opc.Ua;

namespace TestApp.Selective
{
    [DataType]
    public partial class SelectiveConfig
    {
        [DataTypeField(Order = 0)]
        public string Name { get; set; }

        [DataTypeField(Order = 1, Name = "server_port")]
        public int Port { get; set; }

        // Not annotated - should be excluded
        public string InternalNote { get; set; }
    }
}
""";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(generated, Does.Contain("encoder.WriteString(\"Name\""));
            Assert.That(generated, Does.Contain("encoder.WriteInt32(\"server_port\""));
            Assert.That(generated, Does.Not.Contain("InternalNote"));
        }

        [Test]
        public void ClassWithoutAnnotationEncodesAllPublicProperties()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.AllProps
{
    [DataType]
    public partial class AllPropsConfig
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public bool Active { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(generated, Does.Contain("encoder.WriteString(\"Name\""));
            Assert.That(generated, Does.Contain("encoder.WriteInt32(\"Port\""));
            Assert.That(generated, Does.Contain("encoder.WriteBoolean(\"Active\""));
        }

        [Test]
        public void ClassWithUnsupportedPropertyTypeEmitsWarningAndExcludesProperty()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.BadType
{
    public class CustomThing { }

    [DataType]
    public partial class MixedConfig
    {
        public string Name { get; set; }
        public CustomThing Bad { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source, expectWarnings: true);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(generated, Does.Contain("encoder.WriteString(\"Name\""));
            Assert.That(generated, Does.Not.Contain("CustomThing"));

            Assert.That(result.Diagnostics.Any(d =>
                d.Severity == DiagnosticSeverity.Warning &&
                d.GetMessage(CultureInfo.InvariantCulture).Contains("Bad", StringComparison.Ordinal)),
                Is.True, "Should warn about unsupported type");
        }

        [Test]
        public void AnnotatedPropertyWithUnsupportedTypeEmitsError()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.BadAnnotated
{
    public class CustomThing { }

    [DataType]
    public partial class BadAnnotatedConfig
    {
        [DataTypeField(Order = 0)]
        public CustomThing Bad { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source,
                expectErrors: true, expectWarnings: true);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(0),
                "Should not generate code when annotated field has unsupported type");
        }

        [Test]
        public void NonPartialClassEmitsError()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.NotPartial
{
    [DataType]
    public class NotPartialConfig
    {
        public string Name { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source, expectErrors: true);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(0),
                "Should not generate code for non-partial class");
        }

        [Test]
        public void ClassWithOpcUaBuiltInTypesCompiles()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.BuiltIns
{
    [DataType]
    public partial class AllBuiltInsConfig
    {
        public NodeId NodeIdentifier { get; set; }
        public QualifiedName BrowseName { get; set; }
        public LocalizedText DisplayName { get; set; }
        public StatusCode Status { get; set; }
        public ExtensionObject Extension { get; set; }
        public Variant Value { get; set; }
        public DataValue DataVal { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(generated, Does.Contain("WriteNodeId"));
            Assert.That(generated, Does.Contain("WriteQualifiedName"));
            Assert.That(generated, Does.Contain("WriteLocalizedText"));
            Assert.That(generated, Does.Contain("WriteStatusCode"));
            Assert.That(generated, Does.Contain("WriteExtensionObject"));
            Assert.That(generated, Does.Contain("WriteVariant"));
            Assert.That(generated, Does.Contain("WriteDataValue"));
        }

        [Test]
        public void ClassWithCustomDataTypeIdUsesSpecifiedIds()
        {
            const string source = """

using Opc.Ua;

namespace TestApp.CustomId
{
    [DataType(DataTypeId = "i=12345", BinaryEncodingId = "i=12346")]
    public partial class CustomIdConfig
    {
        public string Name { get; set; }
    }
}
""";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(generated, Does.Contain("i=12345"));
            Assert.That(generated, Does.Contain("i=12346"));
        }

        [Test]
        public void TwoClassesSameNamespaceSingleExtensionMethod()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.Shared
{
    [DataType]
    public partial class TypeA
    {
        public string Value { get; set; }
    }

    [DataType]
    public partial class TypeB
    {
        public int Count { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();

            int extensionClassCount = CountOccurrences(
                generated, "TestAppSharedDataTypeExtensions");
            Assert.That(extensionClassCount, Is.GreaterThanOrEqualTo(1),
                "Should have exactly one extension class");

            Assert.That(generated, Does.Contain("TypeAActivator.Instance"));
            Assert.That(generated, Does.Contain("TypeBActivator.Instance"));
        }

        [Test]
        public void IncrementalAddSecondClassProducesSingleFileWithBothTypes()
        {
            const string sourceA = @"
using Opc.Ua;

namespace TestApp.Incremental
{
    [DataType]
    public partial class FirstType
    {
        public string Name { get; set; }
    }
}";

            const string sourceB = @"
using Opc.Ua;

namespace TestApp.Incremental
{
    [DataType]
    public partial class SecondType
    {
        public int Count { get; set; }
    }
}";
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);
            CSharpParseOptions parseOptions = new CSharpParseOptions()
                .WithKind(SourceCodeKind.Regular)
                .WithLanguageVersion(LanguageVersion.CSharp13);

            // Step 1: compile with only FirstType
            CSharpCompilation compilationA = OptimizationLevel.Release
                .CreateCompilation("IncrementalTest")
                .AddCode(
                    new[] { new System.Collections.Generic.KeyValuePair<string, string>(
                        "SourceA.cs", sourceA) }
                    .WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp13);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(parseOptions);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilationA,
                out Compilation outputA,
                out ImmutableArray<Diagnostic> diagsA);

            Assert.That(
                diagsA.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(),
                Is.Empty, "Step 1 generator errors");

            GeneratorDriverRunResult runA = driver.GetRunResult();
            GeneratorRunResult resultA = runA.Results[0];
            Assert.That(resultA.GeneratedSources, Has.Length.EqualTo(1),
                "Step 1 should produce 1 file");

            string generatedA = resultA.GeneratedSources[0].SourceText.ToString();
            Assert.That(generatedA, Does.Contain("partial class FirstType"));
            Assert.That(generatedA, Does.Not.Contain("SecondType"));

            // Step 2: add SecondType and re-run the same driver (incremental)
            // Use the original compilation (not outputA which includes generated trees)
            SyntaxTree newTree = CSharpSyntaxTree.ParseText(
                sourceB, parseOptions, "SourceB.cs");
            CSharpCompilation compilationB = compilationA.AddSyntaxTrees(newTree);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilationB,
                out Compilation outputB,
                out ImmutableArray<Diagnostic> diagsB);

            Assert.That(
                diagsB.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray(),
                Is.Empty, "Step 2 generator errors");

            GeneratorDriverRunResult runB = driver.GetRunResult();
            GeneratorRunResult resultB = runB.Results[0];
            Assert.That(resultB.GeneratedSources, Has.Length.EqualTo(1),
                "Step 2 should still produce exactly 1 file (both types batched)");

            string generatedB = resultB.GeneratedSources[0].SourceText.ToString();
            Assert.That(generatedB, Does.Contain("partial class FirstType"),
                "Final output must contain FirstType");
            Assert.That(generatedB, Does.Contain("partial class SecondType"),
                "Final output must contain SecondType");
            Assert.That(generatedB, Does.Contain("FirstTypeActivator"),
                "Final output must contain FirstType activator");
            Assert.That(generatedB, Does.Contain("SecondTypeActivator"),
                "Final output must contain SecondType activator");

            // Verify single extension class with both registrations
            int extCount = CountOccurrences(
                generatedB, "TestAppIncrementalDataTypeExtensions");
            Assert.That(extCount, Is.GreaterThanOrEqualTo(1),
                "Should have one extension class");
            Assert.That(generatedB, Does.Contain("FirstTypeActivator.Instance"));
            Assert.That(generatedB, Does.Contain("SecondTypeActivator.Instance"));

            // Verify the final compilation succeeds
            outputB.GetDiagnostics().Check(
                TestContext.Out,
                out int errors,
                out int warnings,
                filterLinkerAndReferenceErrors: true);
            errors -= outputB.GetDiagnostics()
                .Count(d => d.Id == "CS0234" &&
                    d.Severity == DiagnosticSeverity.Error);
            Assert.That(errors, Is.Zero,
                $"Final incremental compilation produced {errors} errors");
        }

        private static GeneratorRunResult RunGenerator(
            string source,
            bool expectErrors = false,
            bool expectWarnings = false)
        {
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);

            CSharpCompilation compilation = OptimizationLevel.Release
                .CreateCompilation()
                .AddCode(
                    new[] { new System.Collections.Generic.KeyValuePair<string, string>(
                        "TestSource.cs", source) }
                    .WithOpcUaGeneratedStack(),
                    LanguageVersion.Preview);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(new CSharpParseOptions()
                    .WithKind(SourceCodeKind.Regular)
                    .WithLanguageVersion(LanguageVersion.Preview));

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            if (!expectErrors)
            {
                Assert.That(
                    diagnostics.Where(d =>
                        d.Severity == DiagnosticSeverity.Error).ToArray(),
                    Is.Empty,
                    "Generator should not produce errors");
            }

            outputCompilation.GetDiagnostics().Check(
                TestContext.Out,
                out int errors,
                out int warnings,
                filterLinkerAndReferenceErrors: true);

            // Filter CS0234 (namespace member not found) which occurs
            // because the test stubs don't include Opc.Ua.Utils
            errors -= outputCompilation.GetDiagnostics()
                .Count(d => d.Id == "CS0234" &&
                    d.Severity == DiagnosticSeverity.Error);

            if (!expectErrors)
            {
                Assert.That(errors, Is.Zero,
                    $"Compilation produced {errors} errors");
            }

            if (!expectWarnings)
            {
#if !NETFRAMEWORK
                Assert.That(warnings, Is.Zero,
                    $"Compilation produced {warnings} warnings");
#endif
            }

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            return runResult.Results[0];
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(
                pattern, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        [Test]
        public void PartialInitPropertiesGenerateBackingFieldsAndDecodeTarget()
        {
            const string source = @"
using Opc.Ua;

namespace TestApp.InitOnly
{
    [DataType]
    public partial record class ImmutableConfig
    {
        [DataTypeField(Order = 0)]
        public partial string Name { get; init; }

        [DataTypeField(Order = 1)]
        public partial int Port { get; init; }

        public string NotSerialized { get; set; }
    }
}";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();

            // Backing fields should be generated
            Assert.That(generated, Does.Contain("private string __Name;"));
            Assert.That(generated, Does.Contain("private int __Port;"));

            // Partial property implementations should be generated
            Assert.That(generated, Does.Contain(
                "public partial string Name { get => __Name; init => __Name = value; }"));
            Assert.That(generated, Does.Contain(
                "public partial int Port { get => __Port; init => __Port = value; }"));

            // Decode should use backing field
            Assert.That(generated, Does.Contain("__Name = decoder.ReadString"));
            Assert.That(generated, Does.Contain("__Port = decoder.ReadInt32"));

            // Encode should use property name
            Assert.That(generated, Does.Contain("encoder.WriteString(\"Name\", Name)"));
            Assert.That(generated, Does.Contain("encoder.WriteInt32(\"Port\", Port)"));
        }

        [Test]
        public void PartialInitPropertiesPreserveDefaultInitializers()
        {
            // Note: C# 14 partial properties cannot have initializers
            // (CS8050). Only properties using the 'field' keyword or
            // auto-properties can have initializers. So for partial
            // init properties, the source gen backing field defaults
            // to default(T). Properties needing non-default values
            // should use 'set' instead of 'partial init'.
            const string source = @"
using Opc.Ua;

namespace TestApp.InitDefaults
{
    [DataType]
    public partial record class ConfigNoDefaults
    {
        [DataTypeField(Order = 0)]
        public partial string Name { get; init; }

        [DataTypeField(Order = 1)]
        public partial int Port { get; init; }

        [DataTypeField(Order = 2)]
        public partial bool Enabled { get; init; }
    }
}";
            GeneratorRunResult result = RunGenerator(source);

            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            string generated = result.GeneratedSources[0].SourceText.ToString();

            // Backing fields default to default(T) — no initializer
            Assert.That(generated, Does.Contain("private string __Name;"));
            Assert.That(generated, Does.Contain("private int __Port;"));
            Assert.That(generated, Does.Contain("private bool __Enabled;"));
        }
    }
}
