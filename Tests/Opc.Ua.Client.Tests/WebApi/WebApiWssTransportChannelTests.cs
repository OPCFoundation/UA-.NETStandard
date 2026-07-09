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
using System.Net.Security;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.WebApi
{
    [TestFixture]
    [Category("Client")]
    [Category("WebApi")]
    public sealed class WebApiWssTransportChannelTests
    {
        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            Assert.That(
                () => new WebApiWssTransportChannel(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DefaultPropertiesAndPreOpenGuardsAreStable()
        {
            using var channel = new WebApiWssTransportChannel(NUnitTelemetryContext.Create());
            channel.OnTokenActivated += (_, _, _) => { };
            channel.OnTokenActivated -= (_, _, _) => { };
            channel.OperationTimeout = 4321;

            Assert.That(channel.UriScheme, Is.EqualTo(global::Opc.Ua.Utils.UriSchemeOpcWssOpenApi));
            Assert.That(channel.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.None));
            Assert.That(channel.CurrentToken, Is.Not.Null);
            Assert.That(channel.ChannelThumbprint, Is.Empty);
            Assert.That(channel.ClientChannelCertificate, Is.Empty);
            Assert.That(channel.ServerChannelCertificate, Is.Empty);
            Assert.That(channel.OperationTimeout, Is.EqualTo(4321));

            ServiceResultException endpoint = Assert.Throws<ServiceResultException>(
                () => _ = channel.EndpointDescription);
            ServiceResultException configuration = Assert.Throws<ServiceResultException>(
                () => _ = channel.EndpointConfiguration);
            ServiceResultException context = Assert.Throws<ServiceResultException>(
                () => _ = channel.MessageContext);
            Assert.That(endpoint.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(configuration.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(context.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task CloseDisposeReconnectAndSendGuardsAreDeterministicAsync()
        {
            using var channel = new WebApiWssTransportChannel(NUnitTelemetryContext.Create());
            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            ServiceResultException reconnect = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.ReconnectAsync().ConfigureAwait(false));
            Assert.That(reconnect.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(
                async () => await channel.SendRequestAsync(null!, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            ServiceResultException notConnected = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.That(notConnected.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));

            channel.Dispose();
            channel.Dispose();
            Assert.That(
                async () => await channel.SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void OpenRejectsNullConnectionAndSettings()
        {
            using var channel = new WebApiWssTransportChannel(NUnitTelemetryContext.Create());
            var connection = new Mock<ITransportWaitingConnection>();
            connection.SetupGet(c => c.EndpointUrl).Returns(new Uri("wss://localhost:4843/ua"));
            var settings = new TransportChannelSettings();

            Assert.That(
                async () => await channel.OpenAsync((Uri)null!, settings, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await channel.OpenAsync(new Uri("wss://localhost:4843/ua"), null!, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await channel.OpenAsync((ITransportWaitingConnection)null!, settings, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void NormalizeUrlAndSecurityChecksHandleWssSchemes()
        {
            MethodInfo normalize = typeof(WebApiWssTransportChannel).GetMethod(
                "NormalizeUrl",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo secure = typeof(WebApiWssTransportChannel).GetMethod(
                "IsSecureScheme",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var openApi = (Uri)normalize.Invoke(null, [new Uri("opc.wss+openapi://localhost:4843/ua")])!;
            var wss = (Uri)normalize.Invoke(null, [new Uri("opc.wss://localhost:4843/ua")])!;
            var unchanged = (Uri)normalize.Invoke(null, [new Uri("wss://localhost:4843/ua")])!;

            Assert.That(openApi.Scheme, Is.EqualTo("wss"));
            Assert.That(wss.Scheme, Is.EqualTo("wss"));
            Assert.That(unchanged, Is.EqualTo(new Uri("wss://localhost:4843/ua")));
            Assert.That((bool)secure.Invoke(null, [null])!, Is.False);
            Assert.That((bool)secure.Invoke(null, [new Uri("ws://localhost")])!, Is.False);
            Assert.That((bool)secure.Invoke(null, [new Uri("wss://localhost")])!, Is.True);
            Assert.That((bool)secure.Invoke(null, [new Uri("https://localhost")])!, Is.True);
        }

        [Test]
        public void ValidateServerCertificateFallsBackToTlsPolicyErrors()
        {
            using var channel = new WebApiWssTransportChannel(NUnitTelemetryContext.Create());
            MethodInfo method = typeof(WebApiWssTransportChannel).GetMethod(
                "ValidateServerCertificate",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var accepted = (bool)method.Invoke(channel, [this, null, null, SslPolicyErrors.None])!;
            var rejected = (bool)method.Invoke(
                channel,
                [this, null, null, SslPolicyErrors.RemoteCertificateNameMismatch])!;

            Assert.That(accepted, Is.True);
            Assert.That(rejected, Is.False);
        }
    }
}
