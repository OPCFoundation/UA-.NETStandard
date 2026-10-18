/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Opc.Ua.SourceGeneration.Dependency;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Verifies opt-in client extensions against a separately compiled model and the real client runtime.
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ClientAccessorGeneratorTests
    {
        /// <summary>
        /// Referencing an existing model does not implicitly opt a consumer into client extension emission.
        /// </summary>
        [Test]
        public void ClientAccessorsAreDisabledByDefault()
        {
            (GeneratorRunResult result, Compilation output, ImmutableArray<Diagnostic> diagnostics) =
                RunConsumer(enabled: false);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(result.GeneratedSources, Is.Empty);
            Assert.That(output.GetTypeByMetadataName("Example.Model.Client.ClientAccessorsClientMembers"), Is.Null);
        }

        /// <summary>
        /// Typed codecs and repeated-object factories compile without re-emitting the referenced model.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ClientAccessorsCompileAgainstReferencedModels(bool customizeNames)
        {
            var options = new Dictionary<string, string>();
            string fullName = "Example.Model.Client.ClientAccessorsClientMembers";
            if (customizeNames)
            {
                options["build_property.ModelSourceGeneratorClientAccessorNamespace"] = "Example.Views";
                options["build_property.ModelSourceGeneratorClientAccessorClassName"] = "DeviceMembers";
                fullName = "Example.Views.DeviceMembers";
            }
            (GeneratorRunResult result, Compilation output, ImmutableArray<Diagnostic> diagnostics) =
                RunConsumer(options: options);

            Assert.That(diagnostics, Is.Empty, string.Join(Environment.NewLine, diagnostics));
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            Assert.That(result.GeneratedSources[0].HintName, Does.EndWith(".ClientMembers.g.cs"));
            AssertCompiles(output);
            INamedTypeSymbol accessors = output.GetTypeByMetadataName(fullName)!;
            Assert.That(accessors, Is.Not.Null);
            IMethodSymbol[] methods = accessors.GetMembers().OfType<IMethodSymbol>().ToArray();
            Assert.That(methods.Select(method => method.Name), Is.EquivalentTo(s_expectedMembers));
            Assert.Multiple(() =>
            {
                Assert.That(ReturnType("ActiveVariable"), Is.EqualTo("Opc.Ua.Client.ClientVariable<bool>"));
                Assert.That(ReturnType("SamplesVariable"),
                    Is.EqualTo("Opc.Ua.Client.ClientVariable<Opc.Ua.ArrayOf<double>>"));
                Assert.That(ReturnType("ZoneVariable"),
                    Is.EqualTo("Opc.Ua.Client.ClientVariable<Opc.Ua.TimeZoneDataType>"));
                Assert.That(ReturnType("KindVariable"), Is.EqualTo("Opc.Ua.Client.ClientVariable<Opc.Ua.NodeClass>"));
                Assert.That(ReturnType("AnyValueVariable"),
                    Is.EqualTo("Opc.Ua.Client.ClientVariable<Opc.Ua.Variant>"));
                Assert.That(ReturnType("EnumeratePartAsync"),
                    Is.EqualTo("System.Collections.Generic.IAsyncEnumerable<Opc.Ua.BaseObjectTypeClient>"));
                Assert.That(methods.All(method => method.IsExtensionMethod), Is.True);
                Assert.That(methods.All(method =>
                    method.Parameters[0].Type.ToDisplayString() == "Example.Model.AccessorDeviceTypeClient"), Is.True);
            });
            string source = result.GeneratedSources[0].SourceText.ToString();
            Assert.That(source, Does.Contain("\"http://opcfoundation.org/UA/\", \"Axis 1\", true"));

            string ReturnType(string name) =>
                methods.Single(method => method.Name == name).ReturnType.ToDisplayString();
        }

        /// <summary>
        /// Client-only mode must not fall back to producing another copy of an absent or differently named model.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ClientAccessorsRequireAMatchingModelProducer(bool wrongPrefix)
        {
            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) = RunConsumer(
                referenceProducer: wrongPrefix, prefix: wrongPrefix ? "Wrong.Model" : null);

            Assert.That(diagnostics.Any(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage(CultureInfo.InvariantCulture)
                    .Contains("requires a referenced producer", StringComparison.Ordinal)), Is.True);
            Assert.That(result.GeneratedSources, Is.Empty);
        }

        /// <summary>
        /// Two incompatible accessor-only modes are refused explicitly.
        /// </summary>
        [TestCase("FluentAccessorsOnly")]
        [TestCase("GenerateNodeManager")]
        public void ClientAccessorsRejectIncompatibleModes(string mode)
        {
            (GeneratorRunResult result, _, ImmutableArray<Diagnostic> diagnostics) = RunConsumer(options: new()
            {
                ["build_property.ModelSourceGenerator" + mode] = "true"
            });

            Assert.That(diagnostics.Any(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage(CultureInfo.InvariantCulture)
                    .Contains("cannot be combined", StringComparison.Ordinal)), Is.True);
            Assert.That(result.GeneratedSources, Is.Empty);
        }

        /// <summary>
        /// ModelDesign inputs use the same referenced-model path and fully qualified runtime names.
        /// </summary>
        [Test]
        public void ClientAccessorsSupportModelDesignInputs()
        {
            CSharpCompilation producer = ModelDependencyScannerTests.CreateStackCompilation("DesignProducer");
            (GeneratorRunResult _, Compilation model, ImmutableArray<Diagnostic> modelDiagnostics) =
                RunDesign(producer, false);
            Assert.That(modelDiagnostics, Is.Empty, string.Join(Environment.NewLine, modelDiagnostics));
            AssertCompiles(model);
            CSharpCompilation consumer = ModelDependencyScannerTests.CreateStackCompilation("DesignConsumer")
                .AddReferences(model.ToMetadataReference());
            consumer = consumer.AddReferences(ClientReference(consumer));

            (GeneratorRunResult result, Compilation output, ImmutableArray<Diagnostic> diagnostics) =
                RunDesign(consumer, true);

            Assert.That(diagnostics, Is.Empty, string.Join(Environment.NewLine, diagnostics));
            Assert.That(result.GeneratedSources, Has.Length.EqualTo(1));
            Assert.That(result.GeneratedSources[0].HintName, Does.EndWith(".ClientMembers.g.cs"));
            AssertCompiles(output);
            INamedTypeSymbol accessors = output.GetTypeByMetadataName(
                "Acme.Opc.Ua.Pumps.Client.NestedOpcPrefixClientMembers")!;
            Assert.That(accessors, Is.Not.Null);
            Assert.That(accessors.GetMembers().OfType<IMethodSymbol>().Select(method => method.Name),
                Is.EquivalentTo(s_designMembers));
        }

        /// <summary>
        /// Referencing Core carries its Programs dependency without claiming ownership of its payload or types.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedProducerKeepsReferencedProgramsPayloadOwnedByCore(bool modelDesign)
        {
            const string programsUri = "http://opcfoundation.org/UA/SDK/Programs/";
            CSharpCompilation producer;
            if (modelDesign)
            {
                (_, Compilation model, ImmutableArray<Diagnostic> diagnostics) =
                    RunDesign(ModelDependencyScannerTests.CreateStackCompilation("DesignProducer"), false);
                Assert.That(diagnostics, Is.Empty, string.Join(Environment.NewLine, diagnostics));
                AssertCompiles(model);
                producer = (CSharpCompilation)model;
            }
            else
            {
                producer = s_producer.Value;
            }
            CSharpCompilation probe = ModelDependencyScannerTests.CreateStackCompilation("OwnershipProbe")
                .AddReferences(producer.ToMetadataReference());
            ModelDependencyReference[] programs = [.. ReferencedModelDependencyScanner.Scan(probe)
                .Where(reference => reference.ModelUri == programsUri)];
            Assert.That(programs, Has.Length.EqualTo(2));
            ModelDependencyReference owner = programs.Single(reference => reference.AssemblyName == "Opc.Ua.Core");
            ModelDependencyReference dependency =
                programs.Single(reference => reference.AssemblyName == producer.AssemblyName);
            ModelDependencyV1? payload = owner.GetDependency();
            Assert.That(payload, Is.Not.Null, "The normal reference layout must contain the current Programs model.");
            Assert.Multiple(() =>
            {
                Assert.That(payload!.ModelUri, Is.EqualTo(programsUri));
                Assert.That(payload.Nodes.Any(node => node.SymbolicName == "ManagedProgramType"), Is.True);
                Assert.That(dependency.Payload, Is.Empty);
                Assert.That(dependency.GetDependency(), Is.Null);
                Assert.That(dependency.Prefix, Is.EqualTo(owner.Prefix));
                Assert.That(dependency.Version, Is.EqualTo(owner.Version));
                Assert.That(dependency.PublicationDate, Is.EqualTo(owner.PublicationDate));
                Assert.That(dependency.Name, Is.EqualTo(owner.Name));
                Assert.That(producer.Assembly.GetTypeByMetadataName("Opc.Ua.Programs.ManagedProgramState"), Is.Null);
                Assert.That(producer.Assembly.GetTypeByMetadataName(
                    modelDesign ? "Acme.Opc.Ua.Pumps.PumpState" : "Example.Model.AccessorDeviceState"), Is.Not.Null);
            });
            TestContext.Out.WriteLine(
                $"Programs owner={owner.AssemblyName}, payloadBytes={owner.Payload.Length}; " +
                $"dependency={dependency.AssemblyName}, payloadBytes={dependency.Payload.Length}.");
        }

        /// <summary>
        /// Uses the existing nontrivial design fixture, including a namespace containing Opc.
        /// </summary>
        private static (GeneratorRunResult Result, Compilation Output, ImmutableArray<Diagnostic> Diagnostics)
            RunDesign(CSharpCompilation compilation, bool clientOnly)
        {
            var options = new AnalyzerOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true",
                ["build_property.ModelSourceGeneratorClientAccessorsOnly"] = clientOnly ? "true" : "false"
            });
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp13))
                .WithUpdatedAnalyzerConfigOptions(options)
                .AddAdditionalTexts([EmbeddedText.From("NestedOpcPrefix.ModelDesign.xml")]);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
            return (driver.GetRunResult().Results[0], output, diagnostics);
        }

        /// <summary>
        /// Generates a consumer using the existing compiler harness and a real client assembly reference.
        /// </summary>
        private static (GeneratorRunResult Result, Compilation Output, ImmutableArray<Diagnostic> Diagnostics)
            RunConsumer(
                bool enabled = true,
                bool referenceProducer = true,
                string? prefix = null,
                Dictionary<string, string>? options = null)
        {
            CSharpCompilation consumer = ModelDependencyScannerTests.CreateStackCompilation("ClientAccessorConsumer");
            if (referenceProducer)
            {
                consumer = consumer.AddReferences(s_producer.Value.ToMetadataReference());
            }
            consumer = consumer.AddReferences(ClientReference(consumer));
            options ??= [];
            if (enabled)
            {
                options["build_property.ModelSourceGeneratorClientAccessorsOnly"] = "true";
            }
            return Run(consumer, options, prefix);
        }

        /// <summary>
        /// Resolves the actual client runtime built for the same configuration and target as the compiler harness.
        /// </summary>
        private static PortableExecutableReference ClientReference(Compilation compilation)
        {
            MetadataReference core = compilation.References.First(reference =>
                Path.GetFileName(reference.Display) == "Opc.Ua.Core.dll");
            var output = new DirectoryInfo(Path.GetDirectoryName(core.Display)!);
            string client = Path.GetFullPath(Path.Combine(
                output.FullName, "..", "..", "..", "..", "Opc.Ua.Client", "bin",
                output.Parent!.Name, output.Name, "Opc.Ua.Client.dll"));
            return MetadataReference.CreateFromFile(client);
        }

        /// <summary>
        /// Creates the separately emitted model, including its standard proxy and dependency metadata.
        /// </summary>
        private static CSharpCompilation CreateProducer()
        {
            CSharpCompilation compilation =
                ModelDependencyScannerTests.CreateStackCompilation("ClientAccessorProducer");
            (_, Compilation output, ImmutableArray<Diagnostic> diagnostics) = Run(compilation, new()
            {
                ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true",
                ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
            });
            Assert.That(diagnostics, Is.Empty, string.Join(Environment.NewLine, diagnostics));
            AssertCompiles(output);
            return (CSharpCompilation)output;
        }

        /// <summary>
        /// Runs the real incremental generator with explicit source identity.
        /// </summary>
        private static (GeneratorRunResult Result, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) Run(
            CSharpCompilation compilation,
            Dictionary<string, string> settings,
            string? prefix = null)
        {
            var options = new AnalyzerOptionsProvider(settings);
            options.TextOptions["ClientAccessors.NodeSet2.xml"] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] = "urn:test:client-accessors",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "ClientAccessors",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] = prefix ?? "Example.Model"
            };
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp13))
                .WithUpdatedAnalyzerConfigOptions(options)
                .AddAdditionalTexts([EmbeddedText.Create("ClientAccessors.NodeSet2.xml", kModel)]);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
            return (driver.GetRunResult().Results[0], output, diagnostics);
        }

        /// <summary>
        /// Checks both binding diagnostics and assembly emission rather than source-text presence alone.
        /// </summary>
        private static void AssertCompiles(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        }

        /// <summary>The model producer shared by the independent consumer cases.</summary>
        private static readonly Lazy<CSharpCompilation> s_producer = new(CreateProducer);

        /// <summary>The complete direct extension surface of the synthetic model.</summary>
        private static readonly string[] s_expectedMembers =
        [
            "ActiveVariable", "SamplesVariable", "ZoneVariable", "KindVariable",
            "AnyValueVariable", "Axis1Variable", "EnumeratePartAsync"
        ];

        /// <summary>The direct ObjectType members of the shared ModelDesign fixture.</summary>
        private static readonly string[] s_designMembers = ["FlowVariable", "SettingsVariable"];

        /// <summary>A synthetic model with scalar, array, enum, structure, variant and placeholder members.</summary>
        private const string kModel =
            """
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:test:client-accessors</Uri></NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:client-accessors" Version="1.0.0"
                    PublicationDate="2026-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1001" BrowseName="1:AccessorDeviceType">
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=6001</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=6002</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=6003</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=6004</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=6005</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=6006</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=2001</Reference>
                </References>
              </UAObjectType>
              <UAVariable NodeId="ns=1;i=6001" BrowseName="1:Active" DataType="i=1">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=6002" BrowseName="1:Samples" DataType="i=11" ValueRank="1">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=6003" BrowseName="1:Zone" DataType="i=8912">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=6004" BrowseName="1:Kind" DataType="i=257">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=6005" BrowseName="1:AnyValue" DataType="i=24" ValueRank="-2">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=6006" BrowseName="Axis 1" SymbolicName="Axis1"
                  DataType="i=11" AccessLevel="3">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=80</Reference>
                </References>
              </UAVariable>
              <UAObject NodeId="ns=1;i=2001" BrowseName="1:Part_&lt;Nr&gt;" SymbolicName="Part">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=58</Reference>
                  <Reference ReferenceType="i=37">i=11508</Reference>
                </References>
              </UAObject>
            </UANodeSet>
            """;
    }
}
