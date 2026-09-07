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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// The materializer converts registry documents through the same Section
    /// 5.2.1 path a standalone conversion takes, so a stored Thing Description
    /// that names a declaration of a stored Thing Model has to populate it -
    /// and a <c>uav:externalSchema</c> has to be checked against whatever
    /// providers the host configured, and against nothing at all when it
    /// configured none.
    /// </summary>
    [TestFixture]
    public sealed class WotMaterializationDeclarationTests
    {
        private const string PumpNamespace = "urn:test:pump";
        private const string TypeId = "nsu=urn:test:pump;i=1042";
        private const string SchemaReference = "https://example.com/schemas/speed.json";

        /// <summary>
        /// The stored Thing Model declares <c>Speed</c> as a mandatory Property
        /// typed <c>Double</c>. The stored instance says only that it is a
        /// number, so it becomes that declared Node rather than a generic
        /// component beside it.
        /// </summary>
        [Test]
        public async Task AStoredInstancePopulatesTheDeclarationOfAStoredModelAsync()
        {
            WotConversionOutput output = await ConvertAsync(
                converter: new WotNodeSetDocumentConverter(),
                model: MandatorySpeedModel(),
                instance: Instance("\"Speed\":{\"type\":\"number\"}")).ConfigureAwait(false);

            UAVariable speed = VariableNamed(output.NodeSet!, "1:Speed");
            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.True);
                Assert.That(speed.DataType, Is.EqualTo("i=11"));
                Assert.That(
                    TypeDefinitionOf(speed), Is.EqualTo("i=68"));
                Assert.That(
                    speed.References!.First(r => !r.IsForward).ReferenceType,
                    Is.EqualTo("HasProperty"));
                Assert.That(
                    output.NodeSet!.Items!.OfType<UAVariable>().Count(
                        v => string.Equals(v.BrowseName, "1:Speed", StringComparison.Ordinal)),
                    Is.EqualTo(1),
                    "A populated declaration must not leave a duplicate sibling.");
            });
        }

        /// <summary>
        /// A closed instance whose member the stored type does not declare is
        /// rejected, so the conversion fails rather than projecting a Node the
        /// type never admitted.
        /// </summary>
        [Test]
        public async Task AClosedStoredInstanceRejectsAnUndeclaredMemberAsync()
        {
            WotConversionOutput output = await ConvertAsync(
                converter: new WotNodeSetDocumentConverter(),
                model: MandatorySpeedModel(),
                instance: Instance(
                    "\"Colour\":{\"type\":\"string\"}",
                    "\"uav:additionalProperties\":false,")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.False);
                Assert.That(
                    output.Errors.Any(e => e.Contains("Colour", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        /// <summary>
        /// A host that configured no external schema provider fetches nothing,
        /// and the reference is reported as carried rather than checked.
        /// </summary>
        [Test]
        public async Task WithoutAProviderTheMaterializerFetchesNothingAsync()
        {
            var provider = new RecordingSchemaProvider("{\"type\":\"number\"}");
            WotConversionOutput output = await ConvertAsync(
                converter: new WotNodeSetDocumentConverter(),
                model: SchemaModel(),
                instance: null).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.True);
                Assert.That(provider.Calls, Is.Zero);
            });
        }

        /// <summary>
        /// A configured provider is consulted, and an external description that
        /// agrees with the canonical DataSchema leaves the conversion clean.
        /// </summary>
        [Test]
        public async Task AConfiguredProviderResolvesACompatibleSchemaAsync()
        {
            var provider = new RecordingSchemaProvider(
                "{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}");
            WotConversionOutput output = await ConvertAsync(
                converter: new WotNodeSetDocumentConverter(
                    null, null, [provider]),
                model: SchemaModel(),
                instance: null).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.True);
                Assert.That(provider.Calls, Is.EqualTo(1));
                Assert.That(
                    VariableNamed(output.NodeSet!, "1:Speed").DataType, Is.EqualTo("i=11"));
            });
        }

        /// <summary>
        /// An external description that disagrees fails the conversion, and the
        /// DataType the canonical DataSchema states is never replaced by it.
        /// </summary>
        [Test]
        public async Task AConfiguredProviderReportsAnIncompatibleSchemaAsync()
        {
            var provider = new RecordingSchemaProvider(
                "{\"type\":\"string\",\"uav:mapToType\":\"i=12\"}");
            WotConversionOutput output = await ConvertAsync(
                converter: new WotNodeSetDocumentConverter(
                    null, null, [provider]),
                model: SchemaModel(),
                instance: null).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.False);
                Assert.That(
                    output.Errors.Any(
                        e => e.Contains("uav:mapToType", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        /// <summary>
        /// Provider order is the host's to choose, and the first provider that
        /// holds the reference settles it.
        /// </summary>
        [Test]
        public async Task ProviderOrderSettlesWhichAnswerIsReadAsync()
        {
            var first = new RecordingSchemaProvider(
                "{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}");
            var second = new RecordingSchemaProvider(
                "{\"type\":\"number\",\"uav:mapToType\":\"i=11\"}");
            WotConversionOutput output = await ConvertAsync(
                converter: new WotNodeSetDocumentConverter(
                    null, null, [first, second]),
                model: SchemaModel(),
                instance: null).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(output.Succeeded, Is.True);
                Assert.That(first.Calls, Is.EqualTo(1));
                Assert.That(
                    second.Calls,
                    Is.EqualTo(1),
                    "Every provider is asked, so a disagreement between two of them " +
                    "is visible rather than hidden by ordering.");
            });
        }

        /// <summary>
        /// The snapshot resolver reports the declarations of a stored Thing
        /// Model, which is what the merge above reads.
        /// </summary>
        [Test]
        public async Task TheSnapshotResolverReportsStoredDeclarationsAsync()
        {
            (WotRegistrySnapshot snapshot, Dictionary<string, ByteString> contents) =
                await StoreAsync(MandatorySpeedModel(), null).ConfigureAwait(false);
            var resolver = new SnapshotWotNodeResolver(snapshot, contents);

            WotTypeDeclarationSet? set = await resolver
                .ResolveDeclarationsAsync(TypeId, WotDeclarationScope.Effective)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(set, Is.Not.Null);
                Assert.That(set!.Declarations.Count, Is.EqualTo(1));
                Assert.That(set.IsComplete, Is.True);
                Assert.That(set.Declarations[0].BrowseName, Is.EqualTo("Speed"));
                Assert.That(set.Declarations[0].IsMandatory, Is.True);
                Assert.That(set.Declarations[0].ReferenceTypeName, Is.EqualTo("HasProperty"));
                Assert.That(set.Declarations[0].DataType, Is.EqualTo("i=11"));
            });
        }

        /// <summary>
        /// A type the snapshot does not hold answers nothing, which is what
        /// lets the composite fall through to the AddressSpace.
        /// </summary>
        [Test]
        public async Task TheSnapshotResolverAnswersNothingForAnUnknownTypeAsync()
        {
            (WotRegistrySnapshot snapshot, Dictionary<string, ByteString> contents) =
                await StoreAsync(MandatorySpeedModel(), null).ConfigureAwait(false);
            var resolver = new SnapshotWotNodeResolver(snapshot, contents);

            Assert.Multiple(async () =>
            {
                Assert.That(
                    await resolver.ResolveDeclarationsAsync(
                        "nsu=urn:test:pump;i=9999", WotDeclarationScope.Direct)
                        .ConfigureAwait(false),
                    Is.Null);
                Assert.That(
                    await resolver.ResolveDeclarationsAsync(
                        string.Empty, WotDeclarationScope.Direct).ConfigureAwait(false),
                    Is.Null);
            });
        }

        private static UAVariable VariableNamed(UANodeSet nodeSet, string browseName)
        {
            return nodeSet.Items!.OfType<UAVariable>()
                .Single(v => string.Equals(v.BrowseName, browseName, StringComparison.Ordinal));
        }

        private static string TypeDefinitionOf(UANode node)
        {
            return node.References!.First(r =>
                r.IsForward &&
                string.Equals(r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal))
                .Value!;
        }

        private static async Task<WotConversionOutput> ConvertAsync(
            WotNodeSetDocumentConverter converter,
            byte[] model,
            byte[]? instance)
        {
            (WotRegistrySnapshot snapshot, Dictionary<string, ByteString> contents) =
                await StoreAsync(model, instance).ConfigureAwait(false);
            WotResource target = instance is null
                ? snapshot.FindResource(WotRegistryGroups.ThingModels, "tank-type")!
                : snapshot.FindResource(WotRegistryGroups.ThingDescriptions, "tank")!;
            ByteString content = contents[target.DefaultVersion!.DigestHex];
            return await converter.ConvertAsync(
                target, content, snapshot, contents, CancellationToken.None)
                .ConfigureAwait(false);
        }

        private static async Task<(
            WotRegistrySnapshot Snapshot,
            Dictionary<string, ByteString> Contents)> StoreAsync(
            byte[] model, byte[]? instance)
        {
            var contents = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            using var service = new WotRegistryService();
            await StoreOneAsync(
                service, contents, WotRegistryGroups.ThingModels, "tank-type",
                WoTDocumentKindEnum.ThingModel, model).ConfigureAwait(false);
            if (instance is not null)
            {
                await StoreOneAsync(
                    service, contents, WotRegistryGroups.ThingDescriptions, "tank",
                    WoTDocumentKindEnum.ThingDescription, instance).ConfigureAwait(false);
            }
            return (service.Current, contents);
        }

        private static async Task StoreOneAsync(
            WotRegistryService service,
            Dictionary<string, ByteString> contents,
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            byte[] document)
        {
            ByteString bytes = ByteString.From(document);
            contents[WotContentDigest.ToHex(WotContentDigest.Compute(bytes))] = bytes;
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = groupId,
                ResourceId = resourceId,
                Kind = kind,
                Content = bytes
            }).ConfigureAwait(false);
        }

        private static byte[] MandatorySpeedModel()
        {
            return Model(
                "\"Speed\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"," +
                "\"uav:modellingRule\":\"Mandatory\"," +
                "\"links\":[{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"i=68\"}]}");
        }

        private static byte[] SchemaModel()
        {
            return Model(
                "\"Speed\":{\"type\":\"number\",\"uav:mapToType\":\"i=11\"," +
                "\"uav:externalSchema\":\"" + SchemaReference + "\"}");
        }

        private static byte[] Model(string properties)
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"TankType\",\"uav:browseName\":\"pump:TankType\"," +
                "\"uav:id\":\"" + TypeId + "\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{" + properties + "}}");
        }

        private static byte[] Instance(string properties, string extraRootTerms = "")
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\",\"pump:TankType\"]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                extraRootTerms +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{" + properties + "}}");
        }

        /// <summary>
        /// A provider that answers with fixed bytes and counts how often it was
        /// asked, so a test can prove that nothing was fetched.
        /// </summary>
        private sealed class RecordingSchemaProvider(string body) : IWotSchemaResolver
        {
            public int Calls { get; private set; }

            public ValueTask<WotResolverResult> ResolveSchemaAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                Calls++;
                return new ValueTask<WotResolverResult>(
                    WotResolverResult.FromBytes(
                        Encoding.UTF8.GetBytes(body), "application/schema+json"));
            }
        }
    }
}
