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
using Opc.Ua.Security.Certificates;

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

        private static void AssertThrowsEndpointNull(Func<Task> code)
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(code);
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
            using var client = CreateClient();
            Assert.That(client.MessageContext.NamespaceUris.GetString(0), Is.EqualTo(OpcUaNamespaceUri));
        }

        [Test]
        public void AdminCredentialsDefaultsToNull()
        {
            using var client = CreateClient();
            Assert.That(client.AdminCredentials, Is.Null);
        }

        [Test]
        public void AdminCredentialsRoundTrips()
        {
            var identity = new UserIdentity();
            using var client = CreateClient();
            client.AdminCredentials = identity;
            Assert.That(client.AdminCredentials, Is.SameAs(identity));
        }

        [Test]
        public void ResetCredentialsClearsAdminCredentials()
        {
            using var client = CreateClient();
            client.AdminCredentials = new UserIdentity();
            client.ResetCredentials();
            Assert.That(client.AdminCredentials, Is.Null);
        }

        [Test]
        public void PreferredLocalesDefaultsToNull()
        {
            using var client = CreateClient();
            Assert.That(client.PreferredLocales.IsNull, Is.True);
        }

        [Test]
        public void PreferredLocalesRoundTrips()
        {
            using var client = CreateClient();
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
            using var client = CreateClient();
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public void SessionDefaultsToNull()
        {
            using var client = CreateClient();
            Assert.That(client.Session, Is.Null);
        }

        [Test]
        public void ServerConfigurationDefaultsToNull()
        {
            using var client = CreateClient();
            Assert.That(client.ServerConfiguration, Is.Null);
        }

        [Test]
        public void DefaultApplicationGroupIsNullBeforeConnect()
        {
            using var client = CreateClient();
            Assert.That(client.DefaultApplicationGroup.IsNull, Is.True);
        }

        [Test]
        public void DefaultHttpsGroupIsNullBeforeConnect()
        {
            using var client = CreateClient();
            Assert.That(client.DefaultHttpsGroup.IsNull, Is.True);
        }

        [Test]
        public void DefaultUserTokenGroupIsNullBeforeConnect()
        {
            using var client = CreateClient();
            Assert.That(client.DefaultUserTokenGroup.IsNull, Is.True);
        }

        [Test]
        public void EndpointIsNullWhenNotConfigured()
        {
            using var client = CreateClient();
            Assert.That(client.Endpoint, Is.Null);
        }

        [Test]
        public void EndpointUrlIsNullWhenNotConfigured()
        {
            using var client = CreateClient();
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
        public void CreateAsyncWithNullSessionFactoryThrows()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(
                    (ISessionFactory)null!, new ApplicationConfiguration(), CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("sessionFactory"));
        }

        [Test]
        public void CreateAsyncWithNullConfigurationThrows()
        {
            ISessionFactory sessionFactory = new Mock<ISessionFactory>().Object;
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(sessionFactory, null!, CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void CreateAsyncWithNullEndpointThrows()
        {
            ISessionFactory sessionFactory = new Mock<ISessionFactory>().Object;
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(
                    sessionFactory, new ApplicationConfiguration(), null!));
            Assert.That(exception.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public void CreateAsyncWithNullChannelManagerThrows()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(
                    (IClientChannelManager)null!, new ApplicationConfiguration(), CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("manager"));
        }

        [Test]
        public void CreateAsyncWithChannelManagerNullConfigurationThrows()
        {
            IClientChannelManager manager = new Mock<IClientChannelManager>().Object;
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(manager, null!, CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void CreateAsyncWithChannelManagerNullEndpointThrows()
        {
            IClientChannelManager manager = new Mock<IClientChannelManager>().Object;
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => ServerPushConfigurationClient.CreateAsync(
                    manager, new ApplicationConfiguration(), null!));
            Assert.That(exception.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public void ConnectAsyncWithNullEndpointUrlThrows()
        {
            using var client = CreateClient();
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => client.ConnectAsync((string)null!).AsTask());
            Assert.That(exception.ParamName, Is.EqualTo("endpointUrl"));
        }

        [Test]
        public void ConnectAsyncWithEmptyEndpointUrlThrows()
        {
            using var client = CreateClient();
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => client.ConnectAsync(string.Empty).AsTask());
            Assert.That(exception.ParamName, Is.EqualTo("endpointUrl"));
        }

        [Test]
        public void ConnectAsyncWithMalformedEndpointUrlThrows()
        {
            using var client = CreateClient();
            ArgumentException exception = Assert.ThrowsAsync<ArgumentException>(
                () => client.ConnectAsync("not a valid url").AsTask());
            Assert.Multiple(() =>
            {
                Assert.That(exception.ParamName, Is.EqualTo("endpointUrl"));
                Assert.That(exception.Message, Does.Contain("is not a valid URL."));
            });
        }

        [Test]
        public void ConnectAsyncWithoutEndpointThrows()
        {
            using var client = CreateClient();
            ConfiguredEndpoint nullEndpoint = null!;
            AssertThrowsEndpointNull(() => client.ConnectAsync(nullEndpoint).AsTask());
        }

        [Test]
        public void ConnectAsyncWithCancellationTokenAndNoEndpointThrows()
        {
            using var client = CreateClient();
            AssertThrowsEndpointNull(() => client.ConnectAsync().AsTask());
        }

        [Test]
        public void CertificateManagementOperationsWithoutEndpointThrow()
        {
            using var client = CreateClient();
            AssertThrowsEndpointNull(() => client.GetCertificatesAsync(NodeId.Null).AsTask());
            AssertThrowsEndpointNull(
                () => client.CreateSigningRequestAsync(
                    NodeId.Null, NodeId.Null, string.Empty, false, default).AsTask());
            AssertThrowsEndpointNull(
                () => client.UpdateCertificateAsync(
                    NodeId.Null, NodeId.Null, default, string.Empty, default, default).AsTask());
            AssertThrowsEndpointNull(() => client.GetRejectedListAsync().AsTask());
            AssertThrowsEndpointNull(() => client.ApplyChangesAsync().AsTask());
            AssertThrowsEndpointNull(
                () => client.CreateSelfSignedCertificateAsync(
                    NodeId.Null, NodeId.Null, string.Empty, default, default, 0, 0).AsTask());
        }

        [Test]
        public void TrustListOperationsWithoutEndpointThrow()
        {
            using var client = CreateClient();
            AssertThrowsEndpointNull(() => client.ReadTrustListAsync(NodeId.Null).AsTask());
            AssertThrowsEndpointNull(() => client.ReadTrustListAsync().AsTask());
            AssertThrowsEndpointNull(() => client.UpdateTrustListAsync((TrustListDataType)null!).AsTask());
            AssertThrowsEndpointNull(
                () => client.UpdateTrustListAsync((TrustListDataType)null!, 0L).AsTask());
            AssertThrowsEndpointNull(
                () => client.UpdateTrustListAsync(NodeId.Null, (TrustListDataType)null!, 0L).AsTask());
            AssertThrowsEndpointNull(() => client.AddCertificateAsync((Certificate)null!, false).AsTask());
            AssertThrowsEndpointNull(
                () => client.AddCertificateAsync(NodeId.Null, (Certificate)null!, false).AsTask());
            AssertThrowsEndpointNull(() => client.RemoveCertificateAsync(string.Empty, false).AsTask());
            AssertThrowsEndpointNull(
                () => client.RemoveCertificateAsync(NodeId.Null, string.Empty, false).AsTask());
        }

        [Test]
        public async Task GetSupportedKeyFormatsReturnsNullWhenNotConfiguredAsync()
        {
            using var client = CreateClient();
            ArrayOf<string> formats = await client.GetSupportedKeyFormatsAsync().ConfigureAwait(false);
            Assert.That(formats.IsNull, Is.True);
        }

        [Test]
        public async Task DisconnectWhenNotConnectedCompletesAsync()
        {
            using var client = CreateClient();
            await client.DisconnectAsync().ConfigureAwait(false);
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public void DisposeTwiceDoesNotThrow()
        {
            var client = CreateClient();
            client.Dispose();
            Assert.DoesNotThrow(client.Dispose);
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public async Task DisposeAsyncTwiceDoesNotThrowAsync()
        {
            var client = CreateClient();
            await client.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
            Assert.That(client.IsConnected, Is.False);
        }
    }
}
