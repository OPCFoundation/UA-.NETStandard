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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Holds companion document-set conversion to full semantic preservation,
    /// including the structured fallback for facts the readable vocabulary
    /// cannot represent.
    /// </summary>
    /// <remarks>
    /// Node counts and NodeClasses do not prove completeness. These fixtures
    /// compare all source facts without an archival envelope; an incomplete
    /// readable candidate must retain its missing facts through verified
    /// structured preservation instead of claiming to be complete.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    public sealed class WotCompanionModelDocumentSetTests
    {
        [Test]
        public async Task DeviceIntegrationModelRetainsEverySourceFactAsync()
        {
            await AssertSetIsEquivalentAsync(
                Path.Combine(
                    RepositoryRoot,
                    "tests",
                    "Opc.Ua.SourceGeneration.Core.Tests",
                    "Resources",
                    "Opc.Ua.Di.NodeSet2.xml"),
                "opc-ua-di",
                "OPC UA Device Integration").ConfigureAwait(false);
        }

        [Test]
        public async Task MachineryModelRetainsEverySourceFactAsync()
        {
            await AssertSetIsEquivalentAsync(
                Path.Combine(
                    RepositoryRoot,
                    "samples",
                    "DI",
                    "PumpDeviceIntegrationServer",
                    "Model",
                    "Opc.Ua.Machinery.NodeSet2.xml"),
                "opc-ua-machinery",
                "OPC UA Machinery").ConfigureAwait(false);
        }

        [Test]
        public async Task PumpsModelRetainsEverySourceFactAsync()
        {
            await AssertSetIsEquivalentAsync(
                Path.Combine(
                    RepositoryRoot,
                    "samples",
                    "DI",
                    "PumpDeviceIntegrationServer",
                    "Model",
                    "Opc.Ua.Pumps.NodeSet2.xml"),
                "opc-ua-pumps",
                "OPC UA Pumps").ConfigureAwait(false);
        }

        [Test]
        public async Task SamplePumpRetainsRawRangesAndModelMetadataAsync()
        {
            await AssertSetIsEquivalentAsync(
                Path.Combine(
                    RepositoryRoot,
                    "samples",
                    "WotCon",
                    "AggregationClient",
                    "Documents",
                    "SamplePump.NodeSet2.xml"),
                "sample-pump",
                "Sample Pump Aggregate").ConfigureAwait(false);
        }

        private static async Task AssertSetIsEquivalentAsync(
            string sourcePath,
            string modelPrefix,
            string title)
        {
            Assert.That(File.Exists(sourcePath), Is.True, $"'{sourcePath}' should exist.");

            using FileStream stream = File.OpenRead(sourcePath);
            UANodeSet? source = UANodeSet.Read(stream);
            Assert.That(source, Is.Not.Null);

            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };
            WotConversionResult<WotDocumentSet> result =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    source!, modelPrefix, title, options).ConfigureAwait(false);
            Assert.That(
                result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.ToString())));
            using WotDocumentSet set = result.Value!;
            var entries = set.Entries.ToList();
            Assert.That(set.Entries.IsEmpty, Is.False);
            Assert.That(
                entries.Select(entry => entry.Href).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(entries.Count));
            foreach (WotDocumentSetEntry entry in entries)
            {
                Assert.That(entry.Document.TryGetEnvelope(out _), Is.False);
            }
            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(set, options).ConfigureAwait(false);
            Assert.That(
                restored.Success, Is.True, string.Join("; ", restored.Diagnostics.Select(d => d.ToString())));
            NodeSetComparisonResult comparison =
                WotNodeSetConverter.CompareDocumentSet(source!, restored.Value!, options);
            Assert.That(
                comparison.AreEquivalent, Is.True,
                $"{Path.GetFileName(sourcePath)}: {string.Join("; ", comparison.Differences)}");
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
