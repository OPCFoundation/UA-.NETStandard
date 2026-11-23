// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Default lock-free <see cref="IPubSubCaptureRegistry"/>. Reads on the
    /// transport hot path are a single <see cref="Volatile"/> read; installs
    /// / clears use <see cref="Interlocked"/> so at most one observer is ever
    /// active.
    /// </summary>
    public sealed class PubSubCaptureRegistry : IPubSubCaptureRegistry
    {
        /// <inheritdoc/>
        public IPubSubCaptureObserver? CurrentObserver => Volatile.Read(ref m_observer);

        /// <inheritdoc/>
        public void SetObserver(IPubSubCaptureObserver observer)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            Volatile.Write(ref m_observer, observer);
        }

        /// <inheritdoc/>
        public bool TryClearObserver(IPubSubCaptureObserver observer)
        {
            if (observer is null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            return ReferenceEquals(
                Interlocked.CompareExchange(ref m_observer, null, observer),
                observer);
        }

        private IPubSubCaptureObserver? m_observer;
    }
}
