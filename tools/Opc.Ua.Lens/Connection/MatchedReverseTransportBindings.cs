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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace UaLens.Connection;

/// <summary>
/// ReverseConnectManager matches ServerUri OR endpoint authority. Filter before
/// that manager sees/claims a ReverseHello so UaLens requires BOTH the expected
/// ServerUri and complete endpoint URL, including its case-sensitive path.
/// </summary>
internal sealed class MatchedReverseTransportBindings : ITransportBindingRegistry
{
    public MatchedReverseTransportBindings(ITransportBindingRegistry inner, ReverseConnectionProfile profile)
    {
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        m_profile = profile ?? throw new ArgumentNullException(nameof(profile));
        profile.Validate();
    }

    public ITransportListener? CreateListener(string uriScheme, ITelemetryContext telemetry)
    {
        ITransportListener? listener = m_inner.CreateListener(uriScheme, telemetry);
        return listener is null ? null : new MatchedReverseListener(listener, m_profile);
    }

    public ITransportChannel? CreateChannel(string uriScheme, ITelemetryContext telemetry)
    {
        return m_inner.CreateChannel(uriScheme, telemetry);
    }

    public ITransportListenerFactory? GetListenerFactory(string uriScheme)
    {
        return m_inner.GetListenerFactory(uriScheme);
    }

    public ITransportChannelFactory? GetChannelFactory(string uriScheme)
    {
        return m_inner.GetChannelFactory(uriScheme);
    }

    public bool HasListenerFactory(string uriScheme)
    {
        return m_inner.HasListenerFactory(uriScheme);
    }

    public bool HasChannelFactory(string uriScheme)
    {
        return m_inner.HasChannelFactory(uriScheme);
    }

    public void RegisterListenerFactory(ITransportListenerFactory factory)
    {
        throw new InvalidOperationException(
            "Change transports through trusted host configuration, not a live listener.");
    }

    public void RegisterChannelFactory(ITransportChannelFactory factory)
    {
        throw new InvalidOperationException(
            "Change transports through trusted host configuration, not a live listener.");
    }

    public bool RemoveListenerFactory(string uriScheme)
    {
        throw new InvalidOperationException("The active reverse transport registry is read-only.");
    }

    public bool RemoveChannelFactory(string uriScheme)
    {
        throw new InvalidOperationException("The active reverse transport registry is read-only.");
    }

    private readonly ITransportBindingRegistry m_inner;
    private readonly ReverseConnectionProfile m_profile;
}

internal sealed class MatchedReverseListener : ITransportListener
{
    public MatchedReverseListener(ITransportListener inner, ReverseConnectionProfile profile)
    {
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        m_profile = profile ?? throw new ArgumentNullException(nameof(profile));
        profile.Validate();
        inner.ConnectionWaiting += OnConnectionWaitingAsync;
    }

    public string ListenerId => m_inner.ListenerId;

    public string UriScheme => m_inner.UriScheme;

    public event ConnectionWaitingHandlerAsync? ConnectionWaiting;

    public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged
    {
        add => m_inner.ConnectionStatusChanged += value;
        remove => m_inner.ConnectionStatusChanged -= value;
    }

    public ValueTask OpenAsync(
        Uri baseAddress,
        TransportListenerSettings settings,
        ITransportListenerCallback callback,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed), this);
        if (!ConnectionProfile.EndpointUrlsMatch(baseAddress.AbsoluteUri, m_profile.ListenerUrl))
        {
            throw new InvalidOperationException(
                "The reverse listener address differs from the explicitly selected address.");
        }
        if (!settings.ReverseConnectListener ||
            (baseAddress.Scheme is "wss" or "opc.wss" &&
                (settings.ServerCertificates is null || settings.CertificateValidator is null)))
        {
            throw new InvalidOperationException(
                "The selected binding requires reverse-listener mode " +
                "and explicit WSS TLS certificates and validation.");
        }
        return m_inner.OpenAsync(baseAddress, settings, callback, ct);
    }

    public ValueTask CloseAsync(CancellationToken ct = default)
    {
        return m_inner.CloseAsync(ct);
    }

    public void CertificateUpdate(ICertificateValidatorEx validator, ICertificateRegistry serverCertificates)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(serverCertificates);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed), this);
        m_inner.CertificateUpdate(validator, serverCertificates);
    }

    public void CreateReverseConnection(Uri url, int timeout)
    {
        throw new InvalidOperationException("The client reverse listener cannot initiate outbound server connections.");
    }

    public void UpdateChannelLastActiveTime(string globalChannelId)
    {
        m_inner.UpdateChannelLastActiveTime(globalChannelId);
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? started = null;
        Task disposal;
        lock (m_gate)
        {
            if (m_disposal is null)
            {
                m_disposed = true;
                started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposal = DisposeCoreAsync(started.Task);
            }
            disposal = m_disposal;
        }
        started?.TrySetResult();
        return new ValueTask(disposal);
    }

    private async Task OnConnectionWaitingAsync(object sender, ConnectionWaitingEventArgs args)
    {
        args.Accepted = false;
        if (Volatile.Read(ref m_disposed) || !m_profile.MatchesPeer(args.ServerUri, args.EndpointUrl))
        {
            return;
        }
        ConnectionWaitingHandlerAsync? handlers = ConnectionWaiting;
        if (handlers is null)
        {
            return;
        }
        bool completed = false;
        try
        {
            foreach (ConnectionWaitingHandlerAsync handler in handlers.GetInvocationList())
            {
                await handler(this, args).ConfigureAwait(false);
            }
            completed = true;
        }
        finally
        {
            if (!completed || Volatile.Read(ref m_disposed))
            {
                args.Accepted = false;
            }
        }
    }

    private async Task DisposeCoreAsync(Task started)
    {
        await started.ConfigureAwait(false);
        m_inner.ConnectionWaiting -= OnConnectionWaitingAsync;
        await m_inner.DisposeAsync().ConfigureAwait(false);
    }

    private readonly ITransportListener m_inner;
    private readonly ReverseConnectionProfile m_profile;
    private readonly Lock m_gate = new();
    private Task? m_disposal;
    private bool m_disposed;
}
