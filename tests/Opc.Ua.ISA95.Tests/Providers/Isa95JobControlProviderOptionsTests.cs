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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.ISA95.Server;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.ISA95.Tests.Providers
{
    [TestFixture]
    public class Isa95JobControlProviderOptionsTests
    {
        [Test]
        public void ValidateAcceptsDefaults()
        {
            var options = new Isa95JobControlProviderOptions();

            Assert.That(options.Validate, Throws.Nothing);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorRejectsInvalidMaxJobOrders(int value)
        {
            Assert.That(
                () => _ = new InMemoryIsa95JobControlProvider(
                    new Isa95JobControlProviderOptions { MaxJobOrders = value }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorRejectsInvalidMaxJobResponses(int value)
        {
            Assert.That(
                () => _ = new InMemoryIsa95JobControlProvider(
                    new Isa95JobControlProviderOptions { MaxJobResponses = value }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ConstructorRejectsNegativeRetention()
        {
            Assert.That(
                () => _ = new InMemoryIsa95JobControlProvider(
                    new Isa95JobControlProviderOptions
                    {
                        ResponseRetention = TimeSpan.FromSeconds(-1)
                    }),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DependencyInjectionRegistersEveryFacetAsTheSameSingleton()
        {
            var services = new ServiceCollection();
            services.AddInMemoryIsa95JobControlProvider();

            using ServiceProvider provider = services.BuildServiceProvider();
            InMemoryIsa95JobControlProvider engine = provider.GetRequiredService<InMemoryIsa95JobControlProvider>();

            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IIsa95JobOrderReceiverV1>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobResponseProviderV1>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobResponseReceiverV1>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobOrderReceiverV2>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobResponseProviderV2>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobResponseReceiverV2>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobStatusSourceV2>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobExecutionController>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobOrderCatalog>(), Is.SameAs(engine));
                Assert.That(provider.GetRequiredService<IIsa95JobOrderCatalogChangeSource>(), Is.SameAs(engine));
            });
        }

        [Test]
        public void DependencyInjectionAppliesConfiguredOptions()
        {
            var services = new ServiceCollection();
            services.AddInMemoryIsa95JobControlProvider(options => options.MaxJobOrders = 1);

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                () => provider.GetRequiredService<InMemoryIsa95JobControlProvider>(),
                Throws.Nothing);
        }
    }
}
