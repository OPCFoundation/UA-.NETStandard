/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    /// <summary>
    /// A live OPC UA binding channel that translates WoT operations onto OPC UA
    /// services: read / write of a NodeId Value attribute, observe and event
    /// subscription via a native <see cref="Subscription"/> / <see cref="MonitoredItem"/>
    /// pair (Part 4 §5.12 / §5.13), and action invocation via Method Call
    /// preserving argument order and <see cref="DataValue"/> / <see cref="StatusCode"/>
    /// metadata.
    /// </summary>
    internal sealed class OpcUaWotBindingChannel : IWotBindingChannel
    {
        public OpcUaWotBindingChannel(
            ISession session,
            bool disposeSession,
            WotCompiledForm form,
            WotExecutorContext context,
            OpcUaWotBindingOptions options)
        {
            m_session = session;
            m_disposeSession = disposeSession;
            Form = form;
            m_options = options;
            m_nodeId = form.Addressing.Target;
        }

        public WotCompiledForm Form { get; }

        public async ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!TryResolveNodeId(m_nodeId, out NodeId nodeId))
            {
                return new WotReadResult(
                    StatusCodes.BadNodeIdInvalid,
                    DataValue.FromStatusCode(StatusCodes.BadNodeIdInvalid),
                    $"'{m_nodeId}' is not a valid NodeId.");
            }
            try
            {
                DataValue value = await m_session.ReadValueAsync(nodeId, cancellationToken).ConfigureAwait(false);
                return new WotReadResult(value.StatusCode, value);
            }
            catch (ServiceResultException ex)
            {
                StatusCode status = ex.StatusCode;
                return new WotReadResult(status, DataValue.FromStatusCode(status), ex.Message);
            }
        }

        public async ValueTask<WotWriteResult> WriteAsync(
            DataValue value, CancellationToken cancellationToken = default)
        {
            if (!TryResolveNodeId(m_nodeId, out NodeId nodeId))
            {
                return new WotWriteResult(StatusCodes.BadNodeIdInvalid, $"'{m_nodeId}' is not a valid NodeId.");
            }
            try
            {
                var write = new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value.WrappedValue)
                };
                WriteResponse response = await m_session
                    .WriteAsync(null, new WriteValue[] { write }, cancellationToken).ConfigureAwait(false);
                StatusCode status = response.Results is { Count: > 0 }
                    ? response.Results[0] : StatusCodes.BadUnexpectedError;
                return new WotWriteResult(status, StatusCode.IsBad(status) ? status.ToString() : null);
            }
            catch (ServiceResultException ex)
            {
                return new WotWriteResult(ex.StatusCode, ex.Message);
            }
        }

        public async ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            if (!Form.Addressing.Metadata.TryGetValue("componentOf", out string? objectRef) ||
                string.IsNullOrEmpty(objectRef) ||
                !TryResolveNodeId(objectRef!, out NodeId objectId))
            {
                return new WotInvokeResult(
                    StatusCodes.BadNodeIdInvalid, null,
                    "An OPC UA action requires a uav:componentOf object NodeId.");
            }
            if (!TryResolveNodeId(m_nodeId, out NodeId methodId))
            {
                return new WotInvokeResult(
                    StatusCodes.BadNodeIdInvalid, null, $"'{m_nodeId}' is not a valid method NodeId.");
            }
            try
            {
                Variant[] arguments = inputs is null ? [] : [.. inputs];
                ArrayOf<Variant> outputs = await m_session
                    .CallAsync(objectId, methodId, cancellationToken, arguments).ConfigureAwait(false);
                var results = new DataValue[outputs.Count];
                for (int i = 0; i < outputs.Count; i++)
                {
                    results[i] = new DataValue(outputs[i], StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now);
                }
                return new WotInvokeResult(StatusCodes.Good, results);
            }
            catch (ServiceResultException ex)
            {
                return new WotInvokeResult(ex.StatusCode, null, ex.Message);
            }
        }

        public ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            if (onNotification is null)
            {
                throw new ArgumentNullException(nameof(onNotification));
            }
            if (!TryResolveNodeId(m_nodeId, out NodeId nodeId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, $"'{m_nodeId}' is not a valid NodeId.");
            }
            // Native data-change subscription: the server samples and reports
            // changes (Part 4 §5.12); no client-side polling is involved.
            return CreateMonitoredSubscriptionAsync(
                nodeId,
                NodeClass.Variable,
                Attributes.Value,
                filter: null,
                queueSize: 1,
                translate: static (_, notificationValue) => notificationValue is MonitoredItemNotification change
                    ? new WotNotification(change.Value)
                    : null,
                onNotification,
                cancellationToken);
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
        {
            if (onEvent is null)
            {
                throw new ArgumentNullException(nameof(onEvent));
            }
            if (!TryResolveNodeId(m_nodeId, out NodeId notifierId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, $"'{m_nodeId}' is not a valid event notifier NodeId.");
            }
            EventFilter filter = BuildEventFilter();
            WotEventSelection selection = Form.EventSelection ?? WotEventSelection.Default;
            return CreateMonitoredSubscriptionAsync(
                notifierId,
                NodeClass.Object,
                Attributes.EventNotifier,
                filter,
                queueSize: m_options.EventQueueSize,
                translate: (_, notificationValue) => notificationValue is EventFieldList eventFields
                    ? BuildEventNotification(selection, eventFields)
                    : null,
                onEvent,
                cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            if (m_disposeSession)
            {
                m_session.Dispose();
            }
            return default;
        }

        /// <summary>
        /// Opens a native OPC UA <see cref="Subscription"/> with a single
        /// <see cref="MonitoredItem"/>, translates each notification through
        /// <paramref name="translate"/> and forwards it to <paramref name="onNotification"/>.
        /// Ownership of the created subscription (and its server-side
        /// resources) transfers to the returned <see cref="IWotSubscription"/>;
        /// on any failure to create/apply, the subscription is torn down and
        /// removed from the session before the exception propagates, so no
        /// session/subscription is leaked.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the subscription is transferred to the caller (an " +
                "IWotSubscription), who disposes it; on failure it is disposed in the catch block.")]
        private async ValueTask<IWotSubscription> CreateMonitoredSubscriptionAsync(
            NodeId targetId,
            NodeClass nodeClass,
            uint attributeId,
            MonitoringFilter? filter,
            uint queueSize,
            Func<MonitoredItem, IEncodeable, WotNotification?> translate,
            Action<WotNotification> onNotification,
            CancellationToken cancellationToken)
        {
            int interval = NormalizeInterval(m_options.ObserveInterval);
            var subscription = new Subscription(m_session.DefaultSubscription)
            {
                DisplayName = "wot-" + Form.AffordanceName,
                PublishingEnabled = true,
                PublishingInterval = interval
            };
            m_session.AddSubscription(subscription);
            try
            {
                await subscription.CreateAsync(cancellationToken).ConfigureAwait(false);

                var item = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = targetId,
                    NodeClass = nodeClass,
                    AttributeId = attributeId,
                    DisplayName = Form.AffordanceName,
                    SamplingInterval = interval,
                    QueueSize = queueSize,
                    DiscardOldest = true
                };
                if (filter is not null)
                {
                    item.Filter = filter;
                }

                void OnItemNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
                {
                    WotNotification? notification = translate(monitoredItem, e.NotificationValue);
                    if (notification is not null)
                    {
                        onNotification(notification);
                    }
                }
                item.Notification += OnItemNotification;

                subscription.AddItem(item);
                await subscription.ApplyChangesAsync(cancellationToken).ConfigureAwait(false);

                if (!item.Created)
                {
                    item.Notification -= OnItemNotification;
                    StatusCode status = item.Status.Error?.StatusCode ?? StatusCodes.BadMonitoredItemFilterUnsupported;
                    throw new ServiceResultException(
                        status,
                        item.Status.Error?.ToString() ?? "The server rejected the monitored item.");
                }

                return new OpcUaMonitoredItemSubscription(Form, m_session, subscription, item, OnItemNotification);
            }
            catch
            {
                await RemoveSubscriptionSafeAsync(subscription).ConfigureAwait(false);
                throw;
            }
        }

        private async ValueTask RemoveSubscriptionSafeAsync(Subscription subscription)
        {
            try
            {
                await m_session.RemoveSubscriptionAsync(subscription, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // Best-effort server-side cleanup; the session or subscription
                // may already be unusable (for example a closed session).
            }
            subscription.Dispose();
        }

        /// <summary>
        /// Builds the event select filter from the compiled selection of
        /// WoT Binding Section 6.1.
        /// </summary>
        /// <remarks>
        /// The planner always compiles the effective selection - the eight
        /// mandatory <c>BaseEventType</c> fields when the affordance states
        /// none, the complete authored list when it states one - so this method
        /// resolves rather than decides. Resolution is what needs a session:
        /// a portable ExpandedNodeId and a NamespaceUri-qualified path element
        /// only become a <see cref="NodeId"/> and a <see cref="QualifiedName"/>
        /// against a namespace table, and a table exists only once a session
        /// does. An empty browse path selects the NodeId Attribute, which is
        /// the OPC 10000-9 <c>ConditionId</c> idiom; every other clause selects
        /// the Value Attribute of the Node its path reaches.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// Thrown when a clause names a type or a namespace this session cannot
        /// resolve. Subscribing with a silently shortened field list would
        /// deliver an event whose <c>data</c> object the document does not
        /// describe.
        /// </exception>
        private EventFilter BuildEventFilter()
        {
            WotEventSelection selection = Form.EventSelection ?? WotEventSelection.Default;
            var filter = new EventFilter();
            foreach (WotEventSelectClause clause in selection.Clauses)
            {
                if (!TryResolveNodeId(clause.TypeDefinitionId, out NodeId typeDefinitionId))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeDefinitionInvalid,
                        "The event select clause names the EventType " +
                        $"'{clause.TypeDefinitionId}', which this session cannot resolve.");
                }
                var operand = new SimpleAttributeOperand
                {
                    TypeDefinitionId = typeDefinitionId,
                    AttributeId = clause.IsConditionIdSelection
                        ? Attributes.NodeId
                        : Attributes.Value,
                    BrowsePath = ResolveBrowsePath(clause.BrowsePath)
                };
                filter.SelectClauses = filter.SelectClauses.AddItem(operand);
            }
            return filter;
        }

        /// <summary>
        /// Resolves a select-clause browse path into qualified names against
        /// the connected session's namespace table.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown when a path element names a NamespaceUri the Server's
        /// namespace table does not hold.
        /// </exception>
        private ArrayOf<QualifiedName> ResolveBrowsePath(string browsePath)
        {
            if (browsePath.Length == 0)
            {
                return ArrayOf<QualifiedName>.Empty;
            }
            string[] elements = browsePath.Split('/');
            var names = new QualifiedName[elements.Length];
            for (int ii = 0; ii < elements.Length; ii++)
            {
                names[ii] = ResolveBrowseName(elements[ii]);
            }
            return names;
        }

        private QualifiedName ResolveBrowseName(string element)
        {
            string? namespaceUri = null;
            string name = element;
            if (element.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int separator = element.IndexOf(';', 4);
                if (separator <= 4 || separator + 1 >= element.Length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBrowseNameInvalid,
                        $"The event select-clause path element '{element}' is not a valid " +
                        "NamespaceUri-qualified browse name.");
                }
                namespaceUri = CoreUtils.UnescapeUri(element.AsSpan(4, separator - 4));
                name = element.Substring(separator + 1);
            }
            else if (element.Length > 0 && element[0] == '{')
            {
                int separator = element.IndexOf('}', 1);
                if (separator <= 1 || separator + 1 >= element.Length)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBrowseNameInvalid,
                        $"The event select-clause path element '{element}' is not a valid " +
                        "NamespaceUri-qualified browse name.");
                }
                namespaceUri = element.Substring(1, separator - 1);
                name = element.Substring(separator + 1);
            }
            if (namespaceUri is null)
            {
                return QualifiedName.From(name);
            }
            int index = m_session.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadBrowseNameInvalid,
                    $"The event select clause names the namespace '{namespaceUri}', which the " +
                    "Server's namespace table does not hold.");
            }
            return new QualifiedName(name, (ushort)index);
        }

        /// <summary>
        /// Projects a raw <see cref="EventFieldList"/> notification into a
        /// <see cref="WotNotification"/> deterministically: every select-clause
        /// field is captured in <see cref="WotNotification.EventFields"/> keyed
        /// by the browse path the <em>document</em> authored, each carrying the
        /// event's own Time / ReceiveTime as its source / server timestamp so no
        /// timestamp is lost. The notification's primary
        /// <see cref="DataValue"/> wraps the Message field (or the first field,
        /// if Message was not selected) with a <see cref="StatusCodes.Good"/>
        /// status.
        /// </summary>
        /// <remarks>
        /// The key comes from the compiled selection rather than from the
        /// resolved <see cref="SimpleAttributeOperand"/>, whose browse path
        /// renders a non-zero namespace as the connected Server's numeric
        /// index: a <c>data</c> member name that changed with the Server's
        /// namespace table would not be the member the document describes. The
        /// two are index-aligned because the filter is built from the same
        /// selection, clause by clause.
        /// </remarks>
        private static WotNotification BuildEventNotification(
            WotEventSelection selection, EventFieldList eventFields)
        {
            ArrayOf<Variant> values = eventFields.EventFields;
            int count = Math.Min(selection.Clauses.Count, values.Count);

            DateTimeUtc sourceTimestamp = DateTimeUtc.Now;
            DateTimeUtc serverTimestamp = DateTimeUtc.Now;
            for (int i = 0; i < count; i++)
            {
                string name = FormatFieldName(selection.Clauses[i]);
                if (string.Equals(name, EventBrowseNames.Time, StringComparison.Ordinal) &&
                    values[i].TryGetValue(out DateTimeUtc time))
                {
                    sourceTimestamp = time;
                }
                else if (string.Equals(name, EventBrowseNames.ReceiveTime, StringComparison.Ordinal) &&
                    values[i].TryGetValue(out DateTimeUtc receiveTime))
                {
                    serverTimestamp = receiveTime;
                }
            }

            var fields = new Dictionary<string, DataValue>(count, StringComparer.Ordinal);
            Variant primary = Variant.Null;
            bool havePrimary = false;
            for (int i = 0; i < count; i++)
            {
                string name = FormatFieldName(selection.Clauses[i]);
                Variant fieldValue = values[i];
                // Two clauses may select the same path under different
                // EventTypes; the flat envelope can carry one, and the planner
                // reports the collision, so the first stated clause wins rather
                // than the last quietly replacing it.
                if (!fields.ContainsKey(name))
                {
                    fields[name] = new DataValue(
                        fieldValue, StatusCodes.Good, sourceTimestamp, serverTimestamp);
                }
                if (!havePrimary &&
                    string.Equals(name, EventBrowseNames.Message, StringComparison.Ordinal))
                {
                    primary = fieldValue;
                    havePrimary = true;
                }
            }
            // Defensive: the documented default selection always carries the
            // Message field, but an authored selection replaces that default and
            // need not select it. The fallback keeps the notification meaningful
            // when it does not.
            if (!havePrimary && count > 0)
            {
                primary = values[0];
            }

            var dataValue = new DataValue(primary, StatusCodes.Good, sourceTimestamp, serverTimestamp);
            return new WotNotification(dataValue, fields);
        }

        /// <summary>
        /// Names the <c>data</c> member a clause supplies: the browse path the
        /// document authored, or <c>ConditionId</c> for the empty path, which is
        /// the OPC 10000-9 idiom (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        private static string FormatFieldName(WotEventSelectClause clause)
        {
            return clause.IsConditionIdSelection
                ? WotEventSelectClauses.ConditionIdFieldName
                : clause.BrowsePath;
        }

        /// <summary>
        /// The mandatory <c>BaseEventType</c> browse names (Part 5 §6.4.2) the
        /// notification projection recognizes so an event's own Time and
        /// ReceiveTime become the notification's source and server timestamps
        /// and its Message becomes the primary value. The select clauses
        /// themselves come from the compiled selection of WoT Binding
        /// Section 6.1, not from here.
        /// </summary>
        private static class EventBrowseNames
        {
            public const string Time = "Time";
            public const string ReceiveTime = "ReceiveTime";
            public const string Message = "Message";
        }

        /// <summary>
        /// Normalizes an observe interval into a bounded millisecond publishing/sampling interval.
        /// </summary>
        private static int NormalizeInterval(TimeSpan interval)
        {
            double ms = interval.TotalMilliseconds;
            return ms > 100.0 ? (int)ms : 100;
        }

        /// <summary>
        /// Resolves a compiled-form NodeId string to a local <see cref="NodeId"/>.
        /// Plain <c>ns=</c> / <c>i=</c> / <c>s=</c> / <c>g=</c> / <c>b=</c> forms
        /// resolve without a session round-trip. A portable NodeId carrying an
        /// <c>nsu=</c> namespace URI (Part 6 §5.3.1.11) cannot be resolved by
        /// <see cref="NodeId.Parse(string)"/> alone (it always fails for that
        /// form), so it is parsed as an <see cref="ExpandedNodeId"/> and
        /// resolved against the connected session's namespace table.
        /// </summary>
        private bool TryResolveNodeId(string value, out NodeId nodeId)
        {
            if (TryParseNodeId(value, out nodeId))
            {
                return true;
            }
            if (!ExpandedNodeId.TryParse(value, out ExpandedNodeId expanded) || expanded.IsNull)
            {
                nodeId = NodeId.Null;
                return false;
            }
            nodeId = ExpandedNodeId.ToNodeId(expanded, m_session.NamespaceUris);
            return !nodeId.IsNull;
        }

        private static bool TryParseNodeId(string value, out NodeId nodeId)
        {
            try
            {
                nodeId = NodeId.Parse(value);
                return !nodeId.IsNull;
            }
            catch (ServiceResultException)
            {
                nodeId = NodeId.Null;
                return false;
            }
            catch (FormatException)
            {
                nodeId = NodeId.Null;
                return false;
            }
            catch (ArgumentException)
            {
                // NodeId.Parse throws ArgumentException (not ServiceResultException)
                // for a portable "nsu=" / missing-identifier form; treat it the
                // same as any other unparseable text so TryResolveNodeId can fall
                // back to ExpandedNodeId + namespace table resolution.
                nodeId = NodeId.Null;
                return false;
            }
        }

        /// <summary>
        /// A running native OPC UA subscription backing an observe or event
        /// channel. Disposing it removes the monitored item's notification
        /// handler and deletes the subscription server-side (via
        /// <see cref="ISession.RemoveSubscriptionAsync"/>) before releasing the
        /// local <see cref="Subscription"/>, so no session/subscription leaks.
        /// </summary>
        private sealed class OpcUaMonitoredItemSubscription : IWotSubscription
        {
            public OpcUaMonitoredItemSubscription(
                WotCompiledForm form,
                ISession session,
                Subscription subscription,
                MonitoredItem item,
                MonitoredItemNotificationEventHandler handler)
            {
                Form = form;
                m_session = session;
                m_subscription = subscription;
                m_item = item;
                m_handler = handler;
            }

            public WotCompiledForm Form { get; }

            public async ValueTask DisposeAsync()
            {
                m_item.Notification -= m_handler;
                try
                {
                    await m_session.RemoveSubscriptionAsync(m_subscription, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Best-effort server-side cleanup; the session may already
                    // be closed or the subscription already removed.
                }
                m_subscription.Dispose();
            }

            private readonly ISession m_session;
            private readonly Subscription m_subscription;
            private readonly MonitoredItem m_item;
            private readonly MonitoredItemNotificationEventHandler m_handler;
        }

        private readonly ISession m_session;
        private readonly bool m_disposeSession;
        private readonly OpcUaWotBindingOptions m_options;
        private readonly string m_nodeId;
    }
}
