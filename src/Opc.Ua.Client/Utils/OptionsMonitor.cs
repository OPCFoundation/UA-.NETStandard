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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Opc.Ua
{
    /// <summary>
    /// Simple in-memory <see cref="IOptionsMonitor{T}"/> adapter that wraps
    /// a single mutable value. Useful when bypassing the DI options system
    /// — for example when constructing subscriptions or monitored items
    /// from a literal options snapshot or from a builder. Calling
    /// <see cref="Configure"/> updates the wrapped value and triggers any
    /// registered <see cref="OnChange"/> listeners.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class OptionsMonitor<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T> :
        IOptionsMonitor<T>
    {
        /// <summary>
        /// Create options
        /// </summary>
        /// <param name="option"></param>
        public OptionsMonitor(T option)
        {
            m_currentValue = option;
        }

        /// <summary>
        /// Configure options
        /// </summary>
        /// <param name="configure"></param>
        public OptionsMonitor<T> Configure(Func<T, T> configure)
        {
            CurrentValue = configure(m_currentValue);
            return this;
        }

        /// <inheritdoc/>
        public T CurrentValue
        {
            get => m_currentValue;
            set
            {
                m_currentValue = value;
                foreach (Listener listener in Volatile.Read(ref m_listeners))
                {
                    listener.Callback(value, null);
                }
            }
        }

        /// <inheritdoc/>
        public T Get(string? name)
        {
            return CurrentValue;
        }

        /// <inheritdoc/>
        public IDisposable? OnChange(Action<T, string?> listener)
        {
            var registration = new Listener(this, listener);
            Listener[] current = Volatile.Read(ref m_listeners);
            while (true)
            {
                var updated = new Listener[current.Length + 1];
                Array.Copy(current, updated, current.Length);
                updated[current.Length] = registration;
                Listener[] observed = Interlocked.CompareExchange(
                    ref m_listeners, updated, current);
                if (ReferenceEquals(observed, current))
                {
                    return registration;
                }
                current = observed;
            }
        }

        /// <summary>
        /// Remove a listener registration. Copy on write, a no-op when the
        /// registration was already removed.
        /// </summary>
        private void Remove(Listener registration)
        {
            Listener[] current = Volatile.Read(ref m_listeners);
            while (true)
            {
                int index = Array.IndexOf(current, registration);
                if (index < 0)
                {
                    return;
                }
                Listener[] updated;
                if (current.Length == 1)
                {
                    updated = [];
                }
                else
                {
                    updated = new Listener[current.Length - 1];
                    Array.Copy(current, updated, index);
                    Array.Copy(current, index + 1, updated, index,
                        current.Length - index - 1);
                }
                Listener[] observed = Interlocked.CompareExchange(
                    ref m_listeners, updated, current);
                if (ReferenceEquals(observed, current))
                {
                    return;
                }
                current = observed;
            }
        }

        /// <summary>
        /// Disposable listener
        /// </summary>
        private sealed class Listener : IDisposable
        {
            /// <inheritdoc/>
            public Listener(OptionsMonitor<T> monitor,
                Action<T, string?> callback)
            {
                m_monitor = monitor;
                Callback = callback;
            }

            /// <summary>
            /// The registered change callback
            /// </summary>
            public Action<T, string?> Callback { get; }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_monitor.Remove(this);
            }

            private readonly OptionsMonitor<T> m_monitor;
        }

        /// <summary>
        /// Copy-on-write listener list. A monitor has zero or one listener
        /// in most cases (one per monitored item), where a concurrent
        /// dictionary would allocate one lock per processor.
        /// </summary>
        private Listener[] m_listeners = [];
        private T m_currentValue;
    }
}
