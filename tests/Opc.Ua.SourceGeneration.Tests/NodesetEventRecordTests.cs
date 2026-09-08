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
    /// Tests event-record generation for structurally eligible NodeSet2 event types.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodesetEventRecordTests
    {
        [Test]
        public void NodeSetEventTypeGeneratesRecordAndEventFilter()
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
            options.TextOptions["Event.NodeSet2.xml"] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorModelUri"] =
                    "urn:test:event-record",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorName"] = "TestEvent",
                ["build_metadata.AdditionalFiles.ModelSourceGeneratorPrefix"] =
                    "Opc.Ua.TestEvent"
            };
            var generator = new ModelSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
                .WithUpdatedParseOptions(
                    new CSharpParseOptions()
                        .WithKind(SourceCodeKind.Regular)
                        .WithLanguageVersion(LanguageVersion.CSharp13))
                .AddAdditionalTexts(
                [
                    EmbeddedText.Create("Event.NodeSet2.xml", NodeSet)
                ])
                .WithUpdatedAnalyzerConfigOptions(options);
            driver = driver.RunGenerators(compilation);

            GeneratorDriverRunResult runResult = driver.GetRunResult();
            ImmutableArray<Diagnostic> diagnostics =
            [
                .. runResult.Diagnostics,
                .. runResult.Results.SelectMany(result => result.Diagnostics)
            ];
            Assert.That(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);

            string generated = string.Join(
                "\n",
                runResult.Results[0].GeneratedSources.Select(source => source.SourceText.ToString()));
            Assert.That(generated, Does.Contain("StatusEventTypeRecord"));
            Assert.That(
                generated,
                Does.Contain("public partial record StatusEventTypeRecord"));
            Assert.That(generated, Does.Contain("class EventFilters"));
            Assert.That(generated, Does.Contain("public string? Status"));
            Assert.That(generated, Does.Contain("NamespaceTable namespaceUris"));
            Assert.That(
                generated,
                Does.Contain("ExpandedNodeId.ToNodeId"));
            Assert.That(
                generated,
                Does.Contain(".ObjectTypeIds.StatusEventType, namespaceUris)"));
            Assert.That(
                generated,
                Does.Contain(
                    "GetEncodeable<global::Opc.Ua.TestEvent.StatusPayload>"));
            Assert.That(generated, Does.Contain("GetNullableUInt32"));
            Assert.That(generated, Does.Contain("GetStringArray"));
            Assert.That(
                generated,
                Does.Contain("public global::Opc.Ua.Variant SourceUrl"));
        }

        private const string NodeSet =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:test:event-record</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:event-record" Version="1.0.0"
                  PublicationDate="2026-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1000" BrowseName="1:StatusEventType" IsAbstract="true">
                <References>
                  <Reference ReferenceType="i=47">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=1002</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=1003</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=1004</Reference>
                  <Reference ReferenceType="i=47">ns=1;i=1005</Reference>
                  <Reference ReferenceType="i=45" IsForward="false">i=2041</Reference>
                </References>
              </UAObjectType>
              <UAVariable NodeId="ns=1;i=1001" BrowseName="1:Status" ParentNodeId="ns=1;i=1000"
                DataType="i=12">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1000</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=1003" BrowseName="1:Epoch" ParentNodeId="ns=1;i=1000"
                DataType="i=7">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1000</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=1004" BrowseName="1:Changed" ParentNodeId="ns=1;i=1000"
                DataType="i=12" ValueRank="1">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1000</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=1005" BrowseName="1:SourceUrl" ParentNodeId="ns=1;i=1000"
                DataType="i=23751">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1000</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=1002" BrowseName="1:Payload"
                ParentNodeId="ns=1;i=1000" DataType="ns=1;i=2000">
                <References>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1000</Reference>
                  <Reference ReferenceType="i=40">i=63</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UADataType NodeId="ns=1;i=2000" BrowseName="1:StatusPayload">
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=22</Reference>
                </References>
                <Definition Name="1:StatusPayload">
                  <Field Name="Text" DataType="i=12" />
                </Definition>
              </UADataType>
            </UANodeSet>
            """;
    }
}
