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

using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// XML round-trip tests for <see cref="BrowserOptions"/>.
    /// </summary>
    [TestFixture]
    [Category("BrowserOptions")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BrowserOptionsXmlRoundTripTests
    {
        [Test]
        public void XmlRoundTripPreservesAllProperties()
        {
            var original = new BrowserOptions
            {
                MaxReferencesReturned = 500,
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = new NodeId(33),
                IncludeSubtypes = false,
                NodeClassMask = 255,
                ResultMask = 63,
                ContinuationPointPolicy = ContinuationPointPolicy.Balanced,
                MaxNodesPerBrowse = 100,
                MaxBrowseContinuationPoints = 10
            };

            IServiceMessageContext ctx = CreateMessageContext();

            string xml = Encode(original, ctx);
            BrowserOptions decoded = Decode(xml, ctx);

            Assert.That(decoded.MaxReferencesReturned,
                Is.EqualTo(original.MaxReferencesReturned));
            Assert.That(decoded.BrowseDirection,
                Is.EqualTo(original.BrowseDirection));
            Assert.That(decoded.ReferenceTypeId,
                Is.EqualTo(original.ReferenceTypeId));
            Assert.That(decoded.IncludeSubtypes,
                Is.EqualTo(original.IncludeSubtypes));
            Assert.That(decoded.NodeClassMask,
                Is.EqualTo(original.NodeClassMask));
            Assert.That(decoded.ResultMask,
                Is.EqualTo(original.ResultMask));
            Assert.That(decoded.ContinuationPointPolicy,
                Is.EqualTo(original.ContinuationPointPolicy));
            Assert.That(decoded.MaxNodesPerBrowse,
                Is.EqualTo(original.MaxNodesPerBrowse));
            Assert.That(decoded.MaxBrowseContinuationPoints,
                Is.EqualTo(original.MaxBrowseContinuationPoints));
        }

        [Test]
        public void XmlRoundTripWithDefaultValues()
        {
            var original = new BrowserOptions();
            IServiceMessageContext ctx = CreateMessageContext();

            string xml = Encode(original, ctx);
            BrowserOptions decoded = Decode(xml, ctx);

            Assert.That(decoded.MaxReferencesReturned,
                Is.EqualTo(original.MaxReferencesReturned));
            Assert.That(decoded.BrowseDirection,
                Is.EqualTo(original.BrowseDirection));
            Assert.That(decoded.IncludeSubtypes,
                Is.EqualTo(original.IncludeSubtypes));
            Assert.That(decoded.NodeClassMask,
                Is.EqualTo(original.NodeClassMask));
            Assert.That(decoded.ResultMask,
                Is.EqualTo(original.ResultMask));
            Assert.That(decoded.ContinuationPointPolicy,
                Is.EqualTo(original.ContinuationPointPolicy));
            Assert.That(decoded.MaxNodesPerBrowse,
                Is.EqualTo(original.MaxNodesPerBrowse));
            Assert.That(decoded.MaxBrowseContinuationPoints,
                Is.EqualTo(original.MaxBrowseContinuationPoints));
        }

        [Test]
        public void XmlRoundTripFromFixtureFile()
        {
            IServiceMessageContext ctx = CreateMessageContext();

            string filePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "test-browser-options.xml");

            BrowserOptions first;
            using (var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read))
            {
                first = DecodeFromStream(stream, ctx);
            }

            Assert.That(first.MaxReferencesReturned, Is.EqualTo(500u));
            Assert.That(first.BrowseDirection, Is.EqualTo(BrowseDirection.Inverse));
            Assert.That(first.ReferenceTypeId, Is.EqualTo(new NodeId(33)));
            Assert.That(first.IncludeSubtypes, Is.False);
            Assert.That(first.NodeClassMask, Is.EqualTo(255));
            Assert.That(first.ResultMask, Is.EqualTo(63u));
            Assert.That(first.ContinuationPointPolicy,
                Is.EqualTo(ContinuationPointPolicy.Balanced));
            Assert.That(first.MaxNodesPerBrowse, Is.EqualTo(100u));
            Assert.That(first.MaxBrowseContinuationPoints, Is.EqualTo(10));

            string xml = Encode(first, ctx);
            BrowserOptions second = Decode(xml, ctx);

            Assert.That(second.MaxReferencesReturned,
                Is.EqualTo(first.MaxReferencesReturned));
            Assert.That(second.BrowseDirection,
                Is.EqualTo(first.BrowseDirection));
            Assert.That(second.ReferenceTypeId,
                Is.EqualTo(first.ReferenceTypeId));
            Assert.That(second.IncludeSubtypes,
                Is.EqualTo(first.IncludeSubtypes));
            Assert.That(second.NodeClassMask,
                Is.EqualTo(first.NodeClassMask));
            Assert.That(second.ResultMask,
                Is.EqualTo(first.ResultMask));
            Assert.That(second.ContinuationPointPolicy,
                Is.EqualTo(first.ContinuationPointPolicy));
            Assert.That(second.MaxNodesPerBrowse,
                Is.EqualTo(first.MaxNodesPerBrowse));
            Assert.That(second.MaxBrowseContinuationPoints,
                Is.EqualTo(first.MaxBrowseContinuationPoints));
        }

        private static ServiceMessageContext CreateMessageContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return ServiceMessageContext.CreateEmpty(telemetry);
        }

        private static string Encode(
            BrowserOptions options,
            IServiceMessageContext ctx)
        {
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            using (var writer = XmlWriter.Create(stream, settings))
            {
                using var encoder = new XmlEncoder(
                    typeof(BrowserOptions), writer, ctx);
                options.Encode(encoder);
                encoder.Close();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static BrowserOptions Decode(
            string xml,
            IServiceMessageContext ctx)
        {
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(xml));
            return DecodeFromStream(stream, ctx);
        }

        private static BrowserOptions DecodeFromStream(
            Stream stream,
            IServiceMessageContext ctx)
        {
            using var parser = new XmlParser(
                typeof(BrowserOptions), stream, ctx);
            var result = new BrowserOptions();
            result.Decode(parser);
            return result;
        }
    }
}
