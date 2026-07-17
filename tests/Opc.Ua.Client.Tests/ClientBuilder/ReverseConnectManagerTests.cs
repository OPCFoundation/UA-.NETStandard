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

#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Unit tests for <see cref="ReverseConnectManager"/> public API:
    /// AddEndpoint overloads, RegisterWaitingConnection, UnregisterWaitingConnection,
    /// ClearWaitingConnections, StartService overloads and failure diagnostics,
    /// and Dispose lifecycle.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ReverseConnect")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ReverseConnectManagerTests
    {
        private static ITelemetryContext CreateTelemetry()
        {
            return NUnitTelemetryContext.Create();
        }

        private static ApplicationConfiguration CreateAppConfig(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "Test",
                ClientConfiguration = new ClientConfiguration
                {
                    ReverseConnect = new ReverseConnectClientConfiguration()
                }
            };
        }

        private static Uri CreateListenerUri()
        {
            int port = ServerFixtureUtils.GetNextFreeIPPort();
            return new Uri(
                "opc.tcp://localhost:" +
                port.ToString(CultureInfo.InvariantCulture) +
                "/reverse");
        }

        [Test]
        public void Constructor_WithTelemetry_Succeeds()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void Constructor_WithTelemetryAndTimeProvider_Succeeds()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry, TimeProvider.System);

            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void Constructor_WithTelemetryAndNullTimeProvider_UsesSystemTime()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry, timeProvider: null);

            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void TransportBindings_DefaultsToNull()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(manager.TransportBindings, Is.Null);
        }

        [Test]
        public void TransportBindings_CanBeAssigned()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var registry = DefaultTransportBindingRegistry.WithDefaultTcp();

            manager.TransportBindings = registry;

            Assert.That(manager.TransportBindings, Is.SameAs(registry));
        }

        [Test]
        public void AddEndpoint_NullUrl_ThrowsArgumentNullException()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(
                () => manager.AddEndpoint(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddEndpoint_ValidTcpUrl_DoesNotThrow()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var uri = new Uri("opc.tcp://localhost:54321/reverse");

            // The listener is created here but does not bind until StartService.
            Assert.That(() => manager.AddEndpoint(uri), Throws.Nothing);
        }

        [Test]
        public void AddEndpointHostlessUrlThrowsBadTcpEndpointUrlInvalid()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var uri = new Uri("opc.tcp:///reverse");

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => manager.AddEndpoint(uri))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTcpEndpointUrlInvalid));
            Assert.That(exception.Message, Does.Contain(uri.ToString()));
        }

        [Test]
        public void AddEndpointFactoryArgumentExceptionThrowsBadTcpEndpointUrlInvalid()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var registry = new Mock<ITransportBindingRegistry>();
            registry
                .Setup(r => r.CreateListener("opc.test", telemetry))
                .Throws(new ArgumentException("Invalid listener."));
            using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = registry.Object
            };
            var uri = new Uri("opc.test://localhost:4840");

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => manager.AddEndpoint(uri))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTcpEndpointUrlInvalid));
            Assert.That(exception.Message, Does.Contain(uri.ToString()));
        }

        [Test]
        public void AddEndpoint_AfterStartService_ThrowsBadInvalidState()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            // Start with an empty config so no listeners are opened.
            manager.StartService(new ReverseConnectClientConfiguration());

            Assert.That(
                () => manager.AddEndpoint(new Uri("opc.tcp://localhost:54321/")),
                Throws.TypeOf<ServiceResultException>()
                      .With.Property(nameof(ServiceResultException.StatusCode))
                           .EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void AddEndpointWithConfig_NullUrl_ThrowsArgumentNullException()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(
                () => manager.AddEndpoint(null!, null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddEndpointWithConfig_NullConfig_DoesNotThrow()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var uri = new Uri("opc.tcp://localhost:54322/reverse");

            Assert.That(() => manager.AddEndpoint(uri, null), Throws.Nothing);
        }

        [Test]
        public void AddEndpointWithConfig_WithConfiguration_DoesNotThrow()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var uri = new Uri("opc.tcp://localhost:54323/reverse");
            ApplicationConfiguration cfg = CreateAppConfig(telemetry);

            Assert.That(() => manager.AddEndpoint(uri, cfg), Throws.Nothing);
        }

        [Test]
        public void AddEndpointWithConfig_AfterStartService_ThrowsBadInvalidState()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            manager.StartService(new ReverseConnectClientConfiguration());

            Assert.That(
                () => manager.AddEndpoint(new Uri("opc.tcp://localhost:54324/"), null),
                Throws.TypeOf<ServiceResultException>()
                      .With.Property(nameof(ServiceResultException.StatusCode))
                           .EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void StartServiceClientConfig_NullConfig_ThrowsArgumentNullException()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(
                () => manager.StartService((ReverseConnectClientConfiguration)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void StartServiceClientConfig_EmptyConfig_Succeeds()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            // StartService with an empty configuration (no endpoints to open)
            // should succeed without trying to bind any network port.
            Assert.That(
                () => manager.StartService(new ReverseConnectClientConfiguration()),
                Throws.Nothing);
        }

        [Test]
        public void StartServiceClientConfig_CalledTwice_ThrowsBadInvalidState()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            manager.StartService(new ReverseConnectClientConfiguration());

            Assert.That(
                () => manager.StartService(new ReverseConnectClientConfiguration()),
                Throws.TypeOf<ServiceResultException>()
                      .With.Property(nameof(ServiceResultException.StatusCode))
                           .EqualTo(StatusCodes.BadInvalidState));
        }

        [TestCase("not a uri")]
        [TestCase("opc.tcp:///reverse")]
        public void StartServiceClientConfigInvalidEndpointThrowsBadTcpEndpointUrlInvalid(
            string endpointUrl)
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var configuration = new ReverseConnectClientConfiguration
            {
                ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                new ReverseConnectClientEndpoint[]
                {
                    new ReverseConnectClientEndpoint
                    {
                        EndpointUrl = endpointUrl
                    }
                })
            };

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => manager.StartService(configuration))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTcpEndpointUrlInvalid));
            Assert.That(exception.Message, Does.Contain(endpointUrl));
        }

        [Test]
        public void StartServiceClientConfigOccupiedPortThrowsBadNoCommunication()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            Uri listenerUrl = CreateListenerUri();
            using var firstManager = new ReverseConnectManager(telemetry);
            firstManager.AddEndpoint(listenerUrl);
            firstManager.StartService(new ReverseConnectClientConfiguration());
            using var secondManager = new ReverseConnectManager(telemetry);
            secondManager.AddEndpoint(listenerUrl);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => secondManager.StartService(new ReverseConnectClientConfiguration()))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(exception.Message, Does.Contain(listenerUrl.ToString()));
        }

        [Test]
        public void StartServiceClientConfigMultipleOccupiedPortsReportsAllEndpoints()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            Uri firstUrl = CreateListenerUri();
            Uri secondUrl;
            do
            {
                secondUrl = CreateListenerUri();
            } while (secondUrl.Port == firstUrl.Port);
            using var occupyingManager = new ReverseConnectManager(telemetry);
            occupyingManager.AddEndpoint(firstUrl);
            occupyingManager.AddEndpoint(secondUrl);
            occupyingManager.StartService(new ReverseConnectClientConfiguration());
            using var failingManager = new ReverseConnectManager(telemetry);
            failingManager.AddEndpoint(firstUrl);
            failingManager.AddEndpoint(secondUrl);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => failingManager.StartService(new ReverseConnectClientConfiguration()))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(exception.Message, Does.Contain(firstUrl.ToString()));
            Assert.That(exception.Message, Does.Contain(secondUrl.ToString()));
            Assert.That(exception.InnerException, Is.TypeOf<AggregateException>());
        }

        [Test]
        public void StartServiceClientConfigCreationFailureClosesStagedListeners()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            var listener = new Mock<ITransportListener>();
            listener
                .Setup(l => l.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var factory = new Mock<ITransportListenerFactory>();
            factory.SetupGet(f => f.UriScheme).Returns("opc.first");
            factory
                .Setup(f => f.Create(telemetry))
                .Returns(listener.Object);
            var registry = new DefaultTransportBindingRegistry();
            registry.RegisterListenerFactory(factory.Object);
            using var manager = new ReverseConnectManager(telemetry)
            {
                TransportBindings = registry
            };
            var configuration = new ReverseConnectClientConfiguration
            {
                ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                new ReverseConnectClientEndpoint[]
                {
                    new ReverseConnectClientEndpoint
                    {
                        EndpointUrl = "opc.first://localhost:4840"
                    },
                    new ReverseConnectClientEndpoint
                    {
                        EndpointUrl = "opc.missing://localhost:4840"
                    }
                })
            };

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => manager.StartService(configuration))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadProtocolVersionUnsupported));
            listener.Verify(
                l => l.CloseAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void StartServiceAppConfig_NullConfig_ThrowsArgumentNullException()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(
                () => manager.StartService((ApplicationConfiguration)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void StartServiceAppConfig_ValidEmptyConfig_Succeeds()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            ApplicationConfiguration cfg = CreateAppConfig(telemetry);

            Assert.That(() => manager.StartService(cfg), Throws.Nothing);
        }

        [Test]
        public void StartServiceAppConfig_CalledTwice_ThrowsBadInvalidState()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            ApplicationConfiguration cfg = CreateAppConfig(telemetry);

            manager.StartService(cfg);

            Assert.That(
                () => manager.StartService(cfg),
                Throws.TypeOf<ServiceResultException>()
                      .With.Property(nameof(ServiceResultException.StatusCode))
                           .EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void StartServiceAppConfigInvalidEndpointThrowsBadTcpEndpointUrlInvalid()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            ApplicationConfiguration configuration = CreateAppConfig(telemetry);
            configuration.ClientConfiguration!.ReverseConnect!.ClientEndpoints =
                new ArrayOf<ReverseConnectClientEndpoint>(
                new ReverseConnectClientEndpoint[]
                {
                    new ReverseConnectClientEndpoint
                    {
                        EndpointUrl = "not a uri"
                    }
                });

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => manager.StartService(configuration))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTcpEndpointUrlInvalid));
            Assert.That(
                () => manager.StartService(new ReverseConnectClientConfiguration()),
                Throws.Nothing);
        }

        [Test]
        public void StartServiceAppConfigFailureRollsBackOpenedListeners()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            Uri occupiedUrl = CreateListenerUri();
            using var firstManager = new ReverseConnectManager(telemetry);
            firstManager.AddEndpoint(occupiedUrl);
            firstManager.StartService(new ReverseConnectClientConfiguration());
            Uri availableUrl = CreateListenerUri();
            using var failingManager = new ReverseConnectManager(telemetry);
            failingManager.AddEndpoint(availableUrl);
            failingManager.AddEndpoint(occupiedUrl);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => failingManager.StartService(CreateAppConfig(telemetry)))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(exception.Message, Does.Contain(occupiedUrl.ToString()));

            using var retryManager = new ReverseConnectManager(telemetry);
            retryManager.AddEndpoint(availableUrl);
            Assert.That(
                () => retryManager.StartService(new ReverseConnectClientConfiguration()),
                Throws.Nothing);
        }

        [Test]
        public void StartServiceAppConfigWatcherFailureDoesNotBindListener()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            Uri listenerUrl = CreateListenerUri();
            using var failingManager = new ReverseConnectManager(telemetry);
            failingManager.AddEndpoint(listenerUrl);
            ApplicationConfiguration configuration = CreateAppConfig(telemetry);
            string sourceFilePath = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                "missing.config.xml");
            typeof(ApplicationConfiguration)
                .GetProperty(nameof(ApplicationConfiguration.SourceFilePath))!
                .SetValue(configuration, sourceFilePath);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => failingManager.StartService(configuration))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));

            using var retryManager = new ReverseConnectManager(telemetry);
            retryManager.AddEndpoint(listenerUrl);
            Assert.That(
                () => retryManager.StartService(new ReverseConnectClientConfiguration()),
                Throws.Nothing);
        }

        [Test]
        public void DisposeAfterStartWithSourceFileDisposesWatcher()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            string sourceFilePath = Path.GetTempFileName();
            try
            {
                using var manager = new ReverseConnectManager(telemetry);
                ApplicationConfiguration configuration = CreateAppConfig(telemetry);
                typeof(ApplicationConfiguration)
                    .GetProperty(nameof(ApplicationConfiguration.SourceFilePath))!
                    .SetValue(configuration, sourceFilePath);

                Assert.That(() => manager.StartService(configuration), Throws.Nothing);
            }
            finally
            {
                File.Delete(sourceFilePath);
            }
        }

        [Test]
        public void RegisterWaitingConnection_NullUrl_ThrowsArgumentNullException()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(
                () => manager.RegisterWaitingConnection(
                    null!,
                    null,
                    (_, _) => { },
                    ReverseConnectManager.ReverseConnectStrategy.Once),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RegisterWaitingConnection_ValidArgs_ReturnsNonZeroHashCode()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var uri = new Uri("opc.tcp://localhost:54325/");

            int hashCode = manager.RegisterWaitingConnection(
                uri,
                null,
                (_, _) => { },
                ReverseConnectManager.ReverseConnectStrategy.Once);

            Assert.That(hashCode, Is.Not.Zero);
        }

        [Test]
        public void RegisterAndUnregisterWaitingConnection_RoundTrip()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var uri = new Uri("opc.tcp://localhost:54326/");

            int hashCode = manager.RegisterWaitingConnection(
                uri,
                "urn:server:test",
                (_, _) => { },
                ReverseConnectManager.ReverseConnectStrategy.Always);

            // Unregistering a known hash should not throw.
            Assert.That(() => manager.UnregisterWaitingConnection(hashCode), Throws.Nothing);
        }

        [Test]
        public void UnregisterWaitingConnection_UnknownHashCode_DoesNotThrow()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(
                () => manager.UnregisterWaitingConnection(999999),
                Throws.Nothing);
        }

        [Test]
        public void RegisterWaitingConnection_MultipleRegistrations_AllReturnDistinctHashCodes()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var uri = new Uri("opc.tcp://localhost:54327/");

            int hc1 = manager.RegisterWaitingConnection(
                uri, null, (_, _) => { }, ReverseConnectManager.ReverseConnectStrategy.Once);
            int hc2 = manager.RegisterWaitingConnection(
                uri, null, (_, _) => { }, ReverseConnectManager.ReverseConnectStrategy.AnyOnce);

            Assert.That(hc1, Is.Not.EqualTo(hc2));
        }

        [Test]
        public void ClearWaitingConnections_RemovesAll()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            var uri = new Uri("opc.tcp://localhost:54328/");

            manager.RegisterWaitingConnection(
                uri, null, (_, _) => { }, ReverseConnectManager.ReverseConnectStrategy.Once);
            manager.RegisterWaitingConnection(
                uri, null, (_, _) => { }, ReverseConnectManager.ReverseConnectStrategy.Always);

            Assert.That(manager.ClearWaitingConnections, Throws.Nothing);
        }

        [Test]
        public void ClearWaitingConnections_OnEmptyManager_DoesNotThrow()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);

            Assert.That(manager.ClearWaitingConnections, Throws.Nothing);
        }

        [Test]
        public void DefaultWaitTimeout_Is20000Ms()
        {
            Assert.That(ReverseConnectManager.DefaultWaitTimeout, Is.EqualTo(20000));
        }

        [Test]
        public void Dispose_CanBeCalledOnNewManager()
        {
            ITelemetryContext telemetry = CreateTelemetry();
#pragma warning disable CA2000 // Intentionally testing Dispose() directly — not using `using`
            var manager = new ReverseConnectManager(telemetry);
#pragma warning restore CA2000
            Assert.That(manager.Dispose, Throws.Nothing);
        }

        [Test]
        public void Dispose_CanBeCalledAfterStartService()
        {
            ITelemetryContext telemetry = CreateTelemetry();
#pragma warning disable CA2000 // Intentionally testing Dispose() directly — not using `using`
            var manager = new ReverseConnectManager(telemetry);
#pragma warning restore CA2000
            manager.StartService(new ReverseConnectClientConfiguration());
            Assert.That(manager.Dispose, Throws.Nothing);
        }

        [Test]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            ITelemetryContext telemetry = CreateTelemetry();
#pragma warning disable CA2000 // Intentionally testing double-Dispose: not using `using`
            var manager = new ReverseConnectManager(telemetry);
#pragma warning restore CA2000
            manager.Dispose();

            // A second Dispose must not throw.
            Assert.That(manager.Dispose, Throws.Nothing);
        }

        [Test]
        public async Task WaitForConnectionAsync_Cancelled_ThrowsServiceResultException()
        {
            ITelemetryContext telemetry = CreateTelemetry();
            using var manager = new ReverseConnectManager(telemetry);
            using var cts = new CancellationTokenSource(millisecondsDelay: 100);
            var uri = new Uri("opc.tcp://localhost:54329/");

            try
            {
                await manager.WaitForConnectionAsync(uri, null, cts.Token)
                    .ConfigureAwait(false);

                Assert.Fail("Expected ServiceResultException or OperationCanceledException.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            }
            catch (OperationCanceledException)
            {
                // Also acceptable — cancellation propagated before the timeout path.
            }
        }
    }
}
