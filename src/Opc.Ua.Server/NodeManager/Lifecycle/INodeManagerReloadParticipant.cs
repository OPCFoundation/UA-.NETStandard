// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Participates in safe live replacement of a NodeManager generation.
    /// </summary>
    public interface INodeManagerReloadParticipant
    {
        /// <summary>
        /// Transfers retained inbound references and returns counterparts to remove.
        /// </summary>
        ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
            IAsyncNodeManager replacement,
            CancellationToken ct = default);
    }

}
