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

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    [Category("WoT")]
    [NonParallelizable]
    public sealed class WotProjectionViewCanonicalCoordinatorTests
    {
        [Test]
        public async Task AuthoredViewIdentityIsUsedByCoordinatorAsync()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            WotRegistryService registry = await runtime.CreateRegistryAsync().ConfigureAwait(false);
            using var viewHost = new LifecycleWotViewProjectionHost(runtime.Lifecycle);
            using var coordinator = new WotMaterializationCoordinator(
                registry, runtime.Host,
                documentConverter: new FakeWotDocumentConverter(),
                viewProjectionHost: viewHost);
            await runtime.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, registry, coordinator), callerContext: null)
                .ConfigureAwait(false);
            NamespaceTable namespaces = runtime.Namespaces;
            ushort viewNamespace = namespaces.GetIndexOrAppend("urn:c2:views");
            coordinator.ServerNamespaceUris = namespaces;
            WotRegistryMutationResult source = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "canonical-source",
                Content = ByteString.From(Encoding.UTF8.GetBytes(kSourceDocument))
            }).ConfigureAwait(false);
            Assert.That(source.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), source.Message);
            WotRegistryMutationResult projection = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "canonical-view",
                Format = "WoT-Projection/1.2",
                ContentType =
                    "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\"",
                Content = ByteString.From(Encoding.UTF8.GetBytes(kProjectionDocument))
            }).ConfigureAwait(false);
            Assert.That(projection.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), projection.Message);

            WotRefreshResult refreshed = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            WoTResourceLoadResultDataType result = refreshed.Results.Single(row => row.ResourceId == "canonical-view");
            Assert.That(result.LoadState, Is.EqualTo(WoTLoadStateEnum.Active), result.Message);
            var expected = new NodeId("AuthoredView", viewNamespace);
            Assert.That(result.RootNodeId, Is.EqualTo(expected));
            Assert.That(coordinator.CommittedPublication.Views.ToList().Single().ViewNodeId, Is.EqualTo(expected));
            Assert.That(registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "canonical-view")!.RootNodeId, Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnresolvedAuthoredIdentityDoesNotAllocateNamespaceStateAsync(bool suppliedTable)
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync()
                .ConfigureAwait(false);
            WotRegistryService registry = await runtime.CreateRegistryAsync().ConfigureAwait(false);
            using var viewHost = new LifecycleWotViewProjectionHost(runtime.Lifecycle);
            using var coordinator = new WotMaterializationCoordinator(
                registry, runtime.Host,
                documentConverter: new FakeWotDocumentConverter(),
                viewProjectionHost: viewHost);
            await runtime.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, registry, coordinator), callerContext: null)
                .ConfigureAwait(false);
            NamespaceTable namespaces = runtime.Namespaces;
            int originalCount = namespaces.Count;
            coordinator.ServerNamespaceUris = suppliedTable ? namespaces : null;
            WotRegistryMutationResult source = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "canonical-source",
                Content = ByteString.From(Encoding.UTF8.GetBytes(kSourceDocument))
            }).ConfigureAwait(false);
            Assert.That(source.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), source.Message);
            WotRegistryMutationResult projection = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "canonical-view",
                Format = "WoT-Projection/1.2",
                ContentType =
                    "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\"",
                Content = ByteString.From(Encoding.UTF8.GetBytes(kProjectionDocument))
            }).ConfigureAwait(false);
            Assert.That(projection.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), projection.Message);

            WotRefreshResult refreshed = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            WoTResourceLoadResultDataType result = refreshed.Results.Single(row => row.ResourceId == "canonical-view");
            Assert.Multiple(() =>
            {
                Assert.That(result.LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
                Assert.That(result.RootNodeId.IsNull, Is.True);
                Assert.That(coordinator.CommittedPublication.Views.IsEmpty, Is.True);
                Assert.That(namespaces.Count, Is.EqualTo(originalCount));
                Assert.That(namespaces.GetIndex("urn:c2:views"), Is.EqualTo(-1));
                if (suppliedTable)
                {
                    Assert.That(coordinator.ServerNamespaceUris, Is.SameAs(namespaces));
                }
                else
                {
                    Assert.That(coordinator.ServerNamespaceUris, Is.Null);
                }
            });
        }

        private const string kSourceDocument = """
            {
              "@context":"https://www.w3.org/2022/wot/td/v1.1",
              "id":"urn:c2:source",
              "title":"Source",
              "@type":"uav:object",
              "properties":{"Reading":{"type":"number","forms":[{"href":"reading"}]}}
            }
            """;

        private const string kProjectionDocument = """
            {
              "@context":[
                "https://www.w3.org/2022/wot/td/v1.1",
                {"uav":"http://opcfoundation.org/UA/WoT-Binding/","tm":"https://www.w3.org/2019/wot/tm#"}
              ],
              "@type":["uav:projection"],
              "uav:projectionKind":"ThingDescription",
              "id":"urn:c2:view",
              "title":"Canonical View",
              "uav:scenario":"urn:c2:scenario",
              "uav:id":"nsu=urn:c2:views;s=AuthoredView",
              "securityDefinitions":{"none":{"scheme":"nosec"}},
              "security":"none",
              "uav:projects":[
                {"uav:sourceName":"source","href":"urn:c2:source","type":"application/td+json",
                 "uav:routing":"source","uav:selectAll":true}
              ]
            }
            """;
    }
}
