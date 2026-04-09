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

using System;
using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// XML round-trip tests for source-generated IEncodeable configuration types.
    /// </summary>
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConfigurationRoundTripTests
    {
        [Test]
        public void FullApplicationConfigurationRoundTrip()
        {
            // Parse the XML fixture
            ApplicationConfiguration config = DecodeFromFile<ApplicationConfiguration>("testconfig-roundtrip.xml");

            // Verify all parsed fields
            Assert.That(config.ApplicationName, Is.EqualTo("Round Trip Test"));
            Assert.That(config.ApplicationUri, Is.EqualTo("urn:localhost:roundtriptest"));
            Assert.That(config.ProductUri, Is.EqualTo("http://test.org/roundtrip"));
            Assert.That(config.ApplicationType, Is.EqualTo(ApplicationType.Server));

            // SecurityConfiguration
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
            Assert.That(
                config.SecurityConfiguration.ApplicationCertificate.StoreType,
                Is.EqualTo(CertificateStoreType.Directory));
            Assert.That(
                config.SecurityConfiguration.ApplicationCertificate.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/test"));
            Assert.That(
                config.SecurityConfiguration.ApplicationCertificate.SubjectName,
                Is.EqualTo("CN=Test, O=OPC Foundation"));
            Assert.That(
                config.SecurityConfiguration.TrustedIssuerCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/issuers"));
            Assert.That(
                config.SecurityConfiguration.TrustedPeerCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/trusted"));
            Assert.That(
                config.SecurityConfiguration.RejectedCertificateStore.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/rejected"));
            Assert.That(config.SecurityConfiguration.NonceLength, Is.EqualTo(32));
            Assert.That(
                config.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                Is.True);
            Assert.That(
                config.SecurityConfiguration.RejectSHA1SignedCertificates,
                Is.True);
            Assert.That(
                config.SecurityConfiguration.MinimumCertificateKeySize,
                Is.EqualTo(2048));

            // TransportQuotas
            Assert.That(config.TransportQuotas, Is.Not.Null);
            Assert.That(config.TransportQuotas.OperationTimeout, Is.EqualTo(60000));
            Assert.That(config.TransportQuotas.MaxStringLength, Is.EqualTo(1048576));
            Assert.That(config.TransportQuotas.MaxByteStringLength, Is.EqualTo(4194304));
            Assert.That(config.TransportQuotas.MaxArrayLength, Is.EqualTo(65535));
            Assert.That(config.TransportQuotas.MaxMessageSize, Is.EqualTo(4194304));
            Assert.That(config.TransportQuotas.MaxBufferSize, Is.EqualTo(65535));
            Assert.That(config.TransportQuotas.ChannelLifetime, Is.EqualTo(300000));
            Assert.That(config.TransportQuotas.SecurityTokenLifetime, Is.EqualTo(3600000));

            // ServerConfiguration
            Assert.That(config.ServerConfiguration, Is.Not.Null);
            Assert.That(config.ServerConfiguration.BaseAddresses, Has.Count.EqualTo(1));
            Assert.That(
                config.ServerConfiguration.BaseAddresses[0],
                Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(config.ServerConfiguration.SecurityPolicies, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(config.ServerConfiguration.MaxSessionCount, Is.EqualTo(100));
            Assert.That(config.ServerConfiguration.MinSessionTimeout, Is.EqualTo(10000));
            Assert.That(config.ServerConfiguration.MaxSessionTimeout, Is.EqualTo(3600000));

            // TraceConfiguration
            Assert.That(config.TraceConfiguration, Is.Not.Null);
            Assert.That(config.TraceConfiguration.OutputFilePath, Is.EqualTo("test.log"));
            Assert.That(config.TraceConfiguration.TraceMasks, Is.EqualTo(519));

            // Encode to XML
            string xml = EncodeToXml(config);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            // Re-parse and verify round-trip
            ApplicationConfiguration roundTripped = DecodeFromString<ApplicationConfiguration>(xml);

            Assert.That(roundTripped.ApplicationName, Is.EqualTo(config.ApplicationName));
            Assert.That(roundTripped.ApplicationUri, Is.EqualTo(config.ApplicationUri));
            Assert.That(roundTripped.ProductUri, Is.EqualTo(config.ProductUri));
            Assert.That(roundTripped.ApplicationType, Is.EqualTo(config.ApplicationType));
            Assert.That(
                roundTripped.SecurityConfiguration.NonceLength,
                Is.EqualTo(config.SecurityConfiguration.NonceLength));
            Assert.That(
                roundTripped.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                Is.EqualTo(config.SecurityConfiguration.AutoAcceptUntrustedCertificates));
            Assert.That(
                roundTripped.SecurityConfiguration.RejectSHA1SignedCertificates,
                Is.EqualTo(config.SecurityConfiguration.RejectSHA1SignedCertificates));
            Assert.That(
                roundTripped.SecurityConfiguration.MinimumCertificateKeySize,
                Is.EqualTo(config.SecurityConfiguration.MinimumCertificateKeySize));
            Assert.That(
                roundTripped.TransportQuotas.OperationTimeout,
                Is.EqualTo(config.TransportQuotas.OperationTimeout));
            Assert.That(
                roundTripped.TransportQuotas.MaxStringLength,
                Is.EqualTo(config.TransportQuotas.MaxStringLength));
            Assert.That(
                roundTripped.TransportQuotas.MaxByteStringLength,
                Is.EqualTo(config.TransportQuotas.MaxByteStringLength));
            Assert.That(
                roundTripped.TransportQuotas.MaxMessageSize,
                Is.EqualTo(config.TransportQuotas.MaxMessageSize));
            Assert.That(
                roundTripped.ServerConfiguration.BaseAddresses[0],
                Is.EqualTo(config.ServerConfiguration.BaseAddresses[0]));
            Assert.That(
                roundTripped.ServerConfiguration.MaxSessionCount,
                Is.EqualTo(config.ServerConfiguration.MaxSessionCount));
            Assert.That(
                roundTripped.TraceConfiguration.OutputFilePath,
                Is.EqualTo(config.TraceConfiguration.OutputFilePath));
            Assert.That(
                roundTripped.TraceConfiguration.TraceMasks,
                Is.EqualTo(config.TraceConfiguration.TraceMasks));
        }

        [Test]
        public void FullClientConfigurationRoundTrip()
        {
            // Parse the XML fixture
            ApplicationConfiguration config = DecodeFromFile<ApplicationConfiguration>("test-client-full.xml");

            // Verify top-level fields
            Assert.That(config.ApplicationName, Is.EqualTo("Client Test"));
            Assert.That(config.ApplicationUri, Is.EqualTo("urn:localhost:clienttest"));
            Assert.That(config.ProductUri, Is.EqualTo("http://test.org/client"));
            Assert.That(config.ApplicationType, Is.EqualTo(ApplicationType.Client));

            // ClientConfiguration
            Assert.That(config.ClientConfiguration, Is.Not.Null);
            Assert.That(
                config.ClientConfiguration.DefaultSessionTimeout,
                Is.EqualTo(120000));
            Assert.That(
                config.ClientConfiguration.WellKnownDiscoveryUrls,
                Has.Count.EqualTo(2));
            Assert.That(
                config.ClientConfiguration.WellKnownDiscoveryUrls[0],
                Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(
                config.ClientConfiguration.WellKnownDiscoveryUrls[1],
                Is.EqualTo("https://localhost:4843"));
            Assert.That(
                config.ClientConfiguration.EndpointCacheFilePath,
                Is.EqualTo("endpoints.xml"));
            Assert.That(
                config.ClientConfiguration.MinSubscriptionLifetime,
                Is.EqualTo(20000));

            // ReverseConnect
            Assert.That(config.ClientConfiguration.ReverseConnect, Is.Not.Null);
            Assert.That(
                config.ClientConfiguration.ReverseConnect.ClientEndpoints,
                Has.Count.EqualTo(2));
            Assert.That(
                config.ClientConfiguration.ReverseConnect.ClientEndpoints[0].EndpointUrl,
                Is.EqualTo("opc.tcp://localhost:65300"));
            Assert.That(
                config.ClientConfiguration.ReverseConnect.ClientEndpoints[1].EndpointUrl,
                Is.EqualTo("opc.tcp://localhost:65301"));
            Assert.That(
                config.ClientConfiguration.ReverseConnect.HoldTime,
                Is.EqualTo(30000));
            Assert.That(
                config.ClientConfiguration.ReverseConnect.WaitTimeout,
                Is.EqualTo(40000));

            // OperationLimits
            Assert.That(config.ClientConfiguration.OperationLimits, Is.Not.Null);
            Assert.That(
                config.ClientConfiguration.OperationLimits.MaxNodesPerRead,
                Is.EqualTo(1000));
            Assert.That(
                config.ClientConfiguration.OperationLimits.MaxNodesPerWrite,
                Is.EqualTo(500));
            Assert.That(
                config.ClientConfiguration.OperationLimits.MaxNodesPerBrowse,
                Is.EqualTo(2000));
            Assert.That(
                config.ClientConfiguration.OperationLimits.MaxNodesPerMethodCall,
                Is.EqualTo(100));

            // Encode to XML
            string xml = EncodeToXml(config);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            // Re-parse and verify round-trip
            ApplicationConfiguration rt = DecodeFromString<ApplicationConfiguration>(xml);

            Assert.That(rt.ApplicationName, Is.EqualTo(config.ApplicationName));
            Assert.That(rt.ApplicationUri, Is.EqualTo(config.ApplicationUri));
            Assert.That(rt.ProductUri, Is.EqualTo(config.ProductUri));
            Assert.That(rt.ApplicationType, Is.EqualTo(config.ApplicationType));
            Assert.That(
                rt.ClientConfiguration.DefaultSessionTimeout,
                Is.EqualTo(config.ClientConfiguration.DefaultSessionTimeout));
            Assert.That(
                rt.ClientConfiguration.WellKnownDiscoveryUrls,
                Has.Count.EqualTo(
                    config.ClientConfiguration.WellKnownDiscoveryUrls.Count));
            Assert.That(
                rt.ClientConfiguration.WellKnownDiscoveryUrls[0],
                Is.EqualTo(config.ClientConfiguration.WellKnownDiscoveryUrls[0]));
            Assert.That(
                rt.ClientConfiguration.WellKnownDiscoveryUrls[1],
                Is.EqualTo(config.ClientConfiguration.WellKnownDiscoveryUrls[1]));
            Assert.That(
                rt.ClientConfiguration.EndpointCacheFilePath,
                Is.EqualTo(config.ClientConfiguration.EndpointCacheFilePath));
            Assert.That(
                rt.ClientConfiguration.MinSubscriptionLifetime,
                Is.EqualTo(config.ClientConfiguration.MinSubscriptionLifetime));
            Assert.That(
                rt.ClientConfiguration.ReverseConnect.ClientEndpoints,
                Has.Count.EqualTo(
                    config.ClientConfiguration.ReverseConnect.ClientEndpoints.Count));
            Assert.That(
                rt.ClientConfiguration.ReverseConnect.ClientEndpoints[0].EndpointUrl,
                Is.EqualTo(
                    config.ClientConfiguration.ReverseConnect.ClientEndpoints[0].EndpointUrl));
            Assert.That(
                rt.ClientConfiguration.ReverseConnect.ClientEndpoints[1].EndpointUrl,
                Is.EqualTo(
                    config.ClientConfiguration.ReverseConnect.ClientEndpoints[1].EndpointUrl));
            Assert.That(
                rt.ClientConfiguration.ReverseConnect.HoldTime,
                Is.EqualTo(config.ClientConfiguration.ReverseConnect.HoldTime));
            Assert.That(
                rt.ClientConfiguration.ReverseConnect.WaitTimeout,
                Is.EqualTo(config.ClientConfiguration.ReverseConnect.WaitTimeout));
            Assert.That(
                rt.ClientConfiguration.OperationLimits.MaxNodesPerRead,
                Is.EqualTo(config.ClientConfiguration.OperationLimits.MaxNodesPerRead));
            Assert.That(
                rt.ClientConfiguration.OperationLimits.MaxNodesPerWrite,
                Is.EqualTo(config.ClientConfiguration.OperationLimits.MaxNodesPerWrite));
            Assert.That(
                rt.ClientConfiguration.OperationLimits.MaxNodesPerBrowse,
                Is.EqualTo(config.ClientConfiguration.OperationLimits.MaxNodesPerBrowse));
            Assert.That(
                rt.ClientConfiguration.OperationLimits.MaxNodesPerMethodCall,
                Is.EqualTo(config.ClientConfiguration.OperationLimits.MaxNodesPerMethodCall));
        }

        [Test]
        public void ConfiguredEndpointCollectionFullRoundTrip()
        {
            // Parse the XML fixture
            ConfiguredEndpointCollection collection = DecodeFromFile<ConfiguredEndpointCollection>(
                "test-endpoints-full.xml");

            // KnownHosts
            Assert.That(collection.KnownHosts, Has.Count.EqualTo(2));
            Assert.That(collection.KnownHosts[0], Is.EqualTo("localhost"));
            Assert.That(collection.KnownHosts[1], Is.EqualTo("192.168.1.100"));

            // Endpoints count
            Assert.That(collection.Endpoints, Has.Count.EqualTo(3));

            // Endpoint 0: SignAndEncrypt, Optional
            ConfiguredEndpoint ep0 = collection.Endpoints[0];
            Assert.That(
                ep0.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(
                ep0.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(
                ep0.Description.SecurityPolicyUri,
                Is.EqualTo(
                    "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256"));
            Assert.That(ep0.UpdateBeforeConnect, Is.True);
            Assert.That(
                ep0.BinaryEncodingSupport,
                Is.EqualTo(BinaryEncodingSupport.Optional));
            Assert.That(ep0.SelectedUserTokenPolicyIndex, Is.Zero);

            // Endpoint 1: None security, Required encoding, ReverseConnect
            ConfiguredEndpoint ep1 = collection.Endpoints[1];
            Assert.That(
                ep1.Description.EndpointUrl,
                Is.EqualTo("https://192.168.1.100:4843"));
            Assert.That(
                ep1.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
            Assert.That(
                ep1.Description.SecurityPolicyUri,
                Is.EqualTo(
                    "http://opcfoundation.org/UA/SecurityPolicy#None"));
            Assert.That(ep1.UpdateBeforeConnect, Is.False);
            Assert.That(
                ep1.BinaryEncodingSupport,
                Is.EqualTo(BinaryEncodingSupport.Required));
            Assert.That(ep1.SelectedUserTokenPolicyIndex, Is.EqualTo(1));
            Assert.That(ep1.ReverseConnect, Is.Not.Null);
            Assert.That(ep1.ReverseConnect.Enabled, Is.True);
            Assert.That(
                ep1.ReverseConnect.ServerUri,
                Is.EqualTo("urn:server:example"));
            Assert.That(
                ep1.ReverseConnect.Thumbprint,
                Is.EqualTo("AABBCCDDEEFF00112233"));

            // Endpoint 2: Sign, None encoding
            ConfiguredEndpoint ep2 = collection.Endpoints[2];
            Assert.That(
                ep2.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://localhost:4841"));
            Assert.That(
                ep2.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.Sign));
            Assert.That(
                ep2.Description.SecurityPolicyUri,
                Is.EqualTo(
                    "http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep"));
            Assert.That(ep2.UpdateBeforeConnect, Is.True);
            Assert.That(
                ep2.BinaryEncodingSupport,
                Is.EqualTo(BinaryEncodingSupport.None));
            Assert.That(ep2.SelectedUserTokenPolicyIndex, Is.EqualTo(2));

            // TcpProxyUrl
            Assert.That(
                collection.TcpProxyUrl,
                Is.EqualTo(new Uri("opc.tcp://proxy.example.com:4840")));

            // Encode to XML
            string xml = EncodeToXml(collection);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            // Re-parse and verify round-trip
            ConfiguredEndpointCollection rt = DecodeFromString<ConfiguredEndpointCollection>(xml);

            Assert.That(rt.KnownHosts, Has.Count.EqualTo(collection.KnownHosts.Count));
            Assert.That(rt.KnownHosts[0], Is.EqualTo(collection.KnownHosts[0]));
            Assert.That(rt.KnownHosts[1], Is.EqualTo(collection.KnownHosts[1]));
            Assert.That(rt.Endpoints, Has.Count.EqualTo(collection.Endpoints.Count));

            for (int i = 0; i < collection.Endpoints.Count; i++)
            {
                Assert.That(
                    rt.Endpoints[i].Description.EndpointUrl,
                    Is.EqualTo(collection.Endpoints[i].Description.EndpointUrl));
                Assert.That(
                    rt.Endpoints[i].Description.SecurityMode,
                    Is.EqualTo(collection.Endpoints[i].Description.SecurityMode));
                Assert.That(
                    rt.Endpoints[i].Description.SecurityPolicyUri,
                    Is.EqualTo(collection.Endpoints[i].Description.SecurityPolicyUri));
                Assert.That(
                    rt.Endpoints[i].UpdateBeforeConnect,
                    Is.EqualTo(collection.Endpoints[i].UpdateBeforeConnect));
                Assert.That(
                    rt.Endpoints[i].BinaryEncodingSupport,
                    Is.EqualTo(collection.Endpoints[i].BinaryEncodingSupport));
                Assert.That(
                    rt.Endpoints[i].SelectedUserTokenPolicyIndex,
                    Is.EqualTo(collection.Endpoints[i].SelectedUserTokenPolicyIndex));
            }

            // Verify ReverseConnect round-trip for endpoint 1
            Assert.That(rt.Endpoints[1].ReverseConnect, Is.Not.Null);
            Assert.That(
                rt.Endpoints[1].ReverseConnect.Enabled,
                Is.EqualTo(ep1.ReverseConnect.Enabled));
            Assert.That(
                rt.Endpoints[1].ReverseConnect.ServerUri,
                Is.EqualTo(ep1.ReverseConnect.ServerUri));
            Assert.That(
                rt.Endpoints[1].ReverseConnect.Thumbprint,
                Is.EqualTo(ep1.ReverseConnect.Thumbprint));

            // TcpProxyUrl round-trip
            Assert.That(rt.TcpProxyUrl, Is.EqualTo(collection.TcpProxyUrl));
        }

        [Test]
        public void ConfiguredEndpointCollectionRoundTrip()
        {
            // Parse the XML fixture
            ConfiguredEndpointCollection collection = DecodeFromFile<ConfiguredEndpointCollection>("test-endpoints.xml");

            // Verify parsed fields
            Assert.That(collection.KnownHosts, Has.Count.EqualTo(1));
            Assert.That(collection.KnownHosts[0], Is.EqualTo("localhost"));
            Assert.That(collection.Endpoints, Has.Count.EqualTo(1));

            ConfiguredEndpoint endpoint = collection.Endpoints[0];
            Assert.That(
                endpoint.Description.EndpointUrl,
                Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(
                endpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(
                endpoint.Description.SecurityPolicyUri,
                Is.EqualTo("http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256"));
            Assert.That(endpoint.UpdateBeforeConnect, Is.True);
            Assert.That(endpoint.BinaryEncodingSupport, Is.EqualTo(BinaryEncodingSupport.Optional));
            Assert.That(endpoint.SelectedUserTokenPolicyIndex, Is.Zero);

            // Encode to XML
            string xml = EncodeToXml(collection);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            // Re-parse and verify round-trip
            ConfiguredEndpointCollection roundTripped = DecodeFromString<ConfiguredEndpointCollection>(xml);

            Assert.That(roundTripped.KnownHosts, Has.Count.EqualTo(collection.KnownHosts.Count));
            Assert.That(roundTripped.KnownHosts[0], Is.EqualTo(collection.KnownHosts[0]));
            Assert.That(roundTripped.Endpoints, Has.Count.EqualTo(collection.Endpoints.Count));

            ConfiguredEndpoint rtEndpoint = roundTripped.Endpoints[0];
            Assert.That(
                rtEndpoint.Description.EndpointUrl,
                Is.EqualTo(endpoint.Description.EndpointUrl));
            Assert.That(
                rtEndpoint.Description.SecurityMode,
                Is.EqualTo(endpoint.Description.SecurityMode));
            Assert.That(
                rtEndpoint.Description.SecurityPolicyUri,
                Is.EqualTo(endpoint.Description.SecurityPolicyUri));
            Assert.That(
                rtEndpoint.UpdateBeforeConnect,
                Is.EqualTo(endpoint.UpdateBeforeConnect));
            Assert.That(
                rtEndpoint.BinaryEncodingSupport,
                Is.EqualTo(endpoint.BinaryEncodingSupport));
            Assert.That(
                rtEndpoint.SelectedUserTokenPolicyIndex,
                Is.EqualTo(endpoint.SelectedUserTokenPolicyIndex));
        }

        [Test]
        public void OAuth2CredentialRoundTrip()
        {
            // Parse the XML fixture
            OAuth2Credential credential = DecodeFromFile<OAuth2Credential>("test-oauth2.xml");

            // Verify parsed fields
            Assert.That(
                credential.AuthorityUrl,
                Is.EqualTo("https://auth.example.com"));
            Assert.That(credential.GrantType, Is.EqualTo("authorization_code"));
            Assert.That(credential.ClientId, Is.EqualTo("test-client-id"));
            Assert.That(credential.ClientSecret, Is.EqualTo("test-client-secret"));
            Assert.That(
                credential.RedirectUrl,
                Is.EqualTo("https://auth.example.com/callback"));
            Assert.That(
                credential.TokenEndpoint,
                Is.EqualTo("https://auth.example.com/token"));
            Assert.That(
                credential.AuthorizationEndpoint,
                Is.EqualTo("https://auth.example.com/authorize"));

            // Encode to XML
            string xml = EncodeToXml(credential);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            // Re-parse and verify round-trip
            OAuth2Credential roundTripped = DecodeFromString<OAuth2Credential>(xml);

            Assert.That(roundTripped.AuthorityUrl, Is.EqualTo(credential.AuthorityUrl));
            Assert.That(roundTripped.GrantType, Is.EqualTo(credential.GrantType));
            Assert.That(roundTripped.ClientId, Is.EqualTo(credential.ClientId));
            Assert.That(roundTripped.ClientSecret, Is.EqualTo(credential.ClientSecret));
            Assert.That(roundTripped.RedirectUrl, Is.EqualTo(credential.RedirectUrl));
            Assert.That(roundTripped.TokenEndpoint, Is.EqualTo(credential.TokenEndpoint));
            Assert.That(
                roundTripped.AuthorizationEndpoint,
                Is.EqualTo(credential.AuthorizationEndpoint));
        }

        [Test]
        public void LegacySecurityConfigurationRoundTrip()
        {
            ApplicationConfiguration config = DecodeFromFile<ApplicationConfiguration>(
                "test-security-legacy.xml");

            Assert.That(config.ApplicationName, Is.EqualTo("Legacy Test"));
            Assert.That(config.ApplicationUri, Is.EqualTo("urn:localhost:legacytest"));

            SecurityConfiguration sec = config.SecurityConfiguration;
            Assert.That(sec, Is.Not.Null);
            Assert.That(sec.IsDeprecatedConfiguration, Is.True);

            // Legacy format exposes the single cert via ApplicationCertificate
            Assert.That(sec.ApplicationCertificate, Is.Not.Null);
            Assert.That(
                sec.ApplicationCertificate.SubjectName,
                Is.EqualTo("CN=Legacy, O=Test"));
            Assert.That(
                sec.ApplicationCertificate.StoreType,
                Is.EqualTo(CertificateStoreType.Directory));
            Assert.That(
                sec.ApplicationCertificate.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/legacy"));

            // The backing collection should contain 1 entry
            Assert.That(sec.ApplicationCertificates, Has.Count.EqualTo(1));

            Assert.That(
                sec.TrustedIssuerCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/issuers"));
            Assert.That(
                sec.TrustedPeerCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/trusted"));
            Assert.That(sec.NonceLength, Is.EqualTo(32));
            Assert.That(sec.RejectSHA1SignedCertificates, Is.True);
            Assert.That(sec.MinimumCertificateKeySize, Is.EqualTo(2048));
            Assert.That(sec.SendCertificateChain, Is.True);
            Assert.That(sec.AddAppCertToTrustedStore, Is.True);

            // Round-trip
            string xml = EncodeToXml(config);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            ApplicationConfiguration rt = DecodeFromString<ApplicationConfiguration>(xml);

            // After round-trip, both ApplicationCertificate (legacy) and
            // ApplicationCertificates (modern) are written; re-decode
            // normalizes to modern format (IsDeprecatedConfiguration = false).
            Assert.That(rt.SecurityConfiguration.IsDeprecatedConfiguration, Is.False);
            Assert.That(
                rt.SecurityConfiguration.ApplicationCertificates,
                Has.Count.EqualTo(1));
            Assert.That(
                rt.SecurityConfiguration.ApplicationCertificate.SubjectName,
                Is.EqualTo("CN=Legacy, O=Test"));
            Assert.That(
                rt.SecurityConfiguration.RejectSHA1SignedCertificates,
                Is.EqualTo(sec.RejectSHA1SignedCertificates));
            Assert.That(
                rt.SecurityConfiguration.SendCertificateChain,
                Is.EqualTo(sec.SendCertificateChain));
            Assert.That(
                rt.SecurityConfiguration.AddAppCertToTrustedStore,
                Is.EqualTo(sec.AddAppCertToTrustedStore));
        }

        [Test]
        public void ModernSecurityConfigurationRoundTrip()
        {
            ApplicationConfiguration config = DecodeFromFile<ApplicationConfiguration>(
                "test-security-modern.xml");

            Assert.That(config.ApplicationName, Is.EqualTo("Modern Test"));
            Assert.That(config.ApplicationUri, Is.EqualTo("urn:localhost:moderntest"));

            SecurityConfiguration sec = config.SecurityConfiguration;
            Assert.That(sec, Is.Not.Null);
            Assert.That(sec.IsDeprecatedConfiguration, Is.False);

            // Modern format: 2 application certificates
            Assert.That(sec.ApplicationCertificates, Has.Count.EqualTo(2));
            Assert.That(
                sec.ApplicationCertificates[0].SubjectName,
                Is.EqualTo("CN=Modern RSA, O=Test"));
            Assert.That(
                sec.ApplicationCertificates[1].SubjectName,
                Is.EqualTo("CN=Modern ECC, O=Test"));

            // All 6 trust list stores populated
            Assert.That(sec.TrustedIssuerCertificates, Is.Not.Null);
            Assert.That(
                sec.TrustedIssuerCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/issuers"));

            Assert.That(sec.TrustedPeerCertificates, Is.Not.Null);
            Assert.That(
                sec.TrustedPeerCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/trusted"));

            // TrustedCertificates in TrustedPeerCertificates
            Assert.That(
                sec.TrustedPeerCertificates.TrustedCertificates,
                Has.Count.EqualTo(1));
            Assert.That(
                sec.TrustedPeerCertificates.TrustedCertificates[0].SubjectName,
                Is.EqualTo("CN=Trusted Peer, O=Test"));

            Assert.That(sec.UserIssuerCertificates, Is.Not.Null);
            Assert.That(
                sec.UserIssuerCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/user-issuers"));

            Assert.That(sec.TrustedUserCertificates, Is.Not.Null);
            Assert.That(
                sec.TrustedUserCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/user-trusted"));

            Assert.That(sec.HttpsIssuerCertificates, Is.Not.Null);
            Assert.That(
                sec.HttpsIssuerCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/https-issuers"));

            Assert.That(sec.TrustedHttpsCertificates, Is.Not.Null);
            Assert.That(
                sec.TrustedHttpsCertificates.StorePath,
                Is.EqualTo("%LocalApplicationData%/OPC/https-trusted"));

            // Boolean and numeric settings
            Assert.That(sec.NonceLength, Is.EqualTo(32));
            Assert.That(sec.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(sec.RejectSHA1SignedCertificates, Is.True);
            Assert.That(sec.RejectUnknownRevocationStatus, Is.True);
            Assert.That(sec.MinimumCertificateKeySize, Is.EqualTo(2048));
            Assert.That(sec.SendCertificateChain, Is.True);
            Assert.That(sec.AddAppCertToTrustedStore, Is.False);
            Assert.That(sec.SuppressNonceValidationErrors, Is.False);

            // Round-trip
            string xml = EncodeToXml(config);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            ApplicationConfiguration rt = DecodeFromString<ApplicationConfiguration>(xml);
            SecurityConfiguration rtSec = rt.SecurityConfiguration;
            Assert.That(rtSec.IsDeprecatedConfiguration, Is.False);
            Assert.That(rtSec.ApplicationCertificates, Has.Count.EqualTo(2));
            Assert.That(
                rtSec.ApplicationCertificates[0].SubjectName,
                Is.EqualTo(sec.ApplicationCertificates[0].SubjectName));
            Assert.That(
                rtSec.ApplicationCertificates[1].SubjectName,
                Is.EqualTo(sec.ApplicationCertificates[1].SubjectName));
            Assert.That(
                rtSec.TrustedPeerCertificates.TrustedCertificates,
                Has.Count.EqualTo(1));
            Assert.That(
                rtSec.TrustedPeerCertificates.TrustedCertificates[0].SubjectName,
                Is.EqualTo("CN=Trusted Peer, O=Test"));
            Assert.That(
                rtSec.UserIssuerCertificates.StorePath,
                Is.EqualTo(sec.UserIssuerCertificates.StorePath));
            Assert.That(
                rtSec.TrustedUserCertificates.StorePath,
                Is.EqualTo(sec.TrustedUserCertificates.StorePath));
            Assert.That(
                rtSec.HttpsIssuerCertificates.StorePath,
                Is.EqualTo(sec.HttpsIssuerCertificates.StorePath));
            Assert.That(
                rtSec.TrustedHttpsCertificates.StorePath,
                Is.EqualTo(sec.TrustedHttpsCertificates.StorePath));
            Assert.That(
                rtSec.RejectUnknownRevocationStatus,
                Is.EqualTo(sec.RejectUnknownRevocationStatus));
            Assert.That(
                rtSec.SuppressNonceValidationErrors,
                Is.EqualTo(sec.SuppressNonceValidationErrors));
        }

        [Test]
        public void FullServerConfigurationRoundTrip()
        {
            ApplicationConfiguration config = DecodeFromFile<ApplicationConfiguration>(
                "test-server-full.xml");

            Assert.That(config.ApplicationName, Is.EqualTo("Full Server Test"));

            ServerConfiguration srv = config.ServerConfiguration;
            Assert.That(srv, Is.Not.Null);

            // BaseAddresses
            Assert.That(srv.BaseAddresses, Has.Count.EqualTo(2));
            Assert.That(
                srv.BaseAddresses[0],
                Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(
                srv.BaseAddresses[1],
                Is.EqualTo("https://localhost:4843"));

            // AlternateBaseAddresses
            Assert.That(srv.AlternateBaseAddresses, Has.Count.EqualTo(1));
            Assert.That(
                srv.AlternateBaseAddresses[0],
                Is.EqualTo("opc.tcp://myhost:4840"));

            // SecurityPolicies
            Assert.That(srv.SecurityPolicies, Has.Count.EqualTo(2));
            Assert.That(
                srv.SecurityPolicies[0].SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(
                srv.SecurityPolicies[0].SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(
                srv.SecurityPolicies[1].SecurityMode,
                Is.EqualTo(MessageSecurityMode.Sign));
            Assert.That(
                srv.SecurityPolicies[1].SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Aes128_Sha256_RsaOaep));

            // Thread counts
            Assert.That(srv.MinRequestThreadCount, Is.EqualTo(5));
            Assert.That(srv.MaxRequestThreadCount, Is.EqualTo(50));
            Assert.That(srv.MaxQueuedRequestCount, Is.EqualTo(500));

            // UserTokenPolicies
            Assert.That(srv.UserTokenPolicies, Has.Count.EqualTo(2));
            Assert.That(
                srv.UserTokenPolicies[0].TokenType,
                Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(
                srv.UserTokenPolicies[1].TokenType,
                Is.EqualTo(UserTokenType.UserName));

            // Session and channel limits
            Assert.That(srv.DiagnosticsEnabled, Is.True);
            Assert.That(srv.MaxSessionCount, Is.EqualTo(200));
            Assert.That(srv.MaxChannelCount, Is.EqualTo(500));
            Assert.That(srv.MinSessionTimeout, Is.EqualTo(5000));
            Assert.That(srv.MaxSessionTimeout, Is.EqualTo(1800000));

            // Continuation points
            Assert.That(srv.MaxBrowseContinuationPoints, Is.EqualTo(20));
            Assert.That(srv.MaxQueryContinuationPoints, Is.EqualTo(15));
            Assert.That(srv.MaxHistoryContinuationPoints, Is.EqualTo(50));

            // Publishing
            Assert.That(srv.MaxRequestAge, Is.EqualTo(300000));
            Assert.That(srv.MinPublishingInterval, Is.EqualTo(50));
            Assert.That(srv.MaxPublishingInterval, Is.EqualTo(1800000));
            Assert.That(srv.PublishingResolution, Is.EqualTo(50));
            Assert.That(srv.MaxSubscriptionLifetime, Is.EqualTo(1800000));
            Assert.That(srv.MinSubscriptionLifetime, Is.EqualTo(5000));
            Assert.That(srv.MaxMessageQueueSize, Is.EqualTo(20));
            Assert.That(srv.MaxNotificationQueueSize, Is.EqualTo(200));
            Assert.That(srv.MaxNotificationsPerPublish, Is.EqualTo(500));
            Assert.That(srv.MinMetadataSamplingInterval, Is.EqualTo(2000));

            // SamplingRateGroups
            Assert.That(srv.AvailableSamplingRates, Has.Count.EqualTo(2));
            Assert.That(srv.AvailableSamplingRates[0].Start, Is.EqualTo(5));
            Assert.That(srv.AvailableSamplingRates[0].Increment, Is.EqualTo(5));
            Assert.That(srv.AvailableSamplingRates[0].Count, Is.EqualTo(20));
            Assert.That(srv.AvailableSamplingRates[1].Start, Is.EqualTo(100));
            Assert.That(srv.AvailableSamplingRates[1].Increment, Is.EqualTo(100));
            Assert.That(srv.AvailableSamplingRates[1].Count, Is.EqualTo(4));

            // Registration and misc counts
            Assert.That(srv.MaxRegistrationInterval, Is.EqualTo(60000));
            Assert.That(srv.MaxPublishRequestCount, Is.EqualTo(50));
            Assert.That(srv.MaxSubscriptionCount, Is.EqualTo(200));
            Assert.That(srv.MaxEventQueueSize, Is.EqualTo(5000));

            // Server profile, capabilities, key formats
            Assert.That(srv.ServerProfileArray, Has.Count.EqualTo(2));
            Assert.That(
                srv.ServerProfileArray[0],
                Does.Contain("StandardUA2017"));
            Assert.That(srv.ServerCapabilities, Has.Count.EqualTo(2));
            Assert.That(srv.ServerCapabilities[0], Is.EqualTo("DA"));
            Assert.That(srv.ServerCapabilities[1], Is.EqualTo("HD"));
            Assert.That(srv.SupportedPrivateKeyFormats, Has.Count.EqualTo(2));
            Assert.That(srv.SupportedPrivateKeyFormats[0], Is.EqualTo("PFX"));
            Assert.That(srv.SupportedPrivateKeyFormats[1], Is.EqualTo("PEM"));

            // Misc scalars
            Assert.That(srv.ShutdownDelay, Is.EqualTo(10));
            Assert.That(srv.MaxTrustListSize, Is.EqualTo(65536));
            Assert.That(srv.MultiCastDnsEnabled, Is.True);

            // OperationLimits
            OperationLimits ol = srv.OperationLimits;
            Assert.That(ol, Is.Not.Null);
            Assert.That(ol.MaxNodesPerRead, Is.EqualTo(1000));
            Assert.That(ol.MaxNodesPerHistoryReadData, Is.EqualTo(500));
            Assert.That(ol.MaxNodesPerHistoryReadEvents, Is.EqualTo(500));
            Assert.That(ol.MaxNodesPerWrite, Is.EqualTo(1000));
            Assert.That(ol.MaxNodesPerHistoryUpdateData, Is.EqualTo(200));
            Assert.That(ol.MaxNodesPerHistoryUpdateEvents, Is.EqualTo(200));
            Assert.That(ol.MaxNodesPerMethodCall, Is.EqualTo(100));
            Assert.That(ol.MaxNodesPerBrowse, Is.EqualTo(2000));
            Assert.That(ol.MaxNodesPerRegisterNodes, Is.EqualTo(1500));
            Assert.That(
                ol.MaxNodesPerTranslateBrowsePathsToNodeIds,
                Is.EqualTo(1500));
            Assert.That(ol.MaxNodesPerNodeManagement, Is.EqualTo(500));
            Assert.That(ol.MaxMonitoredItemsPerCall, Is.EqualTo(3000));

            // Auditing and HTTPS
            Assert.That(srv.AuditingEnabled, Is.True);
            Assert.That(srv.HttpsMutualTls, Is.True);

            // Durable subscriptions
            Assert.That(srv.DurableSubscriptionsEnabled, Is.True);
            Assert.That(
                srv.MaxDurableNotificationQueueSize,
                Is.EqualTo(100000));
            Assert.That(srv.MaxDurableEventQueueSize, Is.EqualTo(50000));
            Assert.That(
                srv.MaxDurableSubscriptionLifetimeInHours,
                Is.EqualTo(24));

            // ReverseConnect
            ReverseConnectServerConfiguration rc = srv.ReverseConnect;
            Assert.That(rc, Is.Not.Null);
            Assert.That(rc.Clients, Has.Count.EqualTo(2));
            Assert.That(
                rc.Clients[0].EndpointUrl,
                Is.EqualTo("opc.tcp://client1:65300"));
            Assert.That(rc.Clients[0].Timeout, Is.EqualTo(5000));
            Assert.That(rc.Clients[0].MaxSessionCount, Is.EqualTo(10));
            Assert.That(rc.Clients[0].Enabled, Is.True);
            Assert.That(
                rc.Clients[1].EndpointUrl,
                Is.EqualTo("opc.tcp://client2:65301"));
            Assert.That(rc.Clients[1].Timeout, Is.EqualTo(10000));
            Assert.That(rc.Clients[1].MaxSessionCount, Is.EqualTo(5));
            Assert.That(rc.Clients[1].Enabled, Is.False);
            Assert.That(rc.ConnectInterval, Is.EqualTo(20000));
            Assert.That(rc.ConnectTimeout, Is.EqualTo(45000));
            Assert.That(rc.RejectTimeout, Is.EqualTo(90000));

            // Encode to XML and round-trip
            string xml = EncodeToXml(config);
            Assert.That(xml, Is.Not.Null.And.Not.Empty);

            ApplicationConfiguration rt = DecodeFromString<ApplicationConfiguration>(xml);
            ServerConfiguration rtSrv = rt.ServerConfiguration;
            Assert.That(rtSrv, Is.Not.Null);

            // Verify key fields survived the round-trip
            Assert.That(rtSrv.BaseAddresses, Has.Count.EqualTo(2));
            Assert.That(
                rtSrv.BaseAddresses[0],
                Is.EqualTo(srv.BaseAddresses[0]));
            Assert.That(
                rtSrv.BaseAddresses[1],
                Is.EqualTo(srv.BaseAddresses[1]));
            Assert.That(
                rtSrv.AlternateBaseAddresses,
                Has.Count.EqualTo(1));
            Assert.That(rtSrv.SecurityPolicies, Has.Count.EqualTo(2));
            Assert.That(
                rtSrv.SecurityPolicies[0].SecurityMode,
                Is.EqualTo(srv.SecurityPolicies[0].SecurityMode));
            Assert.That(
                rtSrv.MinRequestThreadCount,
                Is.EqualTo(srv.MinRequestThreadCount));
            Assert.That(
                rtSrv.MaxRequestThreadCount,
                Is.EqualTo(srv.MaxRequestThreadCount));
            Assert.That(
                rtSrv.MaxQueuedRequestCount,
                Is.EqualTo(srv.MaxQueuedRequestCount));
            Assert.That(rtSrv.UserTokenPolicies, Has.Count.EqualTo(2));
            Assert.That(
                rtSrv.UserTokenPolicies[0].TokenType,
                Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(rtSrv.DiagnosticsEnabled, Is.True);
            Assert.That(
                rtSrv.MaxSessionCount,
                Is.EqualTo(srv.MaxSessionCount));
            Assert.That(
                rtSrv.MaxChannelCount,
                Is.EqualTo(srv.MaxChannelCount));
            Assert.That(
                rtSrv.MinSessionTimeout,
                Is.EqualTo(srv.MinSessionTimeout));
            Assert.That(
                rtSrv.MaxSessionTimeout,
                Is.EqualTo(srv.MaxSessionTimeout));
            Assert.That(
                rtSrv.MaxBrowseContinuationPoints,
                Is.EqualTo(srv.MaxBrowseContinuationPoints));
            Assert.That(
                rtSrv.MaxQueryContinuationPoints,
                Is.EqualTo(srv.MaxQueryContinuationPoints));
            Assert.That(
                rtSrv.MaxHistoryContinuationPoints,
                Is.EqualTo(srv.MaxHistoryContinuationPoints));
            Assert.That(
                rtSrv.MaxRequestAge,
                Is.EqualTo(srv.MaxRequestAge));
            Assert.That(
                rtSrv.MinPublishingInterval,
                Is.EqualTo(srv.MinPublishingInterval));
            Assert.That(
                rtSrv.MaxPublishingInterval,
                Is.EqualTo(srv.MaxPublishingInterval));
            Assert.That(
                rtSrv.PublishingResolution,
                Is.EqualTo(srv.PublishingResolution));
            Assert.That(
                rtSrv.MaxSubscriptionLifetime,
                Is.EqualTo(srv.MaxSubscriptionLifetime));
            Assert.That(
                rtSrv.MinSubscriptionLifetime,
                Is.EqualTo(srv.MinSubscriptionLifetime));
            Assert.That(
                rtSrv.MaxMessageQueueSize,
                Is.EqualTo(srv.MaxMessageQueueSize));
            Assert.That(
                rtSrv.MaxNotificationQueueSize,
                Is.EqualTo(srv.MaxNotificationQueueSize));
            Assert.That(
                rtSrv.MaxNotificationsPerPublish,
                Is.EqualTo(srv.MaxNotificationsPerPublish));
            Assert.That(
                rtSrv.MinMetadataSamplingInterval,
                Is.EqualTo(srv.MinMetadataSamplingInterval));
            Assert.That(
                rtSrv.AvailableSamplingRates,
                Has.Count.EqualTo(2));
            Assert.That(
                rtSrv.AvailableSamplingRates[0].Start,
                Is.EqualTo(srv.AvailableSamplingRates[0].Start));
            Assert.That(
                rtSrv.MaxRegistrationInterval,
                Is.EqualTo(srv.MaxRegistrationInterval));
            Assert.That(
                rtSrv.MaxPublishRequestCount,
                Is.EqualTo(srv.MaxPublishRequestCount));
            Assert.That(
                rtSrv.MaxSubscriptionCount,
                Is.EqualTo(srv.MaxSubscriptionCount));
            Assert.That(
                rtSrv.MaxEventQueueSize,
                Is.EqualTo(srv.MaxEventQueueSize));
            Assert.That(
                rtSrv.ServerProfileArray,
                Has.Count.EqualTo(2));
            Assert.That(
                rtSrv.ShutdownDelay,
                Is.EqualTo(srv.ShutdownDelay));
            Assert.That(
                rtSrv.ServerCapabilities,
                Has.Count.EqualTo(2));
            Assert.That(
                rtSrv.SupportedPrivateKeyFormats,
                Has.Count.EqualTo(2));
            Assert.That(
                rtSrv.MaxTrustListSize,
                Is.EqualTo(srv.MaxTrustListSize));
            Assert.That(
                rtSrv.MultiCastDnsEnabled,
                Is.EqualTo(srv.MultiCastDnsEnabled));

            // OperationLimits round-trip
            Assert.That(rtSrv.OperationLimits, Is.Not.Null);
            Assert.That(
                rtSrv.OperationLimits.MaxNodesPerRead,
                Is.EqualTo(ol.MaxNodesPerRead));
            Assert.That(
                rtSrv.OperationLimits.MaxNodesPerBrowse,
                Is.EqualTo(ol.MaxNodesPerBrowse));
            Assert.That(
                rtSrv.OperationLimits.MaxMonitoredItemsPerCall,
                Is.EqualTo(ol.MaxMonitoredItemsPerCall));

            // Auditing and durable round-trip
            Assert.That(
                rtSrv.AuditingEnabled,
                Is.EqualTo(srv.AuditingEnabled));
            Assert.That(
                rtSrv.HttpsMutualTls,
                Is.EqualTo(srv.HttpsMutualTls));
            Assert.That(
                rtSrv.DurableSubscriptionsEnabled,
                Is.EqualTo(srv.DurableSubscriptionsEnabled));
            Assert.That(
                rtSrv.MaxDurableNotificationQueueSize,
                Is.EqualTo(srv.MaxDurableNotificationQueueSize));
            Assert.That(
                rtSrv.MaxDurableEventQueueSize,
                Is.EqualTo(srv.MaxDurableEventQueueSize));
            Assert.That(
                rtSrv.MaxDurableSubscriptionLifetimeInHours,
                Is.EqualTo(srv.MaxDurableSubscriptionLifetimeInHours));

            // ReverseConnect round-trip
            Assert.That(rtSrv.ReverseConnect, Is.Not.Null);
            Assert.That(rtSrv.ReverseConnect.Clients, Has.Count.EqualTo(2));
            Assert.That(
                rtSrv.ReverseConnect.Clients[0].EndpointUrl,
                Is.EqualTo(rc.Clients[0].EndpointUrl));
            Assert.That(
                rtSrv.ReverseConnect.Clients[1].Enabled,
                Is.EqualTo(rc.Clients[1].Enabled));
            Assert.That(
                rtSrv.ReverseConnect.ConnectInterval,
                Is.EqualTo(rc.ConnectInterval));
            Assert.That(
                rtSrv.ReverseConnect.ConnectTimeout,
                Is.EqualTo(rc.ConnectTimeout));
            Assert.That(
                rtSrv.ReverseConnect.RejectTimeout,
                Is.EqualTo(rc.RejectTimeout));
        }

        private static IServiceMessageContext CreateMessageContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable scope = AmbientMessageContext.SetScopedContext(telemetry);
            return AmbientMessageContext.CurrentContext
                ?? ServiceMessageContext.CreateEmpty(telemetry);
        }

        private static T DecodeFromFile<T>(string fileName) where T : IEncodeable, new()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            IServiceMessageContext ctx = CreateMessageContext();
            var parser = new XmlParser(typeof(T), stream, ctx);
            var result = new T();
            result.Decode(parser);
            return result;
        }

        private static T DecodeFromString<T>(string xml) where T : IEncodeable, new()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            IServiceMessageContext ctx = CreateMessageContext();
            var parser = new XmlParser(typeof(T), stream, ctx);
            var result = new T();
            result.Decode(parser);
            return result;
        }

        private static string EncodeToXml<T>(T value) where T : IEncodeable
        {
            IServiceMessageContext ctx = CreateMessageContext();
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            using (var writer = XmlWriter.Create(stream, settings))
            {
                var encoder = new XmlEncoder(typeof(T), writer, ctx);
                value.Encode(encoder);
                encoder.Close();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
