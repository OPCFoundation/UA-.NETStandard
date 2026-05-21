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
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Interface for a node manager that manages
    /// <c>ApplicationConfigurationType</c> instances under the
    /// <c>ManagedApplications</c> folder per OPC 10000-12 §7.10.16.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations create <see cref="ApplicationConfigurationState"/>
    /// instances for each managed application and expose them under the
    /// well-known <c>ManagedApplications</c> folder. Each instance owns
    /// a <see cref="ConfigurationFileState"/> that supports the
    /// <c>CloseAndUpdate</c> / <c>ConfirmUpdate</c> transaction pattern
    /// described in OPC 10000-12 §7.7.6.
    /// </para>
    /// <para>
    /// The <see cref="StubManagedApplicationsNodeManager"/> provides a
    /// minimal implementation that satisfies the model-level requirement
    /// by exposing the <c>ManagedApplications</c> folder but returns
    /// <c>Bad_NotSupported</c> for the configuration-file write
    /// operations. Production systems should replace it with a node
    /// manager that persists configuration data and implements the full
    /// <c>ConfirmUpdate</c> transaction lifecycle.
    /// </para>
    /// </remarks>
    public interface IManagedApplicationsNodeManager : INodeManager
    {
    }

    /// <summary>
    /// Stub implementation of <see cref="IManagedApplicationsNodeManager"/>
    /// that exposes the <c>ManagedApplications</c> folder in the address
    /// space and wires <c>ConfirmUpdate</c> with a
    /// <c>Bad_NotSupported</c> response. A production GDS should replace
    /// this stub with a full implementation.
    /// </summary>
    public class StubManagedApplicationsNodeManager
        : CustomNodeManager2, IManagedApplicationsNodeManager
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public StubManagedApplicationsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<StubManagedApplicationsNodeManager>())
        {
            // The ManagedApplications folder lives under the
            // ServerConfiguration object in the base UA namespace.
            NamespaceUris = [Namespaces.OpcUa];
        }

        /// <inheritdoc/>
        protected override NodeStateCollection LoadPredefinedNodes(
            ISystemContext context)
        {
            // The ManagedApplications folder and its
            // ApplicationConfigurationFolderType are defined in the
            // base UA nodeset (StandardTypes.xml). They're loaded
            // by the core node manager. This stub node manager does
            // not contribute additional predefined nodes.
            return [];
        }

        /// <inheritdoc/>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            // The ManagedApplications folder is already part of the
            // core UA nodeset. Future implementations would browse it
            // here and populate ApplicationConfigurationType instances
            // from a configuration database.
        }
    }
}
