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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Api.Tests
{
    /// <summary>
    /// Test generating and compiling stack
    /// </summary>
    [TestFixture]
    [Category("Api")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    // [Parallelizable(ParallelScope.All)]
    public class GenerateStackTests
    {
        [DatapointSource]
        public OptimizationLevel[] OptimizationLevels = CompilerUtils.SupportedOptimizationLevels;

        [DatapointSource]
        public StackGenerationType[] GenerationTypes =
        [
            StackGenerationType.None,
            StackGenerationType.Stack,
            StackGenerationType.Models,
            StackGenerationType.All
        ];

        [Theory]
        public void GenerateStackTest(StackGenerationType generationType)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            GenerateStack(generationType, telemetry, out _);
        }

        [Test]
        public void EncodedTicketUsesByteStringSupertype()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            Dictionary<string, string> generatedText = GenerateStack(
                StackGenerationType.Models,
                telemetry,
                out _);
            string nodeStates = generatedText.Single(pair =>
                pair.Key.EndsWith(
                    "Opc.Ua.NodeStates.ex.g.cs",
                    System.StringComparison.Ordinal)).Value;
            const string declaration =
                "internal static global::Opc.Ua.DataTypeState CreateEncodedTicket(";
            int declarationIndex = nodeStates.IndexOf(
                declaration,
                System.StringComparison.Ordinal);

            Assert.That(declarationIndex, Is.GreaterThanOrEqualTo(0));
            int initializerEnd = System.Math.Min(
                declarationIndex + 1500,
                nodeStates.Length);
            string encodedTicketInitializer =
                nodeStates[declarationIndex..initializerEnd];
            Assert.That(
                encodedTicketInitializer,
                Does.Contain("state.SuperTypeId = global::Opc.Ua.NodeId.Create(15u,"));
        }

        /// <summary>
        /// The served DataTypeDictionary must describe the layout the generated
        /// Encode/Decode actually produces. OPC 10000-6 §5.2.7 assigns the first
        /// optional field bit '0', the second bit '1' and so on - counting
        /// optional fields only - and pads the mask to 32 bits. LogRecord is the
        /// standard model's structure with optional fields, and it interleaves
        /// them with mandatory ones, so it pins the bit order on both sides.
        /// </summary>
        [Test]
        public void OptionalFieldBitsAgreeBetweenBsdAndGeneratedEncoder()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            Dictionary<string, string> generatedText = GenerateStack(
                StackGenerationType.All,
                telemetry,
                out Dictionary<string, string> nonSourceCode);

            string bsd = nonSourceCode.Single(pair =>
                pair.Key.EndsWith(".Types.bsd", StringComparison.Ordinal)).Value;
            string dataTypes = generatedText.Single(pair =>
                pair.Key.EndsWith("Opc.Ua.DataTypes.g.cs", StringComparison.Ordinal)).Value;

            // The presence bits, in the order the binary schema declares them.
            string logRecord = Between(
                bsd,
                "<opc:StructuredType Name=\"LogRecord\"",
                "</opc:StructuredType>");
            var bsdBits = new List<string>();
            foreach (string line in logRecord.Split('\n'))
            {
                if (line.Contains("TypeName=\"opc:Bit\"", StringComparison.Ordinal) &&
                    !line.Contains("Name=\"Reserved", StringComparison.Ordinal))
                {
                    bsdBits.Add(Between(line, "Name=\"", "\"").Replace(
                        "Specified", string.Empty, StringComparison.Ordinal));
                }
            }

            string[] expectedBits =
            [
                "EventType", "SourceNode", "SourceName", "TraceContext", "AdditionalData"
            ];
            Assert.That(
                bsdBits,
                Is.EqualTo(expectedBits),
                "presence bits are the optional fields, in declaration order");

            // The mask is padded out to the 32 bits the encoding uses.
            Assert.That(
                logRecord,
                Does.Contain("<opc:Field Name=\"Reserved1\" TypeName=\"opc:Bit\" Length=\"27\" />"));

            // Every optional data field selects on its own presence bit.
            foreach (string name in bsdBits)
            {
                Assert.That(
                    logRecord,
                    Does.Contain($"SwitchField=\"{name}Specified\""),
                    $"'{name}' must select on its presence bit");
            }

            // And the generated encoder assigns those same bits, in that order.
            string maskEnum = Between(
                dataTypes, "public enum LogRecordFields : uint", "}");
            var encoderBits = new List<string>();
            foreach (string line in maskEnum.Split('\n'))
            {
                int at = line.IndexOf(" = 0x", StringComparison.Ordinal);
                if (at > 0)
                {
                    encoderBits.Add(line.Trim()[..line.Trim()
                        .IndexOf(" = 0x", StringComparison.Ordinal)]);
                }
            }

            Assert.That(
                encoderBits,
                Is.EqualTo(bsdBits),
                "the dictionary and the generated encoder must agree on the bit order");
            Assert.That(maskEnum, Does.Contain("EventType = 0x1"));
            Assert.That(maskEnum, Does.Contain("AdditionalData = 0x10"));
        }

        private static string Between(string source, string start, string end)
        {
            int at = source.IndexOf(start, StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0), $"'{start}' not found");
            at += start.Length;
            int to = source.IndexOf(end, at, StringComparison.Ordinal);
            Assert.That(to, Is.GreaterThanOrEqualTo(0), $"'{end}' not found");
            return source[at..to];
        }

        [Theory]
        public async Task GenerateAndCompileStackTestAsync(
            OptimizationLevel optimizationLevel,
            bool withAnalyzers,
            bool withNodeLoader)
        {
            // Generate
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            Dictionary<string, string> generatedText = GenerateStack(StackGenerationType.All, telemetry, out _);
            if (withNodeLoader)
            {
                AddPredefinedNodeLoader(generatedText);
            }

            // Parse and compile the generated code
            var sw = Stopwatch.StartNew();
            using var peStream = new MemoryStream();
            using var xmlStream = new MemoryStream();
            bool success = optimizationLevel
                .CreateCompilation()
                .AddCode(
                    generatedText.WithOpcUaCoreStubs(
                        includeBaseEventTypeRecord: false),
                    LanguageVersion.Latest) // Only support latest - internal use only
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

        [GlobalSetup(Target = nameof(GenerateToFile))]
        [GlobalCleanup(Target = nameof(GenerateToFile))]
#pragma warning disable NUnit1028 // The non-test method is public
        public void Setup()
#pragma warning restore NUnit1028 // The non-test method is public
        {
            try
            {
                Directory.Delete(Path.Combine(Directory.GetCurrentDirectory(), "Benchmark"), true);
            }
            catch
            {
                // Ignore
            }
        }

        [Benchmark]
#pragma warning disable NUnit1028 // The non-test method is public
        public void GenerateToFile()
#pragma warning restore NUnit1028 // The non-test method is public
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.CreateForBenchmarks(logLevel: LogLevel.Error);
            Generators.GenerateStack(
                StackGenerationType.All,
                LocalFileSystem.Instance,
                Path.Combine(Directory.GetCurrentDirectory(), "Benchmark"), telemetry);
        }

        [Benchmark]
#pragma warning disable NUnit1028 // The non-test method is public
        public void GenerateToMemory()
#pragma warning restore NUnit1028 // The non-test method is public
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.CreateForBenchmarks(logLevel: LogLevel.Error);
            GenerateStack(StackGenerationType.All, telemetry, out _);
        }

        [Benchmark]
        [Arguments(OptimizationLevel.Release)]
        [Arguments(OptimizationLevel.Debug)]
#pragma warning disable NUnit1028 // The non-test method is public
        public void GenerateAndComile(OptimizationLevel optimizationLevel)
#pragma warning restore NUnit1028 // The non-test method is public
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.CreateForBenchmarks(logLevel: LogLevel.Error);
            Dictionary<string, string> generatedText = GenerateStack(StackGenerationType.All, telemetry, out _);
            using var peStream = new MemoryStream();
            using var xmlStream = new MemoryStream();
            bool success = optimizationLevel
                .CreateCompilation("Opc.Ua.Test")
                .AddCode(
                    generatedText.WithOpcUaCoreStubs(
                        includeBaseEventTypeRecord: false),
                    LanguageVersion.Latest)
                .Emit(peStream, xmlDocumentationStream: xmlStream)
                .Check(TestContext.Out, out int errorCount, out int warnCount);
        }

        /// <summary>
        /// Generate stack code
        /// </summary>
#pragma warning disable NUnit1028 // The non-test method is public
        internal static Dictionary<string, string> GenerateStack(
#pragma warning restore NUnit1028 // The non-test method is public
            StackGenerationType generationType,
            ITelemetryContext telemetry,
            out Dictionary<string, string> nonSourceCode)
        {
            // Generate
            var sw = Stopwatch.StartNew();
            using var fileSystem = new VirtualFileSystem();
            // The test compilation provides Core stubs via
            // WithOpcUaCoreStubs() but no Opc.Ua.Server reference.
            // Suppress fluent-builder emission to keep the generated
            // code self-contained (mirrors model-only csproj
            // configuration in production).
            Generators.GenerateStack(generationType, fileSystem, string.Empty, telemetry,
                new GeneratorOptions
                {
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
