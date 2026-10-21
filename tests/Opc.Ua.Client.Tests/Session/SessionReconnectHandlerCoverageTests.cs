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
using System.Reflection;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Additional deterministic coverage for <see cref="SessionReconnectHandler"/> that focuses
    /// on the synchronous surface: constants, initial state, reconnect-period arithmetic,
    /// cancellation, disposal and argument-guard branches. These tests never start the reconnect
    /// timer loop and never touch a live session or the network.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SessionReconnectHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionReconnectHandlerCoverageTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ReconnectPeriodConstantsHaveExpectedValues()
        {
            Assert.That(SessionReconnectHandler.MinReconnectPeriod, Is.EqualTo(500));
            Assert.That(SessionReconnectHandler.MaxReconnectPeriod, Is.EqualTo(30000));
            Assert.That(SessionReconnectHandler.DefaultReconnectPeriod, Is.EqualTo(1000));
            Assert.That(SessionReconnectHandler.MinReconnectOperationTimeout, Is.EqualTo(5000));
        }

        [Test]
        public void NewHandlerStartsInReadyState()
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Ready));
        }

        [Test]
        public void NewHandlerHasNoSession()
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(handler.Session, Is.Null);
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR04LegacyReconnectRejectsHttpRefreshBeforeRecreationAsync()
        {
            await using var fixture = new SessionRequestContextTestContext();
            ConfiguredEndpoint endpoint = fixture.InnerSession.ConfiguredEndpoint;
            endpoint.Description.EndpointUrl = "https://requested.test/ua";
            endpoint.Description.TransportProfileUri = Profiles.HttpsBinaryTransport;
            endpoint.Description.SecurityMode = MessageSecurityMode.None;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.None;
            endpoint.Description.UserIdentityTokens =
            [
                new UserTokenPolicy
                {
                    TokenType = UserTokenType.UserName,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }
            ];
            using var handler = new HttpRefreshReconnectHandler(m_telemetry);
            typeof(SessionReconnectHandler).GetProperty(nameof(SessionReconnectHandler.Session))!
                .GetSetMethod(nonPublic: true)!.Invoke(handler, [fixture.InnerSession]);
            foreach (string field in new[] { "m_reconnectFailed", "m_updateFromServer" })
            {
                typeof(SessionReconnectHandler).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(handler, true);
            }
            var operation = (Task<bool>)typeof(SessionReconnectHandler)
                .GetMethod("DoReconnectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(handler, null)!;

            Assert.That(await operation.ConfigureAwait(false), Is.False);
            Assert.That(handler.Refreshes, Is.EqualTo(1));
            Assert.That(handler.Session, Is.SameAs(fixture.InnerSession));
            Assert.That(endpoint.Description.EndpointUrl, Is.EqualTo("https://requested.test/ua"));
            Assert.That(endpoint.Description.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
            Assert.That(endpoint.Description.UserIdentityTokens[0].SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(fixture.Requests, Is.Empty);
        }

        private sealed class HttpRefreshReconnectHandler(ITelemetryContext telemetry) : SessionReconnectHandler(telemetry)
        {
            internal int Refreshes { get; private set; }

            internal override Task UpdateFromServerAsync(
                ConfiguredEndpoint endpoint, ITransportWaitingConnection? connection,
                MessageSecurityMode securityMode, string securityPolicyUri)
            {
                Refreshes++;
                endpoint.Description.EndpointUrl = "http://requested.test/ua";
                return Task.CompletedTask;
            }
        }

        [Test]
        public void ConstructorWithTimeProviderStartsReady()
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: true,
                maxReconnectPeriod: 10000,
                timeProvider: TimeProvider.System);

            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Ready));
        }

        [TestCase(0, 500)]
        [TestCase(100, 500)]
        public void CheckedReconnectPeriodWithoutMaxRaisesValuesBelowMinimumToMinimum(
            int input, int expected)
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(handler.CheckedReconnectPeriod(input), Is.EqualTo(expected));
        }

        [TestCase(500, 500)]
        [TestCase(1000, 1000)]
        [TestCase(30000, 30000)]
        [TestCase(60000, 60000)]
        public void CheckedReconnectPeriodWithoutMaxReturnsValuesAtOrAboveMinimumUnchanged(
            int input, int expected)
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(handler.CheckedReconnectPeriod(input), Is.EqualTo(expected));
        }

        [TestCase(100, 500)]
        [TestCase(1000, 1000)]
        [TestCase(4000, 4000)]
        public void CheckedReconnectPeriodWithoutMaxIgnoresExponentialBackoff(int input, int expected)
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(
                handler.CheckedReconnectPeriod(input, exponentialBackoff: true),
                Is.EqualTo(expected));
        }

        [TestCase(100, 500)]
        [TestCase(500, 500)]
        [TestCase(5000, 5000)]
        [TestCase(10000, 10000)]
        [TestCase(20000, 10000)]
        public void CheckedReconnectPeriodWithMaxClampsWithinBounds(int input, int expected)
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: false,
                maxReconnectPeriod: 10000);

            Assert.That(handler.CheckedReconnectPeriod(input), Is.EqualTo(expected));
        }

        [TestCase(100, 500)]
        [TestCase(1000, 2000)]
        [TestCase(5000, 10000)]
        [TestCase(8000, 10000)]
        public void CheckedReconnectPeriodWithMaxAppliesExponentialBackoff(int input, int expected)
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: false,
                maxReconnectPeriod: 10000);

            Assert.That(
                handler.CheckedReconnectPeriod(input, exponentialBackoff: true),
                Is.EqualTo(expected));
        }

        [Test]
        public void CheckedReconnectPeriodClampsToGlobalMaximum()
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: false,
                maxReconnectPeriod: 50000);

            Assert.That(handler.CheckedReconnectPeriod(100000), Is.EqualTo(30000));
            Assert.That(handler.CheckedReconnectPeriod(1000), Is.EqualTo(1000));
        }

        [Test]
        public void CheckedReconnectPeriodWithMaxBelowMinimumDisablesBackoff()
        {
            using var handler = new SessionReconnectHandler(
                m_telemetry,
                reconnectAbort: false,
                maxReconnectPeriod: 100);

            Assert.That(handler.CheckedReconnectPeriod(200), Is.EqualTo(500));
            Assert.That(
                handler.CheckedReconnectPeriod(2000, exponentialBackoff: true),
                Is.EqualTo(2000));
        }

        [Test]
        public void JitteredReconnectPeriodStaysWithinTenPercent()
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            for (int i = 0; i < 200; i++)
            {
                int jittered = handler.JitteredReconnectPeriod(10000);

                Assert.That(jittered, Is.InRange(9000, 11000));
            }
        }

        [Test]
        public void CancelReconnectWhenIdleDoesNotThrowAndStaysReady()
        {
            using var handler = new SessionReconnectHandler(m_telemetry);

            Assert.That(handler.CancelReconnect, Throws.Nothing);
            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Ready));
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var handler = new SessionReconnectHandler(m_telemetry);
            handler.Dispose();

            Assert.That(handler.Dispose, Throws.Nothing);
            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Disposed));
        }

        [Test]
        public void CancelReconnectAfterDisposeDoesNotThrow()
        {
            var handler = new SessionReconnectHandler(m_telemetry);
            handler.Dispose();

            Assert.That(handler.CancelReconnect, Throws.Nothing);
            Assert.That(handler.State, Is.EqualTo(SessionReconnectHandler.ReconnectState.Disposed));
        }

        [Test]
        public void BeginReconnectAfterDisposeThrowsServiceResultException()
        {
            var handler = new SessionReconnectHandler(m_telemetry);
            handler.Dispose();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                handler.BeginReconnect(null!, 1000, (_, _) => { }));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void BeginReconnectWithNonSessionAfterDisposeThrowsNotSupportedException()
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.SessionId).Returns(NodeId.Null);

            var handler = new SessionReconnectHandler(m_telemetry);
            handler.Dispose();

            Assert.That(
                () => handler.BeginReconnect(session.Object, 1000, (_, _) => { }),
                Throws.TypeOf<NotSupportedException>());
        }
    }
}
