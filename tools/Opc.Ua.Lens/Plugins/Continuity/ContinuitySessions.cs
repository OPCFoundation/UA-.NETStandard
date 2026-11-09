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
using Opc.Ua.Client;
using UaLens.Connection;

namespace UaLens.Plugins.Continuity;

internal enum ContinuitySessionPurpose
{
    Source,
    Restore,
    RedundantTarget
}

/// <summary>
/// Explicitly configured lab-only session ownership. Implementations resolve the
/// same identity and security through the connection/provider owner. They must not
/// return the primary workspace session or broaden its trust decisions.
/// </summary>
internal interface IContinuitySessionFactory
{
    ContinuitySetup CheckSetup(ContinuityScenario scenario);

    Task<IContinuitySessionLease> OpenAsync(ContinuitySessionPurpose purpose, CancellationToken ct);
}

internal interface IContinuitySessionLease : IAsyncDisposable
{
    ISession Session { get; }

    Task CloseAsync(bool retainSubscriptions, CancellationToken ct);
}

/// <summary>
/// Typed configuration seam for an application's identity/trust owner. The supplied
/// opener must use the existing ManagedSession backend and a configured redundancy
/// provider for RedundantTarget; it is not an endpoint string from workspace JSON.
/// </summary>
internal sealed class ConfiguredContinuitySessionFactory : IContinuitySessionFactory
{
    public ConfiguredContinuitySessionFactory(
        Func<ContinuitySessionPurpose, CancellationToken, Task<IConnectionSession>> open,
        bool gracefulDurableSampleConfigured = false,
        bool redundantSetConfigured = false)
    {
        m_open = open ?? throw new ArgumentNullException(nameof(open));
        m_gracefulDurableSampleConfigured = gracefulDurableSampleConfigured;
        m_redundantSetConfigured = redundantSetConfigured;
    }

    public ContinuitySetup CheckSetup(ContinuityScenario scenario)
    {
        if (scenario == ContinuityScenario.GracefulDurableRestore && !m_gracefulDurableSampleConfigured)
        {
            return new(ContinuityAvailability.RequiresConfiguration,
                "Configure the repository durable-subscription sample and its store. " +
                "Quickstarts persists only graceful shutdowns and excludes issued-token subscriptions.");
        }
        if (scenario == ContinuityScenario.ConfiguredFailover && !m_redundantSetConfigured)
        {
            return new(ContinuityAvailability.RequiresConfiguration,
                "Configure a redundant set and an authorized target through the existing redundancy provider.");
        }
        return new(ContinuityAvailability.Supported,
            "Configured scenario-owned sessions; identity and trust remain the connection provider's responsibility.");
    }

    public async Task<IContinuitySessionLease> OpenAsync(ContinuitySessionPurpose purpose, CancellationToken ct)
    {
        if (purpose == ContinuitySessionPurpose.RedundantTarget && !m_redundantSetConfigured)
        {
            throw new InvalidOperationException("A configured redundant target is required.");
        }
        IConnectionSession connection = await m_open(purpose, ct).ConfigureAwait(false);
        if (connection.Session is not ManagedSession session)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("Continuity auxiliary sessions must use ManagedSession.");
        }
        return new ManagedContinuitySessionLease(connection, session);
    }

    private readonly Func<ContinuitySessionPurpose, CancellationToken, Task<IConnectionSession>> m_open;
    private readonly bool m_gracefulDurableSampleConfigured;
    private readonly bool m_redundantSetConfigured;
}

internal sealed class ManagedContinuitySessionLease : IContinuitySessionLease
{
    public ManagedContinuitySessionLease(IConnectionSession owner, ManagedSession session)
    {
        m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
        m_session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public ISession Session => m_session;

    public async Task CloseAsync(bool retainSubscriptions, CancellationToken ct)
    {
        bool wasConnected = m_session.Connected;
        m_session.DeleteSubscriptionsOnClose = !retainSubscriptions;
        StatusCode status = await m_session.CloseAsync(10000, closeChannel: true, ct).ConfigureAwait(false);
        if (StatusCode.IsBad(status))
        {
            throw new ServiceResultException(status);
        }
        if (!wasConnected)
        {
            throw new ServiceResultException(StatusCodes.BadNotConnected,
                "The auxiliary session was already disconnected; its server close outcome is unconfirmed.");
        }
    }

    public ValueTask DisposeAsync() => m_owner.DisposeAsync();

    private readonly IConnectionSession m_owner;
    private readonly ManagedSession m_session;
}
