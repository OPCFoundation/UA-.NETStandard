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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// A NodeSet2 may identify a node with any of the four identifier types of
    /// OPC 10000-3 5.2.2. Guid and Opaque identifiers used to be dropped during
    /// the import, leaving a node without any identity in the generated code.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodesetExtendedNodeIdTests
    {
        [Test]
        public void NodeSetWithGuidAndOpaqueNodeIdsGeneratesCompilableIdentifiers()
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
            options.TextOptions["ExtendedIds.NodeSet2.xml"] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                    "urn:test:extended-ids",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "TestExtendedIds",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] =
                    "Opc.Ua.TestExtendedIds"
            };
            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(
                    new CSharpParseOptions()
                        .WithKind(SourceCodeKind.Regular)
                        .WithLanguageVersion(LanguageVersion.CSharp13))
                .AddAdditionalTexts(
                [
                    EmbeddedText.Create("ExtendedIds.NodeSet2.xml", NodeSet)
                ])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(
                diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join("\n", diagnostics.Select(diagnostic => diagnostic.ToString())));

            string generated = string.Join(
                "\n",
                driver.GetRunResult().Results[0].GeneratedSources
                    .Select(source => source.SourceText.ToString()));

            Assert.Multiple(() =>
            {
                Assert.That(
                    generated,
                    Does.Contain(
                        "public static readonly global::System.Guid GuidObject = " +
                        "new global::System.Guid(\"09087e75-8e5e-499b-954f-f2a9603db28a\");"));
                Assert.That(
                    generated,
                    Does.Contain(
                        "public static readonly global::Opc.Ua.ByteString OpaqueObject = " +
                        "global::Opc.Ua.ByteString.FromBase64(\"M/RbKBsRVkePCePcx24oRA==\");"));
            });

            outputCompilation.GetDiagnostics().Check(
                TestContext.Out,
                out int errors,
                out _);
            Assert.That(errors, Is.Zero, $"Compilation produced {errors} errors");
        }

        private const string NodeSet =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
                       xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:test:extended-ids</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:extended-ids" Version="1.0.0"
                  PublicationDate="2026-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1000" BrowseName="1:PlantType">
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                </References>
              </UAObjectType>
              <UAObject NodeId="ns=1;g=09087e75-8e5e-499b-954f-f2a9603db28a"
                BrowseName="1:GuidObject">
                <References>
                  <Reference ReferenceType="i=40">ns=1;i=1000</Reference>
                  <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;b=M/RbKBsRVkePCePcx24oRA==" BrowseName="1:OpaqueObject">
                <References>
                  <Reference ReferenceType="i=40">ns=1;i=1000</Reference>
                  <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                </References>
              </UAObject>
            </UANodeSet>
            """;
    }
}
