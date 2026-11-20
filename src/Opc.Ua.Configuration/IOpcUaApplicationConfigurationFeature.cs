// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Contributes a client or server feature to a shared application
    /// configuration created by <see cref="IOpcUaApplicationConfigurationProvider"/>.
    /// </summary>
    public interface IOpcUaApplicationConfigurationFeature
    {
        /// <summary>
        /// Supplies feature-specific defaults for common application options.
        /// Explicit values configured through <c>ConfigureApplication</c> win.
        /// </summary>
        void ApplyDefaults(OpcUaApplicationOptions options);

        /// <summary>
        /// Configures the feature and returns the security-builder stage.
        /// </summary>
        IApplicationConfigurationBuilderSecurity Configure(
            IApplicationConfigurationBuilderTypes builder);
    }
}
