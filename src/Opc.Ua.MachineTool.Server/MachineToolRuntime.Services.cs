// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
