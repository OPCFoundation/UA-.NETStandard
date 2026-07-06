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
    /// Offline unit tests for <see cref="GlobalDiscoveryServerClient"/> covering the
    /// surface reachable without a live directory session: construction, argument
    /// validation, property defaults and round-trips, endpoint plumbing and the
    /// not-connected fast-fail paths of the Directory and CertificateDirectory calls.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("GdsClientOffline")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class GlobalDiscoveryServerClientOfflineTests
    {
        private const string OpcUaNamespaceUri = "http://opcfoundation.org/UA/";
        private const string TestEndpointUrl = "opc.tcp://localhost:58810";

        private static GlobalDiscoveryServerClient CreateClient()
        {
            return new GlobalDiscoveryServerClient(new ApplicationConfiguration());
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
                () => new GlobalDiscoveryServerClient(null!));
            Assert.That(exception.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void ConstructorStoresAdminUserIdentity()
        {
            var identity = new UserIdentity();
            using var client = new GlobalDiscoveryServerClient(new ApplicationConfiguration(), identity);
            Assert.That(client.AdminCredentials, Is.SameAs(identity));
        }

        [Test]
        public void ConstructorWithOptionsRetainsConfigurationAndCredentials()
        {
            var configuration = new ApplicationConfiguration();
            var identity = new UserIdentity();
            ISessionFactory sessionFactory = new Mock<ISessionFactory>().Object;
            using var client = new GlobalDiscoveryServerClient(
                configuration,
                new GdsClientOptions(),
                identity,
                sessionFactory,
                DiagnosticsMasks.None,
                TimeProvider.System);
            Assert.Multiple(() =>
            {
                Assert.That(client.Configuration, Is.SameAs(configuration));
                Assert.That(client.AdminCredentials, Is.SameAs(identity));
            });
        }

        [Test]
        public void ConfigurationReturnsSuppliedInstance()
        {
            var configuration = new ApplicationConfiguration();
            using var client = new GlobalDiscoveryServerClient(configuration);
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
            var identity = new UserIdentity();
            using var client = new GlobalDiscoveryServerClient(new ApplicationConfiguration(), identity);
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
        public void DirectoryDefaultsToNull()
        {
            using var client = CreateClient();
            Assert.That(client.Directory, Is.Null);
        }

        [Test]
        public void CertificateDirectoryDefaultsToNull()
        {
            using var client = CreateClient();
            Assert.That(client.CertificateDirectory, Is.Null);
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
            using var client = new GlobalDiscoveryServerClient(new ApplicationConfiguration())
            {
                Endpoint = endpoint
            };
            Assert.That(client.Endpoint, Is.SameAs(endpoint));
        }

        [Test]
        public void EndpointUrlReflectsConfiguredEndpoint()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            using var client = new GlobalDiscoveryServerClient(new ApplicationConfiguration())
            {
                Endpoint = endpoint
            };
            Assert.That(client.EndpointUrl, Is.EqualTo(endpoint.EndpointUrl!.ToString()));
        }

        [Test]
        public void CreateAsyncWithNullSessionFactoryThrows()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => GlobalDiscoveryServerClient.CreateAsync(
                    (ISessionFactory)null!, new ApplicationConfiguration(), CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("sessionFactory"));
        }

        [Test]
        public void CreateAsyncWithNullConfigurationThrows()
        {
            ISessionFactory sessionFactory = new Mock<ISessionFactory>().Object;
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => GlobalDiscoveryServerClient.CreateAsync(sessionFactory, null!, CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void CreateAsyncWithNullEndpointThrows()
        {
            ISessionFactory sessionFactory = new Mock<ISessionFactory>().Object;
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => GlobalDiscoveryServerClient.CreateAsync(
                    sessionFactory, new ApplicationConfiguration(), null!));
            Assert.That(exception.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public void CreateAsyncWithNullChannelManagerThrows()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => GlobalDiscoveryServerClient.CreateAsync(
                    (IClientChannelManager)null!, new ApplicationConfiguration(), CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("manager"));
        }

        [Test]
        public void CreateAsyncWithChannelManagerNullConfigurationThrows()
        {
            IClientChannelManager manager = new Mock<IClientChannelManager>().Object;
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => GlobalDiscoveryServerClient.CreateAsync(manager, null!, CreateEndpoint()));
            Assert.That(exception.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void CreateAsyncWithChannelManagerNullEndpointThrows()
        {
            IClientChannelManager manager = new Mock<IClientChannelManager>().Object;
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(
                () => GlobalDiscoveryServerClient.CreateAsync(
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
        public void DirectoryOperationsWithoutEndpointThrow()
        {
            using var client = CreateClient();
            AssertThrowsEndpointNull(() => client.FindApplicationAsync(string.Empty).AsTask());
            AssertThrowsEndpointNull(
                () => client.QueryServersAsync(0u, string.Empty, string.Empty, string.Empty, default).AsTask());
            AssertThrowsEndpointNull(
                () => client.QueryServersAsync(
                    0u, 0u, string.Empty, string.Empty, string.Empty, default).AsTask());
            AssertThrowsEndpointNull(
                () => client.QueryApplicationsAsync(
                    0u, 0u, string.Empty, string.Empty, 0u, string.Empty, default).AsTask());
            AssertThrowsEndpointNull(() => client.GetApplicationAsync(NodeId.Null).AsTask());
            AssertThrowsEndpointNull(
                () => client.RegisterApplicationAsync((ApplicationRecordDataType)null!).AsTask());
            AssertThrowsEndpointNull(
                () => client.UpdateApplicationAsync((ApplicationRecordDataType)null!).AsTask());
            AssertThrowsEndpointNull(() => client.UnregisterApplicationAsync(NodeId.Null).AsTask());
        }

        [Test]
        public void CertificateDirectoryOperationsWithoutEndpointThrow()
        {
            using var client = CreateClient();
            AssertThrowsEndpointNull(() => client.GetCertificatesAsync(NodeId.Null, NodeId.Null).AsTask());
            AssertThrowsEndpointNull(() => client.CheckRevocationStatusAsync(default).AsTask());
            AssertThrowsEndpointNull(() => client.RevokeCertificateAsync(NodeId.Null, default).AsTask());
            AssertThrowsEndpointNull(
                () => client.StartNewKeyPairRequestAsync(
                    NodeId.Null,
                    NodeId.Null,
                    NodeId.Null,
                    string.Empty,
                    default,
                    string.Empty,
                    Array.Empty<char>()).AsTask());
            AssertThrowsEndpointNull(
                () => client.StartSigningRequestAsync(
                    NodeId.Null, NodeId.Null, NodeId.Null, default).AsTask());
            AssertThrowsEndpointNull(() => client.FinishRequestAsync(NodeId.Null, NodeId.Null).AsTask());
            AssertThrowsEndpointNull(() => client.GetCertificateGroupsAsync(NodeId.Null).AsTask());
            AssertThrowsEndpointNull(() => client.GetTrustListAsync(NodeId.Null, NodeId.Null).AsTask());
            AssertThrowsEndpointNull(
                () => client.GetCertificateStatusAsync(NodeId.Null, NodeId.Null, NodeId.Null).AsTask());
            AssertThrowsEndpointNull(() => client.ReadTrustListAsync(NodeId.Null).AsTask());
            AssertThrowsEndpointNull(() => client.ReadTrustListAsync(NodeId.Null, 0L).AsTask());
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
