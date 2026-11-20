/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using Opc.Ua.Server;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server.Builders;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Build-time surface exposed to Vision configurators. Sensors,
    /// pipelines and coordinate frames are created through the fluent
    /// entrypoints on <see cref="Nodes"/>; low-level access to the
    /// active node manager and system context is available for cases
    /// where the fluent API does not model a particular customisation.
    /// </summary>
    public interface IVisionBuildContext
    {
        /// <summary>
        /// Gets the active node manager.
        /// </summary>
        AsyncCustomNodeManager Manager { get; }

        /// <summary>
        /// Gets the active system context.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// Gets the application-owned instance namespace index.
        /// </summary>
        ushort InstanceNamespaceIndex { get; }

        /// <summary>
        /// Gets the Vision namespace index.
        /// </summary>
        ushort VisionNamespaceIndex { get; }

        /// <summary>
        /// Gets the well-known <c>Server/Vision</c> root object created by
        /// the node manager.
        /// </summary>
        VisionRootState Root { get; }

        /// <summary>
        /// Gets the fluent Vision node builder rooted at
        /// <see cref="Root"/>.
        /// </summary>
        IVisionNodeBuilder Nodes { get; }

        /// <summary>
        /// Gets the startup cancellation token.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required application service. Throws when the
        /// context was created without an <see cref="IServiceProvider"/>.
        /// </summary>
        /// <typeparam name="T">
        /// The service type.
        /// </typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
