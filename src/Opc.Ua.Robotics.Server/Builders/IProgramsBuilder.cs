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
