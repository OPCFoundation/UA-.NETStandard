// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
