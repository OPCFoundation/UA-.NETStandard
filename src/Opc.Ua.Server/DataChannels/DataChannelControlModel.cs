/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

namespace Opc.Ua.Server.DataChannels
{
    /// <summary>
    /// Control-plane comparison and normal Call operation result mapping.
    /// </summary>
    internal static class DataChannelControlModel
    {
        /// <summary>
        /// Captures the generated descriptor without retaining caller-owned mutable state.
        /// </summary>
        internal static DataChannelParametersDataType Copy(DataChannelParametersDataType value)
        {
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
        /// Checks every immutable selection.
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
        /// Checks an idempotent proposal without omitting any mutable bound.
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
        /// Maps flow outcomes to standard Call operation StatusCodes, never new Services or statuses.
        /// </summary>
        internal static StatusCode ToStatusCode(DataChannelResult result)
        {
            return result switch
            {
                DataChannelResult.Good => StatusCodes.Good,
                DataChannelResult.Unsupported => StatusCodes.BadNotSupported,
                DataChannelResult.InvalidArgument => StatusCodes.BadInvalidArgument,
                DataChannelResult.InvalidState or DataChannelResult.Closed => StatusCodes.BadInvalidState,
                DataChannelResult.NotAuthorized => StatusCodes.BadUserAccessDenied,
                DataChannelResult.SecurityFailure => StatusCodes.BadSecurityChecksFailed,
                DataChannelResult.TooLarge or DataChannelResult.WouldBlock => StatusCodes.BadEncodingLimitsExceeded,
                DataChannelResult.Timeout => StatusCodes.BadTimeout,
                _ => StatusCodes.BadNodeIdUnknown
            };
        }

        /// <summary>
        /// Classifies standard Call failures for safe, typed audit attribution.
        /// </summary>
        internal static DataChannelResult FromStatusCode(StatusCode status)
        {
            if (status == StatusCodes.BadSecurityChecksFailed)
            {
                return DataChannelResult.SecurityFailure;
            }
            if (status == StatusCodes.BadTimeout)
            {
                return DataChannelResult.Timeout;
            }
            if (status == StatusCodes.BadNotSupported)
            {
                return DataChannelResult.Unsupported;
            }
            if (status == StatusCodes.BadEncodingLimitsExceeded)
            {
                return DataChannelResult.TooLarge;
            }
            if (status == StatusCodes.BadInvalidArgument)
            {
                return DataChannelResult.InvalidArgument;
            }
            if (status == StatusCodes.BadNodeIdUnknown)
            {
                return DataChannelResult.UnknownChannel;
            }
            if (status == StatusCodes.BadInvalidState)
            {
                return DataChannelResult.InvalidState;
            }
            return DataChannelResult.NotAuthorized;
        }
    }
}
