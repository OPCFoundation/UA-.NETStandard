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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The budget of Session-less requests (OPC 10000-4 §6.3): how many run at the same
    /// time on a server and on a channel, and that every way a request ends returns its place.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Parallelizable]
    public class SessionlessRequestBudgetTests
    {
        private const string kGoodAccessToken = "good.jwt.token";

        private Mock<IServerInternal> m_serverMock = null!;
        private ApplicationConfiguration m_config = null!;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            m_serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            m_serverMock.Setup(s => s.IdentityRegistry).Returns(new ServerIdentityRegistry());

            m_config = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100,
                    MaxRequestAge = 60_000,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10,
                    HttpsMutualTls = false
                }
            };
        }

        [Test]
        public void TheDefaultLimitsAreFinite()
        {
            var options = new SessionlessInvocationOptions();

            Assert.That(options.MaxConcurrentRequests, Is.EqualTo(SessionlessInvocationOptions.DefaultMaxConcurrentRequests));
            Assert.That(
                options.MaxConcurrentRequestsPerChannel,
                Is.EqualTo(SessionlessInvocationOptions.DefaultMaxConcurrentRequestsPerChannel));
            Assert.That(options.MaxConcurrentRequests, Is.GreaterThan(0));
            Assert.That(options.MaxConcurrentRequestsPerChannel, Is.GreaterThan(0));
            Assert.That(options.MaxConcurrentRequestsPerChannel, Is.LessThanOrEqualTo(options.MaxConcurrentRequests));
        }

        [Test]
        public void ANegativeLimitIsRejected()
        {
            var options = new SessionlessInvocationOptions();

            Assert.That(() => options.MaxConcurrentRequests = -1, Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => options.MaxConcurrentRequestsPerChannel = -1,
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => options.MaxConcurrentRequests = 0, Throws.Nothing);
        }

        [Test]
        public void TheBudgetNeedsOptions()
        {
            Assert.That(() => new SessionlessRequestBudget(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void TheServerLimitRefusesAndAReleaseFreesAPlace()
        {
            var budget = new SessionlessRequestBudget(
                new SessionlessInvocationOptions { MaxConcurrentRequests = 2, MaxConcurrentRequestsPerChannel = 0 });

            Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? first), Is.EqualTo(SessionlessLimit.None));
            Assert.That(budget.TryAcquire(Channel("b"), out IDisposable? second), Is.EqualTo(SessionlessLimit.None));
            Assert.That(budget.TryAcquire(Channel("c"), out IDisposable? refused), Is.EqualTo(SessionlessLimit.Server));
            Assert.That(refused, Is.Null);
            Assert.That(budget.ActiveRequests, Is.EqualTo(2));

            first!.Dispose();
            Assert.That(budget.ActiveRequests, Is.EqualTo(1));
            Assert.That(budget.TryAcquire(Channel("c"), out IDisposable? third), Is.EqualTo(SessionlessLimit.None));

            second!.Dispose();
            third!.Dispose();
            Assert.That(budget.ActiveRequests, Is.Zero);
            Assert.That(budget.TrackedChannels, Is.Zero);
        }

        [Test]
        public void TheChannelLimitRefusesOnlyThatChannelAndTakesNothingFromTheServer()
        {
            var budget = new SessionlessRequestBudget(
                new SessionlessInvocationOptions { MaxConcurrentRequests = 10, MaxConcurrentRequestsPerChannel = 2 });
            var held = new List<IDisposable>();
            for (int i = 0; i < 2; i++)
            {
                Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? lease), Is.EqualTo(SessionlessLimit.None));
                held.Add(lease!);
            }

            Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? refused), Is.EqualTo(SessionlessLimit.Channel));
            Assert.That(refused, Is.Null);
            Assert.That(budget.ActiveRequests, Is.EqualTo(2), "a refused request must not hold a place");

            Assert.That(budget.TryAcquire(Channel("b"), out IDisposable? other), Is.EqualTo(SessionlessLimit.None));
            held.Add(other!);

            held[0].Dispose();
            Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? again), Is.EqualTo(SessionlessLimit.None));
            held.Add(again!);

            foreach (IDisposable lease in held)
            {
                lease.Dispose();
            }
            Assert.That(budget.ActiveRequests, Is.Zero);
            Assert.That(budget.TrackedChannels, Is.Zero);
        }

        [Test]
        public void TheServerLimitIsCheckedBeforeTheChannelLimit()
        {
            var budget = new SessionlessRequestBudget(
                new SessionlessInvocationOptions { MaxConcurrentRequests = 1, MaxConcurrentRequestsPerChannel = 1 });
            Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? lease), Is.EqualTo(SessionlessLimit.None));

            Assert.That(budget.TryAcquire(Channel("a"), out _), Is.EqualTo(SessionlessLimit.Server));

            lease!.Dispose();
        }

        [Test]
        public void ZeroDoesNotLimit()
        {
            var budget = new SessionlessRequestBudget(
                new SessionlessInvocationOptions { MaxConcurrentRequests = 0, MaxConcurrentRequestsPerChannel = 0 });
            var held = new List<IDisposable>();

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? lease), Is.EqualTo(SessionlessLimit.None));
                held.Add(lease!);
            }

            Assert.That(budget.ActiveRequests, Is.EqualTo(1000));
            held.ForEach(l => l.Dispose());
            Assert.That(budget.ActiveRequests, Is.Zero);
        }

        [Test]
        public void ALeaseReleasesItsPlaceOnlyOnce()
        {
            var budget = new SessionlessRequestBudget(
                new SessionlessInvocationOptions { MaxConcurrentRequests = 2, MaxConcurrentRequestsPerChannel = 0 });
            Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? first), Is.EqualTo(SessionlessLimit.None));
            Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? second), Is.EqualTo(SessionlessLimit.None));

            first!.Dispose();
            first.Dispose();
            first.Dispose();

            Assert.That(budget.ActiveRequests, Is.EqualTo(1));
            Assert.That(budget.TryAcquire(Channel("a"), out IDisposable? third), Is.EqualTo(SessionlessLimit.None));
            Assert.That(budget.TryAcquire(Channel("a"), out _), Is.EqualTo(SessionlessLimit.Server));

            second!.Dispose();
            third!.Dispose();
        }

        [Test]
        public void ARequestOverHttpsIsGroupedByThePeerAddress()
        {
            var budget = new SessionlessRequestBudget(
                new SessionlessInvocationOptions { MaxConcurrentRequests = 0, MaxConcurrentRequestsPerChannel = 1 });

            // the HTTPS bindings use one SecureChannel id for all their requests
            Assert.That(
                budget.TryAcquire(Channel("listener", IPAddress.Parse("192.0.2.1")), out IDisposable? first),
                Is.EqualTo(SessionlessLimit.None));
            Assert.That(
                budget.TryAcquire(Channel("listener", IPAddress.Parse("192.0.2.2")), out IDisposable? second),
                Is.EqualTo(SessionlessLimit.None));
            Assert.That(
                budget.TryAcquire(Channel("listener", IPAddress.Parse("192.0.2.1")), out _),
                Is.EqualTo(SessionlessLimit.Channel));

            // an IPv4 address and its IPv4-mapped IPv6 form are one peer
            Assert.That(
                budget.TryAcquire(
                    Channel("listener", IPAddress.Parse("192.0.2.2").MapToIPv6()),
                    out _),
                Is.EqualTo(SessionlessLimit.Channel));

            first!.Dispose();
            second!.Dispose();
        }

        [Test]
        public async Task ConcurrentRequestsNeverExceedTheLimitsAsync()
        {
            const int kServerLimit = 12;
            const int kChannelLimit = 5;
            const int kCallers = 200;
            var budget = new SessionlessRequestBudget(
                new SessionlessInvocationOptions
                {
                    MaxConcurrentRequests = kServerLimit,
                    MaxConcurrentRequestsPerChannel = kChannelLimit
                });
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var granted = new ConcurrentQueue<(string Channel, IDisposable Lease)>();

            Task[] callers = Enumerable.Range(0, kCallers).Select(i => Task.Run(async () =>
            {
                string channel = "channel" + (i % 4);
                await start.Task.ConfigureAwait(false);
                if (budget.TryAcquire(Channel(channel), out IDisposable? lease) == SessionlessLimit.None)
                {
                    granted.Enqueue((channel, lease!));
                }
            })).ToArray();

            start.SetResult(true);
            await Task.WhenAll(callers).ConfigureAwait(false);

            // 4 channels with 5 places each would take 20, the server holds 12
            Assert.That(granted, Has.Count.EqualTo(kServerLimit));
            Assert.That(budget.ActiveRequests, Is.EqualTo(kServerLimit));
            Assert.That(
                granted.GroupBy(g => g.Channel).All(g => g.Count() <= kChannelLimit),
                Is.True);

            // release in parallel: nothing is lost and nothing is released twice
            await Task.WhenAll(granted.Select(g => Task.Run(g.Lease.Dispose))).ConfigureAwait(false);
            Assert.That(budget.ActiveRequests, Is.Zero);
            Assert.That(budget.TrackedChannels, Is.Zero);
        }

        [Test]
        public async Task ParallelAcquireAndReleaseNeverExceedTheLimitAsync()
        {
            const int kLimit = 6;
            var budget = new SessionlessRequestBudget(
                new SessionlessInvocationOptions { MaxConcurrentRequests = kLimit, MaxConcurrentRequestsPerChannel = 0 });
            int running = 0;
            int highWater = 0;
            int refused = 0;

            Task[] callers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 500; i++)
                {
                    if (budget.TryAcquire(Channel("a"), out IDisposable? lease) != SessionlessLimit.None)
                    {
                        Interlocked.Increment(ref refused);
                        await Task.Yield();
                        continue;
                    }
                    int now = Interlocked.Increment(ref running);
                    int seen;
                    while (now > (seen = Volatile.Read(ref highWater)) &&
                        Interlocked.CompareExchange(ref highWater, now, seen) != seen)
                    {
                    }
                    await Task.Yield();
                    Interlocked.Decrement(ref running);
                    lease!.Dispose();
                }
            })).ToArray();
            await Task.WhenAll(callers).ConfigureAwait(false);

            Assert.That(highWater, Is.LessThanOrEqualTo(kLimit));
            Assert.That(highWater, Is.GreaterThan(0));
            Assert.That(refused, Is.GreaterThan(0), "16 callers must have run into the limit of 6");
            Assert.That(budget.ActiveRequests, Is.Zero);
            Assert.That(budget.TrackedChannels, Is.Zero);
        }

        [Test]
        public async Task ARequestOverTheServerLimitIsAnsweredBadServerTooBusyAsync()
        {
            using SessionManager manager = CreateManager(maxRequests: 2, maxPerChannel: 0);
            OperationContext first = await ValidateAsync(manager, Channel("a")).ConfigureAwait(false);
            OperationContext second = await ValidateAsync(manager, Channel("b")).ConfigureAwait(false);

            ServiceResultException? error = Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, Channel("c")).ConfigureAwait(false));

            Assert.That(error, Is.InstanceOf<ServerBusyException>());
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));

            first.Dispose();
            OperationContext third = await ValidateAsync(manager, Channel("c")).ConfigureAwait(false);

            second.Dispose();
            third.Dispose();
        }

        [Test]
        public async Task ARequestOverTheChannelLimitIsAnsweredBadServerTooBusyAsync()
        {
            using SessionManager manager = CreateManager(maxRequests: 0, maxPerChannel: 1);
            OperationContext first = await ValidateAsync(manager, Channel("a")).ConfigureAwait(false);

            ServiceResultException? error = Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, Channel("a")).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));

            OperationContext other = await ValidateAsync(manager, Channel("b")).ConfigureAwait(false);

            first.Dispose();
            OperationContext again = await ValidateAsync(manager, Channel("a")).ConfigureAwait(false);

            other.Dispose();
            again.Dispose();
        }

        [Test]
        public async Task DisposingTheContextTwiceReleasesItsPlaceOnceAsync()
        {
            using SessionManager manager = CreateManager(maxRequests: 2, maxPerChannel: 0);
            OperationContext first = await ValidateAsync(manager, Channel("a")).ConfigureAwait(false);
            OperationContext second = await ValidateAsync(manager, Channel("a")).ConfigureAwait(false);

            first.Dispose();
            first.Dispose();
            OperationContext third = await ValidateAsync(manager, Channel("a")).ConfigureAwait(false);

            ServiceResultException? error = Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, Channel("a")).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));

            second.Dispose();
            third.Dispose();
        }

        [Test]
        public async Task ARejectedAccessTokenReturnsItsPlaceAsync()
        {
            UseJwtAuthenticator();
            using SessionManager manager = CreateManager(maxRequests: 1, maxPerChannel: 1);

            // with a leak the second attempt would be answered Bad_ServerTooBusy
            for (int i = 0; i < 5; i++)
            {
                ServiceResultException? error = Assert.CatchAsync<ServiceResultException>(
                    async () => await ValidateAsync(manager, HttpsChannel(), new NodeId("forged.jwt.token", 0))
                        .ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            }

            OperationContext context = await ValidateAsync(
                manager,
                HttpsChannel(),
                new NodeId(kGoodAccessToken, 0)).ConfigureAwait(false);
            context.Dispose();
        }

        [Test]
        public async Task AnUnencryptedChannelAndAMissingIdentityReturnTheirPlaceAsync()
        {
            using SessionManager manager = CreateManager(maxRequests: 1, maxPerChannel: 1);
            manager.SessionlessInvocation!.AllowAnonymous = false;

            for (int i = 0; i < 3; i++)
            {
                ServiceResultException? noIdentity = Assert.CatchAsync<ServiceResultException>(
                    async () => await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));
                Assert.That(noIdentity!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));

                ServiceResultException? insecure = Assert.CatchAsync<ServiceResultException>(
                    async () => await ValidateAsync(
                        manager,
                        TcpChannel(MessageSecurityMode.None),
                        new NodeId(kGoodAccessToken, 0)).ConfigureAwait(false));
                Assert.That(insecure!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            }

            manager.SessionlessInvocation.AllowAnonymous = true;
            OperationContext context = await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false);
            context.Dispose();
        }

        [Test]
        public async Task ACancelledValidationReturnsItsPlaceAsync()
        {
            var authenticator = new Mock<IUserTokenAuthenticator>();
            authenticator.Setup(a => a.TokenType).Returns(UserTokenType.IssuedToken);
            authenticator.Setup(a => a.IssuedTokenProfileUri).Returns(Profiles.JwtUserToken);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            authenticator
                .Setup(a => a.AuthenticateAsync(It.IsAny<AuthenticationContext>(), It.IsAny<CancellationToken>()))
                .Returns<AuthenticationContext, CancellationToken>(async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return AuthenticationResult.NotHandled;
                });
            m_serverMock.Setup(s => s.IdentityRegistry)
                .Returns(new ServerIdentityRegistry(authenticator.Object));
            using SessionManager manager = CreateManager(maxRequests: 1, maxPerChannel: 1);

            using var lifetime = new RequestLifetime();
            Task<OperationContext> pending = ValidateAsync(
                manager,
                HttpsChannel(),
                new NodeId(kGoodAccessToken, 0),
                lifetime).AsTask();
            await entered.Task.ConfigureAwait(false);

            // the place is taken while the validation runs
            ServiceResultException? busy = Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));
            Assert.That(busy!.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));

            Assert.That(lifetime.TryCancel(StatusCodes.BadRequestCancelledByClient), Is.True);
            // the cancellation reaches the endpoint, which maps it to the status of the request lifetime
            Assert.CatchAsync<OperationCanceledException>(() => pending);

            OperationContext context = await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false);
            context.Dispose();
        }

        [Test]
        public async Task AFailingHandlerReturnsItsPlaceAsync()
        {
            using SessionManager manager = CreateManager(maxRequests: 1, maxPerChannel: 1);
            int calls = 0;
            manager.ValidateSessionLessRequest += (_, args) =>
            {
                switch (Interlocked.Increment(ref calls))
                {
                    case 1:
                        args.Error = new ServiceResult(StatusCodes.BadUserAccessDenied);
                        break;
                    case 2:
                        throw new InvalidOperationException("handler failure");
                    default:
                        args.Identity = new UserIdentity();
                        break;
                }
            };

            ServiceResultException? denied = Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            ServiceResultException? failed = Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));
            Assert.That(failed!.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));

            OperationContext context = await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(3));
            context.Dispose();
        }

        [Test]
        public async Task TheBudgetAlsoLimitsTheRequestsAHandlerDecidesAsync()
        {
            using SessionManager manager = CreateManager(maxRequests: 1, maxPerChannel: 0);
            int calls = 0;
            manager.ValidateSessionLessRequest += (_, args) =>
            {
                Interlocked.Increment(ref calls);
                args.Identity = new UserIdentity();
            };
            OperationContext first = await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false);

            ServiceResultException? error = Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(calls, Is.EqualTo(1), "the handler is not asked for a request over the budget");
            first.Dispose();
        }

        [Test]
        public async Task ARequestADisabledServerRefusesDoesNotTakeAPlaceAsync()
        {
            using var manager = new SessionManager(m_serverMock.Object, m_config);

            // no handler and no options: there is no budget, and every request is refused
            for (int i = 0; i < 200; i++)
            {
                ServiceResultException? error = Assert.CatchAsync<ServiceResultException>(
                    async () => await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            }

            // a handler without options is not budgeted
            manager.ValidateSessionLessRequest += (_, args) => args.Identity = new UserIdentity();
            var held = new List<OperationContext>();
            for (int i = 0; i < 200; i++)
            {
                held.Add(await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));
            }
            held.ForEach(c => c.Dispose());
        }

        [Test]
        public async Task AServiceOutsideTheSessionlessSetTakesNoPlaceAsync()
        {
            using SessionManager manager = CreateManager(maxRequests: 1, maxPerChannel: 1);

            for (int i = 0; i < 5; i++)
            {
                ServiceResultException? error = Assert.CatchAsync<ServiceResultException>(
                    async () => await ValidateAsync(
                        manager,
                        HttpsChannel(),
                        NodeId.Null,
                        requestType: RequestType.CreateSubscription).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            }

            OperationContext context = await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false);
            context.Dispose();
        }

        [Test]
        public async Task ChangedLimitsApplyToTheNextRequestAndNewOptionsStartANewBudgetAsync()
        {
            using SessionManager manager = CreateManager(maxRequests: 1, maxPerChannel: 0);
            SessionlessInvocationOptions options = manager.SessionlessInvocation!;
            OperationContext first = await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false);
            Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));

            options.MaxConcurrentRequests = 2;
            OperationContext second = await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false);

            var replacement = new SessionlessInvocationOptions
            {
                AllowAnonymous = true,
                MaxConcurrentRequests = 1,
                MaxConcurrentRequestsPerChannel = 0
            };
            manager.SessionlessInvocation = replacement;
            Assert.That(manager.SessionlessInvocation, Is.SameAs(replacement));
            OperationContext third = await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false);

            // the places of the old budget are returned to the old budget
            first.Dispose();
            second.Dispose();
            Assert.CatchAsync<ServiceResultException>(
                async () => await ValidateAsync(manager, HttpsChannel()).ConfigureAwait(false));

            third.Dispose();
            manager.SessionlessInvocation = null;
            Assert.That(manager.SessionlessInvocation, Is.Null);
        }

        private SessionManager CreateManager(int maxRequests, int maxPerChannel)
        {
            return new SessionManager(m_serverMock.Object, m_config)
            {
                SessionlessInvocation = new SessionlessInvocationOptions
                {
                    AllowAnonymous = true,
                    MaxConcurrentRequests = maxRequests,
                    MaxConcurrentRequestsPerChannel = maxPerChannel
                }
            };
        }

        private static ValueTask<OperationContext> ValidateAsync(
            SessionManager manager,
            SecureChannelContext channel,
            NodeId authenticationToken = default,
            RequestLifetime? lifetime = null,
            RequestType requestType = RequestType.Read)
        {
            return manager.ValidateRequestAsync(
                new RequestHeader { AuthenticationToken = authenticationToken },
                channel,
                requestType,
                lifetime ?? RequestLifetime.None);
        }

        private static SecureChannelContext Channel(string id, IPAddress? peer = null)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            return new SecureChannelContext(id, endpoint, RequestEncoding.Binary, peerAddress: peer);
        }

        private static SecureChannelContext HttpsChannel()
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "https://localhost/",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            return new SecureChannelContext("rest", endpoint, RequestEncoding.Json);
        }

        private static SecureChannelContext TcpChannel(MessageSecurityMode securityMode)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost",
                SecurityMode = securityMode,
                SecurityPolicyUri = SecurityPolicies.None
            };
            return new SecureChannelContext("tcp", endpoint, RequestEncoding.Binary);
        }

        private void UseJwtAuthenticator()
        {
            var authenticator = new Mock<IUserTokenAuthenticator>();
            authenticator.Setup(a => a.TokenType).Returns(UserTokenType.IssuedToken);
            authenticator.Setup(a => a.IssuedTokenProfileUri).Returns(Profiles.JwtUserToken);
            authenticator
                .Setup(a => a.AuthenticateAsync(It.IsAny<AuthenticationContext>(), It.IsAny<CancellationToken>()))
                .Returns<AuthenticationContext, CancellationToken>((ctx, _) =>
                    new ValueTask<AuthenticationResult>(
                        ctx.TokenHandler is IssuedIdentityTokenHandler jwt &&
                        System.Text.Encoding.UTF8.GetString(jwt.DecryptedTokenData!) == kGoodAccessToken
                            ? AuthenticationResult.Accept(new UserIdentity())
                            : AuthenticationResult.Reject(new ServiceResult(StatusCodes.BadIdentityTokenRejected))));
            m_serverMock.Setup(s => s.IdentityRegistry).Returns(new ServerIdentityRegistry(authenticator.Object));
        }
    }
}
