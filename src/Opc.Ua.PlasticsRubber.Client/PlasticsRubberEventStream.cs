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
