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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using ServerSession = Opc.Ua.Server.Session;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for request admission while a Session is being closed and for
    /// the rejection accounting of requests refused during validation.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public class SessionRequestAdmissionRegressionTests
    {
        /// <summary>
        /// A request whose cancellation callback fails while its Session is closed must not
        /// leave the Session marked closing but still registered (OPC 10000-4 5.7.2.1).
        /// </summary>
        [Test]
        public async Task CloseSessionCompletesWhenAbortingARequestFailsAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                (RequestHeader requestHeader, SecureChannelContext _) =
                    await server.CreateAndActivateSessionAsync("AbortFails").ConfigureAwait(false);
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                ISession? session = serverInternal.SessionManager.GetSession(requestHeader.AuthenticationToken);
                Assert.That(session, Is.Not.Null);

                using var lifetime = new RequestLifetime();
                using CancellationTokenRegistration registration = lifetime.CancellationToken.Register(
                    () => throw new InvalidOperationException("cancellation callback"));
                var outstanding = new OperationContext(
                    new RequestHeader { RequestHandle = 1 },
                    null!,
                    RequestType.Call,
                    lifetime,
                    session!);
                using IDisposable requestScope = serverInternal.RequestManager.EnterRequestScope(outstanding);

                Assert.DoesNotThrowAsync(async () => await serverInternal
                    .CloseSessionAsync(null!, session!.Id, true)
                    .ConfigureAwait(false));

                Assert.That(
                    serverInternal.SessionManager.GetSession(requestHeader.AuthenticationToken),
                    Is.Null,
                    "The Session must be removed although aborting its request failed.");
                Assert.That(outstanding.OperationStatus.Code, Is.EqualTo(StatusCodes.BadSessionClosed));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A request the Session admitted just before a close marked it closing, and that
        /// registered only after the close aborted the outstanding requests, must not run
        /// against the closing Session (OPC 10000-4 5.7.2.1).
        /// </summary>
        [Test]
        public async Task RequestRegisteredAfterTheCloseSweepIsRejectedAsync()
        {
            var fixture = new ServerFixture<AdmissionServer>(t => new AdmissionServer(t));
            AdmissionServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("ClosingWindow").ConfigureAwait(false);
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                var sessionManager = (ClosingWindowSessionManager)serverInternal.SessionManager;
                uint rejectedBefore = serverInternal.ServerDiagnostics.RejectedRequestsCount;

                // The close starts after Session.ValidateRequest admitted the Read.
                sessionManager.MarkClosingAfterAdmission = true;
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ReadAsync(
                        secureChannelContext,
                        new RequestHeader { AuthenticationToken = requestHeader.AuthenticationToken },
                        0,
                        TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                        RequestLifetime.None).ConfigureAwait(false))!;

                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
                Assert.That(
                    serverInternal.ServerDiagnostics.RejectedRequestsCount,
                    Is.EqualTo(rejectedBefore + 1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A server whose session manager can start a close inside the admission window.
        /// </summary>
        public sealed class AdmissionServer : StandardServer
        {
            public AdmissionServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            protected override ISessionManager CreateSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return new ClosingWindowSessionManager(server, configuration);
            }
        }

        /// <summary>
        /// Marks the Session closing after it admitted a request, which is where a
        /// concurrent close lands in the race.
        /// </summary>
        private sealed class ClosingWindowSessionManager : SessionManager
        {
            public ClosingWindowSessionManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            public bool MarkClosingAfterAdmission { get; set; }

            protected override void ReevaluateIdentityIfStale(
                ISession session,
                SecureChannelContext secureChannelContext)
            {
                base.ReevaluateIdentityIfStale(session, secureChannelContext);
                if (MarkClosingAfterAdmission)
                {
                    MarkClosingAfterAdmission = false;
                    ((ServerSession)session).MarkClosing();
                }
            }
        }
    }
}
