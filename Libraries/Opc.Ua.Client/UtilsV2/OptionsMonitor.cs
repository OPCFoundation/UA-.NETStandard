#if OPCUA_CLIENT_V2
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

namespace Opc.Ua
{
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Options monitor adapter
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class OptionsMonitor<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T> :
        IOptionsMonitor<T>
    {
        /// <summary>
        /// Create options
        /// </summary>
        /// <param name="option"></param>
        public OptionsMonitor(T option) => _currentValue = option;

        /// <summary>
        /// Configure options
        /// </summary>
        /// <param name="configure"></param>
        public OptionsMonitor<T> Configure(Func<T, T> configure)
        {
            CurrentValue = configure(_currentValue);
            return this;
        }

        /// <inheritdoc/>
        public T CurrentValue
        {
            get => _currentValue;
            set
            {
                _currentValue = value;
                foreach (var listener in _listeners)
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
                _monitor = monitor;
                _monitor._listeners.TryAdd(this, listener);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                _monitor._listeners.TryRemove(this, out _);
            }

            private readonly OptionsMonitor<T> _monitor;
        }

        private readonly ConcurrentDictionary<Listener, Action<T, string?>> _listeners = new();
        private T _currentValue;
    }
}
#endif
