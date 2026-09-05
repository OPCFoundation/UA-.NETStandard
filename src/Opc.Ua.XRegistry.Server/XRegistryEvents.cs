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
using System.Collections.Immutable;
using System.Linq;
using Opc.Ua.XRegistry;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Identifies one concrete xRegistry event type.
    /// </summary>
    internal enum XRegistryEventKind
    {
        RegistryCreated,
        RegistryUpdated,
        RegistryDeleted,
        ModelUpdated,
        ModelSourceUpdated,
        CapabilitiesUpdated,
        GroupCreated,
        GroupUpdated,
        GroupDeprecated,
        GroupUndeprecated,
        GroupDeleted,
        ResourceCreated,
        ResourceUpdated,
        ResourceDeprecated,
        ResourceUndeprecated,
        ResourceDeleted,
        VersionCreated,
        VersionUpdated,
        VersionDeleted
    }

    /// <summary>
    /// Immutable description of one xRegistry event before coalescing and publication.
    /// </summary>
    internal sealed record XRegistryEventChange(
        XRegistryEventKind Kind,
        string Subject,
        NodeId SourceNodeId,
        uint? Epoch = null,
        uint? MetaEpoch = null,
        ImmutableArray<string> Changed = default)
    {
        /// <summary>
        /// Gets the event SourceName. Deleted entities retain their former display name.
        /// </summary>
        public string? SourceName { get; init; }

        /// <summary>
        /// Gets the surviving node through which this event is reported.
        /// </summary>
        public NodeState? Notifier { get; init; }
    }

    /// <summary>
    /// Coalesces all changes produced by one logical registry interaction.
    /// </summary>
    internal static class XRegistryEventCoalescer
    {
        /// <summary>
        /// Applies lifecycle precedence and merges, sorts and de-duplicates Changed names.
        /// </summary>
        public static ImmutableArray<XRegistryEventChange> Coalesce(
            IEnumerable<XRegistryEventChange> changes)
        {
            if (changes is null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            var selected = new Dictionary<(string Family, string Subject), XRegistryEventChange>();
            foreach (XRegistryEventChange change in changes)
            {
                if (string.IsNullOrWhiteSpace(change.Subject))
                {
                    throw new ArgumentException("An xRegistry event Subject is required.", nameof(changes));
                }

                string family = CoalescingFamily(change.Kind);
                var key = (family, change.Subject);
                XRegistryEventChange normalized = Normalize(change);
                if (!selected.TryGetValue(key, out XRegistryEventChange? current))
                {
                    selected.Add(key, normalized);
                    continue;
                }

                int currentPrecedence = Precedence(current.Kind);
                int nextPrecedence = Precedence(normalized.Kind);
                if (nextPrecedence > currentPrecedence)
                {
                    selected[key] = normalized;
                }
                else if (nextPrecedence == currentPrecedence && current.Kind == normalized.Kind)
                {
                    selected[key] = normalized with
                    {
                        Epoch = normalized.Epoch ?? current.Epoch,
                        MetaEpoch = normalized.MetaEpoch ?? current.MetaEpoch,
                        Changed = MergeChanged(current.Changed, normalized.Changed)
                    };
                }
            }

            return selected.Values
                .OrderBy(change => EventOrder(change.Kind))
                .ThenBy(change => change.Subject, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        private static XRegistryEventChange Normalize(XRegistryEventChange change)
        {
            return change with { Changed = MergeChanged(change.Changed, default) };
        }

        private static ImmutableArray<string> MergeChanged(
            ImmutableArray<string> left,
            ImmutableArray<string> right)
        {
            left = left.IsDefault ? ImmutableArray<string>.Empty : left;
            right = right.IsDefault ? ImmutableArray<string>.Empty : right;
            return left.IsEmpty && right.IsEmpty
                ? ImmutableArray<string>.Empty
                : left.Concat(right)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToImmutableArray();
        }

        private static string CoalescingFamily(XRegistryEventKind kind)
        {
            return kind switch
            {
                XRegistryEventKind.RegistryCreated or
                XRegistryEventKind.RegistryUpdated or
                XRegistryEventKind.RegistryDeleted => "registry",
                XRegistryEventKind.GroupCreated or
                XRegistryEventKind.GroupUpdated or
                XRegistryEventKind.GroupDeleted => "group",
                XRegistryEventKind.ResourceCreated or
                XRegistryEventKind.ResourceUpdated or
                XRegistryEventKind.ResourceDeleted => "resource",
                XRegistryEventKind.VersionCreated or
                XRegistryEventKind.VersionUpdated or
                XRegistryEventKind.VersionDeleted => "version",
                _ => kind.ToString()
            };
        }

        private static int Precedence(XRegistryEventKind kind)
        {
            return kind switch
            {
                XRegistryEventKind.RegistryDeleted or
                XRegistryEventKind.GroupDeleted or
                XRegistryEventKind.ResourceDeleted or
                XRegistryEventKind.VersionDeleted => 3,
                XRegistryEventKind.RegistryCreated or
                XRegistryEventKind.GroupCreated or
                XRegistryEventKind.ResourceCreated or
                XRegistryEventKind.VersionCreated => 2,
                _ => 1
            };
        }

        private static int EventOrder(XRegistryEventKind kind)
        {
            return kind switch
            {
                XRegistryEventKind.VersionDeleted => 0,
                XRegistryEventKind.ResourceDeleted => 1,
                XRegistryEventKind.GroupDeleted => 2,
                XRegistryEventKind.RegistryDeleted => 3,
                XRegistryEventKind.RegistryCreated => 10,
                XRegistryEventKind.GroupCreated => 11,
                XRegistryEventKind.ResourceCreated => 12,
                XRegistryEventKind.VersionCreated => 13,
                _ => 20 + (int)kind
            };
        }
    }

    /// <summary>
    /// Builds and reports native OPC UA events from coalesced xRegistry changes.
    /// </summary>
    internal sealed class XRegistryEventEmitter
    {
        /// <summary>
        /// Initializes an emitter.
        /// </summary>
        public XRegistryEventEmitter(ISystemContext context, string eventSourceUrl)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            if (!Uri.TryCreate(eventSourceUrl, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "The xRegistry EventSourceUrl must be an absolute URI.",
                    nameof(eventSourceUrl));
            }
            m_eventSourceUrl = eventSourceUrl;
        }

        /// <summary>
        /// Reports one coalesced interaction through a surviving notifier.
        /// </summary>
        public void Report(NodeState notifier, IEnumerable<XRegistryEventChange> changes)
        {
            if (notifier is null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }

            ImmutableArray<XRegistryEventChange> batch = XRegistryEventCoalescer.Coalesce(changes);
            DateTimeUtc commonTime = DateTimeUtc.Now;
            foreach (XRegistryEventChange change in batch)
            {
                NodeState reporter = change.Notifier ?? notifier;
                reporter.ReportEvent(m_context, BuildEvent(reporter, change, commonTime));
            }
        }

        internal BaseEventState BuildEvent(
            NodeState notifier,
            XRegistryEventChange change,
            DateTimeUtc time)
        {
            BaseEventState evt = change.Kind switch
            {
                XRegistryEventKind.RegistryCreated =>
                    m_context.CreateInstanceOfRegistryCreatedEventType(notifier),
                XRegistryEventKind.RegistryUpdated =>
                    m_context.CreateInstanceOfRegistryUpdatedEventType(notifier),
                XRegistryEventKind.RegistryDeleted =>
                    m_context.CreateInstanceOfRegistryDeletedEventType(notifier),
                XRegistryEventKind.ModelUpdated =>
                    m_context.CreateInstanceOfModelUpdatedEventType(notifier),
                XRegistryEventKind.ModelSourceUpdated =>
                    m_context.CreateInstanceOfModelSourceUpdatedEventType(notifier),
                XRegistryEventKind.CapabilitiesUpdated =>
                    m_context.CreateInstanceOfCapabilitiesUpdatedEventType(notifier),
                XRegistryEventKind.GroupCreated =>
                    m_context.CreateInstanceOfGroupCreatedEventType(notifier),
                XRegistryEventKind.GroupUpdated =>
                    m_context.CreateInstanceOfGroupUpdatedEventType(notifier),
                XRegistryEventKind.GroupDeprecated =>
                    m_context.CreateInstanceOfGroupDeprecatedEventType(notifier),
                XRegistryEventKind.GroupUndeprecated =>
                    m_context.CreateInstanceOfGroupUndeprecatedEventType(notifier),
                XRegistryEventKind.GroupDeleted =>
                    m_context.CreateInstanceOfGroupDeletedEventType(notifier),
                XRegistryEventKind.ResourceCreated =>
                    m_context.CreateInstanceOfResourceCreatedEventType(notifier),
                XRegistryEventKind.ResourceUpdated =>
                    m_context.CreateInstanceOfResourceUpdatedEventType(notifier),
                XRegistryEventKind.ResourceDeprecated =>
                    m_context.CreateInstanceOfResourceDeprecatedEventType(notifier),
                XRegistryEventKind.ResourceUndeprecated =>
                    m_context.CreateInstanceOfResourceUndeprecatedEventType(notifier),
                XRegistryEventKind.ResourceDeleted =>
                    m_context.CreateInstanceOfResourceDeletedEventType(notifier),
                XRegistryEventKind.VersionCreated =>
                    m_context.CreateInstanceOfVersionCreatedEventType(notifier),
                XRegistryEventKind.VersionUpdated =>
                    m_context.CreateInstanceOfVersionUpdatedEventType(notifier),
                XRegistryEventKind.VersionDeleted =>
                    m_context.CreateInstanceOfVersionDeletedEventType(notifier),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };

            evt.Initialize(
                m_context,
                notifier,
                EventSeverity.Medium,
                new LocalizedText(change.Kind.ToString()));
            evt.SourceNode!.Value = change.SourceNodeId;
            evt.SourceName!.Value = change.SourceName ?? change.Subject;
            evt.Time!.Value = time;
            evt.ReceiveTime!.Value = time;

            var xregistry = (XRegistryEventState)evt;
            xregistry.SourceUrl!.Value = m_eventSourceUrl;
            xregistry.Subject!.Value = change.Subject;
            PopulateTypedFields(evt, change);
            return evt;
        }

        private void PopulateTypedFields(BaseEventState evt, XRegistryEventChange change)
        {
            switch (evt)
            {
                case RegistryCreatedEventState state:
                    state.Epoch!.Value = Required(change.Epoch, change.Kind);
                    break;
                case RegistryUpdatedEventState state:
                    state.Epoch!.Value = Required(change.Epoch, change.Kind);
                    SetChanged(state.AddChanged(m_context).Changed, change.Changed);
                    break;
                case CapabilitiesUpdatedEventState state:
                    SetChanged(state.AddChanged(m_context).Changed, change.Changed);
                    break;
                case GroupCreatedEventState state:
                    state.Epoch!.Value = Required(change.Epoch, change.Kind);
                    break;
                case GroupUpdatedEventState state:
                    state.Epoch!.Value = Required(change.Epoch, change.Kind);
                    SetChanged(state.AddChanged(m_context).Changed, change.Changed);
                    break;
                case ResourceCreatedEventState state:
                    state.Epoch!.Value = Required(change.Epoch, change.Kind);
                    state.MetaEpoch!.Value = Required(change.MetaEpoch, change.Kind);
                    break;
                case ResourceUpdatedEventState state:
                    state.Epoch!.Value = Required(change.Epoch, change.Kind);
                    state.MetaEpoch!.Value = Required(change.MetaEpoch, change.Kind);
                    SetChanged(state.AddChanged(m_context).Changed, change.Changed);
                    break;
                case VersionCreatedEventState state:
                    state.Epoch!.Value = Required(change.Epoch, change.Kind);
                    break;
                case VersionUpdatedEventState state:
                    state.Epoch!.Value = Required(change.Epoch, change.Kind);
                    SetChanged(state.AddChanged(m_context).Changed, change.Changed);
                    break;
            }
        }

        private static uint Required(uint? value, XRegistryEventKind kind)
        {
            return value ?? throw new InvalidOperationException(
                $"{kind} requires an epoch value.");
        }

        private static void SetChanged(
            PropertyState<ArrayOf<string>>? property,
            ImmutableArray<string> changed)
        {
            if (property is not null && !changed.IsDefaultOrEmpty)
            {
                property.Value = new ArrayOf<string>(changed.ToArray());
            }
        }

        private readonly ISystemContext m_context;
        private readonly string m_eventSourceUrl;
    }
}
