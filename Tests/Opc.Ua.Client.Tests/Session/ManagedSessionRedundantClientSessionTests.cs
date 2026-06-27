/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Tests for redundant managed client session adapters.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ServerRedundancy")]
    public sealed class ManagedSessionRedundantClientSessionTests
    {
        [Test]
        public async Task FactoryCreatesAdapterWithoutConnectingItAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server");
            var factory = new DefaultRedundantManagedClientSessionFactory(
                (_, _) => throw new InvalidOperationException("Should not connect during factory create."));

            IRedundantManagedClientSession session = await factory
                .CreateAsync(endpoint)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.Endpoint, Is.SameAs(endpoint));
                Assert.That(session.IsConnected, Is.False);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisconnectedAdapterHandlesReadCloseAndDisposeSafelyAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server");
            var session = new ManagedSessionRedundantClientSession(
                endpoint,
                (_, _) => throw new InvalidOperationException("Connect is not expected."));

            byte serviceLevel = await session.ReadServiceLevelAsync().ConfigureAwait(false);
            await session.CloseAsync().ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);

            Assert.That(serviceLevel, Is.Zero);
            Assert.That(session.IsConnected, Is.False);
        }

        [Test]
        public async Task DisconnectedAdapterRejectsOperationsThatNeedConnectionAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server");
            var session = new ManagedSessionRedundantClientSession(
                endpoint,
                (_, _) => throw new InvalidOperationException("Connect is not expected."));
            try
            {
                var handler = new Mock<IServerRedundancyHandler>();
                using var subscription = new Subscription((ITelemetryContext)null!);

                Assert.That(
                    async () => await session.FetchRedundancyInfoAsync(handler.Object).ConfigureAwait(false),
                    Throws.InvalidOperationException);
                Assert.That(
                    async () => await session.AddSubscriptionAsync(
                        "sub",
                        subscription,
                        MonitoringMode.Reporting,
                        publishingEnabled: true).ConfigureAwait(false),
                    Throws.InvalidOperationException);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AdapterValidatesArgumentsAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server");

            Assert.That(
                () => new ManagedSessionRedundantClientSession(null!, (_, _) => default),
                Throws.ArgumentNullException);
            Assert.That(
                () => new ManagedSessionRedundantClientSession(endpoint, null!),
                Throws.ArgumentNullException);

            var session = new ManagedSessionRedundantClientSession(
                endpoint,
                (_, _) => throw new InvalidOperationException("Connect is not expected."));
            try
            {
                using var subscription = new Subscription((ITelemetryContext)null!);

                Assert.That(
                    async () => await session.FetchRedundancyInfoAsync(null!).ConfigureAwait(false),
                    Throws.ArgumentNullException);
                Assert.That(
                    async () => await session.AddSubscriptionAsync(
                        string.Empty,
                        subscription,
                        MonitoringMode.Reporting,
                        publishingEnabled: true).ConfigureAwait(false),
                    Throws.ArgumentException);
                Assert.That(
                    async () => await session.AddSubscriptionAsync(
                        "sub",
                        null!,
                        MonitoringMode.Reporting,
                        publishingEnabled: true).ConfigureAwait(false),
                    Throws.ArgumentNullException);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ActivateMirroredSessionRejectsDisconnectedSourceAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server");
            var session = new ManagedSessionRedundantClientSession(
                endpoint,
                (_, _) => throw new InvalidOperationException("Connect is not expected."));
            try
            {
                var source = new Mock<IRedundantManagedClientSession>();

                Assert.That(
                    async () => await session
                        .ActivateMirroredSessionAsync(source.Object)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ConfiguredEndpoint CreateEndpoint(string serverUri)
        {
            var description = new EndpointDescription
            {
                EndpointUrl = $"opc.tcp://{serverUri[4..]}:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri
                }
            };

            return new ConfiguredEndpoint(null, description, configuration: null);
        }
    }
}
