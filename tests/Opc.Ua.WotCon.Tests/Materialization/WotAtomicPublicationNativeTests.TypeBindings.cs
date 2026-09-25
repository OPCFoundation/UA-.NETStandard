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

using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeTypeBindingResolvesTheCapturedThingModel(bool documentIri)
        {
            m_coordinator.Dispose();
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: new WotNodeSetDocumentConverter())
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            await AddNativePredecessorAsync().ConfigureAwait(false);
            string href = documentIri
                ? "urn:r30:native-predecessor" : "nsu=urn:r30:native-predecessor-model;i=6000";
            WotRegistryMutationResult added = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "document-typed-instance",
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(Encoding.UTF8.GetBytes($$$"""
                    {
                      "@context": ["https://www.w3.org/2022/wot/td/v1.1", {
                        "uav":"http://opcfoundation.org/UA/WoT-Binding/",
                        "ua":"http://opcfoundation.org/UA/"
                      }],
                      "@type":"Thing",
                      "id":"urn:m0:document-typed-instance",
                      "title":"TypedInstance",
                      "securityDefinitions":{"nosec_sc":{"scheme":"nosec"}},
                      "security":["nosec_sc"],
                      "uav:id":"nsu=urn:m0:document-typed-instance-model;i=5000",
                      "links":[{"rel":"ua:HasTypeDefinition","href":"{{{href}}}"}]
                    }
                    """))
            }).ConfigureAwait(false);
            Assert.That(added.Changed, Is.True, added.Message);

            WotRefreshResult result = await m_coordinator.RefreshAsync(new WotRefreshRequest
            {
                RequestId = "m0-document-type",
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }
            }).ConfigureAwait(false);

            Assert.That(result.Summary.Failed, Is.Zero,
                string.Join("; ", result.Results.Select(row => $"{row.ResourceId}: {row.Message}")));
            WotResource active = m_registry.Current.FindResourceByXid(added.Resource!.Xid)!;
            Assert.That(active.ActiveVersionId, Is.EqualTo("v1"));
            Assert.That(active.RootNodeId.IsNull, Is.False);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            (_, _, ArrayOf<ReferenceDescription> references) = await m_session.BrowseAsync(
                null, null, active.RootNodeId, 0, BrowseDirection.Forward, Ua.ReferenceTypeIds.HasTypeDefinition,
                false, (uint)NodeClass.ObjectType, CancellationToken.None).ConfigureAwait(false);
            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(ExpandedNodeId.ToNodeId(references[0].NodeId, m_session.NamespaceUris), Is.EqualTo(
                new NodeId(6000u, (ushort)m_session.NamespaceUris.GetIndex("urn:r30:native-predecessor-model"))));
        }
    }
}
