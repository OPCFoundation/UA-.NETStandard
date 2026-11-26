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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The endpointUrl of CreateSession, and a CreateSession that fails after the Session
    /// was created.
    /// </summary>
    /// <remarks>
    /// OPC 10000-4 v1.05.07 5.7.2.2: the Server uses the endpointUrl "for diagnostics and to
    /// determine what URLs to return in the response" and "should return a suitable default
    /// URL if it does not recognize the HostName in the URL". No result code is defined for
    /// an endpointUrl that cannot be parsed.
    /// </remarks>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CreateSessionEndpointUrlTests
    {
        private const string kUnknownHost = "opc.tcp://unknown-host.invalid:4840";

        /// <summary>
        /// An endpointUrl that cannot be parsed is handled like one with an unknown host:
        /// the Session is created and can be activated, and the mismatch is audited.
        /// </summary>
        [TestCase(kUnknownHost, true)]
        [TestCase("not a url", true)]
        [TestCase("opc.tcp://", true)]
        [TestCase("opc.tcp://host:notaport", true)]
        [TestCase("myhost:4840/x", true)]
        [TestCase("/relative/path", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public async Task EndpointUrlDoesNotFailCreateSessionAsync(string endpointUrl, bool isAudited)
        {
            var fixture = new ServerFixture<StandardServer> { SecurityNone = true };
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                var audited = new List<string>();
                NodeStateReportEventHandler reportEvent = server.CurrentInstance.ServerObject.OnReportEvent;
                server.CurrentInstance.ServerObject.OnReportEvent = (context, node, e) =>
                {
                    if (e is AuditUrlMismatchEventState audit)
                    {
                        audited.Add(audit.EndpointUrl?.Value);
                    }
                    reportEvent?.Invoke(context, node, e);
                };

                SecureChannelContext channel = CreateChannel(server);
                CreateSessionResponse response = await CreateSessionAsync(server, channel, endpointUrl)
                    .ConfigureAwait(false);

                Assert.That(response.ServerEndpoints, Is.Not.Empty);
                Assert.That(audited, Is.EqualTo(isAudited ? [endpointUrl] : Array.Empty<string>()));

                var requestHeader = new RequestHeader { AuthenticationToken = response.AuthenticationToken };
                await server.ActivateSessionAsync(
                    channel,
                    requestHeader,
                    response.ServerSignature,
                    [],
                    [],
                    default,
                    null,
                    CancellationToken.None).ConfigureAwait(false);
                AssertSessionCounts(server, current: 1, cumulated: 1, rejected: 0);

                await server.CloseSessionAsync(channel, requestHeader, true, CancellationToken.None)
                    .ConfigureAwait(false);
                AssertSessionCounts(server, current: 0, cumulated: 1, rejected: 0);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A CreateSession that fails after the Session was created must not leave the
        /// Session or its count behind. The failure is audited and the
        /// type of the exception is kept.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task FailureAfterCreationRemovesTheSessionAsync(bool serviceResultException)
        {
            var fixture = new ServerFixture<FailingServer> { SecurityNone = true };
            FailingServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                server.Failure = serviceResultException
                    ? new ServiceResultException(StatusCodes.BadInvalidArgument)
                    : new InvalidOperationException("test");
                SecureChannelContext channel = CreateChannel(server);

                var audited = new List<bool?>();
                NodeStateReportEventHandler reportEvent = server.CurrentInstance.ServerObject.OnReportEvent;
                server.CurrentInstance.ServerObject.OnReportEvent = (context, node, e) =>
                {
                    if (e is AuditCreateSessionEventState audit)
                    {
                        audited.Add(audit.Status?.Value);
                    }
                    reportEvent?.Invoke(context, node, e);
                };

                if (serviceResultException)
                {
                    ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                        () => CreateSessionAsync(server, channel, null, additionalParameters: true));
                    Assert.That((StatusCode)sre.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
                }
                else
                {
                    Assert.ThrowsAsync<InvalidOperationException>(
                        () => CreateSessionAsync(server, channel, null, additionalParameters: true));
                }
                AssertSessionCounts(server, current: 0, cumulated: 0, rejected: 1);
                Assert.That(audited, Is.EqualTo(new bool?[] { false }));

                server.Failure = null;
                await CreateSessionAsync(server, channel, null, additionalParameters: true).ConfigureAwait(false);
                AssertSessionCounts(server, current: 1, cumulated: 1, rejected: 1);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static void AssertSessionCounts(StandardServer server, uint current, uint cumulated, uint rejected)
        {
            ServerDiagnosticsSummaryDataType diagnostics = server.CurrentInstance.ServerDiagnostics;
            Assert.That(server.CurrentInstance.SessionManager.GetSessions(), Has.Count.EqualTo((int)current));
            Assert.That(diagnostics.CurrentSessionCount, Is.EqualTo(current));
            Assert.That(diagnostics.CumulatedSessionCount, Is.EqualTo(cumulated));
            Assert.That(diagnostics.RejectedSessionCount, Is.EqualTo(rejected));
        }

        private static SecureChannelContext CreateChannel(StandardServer server)
        {
            EndpointDescription endpoint = server.GetEndpoints()
                .First(e => e.SecurityPolicyUri == SecurityPolicies.None);
            return new SecureChannelContext("channel", endpoint, RequestEncoding.Binary);
        }

        private static Task<CreateSessionResponse> CreateSessionAsync(
            StandardServer server,
            SecureChannelContext channel,
            string endpointUrl,
            bool additionalParameters = false)
        {
            var requestHeader = new RequestHeader();
            if (additionalParameters)
            {
                requestHeader.AdditionalHeader = new ExtensionObject(new AdditionalParametersType());
            }
            return server.CreateSessionAsync(
                channel,
                requestHeader,
                null,
                null,
                endpointUrl,
                "Session",
                null,
                null,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                CancellationToken.None);
        }

        /// <summary>
        /// Fails after the Session was created.
        /// </summary>
        public class FailingServer : StandardServer
        {
            public Exception Failure { get; set; }

            protected override AdditionalParametersType CreateSessionProcessAdditionalParameters(
                ISession session,
                AdditionalParametersType parameters)
            {
                if (Failure != null)
                {
                    throw Failure;
                }
                return base.CreateSessionProcessAdditionalParameters(session, parameters);
            }
        }
    }
}
