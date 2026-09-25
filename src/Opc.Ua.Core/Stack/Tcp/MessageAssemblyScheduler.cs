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
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Owns one assembly-deadline timer per active clock within a channel-quota scope.
    /// </summary>
    internal sealed class MessageAssemblyScheduler
    {
        internal void Register(TcpListenerChannel channel, TimeProvider clock, int lifetime)
        {
            lock (m_lock)
            {
                foreach (ClockScope scope in m_scopes)
                {
                    if (ReferenceEquals(scope.Clock, clock))
                    {
                        scope.Channels.Add(channel);
                        return;
                    }
                }

                var newScope = new ClockScope(clock);
                newScope.Channels.Add(channel);
                TimeSpan period = TimeSpan.FromMilliseconds(Math.Max(1, lifetime / 2));
                newScope.Timer = clock.CreateTimer(
                    state => CheckDeadlines((ClockScope)state!),
                    newScope,
                    period,
                    period);
                m_scopes.Add(newScope);
            }
        }

        internal void Unregister(TcpListenerChannel channel, TimeProvider clock)
        {
            ITimer? timer = null;
            lock (m_lock)
            {
                for (int ii = 0; ii < m_scopes.Count; ii++)
                {
                    ClockScope scope = m_scopes[ii];
                    if (!ReferenceEquals(scope.Clock, clock))
                    {
                        continue;
                    }
                    scope.Channels.Remove(channel);
                    if (scope.Channels.Count == 0)
                    {
                        timer = scope.Timer;
                        m_scopes.RemoveAt(ii);
                    }
                    break;
                }
            }
            timer?.Dispose();
        }

        private void CheckDeadlines(ClockScope scope)
        {
            TcpListenerChannel[] channels;
            lock (m_lock)
            {
                channels = new TcpListenerChannel[scope.Channels.Count];
                scope.Channels.CopyTo(channels);
            }
            // Channel callbacks may close and unregister themselves.
            foreach (TcpListenerChannel channel in channels)
            {
                channel.CheckMessageAssemblyTimeout();
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<ClockScope> m_scopes = [];

        private sealed class ClockScope(TimeProvider clock)
        {
            public TimeProvider Clock { get; } = clock;

            public HashSet<TcpListenerChannel> Channels { get; } = [];

            public ITimer? Timer { get; set; }
        }
    }
}
