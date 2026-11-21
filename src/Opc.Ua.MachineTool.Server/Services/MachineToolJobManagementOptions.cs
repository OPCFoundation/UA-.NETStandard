// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Selects complete, application-owned providers for the machine's declared Machinery Jobs AddIn.
    /// </summary>
    public sealed record MachineToolJobManagementOptions
    {
        /// <summary>
        /// Gets the explicit receiver of standard ISA-95 job-order operations.
        /// </summary>
        public IIsa95JobOrderReceiverV2? Receiver { get; init; }

        /// <summary>
        /// Gets the explicit response provider; receiver-only configurations are rejected.
        /// </summary>
        public IIsa95JobResponseProviderV2? ResponseProvider { get; init; }

        /// <summary>
        /// Gets the order catalog, or uses the receiver's or response provider's catalog when implemented.
        /// </summary>
        public IIsa95JobOrderCatalog? Catalog { get; init; }

        /// <summary>
        /// Gets whether the shared binder validates predefined Machinery job parameters.
        /// </summary>
        public bool PredefinedParameters { get; init; } = true;

        /// <summary>
        /// Gets the per-instance catalog without borrowing a different machine's container registration.
        /// </summary>
        internal IIsa95JobOrderCatalog? EffectiveCatalog =>
            Catalog ?? Receiver as IIsa95JobOrderCatalog ?? ResponseProvider as IIsa95JobOrderCatalog;

        /// <summary>
        /// Validates complete provider selection before any machine factory runs.
        /// </summary>
        internal void Validate()
        {
            if (Receiver == null || ResponseProvider == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "MachineTool job management requires both an order receiver and a response provider.");
            }
            if (EffectiveCatalog == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "MachineTool job management requires a per-instance job-order catalog.");
            }
        }
    }
}
