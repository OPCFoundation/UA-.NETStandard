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
 *
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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The two guards that only a value from outside the synthesizer can trip:
    /// an <c>Argument</c> that does not carry the members the encoding gives
    /// it, and a walk that meets its own depth bound.
    /// </summary>
    /// <remarks>
    /// Closure validation reads the encoded <c>Argument</c> the
    /// InputArguments Property holds, and the projection check walks whatever
    /// the document put in <c>uav:nodes</c>. Both therefore have to survive a
    /// value the synthesizer would never write, and both are reached here
    /// directly because a well-formed document cannot produce one.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotClosureAndDepthGuardTests
    {
        private const string Refused = "ns=1;s=DataTypes/Reading";

        [Test]
        public void ANodeThatIsNotAVariableIsSkipped()
        {
            var diagnostics = new List<WotDiagnostic>();

            WotNodeSetConverter.ValidateNestedOnlySelection(
                [Refused],
                [new UAObject { NodeId = "ns=1;i=1", BrowseName = "1:O" }],
                diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        /// <summary>
        /// Nothing is refused when no type is nested-only, which is what keeps
        /// the check free for an ordinary document.
        /// </summary>
        [Test]
        public void AnEmptyRefusalSetStopsBeforeTheWalk()
        {
            var diagnostics = new List<WotDiagnostic>();

            WotNodeSetConverter.ValidateNestedOnlySelection(
                [],
                [Arguments("<uax:Argument xmlns:uax=\"" + Uax + "\">" +
                    "<uax:Name>Input</uax:Name><uax:DataType>" +
                    "<uax:Identifier>" + Refused + "</uax:Identifier>" +
                    "</uax:DataType></uax:Argument>")],
                diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void AVariableWithoutADataTypeIsNotRefused()
        {
            var diagnostics = new List<WotDiagnostic>();

            WotNodeSetConverter.ValidateNestedOnlySelection(
                [Refused],
                [new UAVariable { NodeId = "ns=1;i=2", BrowseName = "1:V", DataType = null }],
                diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void AVariableWhoseValueIsNotXmlIsSkipped()
        {
            var diagnostics = new List<WotDiagnostic>();

            WotNodeSetConverter.ValidateNestedOnlySelection(
                [Refused],
                [new UAVariable
                {
                    NodeId = "ns=1;i=3",
                    BrowseName = "1:V",
                    DataType = "i=12"
                }],
                diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        /// <summary>
        /// A text node between the elements is not an element, so the member
        /// search steps over it rather than reading it as one.
        /// </summary>
        [Test]
        public void TextBetweenTheArgumentMembersIsSteppedOver()
        {
            var diagnostics = new List<WotDiagnostic>();

            WotNodeSetConverter.ValidateNestedOnlySelection(
                [Refused],
                [Arguments("<uax:Argument xmlns:uax=\"" + Uax + "\">stray" +
                    "<uax:Name>Input</uax:Name>stray<uax:DataType>stray" +
                    "<uax:Identifier>" + Refused + "</uax:Identifier>stray" +
                    "</uax:DataType>stray</uax:Argument>")],
                diagnostics);

            Assert.That(
                diagnostics.Any(d =>
                    d.Message.Contains("argument 'Input'", StringComparison.Ordinal)),
                Is.True,
                string.Join("; ", diagnostics.Select(d => d.Message)));
        }

        /// <summary>
        /// An Argument with no DataType names no type, so there is nothing to
        /// refuse and nothing to dereference.
        /// </summary>
        [Test]
        public void AnArgumentWithoutADataTypeIsSkipped()
        {
            var diagnostics = new List<WotDiagnostic>();

            WotNodeSetConverter.ValidateNestedOnlySelection(
                [Refused],
                [Arguments("<uax:Argument xmlns:uax=\"" + Uax + "\">" +
                    "<uax:Name>Input</uax:Name></uax:Argument>")],
                diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void AnArgumentWhoseDataTypeCarriesNoIdentifierIsSkipped()
        {
            var diagnostics = new List<WotDiagnostic>();

            WotNodeSetConverter.ValidateNestedOnlySelection(
                [Refused],
                [Arguments("<uax:Argument xmlns:uax=\"" + Uax + "\">" +
                    "<uax:Name>Input</uax:Name><uax:DataType /></uax:Argument>")],
                diagnostics);

            Assert.That(diagnostics, Is.Empty);
        }

        /// <summary>
        /// An Argument that names a refused type but no name of its own is
        /// still reported: the defect is the type, and withholding the
        /// diagnostic because the label is missing would hide it.
        /// </summary>
        [Test]
        public void AnUnnamedArgumentIsStillReported()
        {
            var diagnostics = new List<WotDiagnostic>();

            WotNodeSetConverter.ValidateNestedOnlySelection(
                [Refused],
                [Arguments("<uax:Argument xmlns:uax=\"" + Uax + "\"><uax:DataType>" +
                    "<uax:Identifier>" + Refused + "</uax:Identifier>" +
                    "</uax:DataType></uax:Argument>")],
                diagnostics);

            Assert.That(
                diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Message.Contains("argument '?'", StringComparison.Ordinal)),
                Is.True,
                string.Join("; ", diagnostics.Select(d => d.Message)));
        }

        /// <summary>
        /// A projection nested deeper than the configured bound stops the walk
        /// rather than being the thing that overruns.
        /// </summary>
        [Test]
        public void TheScalarKindWalkStopsAtTheConfiguredDepth()
        {
            using JsonDocument document = JsonDocument.Parse(
                "{\"@type\":\"uav:NodeModel\",\"profileVersion\":\"1.0\"," +
                "\"nodes\":[{\"nodeId\":12345}]}");
            var shallow = new WotNodeSetConverterOptions { MaxJsonDepth = 1 };
            var deep = new WotNodeSetConverterOptions();

            var stopped = new List<WotDiagnostic>();
            var walked = new List<WotDiagnostic>();

            Assert.Multiple(() =>
            {
                Assert.That(
                    WotNativeProjection.ValidateScalarKinds(
                        document.RootElement, shallow, stopped),
                    Is.True,
                    "The walk stops at the bound, so the malformed member below " +
                    "it is not reached.");
                Assert.That(
                    WotNativeProjection.ValidateScalarKinds(
                        document.RootElement, deep, walked),
                    Is.False,
                    "With the ordinary bound the same member is reached and " +
                    "reported, so the contrast is the depth and nothing else.");
            });
        }

        private const string Uax = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        private static UAVariable Arguments(string argumentXml)
        {
            var document = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(
                new StringReader(
                    "<uax:ListOfExtensionObject xmlns:uax=\"" + Uax + "\">" +
                    "<uax:ExtensionObject><uax:Body>" + argumentXml +
                    "</uax:Body></uax:ExtensionObject></uax:ListOfExtensionObject>"),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            document.Load(reader);
            return new UAVariable
            {
                NodeId = "ns=1;s=M/InputArguments",
                BrowseName = "InputArguments",
                DataType = "i=296",
                Value = document.DocumentElement
            };
        }
    }
}
