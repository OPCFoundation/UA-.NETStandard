// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Observability and time services that can be
    /// passed around in the sdk as a unit.
    /// </summary>
    public interface ITelemetryContext
    {
        /// <summary>
        /// Logger factory
        /// </summary>
        ILoggerFactory LoggerFactory { get; }

        /// <summary>
        /// Meter factory
        /// </summary>
        IMeterFactory MeterFactory { get; }

        /// <summary>
        /// Time provider
        /// </summary>
        TimeProvider TimeProvider { get; }

        /// <summary>
        /// Activity source to use
        /// </summary>
        ActivitySource? ActivitySource { get; }
    }
}
