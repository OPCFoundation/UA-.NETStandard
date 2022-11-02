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
using System.Runtime.Serialization;
using System.Xml;
using Castle.Components.DictionaryAdapter.Xml;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using Opc.Ua.Core.Tests.Stack.Schema;
using Opc.Ua.Schema.Xml;

namespace Opc.Ua.Core.Tests.Types.Schemas
{
    /// <summary>
    /// Tests for the Binary Schema Validator class.
    /// </summary>
    [TestFixture, Category("XmlSchema")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class XmlSchemaWellKnownTests : XmlSchemaValidator
    {
        #region DataPointSources
        [DatapointSource]
        public string[][] WellKnownSchemaData => WellKnownDictionaries;
        #endregion

        #region Test Methods
        /// <summary>
        /// Load well known resource type dictionaries.
        /// </summary>
        [Theory]
        public void LoadResources(string[] schemaData)
        {
            Assert.That(schemaData.Length == 2);
            var assembly = typeof(XmlSchemaValidator).GetTypeInfo().Assembly;
            using (var stream = assembly.GetManifestResourceStream(schemaData[1]))
            {
                Assert.IsNotNull(stream);
            }
        }

        /// <summary>
        /// Load and validate well known resource type dictionaries.
        /// </summary>
        [Theory]
        public void ValidateResources(string[] schemaData)
        {
            var assembly = typeof(XmlSchemaValidator).GetTypeInfo().Assembly;
            using (var stream = assembly.GetManifestResourceStream(schemaData[1]))
            {
                Assert.IsNotNull(stream);
                var schema = new XmlSchemaValidator();
                Assert.IsNotNull(schema);
                schema.Validate(stream);
                Assert.IsNull(schema.FilePath);
                Assert.AreEqual(schemaData[0], schema.TargetSchema.TargetNamespace);
            }
        }

        /// <summary>
        /// Load zipped Nodeset2 resource.
        /// </summary>
        [Test]
        [TestCase("Opc.Ua.Nodeset2.xml")]
        public void LoadZipNodeset2Resources(string resource)
        {
            const string schemaPrefix = "Opc.Ua.Schema.";
            const string zipExtension = ".zip";
            var assembly = typeof(XmlSchemaValidator).GetTypeInfo().Assembly;

            using (var stream = assembly.GetManifestResourceStream(schemaPrefix + resource + zipExtension))
            {
                Assert.IsNotNull(stream);
                using (ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    Assert.NotNull(zipArchive);
                    Assert.AreEqual(1, zipArchive.Entries.Count);
                    var zipEntry = zipArchive.GetEntry(zipArchive.Entries[0].Name);
                    Assert.AreEqual(resource, zipEntry.Name);
                    var zipStream = zipEntry.Open();
                    Assert.IsNotNull(zipStream);

                    XmlReaderSettings settings = Utils.DefaultXmlReaderSettings();
                    settings.CloseInput = true;

                    var localContext = new SystemContext {
                        NamespaceUris = new NamespaceTable()
                    };

                    var exportedNodeSet = Export.UANodeSet.Read(zipStream);
                    Assert.IsNotNull(exportedNodeSet);
                    exportedNodeSet.NamespaceUris = exportedNodeSet.NamespaceUris ?? System.Array.Empty<string>();
                    foreach (var namespaceUri in exportedNodeSet.NamespaceUris)
                    {
                        localContext.NamespaceUris.Append(namespaceUri);
                    }
                    var nodeStates = new NodeStateCollection();
                    exportedNodeSet.Import(localContext, nodeStates);
                    Assert.Greater(nodeStates.Count, 0);
                }
            }
        }
        #endregion
    }
}
