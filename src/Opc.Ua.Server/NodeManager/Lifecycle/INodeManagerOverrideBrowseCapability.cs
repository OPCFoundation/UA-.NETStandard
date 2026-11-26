// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
