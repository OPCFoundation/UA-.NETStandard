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
using System.Text;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Maps generated model values to the one private wire representation.
    /// </summary>
    internal static class FlowModel
    {
        /// <summary>
        /// Copies an application-owned generated descriptor into runtime-owned state.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        internal static DataChannelParametersDataType Copy(DataChannelParametersDataType value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            return new DataChannelParametersDataType
            {
                ResourceUri = value.ResourceUri,
                Protocol = value.Protocol,
                ContentType = value.ContentType,
                Direction = value.Direction,
                Delivery = value.Delivery,
                Protection = value.Protection,
                MaxMessageSize = value.MaxMessageSize,
                MaxReceiveBytes = value.MaxReceiveBytes,
                MaxRate = value.MaxRate == 0 ? 0 : value.MaxRate,
                Priority = value.Priority,
                AllowQueueExpiry = value.AllowQueueExpiry
            };
        }

        /// <summary>
        /// Converts an admitted model descriptor to typed wire fields.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        internal static FlowWireParameters ToWire(DataChannelParametersDataType value)
        {
            return new FlowWireParameters(
                value.ResourceUri ?? throw new DataChannelException(DataChannelResult.InvalidArgument),
                value.Protocol ?? throw new DataChannelException(DataChannelResult.InvalidArgument),
                value.ContentType ?? string.Empty,
                value.Direction switch
                {
                    DataChannelDirection.OpenerToAcceptor => FlowWireDirection.OpenerToAcceptor,
                    DataChannelDirection.AcceptorToOpener => FlowWireDirection.AcceptorToOpener,
                    DataChannelDirection.Duplex => FlowWireDirection.Duplex,
                    _ => throw new DataChannelException(DataChannelResult.InvalidArgument)
                },
                value.Delivery switch
                {
                    DataChannelDeliveryMode.Ordered => FlowWireDelivery.Ordered,
                    DataChannelDeliveryMode.Datagram => FlowWireDelivery.Datagram,
                    _ => throw new DataChannelException(DataChannelResult.InvalidArgument)
                },
                ToWire(value.Protection),
                Limits(value),
                value.AllowQueueExpiry);
        }

        /// <summary>
        /// Converts a typed wire descriptor to the authoritative generated model.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        internal static DataChannelParametersDataType FromWire(FlowWireParameters value)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = value.Resource,
                Protocol = value.Protocol,
                ContentType = value.ContentType,
                Direction = value.Direction switch
                {
                    FlowWireDirection.OpenerToAcceptor => DataChannelDirection.OpenerToAcceptor,
                    FlowWireDirection.AcceptorToOpener => DataChannelDirection.AcceptorToOpener,
                    FlowWireDirection.Duplex => DataChannelDirection.Duplex,
                    _ => throw new DataChannelException(DataChannelResult.InvalidArgument)
                },
                Delivery = value.Delivery == FlowWireDelivery.Ordered
                    ? DataChannelDeliveryMode.Ordered
                    : DataChannelDeliveryMode.Datagram,
                Protection = value.Protection == FlowWireProtection.Confidential
                    ? DataChannelProtectionMode.Confidential
                    : DataChannelProtectionMode.Integrity,
                MaxMessageSize = value.Limits.MaxMessageSize,
                MaxReceiveBytes = value.Limits.MaxReceiveBytes,
                MaxRate = value.Limits.MaxRate,
                Priority = value.Limits.Priority,
                AllowQueueExpiry = value.AllowQueueExpiry
            };
        }

        /// <summary>
        /// Gets just the revisable values.
        /// </summary>
        internal static FlowWireLimits Limits(DataChannelParametersDataType value)
        {
            return new FlowWireLimits(value.MaxMessageSize, value.MaxReceiveBytes, value.MaxRate, value.Priority);
        }

        /// <summary>
        /// Applies complete proposed limits without mutating an existing descriptor.
        /// </summary>
        internal static DataChannelParametersDataType WithLimits(
            DataChannelParametersDataType value,
            FlowWireLimits limits)
        {
            DataChannelParametersDataType copy = Copy(value);
            copy.MaxMessageSize = limits.MaxMessageSize;
            copy.MaxReceiveBytes = limits.MaxReceiveBytes;
            copy.MaxRate = limits.MaxRate;
            copy.Priority = limits.Priority;
            return copy;
        }

        /// <summary>
        /// Compares every immutable descriptor field without URI normalization.
        /// </summary>
        internal static bool SameDescriptor(DataChannelParametersDataType left, DataChannelParametersDataType right)
        {
            return left.ResourceUri == right.ResourceUri &&
                left.Protocol == right.Protocol &&
                left.ContentType == right.ContentType &&
                left.Direction == right.Direction &&
                left.Delivery == right.Delivery &&
                left.Protection == right.Protection &&
                left.AllowQueueExpiry == right.AllowQueueExpiry;
        }

        /// <summary>
        /// Compares a complete revision proposal.
        /// </summary>
        internal static bool SameParameters(DataChannelParametersDataType left, DataChannelParametersDataType right)
        {
            return SameDescriptor(left, right) &&
                left.MaxMessageSize == right.MaxMessageSize &&
                left.MaxReceiveBytes == right.MaxReceiveBytes &&
                left.MaxRate == right.MaxRate &&
                left.Priority == right.Priority;
        }

        /// <summary>
        /// Checks that ACCEPT did not broaden a request or silently alter its behavior.
        /// </summary>
        internal static bool IsNarrowing(DataChannelParametersDataType request, DataChannelParametersDataType admitted)
        {
            return SameDescriptor(request, admitted) &&
                admitted.MaxMessageSize <= request.MaxMessageSize &&
                admitted.MaxReceiveBytes <= request.MaxReceiveBytes &&
                admitted.Priority == request.Priority &&
                (request.MaxRate == 0 || (admitted.MaxRate > 0 && admitted.MaxRate <= request.MaxRate));
        }

        /// <summary>
        /// Validates an absolute bounded URI without fetching or normalizing it.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static void ValidateUri(string value, string name)
        {
            if (string.IsNullOrEmpty(value) ||
                Encoding.UTF8.GetByteCount(value) > 4096 ||
                !Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute, bounded URI is required.", name);
            }
        }

        /// <summary>
        /// Maps the selected endpoint profile without changing the runtime's Service-sharing policy.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static FlowCarrier ToWire(DataChannelCarrierKind value, string? transportProfileUri = null)
        {
            if (transportProfileUri == DataChannelProfiles.PubSubUasc)
            {
                return value == DataChannelCarrierKind.SharedUasc
                    ? FlowCarrier.DedicatedUasc
                    : throw new ArgumentOutOfRangeException(nameof(value));
            }
            return value switch
            {
                DataChannelCarrierKind.DedicatedUasc => FlowCarrier.DedicatedUasc,
                DataChannelCarrierKind.SharedUasc => FlowCarrier.SharedUasc,
                DataChannelCarrierKind.Quic or DataChannelCarrierKind.QuicUasc => FlowCarrier.Quic,
                DataChannelCarrierKind.WebTransport or DataChannelCarrierKind.WebTransportUasc => FlowCarrier.WebTransport,
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };
        }

        /// <summary>
        /// Identifies carriers whose management messages use the authenticated UASC writer.
        /// </summary>
        internal static bool IsUasc(DataChannelCarrierKind kind)
        {
            return kind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc or
                DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc or
                DataChannelCarrierKind.WebTransport;
        }

        /// <summary>
        /// Identifies UASC baselines that also carry ordinary Service messages.
        /// </summary>
        internal static bool SharesServices(DataChannelCarrierKind? kind)
        {
            return kind is DataChannelCarrierKind.SharedUasc or
                DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc;
        }

        /// <summary>
        /// Explicitly maps the validated protection.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        internal static FlowWireProtection ToWire(DataChannelProtectionMode value)
        {
            return value switch
            {
                DataChannelProtectionMode.Confidential => FlowWireProtection.Confidential,
                DataChannelProtectionMode.Integrity => FlowWireProtection.Integrity,
                _ => throw new DataChannelException(DataChannelResult.InvalidArgument)
            };
        }

        /// <summary>
        /// Explicitly maps a generated result to a closed wire discriminator.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static FlowWireStatus ToWire(DataChannelResult value)
        {
            return value switch
            {
                DataChannelResult.Good => FlowWireStatus.Good,
                DataChannelResult.Unsupported => FlowWireStatus.Unsupported,
                DataChannelResult.InvalidArgument => FlowWireStatus.InvalidArgument,
                DataChannelResult.InvalidState => FlowWireStatus.InvalidState,
                DataChannelResult.NotAuthorized => FlowWireStatus.NotAuthorized,
                DataChannelResult.SecurityFailure => FlowWireStatus.SecurityFailure,
                DataChannelResult.TooLarge => FlowWireStatus.TooLarge,
                DataChannelResult.Timeout => FlowWireStatus.Timeout,
                DataChannelResult.Closed => FlowWireStatus.Closed,
                DataChannelResult.UnknownChannel => FlowWireStatus.UnknownChannel,
                DataChannelResult.WouldBlock => FlowWireStatus.WouldBlock,
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };
        }

        /// <summary>
        /// Explicitly maps a wire outcome to the generated model.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static DataChannelResult FromWire(FlowWireStatus value)
        {
            return value switch
            {
                FlowWireStatus.Good => DataChannelResult.Good,
                FlowWireStatus.Unsupported => DataChannelResult.Unsupported,
                FlowWireStatus.InvalidArgument => DataChannelResult.InvalidArgument,
                FlowWireStatus.InvalidState => DataChannelResult.InvalidState,
                FlowWireStatus.NotAuthorized => DataChannelResult.NotAuthorized,
                FlowWireStatus.SecurityFailure => DataChannelResult.SecurityFailure,
                FlowWireStatus.TooLarge => DataChannelResult.TooLarge,
                FlowWireStatus.Timeout => DataChannelResult.Timeout,
                FlowWireStatus.Closed => DataChannelResult.Closed,
                FlowWireStatus.UnknownChannel => DataChannelResult.UnknownChannel,
                FlowWireStatus.WouldBlock => DataChannelResult.WouldBlock,
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };
        }
    }
}
