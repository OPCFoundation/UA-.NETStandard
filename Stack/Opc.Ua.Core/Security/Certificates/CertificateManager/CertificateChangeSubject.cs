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
        private readonly List<IObserver<CertificateChangeEvent>> _observers = [];
        private readonly object _lock = new();

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<CertificateChangeEvent> observer)
        {
            lock (_lock) { _observers.Add(observer); }
            return new Unsubscriber(this, observer);
        }

        /// <summary>
        /// Pushes a certificate change event to all current subscribers.
        /// </summary>
        public void Notify(CertificateChangeEvent evt)
        {
            IObserver<CertificateChangeEvent>[] snapshot;
            lock (_lock) { snapshot = [.. _observers]; }
            foreach (var observer in snapshot)
            {
                observer.OnNext(evt);
            }
        }

        /// <summary>
        /// Signals completion to all subscribers and clears the list.
        /// </summary>
        public void Complete()
        {
            IObserver<CertificateChangeEvent>[] snapshot;
            lock (_lock) { snapshot = [.. _observers]; _observers.Clear(); }
            foreach (var observer in snapshot)
            {
                observer.OnCompleted();
            }
        }

        private sealed class Unsubscriber(
            CertificateChangeSubject subject,
            IObserver<CertificateChangeEvent> observer) : IDisposable
        {
            public void Dispose()
            {
                lock (subject._lock) { subject._observers.Remove(observer); }
            }
        }
    }
}
