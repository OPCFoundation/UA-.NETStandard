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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Locks down the readable round trip of a whole companion information
    /// model: NodeSet to a set of linked WoT documents and back.
    /// </summary>
    /// <remarks>
    /// This is the measure that matters for WoT Binding §9.1 and the
    /// completeness contract of §6.11.8. Anything the readable vocabulary
    /// cannot express has to fall back to the <c>uav:nodes</c> projection, so
    /// every Node that fails to survive here is a Node that forces the
    /// projection onto a document that should not need it.
    ///
    /// Both directions of the comparison matter and are asserted separately. A
    /// Node that disappears is an obvious loss; a Node that appears from
    /// nowhere is a quieter fault, because the address space still browses and
    /// only turns out to be wrong when something resolves an identity that was
    /// never in the source.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    public sealed class WotCompanionModelRoundTripTests
    {
        [Test]
        public async Task DeviceIntegrationModelSurvivesTheReadableRoundTripAsync()
        {
            await AssertModelRoundTripsAsync("Opc.Ua.Di.NodeSet2.xml").ConfigureAwait(false);
        }

        [Test]
        public async Task JobControlModelSurvivesTheReadableRoundTripAsync()
        {
            await AssertModelRoundTripsAsync("Isa95JobControl.NodeSet2.xml").ConfigureAwait(false);
        }

        [Test]
        public async Task DemoModelSurvivesTheReadableRoundTripAsync()
        {
            await AssertModelRoundTripsAsync("DemoModel.NodeSet2.xml").ConfigureAwait(false);
        }

        private static async Task AssertModelRoundTripsAsync(string fileName)
        {
            UANodeSet source = ReadCompanionModel(fileName);

            WotConversionResult<WotDocumentSet> documents =
                WotNodeSetConverter.FromNodeSetDocuments(source, "model");
            Assert.That(
                documents.Diagnostics
                    .Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);

            using WotDocumentSet set = documents.Value!;
            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(set).ConfigureAwait(false);

            Assert.That(restored.Value, Is.Not.Null);

            Dictionary<string, UANode> before = IndexByNodeId(source);
            Dictionary<string, UANode> after = IndexByNodeId(restored.Value!);

            string[] lost = before.Keys.Where(id => !after.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert.That(
                lost,
                Is.Empty,
                $"{fileName}: {lost.Length} Node(s) did not survive the readable " +
                "round trip, so the model still needs the uav:nodes projection.");

            string[] invented = after.Keys.Where(id => !before.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert.That(
                invented,
                Is.Empty,
                $"{fileName}: {invented.Length} Node(s) were created that the " +
                "source never stated.");

            // Same identity is not the same Node. A NodeClass that changes on
            // the way back is how a VariableType once became an ObjectType
            // while the counts still balanced.
            foreach (KeyValuePair<string, UANode> entry in before)
            {
                Assert.That(
                    after[entry.Key].GetType(),
                    Is.EqualTo(entry.Value.GetType()),
                    $"{fileName}: '{entry.Value.BrowseName}' came back as a " +
                    "different NodeClass.");
            }
        }

        private static Dictionary<string, UANode> IndexByNodeId(UANodeSet nodeSet)
        {
            var index = new Dictionary<string, UANode>(StringComparer.Ordinal);
            foreach (UANode node in nodeSet.Items ?? [])
            {
                if (!string.IsNullOrEmpty(node.NodeId))
                {
                    index[node.NodeId!] = node;
                }
            }
            return index;
        }

        private static UANodeSet ReadCompanionModel(string fileName)
        {
            string path = Path.Combine(
                RepositoryRoot,
                "tests",
                "Opc.Ua.SourceGeneration.Core.Tests",
                "Resources",
                fileName);
            Assert.That(File.Exists(path), Is.True, $"'{path}' should exist.");
            using FileStream stream = File.OpenRead(path);
            UANodeSet? nodeSet = UANodeSet.Read(stream);
            Assert.That(nodeSet, Is.Not.Null);
            return nodeSet!;
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
