/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("TransportChannelTests")]
    [Parallelizable]
    public class OpcUaHttpClientFactoryTests
    {
        [Test]
        public void StandaloneFactoryCachesClientsByName()
        {
            var factory = new DefaultOpcUaHttpClientFactory();
            string name = "https://localhost/" + Guid.NewGuid().ToString("N");

            HttpClient first = factory.CreateClient(name);
            HttpClient second = factory.CreateClient(name);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void StandaloneFactoryUsesSeparateClientsForDifferentNames()
        {
            var factory = new DefaultOpcUaHttpClientFactory();
            string firstName = "https://localhost/" + Guid.NewGuid().ToString("N");
            string secondName = "https://localhost/" + Guid.NewGuid().ToString("N");

            HttpClient first = factory.CreateClient(firstName);
            HttpClient second = factory.CreateClient(secondName);

            Assert.That(second, Is.Not.SameAs(first));
        }

        [Test]
        public void FactoryRejectsEmptyName()
        {
            var factory = new DefaultOpcUaHttpClientFactory();

            Assert.That(
                () => factory.CreateClient(string.Empty),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void FactoryDelegatesToInnerHttpClientFactory()
        {
            var innerFactory = new RecordingHttpClientFactory();
            var factory = new DefaultOpcUaHttpClientFactory(innerFactory);

            using HttpClient client = factory.CreateClient(OpcUaHttpClientDefaults.ClientName);

            Assert.That(client, Is.Not.Null);
            Assert.That(innerFactory.LastName, Is.EqualTo(OpcUaHttpClientDefaults.ClientName));
            Assert.That(innerFactory.CreateCount, Is.EqualTo(1));
        }

        private sealed class RecordingHttpClientFactory : IHttpClientFactory
        {
            public string? LastName { get; private set; }

            public int CreateCount { get; private set; }

            public HttpClient CreateClient(string name)
            {
                LastName = name;
                CreateCount++;
#pragma warning disable CA2000 // HttpClient owns the handler; TODO: remove when analyzer tracks disposeHandler.
                StubHandler handler = new();
#pragma warning restore CA2000
                try
                {
                    return new HttpClient(handler, disposeHandler: true);
                }
                catch
                {
                    handler.Dispose();
                    throw;
                }
            }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }
    }
}
