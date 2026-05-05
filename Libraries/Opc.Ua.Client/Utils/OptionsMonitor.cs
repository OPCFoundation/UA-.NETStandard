// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
                foreach (KeyValuePair<Listener, Action<T, string?>> listener in m_listeners)
                {
                    listener.Value(value, null);
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
            return new Listener(this, listener);
        }

        /// <summary>
        /// Disposable listener
        /// </summary>
        private sealed class Listener : IDisposable
        {
            /// <inheritdoc/>
            public Listener(OptionsMonitor<T> monitor,
                Action<T, string?> listener)
            {
                m_monitor = monitor;
                m_monitor.m_listeners.TryAdd(this, listener);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_monitor.m_listeners.TryRemove(this, out _);
            }

            private readonly OptionsMonitor<T> m_monitor;
        }

        private readonly ConcurrentDictionary<Listener, Action<T, string?>> m_listeners = new();
        private T m_currentValue;
    }
}
