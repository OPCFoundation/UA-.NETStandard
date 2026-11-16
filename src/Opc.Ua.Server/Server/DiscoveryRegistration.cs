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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Registers a server with the discovery server (LDS): builds the registration
    /// information when the server starts, registers periodically once the server is
    /// ready (retrying with back-off, alternating RegisterServer2 and RegisterServer),
    /// and unregisters when the server shuts down.
    /// </summary>
    /// <remarks>
    /// The registration is owned by <see cref="StandardServer"/>, which drives it from
    /// its start and shutdown sequence. Every attempt runs under a generation: stopping
    /// the registration bumps the generation, so an attempt that is still running when
    /// the registration is stopped or restarted never re-arms the timer.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1001:Types that own disposable fields should be disposable",
        Justification = "The timer and the cancellation of an attempt are released by StopAsync, which the owning server runs on every start and shutdown.")]
    internal sealed class DiscoveryRegistration
    {
        /// <summary>
        /// Creates the registration of a server.
        /// </summary>
        /// <param name="server">The server to register.</param>
        /// <param name="configuration">The current configuration of the server.</param>
        /// <param name="timeProvider">The time provider of the server.</param>
        /// <param name="logger">The logger of the server.</param>
        /// <param name="workerFactory">Replaces a registration attempt (tests), or null.</param>
        public DiscoveryRegistration(
            ServerBase server,
            Func<ApplicationConfiguration?> configuration,
            TimeProvider timeProvider,
            ILogger logger,
            Func<CancellationToken, Task>? workerFactory)
        {
            m_server = server;
            m_configuration = configuration;
            m_timeProvider = timeProvider;
            m_logger = logger;
            m_workerFactory = workerFactory;
        }

        /// <summary>
        /// Allows the registration to start again after a shutdown request (the server
        /// is starting).
        /// </summary>
        public void ClearShutdownRequest()
        {
            lock (m_lock)
            {
                m_shutdownRequested = false;
            }
        }

        /// <summary>
        /// The next attempt starts with RegisterServer2.
        /// </summary>
        public void PreferRegisterServer2()
        {
            m_useRegisterServer2 = true;
        }

        /// <summary>
        /// Stops a running registration and builds the registration information and
        /// endpoints of the server from its configuration.
        /// </summary>
        /// <param name="configuration">The configuration of the starting server.</param>
        /// <param name="serverDescription">The description of the server.</param>
        /// <param name="discoveryUrls">The discovery urls of the base addresses of the server.</param>
        public async ValueTask ConfigureAsync(
            ApplicationConfiguration configuration,
            ApplicationDescription serverDescription,
            IReadOnlyList<Uri?> discoveryUrls)
        {
            await StopAsync().ConfigureAwait(false);
            lock (m_lock)
            {
                m_maxRegistrationInterval = configuration.ServerConfiguration!
                    .MaxRegistrationInterval;

                m_registrationInfo = new RegisteredServer
                {
                    ServerUri = serverDescription.ApplicationUri!
                };
                m_registrationInfo.ServerNames += serverDescription.ApplicationName;
                m_registrationInfo.ProductUri = serverDescription.ProductUri;
                m_registrationInfo.ServerType = serverDescription.ApplicationType;
                m_registrationInfo.GatewayServerUri = null;
                m_registrationInfo.IsOnline = false;
                m_registrationInfo.SemaphoreFilePath = null;

                // add all discovery urls.
                string computerName = Utils.GetHostName();

                var registeredUrls = new List<string>();
                for (int ii = 0; ii < discoveryUrls.Count; ii++)
                {
                    var uri = new UriBuilder(discoveryUrls[ii]!);

                    if (string.Equals(
                        uri.Host,
                        "localhost",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        uri.Host = computerName;
                    }

                    registeredUrls.Add(uri.ToString());
                }
                m_registrationInfo.DiscoveryUrls = registeredUrls;

                // build list of registration endpoints.
                m_registrationEndpoints = new ConfiguredEndpointCollection(configuration);

                EndpointDescription? endpoint = configuration.ServerConfiguration
                    .RegistrationEndpoint;

                if (endpoint == null)
                {
                    endpoint = new EndpointDescription
                    {
                        EndpointUrl = Utils.Format(Utils.DiscoveryUrls[0], "localhost"),
                        SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(
                            MessageSecurityMode.SignAndEncrypt,
                            SecurityPolicies.Basic256Sha256,
                            m_logger),
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    };
                    endpoint.Server.ApplicationType = ApplicationType.DiscoveryServer;
                }

                m_registrationEndpoints.Add(endpoint);

                m_registeredWithDiscoveryServer = false;
                m_minRegistrationInterval = 1000;
                m_lastRegistrationInterval = m_minRegistrationInterval;
            }
        }

        /// <summary>
        /// Starts discovery registration after the initial NodeManager readiness callbacks have completed.
        /// </summary>
        public void StartAfterReadiness()
        {
            lock (m_lock)
            {
                if (m_registrationInfo is null)
                {
                    throw new InvalidOperationException("Server registration information is not initialized.");
                }
                if (m_shutdownRequested)
                {
                    return;
                }

                m_registrationInfo.IsOnline = true;
                if (m_maxRegistrationInterval <= 0)
                {
                    return;
                }

                m_timer?.Dispose();
                m_timer = null;
                m_stopped = false;
                m_cts = new CancellationTokenSource();
                m_logger.ServerRegistrationTimerStarted();
                m_timer = m_timeProvider.CreateTimer(
                    OnRegisterServer,
                    ++m_generation,
                    TimeSpan.FromMilliseconds(m_minRegistrationInterval),
                    Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Registers the server with the discovery server, trying each registration
        /// endpoint (twice, alternating RegisterServer2 and RegisterServer).
        /// </summary>
        /// <returns>Whether the server was registered.</returns>
        public async ValueTask<bool> RegisterAsync(CancellationToken ct = default)
        {
            CertificateManager certificateManager = m_server.CertificateManager!;
            var configuration = new ApplicationConfiguration(m_configuration()!)
            {
                // share the server's CertificateManager so the registration channel uses
                // the same trust list, rejected store, and cached validation results.
                // The base copy ctor already propagates the legacy CertificateValidator.
                CertificateManager = certificateManager
            };

            // try each endpoint.
            if (m_registrationEndpoints != null)
            {
                foreach (ConfiguredEndpoint endpoint in m_registrationEndpoints.Endpoints)
                {
                    RegistrationClient? client = null;
                    int i = 0;

                    while (i++ < 2)
                    {
                        try
                        {
                            // update from the server.
                            bool updateRequired = true;

                            lock (m_lock)
                            {
                                updateRequired = endpoint.UpdateBeforeConnect;
                            }

                            if (updateRequired)
                            {
                                await endpoint.UpdateFromServerAsync(m_server.MessageContext.Telemetry, ct).ConfigureAwait(false);
                            }

                            lock (m_lock)
                            {
                                endpoint.UpdateBeforeConnect = false;
                            }

                            var requestHeader = new RequestHeader
                            {
                                Timestamp = m_timeProvider.GetUtcNow().UtcDateTime
                            };

                            // create the client. The registration channel takes
                            // ownership of the instance certificate handle and
                            // disposes it on close, so hand it an independent
                            // ref-counted handle (AddRef) and dispose the
                            // acquired entry here.
                            using CertificateEntry? instanceEntry =
                                certificateManager.AcquireApplicationCertificateBySecurityPolicy(
                                    endpoint.Description?.SecurityPolicyUri ??
                                    SecurityPolicies.None);
                            Certificate? instanceCertificate =
                                instanceEntry?.Certificate?.AddRef();
                            try
                            {
                                client = await RegistrationClient.CreateAsync(
                                    configuration,
                                    endpoint.Description!,
                                    endpoint.Configuration!,
                                    instanceCertificate!,
                                    ct: ct).ConfigureAwait(false);

                                // Ownership of the AddRef'd handle has transferred
                                // to the registration channel, which disposes it
                                // when the channel closes.
                                instanceCertificate = null;
                            }
                            finally
                            {
                                instanceCertificate?.Dispose();
                            }

                            client.OperationTimeout = 10000;

                            // register the server.
                            if (m_useRegisterServer2)
                            {
                                var mdnsDiscoveryConfig = new MdnsDiscoveryConfiguration
                                {
                                    ServerCapabilities = configuration.ServerConfiguration!
                                        .ServerCapabilities,
                                    MdnsServerName = Utils.GetHostName()
                                };
                                var extensionObject = new ExtensionObject(mdnsDiscoveryConfig);
                                ArrayOf<ExtensionObject> discoveryConfiguration = [extensionObject];
                                await client.RegisterServer2Async(
                                    requestHeader,
                                    m_registrationInfo,
                                    discoveryConfiguration,
                                    ct).ConfigureAwait(false);
                            }
                            else
                            {
                                await client.RegisterServerAsync(
                                    requestHeader,
                                    m_registrationInfo,
                                    ct)
                                    .ConfigureAwait(false);
                            }

                            m_registeredWithDiscoveryServer = m_registrationInfo!.IsOnline;
                            return true;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            m_logger.RegisterServerApiFailedForEndpointUrlException(
                                m_useRegisterServer2 ? "2" : string.Empty,
                                endpoint.EndpointUrl,
                                e.Message);
                            m_useRegisterServer2 = !m_useRegisterServer2;
                        }
                        finally
                        {
                            if (client != null)
                            {
                                try
                                {
                                    await client.CloseAsync(ct).ConfigureAwait(false);
                                }
                                catch (Exception e)
                                {
                                    m_logger.NotCleanlyCloseConnectionWithLDSException(e.Message);
                                }
                                finally
                                {
                                    client.Dispose();
                                    client = null;
                                }
                            }
                        }
                    }
                }
                // retry to start with RegisterServer2 if both failed
                m_useRegisterServer2 = true;
            }

            m_registeredWithDiscoveryServer = false;
            return false;
        }

        /// <summary>
        /// Takes the server offline at the discovery server, if it is registered there
        /// (the server is shutting down).
        /// </summary>
        public async ValueTask UnregisterAsync(CancellationToken cancellationToken)
        {
            if (m_maxRegistrationInterval > 0 && m_registeredWithDiscoveryServer)
            {
                m_registrationInfo!.IsOnline = false;
                await RegisterAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Prevents registration from starting until <see cref="ClearShutdownRequest"/>.
        /// </summary>
        public void RequestShutdown()
        {
            lock (m_lock)
            {
                m_shutdownRequested = true;
            }
        }

        /// <summary>
        /// Prevents registration rearming, cancels the active attempt and drains it before releasing cancellation
        /// state.
        /// </summary>
        public async ValueTask StopAsync()
        {
            CancellationTokenSource? cancellation;
            Task? registration;
            lock (m_lock)
            {
                m_stopped = true;
                m_generation++;
                m_timer?.Dispose();
                m_timer = null;
                cancellation = m_cts;
                m_cts = null;
                registration = m_task;
            }

            var failures = new List<Exception>();
            try
            {
                await StandardServer.RunShutdownStageAsync(failures, () =>
                {
                    cancellation?.Cancel();
                    return default;
                }).ConfigureAwait(false);
                if (registration != null)
                {
                    await StandardServer.RunShutdownStageAsync(failures, () => new ValueTask(registration)).ConfigureAwait(false);
                }
            }
            finally
            {
                cancellation?.Dispose();
                lock (m_lock)
                {
                    if (ReferenceEquals(m_task, registration))
                    {
                        m_task = null;
                    }
                }
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Discovery registration shutdown failed.", failures);
            }
        }

        /// <summary>
        /// Registers the server endpoints with the LDS.
        /// </summary>
        /// <param name="state">The state.</param>
        private void OnRegisterServer(object? state)
        {
            long generation = (long)state!;
            lock (m_lock)
            {
                if (m_stopped ||
                    generation != m_generation ||
                    m_task is { IsCompleted: false })
                {
                    return;
                }
                m_timer?.Dispose();
                m_timer = null;
                m_task = m_workerFactory is { } factory
                    ? factory(m_cts!.Token)
                    : RegisterIterationAsync(generation, m_cts!.Token);
            }
        }

        /// <summary>
        /// Attempts discovery registration and schedules the next retry only for the active registration generation.
        /// </summary>
        private async Task RegisterIterationAsync(long generation, CancellationToken cancellationToken)
        {
            try
            {
                bool registered = await RegisterAsync(cancellationToken).ConfigureAwait(false);
                lock (m_lock)
                {
                    if (m_stopped ||
                        generation != m_generation ||
                        cancellationToken.IsCancellationRequested ||
                        m_maxRegistrationInterval <= 0)
                    {
                        return;
                    }
                    int delay;
                    if (registered)
                    {
                        delay = m_maxRegistrationInterval;
                        m_lastRegistrationInterval = m_minRegistrationInterval;
                        m_logger.RegisterServerSucceededRegisteringAgainInRegistrationInterval(delay);
                    }
                    else
                    {
                        delay = (int)Math.Min((long)m_lastRegistrationInterval * 2, m_maxRegistrationInterval);
                        m_lastRegistrationInterval = delay;
                        m_logger.RegisterServerFailedTryingAgainInRegistrationInterval(delay);
                    }
                    m_timer = m_timeProvider.CreateTimer(
                        OnRegisterServer,
                        ++m_generation,
                        TimeSpan.FromMilliseconds(delay),
                        Timeout.InfiniteTimeSpan);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The registration owner is stopping.
            }
            catch (Exception e)
            {
                m_logger.UnexpectedExceptionHandlingRegistrationTimer(e);
            }
        }

        private readonly Lock m_lock = new();
        private readonly ServerBase m_server;
        private readonly Func<ApplicationConfiguration?> m_configuration;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;

        /// <summary>
        /// Replaces a registration attempt when set (tests).
        /// </summary>
        private readonly Func<CancellationToken, Task>? m_workerFactory;
        private ConfiguredEndpointCollection? m_registrationEndpoints;
        private RegisteredServer? m_registrationInfo;
        private ITimer? m_timer;

        /// <summary>
        /// Cancels the active registration attempt.
        /// </summary>
        private CancellationTokenSource? m_cts;

        /// <summary>
        /// The active registration attempt.
        /// </summary>
        private Task? m_task;

        /// <summary>
        /// The generation of the armed timer; stale timer callbacks and attempts do nothing.
        /// </summary>
        private long m_generation;

        /// <summary>
        /// Set while the server shuts down, so readiness cannot start registration.
        /// </summary>
        private bool m_shutdownRequested;

        /// <summary>
        /// Set while registration is stopped, so attempts do not re-arm the timer.
        /// </summary>
        private bool m_stopped = true;
        private int m_minRegistrationInterval;
        private int m_maxRegistrationInterval;
        private int m_lastRegistrationInterval;
        private bool m_registeredWithDiscoveryServer;
        private bool m_useRegisterServer2;
    }
}
