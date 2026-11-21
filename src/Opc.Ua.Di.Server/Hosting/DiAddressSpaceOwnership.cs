// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// Marks the single hosted node manager that owns the OPC UA Device
    /// Integration namespace and address space.
    /// </summary>
    /// <remarks>
    /// Hosting registrations for companion node managers that load the DI model
    /// must register this marker and fail if one is already registered.
    /// </remarks>
    public sealed class DiAddressSpaceOwnership
    {
        /// <summary>
        /// Creates an ownership marker for the named hosting registration.
        /// </summary>
        /// <param name="ownerName">
        /// The name of the hosting registration that owns the DI address space.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="ownerName"/> is empty.
        /// </exception>
        public DiAddressSpaceOwnership(string ownerName)
        {
            if (string.IsNullOrWhiteSpace(ownerName))
            {
                throw new ArgumentException(
                    "The DI address-space owner name must not be empty.",
                    nameof(ownerName));
            }

            OwnerName = ownerName;
        }

        /// <summary>
        /// Gets the name of the hosting registration that owns the DI address space.
        /// </summary>
        public string OwnerName { get; }
    }
}
