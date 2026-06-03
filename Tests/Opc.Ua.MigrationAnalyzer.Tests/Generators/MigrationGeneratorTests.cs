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
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Opc.Ua.MigrationAnalyzer.Generator;

namespace Opc.Ua.MigrationAnalyzer.Tests.Generators
{
    /// <summary>
    /// Verifies <see cref="MigrationGenerator"/> emits one
    /// <c>internal sealed [Obsolete] class &lt;Name&gt;Collection : List&lt;TElement&gt;</c>
    /// per uniquely-referenced legacy wrapper, falls back to semantic lookup for
    /// model-compiled element types, and surfaces <c>MIG01</c> for unresolvable
    /// references.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    public sealed class MigrationGeneratorTests
    {
        private static readonly ImmutableArray<MetadataReference> s_baseReferences = BuildBaseReferences();

        private static ImmutableArray<MetadataReference> BuildBaseReferences()
        {
            // On .NET Core / .NET 5+, the runtime exposes the full set of trusted
            // platform assemblies via AppContext.
            string trustedAssemblies = (string?)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            string[] tpaList = trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
            if (tpaList.Length > 0)
            {
                return [.. tpaList.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
            }
            // On .NET Framework, TPA is not exposed. Fall back to scanning the
            // runtime directory for BCL assemblies (mscorlib, System.*) so that
            // System.Int32 et al. are available for generator semantic lookups.
            string runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (string.IsNullOrEmpty(runtimeDir) || !Directory.Exists(runtimeDir))
            {
                return [];
            }
            return [.. Directory.EnumerateFiles(runtimeDir, "*.dll")
                .Where(IsBclAssembly)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))];
        }

        private static bool IsBclAssembly(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.Equals("mscorlib", System.StringComparison.OrdinalIgnoreCase)
                || name.Equals("netstandard", System.StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("System", System.StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Microsoft.CSharp", System.StringComparison.OrdinalIgnoreCase);
        }

        private static GeneratorDriverRunResult Run(string userSource, string? extraSource = null)
        {
            CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);

            List<SyntaxTree> trees = new()
            {
                CSharpSyntaxTree.ParseText(MinimalOpcUaStubs.Source, parseOptions, "OpcUaStubs.cs"),
                CSharpSyntaxTree.ParseText(userSource, parseOptions, "Consumer.cs"),
            };
            if (extraSource is not null)
            {
                trees.Add(CSharpSyntaxTree.ParseText(extraSource, parseOptions, "Extra.cs"));
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "GeneratorTestAssembly",
                trees,
                s_baseReferences,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new MigrationGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out _,
                out _);
            return driver.GetRunResult();
        }

        private static IEnumerable<TestCaseData> WellKnownOverridesCases()
        {
            // Catalog override entries: each one is an element-type RENAME the
            // semantic-lookup fallback couldn't infer correctly. Everything else
            // (primitives, built-in unrenamed types, model-compiled user types)
            // resolves via semantic lookup, exercised by separate test cases below.
            foreach (KeyValuePair<string, string> entry in CollectionShimCatalog.WellKnownOverrides)
            {
                yield return new TestCaseData(entry.Key, entry.Value).SetName($"WellKnown_{entry.Key}");
            }
        }

        [TestCaseSource(nameof(WellKnownOverridesCases))]
        public void WellKnownOverrideEmitsExpectedShimType(string shortName, string elementDisplay)
        {
            string user = $$"""
                using Opc.Ua;
                public static class Use
                {
                    public static void M({{shortName}} arg) { }
                }
                """;

            GeneratorDriverRunResult result = Run(user);
            GeneratedSourceResult? generated = result.Results
                .SelectMany(r => r.GeneratedSources)
                .Cast<GeneratedSourceResult?>()
                .FirstOrDefault(s => s!.Value.HintName == $"{shortName}.g.cs");

            Assert.That(generated, Is.Not.Null, $"No generated file for '{shortName}.g.cs'");
            string text = generated!.Value.SourceText.ToString();
            Assert.That(text, Does.Contain("namespace Opc.Ua"));
            Assert.That(text, Does.Contain($"internal sealed class {shortName} : global::System.Collections.Generic.List<{elementDisplay}>"));
            Assert.That(text, Does.Contain("[global::System.Obsolete("));
            Assert.That(text, Does.Contain("(UA0002)"));
            Assert.That(text, Does.Contain($"implicit operator global::Opc.Ua.ArrayOf<{elementDisplay}>"));
        }

        [Test]
        public void ModelCompiledUserTypeResolvesViaSemanticLookup()
        {
            string user = """
                using Acme;
                using Opc.Ua;
                public static class Use
                {
                    public static void M(WaterPumpCollection arg) { }
                }
                """;
            string extra = """
                namespace Acme
                {
                    public sealed class WaterPump { }
                }
                """;

            GeneratorDriverRunResult result = Run(user, extra);
            GeneratedSourceResult? generated = result.Results
                .SelectMany(r => r.GeneratedSources)
                .Cast<GeneratedSourceResult?>()
                .FirstOrDefault(s => s!.Value.HintName == "WaterPumpCollection.g.cs");

            Assert.That(generated, Is.Not.Null);
            string text = generated!.Value.SourceText.ToString();
            Assert.That(text, Does.Contain("internal sealed class WaterPumpCollection : global::System.Collections.Generic.List<global::Acme.WaterPump>"));
        }

        [Test]
        public void BuiltInUnrenamedTypeResolvesViaSemanticLookup()
        {
            // NodeIdCollection is NOT in CollectionShimCatalog any more because
            // the bare short name 'NodeId' resolves uniquely to Opc.Ua.NodeId via
            // semantic lookup. Verify the fall-through path emits the right shim.
            string user = """
                using Opc.Ua;
                public static class Use
                {
                    public static NodeIdCollection BuildNodes() => new NodeIdCollection();
                }
                """;

            GeneratorDriverRunResult result = Run(user);
            GeneratedSourceResult? generated = result.Results
                .SelectMany(r => r.GeneratedSources)
                .Cast<GeneratedSourceResult?>()
                .FirstOrDefault(s => s!.Value.HintName == "NodeIdCollection.g.cs");

            Assert.That(generated, Is.Not.Null, "Semantic-lookup fallback must emit NodeIdCollection");
            string text = generated!.Value.SourceText.ToString();
            Assert.That(text, Does.Contain("internal sealed class NodeIdCollection : global::System.Collections.Generic.List<global::Opc.Ua.NodeId>"));
        }

        [Test]
        public void PrimitiveTypedCollectionResolvesViaSemanticLookup()
        {
            // Int32Collection is NOT in CollectionShimCatalog any more because
            // the bare short name 'Int32' resolves uniquely to System.Int32 via
            // semantic lookup. The generated element type is global::System.Int32
            // (equivalent to 'int', just more verbose) — confirm the path works.
            string user = """
                using Opc.Ua;
                public static class Use
                {
                    public static Int32Collection BuildInts() => new Int32Collection { 1, 2, 3 };
                }
                """;

            GeneratorDriverRunResult result = Run(user);
            GeneratedSourceResult? generated = result.Results
                .SelectMany(r => r.GeneratedSources)
                .Cast<GeneratedSourceResult?>()
                .FirstOrDefault(s => s!.Value.HintName == "Int32Collection.g.cs");

            Assert.That(generated, Is.Not.Null, "Semantic-lookup fallback must emit Int32Collection");
            string text = generated!.Value.SourceText.ToString();
            // FullyQualifiedFormat uses C# keyword aliases for primitives, so
            // System.Int32 is rendered as `int` — that's intentional, the emitted
            // shim is consumer-facing and reads more naturally with the alias.
            Assert.That(text, Does.Contain("internal sealed class Int32Collection : global::System.Collections.Generic.List<int>"));
        }

        [Test]
        public void ExistingTypeProducesNoShim()
        {
            // Consumer already declares a type called 'AlreadyDefinedCollection';
            // the generator must NOT emit a shim for it.
            string user = """
                public sealed class AlreadyDefinedCollection { }
                public static class Use
                {
                    public static void M(AlreadyDefinedCollection arg) { }
                }
                """;

            GeneratorDriverRunResult result = Run(user);
            bool emitted = result.Results
                .SelectMany(r => r.GeneratedSources)
                .Any(s => s.HintName == "AlreadyDefinedCollection.g.cs");
            Assert.That(emitted, Is.False);
        }

        [Test]
        public void UnresolvableElementTypeReportsMig01AndEmitsNothing()
        {
            string user = """
                using Opc.Ua;
                public static class Use
                {
                    public static void M(NeverDefinedCollection arg) { }
                }
                """;

            GeneratorDriverRunResult result = Run(user);
            bool emitted = result.Results
                .SelectMany(r => r.GeneratedSources)
                .Any(s => s.HintName == "NeverDefinedCollection.g.cs");
            Assert.That(emitted, Is.False, "MIG01 path must not emit a shim type");

            ImmutableArray<Diagnostic> diagnostics = result.Diagnostics;
            Assert.That(diagnostics.Any(d => d.Id == "MIG01"), Is.True,
                $"Expected MIG01 diagnostic; saw: {string.Join(", ", diagnostics.Select(d => d.Id))}");
        }

        [Test]
        public void MultipleReferencesProduceSingleDeduplicatedEmission()
        {
            string user = """
                using Opc.Ua;
                public static class Use1
                {
                    public static Int32Collection First() => new();
                    public static void Second(Int32Collection arg) { }
                    public static System.Type Third() => typeof(Int32Collection);
                }
                """;

            GeneratorDriverRunResult result = Run(user);
            int count = result.Results
                .SelectMany(r => r.GeneratedSources)
                .Count(s => s.HintName == "Int32Collection.g.cs");
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void NonCollectionIdentifiersIgnored()
        {
            // Identifier that ends in "Collection" but isn't in a type position
            // (variable name) must not trigger emission or diagnostics.
            string user = """
                using System.Collections.Generic;
                public static class Use
                {
                    public static void M()
                    {
                        var fooCollection = new List<int>();
                    }
                }
                """;

            GeneratorDriverRunResult result = Run(user);
            Assert.That(result.GeneratedTrees, Is.Empty);
            Assert.That(result.Diagnostics, Is.Empty);
        }

        [Test]
        public void NoReferencesProducesNoEmission()
        {
            string user = """
                public static class Use
                {
                    public static int M() => 42;
                }
                """;

            GeneratorDriverRunResult result = Run(user);
            Assert.That(result.GeneratedTrees, Is.Empty);
            Assert.That(result.Diagnostics, Is.Empty);
        }

        /// <summary>
        /// Minimal stub surface that defines just enough of Opc.Ua to exercise the
        /// generator: an <c>ArrayOf&lt;T&gt;</c> type with a <c>ToArrayOf()</c>
        /// extension so the generated implicit conversion body compiles, and the
        /// handful of 2.0-only element types referenced by the catalog overrides.
        /// </summary>
        private static class MinimalOpcUaStubs
        {
            public const string Source = """
                #nullable enable
                using System.Collections.Generic;

                namespace Opc.Ua
                {
                    public readonly struct ArrayOf<T>
                    {
                        public static ArrayOf<T> Empty => default;
                    }

                    public static class ArrayOfExtensions
                    {
                        public static ArrayOf<T> ToArrayOf<T>(this IEnumerable<T> source) => default;
                    }

                    public readonly struct DateTimeUtc { }
                    public readonly struct Uuid { }
                    public readonly struct ByteString { }
                    public readonly struct NodeId { }
                    public readonly struct ExpandedNodeId { }
                    public readonly struct QualifiedName { }
                    public readonly struct LocalizedText { }
                    public readonly struct StatusCode { }
                    public readonly struct Variant { }
                    public readonly struct DiagnosticInfo { }
                    public readonly struct DataValue { }
                    public readonly struct ExtensionObject { }
                    public sealed class Argument { }
                    public sealed class ServerSecurityPolicy { }
                    public sealed class TransportConfiguration { }
                    public sealed class ReverseConnectClient { }
                }
                """;
        }
    }
}
