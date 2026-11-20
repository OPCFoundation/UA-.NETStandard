// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Scales.Server.Hosting
{
    /// <summary>
    /// Scales configuration on a selected owner, before its Machinery builder is sealed.
    /// </summary>
    public interface IScalesBuildContext
    {
        /// <summary>
        /// Gets the shared runtime for all scale kinds, scale systems and subdevices.
        /// </summary>
        ScalesRuntime Runtime { get; }

        /// <summary>
        /// Gets the existing address-space owner, also used for AMB asset registration.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the startup cancellation token.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves an application service from the hosting container.
        /// </summary>
        /// <typeparam name="T">The service contract.</typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
