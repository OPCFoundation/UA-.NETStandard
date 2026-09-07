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

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotLinkedModelMaterializationTests
    {
        [Test]
        public async Task DisjointDocumentsOfOneModelActivateAsOneSource()
        {
            using var registry = new WotRegistryService();
            await AddAsync(registry, "first", Partition("First")).ConfigureAwait(false);
            await AddAsync(registry, "second", Partition("Second")).ConfigureAwait(false);
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, new WotProtocolBinderRegistry([]));

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(result.Results.Select(item => item.Outcome), Is.All.EqualTo(WoTOutcomeEnum.Success),
                string.Join("; ", result.Results.Select(item => item.Message)));
            Assert.That(host.AddCount, Is.EqualTo(1));
            WotProjectionDocument projected = host.Operations.Single().Document!;
            Assert.That(projected.Sources, Has.Length.EqualTo(1));
            Assert.That(projected.BindingPlans.Count, Is.EqualTo(2));
            using var xml = new MemoryStream(projected.Sources[0].NodeSetXml, writable: false);
            UANodeSet merged = UANodeSet.Read(xml)!;
            Assert.That(merged.Models, Has.Length.EqualTo(1));
            Assert.That(merged.Models![0].ModelUri, Is.EqualTo(kNamespace));
            Assert.That(merged.Items!.Select(node => node.NodeId), Is.EquivalentTo(s_nodeIds));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConflictingModelPartitionsNeverReachTheProjectionHost(bool conflictingHeader)
        {
            using var registry = new WotRegistryService();
            await AddAsync(registry, "first", Partition("First")).ConfigureAwait(false);
            UANodeSet second = Partition(conflictingHeader ? "Second" : "First");
            if (conflictingHeader)
            {
                second.Models![0].Version = "2.0.0";
            }
            await AddAsync(registry, "second", second).ConfigureAwait(false);
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, new WotProtocolBinderRegistry([]));

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(result.Results.Any(item => item.Outcome == WoTOutcomeEnum.Failed), Is.True);
            Assert.That(host.AddCount, Is.Zero);
        }

        private static async Task AddAsync(WotRegistryService registry, string resourceId, UANodeSet source)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source, options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(document.Utf8Json.Span)
            }).ConfigureAwait(false);
        }

        private static UANodeSet Partition(string name)
        {
            return new UANodeSet
            {
                NamespaceUris = [kNamespace],
                Models = [new ModelTableEntry { ModelUri = kNamespace, Version = "1.0.0" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;s=" + name,
                        BrowseName = "1:" + name,
                        DisplayName = [new Export.LocalizedText { Value = name }],
                        References =
                        [
                            new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=58" }
                        ]
                    }
                ]
            };
        }

        private const string kNamespace = "urn:linked-model";
        private static readonly string[] s_nodeIds = ["ns=1;s=First", "ns=1;s=Second"];
    }
}
