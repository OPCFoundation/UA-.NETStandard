// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Server.Hosting
{
    /// <summary>
    /// Context handed to the delegates registered with
    /// <c>ConfigureAssetManagement</c>, which run inside the Asset Management
    /// Basics node manager once its address space is loaded.
    /// </summary>
    /// <remarks>
    /// Assets themselves are registered from the setup of the node manager
    /// that owns them, for example <c>ConfigureDevicesFor</c>. This context is
    /// for what belongs to the AMB node manager. A failing delegate aborts the
    /// server startup.
    /// </remarks>
    public interface IAssetManagementSetupContext
    {
        /// <summary>
        /// Gets the Asset Management Basics node manager.
        /// </summary>
        AmbNodeManager Manager { get; }

        /// <summary>
        /// Gets the registry of the manageable assets.
        /// </summary>
        IAssetManagement Assets { get; }

        /// <summary>
        /// Gets the fluent builder of the node manager, open until the
        /// delegates have run.
        /// </summary>
        INodeManagerBuilder Builder { get; }

        /// <summary>
        /// Gets the cancellation token of the server startup.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Gets the initializer's readiness admission, or null during direct setup.
        /// </summary>
        NodeManagerReadinessContext? Readiness { get; }

        /// <summary>
        /// Resolves a required application service.
        /// </summary>
        /// <typeparam name="T">The service contract.</typeparam>
        T GetRequiredService<T>() where T : notnull;

        /// <summary>
        /// Resolves an optional application service, or returns
        /// <see langword="null"/> when none is registered.
        /// </summary>
        /// <typeparam name="T">The service contract.</typeparam>
        T? GetService<T>() where T : class;
    }
}
