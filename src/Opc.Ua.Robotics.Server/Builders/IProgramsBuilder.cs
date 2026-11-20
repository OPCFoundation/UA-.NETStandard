/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Configures the optional OPC 40010 Controller <c>Programs</c> directory.
    /// The directory is a standard Part 5 <c>FileDirectoryType</c>, so it is
    /// backed by the stack's existing <see cref="IFileSystemProvider"/> model
    /// rather than by a Robotics-specific file abstraction.
    /// </summary>
    public interface IProgramsBuilder
    {
        /// <summary>
        /// Gets the Programs directory being configured.
        /// </summary>
        FileDirectoryState Directory { get; }

        /// <summary>
        /// Backs the Programs directory with an explicit provider instance.
        /// </summary>
        /// <param name="provider">
        /// The file-system provider serving the controller's programs.
        /// </param>
        IProgramsBuilder UseFileSystem(IFileSystemProvider provider);

        /// <summary>
        /// Backs the Programs directory with a provider resolved from the
        /// application service provider.
        /// </summary>
        /// <typeparam name="TProvider">
        /// The registered provider contract.
        /// </typeparam>
        IProgramsBuilder UseFileSystem<TProvider>()
            where TProvider : class, IFileSystemProvider;

        /// <summary>
        /// Applies binding options such as the create, delete, and move
        /// permissions and the materialisation guards.
        /// </summary>
        /// <param name="configure">
        /// Receives the options applied when the directory is bound.
        /// </param>
        IProgramsBuilder WithOptions(Action<FileDirectoryBindingOptions> configure);
    }
}
