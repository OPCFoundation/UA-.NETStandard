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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Extension methods that turn a boolean supervision variable into
    /// an alarm trigger. Modelled on the OPC UA DI / NAMUR NE 107
    /// pattern: a <c>TwoStateDiscreteType</c> variable goes
    /// <see langword="true"/> to indicate a supervision condition is
    /// active and <see langword="false"/> to indicate it cleared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fluent surface is intentionally narrow — observe the variable's
    /// value transitions and dispatch typed callbacks (or activate /
    /// deactivate an alarm created via the G2
    /// <see cref="AlarmBuilderExtensions"/> helpers).
    /// </para>
    /// <para>
    /// Detection is value-change based: the helper wraps the
    /// variable's <c>OnStateChanged</c> hook and emits a transition
    /// signal whenever the new boolean differs from the previous value.
    /// No background polling thread is started — transitions only fire
    /// when something else (an OnWrite handler, a simulation tick
    /// handler, etc.) actually mutates the variable.
    /// </para>
    /// </remarks>
    public static class SupervisionBuilderExtensions
    {
        /// <summary>
        /// Returns a builder that fires the supplied
        /// <paramref name="handler"/> whenever the variable's value
        /// transitions from <see langword="false"/> to
        /// <see langword="true"/>.
        /// </summary>
        public static IVariableBuilder<bool> OnRisingEdge(
            this IVariableBuilder<bool> builder,
            Action<ISystemContext> handler)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            AttachEdgeTracker(builder.Node).RisingEdge += handler;
            return builder;
        }

        /// <summary>
        /// Returns a builder that fires the supplied
        /// <paramref name="handler"/> whenever the variable's value
        /// transitions from <see langword="true"/> to
        /// <see langword="false"/>.
        /// </summary>
        public static IVariableBuilder<bool> OnFallingEdge(
            this IVariableBuilder<bool> builder,
            Action<ISystemContext> handler)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            AttachEdgeTracker(builder.Node).FallingEdge += handler;
            return builder;
        }

        /// <summary>
        /// Wires a NAMUR-style supervision flag into an alarm:
        /// the alarm's <see cref="ConditionState.EnabledState"/> /
        /// <see cref="AlarmConditionState.ActiveState"/> are flipped
        /// in lock-step with the variable's value transitions.
        /// </summary>
        /// <typeparam name="TAlarm">
        /// Concrete alarm state type — must derive from
        /// <see cref="AlarmConditionState"/> so the
        /// <c>ActiveState</c> child is present.
        /// </typeparam>
        /// <param name="builder">The supervision variable builder.</param>
        /// <param name="alarm">
        /// An alarm previously created via the G2 builders
        /// (<see cref="AlarmBuilderExtensions"/>) or hand-rolled.
        /// </param>
        public static IVariableBuilder<bool> ActivatesAlarm<TAlarm>(
            this IVariableBuilder<bool> builder,
            IAlarmBuilder<TAlarm> alarm)
            where TAlarm : ConditionState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (alarm == null)
            {
                throw new ArgumentNullException(nameof(alarm));
            }
            if (alarm.Alarm is not AlarmConditionState ac)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "ActivatesAlarm requires an AlarmConditionState; alarm '{0}' is '{1}'.",
                    alarm.Alarm.BrowseName,
                    alarm.Alarm.GetType().Name);
            }

            EdgeTracker tracker = AttachEdgeTracker(builder.Node);
            tracker.RisingEdge += ctx => SetAlarmActive(ctx, ac, active: true);
            tracker.FallingEdge += ctx => SetAlarmActive(ctx, ac, active: false);
            return builder;
        }

        private static void SetAlarmActive(
            ISystemContext context,
            AlarmConditionState alarm,
            bool active)
        {
            if (alarm.ActiveState?.Id != null)
            {
                alarm.ActiveState.Id.Value = active;
                alarm.ActiveState.Value = new LocalizedText(active ? "Active" : "Inactive");
                alarm.Time?.Value = DateTimeUtc.Now;
                alarm.ClearChangeMasks(context, includeChildren: true);
            }
        }

        private static EdgeTracker AttachEdgeTracker(BaseVariableState variable)
        {
            // Reuse an attached tracker so multiple OnRisingEdge /
            // OnFallingEdge calls share a single OnAfterChange hook.
            // The tracker is stashed on Handle so it survives across
            // builder calls without us having to thread it through.
            if (variable.Handle is EdgeTracker existing)
            {
                return existing;
            }
            var tracker = new EdgeTracker();

            // Seed the tracker with the current value so the very next
            // transition fires a proper edge event. Without this, a
            // variable that starts at true and goes to false would not
            // fire OnFallingEdge because the tracker would interpret the
            // false as its baseline.
            if (variable.WrappedValue.TryGetValue(out bool initial))
            {
                tracker.SeedInitial(initial);
            }

            NodeStateChangedHandler? previous = variable.OnStateChanged;
            variable.OnStateChanged = (ctx, node, mask) =>
            {
                previous?.Invoke(ctx, node, mask);
                if ((mask & NodeStateChangeMasks.Value) != 0 &&
                    node is BaseVariableState v)
                {
                    bool current = v.WrappedValue.TryGetValue(out bool b) && b;
                    tracker.NotifyValue(ctx, current);
                }
            };
            variable.Handle = tracker;
            return tracker;
        }

        /// <summary>
        /// Per-variable edge dispatcher attached to the variable's
        /// <c>Handle</c> property. Tracks the previous observed value
        /// to emit only true edge transitions.
        /// </summary>
        private sealed class EdgeTracker
        {
            public event Action<ISystemContext>? RisingEdge;
            public event Action<ISystemContext>? FallingEdge;

            public void SeedInitial(bool value)
            {
                m_last = value;
                m_initialized = true;
            }

            public void NotifyValue(ISystemContext context, bool current)
            {
                if (m_initialized && current == m_last)
                {
                    return;
                }
                bool wasInitialized = m_initialized;
                m_initialized = true;
                bool previous = wasInitialized && m_last;
                m_last = current;

                if (current && !previous)

                {

                    RisingEdge?.Invoke(context);

                }
                else if (!current && previous)
                {
                    FallingEdge?.Invoke(context);
                }
            }

            private bool m_last;
            private bool m_initialized;
        }
    }
}
