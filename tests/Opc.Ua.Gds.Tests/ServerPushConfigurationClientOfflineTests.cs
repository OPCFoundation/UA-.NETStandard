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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Offline unit tests for <see cref="ServerPushConfigurationClient"/> covering
    /// the surface reachable without a live ServerConfiguration session:
    /// construction, argument validation, property defaults and round-trips,
    /// endpoint plumbing and the not-connected fast-fail paths.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("GdsClientOffline")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ServerPushConfigurationClientOfflineTests
    {
        private const string OpcUaNamespaceUri = "http://opcfoundation.org/UA/";
        private const string TestEndpointUrl = "opc.tcp://localhost:4840";

        private static ServerPushConfigurationClient CreateClient()
        {
            return new ServerPushConfigurationClient(new ApplicationConfiguration());
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            var description = new EndpointDescription { EndpointUrl = TestEndpointUrl };
            return new ConfiguredEndpoint(null, description, EndpointConfiguration.Create());
        }

        private static async Task AssertThrowsEndpointNull(Func<Task> code)
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(code);
            Assert.That(exception.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public void ConstructorWithNullConfigurationThrows()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new ServerPushConfigurationClient(null!));
            Assert.That(exception.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void ConstructorWithOptionsRetainsConfiguration()
        {
            var configuration = new ApplicationConfiguration();
            ISessionFactory sessionFactory = new Mock<ISessionFactory>().Object;
            using var client = new ServerPushConfigurationClient(
                configuration,
                new GdsClientOptions(),
                sessionFactory,
                DiagnosticsMasks.None,
                TimeProvider.System);
            Assert.That(client.Configuration, Is.SameAs(configuration));
        }

        [Test]
        public void ConfigurationReturnsSuppliedInstance()
        {
            var configuration = new ApplicationConfiguration();
            using var client = new ServerPushConfigurationClient(configuration);
            Assert.That(client.Configuration, Is.SameAs(configuration));
        }

        [Test]
        public void MessageContextExposesOpcUaBaseNamespace()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.MessageContext.NamespaceUris.GetString(0), Is.EqualTo(OpcUaNamespaceUri));
        }

        [Test]
        public void AdminCredentialsDefaultsToNull()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.AdminCredentials, Is.Null);
        }

        [Test]
        public void AdminCredentialsRoundTrips()
        {
            var identity = new UserIdentity();
            using ServerPushConfigurationClient client = CreateClient();
            client.AdminCredentials = identity;
            Assert.That(client.AdminCredentials, Is.SameAs(identity));
        }

        [Test]
        public void ResetCredentialsClearsAdminCredentials()
        {
            using ServerPushConfigurationClient client = CreateClient();
            client.AdminCredentials = new UserIdentity();
            client.ResetCredentials();
            Assert.That(client.AdminCredentials, Is.Null);
        }

        [Test]
        public void PreferredLocalesDefaultsToNull()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.PreferredLocales.IsNull, Is.True);
        }

        [Test]
        public void PreferredLocalesRoundTrips()
        {
            using ServerPushConfigurationClient client = CreateClient();
            client.PreferredLocales = new[] { "en-US", "de-DE" };
            Assert.Multiple(() =>
            {
                Assert.That(client.PreferredLocales.Count, Is.EqualTo(2));
                Assert.That(client.PreferredLocales[0], Is.EqualTo("en-US"));
                Assert.That(client.PreferredLocales[1], Is.EqualTo("de-DE"));
            });
        }

        [Test]
        public void IsConnectedDefaultsToFalse()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public void SessionDefaultsToNull()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.Session, Is.Null);
        }

        [Test]
        public void ServerConfigurationDefaultsToNull()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.ServerConfiguration, Is.Null);
        }

        [Test]
        public void DefaultApplicationGroupIsNullBeforeConnect()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.DefaultApplicationGroup.IsNull, Is.True);
        }

        [Test]
        public void DefaultHttpsGroupIsNullBeforeConnect()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.DefaultHttpsGroup.IsNull, Is.True);
        }

        [Test]
        public void DefaultUserTokenGroupIsNullBeforeConnect()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.DefaultUserTokenGroup.IsNull, Is.True);
        }

        [Test]
        public void EndpointIsNullWhenNotConfigured()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.Endpoint, Is.Null);
        }

        [Test]
        public void EndpointUrlIsNullWhenNotConfigured()
        {
            using ServerPushConfigurationClient client = CreateClient();
            Assert.That(client.EndpointUrl, Is.Null);
        }

        [Test]
        public void EndpointRoundTripsWhenNotConnected()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            using var client = new ServerPushConfigurationClient(new ApplicationConfiguration())
            {
                Endpoint = endpoint
            };
            Assert.That(client.Endpoint, Is.SameAs(endpoint));
        }

        [Test]
        public void EndpointUrlReflectsConfiguredEndpoint()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            using var client = new ServerPushConfigurationClient(new ApplicationConfiguration())
            {
                Endpoint = endpoint
            };
            Assert.That(client.EndpointUrl, Is.EqualTo(endpoint.EndpointUrl!.ToString()));
        }

        [Test]
        public async Task CreateAsyncWithNullSessionFactoryThrows()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(
                    (ISessionFactory)null!, new ApplicationConfiguration(), CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("sessionFactory"));
        }

        [Test]
        public async Task CreateAsyncWithNullConfigurationThrows()
        {
            ISessionFactory sessionFactory = new Mock<ISessionFactory>().Object;
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(sessionFactory, null!, CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public async Task CreateAsyncWithNullEndpointThrows()
        {
            ISessionFactory sessionFactory = new Mock<ISessionFactory>().Object;
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(
                    sessionFactory, new ApplicationConfiguration(), null!));
            Assert.That(exception.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public async Task CreateAsyncWithNullChannelManagerThrows()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(
                    (IClientChannelManager)null!, new ApplicationConfiguration(), CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("manager"));
        }

        [Test]
        public async Task CreateAsyncWithChannelManagerNullConfigurationThrows()
        {
            IClientChannelManager manager = new Mock<IClientChannelManager>().Object;
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(manager, null!, CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public async Task CreateAsyncWithChannelManagerNullEndpointThrows()
        {
            IClientChannelManager manager = new Mock<IClientChannelManager>().Object;
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(
                    manager, new ApplicationConfiguration(), null!));
            Assert.That(exception.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public async Task ConnectAsyncWithNullEndpointUrlThrows()
        {
            using ServerPushConfigurationClient client = CreateClient();
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => client.ConnectAsync((string)null!).AsTask());
            Assert.That(exception.ParamName, Is.EqualTo("endpointUrl"));
        }

        [Test]
        public async Task ConnectAsyncWithEmptyEndpointUrlThrows()
        {
            using ServerPushConfigurationClient client = CreateClient();
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => client.ConnectAsync(string.Empty).AsTask());
            Assert.That(exception.ParamName, Is.EqualTo("endpointUrl"));
        }

        [Test]
        public async Task ConnectAsyncWithMalformedEndpointUrlThrows()
        {
            using ServerPushConfigurationClient client = CreateClient();
            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
                () => client.ConnectAsync("not a valid url").AsTask());
            Assert.Multiple(() =>
            {
                Assert.That(exception.ParamName, Is.EqualTo("endpointUrl"));
                Assert.That(exception.Message, Does.Contain("is not a valid URL."));
            });
        }

        [Test]
        public async Task ConnectAsyncWithoutEndpointThrows()
        {
            using ServerPushConfigurationClient client = CreateClient();
            ConfiguredEndpoint nullEndpoint = null!;
            await AssertThrowsEndpointNull(() => client.ConnectAsync(nullEndpoint).AsTask());
        }

        [Test]
        public async Task ConnectAsyncWithCancellationTokenAndNoEndpointThrows()
        {
            using ServerPushConfigurationClient client = CreateClient();
            await AssertThrowsEndpointNull(() => client.ConnectAsync().AsTask());
        }

        [Test]
        public async Task CertificateManagementOperationsWithoutEndpointThrow()
        {
            using ServerPushConfigurationClient client = CreateClient();
            await AssertThrowsEndpointNull(() => client.GetCertificatesAsync(NodeId.Null).AsTask());
            await AssertThrowsEndpointNull(
                () => client.CreateSigningRequestAsync(
                    NodeId.Null, NodeId.Null, string.Empty, false, default).AsTask());
            await AssertThrowsEndpointNull(
                () => client.UpdateCertificateAsync(
                    NodeId.Null, NodeId.Null, default, string.Empty, default, default).AsTask());
            await AssertThrowsEndpointNull(() => client.GetRejectedListAsync().AsTask());
            await AssertThrowsEndpointNull(() => client.ApplyChangesAsync().AsTask());
            await AssertThrowsEndpointNull(
                () => client.CreateSelfSignedCertificateAsync(
                    NodeId.Null, NodeId.Null, string.Empty, default, default, 0, 0).AsTask());
        }

        [Test]
        public async Task TrustListOperationsWithoutEndpointThrow()
        {
            using ServerPushConfigurationClient client = CreateClient();
            await AssertThrowsEndpointNull(() => client.ReadTrustListAsync(NodeId.Null).AsTask());
            await AssertThrowsEndpointNull(() => client.ReadTrustListAsync().AsTask());
            await AssertThrowsEndpointNull(() => client.UpdateTrustListAsync(null!).AsTask());
            await AssertThrowsEndpointNull(
                () => client.UpdateTrustListAsync(null!, 0L).AsTask());
            await AssertThrowsEndpointNull(
                () => client.UpdateTrustListAsync(NodeId.Null, null!, 0L).AsTask());
            await AssertThrowsEndpointNull(() => client.AddCertificateAsync(null!, false).AsTask());
            await AssertThrowsEndpointNull(
                () => client.AddCertificateAsync(NodeId.Null, null!, false).AsTask());
            await AssertThrowsEndpointNull(() => client.RemoveCertificateAsync(string.Empty, false).AsTask());
            await AssertThrowsEndpointNull(
                () => client.RemoveCertificateAsync(NodeId.Null, string.Empty, false).AsTask());
        }

        [Test]
        public async Task GetSupportedKeyFormatsReturnsNullWhenNotConfiguredAsync()
        {
            using ServerPushConfigurationClient client = CreateClient();
            ArrayOf<string> formats = await client.GetSupportedKeyFormatsAsync().ConfigureAwait(false);
            Assert.That(formats.IsNull, Is.True);
        }

        [Test]
        public async Task DisconnectWhenNotConnectedCompletesAsync()
        {
            using ServerPushConfigurationClient client = CreateClient();
            await client.DisconnectAsync().ConfigureAwait(false);
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public void DisposeTwiceDoesNotThrow()
        {
            ServerPushConfigurationClient client = CreateClient();
            client.Dispose();
            Assert.DoesNotThrow(client.Dispose);
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public async Task DisposeAsyncTwiceDoesNotThrowAsync()
        {
            ServerPushConfigurationClient client = CreateClient();
            await client.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
            Assert.That(client.IsConnected, Is.False);
        }
    }
}
