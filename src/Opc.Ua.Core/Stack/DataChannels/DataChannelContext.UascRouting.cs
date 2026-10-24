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
using System.Linq;
using System.Threading;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Registers the configured UADP PublisherId associated with a resource on this UASC context.
        /// A null PublisherId registers a wildcard for a single unambiguous resource.
        /// </summary>
        /// <param name="resourceUri">The exact resource URI configured for the PubSub flow.</param>
        /// <param name="publisherId">The expected PublisherId, or null to accept any PublisherId.</param>
        /// <returns>An ownership token that removes the association when disposed.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="DataChannelException"></exception>
        public IDisposable RegisterUascPublisherRoute(string resourceUri, Variant publisherId)
        {
            return RegisterUascPublisherRoute(resourceUri, publisherId, null, null);
        }

        /// <summary>
        /// Registers one configured UADP source and writer association with a resource on this UASC context.
        /// A null PublisherId, WriterGroupId, or DataSetWriterId is a wildcard for that field.
        /// </summary>
        /// <param name="resourceUri">The exact resource URI configured for the PubSub flow.</param>
        /// <param name="publisherId">The expected PublisherId, or null to accept any PublisherId.</param>
        /// <param name="writerGroupId">The expected visible WriterGroupId, or null to accept any group.</param>
        /// <param name="dataSetWriterId">The expected visible DataSetWriterId, or null to accept any writer.</param>
        /// <returns>An ownership token that removes the association when disposed.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="DataChannelException"></exception>
        public IDisposable RegisterUascPublisherRoute(
            string resourceUri,
            Variant publisherId,
            ushort? writerGroupId,
            ushort? dataSetWriterId)
        {
            return RegisterUascPublisherRoute(resourceUri, publisherId, writerGroupId, dataSetWriterId, null);
        }

        /// <summary>
        /// Registers one configured UADP source, writer, and NetworkMessageType association with a resource.
        /// Null selectors accept any value for that field.
        /// Minimal application-payload routes cannot select absent WriterGroupId or DataSetWriterId fields.
        /// Shared-UASC streaming admission snapshots finite typed receive associations before READY.
        /// Registration changes affect future admissions, not an admitted streaming flow.
        /// </summary>
        /// <param name="resourceUri">The exact resource URI configured for the PubSub flow.</param>
        /// <param name="publisherId">The expected PublisherId, or null to accept any PublisherId.</param>
        /// <param name="writerGroupId">The expected visible WriterGroupId, or null to accept any group.</param>
        /// <param name="dataSetWriterId">The expected visible DataSetWriterId, or null to accept any writer.</param>
        /// <param name="networkMessageType">The Part 14 NetworkMessageType value, or null for any class.</param>
        /// <returns>An ownership token that removes the association when disposed.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="DataChannelException"></exception>
        public IDisposable RegisterUascPublisherRoute(
            string resourceUri,
            Variant publisherId,
            ushort? writerGroupId,
            ushort? dataSetWriterId,
            byte? networkMessageType)
        {
            if (string.IsNullOrWhiteSpace(resourceUri))
            {
                throw new ArgumentNullException(nameof(resourceUri));
            }
            if (System.Text.Encoding.UTF8.GetByteCount(resourceUri) > 4096 ||
                !Uri.TryCreate(resourceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("A bounded absolute resource URI is required.", nameof(resourceUri));
            }
            if (IsNative)
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }
            if (!publisherId.IsNull)
            {
                UadpApplicationPayloadHeaderCodec.ValidatePublisherId(publisherId);
            }
            if (networkMessageType is > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(networkMessageType));
            }
            if (networkMessageType == 3 && (writerGroupId.HasValue || dataSetWriterId.HasValue))
            {
                throw new ArgumentException(
                    "Minimal application-payload headers contain no writer group or DataSet writer identifiers.",
                    nameof(networkMessageType));
            }

            lock (m_gate)
            {
                if (m_closed)
                {
                    throw new DataChannelException(DataChannelResult.Closed);
                }
                UascPublisherRoute? route = m_uascPublisherRoutes.Find(item =>
                    item.PublisherId.Equals(publisherId) &&
                    item.WriterGroupId == writerGroupId &&
                    item.DataSetWriterId == dataSetWriterId &&
                    item.NetworkMessageType == networkMessageType);
                UascPublisherRoute? conflictingRoute = m_uascPublisherRoutes.Find(item =>
                    !string.Equals(item.ResourceUri, resourceUri, StringComparison.Ordinal) &&
                    RoutesConflict(item, publisherId, writerGroupId, dataSetWriterId, networkMessageType));
                if (conflictingRoute is not null)
                {
                    throw new DataChannelException(DataChannelResult.InvalidArgument);
                }
                if (route is null)
                {
                    route = new UascPublisherRoute(
                        publisherId, writerGroupId, dataSetWriterId, networkMessageType, resourceUri);
                    m_uascPublisherRoutes.Add(route);
                }
                route.Registrations++;
                return new UascPublisherRouteRegistration(ReleaseUascPublisherRoute, route);
            }
        }

        /// <summary>
        /// Reserves finite typed receive associations before a streaming flow can publish ACCEPT or READY.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private void BindUascStreamingPublishersLocked(FlowState flow)
        {
            if (!FlowModel.SharesServices(m_carrier.Kind) || flow.DirectLane != null || !flow.MayReceive ||
                flow.Parameters.Protocol is not (DataChannelProfiles.UadpAvApplicationPayload or
                    DataChannelProfiles.UadpOpaqueApplicationPayload))
            {
                return;
            }
            ArrayOf<Variant> publishers = [.. m_uascPublisherRoutes
                .Where(route => string.Equals(
                    route.ResourceUri, flow.Parameters.ResourceUri, StringComparison.Ordinal) &&
                    !route.PublisherId.IsNull && !route.WriterGroupId.HasValue && !route.DataSetWriterId.HasValue &&
                    route.NetworkMessageType is null or 3)
                .Select(static route => route.PublisherId)
                .Distinct()];
            if (publishers.IsEmpty || m_flows.Values.Any(existing =>
                existing != flow && !existing.IsTerminal && existing.MayReceive &&
                existing.UascStreamingPublisherIds.Contains(publisher => publishers.Contains(publisher))))
            {
                throw new DataChannelException(DataChannelResult.InvalidArgument);
            }
            flow.UascStreamingPublisherIds = publishers;
        }

        /// <summary>
        /// Prevents a broad PublisherId-only association from shadowing another resource.
        /// </summary>
        private static bool RoutesConflict(
            UascPublisherRoute existing,
            Variant publisherId,
            ushort? writerGroupId,
            ushort? dataSetWriterId,
            byte? networkMessageType)
        {
            if (!existing.PublisherId.Equals(publisherId))
            {
                return false;
            }
            bool sameAssociation = existing.WriterGroupId == writerGroupId &&
                existing.DataSetWriterId == dataSetWriterId &&
                existing.NetworkMessageType == networkMessageType;
            bool existingPublisherOnly = !existing.WriterGroupId.HasValue &&
                !existing.DataSetWriterId.HasValue &&
                !existing.NetworkMessageType.HasValue;
            bool requestedPublisherOnly = !writerGroupId.HasValue && !dataSetWriterId.HasValue;
            requestedPublisherOnly &= !networkMessageType.HasValue;
            return sameAssociation || existingPublisherOnly || requestedPublisherOnly;
        }

        /// <summary>
        /// Releases one configured route reference without disturbing other owners.
        /// </summary>
        private void ReleaseUascPublisherRoute(UascPublisherRoute route)
        {
            lock (m_gate)
            {
                if (route.Registrations == 0)
                {
                    return;
                }
                route.Registrations--;
                if (route.Registrations == 0)
                {
                    _ = m_uascPublisherRoutes.Remove(route);
                }
            }
        }

        /// <summary>
        /// Holds one configured publisher-to-resource association.
        /// </summary>
        private sealed class UascPublisherRoute(
            Variant publisherId,
            ushort? writerGroupId,
            ushort? dataSetWriterId,
            byte? networkMessageType,
            string resourceUri)
        {
            /// <summary>
            /// Gets the expected PublisherId, or null for a wildcard.
            /// </summary>
            internal Variant PublisherId { get; } = publisherId;

            /// <summary>
            /// Gets the expected WriterGroupId, or null for a wildcard.
            /// </summary>
            internal ushort? WriterGroupId { get; } = writerGroupId;

            /// <summary>
            /// Gets the expected DataSetWriterId, or null for a wildcard.
            /// </summary>
            internal ushort? DataSetWriterId { get; } = dataSetWriterId;

            /// <summary>
            /// Gets the expected NetworkMessageType, or null for a wildcard.
            /// </summary>
            internal byte? NetworkMessageType { get; } = networkMessageType;

            /// <summary>
            /// Gets the exact resource URI for the association.
            /// </summary>
            internal string ResourceUri { get; } = resourceUri;

            /// <summary>
            /// Gets or sets the number of active registration owners.
            /// </summary>
            internal int Registrations { get; set; }
        }

        /// <summary>
        /// Releases its route at most once.
        /// </summary>
        private sealed class UascPublisherRouteRegistration(
            Action<UascPublisherRoute> release,
            UascPublisherRoute route) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                Action<UascPublisherRoute>? releaseAction = Interlocked.Exchange(ref m_release, null);
                releaseAction?.Invoke(route);
            }

            private Action<UascPublisherRoute>? m_release = release;
        }
    }
}
