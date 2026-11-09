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
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolRuntime<TState>
    {
        /// <summary>
        /// Gets the shared Machinery handle for advanced companion bindings on this same registered machine.
        /// </summary>
        public IMachineHandle<TState> Machinery => m_machinery ??
            throw new ServiceResultException(StatusCodes.BadInvalidState, "The machine has not completed registration.");

        /// <summary>
        /// Reconciles externally changed provider files using the same binding that owns client file handles.
        /// No file contents or equipment state are changed by this operation.
        /// </summary>
        /// <param name="cancellationToken">Cancels provider enumeration and node reconciliation.</param>
        public async ValueTask RefreshFileSystemAsync(CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource operation = BeginOperation(cancellationToken);
            try
            {
                IFileDirectoryBinding binding = m_fileBinding ??
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "This machine does not expose a configured file system.");
                await binding.RefreshAsync(operation.Token).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Retains the successfully initialized binding while the shared registration owns its disposal.
        /// </summary>
        internal void SetFileSystemBinding(IFileDirectoryBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (Interlocked.CompareExchange(ref m_fileBinding, binding, null) != null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "A machine file-system binding has already been assigned.");
            }
        }

        /// <summary>
        /// Retains the actual completed shared registration instead of creating another machine or publisher.
        /// </summary>
        internal void SetMachineryHandle(IMachineHandle<TState> handle)
        {
            m_machinery = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        /// <summary>The file binding owned by this machine's shared registration resource.</summary>
        private IFileDirectoryBinding? m_fileBinding;

        /// <summary>The actual shared machine registration and its result/notification publishers.</summary>
        private IMachineHandle<TState>? m_machinery;
    }
}
