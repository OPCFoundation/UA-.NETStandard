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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AMB.Server.Health;

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// The server-specific alarm types of the AMB node manager.
    /// </summary>
    public sealed partial class AmbNodeManager
    {
        /// <summary>
        /// Gets the index of the namespace of the server-specific types.
        /// </summary>
        public ushort TypeNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(m_assetManagement.Options.TypeNamespaceUri);

        /// <summary>
        /// Gets whether health alarms are instances of server-specific types.
        /// </summary>
        internal bool UseServerDefinedAlarmTypes => m_assetManagement.Options.UseServerDefinedAlarmTypes;

        /// <summary>
        /// Creates the server-specific alarm types once and returns them.
        /// </summary>
        /// <remarks>
        /// The types refine the Device Integration alarm types, which have to
        /// be in the type tree first. When the Device Integration manager is
        /// registered before this one, the types are created with the address
        /// space; otherwise when the first asset with a health alarm is
        /// registered, which happens after the Device Integration model is
        /// loaded.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the Device
        /// Integration alarm types are not loaded.
        /// </exception>
        internal async ValueTask<AmbAlarmTypes> EnsureAlarmTypesAsync(CancellationToken cancellationToken)
        {
            AmbAlarmTypes? types = Volatile.Read(ref m_alarmTypes);
            if (types != null)
            {
                return types;
            }

            await m_typesLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_alarmTypes != null)
                {
                    return m_alarmTypes;
                }
                if (!DeviceIntegrationAlarmTypesLoaded())
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "The OPC 10000-100 alarm types are not loaded, so the AMB alarm types cannot refine " +
                        "them. Register AddOpcUaDi() or a companion specification built on it.");
                }

                var nodes = new List<BaseObjectTypeState>();
                types = AmbAlarmTypes.Build(Server.NamespaceUris, TypeNamespaceIndex, nodes);
                foreach (BaseObjectTypeState node in nodes)
                {
                    await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
                }
                Volatile.Write(ref m_alarmTypes, types);
                return types;
            }
            finally
            {
                m_typesLock.Release();
            }
        }

        /// <summary>
        /// Creates the server-specific alarm types with the address space
        /// when the Device Integration model is loaded already.
        /// </summary>
        private async ValueTask InitializeAlarmTypesAsync(CancellationToken cancellationToken)
        {
            if (UseServerDefinedAlarmTypes && DeviceIntegrationAlarmTypesLoaded())
            {
                await EnsureAlarmTypesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private bool DeviceIntegrationAlarmTypesLoaded()
        {
            NodeId failure = AmbAlarmTypes.DiTypeOf(AssetHealthAlarmKind.Failure, Server.NamespaceUris);
            return !failure.IsNull && Server.TypeTree.IsKnown(failure);
        }

        private readonly SemaphoreSlim m_typesLock = new(1, 1);
        private AmbAlarmTypes? m_alarmTypes;
    }
}
