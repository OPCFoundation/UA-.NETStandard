/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.WotCon.Server.Assets;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Validates xRegistry label (attribute) keys and values before they flow
    /// into a materialized <c>Labels</c> (AttributesType) container: the key
    /// becomes the child <see cref="QualifiedName"/> BrowseName and a
    /// deterministic <see cref="NodeId"/> path segment. Reuses
    /// <see cref="WotChildNameValidator"/> for the shared control/BIDI/path
    /// character checks and additionally rejects keys that would collide with
    /// the container's own fixed <c>AddAttribute</c>/<c>RemoveAttribute</c>
    /// Method BrowseNames.
    /// </summary>
    internal static class WotLabelValidator
    {
        /// <summary>
        /// Validates a label key against the configured bounds, reserved
        /// AttributesType member names and character-safety rules.
        /// </summary>
        public static ServiceResult ValidateKey(string? key, WotRegistryPersistenceBounds bounds)
        {
            if (string.IsNullOrEmpty(key))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "The Key argument is required.");
            }
            if (key!.Length > bounds.MaxLabelKeyLength)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The label key exceeds the maximum length of {0} characters.",
                    bounds.MaxLabelKeyLength);
            }
            if (string.Equals(key, XRegistry.BrowseNames.AddAttribute, StringComparison.Ordinal) ||
                string.Equals(key, XRegistry.BrowseNames.RemoveAttribute, StringComparison.Ordinal))
            {
                return ServiceResult.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "The label key '{0}' collides with a fixed Labels container member.",
                    key);
            }
            return WotChildNameValidator.Validate(key);
        }

        /// <summary>
        /// Validates a label value against the configured maximum length.
        /// Any string content is otherwise accepted: the value is a
        /// read-only Property value, not a BrowseName/NodeId path segment.
        /// </summary>
        public static ServiceResult ValidateValue(string? value, WotRegistryPersistenceBounds bounds)
        {
            if (value is not null && value.Length > bounds.MaxLabelValueLength)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The label value exceeds the maximum length of {0} characters.",
                    bounds.MaxLabelValueLength);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Validates a key/value pair and throws a
        /// <see cref="ServiceResultException"/> with a precise StatusCode on
        /// the first failing check.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static void Validate(string? key, string? value, WotRegistryPersistenceBounds bounds)
        {
            ServiceResult keyResult = ValidateKey(key, bounds);
            if (ServiceResult.IsBad(keyResult))
            {
                throw new ServiceResultException(keyResult);
            }
            ServiceResult valueResult = ValidateValue(value, bounds);
            if (ServiceResult.IsBad(valueResult))
            {
                throw new ServiceResultException(valueResult);
            }
        }
    }
}
