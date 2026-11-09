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

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Optional feature families of an OPC 40100-1 system. The control state
    /// machines and their mandatory methods are always provided.
    /// </summary>
    [Flags]
    public enum MachineVisionFeatures
    {
        /// <summary>
        /// Mandatory control only.
        /// </summary>
        None = 0,

        /// <summary>
        /// Recipe and product management.
        /// </summary>
        Recipes = 1,

        /// <summary>
        /// Configuration management.
        /// </summary>
        Configurations = 2,

        /// <summary>
        /// Result retrieval and filtering.
        /// </summary>
        Results = 4,

        /// <summary>
        /// Browseable recipe, product, configuration, and result folders.
        /// </summary>
        Folders = 8,

        /// <summary>
        /// Opaque recipe, configuration, and result file transfer.
        /// </summary>
        FileTransfer = 16,

        /// <summary>
        /// Synchronization sub-state machines in every supported parent state.
        /// </summary>
        Steps = 32,

        /// <summary>
        /// Simulation mode control.
        /// </summary>
        Simulation = 64,

        /// <summary>
        /// Safety information reporting.
        /// </summary>
        Safety = 128,

        /// <summary>
        /// Diagnostic-level selection and SEMI E10 state reporting.
        /// </summary>
        Diagnostics = 256,

        /// <summary>
        /// All optional feature families.
        /// </summary>
        All = Recipes | Configurations | Results | Folders | FileTransfer | Steps | Simulation | Safety | Diagnostics
    }

    /// <summary>
    /// Per-system runtime configuration. Limits constrain resources, not the meaning
    /// of client pagination or retention hints.
    /// </summary>
    public sealed class MachineVisionSystemOptions
    {
        /// <summary>
        /// Gets or sets the optional functionality to materialize and bind.
        /// </summary>
        public MachineVisionFeatures Features { get; set; } = MachineVisionFeatures.All;

        /// <summary>
        /// Gets or sets whether a provider-owned recipe is already usable at startup.
        /// This also supports systems which do not expose recipe management.
        /// </summary>
        public bool InitiallyReady { get; set; }

        /// <summary>
        /// Gets or sets the simulation setting captured by the first job, before any SimulationMode call.
        /// </summary>
        public bool InitiallySimulated { get; set; }

        /// <summary>
        /// Gets or sets the maximum simultaneous metadata handles.
        /// </summary>
        public int MaximumHandles { get; set; } = 64;

        /// <summary>
        /// Gets or sets the retention bound for metadata handles when a client gives no estimate.
        /// </summary>
        public TimeSpan HandleTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the maximum simultaneously open file transfers.
        /// </summary>
        public int MaximumTransfers { get; set; } = 8;

        /// <summary>
        /// Gets or sets the maximum size of a transferred file.
        /// </summary>
        public int MaximumFileBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the inactivity timeout of a file transfer.
        /// </summary>
        public TimeSpan TransferTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Validates and copies the options for an immutable runtime configuration.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal MachineVisionSystemOptions CopyValidated()
        {
            if ((Features & ~MachineVisionFeatures.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Features));
            }
            if (MaximumHandles <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumHandles));
            }
            if (MaximumTransfers <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumTransfers));
            }
            if (MaximumFileBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumFileBytes));
            }
            if (HandleTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(HandleTimeout));
            }
            if (TransferTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(TransferTimeout));
            }
            return (MachineVisionSystemOptions)MemberwiseClone();
        }
    }
}
