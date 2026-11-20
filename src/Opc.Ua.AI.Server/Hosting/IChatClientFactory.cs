/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.Extensions.AI;
using Opc.Ua.AI.Inference;

namespace Opc.Ua.AI.Server.Hosting
{
    /// <summary>
    /// Creates host-owned chat clients for AI inference backends.
    /// </summary>
    /// <remarks>
    /// <c>Opc.Ua.AI.Inference</c> deliberately depends only on
    /// <c>Microsoft.Extensions.AI</c>. A host that wants Azure, OpenAI, Ollama or a
    /// local runtime keeps that dependency in its own composition root and exposes
    /// the resulting abstraction here.
    /// </remarks>
    public interface IChatClientFactory
    {
        /// <summary>
        /// Creates the chat client for one configured backend.
        /// </summary>
        /// <param name="backendName">
        /// The options name. Empty means the primary backend; <c>fallback</c> means
        /// the fallback backend.
        /// </param>
        /// <param name="options">The backend configuration.</param>
        /// <returns>
        /// A client instance for the backend. Ownership transfers to the inference
        /// backend, which disposes it with the backend.
        /// </returns>
        IChatClient CreateChatClient(string backendName, InferenceBackendOptions options);
    }
}
