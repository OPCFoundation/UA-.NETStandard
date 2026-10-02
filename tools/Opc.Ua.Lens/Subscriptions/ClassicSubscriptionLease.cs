/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
