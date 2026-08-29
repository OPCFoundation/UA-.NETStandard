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
using System.Threading;
using Opc.Ua.Server;
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
