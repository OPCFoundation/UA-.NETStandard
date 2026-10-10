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
        public PreparedWotTestStorage(string root, bool forceLeasedContent = false)
        {
            m_root = root;
            using var stock = new FileWotRegistryStore(root);
            if (forceLeasedContent || !stock.SupportsPreparedCommits)
            {
                m_content = new RecordingLeasedResourceStore();
            }
        }

        public IXRegistryResourceStore? ContentStore => m_content;

        public FileWotRegistryStore OpenStore(
            Action<FileWotRegistryStore.DirectorySyncPhase>? directorySyncFailureInjector = null,
            Action<string, string, string>? manifestReplace = null)
        {
            return new FileWotRegistryStore(
                m_root, directorySyncFailureInjector, manifestReplace, resourceStore: m_content);
        }

        public void Dispose()
        {
            m_content?.Dispose();
        }

        private readonly string m_root;
        private readonly RecordingLeasedResourceStore? m_content;
    }
}
