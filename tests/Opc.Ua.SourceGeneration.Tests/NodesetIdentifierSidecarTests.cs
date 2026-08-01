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

        private static ImmutableArray<Diagnostic> Run(
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

            GeneratorDriverRunResult result = driver.GetRunResult();
            return [.. result.Diagnostics.Concat(result.Results.SelectMany(generator => generator.Diagnostics))];
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
