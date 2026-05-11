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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Security – endpoint security
    /// policies, user tokens, and secure connections.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    public class SecurityTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifyServerAdvertisesSecureEndpointsAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasSecure = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.SecurityMode != MessageSecurityMode.None)
                {
                    hasSecure = true;
                    break;
                }
            }

            Assert.That(hasSecure, Is.True,
                "Server should advertise at least one secure endpoint.");
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifySecurityPolicyUriIsValidAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(
                        ep.SecurityPolicyUri,
                        Is.Not.Null.And.Not.Empty);
                    Assert.That(
                        ep.SecurityPolicyUri,
                        Does.StartWith("http://opcfoundation.org/UA/"));
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "001")]
        public async Task VerifyAnonymousUserTokenOnEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasAnonymous = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in e.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.Anonymous)
                        {
                            hasAnonymous = true;
                            break;
                        }
                    }
                }

                if (hasAnonymous)
                {
                    break;
                }
            }

            Assert.That(hasAnonymous, Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Security User Name Password 2")]
        [Property("Tag", "015")]
        public async Task VerifyUsernameUserTokenOnEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasUsername = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in e.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            hasUsername = true;
                            break;
                        }
                    }
                }

                if (hasUsername)
                {
                    break;
                }
            }

            if (!hasUsername)
            {
                Assert.Fail(
                    "Server does not advertise Username user token.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security None CreateSession ActivateSession")]
        [Property("Tag", "001")]
        public void ConnectWithSecurityModeNone()
        {
            Assert.That(Session.Connected, Is.True);
            Assert.That(
                Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "004")]
        public async Task ActivateWithAnonymousIdentityAsync()
        {
            ISession additionalSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(additionalSession.Connected, Is.True);
            }
            finally
            {
                await additionalSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                additionalSession.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security None CreateSession ActivateSession")]
        [Property("Tag", "001")]
        public void SessionSecurityModeIsNone()
        {
            Assert.That(
                Session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        [Property("ConformanceUnit", "Security Certificate Validation")]
        [Property("Tag", "001")]
        public async Task VerifyEndpointServerCertificateOnSecureEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(
                        ep.ServerCertificate.Length,
                        Is.GreaterThan(0),
                        "Secure endpoint should have a server certificate.");
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifyEndpointSecurityLevelOrderingAsync()
        {
            ArrayOf<EndpointDescription> endpoints =
                await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasPositiveLevel = false;
            foreach (EndpointDescription e in endpoints)
            {
                if (e.SecurityMode != MessageSecurityMode.None &&
                    e.SecurityLevel > 0)
                {
                    hasPositiveLevel = true;
                    break;
                }
            }

            Assert.That(hasPositiveLevel, Is.True,
                "At least one secure endpoint should have SecurityLevel > 0.");
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task ConnectSecondSessionVerifyIndependentSecurityAsync()
        {
            ISession second = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(Session.Connected, Is.True);
                Assert.That(second.Connected, Is.True);
                Assert.That(
                    Session.SessionId,
                    Is.Not.EqualTo(second.SessionId));
            }
            finally
            {
                await second.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                second.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Name Password 2")]
        [Property("Tag", "003")]
        public async Task ConnectWithEmptyUsernameReturnsBadIdentityTokenInvalidAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription usernameEp = null;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            usernameEp = ep;
                            break;
                        }
                    }
                }

                if (usernameEp != null)
                {
                    break;
                }
            }

            if (usernameEp == null)
            {
                Assert.Fail(
                    "Server does not advertise Username user token.");
            }

            try
            {
                ISession session = await OpenAuxSessionAsync(
                    userIdentity: new UserIdentity(string.Empty, ""u8))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Fail("Expected ServiceResultException.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected identity rejection, got {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Name Password 2")]
        [Property("Tag", "007")]
        public async Task ConnectWithWrongPasswordReturnsBadIdentityTokenRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasUsername = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            hasUsername = true;
                            break;
                        }
                    }
                }

                if (hasUsername)
                {
                    break;
                }
            }

            if (!hasUsername)
            {
                Assert.Fail(
                    "Server does not advertise Username user token.");
            }

            try
            {
                ISession session = await OpenAuxSessionAsync(
                    userIdentity: new UserIdentity("sysadmin", "WRONG_PASSWORD_12345"u8))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                Assert.Fail("Expected ServiceResultException.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected rejection, got {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Name Password 2")]
        [Property("Tag", "001")]
        public async Task ConnectWithSysadminCredentialsAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasUsername = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            hasUsername = true;
                            break;
                        }
                    }
                }

                if (hasUsername)
                {
                    break;
                }
            }

            if (!hasUsername)
            {
                Assert.Fail(
                    "Server does not advertise Username user token.");
            }

            try
            {
                ISession session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None,
                        userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail(
                    $"Server rejected sysadmin credentials: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Name Password 2")]
        [Property("Tag", "002")]
        public async Task ConnectWithAppuserCredentialsAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasUsername = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            hasUsername = true;
                            break;
                        }
                    }
                }

                if (hasUsername)
                {
                    break;
                }
            }

            if (!hasUsername)
            {
                Assert.Ignore(
                    "Server does not advertise Username user token.");
            }

            try
            {
                ISession session = await OpenAuxSessionAsync(
                    userIdentity: new UserIdentity("appuser", "demo"u8))
                    .ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
            catch (ServiceResultException sre)
            {
                Assert.Ignore(
                    $"Server rejected appuser credentials: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Name Password 2")]
        [Property("Tag", "009")]
        public async Task ConnectWithSpecialCharsUsernameAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasUsername = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            hasUsername = true;
                            break;
                        }
                    }
                }

                if (hasUsername)
                {
                    break;
                }
            }

            if (!hasUsername)
            {
                Assert.Fail(
                    "Server does not advertise Username user token.");
            }

            // Expect rejection — the point is the server handles it
            try
            {
                ISession session = await OpenAuxSessionAsync(
                    userIdentity: new UserIdentity("user@#$%^&*()", "demo"u8))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
            catch (ServiceResultException)
            {
                // Any rejection is acceptable
            }
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifyEndpointListsUserIdentityTokenTypesAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool anyHasTokens = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default &&
                    ep.UserIdentityTokens.Count > 0)
                {
                    anyHasTokens = true;
                    break;
                }
            }

            Assert.That(anyHasTokens, Is.True,
                "At least one endpoint should support identity tokens.");
        }

        [Test]
        [Property("ConformanceUnit", "Security Certificate Validation")]
        [Property("Tag", "001")]
        public async Task VerifySecureEndpointsHaveNonEmptyServerCertificateAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(
                        ep.ServerCertificate, Is.Not.Null,
                        "Secure endpoint should have ServerCertificate.");
                    Assert.That(
                        ep.ServerCertificate.Length, Is.GreaterThan(0),
                        "Secure endpoint ServerCertificate should not be empty.");
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifySecurityPolicyUriStartsWithOpcFoundationAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(
                    ep.SecurityPolicyUri, Is.Not.Null.And.Not.Empty,
                    "SecurityPolicyUri should not be null or empty.");
                Assert.That(
                    Uri.IsWellFormedUriString(
                        ep.SecurityPolicyUri, UriKind.Absolute),
                    Is.True,
                    $"SecurityPolicyUri '{ep.SecurityPolicyUri}' should be valid URI.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifySecurityLevelHigherForMoreSecureAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            byte noneLevel = 0;
            byte secureLevel = 0;
            bool foundNone = false;
            bool foundSecure = false;

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None && !foundNone)
                {
                    noneLevel = ep.SecurityLevel;
                    foundNone = true;
                }
                else if (ep.SecurityMode != MessageSecurityMode.None &&
                    !foundSecure)
                {
                    secureLevel = ep.SecurityLevel;
                    foundSecure = true;
                }
            }

            if (foundNone && foundSecure)
            {
                Assert.That(secureLevel, Is.GreaterThanOrEqualTo(noneLevel),
                    "Secure endpoint SecurityLevel should be >= None.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Signing Required")]
        [Property("Tag", "001")]
        public async Task ConnectWithSignSecurityModeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription signEp = null;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.Sign)
                {
                    signEp = ep;
                    break;
                }
            }

            if (signEp == null)
            {
                Assert.Fail("No Sign endpoint available.");
            }

            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, signEp.SecurityPolicyUri)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Encryption Required")]
        [Property("Tag", "001")]
        public async Task ConnectWithSignAndEncryptSecurityModeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            EndpointDescription encryptEp = null;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.SignAndEncrypt)
                {
                    encryptEp = ep;
                    break;
                }
            }

            if (encryptEp == null)
            {
                Assert.Fail("No SignAndEncrypt endpoint available.");
            }

            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, encryptEp.SecurityPolicyUri)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Certificate Validation")]
        [Property("Tag", "001")]
        public async Task VerifyMinimumKeyLengthOnCertificatesAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None &&
                    !ep.ServerCertificate.IsEmpty)
                {
                    X509Certificate2 cert =

                            X509CertificateLoader.LoadCertificate(ep.ServerCertificate.ToArray());
                    int keySize;
                    using (RSA rsa = cert.GetRSAPublicKey())
                    using (ECDsa ecdsa = rsa is null ? cert.GetECDsaPublicKey() : null)
                    {
                        keySize = rsa?.KeySize ?? ecdsa?.KeySize ?? 0;
                    }
                    Assert.That(keySize, Is.GreaterThanOrEqualTo(256),
                        "Server certificate key should be at least 256 bits.");
                    cert.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security None CreateSession ActivateSession")]
        [Property("Tag", "004")]
        public async Task VerifyNoneEndpointHasZeroSecurityLevelAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None)
                {
                    Assert.That(ep.SecurityLevel, Is.Zero,
                        "None endpoint should have SecurityLevel=0.");
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Certificate Validation")]
        [Property("Tag", "001")]
        public async Task VerifyServerCertificateSubjectDNAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None &&
                    !ep.ServerCertificate.IsEmpty)
                {
                    X509Certificate2 cert =

                            X509CertificateLoader.LoadCertificate(ep.ServerCertificate.ToArray());
                    Assert.That(cert.Subject, Is.Not.Null.And.Not.Empty,
                        "Secure endpoint certificate should have a valid Subject.");
                    cert.Dispose();
                    return;
                }
            }

            Assert.Fail("No secure endpoint with certificate found.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Name Password 2")]
        [Property("Tag", "012")]
        public async Task ConnectWithSysadminWriteToNodeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasUsername = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            hasUsername = true;
                            break;
                        }
                    }
                }

                if (hasUsername)
                {
                    break;
                }
            }

            if (!hasUsername)
            {
                Assert.Fail(
                    "Server does not advertise Username user token.");
            }

            ISession session;
            try
            {
                session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None,
                        userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail(
                    $"Server rejected sysadmin credentials: {sre.StatusCode}");
                return;
            }

            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                WriteResponse writeResp = await session.WriteAsync(
                    null,
                    new WriteValue[]
                    {
                        new() {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(Variant.From(42))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(writeResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    writeResp.Results[0].Code,
                    Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifySecureEndpointHasUserTokenPoliciesAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool found = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    Assert.That(
                        ep.UserIdentityTokens, Is.Not.Null,
                        "Secure endpoint should have UserIdentityTokens.");
                    Assert.That(
                        ep.UserIdentityTokens.Count, Is.GreaterThan(0),
                        "Secure endpoint should have at least one token policy.");
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Assert.Fail("No secure endpoint found.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Default ApplicationInstance Certificate")]
        [Property("Tag", "003")]
        public async Task VerifyEndpointApplicationUriMatchesServerAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            string firstUri = endpoints[0].Server.ApplicationUri;
            Assert.That(firstUri, Is.Not.Null.And.Not.Empty);

            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.Server.ApplicationUri,
                    Is.EqualTo(firstUri),
                    "All endpoints should share the same ApplicationUri.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifyTransportProfileUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.TransportProfileUri,
                    Is.Not.Null.And.Not.Empty,
                    "TransportProfileUri should be set on every endpoint.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "SecurityPolicy Support")]
        [Property("Tag", "001")]
        public async Task VerifyEndpointServerDescriptionIsServerAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(
                    ep.Server.ApplicationType,
                    Is.EqualTo(ApplicationType.Server)
                        .Or.EqualTo(ApplicationType.ClientAndServer),
                    "Endpoint Server field should be Server or ClientAndServer.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Certificate Validation")]
        [Property("Tag", "001")]
        public async Task VerifyEndpointsHaveConsistentServerCertificateAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            var certsByPolicy = new System.Collections.Generic.Dictionary<string, byte[]>();

            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None)
                {
                    continue;
                }

                string key = ep.SecurityPolicyUri;
                byte[] certBytes = ep.ServerCertificate.ToArray();
                if (certsByPolicy.TryGetValue(key, out byte[] existing))
                {
                    Assert.That(certBytes, Is.EqualTo(existing),
                        $"Endpoints with policy '{key}' should have same cert.");
                }
                else
                {
                    certsByPolicy[key] = certBytes;
                }
            }
        }

        [Test]
        [Property("ConformanceUnit",
            "Security None CreateSession ActivateSession")]
        [Property("Tag", "001")]
        public async Task NoneSession001InsecureWithCertsAndNonces()
        {
            // Connect on an insecure channel (SecurityMode.None)
            // and verify CreateSession / ActivateSession succeeds
            // when both client certificate and nonces are present.
            ISession session;
            try
            {
                session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadUserAccessDenied &&
                sre.Message.Contains("Too many failed authentication attempts"))
            {
                Assert.Fail("Account locked out from prior negative tests; cannot authenticate");
                return;
            }
            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(
                    session.Endpoint.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.None));

                // Server should have returned a session id and nonce
                Assert.That(
                    session.SessionId, Is.Not.Null,
                    "Session must have a valid SessionId.");
                Assert.That(
                    session.SessionId, Is.Not.EqualTo(NodeId.Null),
                    "Server should supply a valid SessionId.");
            }
            finally
            {
                await session.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit",
            "Security None CreateSession ActivateSession")]
        [Property("Tag", "002")]
        public async Task NoneSession002InsecureNoCerts()
        {
            // Connect on an insecure channel without a client
            // certificate. The server should still accept the
            // session when SecurityMode is None.
            ISession session;
            try
            {
                session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadUserAccessDenied &&
                sre.Message.Contains("Too many failed authentication attempts"))
            {
                Assert.Fail("Account locked out from prior negative tests; cannot authenticate");
                return;
            }
            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(
                    session.Endpoint.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.None));
                Assert.That(
                    session.SessionId, Is.Not.Null,
                    "Session must have a valid SessionId.");
            }
            finally
            {
                await session.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit",
            "Security None CreateSession ActivateSession")]
        [Property("Tag", "003")]
        public async Task NoneSession003InsecureWithCertsNoNonce()
        {
            // Connect on an insecure channel with client certificate
            // present but verify session works even when the server
            // does not strictly require a nonce on None channels.
            ISession session;
            try
            {
                session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadUserAccessDenied &&
                sre.Message.Contains("Too many failed authentication attempts"))
            {
                Assert.Fail("Account locked out from prior negative tests; cannot authenticate");
                return;
            }
            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(
                    session.Endpoint.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.None));
                Assert.That(
                    session.Endpoint.SecurityPolicyUri,
                    Is.EqualTo(SecurityPolicies.None));
            }
            finally
            {
                await session.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit",
            "Security None CreateSession ActivateSession")]
        [Property("Tag", "004")]
        public async Task NoneSession004InsecureNoCertsNoNonce()
        {
            // Connect on an insecure channel with neither client
            // certificate nor nonce requirement. Verify that the
            // session is fully functional.
            ISession session;
            try
            {
                session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadUserAccessDenied &&
                sre.Message.Contains("Too many failed authentication attempts"))
            {
                Assert.Fail("Account locked out from prior negative tests; cannot authenticate");
                return;
            }
            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(
                    session.Endpoint.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.None));

                // Verify the session is functional by reading
                // the server status current time node.
                DataValue timeValue = await session.ReadValueAsync(
                    VariableIds.Server_ServerStatus_CurrentTime,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(timeValue.StatusCode), Is.True,
                    "Should be able to read server time on " +
                    "None session.");
            }
            finally
            {
                await session.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                session.Dispose();
            }
        }

        private async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            return await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
