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

using System.Threading;
using Opc.Ua.RobotIntent;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Build context used by Robot Intent configurators.
    /// </summary>
    public interface IRobotIntentBuildContext
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
        /// Gets the fluent node builder for the instance namespace.
        /// </summary>
        INodeManagerBuilder Nodes { get; }

        /// <summary>
        /// Gets the application-owned instance namespace index.
        /// </summary>
        ushort InstanceNamespaceIndex { get; }

        /// <summary>
        /// Gets the Server/RobotIntent root object.
        /// </summary>
        RobotIntentRootState Root { get; }

        /// <summary>
        /// Gets the startup cancellation token.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required application service.
        /// </summary>
        /// <typeparam name="T">
        /// The service type.
        /// </typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
