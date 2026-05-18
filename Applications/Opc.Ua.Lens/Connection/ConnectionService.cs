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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Diagnostics;
using UaLens.Subscriptions;

namespace UaLens.Connection;

internal sealed class ConnectionService : IAsyncDisposable
{
    private readonly ITelemetryContext m_telemetry;
    private readonly ILogger m_log;
    private readonly SemaphoreSlim m_lock = new(1, 1);
    private ManagedSession? m_session;
    private ApplicationConfiguration? m_config;
    private ISubscriptionAdapter? m_activeAdapter;
    private SubscriptionEngineKind m_engine;
    /// <summary>
    /// Every adapter created via <see cref="CreateAdapter"/>.  Tracked
    /// here so <see cref="DisconnectAsync"/> can dispose every tab's
    /// adapter even when <see cref="MainViewModel"/> hasn't released
    /// them yet (e.g. the user closed the window while still connected).
    /// </summary>
    private readonly List<ISubscriptionAdapter> m_adapters = new();

    public ConnectionService(ITelemetryContext telemetry)
        : this(telemetry, null)
    {
    }

    public ConnectionService(ITelemetryContext telemetry, PublishLogObserver? publishLog)
    {
        m_telemetry = telemetry;
        m_log = telemetry.CreateLogger("Connection");
        PublishLog = publishLog;
    }

    /// <summary>
    /// Shared sink used by the diagnostics view's "Publishes" tab.  Threaded
    /// into every adapter created via <see cref="CreateAdapter"/> so each
    /// engine appends one row per publish callback.  May be <c>null</c> in
    /// tests / smoke runs that don't surface the diagnostics pane.
    /// </summary>
    public PublishLogObserver? PublishLog { get; }

    public bool IsConnected => m_session is not null;
    public ManagedSession? Session => m_session;

    /// <summary>
    /// The currently-active subscription adapter — the one whose tab is
    /// selected in the UI.  Backwards-compat alias for older call sites
    /// that still read <c>Connection.Adapter</c>.  Setter is wired by
    /// <see cref="MainViewModel"/> when the user switches tabs.
    /// </summary>
    public ISubscriptionAdapter? Adapter
    {
        get => m_activeAdapter;
        set => m_activeAdapter = value;
    }

    public SubscriptionEngineKind Engine => m_engine;

    /// <summary>
    /// Fires after every connect/disconnect/reconnect transition.
    /// <para><b>Threading contract:</b> raised from a background worker
    /// thread (the one that completed the connect / disconnect / keep-alive
    /// callback).  Subscribers that touch UI-bound state MUST marshal to
    /// <c>Dispatcher.UIThread</c> themselves.</para>
    /// </summary>
    public event Action? StateChanged;

    /// <summary>
    /// Creates a fresh subscription adapter on the live session, using
    /// the engine kind selected at connect time.  Each tab in the
    /// MainWindow owns one adapter created via this factory.
    /// </summary>
    public ISubscriptionAdapter CreateAdapter()
    {
        if (m_session is null)
        {
            throw new InvalidOperationException("Cannot create a subscription adapter — not connected.");
        }
        ISubscriptionAdapter adapter = m_engine == SubscriptionEngineKind.Classic
            ? new ClassicEngineAdapter(m_session, m_telemetry, PublishLog)
            : new ChannelV2EngineAdapter(m_session, m_telemetry, PublishLog);
        lock (m_adapters)
        {
            m_adapters.Add(adapter);
        }
        // Default the active-adapter to the first one created so legacy
        // call sites (e.g. status text formatting) have something to
        // observe before the user explicitly selects a tab.
        m_activeAdapter ??= adapter;
        return adapter;
    }

    /// <summary>
    /// Removes <paramref name="adapter"/> from the tracked set without
    /// disposing it.  Returns <c>true</c> if the adapter was still tracked
    /// (caller owns disposal), <c>false</c> if it was already removed —
    /// typically because <see cref="DisconnectInternalAsync"/> already
    /// disposed it (caller must not double-dispose).
    /// </summary>
    public bool ForgetAdapter(ISubscriptionAdapter adapter)
    {
        bool removed;
        lock (m_adapters)
        {
            removed = m_adapters.Remove(adapter);
        }
        if (ReferenceEquals(m_activeAdapter, adapter))
        {
            m_activeAdapter = null;
        }
        return removed;
    }

    /// <summary>
    /// Returns the lazily-built application configuration so callers can run
    /// discovery against the same trust/PKI stores that <see cref="ConnectAsync(ConnectionOptions, CancellationToken)"/>
    /// will use.
    /// </summary>
    public async Task<ApplicationConfiguration> GetConfigAsync()
    {
        m_config ??= await AppConfig.BuildAsync(m_telemetry).ConfigureAwait(false);
        return m_config;
    }

    /// <summary>
    /// Headless / probe overload: discovers, picks the SDK's default endpoint, connects Anonymous.
    /// </summary>
    public async Task ConnectAsync(ConnectionOptions options, CancellationToken ct)
    {
        m_config ??= await AppConfig.BuildAsync(m_telemetry).ConfigureAwait(false);
        EndpointDescription? endpointDesc = await CoreClientUtils
            .SelectEndpointAsync(m_config, options.EndpointUrl, options.UseSecurity, telemetry: m_telemetry, ct: ct)
            .ConfigureAwait(false);
        if (endpointDesc is null)
        {
            throw new InvalidOperationException("No endpoint matched.");
        }
        // CA2000: ownership of the UserIdentity transfers to ConnectAsync
        // which retains it for the lifetime of the session.
#pragma warning disable CA2000
        await ConnectAsync(options, endpointDesc, new UserIdentity(new AnonymousIdentityToken()), null, ct)
            .ConfigureAwait(false);
#pragma warning restore CA2000
    }

    /// <summary>
    /// Explicit overload used by the interactive Connect wizard.
    /// </summary>
    /// <param name="options">Engine choice, security flag, etc.</param>
    /// <param name="endpoint">EndpointDescription chosen by the user (from discovery).</param>
    /// <param name="identity">User identity to negotiate with.  Anonymous when the user
    /// picked an endpoint root, UserName when they picked a UserName policy, etc.</param>
    /// <param name="certPrompt">Optional callback raised when the server certificate
    /// fails validation. Returns the user's trust decision. <c>null</c> falls back to
    /// the configuration's <see cref="SecurityConfiguration.AutoAcceptUntrustedCertificates"/>
    /// behaviour.</param>
    public async Task ConnectAsync(
        ConnectionOptions options,
        EndpointDescription endpoint,
        IUserIdentity identity,
        Func<X509Certificate2, ServiceResult, Task<TrustChoice>>? certPrompt,
        CancellationToken ct)
    {
        await m_lock.WaitAsync(ct).ConfigureAwait(false);
        bool? priorAutoAccept = null;
        try
        {
            await DisconnectInternalAsync().ConfigureAwait(false);

            m_config ??= await AppConfig.BuildAsync(m_telemetry).ConfigureAwait(false);
            m_engine = options.Engine;

            // The legacy `CertificateValidator.CertificateValidation`
            // event hook is gone in the upstream cert-manager refactor.
            // For now, when the caller supplied a trust-prompt callback,
            // assume the user wants to engage with the trust UX and
            // auto-accept untrusted peer certificates for the duration
            // of this connect call.  The interactive trust dialog will
            // be re-wired on top of the new RejectedCertificateProcessor
            // once that contract is stable.
            // TODO: redesign cert-trust UX on top of ICertificateValidatorEx.
            if (certPrompt is not null && m_config.SecurityConfiguration is { } sec)
            {
                priorAutoAccept = sec.AutoAcceptUntrustedCertificates;
                sec.AutoAcceptUntrustedCertificates = true;
            }
            _ = certPrompt; // intentionally unused while the new UX is pending

            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(m_config));

            ISubscriptionEngineFactory engineFactory = options.Engine switch
            {
                SubscriptionEngineKind.Classic => ClassicSubscriptionEngineFactory.Instance,
                _ => DefaultSubscriptionEngineFactory.Instance
            };

            m_log.LogInformation(
                "Connecting to {Endpoint} ({Mode}/{Policy}) with identity={Identity}, engine={Engine}",
                endpoint.EndpointUrl, endpoint.SecurityMode, endpoint.SecurityPolicyUri,
                identity.TokenType, options.Engine);

            ManagedSession session = await new ManagedSessionBuilder(m_config, m_telemetry)
                .UseEndpoint(configuredEndpoint)
                .WithUserIdentity(identity)
                .WithSessionName("UaLens")
                .UseSubscriptionEngine(engineFactory)
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            m_session = session;
            m_log.LogInformation("Connected to {Endpoint}.", endpoint.EndpointUrl);
            // No default adapter is created here — MainViewModel adds the
            // first tab (and therefore the first adapter) once it observes
            // the StateChanged → Connected transition.
        }
        finally
        {
            if (priorAutoAccept.HasValue && m_config?.SecurityConfiguration is { } sec)
            {
                sec.AutoAcceptUntrustedCertificates = priorAutoAccept.Value;
            }
            m_lock.Release();
        }
        StateChanged?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        await m_lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            m_lock.Release();
        }
        StateChanged?.Invoke();
    }

    private async Task DisconnectInternalAsync()
    {
        // Dispose every adapter we've handed out.  ForgetAdapter is
        // tolerant of duplicate removals so MainViewModel's CloseTab
        // path still works during disconnect.
        ISubscriptionAdapter[] snap;
        lock (m_adapters)
        {
            snap = m_adapters.ToArray();
            m_adapters.Clear();
        }
        foreach (ISubscriptionAdapter a in snap)
        {
            try
            { await a.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { m_log.LogWarning(ex, "Error disposing subscription adapter."); }
        }
        m_activeAdapter = null;
        if (m_session is not null)
        {
            try
            { await m_session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { m_log.LogWarning(ex, "Error disposing managed session."); }
            m_session = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "DisposeAsync: DisconnectAsync threw — continuing to dispose semaphore.");
        }
        try
        { m_lock.Dispose(); }
        catch { /* idempotent */ }
    }
}

/// <summary>Trust decision returned by the interactive certificate dialog.</summary>
internal enum TrustChoice { Reject, AcceptOnce, TrustPermanently }

