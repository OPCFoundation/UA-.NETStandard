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
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Holds the companion models to the readable mapping: each converts to a
    /// set of linked documents in which no document needs the
    /// <c>uav:nodes</c> projection.
    /// </summary>
    /// <remarks>
    /// A companion model states many type definitions side by side and has no
    /// single root. Converted as one document it leaves everything but the
    /// first root unreachable and falls back to the projection for the whole
    /// model — which is what §6.11.8 and §9.1 exist to avoid. These tests are
    /// the measure of that, model by model.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    public sealed class WotCompanionModelDocumentSetTests
    {
        [Test]
        public void DeviceIntegrationModelStatesEveryDocumentReadably()
        {
            AssertSetIsReadable(
                Path.Combine(
                    RepositoryRoot,
                    "tests",
                    "Opc.Ua.SourceGeneration.Core.Tests",
                    "Resources",
                    "Opc.Ua.Di.NodeSet2.xml"),
                "opc-ua-di",
                "OPC UA Device Integration");
        }

        [Test]
        public void MachineryModelStatesEveryDocumentReadably()
        {
            AssertSetIsReadable(
                Path.Combine(
                    RepositoryRoot,
                    "samples",
                    "PumpDeviceIntegrationServer",
                    "Model",
                    "Opc.Ua.Machinery.NodeSet2.xml"),
                "opc-ua-machinery",
                "OPC UA Machinery");
        }

        [Test]
        public void PumpsModelStatesEveryDocumentReadably()
        {
            AssertSetIsReadable(
                Path.Combine(
                    RepositoryRoot,
                    "samples",
                    "PumpDeviceIntegrationServer",
                    "Model",
                    "Opc.Ua.Pumps.NodeSet2.xml"),
                "opc-ua-pumps",
                "OPC UA Pumps");
        }

        private static void AssertSetIsReadable(
            string sourcePath,
            string modelPrefix,
            string title)
        {
            Assert.That(File.Exists(sourcePath), Is.True, $"'{sourcePath}' should exist.");

            IReadOnlyList<WotAggregationDocumentGenerator.GeneratedDocument> documents =
                WotAggregationDocumentGenerator.GenerateThingModelSet(
                    sourcePath, modelPrefix, title);

            Assert.That(documents, Is.Not.Empty);

            var projected = new List<string>();
            foreach (WotAggregationDocumentGenerator.GeneratedDocument document in documents)
            {
                using var parsed = JsonDocument.Parse(document.Json);
                if (parsed.RootElement.TryGetProperty("uav:nodes", out _))
                {
                    projected.Add(document.Href);
                }
            }

            Assert.That(
                projected,
                Is.Empty,
                $"{Path.GetFileName(sourcePath)}: {projected.Count} of {documents.Count} " +
                "documents still need the uav:nodes projection.");

            // An href identifies a document within the set and becomes its file
            // name, so a duplicate would silently overwrite a sibling.
            Assert.That(
                documents.Select(d => d.Href).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(documents.Count),
                "Every document in the set should have a distinct href.");
        }

        private static string RepositoryRoot
        {
            get
            {
                DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
                while (directory is not null &&
                    !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    directory = directory.Parent;
                }
                if (directory is null)
                {
                    throw new InvalidOperationException("The repository root was not found.");
                }
                return directory.FullName;
            }
        }
    }
}
