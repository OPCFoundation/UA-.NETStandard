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
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for <see cref="WssJsonTransportChannel"/> and
    /// <see cref="WssJsonTransportChannelFactory"/>. Covers constructor,
    /// lifecycle (Open / Close / Reconnect / Dispose), property accessors,
    /// and the factory entry-point without exercising the live network.
    /// </summary>
    [TestFixture]
    [Category("WssJsonTransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WssJsonTransportChannelTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// Constructor must succeed and produce a non-null channel.
        /// </summary>
        [Test]
        public void ConstructorCreatesValidInstance()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            Assert.That(channel, Is.Not.Null);
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannel.UriScheme"/> must equal
        /// the internal pseudo-scheme constant used by <c>ClientChannelManager</c>.
        /// </summary>
        [Test]
        public void UriSchemeMatchesPseudoSchemeConstant()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            Assert.That(channel.UriScheme, Is.EqualTo(WssJsonTransportChannel.PseudoScheme));
        }

        /// <summary>
        /// Before <c>Open</c> is called the channel must report no supported
        /// features (no persistent connection to reconnect over).
        /// </summary>
        [Test]
        public void SupportedFeaturesIsNoneBeforeOpen()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            Assert.That(channel.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.None));
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannel.CurrentToken"/> must return a
        /// value (non-null struct) even before Open.
        /// </summary>
        [Test]
        public void CurrentTokenIsNonNullBeforeOpen()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            ChannelToken token = channel.CurrentToken;
            Assert.That(token, Is.Not.Null);
        }

        /// <summary>
        /// Certificate thumbprints must be empty arrays (not null) before Open.
        /// </summary>
        [Test]
        public void ThumbprintPropertiesReturnEmptyArraysBeforeOpen()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            Assert.That(channel.ChannelThumbprint, Is.Not.Null.And.Empty);
            Assert.That(channel.ClientChannelCertificate, Is.Not.Null.And.Empty);
            Assert.That(channel.ServerChannelCertificate, Is.Not.Null.And.Empty);
        }

        /// <summary>
        /// Accessing <see cref="WssJsonTransportChannel.EndpointDescription"/>
        /// before Open must throw a <see cref="ServiceResultException"/> with
        /// <see cref="StatusCodes.BadNotConnected"/>.
        /// </summary>
        [Test]
        public void EndpointDescriptionBeforeOpenThrowsBadNotConnected()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = channel.EndpointDescription)!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// Accessing <see cref="WssJsonTransportChannel.EndpointConfiguration"/>
        /// before Open must throw a <see cref="ServiceResultException"/> with
        /// <see cref="StatusCodes.BadNotConnected"/>.
        /// </summary>
        [Test]
        public void EndpointConfigurationBeforeOpenThrowsBadNotConnected()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = channel.EndpointConfiguration)!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// Accessing <see cref="WssJsonTransportChannel.MessageContext"/>
        /// before Open must throw a <see cref="ServiceResultException"/> with
        /// <see cref="StatusCodes.BadNotConnected"/>.
        /// </summary>
        [Test]
        public void MessageContextBeforeOpenThrowsBadNotConnected()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = channel.MessageContext)!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannel.SendRequestAsync"/> before Open
        /// must throw <see cref="StatusCodes.BadNotConnected"/>.
        /// </summary>
        [Test]
        public void SendRequestAsyncBeforeOpenThrowsBadNotConnected()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SendRequestAsync(
                    new ReadRequest(),
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannel.CloseAsync"/> on a never-opened
        /// channel must complete without throwing.
        /// </summary>
        [Test]
        public async Task CloseAsyncOnNeverOpenedChannelDoesNotThrowAsync()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannel.ReconnectAsync"/> must complete
        /// without throwing (the channel opens fresh connections per-request
        /// so there is nothing persistent to reconnect).
        /// </summary>
        [Test]
        public async Task ReconnectAsyncIsNoopAsync()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            await channel.ReconnectAsync(
                connection: null,
                ct: CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose must not throw even when called before Open.
        /// </summary>
        [Test]
        public void DisposeBeforeOpenDoesNotThrow()
        {
            var channel = new WssJsonTransportChannel(m_telemetry);
            Assert.DoesNotThrow(channel.Dispose);
        }

        /// <summary>
        /// Calling Dispose twice must not throw (idempotent contract).
        /// </summary>
        [Test]
        public void DoubleDisposeDoesNotThrow()
        {
            var channel = new WssJsonTransportChannel(m_telemetry);
            channel.Dispose();
            Assert.DoesNotThrow(channel.Dispose);
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannel.OperationTimeout"/> property
        /// must be readable and writable before Open.
        /// </summary>
        [Test]
        public void OperationTimeoutIsReadWriteBeforeOpen()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            channel.OperationTimeout = 5000;
            Assert.That(channel.OperationTimeout, Is.EqualTo(5000));
        }

        /// <summary>
        /// The OnTokenActivated event add/remove accessors must not throw.
        /// </summary>
        [Test]
        public void OnTokenActivatedEventIsAddRemoveSafe()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            ChannelTokenActivatedEventHandler handler = (_, _, _) => { };
            Assert.DoesNotThrow(() => channel.OnTokenActivated += handler);
            Assert.DoesNotThrow(() => channel.OnTokenActivated -= handler);
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannel.OpenAsync(Uri, TransportChannelSettings, CancellationToken)"/>
        /// followed by a property access must return the configured endpoint
        /// description and configuration.
        /// </summary>
        [Test]
        public async Task OpenAsyncStoresSettingsAndExposesPropertiesAsync()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            var url = new Uri("opc.wss://localhost:4840/UA");
            var desc = new EndpointDescription { EndpointUrl = url.ToString() };
            var config = EndpointConfiguration.Create();
            var settings = new TransportChannelSettings
            {
                Description = desc,
                Configuration = config,
                Factory = ServiceMessageContext.CreateEmpty(m_telemetry).Factory,
                NamespaceUris = new NamespaceTable()
            };

            await channel.OpenAsync(url, settings, CancellationToken.None).ConfigureAwait(false);

            Assert.That(channel.EndpointDescription, Is.SameAs(desc));
            Assert.That(channel.EndpointConfiguration, Is.SameAs(config));
            Assert.That(channel.MessageContext, Is.Not.Null);
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannel.OpenAsync(ITransportWaitingConnection, TransportChannelSettings, CancellationToken)"/>
        /// stores settings exactly like the Uri overload.
        /// </summary>
        [Test]
        public async Task OpenAsyncConnectionOverloadStoresSettingsAsync()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            var url = new Uri("opc.wss://localhost:4840/UA");
            var desc = new EndpointDescription { EndpointUrl = url.ToString() };
            var config = EndpointConfiguration.Create();
            var settings = new TransportChannelSettings
            {
                Description = desc,
                Configuration = config,
                Factory = ServiceMessageContext.CreateEmpty(m_telemetry).Factory,
                NamespaceUris = new NamespaceTable()
            };

            var connection = new FakeWaitingConnection(url);
            await channel.OpenAsync(connection, settings, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(channel.EndpointDescription, Is.SameAs(desc));
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannelFactory.UriScheme"/> must equal
        /// the WssJsonTransportChannel pseudo-scheme.
        /// </summary>
        [Test]
        public void FactoryUriSchemeMatchesPseudoScheme()
        {
            var factory = new WssJsonTransportChannelFactory();
            Assert.That(factory.UriScheme, Is.EqualTo(WssJsonTransportChannel.PseudoScheme));
        }

        /// <summary>
        /// <see cref="WssJsonTransportChannelFactory.Create"/> must return
        /// a <see cref="WssJsonTransportChannel"/> instance.
        /// </summary>
        [Test]
        public void FactoryCreateReturnsWssJsonTransportChannel()
        {
            var factory = new WssJsonTransportChannelFactory();
            using ITransportChannel channel = factory.Create(m_telemetry);
            Assert.That(channel, Is.InstanceOf<WssJsonTransportChannel>());
        }

        /// <summary>
        /// <c>OperationTimeout</c> is picked up from the EndpointConfiguration
        /// during <see cref="WssJsonTransportChannel.OpenAsync"/>.
        /// </summary>
        [Test]
        public async Task OpenAsyncSetsOperationTimeoutFromConfigurationAsync()
        {
            using var channel = new WssJsonTransportChannel(m_telemetry);
            var url = new Uri("opc.wss://localhost:4840/UA");
            var config = EndpointConfiguration.Create();
            config.OperationTimeout = 12345;
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription { EndpointUrl = url.ToString() },
                Configuration = config,
                Factory = ServiceMessageContext.CreateEmpty(m_telemetry).Factory,
                NamespaceUris = new NamespaceTable()
            };

            await channel.OpenAsync(url, settings, CancellationToken.None).ConfigureAwait(false);

            Assert.That(channel.OperationTimeout, Is.EqualTo(12345));
        }

        /// <summary>
        /// Minimal stub for <see cref="ITransportWaitingConnection"/> used by
        /// the connection-overload Open test.
        /// </summary>
        private sealed class FakeWaitingConnection : ITransportWaitingConnection
        {

            internal FakeWaitingConnection(Uri url)
            {
                EndpointUrl = url;
            }

            public string ServerUri => string.Empty;

            public Uri EndpointUrl { get; }

#pragma warning disable CS8603 // Possible null reference return.
            public object Handle => null!;
#pragma warning restore CS8603
        }
    }
}
