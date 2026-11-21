// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
