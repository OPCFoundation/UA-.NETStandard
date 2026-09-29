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
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Excluded (for example Draft) ObjectTypes get no state class, so the
    /// generated fluent surface must never reference one.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodesetExcludedObjectTypeTests
    {
        /// <summary>
        /// E-5: a predefined instance whose child object is typed by an
        /// excluded ObjectType got an instance wrapper typed on the never
        /// generated state class of that type (CS0246). The child resolves to
        /// the nearest emitted ancestor instead.
        /// </summary>
        [Test]
        public void InstanceChildOfExcludedObjectTypeCompiles()
        {
            CSharpCompilation compilation = ModelDependencyScannerTests
                .CreateStackCompilation("ExcludedTypeConsumer")
                .AddCode(
                    new Dictionary<string, string>
                    {
                        ["Manager.cs"] =
                            """
                            namespace Opc.Ua.TestExcluded
                            {
                                [global::Opc.Ua.Server.Fluent.NodeManager(
                                    NamespaceUri = "urn:test:excluded-type")]
                                public partial class ExcludedNodeManager
                                {
                                }
                            }
                            """
                    },
                    LanguageVersion.CSharp13);
            var options = new AnalyzerOptionsProvider(
                new Dictionary<string, string>
                {
                    ["build_property.ModelSourceGeneratorExclude"] = "Draft",
                    ["build_property.ModelSourceGeneratorOmitEventRecords"] = "true"
                });
            options.TextOptions["Excluded.NodeSet2.xml"] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                    "urn:test:excluded-type",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "TestExcluded",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] =
                    "Opc.Ua.TestExcluded"
            };
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModelSourceGenerator())
                .WithUpdatedParseOptions(
                    new CSharpParseOptions()
                        .WithKind(SourceCodeKind.Regular)
                        .WithLanguageVersion(LanguageVersion.CSharp13))
                .AddAdditionalTexts([EmbeddedText.Create("Excluded.NodeSet2.xml", NodeSet)])
                .WithUpdatedAnalyzerConfigOptions(options);

            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);

            Assert.That(
                diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty);
            string generated = string.Join(
                "\n",
                driver.GetRunResult().Results[0].GeneratedSources
                    .Select(source => source.SourceText.ToString()));
            Assert.That(generated, Does.Contain("PlantBuilder"), "the instance wrapper is generated");
            Assert.That(generated, Does.Not.Contain("DraftThingState"));
            Assert.That(
                outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                string.Join(Environment.NewLine, outputCompilation.GetDiagnostics()));
        }

        private const string NodeSet =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:test:excluded-type</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:excluded-type" Version="1.0.0"
                  PublicationDate="2026-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1000" BrowseName="1:DraftThingType" ReleaseStatus="Draft">
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                </References>
              </UAObjectType>
              <UAObject NodeId="ns=1;i=5000" BrowseName="1:Plant">
                <References>
                  <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                  <Reference ReferenceType="i=40">i=58</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=5001</Reference>
                </References>
              </UAObject>
              <UAObject NodeId="ns=1;i=5001" BrowseName="1:Thing" ParentNodeId="ns=1;i=5000">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=5000</Reference>
                  <Reference ReferenceType="i=40">ns=1;i=1000</Reference>
                </References>
              </UAObject>
            </UANodeSet>
            """;
    }
}
