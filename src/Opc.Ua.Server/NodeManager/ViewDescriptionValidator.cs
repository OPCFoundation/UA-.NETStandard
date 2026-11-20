// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Validates the parameters of a <see cref="ViewDescription"/> that do not
    /// depend on the View node itself (OPC 10000-4, 7.44 and Table 178).
    /// </summary>
    internal static class ViewDescriptionValidator
    {
        /// <summary>
        /// Throws if the timestamp and version of the view description are
        /// inconsistent with each other or with the view id.
        /// </summary>
        /// <remarks>
        /// Either the Timestamp or the ViewVersion may be set, but not both
        /// (Bad_ViewParameterMismatch). Both select a version of a View, so
        /// setting either without a ViewId (the entire AddressSpace) requests
        /// a version that is not available (Bad_ViewTimestampInvalid or
        /// Bad_ViewVersionInvalid) rather than an unknown View.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        public static void ValidateParameters(ViewDescription? view)
        {
            if (view == null)
            {
                return;
            }

            bool hasTimestamp = view.Timestamp != DateTimeUtc.MinValue;
            bool hasVersion = view.ViewVersion != 0;

            if (hasTimestamp && hasVersion)
            {
                throw new ServiceResultException(StatusCodes.BadViewParameterMismatch);
            }

            if (view.ViewId.IsNull)
            {
                if (hasTimestamp)
                {
                    throw new ServiceResultException(StatusCodes.BadViewTimestampInvalid);
                }

                if (hasVersion)
                {
                    throw new ServiceResultException(StatusCodes.BadViewVersionInvalid);
                }
            }
        }
    }
}
