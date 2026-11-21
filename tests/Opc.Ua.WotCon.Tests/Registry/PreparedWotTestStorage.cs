// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Owns prepared-test content across store reopen while retaining the real file-backed manifest.
    /// The fallback protects immutable content in memory; it does not test process-crash content durability.
    /// </summary>
    internal sealed class PreparedWotTestStorage : IDisposable
    {
        /// <summary>
        /// Initializes a new <c>PreparedWotTestStorage</c> instance.
        /// </summary>
        /// <param name="root">The root used to initialize the instance.</param>
        /// <param name="forceLeasedContent">
        /// The force-leased-content flag used to initialize <c>PreparedWotTestStorage</c>.
        /// </param>
        public PreparedWotTestStorage(string root, bool forceLeasedContent = false)
        {
            m_root = root;
            using var stock = new FileWotRegistryStore(root);
            if (forceLeasedContent || !stock.SupportsPreparedCommits)
            {
                m_content = new RecordingLeasedResourceStore();
            }
        }

        /// <summary>
        /// Gets the content store for <c>PreparedWotTestStorage</c>.
        /// </summary>
        public IXRegistryResourceStore? ContentStore => m_content;

        /// <summary>
        /// Opens a file registry store over the storage root and its content store.
        /// </summary>
        /// <param name="directorySyncFailureInjector">The directory sync failure injector.</param>
        /// <param name="manifestReplace">The manifest replace.</param>
        public FileWotRegistryStore OpenStore(
            Action<FileWotRegistryStore.DirectorySyncPhase>? directorySyncFailureInjector = null,
            Action<string, string, string>? manifestReplace = null)
        {
            return new FileWotRegistryStore(
                m_root, directorySyncFailureInjector, manifestReplace, resourceStore: m_content);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_content?.Dispose();
        }

        private readonly string m_root;
        private readonly RecordingLeasedResourceStore? m_content;
    }
}
