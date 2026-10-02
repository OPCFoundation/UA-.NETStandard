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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for CreateSession parameter handling against Part 4 5.7.2
    /// and the session diagnostics of Part 5 12.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class CreateSessionSpecRegressionTests
    {
        /// <summary>
        /// Part 4 5.7.2.1: at the session cap the oldest Session that was never
        /// activated is closed, so abandoned Sessions cannot lock Clients out.
        /// </summary>
        [Test]
        public async Task OldestNonActivatedSessionIsClosedAtSessionCapAsync()
        {
            ServerFixture<StandardServer> fixture = await StartAsync(maxSessionCount: 2).ConfigureAwait(false);
            try
            {
                StandardServer server = fixture.Server;
                CreateSessionResponse first = await CreateAsync(server, "cap-1").ConfigureAwait(false);
                CreateSessionResponse second = await CreateAsync(server, "cap-2").ConfigureAwait(false);
                uint abortsBefore = ((ServerInternalData)server.CurrentInstance).ServerDiagnostics.SessionAbortCount;

                CreateSessionResponse third = await CreateAsync(server, "cap-3").ConfigureAwait(false);

                List<NodeId> ids = [.. server.CurrentInstance.SessionManager.GetSessions().Select(s => s.Id)];
                Assert.That(ids, Has.Count.EqualTo(2));
                Assert.That(ids, Does.Not.Contain(first.SessionId));
                Assert.That(ids, Does.Contain(second.SessionId));
                Assert.That(ids, Does.Contain(third.SessionId));
                // K4: the eviction is a termination by the server (OPC 10000-5 12.9).
                Assert.That(
                    ((ServerInternalData)server.CurrentInstance).ServerDiagnostics.SessionAbortCount,
                    Is.EqualTo(abortsBefore + 1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A cap filled with activated Sessions still rejects CreateSession with
        /// Bad_TooManySessions; activated Sessions are never evicted.
        /// </summary>
        [Test]
        public async Task ActivatedSessionsAtCapAreNotEvictedAsync()
        {
            ServerFixture<StandardServer> fixture = await StartAsync(maxSessionCount: 2).ConfigureAwait(false);
            try
            {
                StandardServer server = fixture.Server;
                await server.CreateAndActivateSessionAsync("activated-1").ConfigureAwait(false);
                await server.CreateAndActivateSessionAsync("activated-2").ConfigureAwait(false);

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await CreateAsync(server, "activated-3").ConfigureAwait(false));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManySessions));
                Assert.That(server.CurrentInstance.SessionManager.GetSessions(), Has.Count.EqualTo(2));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Part 4 5.7.2.2 defines no status code for a malformed endpointUrl: an
        /// empty, relative or garbage value must not fail CreateSession.
        /// </summary>
        [TestCase("")]
        [TestCase("myhost:4840/x")]
        [TestCase("/relative/path")]
        [TestCase("not a url")]
        public async Task MalformedEndpointUrlDoesNotFailCreateSessionAsync(string endpointUrl)
        {
            ServerFixture<StandardServer> fixture = await StartAsync().ConfigureAwait(false);
            try
            {
                CreateSessionResponse response = await CreateAsync(
                    fixture.Server, "endpoint-url", endpointUrl: endpointUrl).ConfigureAwait(false);
                Assert.That(response.SessionId.IsNull, Is.False);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Part 4 5.7.2.2: the Server shall ignore any serverUri. Part 5 12.11
        /// reports the requested value in SessionDiagnostics.ServerUri.
        /// </summary>
        [Test]
        public async Task ForeignServerUriIsIgnoredAndReportedInDiagnosticsAsync()
        {
            ServerFixture<StandardServer> fixture = await StartAsync().ConfigureAwait(false);
            try
            {
                const string foreignUri = "urn:gateway:underlying-server";
                CreateSessionResponse response = await CreateAsync(
                    fixture.Server, "server-uri", serverUri: foreignUri).ConfigureAwait(false);

                ISession session = fixture.Server.CurrentInstance.SessionManager.GetSession(
                    response.AuthenticationToken);
                Assert.That(session.ReadDiagnostics(d => d.ServerUri), Is.EqualTo(foreignUri));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A NaN requestedSessionTimeout is revised to a legal value instead of
        /// creating a Session that is expired from the start.
        /// </summary>
        [Test]
        public async Task NaNSessionTimeoutIsRevisedToTheMinimumAsync()
        {
            ServerFixture<StandardServer> fixture = await StartAsync().ConfigureAwait(false);
            try
            {
                CreateSessionResponse response = await CreateAsync(
                    fixture.Server, "nan-timeout", sessionTimeout: double.NaN).ConfigureAwait(false);

                Assert.That(double.IsNaN(response.RevisedSessionTimeout), Is.False);
                Assert.That(
                    response.RevisedSessionTimeout,
                    Is.EqualTo(fixture.Config.ServerConfiguration.MinSessionTimeout));
                ISession session = fixture.Server.CurrentInstance.SessionManager.GetSession(
                    response.AuthenticationToken);
                Assert.That(session.HasExpired, Is.False);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Part 5 12.11: SessionDiagnostics.MaxResponseMessageSize is mandatory and
        /// reports the maxResponseMessageSize of the CreateSession request.
        /// </summary>
        [Test]
        public async Task MaxResponseMessageSizeIsReportedInDiagnosticsAsync()
        {
            ServerFixture<StandardServer> fixture = await StartAsync().ConfigureAwait(false);
            try
            {
                CreateSessionResponse response = await CreateAsync(
                    fixture.Server, "max-response", maxResponseMessageSize: 65536).ConfigureAwait(false);

                ISession session = fixture.Server.CurrentInstance.SessionManager.GetSession(
                    response.AuthenticationToken);
                Assert.That(session.ReadDiagnostics(d => d.MaxResponseMessageSize), Is.EqualTo(65536u));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Part 5 12.12: the SessionSecurityDiagnostics encoding is "UA Binary",
        /// "XML" or "JSON".
        /// </summary>
        [TestCase(RequestEncoding.Binary, "UA Binary")]
        [TestCase(RequestEncoding.Xml, "XML")]
        [TestCase(RequestEncoding.Json, "JSON")]
        public void SecurityDiagnosticsEncodingUsesTheSpecifiedNames(RequestEncoding encoding, string expected)
        {
            Assert.That(Opc.Ua.Server.Session.GetEncodingName(encoding), Is.EqualTo(expected));
        }

        /// <summary>
        /// Part 5 12.9: a CreateSession rejected by request validation, before the
        /// service body runs, is still counted as a rejected session request.
        /// </summary>
        [Test]
        public async Task CreateSessionRejectedByValidationIsCountedAsync()
        {
            ServerFixture<StandardServer> fixture = await StartAsync().ConfigureAwait(false);
            try
            {
                StandardServer server = fixture.Server;
                Assert.CatchAsync<ServiceResultException>(async () =>
                    await server.CreateSessionAsync(
                        CreateNoneChannel(server, "invalid-header"), null, null, null, null, "invalid-header",
                        default, default, 60000, 0, RequestLifetime.None).ConfigureAwait(false));

                uint rejectedSessions = 0;
                uint rejectedRequests = 0;
                uint cumulated = 0;
                server.CurrentInstance.UpdateServerDiagnostics(diagnostics =>
                {
                    rejectedSessions = diagnostics.RejectedSessionCount;
                    rejectedRequests = diagnostics.RejectedRequestsCount;
                    cumulated = diagnostics.CumulatedSessionCount;
                });
                Assert.That(rejectedSessions, Is.EqualTo(1));
                Assert.That(rejectedRequests, Is.EqualTo(1));
                Assert.That(cumulated, Is.Zero);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Part 4 5.7.2.2: a Server-assigned session name is distinct per Session,
        /// so the SessionDiagnostics objects can be told apart.
        /// </summary>
        [Test]
        public async Task ServerAssignedSessionNamesAreDistinctAsync()
        {
            ServerFixture<StandardServer> fixture = await StartAsync().ConfigureAwait(false);
            try
            {
                CreateSessionResponse first = await CreateAsync(fixture.Server, null).ConfigureAwait(false);
                CreateSessionResponse second = await CreateAsync(fixture.Server, string.Empty).ConfigureAwait(false);

                ISessionManager sessions = fixture.Server.CurrentInstance.SessionManager;
                string firstName = sessions.GetSession(first.AuthenticationToken)
                    .ReadDiagnostics(d => d.SessionName);
                string secondName = sessions.GetSession(second.AuthenticationToken)
                    .ReadDiagnostics(d => d.SessionName);
                Assert.That(firstName, Is.Not.Empty);
                Assert.That(secondName, Is.Not.Empty);
                Assert.That(firstName, Is.Not.EqualTo(secondName));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An OnApplicationCertificateError override that accepts an untrusted client
        /// certificate keeps the certificate: the Session is created with it and the
        /// response carries a serverSignature (Part 4 6.1.8).
        /// </summary>
        [Test]
        public async Task AcceptedCertificateErrorKeepsTheClientCertificateAsync()
        {
            using Certificate untrusted = CreateClientCertificate("CN=Accepted Untrusted Client");
            (ServerFixture<AcceptingServer> fixture, string pkiRoot) =
                await StartAcceptingAsync().ConfigureAwait(false);
            try
            {
                AcceptingServer server = fixture.Server;
                CreateSessionResponse response = await CreateSecuredAsync(
                    server, untrusted, kApplicationUri).ConfigureAwait(false);

                Assert.That(server.ReportedErrors, Does.Contain((StatusCode)StatusCodes.BadCertificateUntrusted));
                Assert.That(response.ServerSignature, Is.Not.Null);
                Assert.That(response.ServerSignature.Signature.IsEmpty, Is.False);
                ISession session = server.CurrentInstance.SessionManager.GetSession(response.AuthenticationToken);
                Assert.That(session.ClientCertificate, Is.Not.Null);
                Assert.That(session.ClientCertificate.Thumbprint, Is.EqualTo(untrusted.Thumbprint));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                DeleteDirectory(pkiRoot);
            }
        }

        /// <summary>
        /// K21: a client certificate whose validation error an override accepted still
        /// signs the session, but it is not a trusted application identity: the
        /// session is not granted TrustedApplication (Part 3 4.9) or application-based
        /// role mappings (Part 18 4.4.4) from it.
        /// </summary>
        [Test]
        public async Task AcceptedCertificateErrorDoesNotGrantTrustedApplicationAsync()
        {
            using Certificate untrusted = CreateClientCertificate("CN=Accepted Untrusted Client Roles");
            (ServerFixture<AcceptingServer> fixture, string pkiRoot) =
                await StartAcceptingAsync().ConfigureAwait(false);
            try
            {
                AcceptingServer server = fixture.Server;
                CreateSessionResponse response = await CreateSecuredAsync(
                    server, untrusted, kApplicationUri).ConfigureAwait(false);
                var session = (Opc.Ua.Server.Session)server.CurrentInstance.SessionManager
                    .GetSession(response.AuthenticationToken);
                Assert.That(session.ClientCertificateValidated, Is.False);

                EndpointDescription endpoint = session.EndpointDescription;
                var channel = new SecureChannelContext(
                    "roles", endpoint, RequestEncoding.Binary,
                    untrusted.RawData, endpoint.ServerCertificate.ToArray());
                using var context = new OperationContext(
                    new RequestHeader(), channel, RequestType.ActivateSession, RequestLifetime.None);
                MethodInfo addMandatoryRoles = typeof(SessionManager).GetMethod(
                    "AddMandatoryRoles", BindingFlags.Instance | BindingFlags.NonPublic);
                object sessionManager = server.CurrentInstance.SessionManager;

                var untrustedIdentity = (IUserIdentity)addMandatoryRoles.Invoke(
                    sessionManager, [session, context, new UserIdentity()]);
                Assert.That(
                    untrustedIdentity.GrantedRoleIds.ToArray(),
                    Has.No.Member(ObjectIds.WellKnownRole_TrustedApplication));

                // the same session with a validated certificate is a trusted application.
                session.ClientCertificateValidated = true;
                var trustedIdentity = (IUserIdentity)addMandatoryRoles.Invoke(
                    sessionManager, [session, context, new UserIdentity()]);
                Assert.That(
                    trustedIdentity.GrantedRoleIds.ToArray(),
                    Has.Member(ObjectIds.WellKnownRole_TrustedApplication));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                DeleteDirectory(pkiRoot);
            }
        }

        /// <summary>
        /// Accepting one certificate error does not skip the checks that follow it:
        /// an ApplicationUri mismatch after an accepted trust error still fails.
        /// </summary>
        [Test]
        public async Task AcceptedCertificateErrorDoesNotSkipTheApplicationUriCheckAsync()
        {
            using Certificate untrusted = CreateClientCertificate("CN=Accepted Untrusted Client Uri");
            (ServerFixture<AcceptingServer> fixture, string pkiRoot) =
                await StartAcceptingAsync().ConfigureAwait(false);
            try
            {
                AcceptingServer server = fixture.Server;
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await CreateSecuredAsync(server, untrusted, "urn:wrong:application").ConfigureAwait(false));

                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
                Assert.That(server.ReportedErrors, Does.Contain((StatusCode)StatusCodes.BadCertificateUntrusted));
                Assert.That(server.CurrentInstance.SessionManager.GetSessions(), Is.Empty);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                DeleteDirectory(pkiRoot);
            }
        }

        private static async Task<ServerFixture<StandardServer>> StartAsync(int maxSessionCount = 0)
        {
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                SecurityNone = true
            };
            await fixture.LoadConfigurationAsync().ConfigureAwait(false);
            if (maxSessionCount > 0)
            {
                fixture.Config.ServerConfiguration.MaxSessionCount = maxSessionCount;
            }
            await fixture.StartAsync().ConfigureAwait(false);
            return fixture;
        }

        private static async Task<(ServerFixture<AcceptingServer>, string)> StartAcceptingAsync()
        {
            string pkiRoot = Path.Combine(Path.GetTempPath(), "cs-accept-" + Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<AcceptingServer>(telemetry => new AcceptingServer(telemetry))
            {
                SecurityNone = true,
                AutoAccept = false
            };
            await fixture.LoadConfigurationAsync(pkiRoot).ConfigureAwait(false);
            fixture.Config.SecurityConfiguration.RejectUnknownRevocationStatus = false;
            await fixture.StartAsync().ConfigureAwait(false);
            return (fixture, pkiRoot);
        }

        private static SecureChannelContext CreateNoneChannel(StandardServer server, string channelId)
        {
            EndpointDescription endpoint = server.GetEndpoints().Find(
                candidate => candidate.SecurityPolicyUri == SecurityPolicies.None);
            Assert.That(endpoint, Is.Not.Null);
            return new SecureChannelContext(channelId, endpoint, RequestEncoding.Binary, null, null);
        }

        private static ValueTask<CreateSessionResponse> CreateAsync(
            StandardServer server,
            string sessionName,
            string serverUri = null,
            string endpointUrl = null,
            double sessionTimeout = 60000,
            uint maxResponseMessageSize = 0)
        {
            return server.CreateSessionAsync(
                CreateNoneChannel(server, Guid.NewGuid().ToString("N")),
                new RequestHeader(),
                null,
                serverUri,
                endpointUrl,
                sessionName,
                default,
                default,
                sessionTimeout,
                maxResponseMessageSize,
                RequestLifetime.None);
        }

        private static ValueTask<CreateSessionResponse> CreateSecuredAsync(
            AcceptingServer server,
            Certificate clientCertificate,
            string applicationUri)
        {
            EndpointDescription template = server.GetEndpoints().Find(endpoint =>
                endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256 &&
                endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                endpoint.TransportProfileUri == Profiles.UaTcpTransport)
                ?? throw new InvalidOperationException("The expected test endpoint was not configured.");
            var channel = new SecureChannelContext(
                Guid.NewGuid().ToString("N"),
                template,
                RequestEncoding.Binary,
                clientCertificate.RawData,
                template.ServerCertificate.ToArray());
            return server.CreateSessionAsync(
                channel,
                new RequestHeader(),
                new ApplicationDescription
                {
                    ApplicationUri = applicationUri,
                    ApplicationName = new LocalizedText("CreateSession spec regression"),
                    ApplicationType = ApplicationType.Client
                },
                null,
                template.EndpointUrl,
                "accepted-certificate",
                Nonce.CreateRandomNonceData(32).ToByteString(),
                clientCertificate.RawData.ToByteString(),
                60000,
                0,
                RequestLifetime.None);
        }

        private static Certificate CreateClientCertificate(string subject)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .AddExtension(new X509SubjectAltNameExtension([kApplicationUri], ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private const string kApplicationUri = "urn:servercore:createsession-spec-regression";

        /// <summary>
        /// A server whose OnApplicationCertificateError accepts untrusted client
        /// certificates and rejects every other error.
        /// </summary>
        public sealed class AcceptingServer : StandardServer
        {
            public AcceptingServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public List<StatusCode> ReportedErrors { get; } = [];

            protected override void OnApplicationCertificateError(
                ByteString clientCertificate,
                ServiceResult result)
            {
                lock (ReportedErrors)
                {
                    ReportedErrors.Add(result.StatusCode);
                }
                if (result.StatusCode == StatusCodes.BadCertificateUntrusted)
                {
                    return;
                }
                base.OnApplicationCertificateError(clientCertificate, result);
            }
        }
    }
}
