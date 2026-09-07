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
