/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// Selects asset history captured through the shared Part 11 historian.
    /// </summary>
    [Flags]
    public enum AssetHistoryKinds
    {
        /// <summary>
        /// No AMB history capture.
        /// </summary>
        None = 0,

        /// <summary>
        /// Raw DeviceHealth changes with source timestamps.
        /// </summary>
        DeviceHealth = 1,

        /// <summary>
        /// Health alarm transitions and details.
        /// </summary>
        HealthEvents = 2,

        /// <summary>
        /// Maintenance transitions and planned or actual details.
        /// </summary>
        MaintenanceEvents = 4,

        /// <summary>
        /// All asset history.
        /// </summary>
        All = DeviceHealth | HealthEvents | MaintenanceEvents
    }

    /// <summary>
    /// Configures capture, not retention: storage and retention remain provider policy.
    /// The server historian registry owns a registered provider's lifetime.
    /// </summary>
    public sealed class AssetHistoryOptions
    {
        /// <summary>
        /// Creates history configuration using an existing Part 11 provider.
        /// </summary>
        public AssetHistoryOptions(IHistorianProvider provider, AssetHistoryKinds kinds = AssetHistoryKinds.All)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if ((kinds & ~AssetHistoryKinds.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(kinds));
            }
            Kinds = kinds;
        }

        /// <summary>
        /// Gets the provider; custom providers must provision the asset's node identifiers before registration.
        /// </summary>
        public IHistorianProvider Provider { get; }

        /// <summary>
        /// Gets the requested capture kinds. Missing asset features are not created implicitly.
        /// </summary>
        public AssetHistoryKinds Kinds { get; }

        /// <summary>
        /// Gets or sets the shared capture queue and batching policy.
        /// </summary>
        public HistorianCaptureOptions? Capture { get; set; }
    }
}
