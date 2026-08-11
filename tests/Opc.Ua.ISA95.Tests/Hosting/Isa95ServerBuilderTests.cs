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

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.ISA95.Server;
using Opc.Ua.ISA95.Server.Hosting;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.ISA95.Tests.Hosting
{
    [TestFixture]
    public sealed class Isa95ServerBuilderTests
    {
        [Test]
        public async Task AddIsa95ServerRegistersFactoryAndSharedProvidersAsync()
        {
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddServer(_ => { })
                .AddIsa95Server(options =>
                    options.InstanceNamespaceUri = "urn:test:isa95");

            await using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(
                provider.GetRequiredService<Isa95NodeManagerFactory>(),
                Is.Not.Null);
            Assert.That(
                provider.GetRequiredService<IIsa95JobOrderReceiverV1>(),
                Is.SameAs(provider.GetRequiredService<IIsa95JobOrderReceiverV2>()));
            Assert.That(
                provider.GetRequiredService<IIsa95JobExecutionController>(),
                Is.SameAs(provider.GetRequiredService<IIsa95JobOrderCatalog>()));
            Assert.That(
                provider.GetRequiredService<IIsa95JobOrderCatalogChangeSource>(),
                Is.SameAs(provider.GetRequiredService<IIsa95JobOrderCatalog>()));
        }

        [Test]
        public void AddIsa95ServerRejectsDuplicateRegistration()
        {
            var services = new ServiceCollection();
            Ua.Server.Hosting.IOpcUaServerBuilder builder = services
                .AddOpcUa()
                .AddServer(_ => { });
            builder.AddIsa95Server();

            Assert.That(
                () => builder.AddIsa95Server(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task ConfigureModelRegistersConfiguratorAsync()
        {
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddServer(_ => { })
                .AddIsa95Server()
                .ConfigureModel((_, _) => default);

            await using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(
                provider.GetServices<IIsa95ModelConfigurator>(),
                Has.Exactly(1).Items);
        }

        [Test]
        public async Task CustomProviderRegistrationDoesNotAddDefaultFacetsAsync()
        {
            var services = new ServiceCollection();
            IIsa95JobOrderReceiverV1 customProvider =
                new Mock<IIsa95JobOrderReceiverV1>().Object;
            services.AddSingleton(customProvider);
            services
                .AddOpcUa()
                .AddServer(_ => { })
                .AddIsa95Server();

            await using ServiceProvider provider = services.BuildServiceProvider();
            Isa95ServerProviders providers =
                provider.GetRequiredService<Isa95ServerProviders>();

            Assert.That(providers.JobOrderReceiverV1, Is.SameAs(customProvider));
            Assert.That(providers.JobOrderReceiverV2, Is.Null);
            Assert.That(
                provider.GetService<InMemoryIsa95JobControlProvider>(),
                Is.Null);
        }

        [Test]
        public async Task CustomProviderAddedAfterDefaultRegistrationIsRejectedAsync()
        {
            var services = new ServiceCollection();
            services
                .AddOpcUa()
                .AddServer(_ => { })
                .AddIsa95Server();
            services.AddSingleton(
                new Mock<IIsa95JobOrderReceiverV1>().Object);

            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => provider.GetRequiredService<Isa95ServerProviders>(),
                Throws.InvalidOperationException);
        }
    }
}
