// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Default <see cref="IApplicationInstanceFactory"/> implementation.
    /// Creates a fresh <see cref="ApplicationInstance"/> per call.
    /// </summary>
    internal sealed class DefaultApplicationInstanceFactory : IApplicationInstanceFactory
    {
        /// <inheritdoc />
        public IApplicationInstance Create(ITelemetryContext telemetry)
        {
            return new ApplicationInstance(telemetry);
        }
    }
}
