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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Schema.Binary;
using Opc.Ua.Schema.Xml;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Api.Tests
{
    /// <summary>
    /// Test generating and compiling model design
    /// </summary>
    [TestFixture]
    [Category("Api")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    // [Parallelizable(ParallelScope.All)]
    public class GenerateCodeTests
    {
        [DatapointSource]
        public OptimizationLevel[] OptimizationLevels = CompilerUtils.SupportedOptimizationLevels;

        [DatapointSource]
        public string[] ModelDesignFiles =
        [
            "TestDataDesign.xml"
#if TEST_ALL
            , "DemoModel.xml"
            , "TestModel.xml"
            , "OpcUaOnboardingModel.xml"
            , "OpcUaSchedulerModel.xml"
            , "DemoModel.json"
            , "TestDataDesign.json"
#endif
        ];

        /// <summary>
        /// Only support modern language versions
        /// </summary>
        [DatapointSource]
        public LanguageVersion[] LanguageVersions =
        [
            LanguageVersion.CSharp11
#if TEST_ALL_LANG_VERSIONS
            ,LanguageVersion.CSharp12
            ,LanguageVersion.CSharp13
         // ,LanguageVersion.CSharp14
#endif
        ];

        [Theory]
        [Pairwise]
        public async Task GenerateAndCompileDesignFileAsync(
            string modelDesignFile,
            OptimizationLevel optimizationLevel,
            LanguageVersion languageVersion,
            bool withAnalyzers,
            bool withNodeLoader)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            Dictionary<string, string> generatedText = GenerateCodeFromModel(
                modelDesignFile,
                languageVersion,
                telemetry,
                out Dictionary<string, string> generatedOther);
            Dictionary<string, string> generatedTextStack = GenerateStackTests.GenerateStack(
                StackGenerationType.Models,
                telemetry,
                out Dictionary<string, string> generatedStackOther);
            foreach (KeyValuePair<string, string> item in generatedTextStack)
            {
                generatedText.Add(item.Key, item.Value);
            }
            if (withNodeLoader)
            {
                AddPredefinedNodeLoader(generatedText);
            }

            var xmlSchemas = generatedOther
                .Concat(generatedStackOther)
                .Where(c => Path.GetExtension(c.Key) == ".xsd")
                .Select(c => c.Value)
                .ToList();
            Assert.That(xmlSchemas, Has.Count.EqualTo(2));

            // Validate xsd schemas
            using (var xsd = new MemoryStream(Encoding.UTF8.GetBytes(xmlSchemas[0])))
            {
                var xmlValidator = new XmlSchemaValidator(new Dictionary<string, byte[]>
                {
                    ["http://opcfoundation.org/UA/2008/02/Types.xsd"] = Encoding.UTF8.GetBytes(xmlSchemas[1])
                });
                xmlValidator.Validate(xsd, telemetry.LoggerFactory.CreateLogger<XmlSchemaValidator>());
            }

            var binSchemas = generatedOther
                .Concat(generatedStackOther)
                .Where(c => Path.GetExtension(c.Key) == ".bsd")
                .Select(c => c.Value)
                .ToList();
            Assert.That(binSchemas, Has.Count.EqualTo(2));

            // Validate binary schema
            using (var bsd = new MemoryStream(Encoding.UTF8.GetBytes(binSchemas[0])))
            {
                var binValidator = new BinarySchemaValidator(new Dictionary<string, byte[]>
                {
                    ["http://opcfoundation.org/UA/"] = Encoding.UTF8.GetBytes(binSchemas[1])
                });
                binValidator.Validate(bsd);
            }

            // Parse and compile the generated code
            var sw = Stopwatch.StartNew();
            using var peStream = new MemoryStream();
            using var xmlStream = new MemoryStream();
            bool success = optimizationLevel
                .CreateCompilation()
                .AddCode(generatedText.WithOpcUaCoreStubs(), languageVersion)
                .WithAnalyzers(withAnalyzers, out CompilationWithAnalyzers compilationWithAnalyzers)
                .Emit(peStream, xmlDocumentationStream: xmlStream)
                .Check(TestContext.Out, out int errorCount, out int warnCount);
            TestContext.Out.WriteLine("Compilation completed in {0} ms", sw.ElapsedMilliseconds);
            if (withAnalyzers)
            {
                if (compilationWithAnalyzers == null)
                {
                    Assert.Ignore("Setup does not support analyzer testing");
                }
                AnalysisResult analysisResults =
                    await compilationWithAnalyzers.GetAnalysisResultAsync(default).ConfigureAwait(false);
                analysisResults.GetAllDiagnostics().Check(TestContext.Out,
                    out int analyzerErrors,
                    out int analzyerWarnings);
                Assert.That(analyzerErrors, Is.Zero, $"Analyzers produced {analyzerErrors} errors");
                TestContext.Out.WriteLine($"Analyzers produced {analzyerWarnings} warnings");
            }
            Assert.That(
                success,
                Is.True,
                $"Compilation failed with {errorCount} errors and {warnCount} warnings.");
            xmlStream.Position = 0;
            var xmlDoc = XDocument.Load(xmlStream);
            Assert.That(xmlDoc, Is.Not.Null);
        }

        [Theory]
        public void GenerateFromDesignFileWithCsharp8(string modelDesignFile)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            Dictionary<string, string> generatedText = GenerateCodeFromModel(
                modelDesignFile,
                LanguageVersion.CSharp8,
                telemetry,
                out _);
            Assert.That(generatedText, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies that DataType structure fields with
        /// <c>ValueRank="OneOrMoreDimensions"</c> are generated as typed
        /// <c>MatrixOf&lt;T&gt;</c> properties and that the encode/decode
        /// pipeline uses the appropriate typed call: dedicated
        /// <c>WriteEncodeableMatrix</c> / <c>ReadEncodeableMatrix</c> for
        /// concrete encodeable matrices and <c>WriteVariant</c> wrapped via
        /// <c>Variant.From</c> together with the matching
        /// <c>Variant.GetXxxMatrix</c> getters for everything else.
        /// </summary>
        [Test]
        public void GenerateMatrixValueDataType_EmitsMatrixOfPropertiesAndCalls()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            Dictionary<string, string> generatedText = GenerateCodeFromModel(
                "TestDataDesign.xml",
                LanguageVersion.CSharp11,
                telemetry,
                out _);

            // Concatenate every generated source file so the snippet match
            // is resilient to whichever file the generator chooses to put
            // the type in (each pipeline groups types differently).
            string code = string.Join("\n", generatedText.Values);

            Assert.That(code, Does.Contain("class MatrixValueDataType"),
                "Expected MatrixValueDataType to be generated.");

            // Property type assertions - each matrix field must render as a
            // typed MatrixOf<T> in the public surface.
            Assert.Multiple(() =>
            {
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.MatrixOf<bool> BooleanMatrix"));
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.MatrixOf<int> Int32Matrix"));
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.MatrixOf<string> StringMatrix"));
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.MatrixOf<global::Opc.Ua.NodeId> NodeIdMatrix"));
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.MatrixOf<global::Opc.Ua.Variant> VariantMatrix"));
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.MatrixOf<global::Opc.Ua.ExtensionObject> ExtensionObjectMatrix"));
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.MatrixOf<global::TestData.Vector> VectorMatrix"));
                // AllowSubTypes structure fields collapse to ExtensionObject
                // during DataTypeNode resolution, so the property emits the
                // matrix-of-extension-object surface even though the design
                // declared the element type as Vector.
                Assert.That(code, Does.Contain(
                    "global::Opc.Ua.MatrixOf<global::Opc.Ua.ExtensionObject> AbstractVectorMatrix"));
            });

            // Encode assertions.
            Assert.Multiple(() =>
            {
                // Concrete encodeable matrix uses the typed
                // WriteEncodeableMatrix overload.
                Assert.That(code, Does.Contain(
                    """encoder.WriteEncodeableMatrix("VectorMatrix", VectorMatrix);"""));
                // Primitive matrices use Variant.From.
                Assert.That(code, Does.Contain(
                    """encoder.WriteVariant("BooleanMatrix", global::Opc.Ua.Variant.From(BooleanMatrix));"""));
                Assert.That(code, Does.Contain(
                    """encoder.WriteVariant("Int32Matrix", global::Opc.Ua.Variant.From(Int32Matrix));"""));
                Assert.That(code, Does.Contain(
                    """encoder.WriteVariant("StringMatrix", global::Opc.Ua.Variant.From(StringMatrix));"""));
                Assert.That(code, Does.Contain(
                    """encoder.WriteVariant("NodeIdMatrix", global::Opc.Ua.Variant.From(NodeIdMatrix));"""));
                Assert.That(code, Does.Contain(
                    """encoder.WriteVariant("VariantMatrix", global::Opc.Ua.Variant.From(VariantMatrix));"""));
                Assert.That(code, Does.Contain(
                    """encoder.WriteVariant("ExtensionObjectMatrix", global::Opc.Ua.Variant.From(ExtensionObjectMatrix));"""));
                // AllowSubTypes -> field resolves to ExtensionObject, so it
                // takes the same Variant.From(MatrixOf<ExtensionObject>) path
                // as the explicit Structure field above.
                Assert.That(code, Does.Contain(
                    """encoder.WriteVariant("AbstractVectorMatrix", global::Opc.Ua.Variant.From(AbstractVectorMatrix));"""));
            });

            // Decode assertions.
            Assert.Multiple(() =>
            {
                Assert.That(code, Does.Contain(
                    """VectorMatrix = decoder.ReadEncodeableMatrix<global::TestData.Vector>("VectorMatrix");"""));
                Assert.That(code, Does.Contain(
                    """BooleanMatrix = decoder.ReadVariant("BooleanMatrix").GetBooleanMatrix();"""));
                Assert.That(code, Does.Contain(
                    """Int32Matrix = decoder.ReadVariant("Int32Matrix").GetInt32Matrix();"""));
                Assert.That(code, Does.Contain(
                    """StringMatrix = decoder.ReadVariant("StringMatrix").GetStringMatrix();"""));
                Assert.That(code, Does.Contain(
                    """NodeIdMatrix = decoder.ReadVariant("NodeIdMatrix").GetNodeIdMatrix();"""));
                Assert.That(code, Does.Contain(
                    """VariantMatrix = decoder.ReadVariant("VariantMatrix").GetVariantMatrix();"""));
                Assert.That(code, Does.Contain(
                    """ExtensionObjectMatrix = decoder.ReadVariant("ExtensionObjectMatrix").GetExtensionObjectMatrix();"""));
                Assert.That(code, Does.Contain(
                    """AbstractVectorMatrix = decoder.ReadVariant("AbstractVectorMatrix").GetExtensionObjectMatrix();"""));
            });
        }

        private static Dictionary<string, string> GenerateCodeFromModel(
            string modelDesignFile,
            LanguageVersion languageVersion,
            ITelemetryContext telemetry,
            out Dictionary<string, string> nonSourceCode)
        {
            // Generate
            var sw = Stopwatch.StartNew();
            using var fileSystem = new VirtualFileSystem();
            Generators.GenerateCode(new DesignFileCollection
            {
                Targets = [Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Resources",
                    modelDesignFile)],
                IdentifierFilePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Resources",
                    Path.GetFileNameWithoutExtension(modelDesignFile) + ".csv"),
                Options = new DesignFileOptions()
            }, fileSystem, string.Empty, telemetry, new GeneratorOptions
            {
                UseUtf8StringLiterals = languageVersion >= LanguageVersion.CSharp11,
                // The test compilation provides Core stubs via
                // WithOpcUaCoreStubs() but no Opc.Ua.Server reference.
                // Suppress fluent-builder emission to keep the generated
                // code self-contained (mirrors model-only csproj
                // configuration in production: Opc.Ua.Di,
                // Opc.Ua.Gds.Common, Opc.Ua.WotCon all opt out via
                // ModelSourceGeneratorOmitFluentApi=true).
                OmitFluentApi = true
            });

            var generatedText = fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) == ".cs")
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
            TestContext.Out.WriteLine("Generation completed in {0} ms", sw.ElapsedMilliseconds);
            Assert.That(generatedText.Values, Is.All.StartsWith("// <auto-generated />"));

            nonSourceCode = fileSystem.CreatedFiles
                .Where(c => Path.GetExtension(c) != ".cs")
                .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
            return generatedText;
        }

        private static void AddPredefinedNodeLoader(Dictionary<string, string> generated)
        {
            generated.Add("Test.cs",
                """
                namespace Opc.Ua
                {
                    public static partial class LoadingTestData
                    {
                        public static NodeStateCollection Load()
                        {
                            // Use predefined nodes
                            return new NodeStateCollection().AddOpcUa(new SystemContext(null));
                        }
                    }
                }
                """);
        }
    }
}
