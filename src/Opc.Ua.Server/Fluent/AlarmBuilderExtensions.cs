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
using System.Threading.Tasks;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Undoes everything attaching an alarm changed in the address space.
    /// </summary>
    /// <remarks>
    /// Held by the node manager as a behavior, so release runs in exact reverse order
    /// with every other behavior and is aggregated with their failures rather than
    /// being lost.
    /// </remarks>
    internal sealed class AlarmRelease : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AlarmRelease"/> class.
        /// </summary>
        /// <param name="alarm">The alarm to release.</param>
        /// <param name="context">The context to release it in.</param>
        /// <param name="eventSource">What registering the alarm changed.</param>
        /// <param name="builder">The builder that owns the alarm.</param>
        /// <param name="enabledByUs">
        /// Whether attaching the alarm was what enabled it. False when it arrived
        /// already enabled, in which case its enable state was never ours to undo.
        /// </param>
        public AlarmRelease(
            ConditionState alarm,
            ISystemContext context,
            AlarmEventSourceRegistration eventSource,
            NodeManagerBuilder builder,
            bool enabledByUs)
        {
            m_alarm = alarm;
            m_context = context;
            m_eventSource = eventSource;
            m_builder = builder;
            m_enabledByUs = enabledByUs;
        }

        public async ValueTask DisposeAsync()
        {
            if (m_released)
            {
                return;
            }
            m_released = true;

            // A failure here must not cost us the rest of the cleanup, but it must not
            // vanish either: the engine promises to aggregate release failures, and an
            // operator otherwise gets no sign that teardown left registration state
            // behind. It is retained and rethrown once the rest has run.
            Exception? rootNotifierFailure = null;
            if (m_eventSource.RootNotifier != null &&
                m_builder.NodeManager is FluentNodeManagerBase manager)
            {
                try
                {
                    await manager
                        .RemoveRootNotifierFromFluentAsync(
                            m_eventSource.RootNotifier,
                            System.Threading.CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    rootNotifierFailure = ex;
                }
            }

            foreach (BaseObjectState notifier in m_eventSource.PromotedNotifiers)
            {
                notifier.EventNotifier = (byte)(notifier.EventNotifier &
                    unchecked((byte)~EventNotifiers.SubscribeToEvents));
            }

            // The OnAcknowledge/OnConfirm slots are plain delegates on a node that is
            // being deleted, so they are left alone: they hold nothing to release, and
            // the fields are not nullable.

            // Disable only what attaching enabled, and only while it is still enabled.
            // Both halves matter, and for different reasons.
            //
            // The ownership half is parity with everything else this class reverses: the
            // notifier bits list only nodes whose bit we set, and the root notifier is
            // claimed only when we inserted it. The enable state was the one thing
            // reversed unconditionally.
            //
            // The still-enabled half guards a case ownership does not cover. A client may
            // disable the condition at runtime through the Disable method, and that path
            // refuses a redundant transition — ProcessBeforeEnableDisable answers
            // BadConditionAlreadyDisabled. SetEnableState is the programmatic path and
            // makes no such check, so disabling an already-disabled condition would
            // rewrite EnabledState, clear Retain and stamp a fresh TransitionTime: a
            // second, spurious disable transition emitted on the way out.
            if (m_enabledByUs && m_alarm.EnabledState?.Id?.Value == true)
            {
                m_alarm.SetEnableState(m_context, enabled: false);
            }

            if (rootNotifierFailure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(rootNotifierFailure)
                    .Throw();
            }
        }

        private readonly ConditionState m_alarm;
        private readonly ISystemContext m_context;
        private readonly AlarmEventSourceRegistration m_eventSource;
        private readonly NodeManagerBuilder m_builder;
        private readonly bool m_enabledByUs;
        private bool m_released;
    }

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
        /// under the resolved parent. The alarm is attached to the
        /// parent, registered with the owning node manager, and the
        /// parent object is promoted as an event notifier.
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
            if (parent.Node is not BaseObjectState)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Alarm parent '{0}' must be an Object; resolved node is '{1}'.",
                    parent.Node.BrowseName,
                    parent.Node.NodeClass);
            }
            string symbolicName = browseName.Name ?? string.Empty;
            TState alarm = factory(parent.Node);
            alarm.SymbolicName = symbolicName;
            alarm.BrowseName = browseName;
            alarm.DisplayName = new LocalizedText(symbolicName);

            FluentNodeRegistration.AssignNodeId(parent.Builder, alarm);

            // Initialize standard alarm state surface so the alarm is
            // immediately addressable as a valid OPC UA condition.
            alarm.Create(
                parent.Builder.Context,
                alarm.NodeId,
                browseName,
                displayName: new LocalizedText(symbolicName),
                assignNodeIds: false);

            // Record whether enabling is ours to undo, before doing it. A freshly
            // created condition is disabled, so today this is always true; it is captured
            // rather than assumed so that teardown reverses a state it observed instead
            // of one it inferred, the same way the notifier chain is recorded below.
            bool enabledByUs = alarm.EnabledState?.Id?.Value != true;
            alarm.SetEnableState(parent.Builder.Context, enabled: true);

            alarm.ReferenceTypeId = ReferenceTypeIds.HasCondition;
            parent.Node.AddChild(alarm);
            InitializeAlarmSource(parent.Node, alarm);

            // The parent's EventNotifier is promoted by RegisterAlarmEventSource below,
            // which walks the whole notifier chain starting at this very node and
            // records what it changed so teardown can undo it. Promoting here as well
            // would set the bit first and leave that record empty.
            parent.Node.AddReference(
                ReferenceTypeIds.HasEventSource,
                isInverse: false,
                alarm.NodeId);
            alarm.AddReference(
                ReferenceTypeIds.HasEventSource,
                isInverse: true,
                parent.Node.NodeId);

            // Index the alarm so it is browsable and resolvable by NodeId,
            // then promote the notifier chain above it and register the
            // source as a root notifier so clients subscribing on the
            // Server Object receive the condition events.
            FluentNodeRegistration.RegisterCreatedNode(parent.Builder, alarm);
            AlarmEventSourceRegistration eventSource =
                FluentNodeRegistration.RegisterAlarmEventSource(parent.Builder, parent.Node);

            // Hand release to the behavior mechanism. Everything above mutates the
            // address space and, until now, nothing undid any of it: an alarm survived
            // its own node manager's teardown as an enabled condition with a promoted
            // notifier chain and a root-notifier registration still in place.
            // Alarms work on any node manager, so a manager that has not opted into the
            // fluent surface keeps the behavior it always had: it simply gets no
            // automatic release.
            NodeManagerBuilder? concrete =
                FluentNodeManagerBase.TryResolveAttachedBuilder(parent.Builder);
            concrete?.RegisterNodeAttachment(
                NodeAttachRegistration.ForNode(
                    alarm,
                    static (_, _, _, state) => new ValueTask<IAsyncDisposable?>(
                        (AlarmRelease)state),
                    new AlarmRelease(
                        alarm,
                        parent.Builder.Context,
                        eventSource,
                        concrete,
                        enabledByUs)));

            return alarm;
        }

        private static void InitializeAlarmSource<TState>(
            NodeState source,
            TState alarm)
            where TState : ConditionState
        {
            if (alarm.SourceNode != null &&
                alarm.SourceNode.Value.IsNull)
            {
                alarm.SourceNode.Value = source.NodeId;
            }

            if (alarm.SourceName != null &&
                string.IsNullOrEmpty(alarm.SourceName.Value))
            {
                QualifiedName browseName = source.BrowseName;
                alarm.SourceName.Value = browseName.IsNull ? string.Empty : browseName.Name ?? string.Empty;
            }

            if (alarm.ConditionName != null &&
                string.IsNullOrEmpty(alarm.ConditionName.Value))
            {
                alarm.ConditionName.Value = alarm.BrowseName.Name ?? string.Empty;
            }

            if (alarm is AlarmConditionState alarmCondition &&
                alarmCondition.InputNode != null &&
                alarmCondition.InputNode.Value.IsNull)
            {
                alarmCondition.InputNode.Value = source.NodeId;
            }
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
