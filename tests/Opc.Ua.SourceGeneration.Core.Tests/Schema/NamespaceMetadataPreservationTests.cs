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

using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Protects declared NodeSet namespace metadata against ModelDesign-only range synthesis.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NamespaceMetadataPreservationTests
    {
        /// <summary>
        /// Keeps every explicit range rather than widening the promise to all positive numeric identifiers.
        /// </summary>
        [Test]
        public void ImportedNamespaceKeepsItsExactStaticRanges()
        {
            VariableDesign variable = ImportRange(
                """
                <Value>
                  <uax:ListOfString><uax:String>1:96</uax:String><uax:String>101:102</uax:String></uax:ListOfString>
                </Value>
                """);

            Assert.That(variable.DefaultValue, Is.Not.Null);
            string[] values =
            [
                .. variable.DefaultValue!.ChildNodes.OfType<System.Xml.XmlElement>()
                    .Select(element => element.InnerText)
            ];
            Assert.That(values, Is.EqualTo(s_expectedRanges));
        }

        /// <summary>
        /// An explicitly empty static range list must not acquire a static-identifier promise.
        /// </summary>
        [Test]
        public void ImportedEmptyStaticRangesStayEmpty()
        {
            VariableDesign variable = ImportRange("<Value><uax:ListOfString /></Value>");

            Assert.That(variable.DefaultValue, Is.Not.Null);
            Assert.That(variable.DefaultValue!.ChildNodes.OfType<System.Xml.XmlElement>(), Is.Empty);
        }

        /// <summary>
        /// An omitted Value remains null instead of being synthesized from undeclared dynamic nodes.
        /// </summary>
        [Test]
        public void ImportedAbsentStaticRangesStayNull()
        {
            VariableDesign variable = ImportRange(string.Empty);

            Assert.That(variable.NodeSetValueIsNull, Is.True);
            Assert.That(variable.DefaultValue, Is.Null);
            Assert.That(variable.DecodedValue, Is.Null);
        }

        private static VariableDesign ImportRange(string value)
        {
            using var files = new VirtualFileSystem();
            const string path = "memory://Metadata.NodeSet2.xml";
            string xml =
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                           xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <NamespaceUris><Uri>urn:namespace-metadata-test</Uri></NamespaceUris>
                  <Models>
                    <Model ModelUri="urn:namespace-metadata-test" Version="1.0.0">
                      <RequiredModel ModelUri="http://opcfoundation.org/UA/"
                                     Version="1.05.04" PublicationDate="2024-05-01T00:00:00Z" />
                    </Model>
                  </Models>
                  <UAObject NodeId="ns=1;i=1" BrowseName="1:Metadata">
                    <DisplayName>Metadata</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=11616</Reference>
                      <Reference ReferenceType="i=46">ns=1;i=2</Reference>
                    </References>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i=2" BrowseName="0:StaticNumericNodeIdRange"
                              ParentNodeId="ns=1;i=1" DataType="i=291" ValueRank="1">
                    <DisplayName>StaticNumericNodeIdRange</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=68</Reference>
                      <Reference ReferenceType="i=46" IsForward="false">ns=1;i=1</Reference>
                    </References>
                    {{value}}
                  </UAVariable>
                </UANodeSet>
                """;
            files.Add(path, Encoding.UTF8.GetBytes(xml));
            IFileSystem fileSystem = typeof(ModelDesignValidator).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(files);
            var validator = new ModelDesignValidator(
                fileSystem, 1000, [],
                NUnitTelemetryContext.Create(logLevel: LogLevel.Error),
                SpecificationVersion.V105);
            validator.Validate([path + ",Metadata,Metadata"], [], null!);
            Assert.That(validator.TryFindNode(
                new XmlQualifiedName("Metadata", "urn:namespace-metadata-test"),
                "Test", "Node", out NodeDesign? metadata), Is.True);
            NodeDesign? node = metadata!.Hierarchy.NodeList
                .Single(child => child.RelativePath == "StaticNumericNodeIdRange").Instance;
            Assert.That(node, Is.InstanceOf<VariableDesign>());
            return (VariableDesign)node!;
        }

        private static readonly string[] s_expectedRanges = ["1:96", "101:102"];
    }
}
