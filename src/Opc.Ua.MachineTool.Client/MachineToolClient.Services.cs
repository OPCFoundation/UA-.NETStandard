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
using System.Threading.Tasks;
using Opc.Ua.Machinery.Jobs;

namespace Opc.Ua.MachineTool.Client
{
    public sealed partial class MachineToolClient
    {
        /// <summary>
        /// Resolves the declared Jobs AddIn below MachineryBuildingBlocks, without inventing a response receiver.
        /// Its generated proxies expose the existing ISA-95 control and response methods.
        /// </summary>
        /// <param name="machineId">The machine instance identifier.</param>
        /// <param name="cancellationToken">Cancels namespace-correct navigation.</param>
        /// <returns>The generated Jobs proxy, or null when the optional AddIn is absent.</returns>
        public async ValueTask<JobManagementTypeClient?> GetJobManagementAsync(
            NodeId machineId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MachineToolTypeClient machine = GetMachineProxy(machineId);
            FolderTypeClient? blocks = await machine.GetMachineryBuildingBlocksAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            if (blocks == null)
            {
                return null;
            }
            ArrayOf<NodeId> jobs = await ResolveChildNodesAsync(
                blocks.ObjectId,
                [Name(Opc.Ua.Machinery.Jobs.BrowseNames.JobManagement, Opc.Ua.Machinery.Jobs.Namespaces.MachineryJobs)],
                cancellationToken).ConfigureAwait(false);
            return jobs[0].IsNull ? null : new JobManagementTypeClient(Session, jobs[0], Telemetry);
        }

        /// <summary>
        /// Returns the generated standard file-directory proxy, or null when FileSystem is not selected.
        /// </summary>
        /// <param name="machineId">The machine instance identifier.</param>
        /// <param name="cancellationToken">Cancels navigation without changing files.</param>
        /// <returns>The optional standard directory proxy.</returns>
        public ValueTask<FileDirectoryTypeClient?> GetFileSystemAsync(
            NodeId machineId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetMachineProxy(machineId).GetFileSystemAsync(Telemetry, cancellationToken);
        }

        /// <summary>
        /// Resolves the MachineTool-qualified WorkMasters directory without substituting a similarly named folder.
        /// </summary>
        /// <param name="machineId">The machine instance identifier.</param>
        /// <param name="cancellationToken">Cancels navigation without changing files.</param>
        /// <returns>The optional work-master directory proxy.</returns>
        public async ValueTask<FileDirectoryTypeClient?> GetWorkMastersAsync(
            NodeId machineId,
            CancellationToken cancellationToken = default)
        {
            FileDirectoryTypeClient? directory = await GetFileSystemAsync(machineId, cancellationToken)
                .ConfigureAwait(false);
            if (directory == null)
            {
                return null;
            }
            ArrayOf<NodeId> children = await ResolveChildNodesAsync(
                directory.ObjectId,
                [Name(BrowseNames.WorkMasters, Namespaces.MachineTool)],
                cancellationToken).ConfigureAwait(false);
            return children[0].IsNull ? null : new FileDirectoryTypeClient(Session, children[0], Telemetry);
        }
    }
}
