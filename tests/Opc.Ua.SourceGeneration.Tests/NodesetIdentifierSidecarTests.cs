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
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Tests explicit NodeSet identifier CSV sidecars.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodesetIdentifierSidecarTests
    {
        [Test]
        public void ExplicitSidecarWithMatchingRowsProducesNoSidecarDiagnostics()
        {
            string modelPath = Path.Combine("Models", "Model.NodeSet2.xml");
            string identifierPath = Path.Combine("Models", "Model.ids.csv");
            ImmutableArray<Diagnostic> diagnostics = Run(
                [
                    EmbeddedText.Create(modelPath, NodeSet("urn:test:sidecar", "Thing", 1)),
                    EmbeddedText.Create(
                        identifierPath,
                        "\uFEFFSymbolicName,NodeId,NodeClass\r\nThing,1,Object\r\n")
                ],
                new Dictionary<string, string> { [modelPath] = "Model.ids.csv" });

            Assert.That(GetSidecarDiagnosticIds(diagnostics), Is.Empty);
        }

        [Test]
        public void XmlOnlyNodeSetDoesNotRequireAnIdentifierSidecar()
        {
            string modelPath = Path.Combine("Models", "Model.NodeSet2.xml");
            ImmutableArray<Diagnostic> diagnostics = Run(
                [EmbeddedText.Create(modelPath, NodeSet("urn:test:xml-only", "Thing", 1))],
                new Dictionary<string, string>());

            Assert.That(GetSidecarDiagnosticIds(diagnostics), Is.Empty);
        }

        [Test]
        public void DocumentationCsvIsNotTreatedAsAnIdentifierFile()
        {
            string modelPath = Path.Combine("Models", "Model.NodeSet2.xml");
            ImmutableArray<Diagnostic> diagnostics = Run(
                [
                    EmbeddedText.Create(modelPath, NodeSet("urn:test:documentation", "Thing", 1)),
                    EmbeddedText.Create(Path.Combine("Models", "Documentation.csv"), "Heading,Description\r\n")
                ],
                new Dictionary<string, string>());

            Assert.That(GetSidecarDiagnosticIds(diagnostics), Is.Empty);
        }

        [TestCase("missing.csv", null, "MODELGEN022")]
        [TestCase("ids.csv", "Thing,1,Object\r\nThing,2,Object\r\n", "MODELGEN023")]
        [TestCase("ids.csv", "Thing,1,Object\r\nOther,1,Object\r\n", "MODELGEN024")]
        [TestCase("ids.csv", "Unknown,1,Object\r\n", "MODELGEN025")]
        [TestCase("ids.csv", "Thing,2,Object\r\n", "MODELGEN026")]
        [TestCase("ids.csv", "Thing,1,Variable\r\n", "MODELGEN027")]
        [TestCase("ids.csv", "Thing,not-a-number,Object\r\n", "MODELGEN029")]
        public void ExplicitSidecarReportsValidationFailures(
            string identifierName,
            string identifierContent,
            string expectedDiagnosticId)
        {
            string modelPath = Path.Combine("Models", "Model.NodeSet2.xml");
            string identifierPath = Path.Combine("Models", identifierName);
            var texts = new List<AdditionalText>
            {
                EmbeddedText.Create(modelPath, NodeSet("urn:test:invalid-sidecar", "Thing", 1))
            };
            if (identifierContent != null)
            {
                texts.Add(EmbeddedText.Create(identifierPath, identifierContent));
            }

            ImmutableArray<Diagnostic> diagnostics = Run(
                texts,
                new Dictionary<string, string> { [modelPath] = identifierName });

            Assert.That(
                GetSidecarDiagnosticIds(diagnostics),
                Does.Contain(expectedDiagnosticId),
                string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        /// <summary>
        /// OPC 30050 PackML lists method arguments ahead of their method, and
        /// declares the method without a <c>ParentNodeId</c> - the parent is only
        /// recoverable from the inverse HasComponent reference. The argument has
        /// to be validated under the same qualified symbol the import pass emits.
        /// </summary>
        [Test]
        public void ArgumentListedBeforeUnparentedMethodValidatesQualifiedSymbol()
        {
            const string modelUri = "urn:test:forward-method-argument";
            string modelPath = Path.Combine("Models", "Model.NodeSet2.xml");
            string identifierPath = Path.Combine("Models", "Model.ids.csv");
            string nodeSet =
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris>
                    <Uri>{{modelUri}}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{{modelUri}}" Version="1.0.0"
                      PublicationDate="2026-01-01T00:00:00Z" />
                  </Models>
                  <Aliases>
                    <Alias Alias="Argument">i=296</Alias>
                    <Alias Alias="HasComponent">i=47</Alias>
                    <Alias Alias="HasProperty">i=46</Alias>
                    <Alias Alias="HasModellingRule">i=37</Alias>
                    <Alias Alias="HasSubtype">i=45</Alias>
                    <Alias Alias="HasTypeDefinition">i=40</Alias>
                  </Aliases>
                  <UAObjectType NodeId="ns=1;i=1" BrowseName="1:ThingType">
                    <DisplayName>ThingType</DisplayName>
                    <References>
                      <Reference ReferenceType="HasSubtype" IsForward="false">i=58</Reference>
                      <Reference ReferenceType="HasComponent">ns=1;i=3</Reference>
                    </References>
                  </UAObjectType>
                  <UAVariable NodeId="ns=1;i=2" BrowseName="InputArguments" ParentNodeId="ns=1;i=3"
                    DataType="Argument" ValueRank="1" ArrayDimensions="1">
                    <DisplayName>InputArguments</DisplayName>
                    <References>
                      <Reference ReferenceType="HasModellingRule">i=78</Reference>
                      <Reference ReferenceType="HasTypeDefinition">i=68</Reference>
                      <Reference ReferenceType="HasProperty" IsForward="false">ns=1;i=3</Reference>
                    </References>
                  </UAVariable>
                  <UAMethod NodeId="ns=1;i=3" BrowseName="1:DoIt">
                    <DisplayName>DoIt</DisplayName>
                    <References>
                      <Reference ReferenceType="HasProperty">ns=1;i=2</Reference>
                      <Reference ReferenceType="HasModellingRule">i=80</Reference>
                      <Reference ReferenceType="HasComponent" IsForward="false">ns=1;i=1</Reference>
                    </References>
                  </UAMethod>
                </UANodeSet>
                """;
            ImmutableArray<Diagnostic> diagnostics = Run(
                [
                    EmbeddedText.Create(modelPath, nodeSet),
                    EmbeddedText.Create(
                        identifierPath,
                        "SymbolicName,NodeId,NodeClass\r\n" +
                        "ThingType,1,ObjectType\r\n" +
                        "ThingType_DoIt_InputArguments,2,Variable\r\n" +
                        "ThingType_DoIt,3,Method\r\n")
                ],
                new Dictionary<string, string> { [modelPath] = "Model.ids.csv" });

            Assert.That(
                GetSidecarDiagnosticIds(diagnostics),
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        [Test]
        public void ExplicitSidecarCannotBeAssignedToMultipleNodeSets()
        {
            string firstModelPath = Path.Combine("Models", "First.NodeSet2.xml");
            string secondModelPath = Path.Combine("Models", "Second.NodeSet2.xml");
            string identifierPath = Path.Combine("Models", "ids.csv");
            ImmutableArray<Diagnostic> diagnostics = Run(
                [
                    EmbeddedText.Create(firstModelPath, NodeSet("urn:test:first", "First", 1)),
                    EmbeddedText.Create(secondModelPath, NodeSet("urn:test:second", "Second", 2)),
                    EmbeddedText.Create(identifierPath, "SymbolicName,NodeId,NodeClass\r\n")
                ],
                new Dictionary<string, string>
                {
                    [firstModelPath] = "ids.csv",
                    [secondModelPath] = "ids.csv"
                });

            Assert.That(GetSidecarDiagnosticIds(diagnostics), Does.Contain("MODELGEN028"));
        }

        /// <summary>
        /// Regression: the adjacent path was combined but never normalized, so
        /// "./x.csv", "../x.csv" and "sub/x.csv" never matched the normalized
        /// AdditionalFiles path and the sidecar was reported missing.
        /// </summary>
        [TestCase("./ids.csv", "Models")]
        [TestCase(".\\ids.csv", "Models")]
        [TestCase("../Shared/ids.csv", "Shared")]
        [TestCase("sub/ids.csv", "Models/sub")]
        public void RelativeSidecarPathIsResolved(string identifierFile, string identifierDirectory)
        {
            string modelPath = Path.Combine("Models", "Model.NodeSet2.xml");
            string identifierPath = Path.Combine(
                [.. identifierDirectory.Split('/'), "ids.csv"]);
            ImmutableArray<Diagnostic> diagnostics = Run(
                [
                    EmbeddedText.Create(modelPath, NodeSet("urn:test:relative", "Thing", 1)),
                    EmbeddedText.Create(identifierPath, "SymbolicName,NodeId,NodeClass\r\nThing,1,Object\r\n")
                ],
                new Dictionary<string, string> { [modelPath] = identifierFile });

            Assert.That(
                GetSidecarDiagnosticIds(diagnostics),
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        /// <summary>
        /// Regression: the suffix fallback matched without a directory boundary,
        /// so "ids.csv" silently selected "Other.ids.csv" instead of reporting
        /// the sidecar missing.
        /// </summary>
        [Test]
        public void SidecarSuffixMatchRequiresADirectoryBoundary()
        {
            string modelPath = Path.Combine("Models", "Model.NodeSet2.xml");
            ImmutableArray<Diagnostic> diagnostics = Run(
                [
                    EmbeddedText.Create(modelPath, NodeSet("urn:test:boundary", "Thing", 1)),
                    EmbeddedText.Create(
                        Path.Combine("Elsewhere", "Other.ids.csv"),
                        "SymbolicName,NodeId,NodeClass\r\nThing,1,Object\r\n")
                ],
                new Dictionary<string, string> { [modelPath] = "ids.csv" });

            Assert.That(
                GetSidecarDiagnosticIds(diagnostics),
                Is.Not.Empty.And.All.EqualTo("MODELGEN022"));
        }

        /// <summary>
        /// Regression: a ModelDesign that is the only design in its folder and
        /// has no same-named CSV adopted the single CSV next to it - even the
        /// identifier sidecar a NodeSet claims through its IdentifierFile
        /// metadata - and took the NodeSet's numeric ids.
        /// </summary>
        [Test]
        public void ModelDesignDoesNotAdoptASidecarClaimedByANodeSet()
        {
            string modelPath = Path.Combine("Models", "Model.NodeSet2.xml");
            string designPath = Path.Combine("Models", "Design.xml");
            GeneratorDriverRunResult result = RunGenerator(
                [
                    EmbeddedText.Create(
                        modelPath,
                        NodeSet("urn:test:claimed", "Thing", 4242).Replace(
                            "SymbolicName=\"Thing\" />",
                            "SymbolicName=\"Thing\"><References>" +
                            "<Reference ReferenceType=\"i=40\">i=58</Reference>" +
                            "</References></UAObject>",
                            StringComparison.Ordinal)),
                    EmbeddedText.Create(
                        Path.Combine("Models", "Model.ids.csv"),
                        "SymbolicName,NodeId,NodeClass\r\nThing,4242,Object\r\n"),
                    EmbeddedText.Create(designPath, Design("urn:test:design", "Thing"))
                ],
                new Dictionary<string, string> { [modelPath] = "Model.ids.csv" });

            GeneratedSourceResult[] designSources = [.. result.Results
                .SelectMany(r => r.GeneratedSources)
                .Where(s => s.HintName.StartsWith("Test.Design.", StringComparison.Ordinal))];
            Assert.That(
                designSources,
                Is.Not.Empty,
                string.Join(", ", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.HintName)) +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Concat(result.Results.SelectMany(r => r.Diagnostics))));
            Assert.That(
                designSources.Select(s => s.SourceText.ToString()),
                Has.None.Contains("4242"),
                "the design must not take the NodeSet sidecar's identifiers");
        }

        private static ImmutableArray<Diagnostic> Run(
            IEnumerable<AdditionalText> additionalTexts,
            IReadOnlyDictionary<string, string> sidecars)
        {
            GeneratorDriverRunResult result = RunGenerator(additionalTexts, sidecars);
            return [.. result.Diagnostics.Concat(result.Results.SelectMany(generator => generator.Diagnostics))];
        }

        private static GeneratorDriverRunResult RunGenerator(
            IEnumerable<AdditionalText> additionalTexts,
            IReadOnlyDictionary<string, string> sidecars)
        {
            CSharpCompilation compilation = OptimizationLevel.Release
                .CreateCompilation()
                .AddCode(
                    new Dictionary<string, string>().WithOpcUaGeneratedStack(),
                    LanguageVersion.CSharp13);
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorOmitFluentApi"] = "true"
                });
            foreach (KeyValuePair<string, string> sidecar in sidecars)
            {
                options.TextOptions[sidecar.Key] = new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.ModelSourceGeneratorIdentifierFile"] =
                        sidecar.Value
                };
            }

            foreach (KeyValuePair<string, string> sidecar in sidecars)
            {
                NodesetFileOptions nodeSetOptions = new AnalyzerOptions(
                    options.TextOptions[sidecar.Key]).ToNodeSetOptions();
                Assert.That(nodeSetOptions.IdentifierFile, Is.EqualTo(sidecar.Value));
            }

            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(
                    new CSharpParseOptions()
                        .WithKind(SourceCodeKind.Regular)
                        .WithLanguageVersion(LanguageVersion.CSharp13))
                .AddAdditionalTexts([.. additionalTexts])
                .WithUpdatedAnalyzerConfigOptions(options);
            driver = driver.RunGenerators(compilation);

            return driver.GetRunResult();
        }

        private static string Design(string namespaceUri, string symbolicName)
        {
            return
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <opc:ModelDesign
                  xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                  xmlns:ua="http://opcfoundation.org/UA/"
                  xmlns="{{namespaceUri}}"
                  TargetNamespace="{{namespaceUri}}">
                  <opc:Namespaces>
                    <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                    <opc:Namespace Name="Design" Prefix="Test.Design">{{namespaceUri}}</opc:Namespace>
                  </opc:Namespaces>
                  <opc:ObjectType SymbolicName="{{symbolicName}}Type" BaseType="ua:BaseObjectType" />
                  <opc:Object SymbolicName="{{symbolicName}}" TypeDefinition="{{symbolicName}}Type" />
                </opc:ModelDesign>
                """;
        }

        private static IEnumerable<string> GetSidecarDiagnosticIds(
            IEnumerable<Diagnostic> diagnostics)
        {
            return diagnostics
                .Select(diagnostic => diagnostic.Id)
                .Where(id => id.StartsWith("MODELGEN02", StringComparison.Ordinal));
        }

        private static string NodeSet(string modelUri, string symbolicName, uint identifier)
        {
            return
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris>
                    <Uri>{{modelUri}}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{{modelUri}}" Version="1.0.0"
                      PublicationDate="2026-01-01T00:00:00Z" />
                  </Models>
                  <UAObject NodeId="ns=1;i={{identifier}}" BrowseName="1:{{symbolicName}}"
                    SymbolicName="{{symbolicName}}" />
                </UANodeSet>
                """;
        }
    }
}
