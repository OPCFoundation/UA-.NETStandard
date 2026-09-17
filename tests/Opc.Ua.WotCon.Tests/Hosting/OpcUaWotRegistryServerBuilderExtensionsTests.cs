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
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    /// <summary>
    /// Verifies that <c>AddWotRegistryServer</c> and
    /// <c>AddWotProtocolBinders</c>/<c>AddWotBinder</c>/<c>AddWotBindingExecutor</c>
    /// register the aggregating <see cref="WotProtocolBinderRegistry"/> exactly
    /// once and expose the same singleton instance as both
    /// <see cref="IWotBinderRegistry"/> and <see cref="IWotBindingChannelFactory"/>,
    /// regardless of registration order.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaWotRegistryServerBuilderExtensionsTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitProjectionCompatibilityReachesRegistryAndViewMaterialization(bool configuration)
        {
            var services = new ServiceCollection();
            var host = new FakeWotProjectionHost();
            var viewHost = new InMemoryWotViewProjectionHost();
            services.AddSingleton<IWotProjectionHost>(host);
            services.AddSingleton<IWotDocumentConverter>(new FakeWotDocumentConverter());
            services.AddSingleton<IWotViewProjectionHost>(viewHost);
            IOpcUaBuilder builder = services.AddOpcUa();
            if (configuration)
            {
                IConfiguration settings = new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [$"{OpcUaWotRegistryServerBuilderExtensions.DefaultConfigurationSection}:" +
                            "ProjectionCompatibilityMode"] = "DraftProjection11"
                    }).Build();
                builder.AddWotRegistryServer(settings);
            }
            else
            {
                builder.AddWotRegistryServer(options =>
                    options.ProjectionCompatibilityMode = WotProjectionCompatibilityMode.DraftProjection11);
            }
            using ServiceProvider provider = services.BuildServiceProvider();
            IWotRegistryService registry = provider.GetRequiredService<IWotRegistryService>();
            await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "plans",
                ResourceId = "source",
                Content = ByteString.From(TestMaterialization.Td("urn:source"))
            }).ConfigureAwait(false);
            WotRegistryMutationResult registered = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "plans",
                ResourceId = "view",
                Format = WotProjection.Format,
                ContentType = WotProjection.ContentType,
                Content = ByteString.From(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                    {
                      "@context":"https://www.w3.org/2022/wot/td/v1.1",
                      "@type":["Thing","uav:projection"],
                      "id":"urn:projection","title":"Projection",
                      "uav:scenario":"urn:scenario",
                      "securityDefinitions":{"none":{"scheme":"nosec"}},"security":"none",
                      "uav:projects":[
                        {"uav:sourceName":"source","href":"urn:source","type":"application/td+json","uav:selectAll":true}
                      ]
                    }
                    """))
            }).ConfigureAwait(false);
            Assert.That(registered.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            WotMaterializationCoordinator coordinator = provider.GetRequiredService<WotMaterializationCoordinator>();

            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest()).ConfigureAwait(false);

            Assert.That(viewHost.Applied, Has.Count.EqualTo(1));
            Assert.That(viewHost.Applied.Single().Plan.DocumentKind, Is.EqualTo(WotDocumentKind.ThingDescription));
            Assert.That(host.AddCount, Is.EqualTo(1));
            Assert.That(result.Results.Single(value => value.ResourceId == "view").LoadState,
                Is.EqualTo(WoTLoadStateEnum.Active));
        }

        [Test]
        public async Task VersionLeaseCapabilityUsesTheRegisteredRegistryOwnerAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddWotRegistryServer(options => options.Bounds.MaxVersionsPerResource = 2);
            ServiceProvider provider = services.BuildServiceProvider();
            await using (provider.ConfigureAwait(false))
            {
                IWotRegistryService registry = provider.GetRequiredService<IWotRegistryService>();
                IWotRegistryVersionLeaseProvider leases =
                    provider.GetRequiredService<IWotRegistryVersionLeaseProvider>();
                Assert.That(leases, Is.SameAs(registry));
                foreach (string version in new[] { "v1", "v2" })
                {
                    await registry.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = "di-lease",
                        VersionId = version,
                        Content = ByteString.From(TestMaterialization.Td("urn:di-lease", version)),
                        SetAsDefault = false
                    }).ConfigureAwait(false);
                }
                await registry.SetDefaultVersionAsync(
                    WotRegistryGroups.ThingDescriptions, "di-lease", "v2").ConfigureAwait(false);
                WotResource resource = registry.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, "di-lease")!;
                using IWotRegistryVersionLease lease = await leases.AcquireVersionLeaseAsync(
                    resource.GroupId, resource.ResourceId, resource.FindVersion("v1")!).ConfigureAwait(false);
                Assert.That(registry, Is.InstanceOf<IWotVersionedRegistryService>());
                var versioned = (IWotVersionedRegistryService)registry;
                await Assert.ThatAsync(async () =>
                {
                    _ = await versioned.TryCreateVersionAsync(
                        resource.GroupId, resource.ResourceId, "v3", resource.Kind).ConfigureAwait(false);
                }, Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
                lease.Dispose();
                Assert.That(await versioned.TryCreateVersionAsync(
                    resource.GroupId, resource.ResourceId, "v3", resource.Kind).ConfigureAwait(false), Is.Not.Null);
            }
        }

        [Test]
        public void OlderRegistryIsNotAdvertisedAsSupportingVersionLeases()
        {
            IWotRegistryService older = new Mock<IWotRegistryService>().Object;
            var services = new ServiceCollection();
            services.AddSingleton(older);
            services.AddOpcUa().AddWotRegistryServer();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IWotRegistryService>(), Is.SameAs(older));
            Assert.That(older, Is.Not.InstanceOf<IWotRegistryVersionLeaseProvider>());
            Assert.That(() => provider.GetRequiredService<IWotRegistryVersionLeaseProvider>(),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("The registered registry does not support Version leases."));
        }

        [Test]
        public void RegistryThenBindersExposesTheSameSingletonForBothInterfaces()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();
            builder.AddWotProtocolBinders();

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotBinderRegistry registry = sp.GetRequiredService<IWotBinderRegistry>();
            IWotBindingChannelFactory channelFactory = sp.GetRequiredService<IWotBindingChannelFactory>();
            WotProtocolBinderRegistry concrete = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry, Is.SameAs(concrete));
            Assert.That(channelFactory, Is.SameAs(concrete));
        }

        [Test]
        public void BindersThenRegistryExposesTheSameSingletonForBothInterfaces()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotProtocolBinders();
            builder.AddWotRegistryServer();

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotBinderRegistry registry = sp.GetRequiredService<IWotBinderRegistry>();
            IWotBindingChannelFactory channelFactory = sp.GetRequiredService<IWotBindingChannelFactory>();
            WotProtocolBinderRegistry concrete = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry, Is.SameAs(concrete));
            Assert.That(channelFactory, Is.SameAs(concrete));
        }

        [Test]
        public void RegistryOnlyStillExposesAWorkingRegistryAndChannelFactory()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWotRegistryServer();

            using ServiceProvider sp = services.BuildServiceProvider();

            IWotBinderRegistry registry = sp.GetRequiredService<IWotBinderRegistry>();
            IWotBindingChannelFactory channelFactory = sp.GetRequiredService<IWotBindingChannelFactory>();
            WotProtocolBinderRegistry concrete = sp.GetRequiredService<WotProtocolBinderRegistry>();

            Assert.That(registry, Is.SameAs(concrete));
            Assert.That(channelFactory, Is.SameAs(concrete));
            Assert.That(registry.Capabilities, Is.Empty,
                "With no binders registered, the shared registry advertises no capabilities.");
        }
    }
}
