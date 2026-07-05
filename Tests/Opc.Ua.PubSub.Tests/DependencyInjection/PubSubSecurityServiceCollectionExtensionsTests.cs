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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Tests.Security.Sks;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Unit tests for
    /// <see cref="PubSubSecurityServiceCollectionExtensions"/>.
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class PubSubSecurityServiceCollectionExtensionsTests
    {
        private const string GroupId = "group-1";
        private static string PolicyUri => PubSubSecurityPolicyUri.PubSubAes128Ctr;

        private static FakeSecurityKeyService CreateFake()
        {
            return new FakeSecurityKeyService(
                PubSubSecurityPolicyRegistry.GetByUri(PolicyUri)!,
                TimeSpan.FromHours(1));
        }

        [Test]
        public async Task AddPubSubSecurityKeyServiceClientRegistersProviderAndStarterAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSecurityKeyServiceClient(GroupId, PolicyUri, _ => CreateFake());
            await using ServiceProvider sp = services.BuildServiceProvider();

            IPubSubSecurityKeyProvider provider = sp.GetServices<IPubSubSecurityKeyProvider>().Single();
            Assert.That(provider, Is.TypeOf<PullSecurityKeyProvider>());
            Assert.That(provider.SecurityGroupId, Is.EqualTo(GroupId));
            Assert.That(
                sp.GetServices<IHostedService>().OfType<PubSubSecurityKeyProviderStarter>().Count(),
                Is.EqualTo(1));
        }

        [Test]
        public async Task AddSecurityKeyServiceClientOnPubSubBuilderRegistersProviderAndStarterAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddLogging();
            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddSecurityKeyServiceClient(
                GroupId,
                PolicyUri,
                _ => CreateFake()));
            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(sp.GetServices<IPubSubSecurityKeyProvider>().Single(), Is.TypeOf<PullSecurityKeyProvider>());
                Assert.That(
                    sp.GetServices<IHostedService>().OfType<PubSubSecurityKeyProviderStarter>().Count(),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public async Task AddSecurityKeyServiceServerOnPubSubBuilderRegistersSingletonAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());

            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddSecurityKeyServiceServer());
            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<InMemoryPubSubKeyServiceServer>(), Is.Not.Null);
        }

        [Test]
        public async Task AddSecurityKeyPushTargetOnPubSubBuilderRegistersProviderAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());

            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddSecurityKeyPushTarget(GroupId));
            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                sp.GetServices<IPubSubSecurityKeyProvider>().Single(),
                Is.TypeOf<PushSecurityKeyProvider>());
        }

        [Test]
        public async Task AddPubSubSecurityKeyServiceClientStartsProviderThatServesKeysAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSecurityKeyServiceClient(
                GroupId,
                PolicyUri,
                _ => CreateFake(),
                options => options.RequestedFutureKeyCount = 1);
            await using ServiceProvider sp = services.BuildServiceProvider();

            PubSubSecurityKeyProviderStarter starter = sp
                .GetServices<IHostedService>()
                .OfType<PubSubSecurityKeyProviderStarter>()
                .Single();
            await starter.StartAsync(CancellationToken.None);

            var provider = (PullSecurityKeyProvider)sp.GetServices<IPubSubSecurityKeyProvider>().Single();
            PubSubSecurityKey key = await provider.GetCurrentKeyAsync();
            Assert.That(key.TokenId, Is.EqualTo(1U));
        }

        [Test]
        public async Task AddPubSubSecurityKeyServiceClientRegistersStarterOnlyOnceAsync()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSecurityKeyServiceClient("group-1", PolicyUri, _ => CreateFake());
            builder.AddPubSubSecurityKeyServiceClient("group-2", PolicyUri, _ => CreateFake());
            await using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetServices<IPubSubSecurityKeyProvider>().Count(), Is.EqualTo(2));
            Assert.That(
                sp.GetServices<IHostedService>().OfType<PubSubSecurityKeyProviderStarter>().Count(),
                Is.EqualTo(1));
        }

        [Test]
        public void AddPubSubSecurityKeyServiceClientUnknownPolicyThrows()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddPubSubSecurityKeyServiceClient(GroupId, "urn:not-a-policy", _ => CreateFake()),
                Throws.ArgumentException);
        }

        [Test]
        public void AddPubSubSecurityKeyServiceClientNullBuilderThrows()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddPubSubSecurityKeyServiceClient(GroupId, PolicyUri, _ => CreateFake()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubSecurityKeyServiceServerRegistersSingleton()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSecurityKeyServiceServer();
            using ServiceProvider sp = services.BuildServiceProvider();
            InMemoryPubSubKeyServiceServer? server =
                sp.GetService<InMemoryPubSubKeyServiceServer>();
            Assert.That(server, Is.Not.Null);
        }

        [Test]
        public void AddPubSubSecurityKeyServiceServerNullBuilderThrows()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddPubSubSecurityKeyServiceServer(),
                Throws.ArgumentNullException);
        }
    }
}
