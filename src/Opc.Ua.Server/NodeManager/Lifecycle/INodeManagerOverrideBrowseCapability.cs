/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// Declares whether a manager can safely browse across an exact-node override publication.
    /// </summary>
    /// <remarks>
    /// This capability covers every source the manager can browse, including dynamically produced sources,
    /// custom browser hooks, lazy references and saved continuations. An implementation returns true only
    /// if every reference to the supplied targets is selected and resolved against the operation's routing
    /// view and a continuation cannot use a retired target. Returning false also covers sources whose
    /// dependencies are unknown. The answer must remain valid for the manager generation's lifetime,
    /// including when browser hooks or their backing state change after the check. A manager must
    /// control those changes or reject them when they cannot preserve this guarantee. It must not
    /// infer safety only from the browser's current output or the current hooks. The check must not
    /// mutate live state or run application callbacks.
    /// </remarks>
    public interface INodeManagerOverrideBrowseCapability
    {
        /// <summary>
        /// Determines whether the manager supports a publication changing the supplied target owners.
        /// </summary>
        /// <param name="targets">The exact target identifiers whose override owners change.</param>
        /// <returns>True when all potentially affected browser paths are snapshot-safe.</returns>
        bool SupportsOverrideBrowse(ArrayOf<NodeId> targets);
    }
}
