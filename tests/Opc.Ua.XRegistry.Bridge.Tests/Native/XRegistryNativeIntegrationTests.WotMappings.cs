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

using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    public sealed partial class XRegistryNativeIntegrationTests
    {
        [Test]
        public async Task ModelDrivenProfilesReadExistingWotProjectionWithoutChangingItsDomainImplementationAsync()
        {
            using var registry = new WotRegistryService();
            await registry.InitializeAsync().ConfigureAwait(false);
            const string document = """
                {"@context":"https://www.w3.org/2022/wot/td/v1.1","id":"urn:xregistry:mapped-pump",
                 "title":"Mapped pump","securityDefinitions":{"nosec_sc":{"scheme":"nosec"}},"security":["nosec_sc"]}
                """;
            WotRegistryMutationResult created = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "tds",
                ResourceId = "pump",
                VersionId = "v1",
                Content = ByteString.From(Encoding.UTF8.GetBytes(document)),
                Kind = WoTDocumentKindEnum.ThingDescription
            }).ConfigureAwait(false);
            Assert.That(created.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            using var coordinator = new WotMaterializationCoordinator(
                registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle));
            var factory = new WotRegistryNodeManagerFactory(new WotRegistryServerOptions
            {
                AutoRefresh = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                    AllowAnonymous = false,
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_Operator
                }
            }, registry, coordinator);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                factory, callerContext: null)
                .ConfigureAwait(false);
            try
            {
                await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
                using var model = JsonDocument.Parse("""
                    {"groups":{"groups":{"singular":"group","resources":{"resources":{"singular":"resource",
                      "attributes":{"thingtitle":{"type":"string"},"enabled":{"type":"boolean"}}}}}}}
                    """);
                XRegistryBridgeNativeOptions options = m_options with
                {
                    BaseModel = model.RootElement,
                    BasePropertyNamespaceUris = [WotCon.Namespaces.WotCon],
                    AttributeMappings =
                    [
                        new("/groups/resources", XRegistryNativeAttributeScope.Resource, ["thingtitle"],
                            [new(WotCon.Namespaces.WotCon, "ThingTitle")]),
                        new("/groups/resources", XRegistryNativeAttributeScope.Resource, ["enabled"],
                            [new(WotCon.Namespaces.WotCon, "Enabled")]),
                        new("/groups/resources", XRegistryNativeAttributeScope.Version, ["thingtitle"],
                            [new(WotCon.Namespaces.WotCon, "ThingTitle")]),
                        new("/groups/resources", XRegistryNativeAttributeScope.Version, ["enabled"],
                            [new(WotCon.Namespaces.WotCon, "Enabled")])
                    ]
                };
                var endpoint = new XRegistryOpcUaEndpoint(
                    m_session,
                    ExpandedNodeId.ToNodeId(WotCon.ObjectIds.WoTRegistry, m_session.NamespaceUris),
                    options,
                    m_telemetry);
                XRegistryResponse read = await endpoint.ExecuteAsync(Request(XRegistryAction.Read,
                    "/groups/tds/resources/pump/versions/v1") with
                { View = XRegistryView.Default }).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(read.StatusCode, Is.EqualTo(200), read.Error?.Detail);
                    Assert.That(read.Metadata.GetProperty("thingtitle").GetString(), Is.EqualTo("Mapped pump"));
                    Assert.That(read.Metadata.GetProperty("enabled").GetBoolean(), Is.True);
                    Assert.That(Encoding.UTF8.GetString(read.Document.ToArray()), Is.EqualTo(document));
                });
                XRegistryResponse denied = await endpoint.ExecuteAsync(
                    Request(XRegistryAction.Merge, "/groups/tds/resources/pump", """{"enabled":true}"""))
                    .ConfigureAwait(false);
                Assert.That(denied.StatusCode, Is.EqualTo(405),
                    "A mapping cannot add HTTP mutation guarantees to WoT.");
                XRegistryResponse root = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/"))
                    .ConfigureAwait(false);
                Assert.That(root.StatusCode, Is.EqualTo(405),
                    "Missing root counters cannot be replaced with fabricated zero.");
            }
            finally
            {
                await m_server.NodeManagerLifecycle.RemoveAsync(registration, callerContext: null)
                    .ConfigureAwait(false);
            }
        }
    }
}
