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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Configuration;
#if NET10_0_OR_GREATER
using Opc.Ua.Pcap.Capture;
#endif
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Quickstarts
{
    public class UAServer<T> where T : StandardServer
    {
        public IApplicationInstance Application { get; private set; } = null!;

        public ApplicationConfiguration Configuration => Application.ApplicationConfiguration!;

        public bool AutoAccept { get; set; }

        public char[]? Password { get; set; }

        public ExitCode ExitCode { get; private set; }

        public T? Server { get; private set; }

        /// <summary>
        /// Ctor of the server.
        /// </summary>
        /// <param name="telemetry">The telemetry context.</param>
        public UAServer(ITelemetryContext telemetry, Func<ITelemetryContext, T> factory)
        {
            m_factory = factory;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<UAServer<T>>();
        }

        /// <summary>
        /// Load the application configuration.
        /// </summary>
        /// <exception cref="ErrorExitException"></exception>
        public async Task LoadAsync(string applicationName, string configSectionName)
        {
            try
            {
                ExitCode = ExitCode.ErrorNotStarted;

                ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
                var passwordProvider = new CertificatePasswordProvider(Password);
                Application = new ApplicationInstance(m_telemetry)
                {
                    ApplicationName = applicationName,
                    ApplicationType = ApplicationType.Server,
                    ConfigSectionName = configSectionName,
                    CertificatePasswordProvider = passwordProvider
                };

                // load the application configuration.
                await Application.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode);
            }
        }

        /// <summary>
        /// Load the application configuration.
        /// </summary>
        /// <exception cref="ErrorExitException"></exception>
        public async Task CheckCertificateAsync(bool renewCertificate)
        {
            try
            {
                ApplicationConfiguration config = Application.ApplicationConfiguration!;
                if (renewCertificate)
                {
                    await Application.DeleteApplicationInstanceCertificateAsync().ConfigureAwait(false);
                }

                // check the application certificate.
                bool haveAppCertificate = await Application
                    .CheckApplicationInstanceCertificatesAsync(false)
                    .ConfigureAwait(false);
                if (!haveAppCertificate)
                {
                    throw new ErrorExitException("Application instance certificate invalid!");
                }

                if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    config.CertificateManager.AcceptError = AcceptCertificate;
                }
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode);
            }
        }

        /// <summary>
        /// Create server instance and add node managers.
        /// </summary>
        /// <exception cref="ErrorExitException"></exception>
        public void Create(ArrayOf<INodeManagerFactory> nodeManagerFactories)
        {
            try
            {
                // create the server.
                Server = CreateServerWithTransportBindings();
                foreach (INodeManagerFactory factory in nodeManagerFactories)
                {
                    Server.AddNodeManager(factory);
                }
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode);
            }
        }

        /// <summary>
        /// Creates the server instance and assigns it a transport binding
        /// registry (see <see cref="CreateTransportBindings"/>). Shared by
        /// <see cref="Create"/> and the lazy-creation fallback in
        /// <see cref="StartAsync"/> so both call sites stay in sync.
        /// </summary>
        private T CreateServerWithTransportBindings()
        {
            T server = m_factory(m_telemetry);
            // The reference server configuration files list opc.https / opc.wss
            // base addresses. Register the matching transport bindings (this
            // project references Opc.Ua.Bindings.Https) so those endpoints
            // resolve to a registered listener instead of failing to start.
            server.TransportBindings = CreateTransportBindings();
            return server;
        }

        /// <summary>
        /// Builds the transport binding registry used by the server. Reuses
        /// the production <c>AddOpcTcpTransport()</c> / <c>AddHttpsTransport()</c> /
        /// <c>AddWssTransport()</c> DI registrations (this project references
        /// <c>Opc.Ua.Bindings.Https</c>) so base addresses configured with the
        /// <c>opc.tcp</c>/<c>opc.https</c>/<c>https</c>/<c>opc.wss</c>/<c>wss</c>
        /// schemes resolve to a registered transport instead of relying on a
        /// hand-rolled duplicate of that registration logic. The backing
        /// <see cref="ServiceProvider"/> is kept alive in <see cref="m_transportServices"/>
        /// for as long as the registry is in use and disposed in <see cref="StopAsync"/>.
        /// </summary>
        private ITransportBindingRegistry CreateTransportBindings()
        {
            // Guard against leaking a previously built provider if Create()/StartAsync()
            // are invoked again without an intervening StopAsync().
            m_transportServices?.Dispose();

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddOpcTcpTransport()
                .AddHttpsTransport()
                .AddWssTransport();
            m_transportServices = services.BuildServiceProvider();
            return m_transportServices.GetRequiredService<ITransportBindingRegistry>();
        }

        /// <summary>
        /// Start the server.
        /// </summary>
        /// <exception cref="ErrorExitException"></exception>
        public async Task StartAsync(CancellationToken ct = default)
        {
            try
            {
                // create the server.
                Server ??= CreateServerWithTransportBindings();

#if NET10_0_OR_GREATER
                // Opt-in diagnostics: when OPCUA_PCAP_FILE / OPCUA_KEYLOGFILE are
                // set, install server-side pcap capture into the transport
                // bindings before the listeners open. Mirrors the client env-var
                // path and honours the same variables; a complete no-op (nothing
                // installed, no files written) when the variables are unset.
                // The pcap capture package targets net8.0+ only.
                m_pcapCapture = await PcapServerCapture.TryStartFromEnvironmentAsync(
                    Server!.TransportBindings,
                    m_telemetry.LoggerFactory,
                    ct).ConfigureAwait(false);
#endif

                // start the server
                await Application.StartAsync(Server, ct).ConfigureAwait(false);

                // save state
                ExitCode = ExitCode.ErrorRunning;

                // print endpoint info
                foreach (string? endpoint in Application.Server!
                    .GetEndpoints()
                    .ConvertAll(e => e.EndpointUrl)
                    .ToList()
                    .Distinct())
                {
                    Console.WriteLine(endpoint);
                }

                // start the status thread
                m_status = Task.Run(StatusThreadAsync, default);

                // print notification on session events
                Server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
                Server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
                Server.CurrentInstance.SessionManager.SessionCreated += EventStatus;
            }
            catch (Exception ex)
            {
                try
                {
                    await DisposeTransportBindingsAsync().ConfigureAwait(false);
                }
                finally
                {
                    throw new ErrorExitException(ex.Message, ExitCode);
                }
            }
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        /// <exception cref="ErrorExitException"></exception>
        public async Task StopAsync(CancellationToken ct = default)
        {
            try
            {
                if (Server != null)
                {
                    using T server = Server;
                    // Stop status thread which monitores the server property.
                    Server = null;
                    await m_status.ConfigureAwait(false);

                    // Stop server and dispose
                    await server.StopAsync(ct).ConfigureAwait(false);
                }

                ExitCode = ExitCode.Ok;
            }
            catch (Exception ex)
            {
                throw new ErrorExitException(ex.Message, ExitCode.ErrorStopping);
            }
            finally
            {
                await DisposeTransportBindingsAsync().ConfigureAwait(false);
            }
        }

        private async Task DisposeTransportBindingsAsync()
        {
#if NET10_0_OR_GREATER
            try
            {
                if (m_pcapCapture != null)
                {
                    await m_pcapCapture.DisposeAsync().ConfigureAwait(false);
                    m_pcapCapture = null;
                }
            }
            finally
#endif
            {
                m_transportServices?.Dispose();
                m_transportServices = null;
            }
        }

        /// <summary>
        /// Per-error accept callback used when AutoAcceptUntrustedCertificates is false.
        /// </summary>
        private bool AcceptCertificate(Certificate certificate, ServiceResult error)
        {
            if (error.StatusCode == StatusCodes.BadCertificateUntrusted && AutoAccept)
            {
                m_logger.AcceptedCertificate(
                    certificate.Subject,
                    certificate.Thumbprint);
                return true;
            }
            m_logger.RejectedCertificate(
                error,
                certificate.Subject,
                certificate.Thumbprint);
            return false;
        }

        /// <summary>
        /// Update the session status.
        /// </summary>
        private void EventStatus(ISession session, SessionEventReason reason)
        {
            m_lastEventTime = DateTime.UtcNow;
            LogSessionStatusFull(session, reason.ToString());
        }

        /// <summary>
        /// Output the status of a connected session.
        /// </summary>
        private void LogSessionStatusLastContact(ISession session, string reason)
        {
            if (!m_logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            // Snapshot the fields under the session's lock, then log outside it.
            (string? name, DateTime lastContact) = session.ReadDiagnostics(
                diagnostics => (
                    diagnostics.SessionName,
                    (DateTime)diagnostics.ClientLastContactTime));

            m_logger.SessionStatusLastContact(reason, name, lastContact.ToLocalTime());
        }

        /// <summary>
        /// Output the status of a connected session.
        /// </summary>
        private void LogSessionStatusFull(ISession session, string reason)
        {
            if (!m_logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            (string? name, DateTime lastContact) = session.ReadDiagnostics(
                diagnostics => (
                    diagnostics.SessionName,
                    (DateTime)diagnostics.ClientLastContactTime));

            m_logger.SessionStatusFull(
                reason,
                name,
                lastContact.ToLocalTime(),
                session.Identity?.DisplayName ?? "Anonymous",
                session.Id);
        }

        /// <summary>
        /// Status thread, prints connection status every 10 seconds.
        /// </summary>
        private async Task StatusThreadAsync()
        {
            while (Server != null)
            {
                if (DateTime.UtcNow - m_lastEventTime > TimeSpan.FromMilliseconds(10000))
                {
                    IList<ISession> sessions = Server.CurrentInstance.SessionManager.GetSessions();
                    for (int ii = 0; ii < sessions.Count; ii++)
                    {
                        ISession session = sessions[ii];
                        LogSessionStatusLastContact(session, "-Status-");
                    }
                    m_lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A dialog which asks for user input.
        /// </summary>
        public class ApplicationMessageDlg : IApplicationMessageDlg
        {
            private readonly TextWriter m_output;
            private string m_message = string.Empty;
            private bool m_ask;

            public ApplicationMessageDlg(TextWriter? output = null)
            {
                m_output = output ?? Console.Out;
            }

            public override void Message(string text, bool ask)
            {
                m_message = text;
                m_ask = ask;
            }

            public override async Task<bool> ShowAsync()
            {
                if (m_ask)
                {
                    var message = new StringBuilder(m_message);
                    message.Append(" (y/n, default y): ");
                    m_output.Write(message.ToString());

                    try
                    {
                        ConsoleKeyInfo result = Console.ReadKey();
                        m_output.WriteLine();
                        return await Task.FromResult(result.KeyChar is 'y' or 'Y' or '\r')
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // intentionally fall through
                    }
                }
                else
                {
                    m_output.WriteLine(m_message);
                }

                return await Task.FromResult(true).ConfigureAwait(false);
            }
        }

        private readonly Func<ITelemetryContext, T> m_factory;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private Task m_status = Task.CompletedTask;
        private DateTime m_lastEventTime;
        private ServiceProvider? m_transportServices;
#if NET10_0_OR_GREATER
        private IAsyncDisposable? m_pcapCapture;
#endif
    }

    internal static partial class UAServerLog
    {
        [LoggerMessage(EventId = ConsoleReferenceServerEventIds.UAServer + 0, Level = LogLevel.Information,
            Message = "Accepted Certificate: [{Subject}] [{Thumbprint}]")]
        public static partial void AcceptedCertificate(this ILogger logger, string subject, string thumbprint);

        [LoggerMessage(EventId = ConsoleReferenceServerEventIds.UAServer + 1, Level = LogLevel.Information,
            Message = "Rejected Certificate: {Error} [{Subject}] [{Thumbprint}]")]
        public static partial void RejectedCertificate(
            this ILogger logger,
            ServiceResult error,
            string subject,
            string thumbprint);

        [LoggerMessage(EventId = ConsoleReferenceServerEventIds.UAServer + 2, Level = LogLevel.Information,
            Message = "{Reason,9}:{Session,20}:Last Event:{LastContactTime:HH:mm:ss}")]
        public static partial void SessionStatusLastContact(
            this ILogger logger,
            string reason,
            string? session,
            DateTime lastContactTime);

        [LoggerMessage(EventId = ConsoleReferenceServerEventIds.UAServer + 3, Level = LogLevel.Information,
            Message = "{Reason,9}:{Session,20}:Last Event:{LastContactTime:HH:mm:ss}:{UserIdentity,20}:{SessionId}")]
        public static partial void SessionStatusFull(
            this ILogger logger,
            string reason,
            string? session,
            DateTime lastContactTime,
            string userIdentity,
            NodeId sessionId);
    }

}
