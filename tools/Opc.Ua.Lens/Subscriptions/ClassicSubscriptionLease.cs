/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace UaLens.Subscriptions;

/// <summary>
/// Owns registration/creation cleanup until a document adopts a classic subscription.
/// </summary>
internal sealed class ClassicSubscriptionLease : IAsyncDisposable
{
    public ClassicSubscriptionLease(ISession session, Subscription subscription)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
    }

    public Subscription Transfer()
    {
        Subscription subscription = m_subscription
            ?? throw new InvalidOperationException("Subscription ownership has already been transferred.");
        m_subscription = null;
        return subscription;
    }

    public async ValueTask DisposeAsync()
    {
        if (m_subscription is null)
        {
            return;
        }
        try
        {
            await m_session.RemoveSubscriptionsAsync([m_subscription], CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            m_subscription.Dispose();
            m_subscription = null;
        }
    }

    private readonly ISession m_session;
    private Subscription? m_subscription;
}
