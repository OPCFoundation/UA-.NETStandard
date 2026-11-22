// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.PlasticsRubber.Client
{
    /// <summary>
    /// Shares generated-event decoding and verified application-subtype resolution across common workflows.
    /// </summary>
    internal static class PlasticsRubberEventStream
    {
        /// <summary>
        /// Decodes exact generated records or verified concrete derivatives of the requested common event.
        /// </summary>
        /// <typeparam name="T">The generated event record expected by the workflow.</typeparam>
        internal static async IAsyncEnumerable<T> ObserveAsync<T>(
            ISession session,
            NodeId notifier,
            NodeId declaredType,
            EventRecordDecoderRegistry registry,
            EventFilter filter,
            IStreamingSubscription streaming,
            MonitoringOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
            where T : BaseEventTypeRecord
        {
            await foreach (EventNotification notification in streaming.SubscribeEventsAsync(
                notifier, filter, options, cancellationToken).ConfigureAwait(false))
            {
                Variant[] fields = notification.Fields.ToArray() ??
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                if (registry.Decode(fields) is T decoded)
                {
                    yield return decoded;
                    continue;
                }
                NodeId actual = NodeId.Null;
                for (int index = 0; index < registry.StandardFields.Length && index < fields.Length; index++)
                {
                    QualifiedName[] path = registry.StandardFields[index];
                    if (path.Length == 1 && path[0].Name == Opc.Ua.BrowseNames.EventType)
                    {
                        fields[index].TryGetValue(out actual);
                        break;
                    }
                }
                if (actual.IsNull ||
                    !await session.NodeCache.IsTypeOfAsync(actual, declaredType, cancellationToken)
                        .ConfigureAwait(false) ||
                    registry.DecodeAs(declaredType, fields) is not T subtype)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }
                yield return subtype;
            }
        }
    }
}
