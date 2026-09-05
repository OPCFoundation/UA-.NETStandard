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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// The materializer converts stored documents, so what a standalone
    /// conversion resolves through its local context a materialization has to
    /// resolve through the registry. These pin the two paths that only exist
    /// once documents are stored together: a DataType definition ingested with
    /// its document, and a parent placement that names a sibling.
    /// </summary>
    [TestFixture]
    public sealed class WotMaterializationIngestionTests
    {
        private const string PumpNamespace = "urn:test:pump";

        /// <summary>
        /// A stored Thing Model that defines a Structure DataType materializes
        /// the DataType Node alongside the type that uses it, so an instance
        /// stored later is typed against a DataType the AddressSpace holds
        /// rather than against a built-in stand-in.
        /// </summary>
        [Test]
        public async Task AStoredDataTypeDefinitionMaterializesItsDataTypeNodeAsync()
        {
            WotConversionOutput output = await ConvertAsync(
                "tm-datatypes", WoTDocumentKindEnum.ThingModel, DataTypeModel())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.True, string.Join("; ", output.Errors));
                Assert.That(
                    output.NodeSet!.Items!.OfType<UADataType>().Select(d => d.BrowseName),
                    Does.Contain("1:PumpReading"));
                UAVariable reading = output.NodeSet.Items!.OfType<UAVariable>()
                    .Single(v => string.Equals(
                        v.BrowseName, "1:Reading", StringComparison.Ordinal));
                UADataType defined = output.NodeSet.Items!.OfType<UADataType>()
                    .Single(d => string.Equals(
                        d.BrowseName, "1:PumpReading", StringComparison.Ordinal));
                Assert.That(
                    reading.DataType,
                    Is.EqualTo(defined.NodeId),
                    "The Variable is typed against the DataType the same document defines.");
            });
        }

        /// <summary>
        /// A malformed DataType definition fails its own document's conversion
        /// whole. Materializing the half that parsed would leave a Variable
        /// typed against a DataType whose fields nobody agreed on.
        /// </summary>
        [Test]
        public async Task AMalformedDataTypeDefinitionFailsTheWholeDocumentAsync()
        {
            WotConversionOutput output = await ConvertAsync(
                "tm-broken", WoTDocumentKindEnum.ThingModel, MalformedDataTypeModel())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.False);
                Assert.That(output.NodeSet, Is.Null);
                Assert.That(output.Errors, Is.Not.Empty);
            });
        }

        /// <summary>
        /// A malformed document is reported and does not take the registry with
        /// it: a refresh over both still projects the well-formed one, which is
        /// what makes one bad upload a per-document failure rather than an
        /// outage.
        /// </summary>
        [Test]
        public async Task AMalformedDocumentDoesNotStopTheRefreshOfAnotherAsync()
        {
            using var registry = new WotRegistryService();
            var host = new FakeWotProjectionHost();
            using var coordinator = new WotMaterializationCoordinator(
                registry, host, documentConverter: new WotNodeSetDocumentConverter());
            await StoreAsync(
                registry, WotRegistryGroups.ThingModels, "tm-good",
                WoTDocumentKindEnum.ThingModel, DataTypeModel()).ConfigureAwait(false);
            await StoreAsync(
                registry, WotRegistryGroups.ThingModels, "tm-broken",
                WoTDocumentKindEnum.ThingModel, MalformedDataTypeModel())
                .ConfigureAwait(false);

            WotRefreshResult result = await coordinator
                .RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Results.Single(r => r.ResourceId == "tm-broken").Outcome,
                    Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(
                    result.Results.Single(r => r.ResourceId == "tm-good").Outcome,
                    Is.EqualTo(WoTOutcomeEnum.Success).Or.EqualTo(WoTOutcomeEnum.Warning));
                Assert.That(
                    registry.Current
                        .FindResource(WotRegistryGroups.ThingModels, "tm-broken")!.LoadState,
                    Is.EqualTo(WoTLoadStateEnum.Failed));
            });
        }

        /// <summary>
        /// WoT Connectivity Section 7.3 lets a stored document say which Node
        /// contains it. A sibling in the registry is the first place that is
        /// looked for.
        /// </summary>
        [Test]
        public async Task AParentPlacementResolvesThroughASiblingDocumentAsync()
        {
            using var registry = new WotRegistryService();
            var contents = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            await StoreAsync(
                registry, WotRegistryGroups.ThingDescriptions, "plant",
                WoTDocumentKindEnum.ThingDescription, Plant(), contents)
                .ConfigureAwait(false);
            await StoreAsync(
                registry, WotRegistryGroups.ThingDescriptions, "pump",
                WoTDocumentKindEnum.ThingDescription, PumpUnder("urn:test:plant"), contents)
                .ConfigureAwait(false);

            WotConversionOutput output = await ConvertStoredAsync(
                registry, contents, WotRegistryGroups.ThingDescriptions, "pump")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.True, string.Join("; ", output.Errors));
                Assert.That(
                    output.NodeSet!.Items![0].References!.Any(
                        r => !r.IsForward &&
                            string.Equals(r.ReferenceType, "HasComponent", StringComparison.Ordinal)),
                    Is.True,
                    "The projected Object is a component of the Node it named.");
            });
        }

        /// <summary>
        /// A parent nothing holds fails the projection rather than silently
        /// rooting the Object somewhere else: a Node placed under a parent
        /// nobody chose is worse than a reported failure.
        /// </summary>
        [Test]
        public async Task AParentPlacementNothingHoldsFailsTheProjectionAsync()
        {
            WotConversionOutput output = await ConvertAsync(
                "pump", WoTDocumentKindEnum.ThingDescription,
                PumpUnder("urn:test:missing")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.False);
                Assert.That(output.FailurePhase, Is.EqualTo(WoTPhaseEnum.Projection));
                Assert.That(
                    output.Errors.Any(
                        e => e.Contains("urn:test:missing", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        private static async Task<WotConversionOutput> ConvertAsync(
            string resourceId, WoTDocumentKindEnum kind, byte[] document)
        {
            using var registry = new WotRegistryService();
            var contents = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            string groupId = kind == WoTDocumentKindEnum.ThingModel
                ? WotRegistryGroups.ThingModels
                : WotRegistryGroups.ThingDescriptions;
            await StoreAsync(registry, groupId, resourceId, kind, document, contents)
                .ConfigureAwait(false);
            return await ConvertStoredAsync(registry, contents, groupId, resourceId)
                .ConfigureAwait(false);
        }

        private static async Task<WotConversionOutput> ConvertStoredAsync(
            WotRegistryService registry,
            Dictionary<string, ByteString> contents,
            string groupId,
            string resourceId)
        {
            WotRegistrySnapshot snapshot = registry.Current;
            WotResource resource = snapshot.FindResource(groupId, resourceId)!;
            var converter = new WotNodeSetDocumentConverter();
            return await converter.ConvertAsync(
                resource,
                contents[resource.DefaultVersion!.DigestHex],
                snapshot,
                contents,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task StoreAsync(
            WotRegistryService registry,
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            byte[] document,
            Dictionary<string, ByteString>? contents = null)
        {
            ByteString bytes = ByteString.From(document);
            contents?.Add(WotContentDigest.ToHex(WotContentDigest.Compute(bytes)), bytes);
            await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = groupId,
                ResourceId = resourceId,
                Kind = kind,
                Content = bytes
            }).ConfigureAwait(false);
        }

        private static byte[] DataTypeModel()
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"PumpType\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=" + PumpNamespace + ";i=1042\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"uav:dataTypeDefinitions\":[" +
                "{\"@id\":\"urn:test:dtd#PumpReading\"," +
                "\"@type\":\"uav:StructureDefinition\"," +
                "\"uav:dataTypeName\":\"pump:PumpReading\"," +
                "\"uav:structureType\":\"Structure\"," +
                "\"uav:fields\":[" +
                "{\"@type\":\"uav:StructureField\",\"uav:fieldName\":\"Value\"," +
                "\"uav:fieldDataTypeName\":\"ua:Double\",\"uav:fieldDataTypeId\":\"i=11\"," +
                "\"uav:valueRank\":-1,\"uav:isOptional\":false," +
                "\"uav:allowSubtypes\":false}]}]," +
                "\"properties\":{\"Reading\":{\"type\":\"object\"," +
                "\"uav:dataTypeDefinition\":{\"@id\":\"urn:test:dtd#PumpReading\"}}}}");
        }

        private static byte[] MalformedDataTypeModel()
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"BrokenType\",\"uav:browseName\":\"pump:BrokenType\"," +
                "\"uav:id\":\"nsu=" + PumpNamespace + ";i=1099\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{\"Reading\":{\"type\":\"number\"," +
                // Two definitive DataType statements that name different types:
                // the document contradicts itself rather than choosing.
                "\"uav:mapToType\":\"i=11\",\"uav:dataTypeId\":\"i=12\"}}}");
        }

        private static byte[] Plant()
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"]," +
                "\"id\":\"urn:test:plant\"," +
                "\"title\":\"Plant\",\"uav:browseName\":\"pump:Plant\"," +
                "\"uav:id\":\"nsu=" + PumpNamespace + ";i=2001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}}");
        }

        private static byte[] PumpUnder(string parentHref)
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"]," +
                "\"id\":\"urn:test:pump-instance\"," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:Pump\"," +
                "\"uav:id\":\"nsu=" + PumpNamespace + ";i=2002\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"links\":[{\"rel\":\"uav:componentOf\",\"href\":\"" +
                parentHref + "\"}]}");
        }
    }
}
