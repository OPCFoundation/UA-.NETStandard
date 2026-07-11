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
    public sealed class WebApiTransportChannelTests
    {
        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            Assert.That(
                () => new WebApiTransportChannel(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DefaultPropertiesAreStableBeforeOpen()
        {
            using var channel = new WebApiTransportChannel(NUnitTelemetryContext.Create());
            channel.OnTokenActivated += (_, _, _) => { };
            channel.OnTokenActivated -= (_, _, _) => { };
            channel.OperationTimeout = 1234;

            Assert.That(channel.UriScheme, Is.EqualTo(global::Opc.Ua.Utils.UriSchemeOpcHttpsWebApi));
            Assert.That(channel.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.None));
            Assert.That(channel.CurrentToken, Is.Not.Null);
            Assert.That(channel.ChannelThumbprint, Is.Empty);
            Assert.That(channel.ClientChannelCertificate, Is.Empty);
            Assert.That(channel.ServerChannelCertificate, Is.Empty);
            Assert.That(channel.OperationTimeout, Is.EqualTo(1234));
        }

        [Test]
        public void ConnectedPropertiesThrowBadNotConnectedBeforeOpen()
        {
            using var channel = new WebApiTransportChannel(NUnitTelemetryContext.Create());

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
        public void OpenRejectsNullArguments()
        {
            using var channel = new WebApiTransportChannel(NUnitTelemetryContext.Create());
            var settings = new TransportChannelSettings();
            var connection = new Mock<ITransportWaitingConnection>();
            connection.SetupGet(c => c.EndpointUrl).Returns(new Uri("https://localhost:4843/"));

            Assert.That(
                async () => await channel.OpenAsync((Uri)null!, settings, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await channel.OpenAsync(new Uri("https://localhost:4843/"), null!, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await channel.OpenAsync((ITransportWaitingConnection)null!, settings, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task CloseReconnectAndDisposeAreSafeBeforeOpenAsync()
        {
            using var channel = new WebApiTransportChannel(NUnitTelemetryContext.Create());
            var connection = new Mock<ITransportWaitingConnection>();
            connection.SetupGet(c => c.EndpointUrl).Returns(new Uri("opc.https://localhost:4843/ua"));

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await channel.ReconnectAsync(connection.Object, CancellationToken.None).ConfigureAwait(false);
            channel.Dispose();
            channel.Dispose();

            Assert.That(
                async () => await channel.SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void SendRequestRequiresOpenChannel()
        {
            using var channel = new WebApiTransportChannel(NUnitTelemetryContext.Create());

            Assert.That(
                async () => await channel.SendRequestAsync(null!, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public void SelectMatchingEndpointPrefersWebApiProfileAndFallsBackToSecurityModeNone()
        {
            MethodInfo method = typeof(WebApiTransportChannel).GetMethod(
                "SelectMatchingEndpoint",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var expected = new EndpointDescription { SecurityMode = MessageSecurityMode.None };
            var secure = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
            var none = new EndpointDescription { SecurityMode = MessageSecurityMode.None };
            var webApi = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                TransportProfileUri = Profiles.HttpsOpenApiTransport
            };

            ArrayOf<EndpointDescription> webApiEndpoints = [secure, none, webApi];
            ArrayOf<EndpointDescription> fallbackEndpoints = [secure, none];

            object? selectedWebApi = method.Invoke(null, [webApiEndpoints, expected]);
            object? selectedFallback = method.Invoke(null, [fallbackEndpoints, expected]);

            Assert.That(selectedWebApi, Is.SameAs(webApi));
            Assert.That(selectedFallback, Is.SameAs(none));
        }

        [Test]
        public void NormalizeUrlMapsOpcUaSchemesToHttps()
        {
            MethodInfo method = typeof(WebApiTransportChannel).GetMethod(
                "NormalizeUrl",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var webApi = (Uri)method.Invoke(null, [new Uri("opc.https+webapi://localhost:4843/ua")])!;
            var https = (Uri)method.Invoke(null, [new Uri("opc.https://localhost:4843/ua")])!;
            var unchanged = (Uri)method.Invoke(null, [new Uri("https://localhost:4843/ua")])!;

            Assert.That(webApi.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
            Assert.That(https.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
            Assert.That(unchanged, Is.EqualTo(new Uri("https://localhost:4843/ua")));
        }
    }
}
