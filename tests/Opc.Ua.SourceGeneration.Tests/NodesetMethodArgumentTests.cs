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
    /// Tests typed method arguments imported from NodeSet2 argument properties.
    /// </summary>
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class NodesetMethodArgumentTests
    {
        [Test]
        public void NodeSetMethodArgumentPropertiesGenerateTypedAsyncHandlerAndResult()
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
            var generator = new ModelSourceGenerator();
            var host = new ModelSourceGeneratorHoist(generator);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(host)
                .WithUpdatedParseOptions(
                    new CSharpParseOptions()
                        .WithKind(SourceCodeKind.Regular)
                        .WithLanguageVersion(LanguageVersion.CSharp13))
                .AddAdditionalTexts(
                [
                    EmbeddedText.Create("MethodArguments.NodeSet2.xml", NodeSet)
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
            Assert.That(generated, Does.Contain("uint jobOrderCommand"));
            Assert.That(generated, Does.Contain("string jobOrder"));
            Assert.That(generated, Does.Contain("public uint ReturnStatus"));
            Assert.That(
                generated,
                Does.Contain("state.CreateOrReplaceInputArguments(context, null)"));
            Assert.That(
                generated,
                Does.Contain("state.CreateOrReplaceOutputArguments(context, null)"));
            Assert.That(generated, Does.Contain("Name = \"JobOrderCommand\""));
            Assert.That(generated, Does.Contain("Name = \"ReturnStatus\""));
        }

        private const string NodeSet =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:test:method-arguments</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:method-arguments" Version="1.0.0"
                  PublicationDate="2026-01-01T00:00:00Z">
                  <RequiredModel ModelUri="http://opcfoundation.org/UA/" />
                </Model>
              </Models>
              <UAObjectType NodeId="ns=1;i=1000" BrowseName="1:ReceiverType">
                <References>
                  <Reference ReferenceType="i=47">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                </References>
              </UAObjectType>
              <UAMethod NodeId="ns=1;i=1001" BrowseName="1:ReceiveOrder" ParentNodeId="ns=1;i=1000">
                <References>
                  <Reference ReferenceType="i=46">ns=1;i=1002</Reference>
                  <Reference ReferenceType="i=46">ns=1;i=1003</Reference>
                  <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1000</Reference>
                </References>
              </UAMethod>
              <UAVariable NodeId="ns=1;i=1002" BrowseName="InputArguments" ParentNodeId="ns=1;i=1001"
                DataType="i=296" ValueRank="1">
                <References>
                  <Reference ReferenceType="i=46" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=68</Reference>
                </References>
                <Value>
                  <uax:ListOfExtensionObject xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                    <uax:ExtensionObject>
                      <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                      <uax:Body>
                        <uax:Argument>
                          <uax:Name>JobOrderCommand</uax:Name>
                          <uax:DataType><uax:Identifier>i=7</uax:Identifier></uax:DataType>
                          <uax:ValueRank>-1</uax:ValueRank>
                          <uax:ArrayDimensions />
                        </uax:Argument>
                      </uax:Body>
                    </uax:ExtensionObject>
                    <uax:ExtensionObject>
                      <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                      <uax:Body>
                        <uax:Argument>
                          <uax:Name>JobOrder</uax:Name>
                          <uax:DataType><uax:Identifier>i=12</uax:Identifier></uax:DataType>
                          <uax:ValueRank>-1</uax:ValueRank>
                          <uax:ArrayDimensions />
                        </uax:Argument>
                      </uax:Body>
                    </uax:ExtensionObject>
                  </uax:ListOfExtensionObject>
                </Value>
              </UAVariable>
              <UAVariable NodeId="ns=1;i=1003" BrowseName="OutputArguments" ParentNodeId="ns=1;i=1001"
                DataType="i=296" ValueRank="1">
                <References>
                  <Reference ReferenceType="i=46" IsForward="false">ns=1;i=1001</Reference>
                  <Reference ReferenceType="i=40">i=68</Reference>
                </References>
                <Value>
                  <uax:ListOfExtensionObject xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                    <uax:ExtensionObject>
                      <uax:TypeId><uax:Identifier>i=297</uax:Identifier></uax:TypeId>
                      <uax:Body>
                        <uax:Argument>
                          <uax:Name>ReturnStatus</uax:Name>
                          <uax:DataType><uax:Identifier>i=7</uax:Identifier></uax:DataType>
                          <uax:ValueRank>-1</uax:ValueRank>
                          <uax:ArrayDimensions />
                        </uax:Argument>
                      </uax:Body>
                    </uax:ExtensionObject>
                  </uax:ListOfExtensionObject>
                </Value>
              </UAVariable>
            </UANodeSet>
            """;
    }
}
