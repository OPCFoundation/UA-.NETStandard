/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Context provided to every <see cref="IHistorianProvider"/> call. It
    /// bundles the OPC UA system + operation contexts, the resolved
    /// <see cref="NodeState"/> when available, and a framework-supplied
    /// default <see cref="ModificationInfo"/> so update providers can
    /// stamp inserts/replaces consistently without reaching back into
    /// session state themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Auditing</strong>: <see cref="DefaultModificationInfo"/>
    /// is pre-populated with the current UTC time and the calling user
    /// identity. Providers may override <see cref="ModificationInfo.UserName"/>
    /// or <see cref="ModificationInfo.ModificationTime"/> if the backend
    /// has stronger audit metadata, but otherwise should attach the
    /// supplied instance verbatim.
    /// </para>
    /// </remarks>
    public sealed class HistorianOperationContext
    {
        /// <summary>
        /// Creates a new context.
        /// </summary>
        /// <param name="systemContext">The server system context.</param>
        /// <param name="operationContext">The operation context for the request.</param>
        /// <param name="node">The resolved <see cref="NodeState"/>, if known.</param>
        /// <param name="defaultUpdateType">The default <see cref="HistoryUpdateType"/> to stamp on writes.</param>
        public HistorianOperationContext(
            ServerSystemContext systemContext,
            OperationContext operationContext,
            NodeState? node,
            HistoryUpdateType defaultUpdateType)
        {
            SystemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
            OperationContext = operationContext ?? throw new ArgumentNullException(nameof(operationContext));
            Node = node;
            DefaultModificationInfo = new ModificationInfo
            {
                ModificationTime = DateTime.UtcNow,
                UpdateType = defaultUpdateType,
                UserName = operationContext.Session?.Identity?.DisplayName
            };
        }

        /// <summary>
        /// The server-wide system context.
        /// </summary>
        public ServerSystemContext SystemContext { get; }

        /// <summary>
        /// The operation context (session, request type, locales, etc.).
        /// </summary>
        public OperationContext OperationContext { get; }

        /// <summary>
        /// The resolved <see cref="NodeState"/> for the historizing variable
        /// or notifier object, when available. May be <c>null</c> when the
        /// node cannot be found in the address space — for example when a
        /// historian holds data for nodes that are no longer exposed.
        /// </summary>
        public NodeState? Node { get; }

        /// <summary>
        /// Default <see cref="ModificationInfo"/> populated from the
        /// current user and UTC time, with <see cref="ModificationInfo.UpdateType"/>
        /// pre-set for the operation. Providers stamping their own audit
        /// metadata may copy fields from here.
        /// </summary>
        public ModificationInfo DefaultModificationInfo { get; }
    }
}
