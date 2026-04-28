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

#if OPCUA_CLIENT_V2

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Sessions;

namespace Opc.Ua.Client.Tests.ManagedSession
{
    /// <summary>
    /// Unit tests for ManagedSession components including
    /// <see cref="ReconnectPolicy"/>, <see cref="ConnectionState"/>,
    /// <see cref="ConnectionStateMachine"/>, and
    /// <see cref="Opc.Ua.Client.ManagedSession"/> factory methods.
    /// </summary>
    [TestFixture]
    public sealed class ManagedSessionTests
    {
        #region ReconnectPolicy Tests

        [Test]
        public void ExponentialBackoffIncreasesDelay()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Exponential,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromMinutes(10),
                JitterFactor = 0.0
            };

            TimeSpan? delay0 = policy.GetNextDelay(0);
            TimeSpan? delay1 = policy.GetNextDelay(1);
            TimeSpan? delay2 = policy.GetNextDelay(2);

            Assert.That(delay0, Is.Not.Null);
            Assert.That(delay1, Is.Not.Null);
            Assert.That(delay2, Is.Not.Null);

            // Exponential: 1s * 2^0 = 1s, 1s * 2^1 = 2s, 1s * 2^2 = 4s
            Assert.That(delay0!.Value.TotalSeconds, Is.EqualTo(1.0));
            Assert.That(delay1!.Value.TotalSeconds, Is.EqualTo(2.0));
            Assert.That(delay2!.Value.TotalSeconds, Is.EqualTo(4.0));
        }

        [Test]
        public void LinearBackoffIncreasesLinearly()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Linear,
                InitialDelay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromMinutes(10),
                JitterFactor = 0.0
            };

            TimeSpan? delay0 = policy.GetNextDelay(0);
            TimeSpan? delay1 = policy.GetNextDelay(1);
            TimeSpan? delay2 = policy.GetNextDelay(2);

            Assert.That(delay0, Is.Not.Null);
            Assert.That(delay1, Is.Not.Null);
            Assert.That(delay2, Is.Not.Null);

            // Linear: 2s * (0+1) = 2s, 2s * (1+1) = 4s, 2s * (2+1) = 6s
            Assert.That(delay0!.Value.TotalSeconds, Is.EqualTo(2.0));
            Assert.That(delay1!.Value.TotalSeconds, Is.EqualTo(4.0));
            Assert.That(delay2!.Value.TotalSeconds, Is.EqualTo(6.0));
        }

        [Test]
        public void ConstantBackoffReturnsSameDelay()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(5),
                JitterFactor = 0.0
            };

            TimeSpan? delay0 = policy.GetNextDelay(0);
            TimeSpan? delay1 = policy.GetNextDelay(1);
            TimeSpan? delay2 = policy.GetNextDelay(5);

            Assert.That(delay0, Is.Not.Null);
            Assert.That(delay1, Is.Not.Null);
            Assert.That(delay2, Is.Not.Null);

            Assert.That(delay0!.Value.TotalSeconds, Is.EqualTo(5.0));
            Assert.That(delay1!.Value.TotalSeconds, Is.EqualTo(5.0));
            Assert.That(delay2!.Value.TotalSeconds, Is.EqualTo(5.0));
        }

        [Test]
        public void MaxDelayIsCapped()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Exponential,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(10),
                JitterFactor = 0.0
            };

            // Attempt 10: 1s * 2^10 = 1024s, should be capped to 10s
            TimeSpan? delay = policy.GetNextDelay(10);

            Assert.That(delay, Is.Not.Null);
            Assert.That(
                delay!.Value.TotalSeconds,
                Is.EqualTo(10.0));
        }

        [Test]
        public void MaxRetriesStopsAfterLimit()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxRetries = 3,
                JitterFactor = 0.0
            };

            Assert.That(policy.GetNextDelay(0), Is.Not.Null);
            Assert.That(policy.GetNextDelay(1), Is.Not.Null);
            Assert.That(policy.GetNextDelay(2), Is.Not.Null);

            // attempt >= MaxRetries => null
            Assert.That(policy.GetNextDelay(3), Is.Null);
            Assert.That(policy.GetNextDelay(4), Is.Null);
        }

        [Test]
        public void UnlimitedRetriesNeverReturnsNull()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxRetries = 0,
                JitterFactor = 0.0
            };

            for (int i = 0; i < 100; i++)
            {
                Assert.That(
                    policy.GetNextDelay(i),
                    Is.Not.Null,
                    $"GetNextDelay returned null at attempt {i}");
            }
        }

        [Test]
        public void JitterAppliesVariation()
        {
            var policy = new ReconnectPolicy {
                Strategy = BackoffStrategy.Constant,
                InitialDelay = TimeSpan.FromSeconds(10),
                MaxDelay = TimeSpan.FromSeconds(30),
                JitterFactor = 0.5
            };

            var delays = new HashSet<double>();
            for (int i = 0; i < 50; i++)
            {
                TimeSpan? delay = policy.GetNextDelay(0);
                Assert.That(delay, Is.Not.Null);
                delays.Add(delay!.Value.TotalMilliseconds);
            }

            // With 50% jitter over 50 iterations we expect variation
            Assert.That(delays.Count, Is.GreaterThan(1),
                "Jitter should produce varying delays");
        }

        #endregion

        #region ConnectionState Tests

        [Test]
        public void ConnectionStateChangedEventArgsHasCorrectProperties()
        {
            var error = new ServiceResult(StatusCodes.BadCommunicationError);
            var args = new ConnectionStateChangedEventArgs {
                PreviousState = ConnectionState.Connected,
                NewState = ConnectionState.Reconnecting,
                Error = error,
                ReconnectAttempt = 3
            };

            Assert.That(args.PreviousState, Is.EqualTo(ConnectionState.Connected));
            Assert.That(args.NewState, Is.EqualTo(ConnectionState.Reconnecting));
            Assert.That(args.Error, Is.Not.Null);
            Assert.That(args.Error!.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(args.ReconnectAttempt, Is.EqualTo(3));
        }

        #endregion

        #region ConnectionStateMachine Tests

        [Test]
        public void InitialStateIsDisconnected()
        {
            var policy = new ReconnectPolicy();
            var logger = new Mock<ILogger>().Object;

            using var cts = new CancellationTokenSource();
            var sm = new ConnectionStateMachine(policy, logger);

            Assert.That(sm.State, Is.EqualTo(ConnectionState.Disconnected));
        }

        [Test]
        public async Task DisposeTransitionsToClosedState()
        {
            var policy = new ReconnectPolicy();
            var logger = new Mock<ILogger>().Object;

            var sm = new ConnectionStateMachine(policy, logger);
            sm.Start();

            await sm.DisposeAsync().ConfigureAwait(false);

            Assert.That(sm.State, Is.EqualTo(ConnectionState.Closed));
        }

        #endregion

        #region ManagedSession Construction Tests

        [Test]
        public void CreateAsyncThrowsOnNullConfiguration()
        {
            var mockFactory = new Mock<ISessionFactory>();
            mockFactory.Setup(f => f.Telemetry)
                .Returns(new Mock<ITelemetryContext>().Object);

            var endpoint = new ConfiguredEndpoint(
                null,
                new EndpointDescription("opc.tcp://localhost:4840"));

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await Opc.Ua.Client.ManagedSession.CreateAsync(
                    configuration: null!,
                    endpoint: endpoint,
                    sessionFactory: mockFactory.Object).ConfigureAwait(false));
        }

        [Test]
        public void CreateAsyncThrowsOnNullEndpoint()
        {
            var mockFactory = new Mock<ISessionFactory>();
            mockFactory.Setup(f => f.Telemetry)
                .Returns(new Mock<ITelemetryContext>().Object);

            var config = new ApplicationConfiguration {
                ApplicationName = "TestApp",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration {
                    ApplicationCertificate = new CertificateIdentifier()
                }
            };

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await Opc.Ua.Client.ManagedSession.CreateAsync(
                    configuration: config,
                    endpoint: null!,
                    sessionFactory: mockFactory.Object).ConfigureAwait(false));
        }

        #endregion
    }
}

#endif
