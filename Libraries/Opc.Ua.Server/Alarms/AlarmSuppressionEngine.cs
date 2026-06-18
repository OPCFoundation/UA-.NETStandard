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

namespace Opc.Ua.Server.Alarms
{
    /// <summary>
    /// Server-side engine for managing <c>AlarmSuppressionGroupType</c>
    /// instances and applying suppression rules to group members,
    /// including first-in-group alarm handling.
    /// </summary>
    public sealed class AlarmSuppressionEngine : IDisposable
    {
        private readonly object m_lock = new();
        private readonly List<RegistrationEntry> m_registrations = [];
        private readonly Dictionary<NodeId, List<AlarmConditionState>> m_firstInGroupMembers = [];
        private bool m_disposed;

        /// <summary>
        /// Registers a suppression group. When <paramref name="suppressionSource"/>
        /// returns true, all alarm members are suppressed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="suppressionGroup"/> is <c>null</c>.</exception>
        public void RegisterSuppressionGroup(
            AlarmGroupState suppressionGroup,
            Func<bool> suppressionSource,
            IReadOnlyList<AlarmConditionState> alarmMembers)
        {
            if (suppressionGroup == null)
            {
                throw new ArgumentNullException(nameof(suppressionGroup));
            }
            if (suppressionSource == null)
            {
                throw new ArgumentNullException(nameof(suppressionSource));
            }
            if (alarmMembers == null)
            {
                throw new ArgumentNullException(nameof(alarmMembers));
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                bool initial = suppressionSource();
                m_registrations.Add(new RegistrationEntry
                {
                    Group = suppressionGroup,
                    Source = suppressionSource,
                    Members = [.. alarmMembers],
                    LastState = initial,
                    FirstEvaluation = true
                });
            }
        }

        /// <summary>
        /// Registers a first-in-group alarm. When this alarm activates,
        /// the supplied member alarms are suppressed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="firstAlarm"/> is <c>null</c>.</exception>
        public void RegisterFirstInGroupAlarm(
            AlarmConditionState firstAlarm,
            AlarmGroupState group,
            IReadOnlyList<AlarmConditionState> otherMembers)
        {
            if (firstAlarm == null)
            {
                throw new ArgumentNullException(nameof(firstAlarm));
            }
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }
            if (otherMembers == null)
            {
                throw new ArgumentNullException(nameof(otherMembers));
            }

            lock (m_lock)
            {
                ThrowIfDisposed();
                if (!m_firstInGroupMembers.TryGetValue(group.NodeId, out List<AlarmConditionState>? list))
                {
                    list = [];
                    m_firstInGroupMembers[group.NodeId] = list;
                }
                list.AddRange(otherMembers);
            }
        }

        /// <summary>
        /// Evaluates all registered suppression groups and applies
        /// suppression to their alarm members based on the current
        /// source condition value.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void Evaluate(ISystemContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            RegistrationEntry[] snapshot;
            lock (m_lock)
            {
                ThrowIfDisposed();
                snapshot = [.. m_registrations];
            }

            foreach (RegistrationEntry entry in snapshot)
            {
                bool currentState;
                try
                {
                    currentState = entry.Source();
                }
                catch
                {
                    continue;
                }

                bool changed;
                lock (m_lock)
                {
                    changed = entry.FirstEvaluation || currentState != entry.LastState;
                    entry.LastState = currentState;
                    entry.FirstEvaluation = false;
                }

                if (!changed)
                {
                    continue;
                }

                foreach (AlarmConditionState alarm in entry.Members)
                {
                    alarm.SetSuppressedState(context, currentState);
                }
            }
        }

        /// <summary>
        /// Notifies the engine that a first-in-group alarm changed
        /// active state. The engine suppresses other group members
        /// when this alarm is active.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void OnFirstInGroupActiveChanged(
            ISystemContext context,
            AlarmConditionState firstAlarm,
            AlarmGroupState group,
            bool firstActive)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            List<AlarmConditionState>? otherMembers;
            lock (m_lock)
            {
                ThrowIfDisposed();
                if (!m_firstInGroupMembers.TryGetValue(group.NodeId, out otherMembers))
                {
                    return;
                }
                otherMembers = [.. otherMembers];
            }

            foreach (AlarmConditionState other in otherMembers)
            {
                if (!ReferenceEquals(other, firstAlarm))
                {
                    other.SetSuppressedState(context, firstActive);
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_registrations.Clear();
                m_firstInGroupMembers.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(AlarmSuppressionEngine));
            }
        }

        private sealed class RegistrationEntry
        {
            public required AlarmGroupState Group { get; init; }
            public required Func<bool> Source { get; init; }
            public required List<AlarmConditionState> Members { get; init; }
            public bool LastState { get; set; }
            public bool FirstEvaluation { get; set; }
        }
    }
}
