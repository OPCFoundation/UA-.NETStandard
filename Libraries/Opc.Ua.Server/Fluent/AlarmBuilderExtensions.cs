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
    /// Strongly-typed fluent builder for an alarm/condition state
    /// instance. Returned by the <c>CreateLimitAlarm</c> /
    /// <c>CreateExclusiveLimitAlarm</c> / <c>CreateOffNormalAlarm</c>
    /// helpers on <see cref="INodeBuilder"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MVP scope: the builder surfaces only the most common alarm
    /// settings (limits, source variable, acknowledge / confirm
    /// callbacks). For anything more advanced use the
    /// <see cref="AlarmBuilderExtensions.ConfigureAlarm{TState}(IAlarmBuilder{TState}, Action{TState})"/>
    /// escape hatch to mutate the underlying
    /// <see cref="ConditionState"/> directly.
    /// </para>
    /// </remarks>
    /// <typeparam name="TState">
    /// Concrete alarm state class — must derive from
    /// <see cref="ConditionState"/>.
    /// </typeparam>
    public interface IAlarmBuilder<TState> where TState : ConditionState
    {
        /// <summary>
        /// The underlying alarm state instance.
        /// </summary>
        TState Alarm { get; }

        /// <summary>
        /// The owning node builder, for chain termination.
        /// </summary>
        INodeBuilder Builder { get; }

        /// <summary>
        /// Sets the <see cref="LimitAlarmState.HighHighLimit"/> /
        /// <see cref="LimitAlarmState.HighLimit"/> /
        /// <see cref="LimitAlarmState.LowLimit"/> /
        /// <see cref="LimitAlarmState.LowLowLimit"/> property values on
        /// a limit-style alarm. Throws
        /// <see cref="StatusCodes.BadTypeMismatch"/> when invoked on
        /// a non-limit alarm. Pass <c>double.NaN</c> for any limit you
        /// don't want to set.
        /// </summary>
        IAlarmBuilder<TState> WithLimits(
            double highHigh = double.NaN,
            double high = double.NaN,
            double low = double.NaN,
            double lowLow = double.NaN);

        /// <summary>
        /// Sets the alarm's <c>SourceNode</c> reference and
        /// <c>SourceName</c> to the supplied target. Equivalent to
        /// setting the alarm's "InputNode" semantics from the spec.
        /// </summary>
        IAlarmBuilder<TState> MonitorVariable(NodeState source);

        /// <summary>
        /// Wires <see cref="AcknowledgeableConditionState.OnAcknowledge"/>.
        /// Return <see cref="ServiceResult.Good"/> from the handler to
        /// permit the acknowledge transition; any other code cancels it.
        /// </summary>
        IAlarmBuilder<TState> OnAcknowledge(ConditionAddCommentEventHandler handler);

        /// <summary>
        /// Wires <see cref="AcknowledgeableConditionState.OnConfirm"/>.
        /// </summary>
        IAlarmBuilder<TState> OnConfirm(ConditionAddCommentEventHandler handler);
    }

    /// <summary>
    /// Extension methods that create alarm instances under a parent node
    /// resolved through the fluent builder.
    /// </summary>
    public static class AlarmBuilderExtensions
    {
        /// <summary>
        /// Creates a new <see cref="NonExclusiveLimitAlarmState"/> child
        /// under the resolved parent. The alarm is attached via
        /// <see cref="NodeState.AddChild(BaseInstanceState)"/>; the
        /// owning manager is responsible for indexing it via
        /// <c>AddPredefinedNodeAsync</c> if direct NodeId lookup is
        /// required.
        /// </summary>
        public static IAlarmBuilder<NonExclusiveLimitAlarmState> CreateLimitAlarm(
            this INodeBuilder parent,
            QualifiedName browseName)
        {
            NonExclusiveLimitAlarmState alarm = AttachAlarm(
                parent, browseName, p => new NonExclusiveLimitAlarmState(p));
            return new AlarmBuilder<NonExclusiveLimitAlarmState>(parent, alarm);
        }

        /// <summary>
        /// Creates a new <see cref="ExclusiveLimitAlarmState"/> child.
        /// </summary>
        public static IAlarmBuilder<ExclusiveLimitAlarmState> CreateExclusiveLimitAlarm(
            this INodeBuilder parent,
            QualifiedName browseName)
        {
            ExclusiveLimitAlarmState alarm = AttachAlarm(
                parent, browseName, p => new ExclusiveLimitAlarmState(p));
            return new AlarmBuilder<ExclusiveLimitAlarmState>(parent, alarm);
        }

        /// <summary>
        /// Creates a new <see cref="OffNormalAlarmState"/> child.
        /// </summary>
        public static IAlarmBuilder<OffNormalAlarmState> CreateOffNormalAlarm(
            this INodeBuilder parent,
            QualifiedName browseName)
        {
            OffNormalAlarmState alarm = AttachAlarm(
                parent, browseName, p => new OffNormalAlarmState(p));
            return new AlarmBuilder<OffNormalAlarmState>(parent, alarm);
        }

        /// <summary>
        /// Escape hatch: directly mutate the underlying alarm state.
        /// Use for properties not covered by the narrow MVP surface
        /// (e.g. severity table, retain flag, branches).
        /// </summary>
        /// <typeparam name="TState">Concrete alarm condition state type.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IAlarmBuilder<TState> ConfigureAlarm<TState>(
            this IAlarmBuilder<TState> builder,
            Action<TState> configure)
            where TState : ConditionState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(builder.Alarm);
            return builder;
        }

        /// <summary>
        /// Returns control to the owning node builder so subsequent
        /// fluent calls operate on the alarm's parent again. Use this
        /// when chaining multiple alarms.
        /// </summary>
        /// <typeparam name="TState">Concrete alarm condition state type.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static INodeBuilder Done<TState>(this IAlarmBuilder<TState> builder)
            where TState : ConditionState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.Builder;
        }

        private static TState AttachAlarm<TState>(
            INodeBuilder parent,
            QualifiedName browseName,
            Func<NodeState, TState> factory)
            where TState : ConditionState
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentNullException(nameof(browseName));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            string symbolicName = browseName.Name ?? string.Empty;
            TState alarm = factory(parent.Node);
            alarm.SymbolicName = symbolicName;
            alarm.BrowseName = browseName;
            alarm.DisplayName = new LocalizedText(symbolicName);

            string parentIdentifier = parent.Node.NodeId.IdentifierAsString;
            alarm.NodeId = new NodeId(
                string.Concat(parentIdentifier, "_", symbolicName),
                parent.Node.NodeId.NamespaceIndex);

            // Initialize standard alarm state surface so the alarm is
            // immediately addressable as a valid OPC UA condition.
            alarm.Create(
                parent.Builder.Context,
                alarm.NodeId,
                browseName,
                displayName: new LocalizedText(symbolicName),
                assignNodeIds: false);

            parent.Node.AddChild(alarm);
            return alarm;
        }
    }

    /// <summary>
    /// Internal implementation of <see cref="IAlarmBuilder{TState}"/>.
    /// </summary>
    /// <typeparam name="TState">Concrete alarm condition state type.</typeparam>
    internal sealed class AlarmBuilder<TState> : IAlarmBuilder<TState>
        where TState : ConditionState
    {
        public AlarmBuilder(INodeBuilder parent, TState alarm)
        {
            Builder = parent ?? throw new ArgumentNullException(nameof(parent));
            Alarm = alarm ?? throw new ArgumentNullException(nameof(alarm));
        }

        public TState Alarm { get; }
        public INodeBuilder Builder { get; }

        public IAlarmBuilder<TState> WithLimits(
            double highHigh = double.NaN,
            double high = double.NaN,
            double low = double.NaN,
            double lowLow = double.NaN)
        {
            if (Alarm is not LimitAlarmState limit)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "WithLimits requires a LimitAlarmState; alarm '{0}' is '{1}'.",
                    Alarm.BrowseName,
                    Alarm.GetType().Name);
            }

            _ = Builder.Builder.Context;
            if (!double.IsNaN(highHigh))
            {
                limit.HighHighLimit ??=
                    new PropertyState<double>.Implementation<VariantBuilder>(limit);
                limit.HighHighLimit.Value = highHigh;
            }
            if (!double.IsNaN(high))
            {
                limit.HighLimit ??=
                    new PropertyState<double>.Implementation<VariantBuilder>(limit);
                limit.HighLimit.Value = high;
            }
            if (!double.IsNaN(low))
            {
                limit.LowLimit ??=
                    new PropertyState<double>.Implementation<VariantBuilder>(limit);
                limit.LowLimit.Value = low;
            }
            if (!double.IsNaN(lowLow))
            {
                limit.LowLowLimit ??=
                    new PropertyState<double>.Implementation<VariantBuilder>(limit);
                limit.LowLowLimit.Value = lowLow;
            }
            return this;
        }

        public IAlarmBuilder<TState> MonitorVariable(NodeState source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            Alarm.SourceNode!.Value = source.NodeId;
            QualifiedName srcName = source.BrowseName;
            Alarm.SourceName!.Value = srcName.IsNull ? string.Empty : (srcName.Name ?? string.Empty);
            return this;
        }

        public IAlarmBuilder<TState> OnAcknowledge(ConditionAddCommentEventHandler handler)
        {
            if (Alarm is not AcknowledgeableConditionState ack)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "OnAcknowledge requires an AcknowledgeableConditionState; alarm '{0}' is '{1}'.",
                    Alarm.BrowseName,
                    Alarm.GetType().Name);
            }
            ack.OnAcknowledge = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        public IAlarmBuilder<TState> OnConfirm(ConditionAddCommentEventHandler handler)
        {
            if (Alarm is not AcknowledgeableConditionState ack)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "OnConfirm requires an AcknowledgeableConditionState; alarm '{0}' is '{1}'.",
                    Alarm.BrowseName,
                    Alarm.GetType().Name);
            }
            ack.OnConfirm = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }
    }
}
