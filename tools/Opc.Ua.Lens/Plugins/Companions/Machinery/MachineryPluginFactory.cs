/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using UaLens.ViewModels;

namespace UaLens.Plugins.Companions.Machinery
{
    /// <summary>
    /// Composes a real Machinery document with optional read, result, policy, time and existing ISA-95 providers.
    /// </summary>
    internal sealed class MachineryPluginFactory(
        IMachineryDataSource? source = null,
        IMachineryResultSource? results = null,
        ICompanionDeploymentPolicy? deploymentPolicy = null,
        TimeProvider? timeProvider = null,
        IPreparedCompanionProvider? jobs = null)
    {
        /// <summary>
        /// Creates an inert, independently owned document; registration remains the caller's responsibility.
        /// </summary>
        public MachineryPlugin Create(PluginHost host)
        {
            ArgumentNullException.ThrowIfNull(host);
            return new MachineryPlugin(host, source, results, deploymentPolicy, timeProvider, jobs);
        }
    }
}
