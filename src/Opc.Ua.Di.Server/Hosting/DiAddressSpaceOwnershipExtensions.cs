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
using Opc.Ua.Di.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Service-collection helpers for claiming ownership of the DI address space.
    /// </summary>
    public static class DiAddressSpaceOwnershipExtensions
    {
        /// <summary>
        /// Registers a <see cref="DiAddressSpaceOwnership"/> marker for
        /// <paramref name="ownerName"/> and fails if another hosting registration
        /// already owns the DI namespace and address space.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="ownerName">The name of the claiming hosting registration.</param>
        /// <param name="guidance">
        /// Optional text appended to the failure message that explains how to
        /// compose into the existing owner instead.
        /// </param>
        /// <returns>The same service collection, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="services"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="ownerName"/> is empty.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A different hosting registration, or the same one, already owns the DI address space.
        /// </exception>
        public static IServiceCollection ClaimDiAddressSpace(
            this IServiceCollection services,
            string ownerName,
            string? guidance = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            var claim = new DiAddressSpaceOwnership(ownerName);

            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(DiAddressSpaceOwnership))
                {
                    string currentOwner =
                        (descriptor.ImplementationInstance as DiAddressSpaceOwnership)?.OwnerName ??
                        "another DI-aware hosting registration";
                    string duplicate = currentOwner == ownerName
                        ? $"{ownerName} has already been called. "
                        : string.Empty;
                    string suffix = string.IsNullOrEmpty(guidance) ? string.Empty : " " + guidance;
                    throw new InvalidOperationException(
                        $"{duplicate}The OPC UA DI namespace and address space are already owned by " +
                        $"'{currentOwner}'. {ownerName} cannot register a second DI-owning manager.{suffix}");
                }
            }

            return services.AddSingleton(claim);
        }
    }
}
