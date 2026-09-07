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
