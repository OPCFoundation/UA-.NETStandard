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
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Tests for <see cref="DefaultRedundantServerEndpointResolver"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ServerRedundancy")]
    public sealed class DefaultRedundantServerEndpointResolverTests
    {
        [Test]
        public void ResolveAsyncRejectsInvalidArguments()
        {
            var resolver = new DefaultRedundantServerEndpointResolver();
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server", "opc.tcp://server:4840");

            Assert.That(
                async () => await resolver.ResolveAsync(string.Empty, endpoint).ConfigureAwait(false),
                Throws.ArgumentException);
            Assert.That(
                async () => await resolver.ResolveAsync("urn:server", null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task ResolveAsyncReturnsNullWhenCurrentEndpointHasNoDiscoveryUrlAsync()
        {
            var resolver = new DefaultRedundantServerEndpointResolver();
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server", string.Empty);

            ConfiguredEndpoint? result = await resolver
                .ResolveAsync("urn:peer", endpoint)
                .ConfigureAwait(false);

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task ResolveAsyncSelectsEndpointWithMatchingSecurityAsync()
        {
            ConfiguredEndpoint currentEndpoint = CreateEndpoint(
                "urn:current",
                "opc.tcp://current:4840",
                MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256);
            ApplicationDescription peer = CreateApplication("urn:peer", "opc.tcp://peer:4840");
            EndpointDescription downgrade = CreateEndpointDescription(
                "urn:peer",
                "opc.tcp://peer:4840",
                MessageSecurityMode.None,
                SecurityPolicies.None);
            EndpointDescription matching = CreateEndpointDescription(
                "urn:peer",
                "opc.tcp://peer:4840",
                MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256);
            RecordingDiscovery discovery = CreateDiscovery(
                [peer],
                [downgrade, matching]);
            var resolver = new DefaultRedundantServerEndpointResolver(
                NUnitTelemetryContext.Create(),
                discovery);

            ConfiguredEndpoint? result = await resolver
                .ResolveAsync("urn:peer", currentEndpoint)
                .ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
            Assert.That(result.Description.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(result.Description.Server.ApplicationUri, Is.EqualTo("urn:peer"));
        }

        [Test]
        public async Task ResolveAsyncRejectsSecurityDowngradeEndpointAsync()
        {
            ConfiguredEndpoint currentEndpoint = CreateEndpoint(
                "urn:current",
                "opc.tcp://current:4840",
                MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256);
            ApplicationDescription peer = CreateApplication("urn:peer", "opc.tcp://peer:4840");
            EndpointDescription downgrade = CreateEndpointDescription(
                "urn:peer",
                "opc.tcp://peer:4840",
                MessageSecurityMode.None,
                SecurityPolicies.None);
            RecordingDiscovery discovery = CreateDiscovery([peer], [downgrade]);
            var resolver = new DefaultRedundantServerEndpointResolver(
                NUnitTelemetryContext.Create(),
                discovery);

            ConfiguredEndpoint? result = await resolver
                .ResolveAsync("urn:peer", currentEndpoint)
                .ConfigureAwait(false);

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task ResolveAsyncReturnsNullWhenFindServersDoesNotReturnPeerAsync()
        {
            ConfiguredEndpoint currentEndpoint = CreateEndpoint("urn:current", "opc.tcp://current:4840");
            RecordingDiscovery discovery = CreateDiscovery(
                [CreateApplication("urn:other", "opc.tcp://other:4840")],
                []);
            var resolver = new DefaultRedundantServerEndpointResolver(
                NUnitTelemetryContext.Create(),
                discovery);

            ConfiguredEndpoint? result = await resolver
                .ResolveAsync("urn:peer", currentEndpoint)
                .ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(discovery.GetEndpointsCallCount, Is.Zero);
        }

        [Test]
        public async Task ResolveAsyncTriesNextDiscoveryUrlWhenFirstHasNoMatchingEndpointAsync()
        {
            ConfiguredEndpoint currentEndpoint = CreateEndpoint(
                "urn:current",
                "opc.tcp://current:4840",
                MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256);
            currentEndpoint.Description.Server.DiscoveryUrls =
            [
                "opc.tcp://discovery-a:4840",
                "opc.tcp://discovery-b:4840"
            ];
            ApplicationDescription peer = CreateApplication("urn:peer", "opc.tcp://peer:4840");
            EndpointDescription matching = CreateEndpointDescription(
                "urn:peer",
                "opc.tcp://peer:4840",
                MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256);
            var discovery = new RecordingDiscovery(
                [peer],
                [[], new ArrayOf<EndpointDescription>(new[] { matching })]);
            var resolver = new DefaultRedundantServerEndpointResolver(
                NUnitTelemetryContext.Create(),
                discovery);

            ConfiguredEndpoint? result = await resolver
                .ResolveAsync("urn:peer", currentEndpoint)
                .ConfigureAwait(false);

            Assert.That(result, Is.Not.Null);
            Assert.That(discovery.GetEndpointsCallCount, Is.EqualTo(2));
        }

        [Test]
        public void PrivateSelectionHelpersMatchSecurityAndDiscoveryFallbacks()
        {
            EndpointDescription current = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://server:4840",
                MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256);
            EndpointDescription same = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://backup:4840",
                MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256);
            EndpointDescription different = CreateEndpointDescription(
                "urn:server",
                "https://backup:443",
                MessageSecurityMode.None,
                SecurityPolicies.None);

            Assert.That(InvokeBool("IsSameScheme", same, current), Is.True);
            Assert.That(InvokeBool("IsSameSecurity", same, current), Is.True);
            Assert.That(InvokeBool("IsSameScheme", different, current), Is.False);
            Assert.That(InvokeBool("IsSameSecurity", different, current), Is.False);

            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server", "opc.tcp://server:4840");
            endpoint.Description.Server.DiscoveryUrls = ["opc.tcp://discovery:4840"];
            Assert.That(InvokeDiscoveryUrls(endpoint), Is.EqualTo(s_discoveryUrls));

            endpoint.Description.Server.DiscoveryUrls = [];
            Assert.That(InvokeDiscoveryUrls(endpoint), Is.EqualTo(s_endpointUrls));
        }

        [Test]
        public void IsSameSchemeMissingUrisReturnsFalse()
        {
            EndpointDescription current = CreateEndpointDescription(
                "urn:server",
                string.Empty,
                MessageSecurityMode.None,
                SecurityPolicies.None);
            EndpointDescription candidate = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://server:4840",
                MessageSecurityMode.None,
                SecurityPolicies.None);

            Assert.That(InvokeBool("IsSameScheme", candidate, current), Is.False);
            Assert.That(InvokeBool("IsSameScheme", current, candidate), Is.False);
        }

        [Test]
        public void IsSameSecurityMatchesSignAndEncryptEndpoints()
        {
            EndpointDescription endpoint1 = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://server:4840",
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256);
            EndpointDescription endpoint2 = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://backup:4840",
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256);

            Assert.That(InvokeBool("IsSameSecurity", endpoint1, endpoint2), Is.True);
        }

        [Test]
        public void IsSameSecurityDifferentiatesSecurityModes()
        {
            EndpointDescription signMode = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://server:4840",
                MessageSecurityMode.Sign,
                SecurityPolicies.Basic256Sha256);
            EndpointDescription noneMode = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://server:4840",
                MessageSecurityMode.None,
                SecurityPolicies.None);

            Assert.That(InvokeBool("IsSameSecurity", signMode, noneMode), Is.False);
        }

        [Test]
        public void IsSameSecurityDifferentiatesSecurityPolicies()
        {
            EndpointDescription basic256 = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://server:4840",
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Basic256Sha256);
            EndpointDescription aes128 = CreateEndpointDescription(
                "urn:server",
                "opc.tcp://server:4840",
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Aes128_Sha256_RsaOaep);

            Assert.That(InvokeBool("IsSameSecurity", basic256, aes128), Is.False);
        }

        [Test]
        public void GetDiscoveryUrlsHandlesEmptyEndpointUrl()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server", string.Empty);
            endpoint.Description.Server = new ApplicationDescription { ApplicationUri = "urn:server" };

            string[] urls = InvokeDiscoveryUrls(endpoint);

            Assert.That(urls, Has.Length.EqualTo(0));
        }

        [Test]
        public void GetDiscoveryUrlsReturnsEndpointUrlWhenNoServerDiscoveryUrls()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server", "opc.tcp://server:4840");
            endpoint.Description.Server.DiscoveryUrls = [];

            string[] urls = InvokeDiscoveryUrls(endpoint);

            Assert.That(urls, Has.Length.EqualTo(1));
            Assert.That(urls[0], Is.EqualTo("opc.tcp://server:4840"));
        }

        [Test]
        public void GetDiscoveryUrlsReturnsServerDiscoveryUrlsWhenAvailable()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server", "opc.tcp://server:4840");
            endpoint.Description.Server.DiscoveryUrls = ["opc.tcp://discovery:4840", "https://discovery:443"];

            string[] urls = InvokeDiscoveryUrls(endpoint);

            Assert.That(urls, Has.Length.EqualTo(2));
            Assert.That(urls[0], Is.EqualTo("opc.tcp://discovery:4840"));
            Assert.That(urls[1], Is.EqualTo("https://discovery:443"));
        }

        [Test]
        public void GetDiscoveryUrlsHandlesNullServer()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint("urn:server", "opc.tcp://server:4840");
            endpoint.Description.Server = null!;

            string[] urls = InvokeDiscoveryUrls(endpoint);

            Assert.That(urls, Has.Length.EqualTo(1));
            Assert.That(urls[0], Is.EqualTo("opc.tcp://server:4840"));
        }

        private static bool InvokeBool(
            string methodName,
            EndpointDescription endpoint,
            EndpointDescription currentEndpoint)
        {
            MethodInfo method = typeof(DefaultRedundantServerEndpointResolver).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return (bool)method.Invoke(null, [endpoint, currentEndpoint])!;
        }

        private static string[] InvokeDiscoveryUrls(ConfiguredEndpoint endpoint)
        {
            MethodInfo method = typeof(DefaultRedundantServerEndpointResolver).GetMethod(
                "GetDiscoveryUrls",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var urls = (IEnumerable<string>)method.Invoke(null, [endpoint])!;
            var result = new List<string>();
            foreach (string url in urls)
            {
                result.Add(url);
            }

            return result.ToArray();
        }

        private static ConfiguredEndpoint CreateEndpoint(string serverUri, string endpointUrl)
        {
            return CreateEndpoint(
                serverUri,
                endpointUrl,
                MessageSecurityMode.None,
                SecurityPolicies.None);
        }

        private static ConfiguredEndpoint CreateEndpoint(
            string serverUri,
            string endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            return new ConfiguredEndpoint(
                null,
                CreateEndpointDescription(
                    serverUri,
                    endpointUrl,
                    securityMode,
                    securityPolicyUri),
                configuration: null);
        }

        private static EndpointDescription CreateEndpointDescription(
            string serverUri,
            string endpointUrl,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri,
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri,
                    DiscoveryUrls = string.IsNullOrEmpty(endpointUrl)
                        ? []
                        : [endpointUrl]
                }
            };
        }

        private static ApplicationDescription CreateApplication(
            string serverUri,
            string discoveryUrl)
        {
            return new ApplicationDescription
            {
                ApplicationUri = serverUri,
                DiscoveryUrls = [discoveryUrl]
            };
        }

        private static RecordingDiscovery CreateDiscovery(
            ApplicationDescription[] applications,
            EndpointDescription[] endpoints)
        {
            return new RecordingDiscovery(
                new ArrayOf<ApplicationDescription>(applications),
                [new ArrayOf<EndpointDescription>(endpoints)]);
        }

        private sealed class RecordingDiscovery : IRedundantServerDiscovery
        {
            public RecordingDiscovery(
                ArrayOf<ApplicationDescription> applications,
                ArrayOf<EndpointDescription>[] endpointResponses)
            {
                m_applications = applications;
                m_endpointResponses = endpointResponses;
            }

            public int GetEndpointsCallCount { get; private set; }

            public ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
                Uri discoveryUri,
                EndpointConfiguration configuration,
                string serverUri,
                ITelemetryContext telemetry,
                CancellationToken ct)
            {
                return new ValueTask<ArrayOf<ApplicationDescription>>(m_applications);
            }

            public ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
                Uri discoveryUri,
                EndpointConfiguration configuration,
                ITelemetryContext telemetry,
                CancellationToken ct)
            {
                int index = Math.Min(GetEndpointsCallCount, m_endpointResponses.Length - 1);
                GetEndpointsCallCount++;
                return new ValueTask<ArrayOf<EndpointDescription>>(m_endpointResponses[index]);
            }

            private readonly ArrayOf<ApplicationDescription> m_applications;
            private readonly ArrayOf<EndpointDescription>[] m_endpointResponses;
        }

        private static readonly string[] s_discoveryUrls = ["opc.tcp://discovery:4840"];
        private static readonly string[] s_endpointUrls = ["opc.tcp://server:4840"];
    }
}
