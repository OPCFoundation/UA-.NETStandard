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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Opc.Ua.Aas.Server
{
    /// <summary>
    /// Supplies AAS JSON and XML documents from a folder.
    /// </summary>
    public sealed class FolderAasEnvironmentProvider : IAasEnvironmentProvider
    {
        /// <summary>
        /// Initializes a folder-backed provider.
        /// </summary>
        public FolderAasEnvironmentProvider(string folderPath)
        {
            m_folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        }

        /// <summary>
        /// Gets document diagnostics collected during the last enumeration.
        /// </summary>
        public ArrayOf<string> Diagnostics => new(m_diagnostics.ToArray());

        /// <inheritdoc/>
        public async IAsyncEnumerable<AasEnvironment> GetEnvironmentsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            m_diagnostics.Clear();
            if (!Directory.Exists(m_folderPath))
            {
                yield break;
            }

            // The order the file system happens to return entries in is not
            // specified and differs between platforms, so a folder is enumerated
            // by name: two servers reading the same folder must publish the same
            // documents in the same order.
            foreach (string path in EnumerateOrdered(m_folderPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string extension = Path.GetExtension(path);
                if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using FileStream stream = File.OpenRead(path);
                AasDocumentReadResult result = string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
                    ? await new AasJsonReader().ReadAsync(stream, cancellationToken).ConfigureAwait(false)
                    : await new AasXmlReader().ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded || result.Environment is null)
                {
                    m_diagnostics.Add(
                        $"The AAS document '{path}' could not be read: {result.Error}");
                    continue;
                }
                yield return result.Environment;
            }
        }

        /// <summary>
        /// Enumerates the folder's files by ordinal name, so the sequence does
        /// not depend on the platform's directory ordering.
        /// </summary>
        private static List<string> EnumerateOrdered(string folderPath)
        {
            var paths = new List<string>(Directory.EnumerateFiles(folderPath));
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        private readonly string m_folderPath;
        private readonly List<string> m_diagnostics = [];
    }
}
