// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    /// Locks down the verified round trip of a whole companion information
    /// model, including structured preservation when readable mapping is incomplete.
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
        public async Task DeviceIntegrationModelSurvivesVerifiedDocumentSetRoundTripAsync()
        {
            await AssertModelRoundTripsAsync("Opc.Ua.Di.NodeSet2.xml").ConfigureAwait(false);
        }

        [Test]
        public async Task JobControlModelSurvivesVerifiedDocumentSetRoundTripAsync()
        {
            await AssertModelRoundTripsAsync("Isa95JobControl.NodeSet2.xml").ConfigureAwait(false);
        }

        [Test]
        public async Task DemoModelSurvivesVerifiedDocumentSetRoundTripAsync()
        {
            await AssertModelRoundTripsAsync("DemoModel.NodeSet2.xml").ConfigureAwait(false);
        }

        private static async Task AssertModelRoundTripsAsync(string fileName)
        {
            UANodeSet source = ReadCompanionModel(fileName);

            var options = new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Never
            };
            WotConversionResult<WotDocumentSet> documents =
                await WotNodeSetConverter.FromNodeSetDocumentsAsync(
                    source, "model", options: options).ConfigureAwait(false);
            Assert.That(
                documents.Success, Is.True,
                string.Join("; ", documents.Diagnostics.Select(d => d.ToString())));

            using WotDocumentSet set = documents.Value!;
            foreach (WotDocumentSetEntry entry in set.Entries)
            {
                Assert.That(entry.Document.TryGetEnvelope(out _), Is.False);
            }
            WotConversionResult<UANodeSet> restored =
                await WotNodeSetConverter.ToNodeSetAsync(set, options).ConfigureAwait(false);
            Assert.That(
                restored.Success, Is.True,
                string.Join("; ", restored.Diagnostics.Select(d => d.ToString())));
            NodeSetComparisonResult comparison =
                WotNodeSetConverter.CompareDocumentSet(source, restored.Value!, options);
            Assert.That(
                comparison.AreEquivalent, Is.True,
                $"{fileName}: {string.Join("; ", comparison.Differences)}");
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
