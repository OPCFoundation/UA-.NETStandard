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
    }
}
