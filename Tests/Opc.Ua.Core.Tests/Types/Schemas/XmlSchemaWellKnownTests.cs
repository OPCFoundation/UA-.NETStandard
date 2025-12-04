/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.IO.Compression;
using System.Reflection;
using System.Xml;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Schema.Xml;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Schemas
{
    /// <summary>
    /// Tests for the Binary Schema Validator class.
    /// </summary>
    [TestFixture]
    [Category("XmlSchema")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class XmlSchemaWellKnownTests : XmlSchemaValidator
    {
        [DatapointSource]
        public string[][] WellKnownSchemaData => WellKnownDictionaries;

        /// <summary>
        /// Load well known resource type dictionaries.
        /// </summary>
        [Theory]
        public void LoadResources(string[] schemaData)
        {
            NUnit.Framework.Assert.That(schemaData.Length == 2);
            Assembly assembly = typeof(XmlSchemaValidator).GetTypeInfo().Assembly;
            using Stream stream = assembly.GetManifestResourceStream(schemaData[1]);
            Assert.IsNotNull(stream);
        }

        /// <summary>
        /// Load and validate well known resource type dictionaries.
        /// </summary>
        [Theory]
        public void ValidateResources(string[] schemaData)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<XmlSchemaWellKnownTests>();

            Assembly assembly = typeof(XmlSchemaValidator).GetTypeInfo().Assembly;
            using Stream stream = assembly.GetManifestResourceStream(schemaData[1]);
            Assert.IsNotNull(stream);
            var schema = new XmlSchemaValidator();
            Assert.IsNotNull(schema);
            schema.Validate(stream, logger);
            Assert.IsNull(schema.FilePath);
            Assert.AreEqual(schemaData[0], schema.TargetSchema.TargetNamespace);
        }

        /// <summary>
        /// Load zipped NodeSet2 resource.
        /// </summary>
        [Test]
        [TestCase("Opc.Ua.NodeSet2.xml")]
        public void LoadZipNodeSet2Resources(string resource)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string schemaPrefix = "Opc.Ua.Schema.";
            const string zipExtension = ".zip";
            Assembly assembly = CoreUtils.GetOpcUaCoreAssembly();

            using Stream stream = assembly.GetManifestResourceStream(
                schemaPrefix + resource + zipExtension);
            Assert.IsNotNull(stream);
            using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            Assert.NotNull(zipArchive);
            Assert.AreEqual(1, zipArchive.Entries.Count);
            ZipArchiveEntry zipEntry = zipArchive.GetEntry(zipArchive.Entries[0].Name);
            Assert.AreEqual(resource, zipEntry.Name);
            Stream zipStream = zipEntry.Open();
            Assert.IsNotNull(zipStream);

            XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
            settings.CloseInput = true;

            var localContext = new SystemContext(telemetry) { NamespaceUris = new NamespaceTable() };

            var exportedNodeSet = Export.UANodeSet.Read(zipStream);
            Assert.IsNotNull(exportedNodeSet);
            exportedNodeSet.NamespaceUris ??= [];
            foreach (string namespaceUri in exportedNodeSet.NamespaceUris)
            {
                localContext.NamespaceUris.Append(namespaceUri);
            }
            var nodeStates = new NodeStateCollection();
            exportedNodeSet.Import(localContext, nodeStates);
            Assert.Greater(nodeStates.Count, 0);
        }
    }
}
