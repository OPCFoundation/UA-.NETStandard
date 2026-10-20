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
using System.Linq;
using Microsoft.Extensions.Options;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Loads the OPC 30010 AutoID model into a Machinery node manager, next to the Device
    /// Integration model its core provider loads.
    /// </summary>
    /// <remarks>
    /// Registered by <c>AddAutoIdToMachinery</c>. Only the AutoID nodes are added: the DI nodes the
    /// owner loaded already are kept, so DI is loaded once however many companions are composed.
    /// </remarks>
    public sealed class AutoIdMachineryModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates a provider with optional additional namespace registrations.
        /// </summary>
        /// <param name="options">The AutoID options; defaults when <see langword="null"/>.</param>
        public AutoIdMachineryModelProvider(IOptions<AutoIdServerOptions>? options = null)
            : this(options?.Value)
        {
        }

        /// <summary>
        /// Creates a provider for explicit options, as the stand-alone owner does.
        /// </summary>
        internal AutoIdMachineryModelProvider(AutoIdServerOptions? options)
        {
            m_options = (options ?? new AutoIdServerOptions()).Validate();
        }

        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
            ((ArrayOf<string>)[AutoIdModel.NamespaceUri]).AddItems(m_options.AdditionalNamespaceUris);

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException">
        /// The owner did not load the Device Integration model.
        /// </exception>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            var deviceSet = ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == deviceSet))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "AutoID composition requires an owner that loads the Device Integration model first.");
            }
            var deviceType = ExpandedNodeId.ToNodeId(ObjectTypeIds.AutoIdDeviceType, context.NamespaceUris);
            if (nodes.Any(node => node.NodeId == deviceType))
            {
                return;
            }

            // The scan settings and results travel as structures: the server has to decode and
            // encode them for the method calls and events.
            if (!context.EncodeableFactory.ContainsEncodeableType(DataTypeIds.ScanSettings))
            {
                context.EncodeableFactory.Builder.AddAutoIdTypeSystem().Commit();
            }
            nodes.AddAutoIdTypeSystem(context);
        }

        private readonly AutoIdServerOptions m_options;
    }
}
