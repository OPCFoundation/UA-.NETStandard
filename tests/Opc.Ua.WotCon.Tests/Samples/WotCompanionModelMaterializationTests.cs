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
using NUnit.Framework;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Materializes the companion models as sets of linked documents.
    /// </summary>
    /// <remarks>
    /// Explicit because it rewrites the checked-in sample document set. Run it
    /// when the converter's output changes, then commit what it produced:
    ///
    ///   dotnet test tests\Opc.Ua.WotCon.Tests --filter "FullyQualifiedName~WriteCompanionModelSets"
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    public sealed class WotCompanionModelMaterializationTests
    {
        [Test]
        [Explicit("Rewrites the checked-in sample documents.")]
        public void WriteCompanionModelSets()
        {
            string documents = Path.Combine(
                RepositoryRoot, "samples", "WotCon", "AggregationClient", "Documents");

            var written = new List<string>();
            string? previousModel = null;
            foreach ((string source, string directory, string title) in s_models)
            {
                IReadOnlyList<WotAggregationDocumentGenerator.GeneratedDocument> set =
                    WotAggregationDocumentGenerator.GenerateThingModelSet(
                        Path.Combine(RepositoryRoot, source), directory, title);

                IReadOnlyList<WotAggregationDocumentGenerator.ManifestEntry> entries =
                    WotAggregationDocumentGenerator.WriteThingModelSet(
                        documents, directory, set, previousModel);

                written.Add($"{directory}: {entries.Count} documents");
                previousModel = entries[^1].ResourceId;
            }

            foreach (string line in written)
            {
                TestContext.Out.WriteLine(line);
            }
            Assert.That(written, Is.Not.Empty);
        }

        private static readonly (string Source, string Directory, string Title)[] s_models =
        [
            (Path.Combine(
                "tests", "Opc.Ua.SourceGeneration.Core.Tests", "Resources",
                "Opc.Ua.Di.NodeSet2.xml"),
                "opc-ua-di",
                "OPC UA Device Integration"),
            (Path.Combine(
                "samples", "DI", "PumpDeviceIntegrationServer", "Model",
                "Opc.Ua.Machinery.NodeSet2.xml"),
                "opc-ua-machinery",
                "OPC UA Machinery"),
            (Path.Combine(
                "samples", "DI", "PumpDeviceIntegrationServer", "Model",
                "Opc.Ua.Pumps.NodeSet2.xml"),
                "opc-ua-pumps",
                "OPC UA Pumps")
        ];

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
