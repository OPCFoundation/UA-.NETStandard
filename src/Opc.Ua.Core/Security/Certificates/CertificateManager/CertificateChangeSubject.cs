/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// A simple <see cref="IObservable{T}"/> implementation for
    /// certificate change events. Thread-safe. No System.Reactive
    /// dependency required.
    /// </summary>
    internal sealed class CertificateChangeSubject : IObservable<CertificateChangeEvent>
    {
        /// <summary>
        /// Creates a subject. Exceptions thrown by observers are logged to
        /// <paramref name="logger"/> (if any) and never propagated.
        /// </summary>
        public CertificateChangeSubject(ILogger? logger = null)
        {
            m_logger = logger;
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<CertificateChangeEvent> observer)
        {
            lock (m_lock)
            {
                m_observers.Add(observer);
            }
            return new Unsubscriber(this, observer);
        }

        /// <summary>
        /// Pushes a certificate change event to all current subscribers.
        /// </summary>
        public void Notify(CertificateChangeEvent evt)
        {
            IObserver<CertificateChangeEvent>[] snapshot;
            lock (m_lock)
            {
                snapshot = [.. m_observers];
            }
            foreach (IObserver<CertificateChangeEvent> observer in snapshot)
            {
                // Isolate observers: one throwing subscriber must neither
                // starve later subscribers nor fail the operation that
                // already committed the change being announced.
                try
                {
                    observer.OnNext(evt);
                }
                catch (Exception ex)
                {
                    m_logger?.CertificateChangeObserverFailed(ex, evt.Kind);
                }
            }
        }

        /// <summary>
        /// Signals completion to all subscribers and clears the list.
        /// </summary>
        public void Complete()
        {
            IObserver<CertificateChangeEvent>[] snapshot;
            lock (m_lock)
            {
                snapshot = [.. m_observers];
                m_observers.Clear();
            }
            foreach (IObserver<CertificateChangeEvent> observer in snapshot)
            {
                try
                {
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    m_logger?.CertificateChangeObserverFailed(ex, null);
                }
            }
        }

        private sealed class Unsubscriber(
            CertificateChangeSubject subject,
            IObserver<CertificateChangeEvent> observer) : IDisposable
        {
            public void Dispose()
            {
                lock (subject.m_lock)
                {
                    subject.m_observers.Remove(observer);
                }
            }
        }

        private readonly List<IObserver<CertificateChangeEvent>> m_observers = [];
        private readonly Lock m_lock = new();
        private readonly ILogger? m_logger;
    }
}
