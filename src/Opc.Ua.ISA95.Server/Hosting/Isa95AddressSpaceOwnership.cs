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
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.ISA95.Server.Hosting
{
    /// <summary>
    /// Marks the single hosted node manager that owns the OPC 10030 ISA-95
    /// namespaces and the OPC 10031-4 Job Control address space.
    /// </summary>
    /// <remarks>
    /// Hosting registrations for companion node managers that load the ISA-95
    /// Job Control models — OPC 40001-3 Machinery Job Management is one — must
    /// register this marker and fail when one is already registered, so two
    /// managers never claim the same namespaces. The marker mirrors
    /// <c>Opc.Ua.Di.Server.Hosting.DiAddressSpaceOwnership</c>.
    /// </remarks>
    public sealed class Isa95AddressSpaceOwnership
    {
        /// <summary>
        /// Creates an ownership marker for the named hosting registration.
        /// </summary>
        /// <param name="ownerName">
        /// The name of the hosting registration that owns the ISA-95 address space.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="ownerName"/> is empty.
        /// </exception>
        public Isa95AddressSpaceOwnership(string ownerName)
        {
            if (string.IsNullOrWhiteSpace(ownerName))
            {
                throw new ArgumentException(
                    "The ISA-95 address-space owner name must not be empty.",
                    nameof(ownerName));
            }

            OwnerName = ownerName;
        }

        /// <summary>
        /// Gets the name of the hosting registration that owns the ISA-95
        /// address space.
        /// </summary>
        public string OwnerName { get; }

        /// <summary>
        /// Claims the ISA-95 namespaces for one hosting registration, failing
        /// when another registration already owns them.
        /// </summary>
        /// <param name="services">The service collection being configured.</param>
        /// <param name="ownerName">
        /// The name of the claiming hosting registration, used in the failure
        /// message.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Another registration already owns the ISA-95 address space.
        /// </exception>
        public static void Claim(IServiceCollection services, string ownerName)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(Isa95AddressSpaceOwnership))
                {
                    string currentOwner =
                        (descriptor.ImplementationInstance as Isa95AddressSpaceOwnership)?
                            .OwnerName ?? "another ISA-95-aware hosting registration";
                    throw new InvalidOperationException(
                        $"The ISA-95 namespaces and address space are already owned by " +
                        $"'{currentOwner}'. {ownerName} cannot register a second " +
                        $"ISA-95-owning manager.");
                }
            }

            services.AddSingleton(new Isa95AddressSpaceOwnership(ownerName));
        }
    }
}
