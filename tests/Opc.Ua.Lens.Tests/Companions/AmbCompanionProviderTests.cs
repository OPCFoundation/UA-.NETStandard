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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AMB.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed partial class AmbCompanionProviderTests
    {
        [Test]
        public async Task AliasDiscoveryDeduplicatesAssetsWithoutAssumingOneObjectTypeAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget asset) = CreateAsset();
            var client = new AmbClient(session.Session.Object, session.Telemetry, new AmbClientOptions());
            AddAlias(session, client.GetCategoryId(AssetAliasCategory.ByAssetId), asset.NodeId, "operator-id");

            ArrayOf<CompanionTarget> found = await new AmbCompanionProvider().DiscoverAsync(
                session.Context(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].NodeId, Is.EqualTo(asset.NodeId));
            Assert.That(found[0].TypeName, Is.EqualTo("Managed asset"));
            Assert.That(found[0].ProviderId, Is.EqualTo("amb"));
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task IdentificationKeepsSourceIdentityAndTrueOptionalAbsenceAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget asset) = CreateAsset();
            NodeId source = session.AddProperty(asset.NodeId, Opc.Ua.Di.Namespaces.OpcUaDi,
                Opc.Ua.Di.BrowseNames.AssetId, Variant.From("ASSET-001"));
            CompanionInspection result = await new AmbCompanionProvider().InspectAsync(
                session.Context(), asset, CancellationToken.None).ConfigureAwait(false);

            CompanionValue identifier = result.Values.ToList().Single(value => value.Name.EndsWith(
                "/ AssetId", StringComparison.Ordinal));
            Assert.That(identifier.SourceNodeId, Is.EqualTo(source));
            Assert.That(identifier.Value, Is.EqualTo(Variant.From("ASSET-001")));
            Assert.That(identifier.Quality, Is.EqualTo(StatusCodes.Good));
            CompanionValue manufacturer = result.Values.ToList().Single(value => value.Name.EndsWith(
                "/ Manufacturer", StringComparison.Ordinal));
            Assert.That(manufacturer.SourceNodeId.IsNull, Is.True);
            Assert.That(manufacturer.Value, Is.EqualTo(Variant.From("Not exposed")));
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task PresentNullValueDoesNotBecomeAnAbsentPropertyAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget asset) = CreateAsset();
            NodeId source = session.AddProperty(asset.NodeId, Opc.Ua.Di.Namespaces.OpcUaDi,
                Opc.Ua.Di.BrowseNames.AssetId, Variant.Null);
            CompanionInspection result = await new AmbCompanionProvider().InspectAsync(
                session.Context(), asset, CancellationToken.None).ConfigureAwait(false);
            CompanionValue value = result.Values.ToList().Single(field => field.SourceNodeId == source);
            Assert.That(value.Value.IsNull, Is.True);
            Assert.That(value.Quality, Is.EqualTo(StatusCodes.Good));
        }

        [TestCase("denied")]
        [TestCase("uncertain")]
        [TestCase("wrong-type")]
        public async Task InvalidIdentificationRemainsAnErrorWithOriginalEvidenceAsync(string kind)
        {
            (IndustrialCompanionTestSession session, CompanionTarget asset) = CreateAsset();
            Variant payload = kind == "wrong-type" ? Variant.From(42) : Variant.From("private-id");
            NodeId source = session.AddProperty(asset.NodeId, Opc.Ua.Di.Namespaces.OpcUaDi,
                Opc.Ua.Di.BrowseNames.AssetId, payload);
            StatusCode status = kind switch
            {
                "denied" => StatusCodes.BadUserAccessDenied,
                "uncertain" => StatusCodes.Uncertain,
                _ => StatusCodes.Good
            };
            session.SetValue(source, payload, status: status);
            AmbValueReadException? failure = null;
            try
            {
                await new AmbCompanionProvider().InspectAsync(session.Context(), asset, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (AmbValueReadException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.ReadValue, Is.Not.Null);
            Assert.That(failure.ReadValue!.SourceNodeId, Is.EqualTo(source));
            Assert.That(failure.ReadValue.Value.WrappedValue, Is.EqualTo(payload));
            Assert.That(failure.ReadValue.Value.StatusCode, Is.EqualTo(status));
        }

        [Test]
        public async Task WithdrawnAliasCannotLeaveAnActionableAssetAsync()
        {
            (IndustrialCompanionTestSession session, CompanionTarget asset) = CreateAsset();
            var client = new AmbClient(session.Session.Object, session.Telemetry, new AmbClientOptions());
            session.SetReferences(client.GetCategoryId(AssetAliasCategory.ByProductInstanceUri),
                BrowseDirection.Forward, ReferenceTypeIds.Organizes, []);
            await Assert.ThatAsync(async () => await new AmbCompanionProvider().InspectAsync(
                session.Context(), asset, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNodeIdUnknown)).ConfigureAwait(false);
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task CancellationBeforeDiscoveryDoesNotStartProtocolWorkAsync()
        {
            (IndustrialCompanionTestSession session, _) = CreateAsset();
            await Assert.ThatAsync(async () => await new AmbCompanionProvider().DiscoverAsync(
                session.Context(), new CancellationToken(true)).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(session.BrowseCalls.IsEmpty, Is.True);
            Assert.That(session.Calls.IsEmpty, Is.True);
        }

        private static (IndustrialCompanionTestSession Session, CompanionTarget Asset) CreateAsset()
        {
            var session = new IndustrialCompanionTestSession();
            session.NamespaceUris.GetIndexOrAppend(Opc.Ua.AMB.Namespaces.AMB);
            NodeId asset = session.Id("asset");
            session.AddObject(asset, ObjectTypeIds.BaseObjectType, "Asset");
            session.AddProperty(asset, Opc.Ua.Di.Namespaces.OpcUaDi,
                Opc.Ua.Di.BrowseNames.ProductInstanceUri, Variant.From("urn:asset:one"));
            var client = new AmbClient(session.Session.Object, session.Telemetry, new AmbClientOptions());
            AddAlias(session, client.GetCategoryId(AssetAliasCategory.ByProductInstanceUri), asset, "urn:asset:one");
            return (session, new CompanionTarget("amb", asset, "Asset", "Managed asset"));
        }

        private static void AddAlias(
            IndustrialCompanionTestSession session, NodeId category, NodeId asset, string name)
        {
            NodeId alias = session.Id("alias-" + category);
            session.SetReferences(category, BrowseDirection.Forward, ReferenceTypeIds.Organizes,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(alias),
                    NodeClass = NodeClass.Object,
                    BrowseName = new QualifiedName(name, session.InstanceNamespaceIndex)
                }
            ]);
            session.SetReferences(alias, BrowseDirection.Forward, ReferenceTypeIds.AliasFor,
            [
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId(asset),
                    NodeClass = NodeClass.Object
                }
            ]);
        }
    }
}
