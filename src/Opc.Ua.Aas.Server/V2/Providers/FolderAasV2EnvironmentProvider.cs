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
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Opc.Ua.Aas.V2;

namespace Opc.Ua.Aas.Server.V2
{
    /// <summary>
    /// Supplies AAS V2 JSON, XML and AASX documents from a folder.
    /// </summary>
    public sealed class FolderAasV2EnvironmentProvider : IAasV2EnvironmentProvider
    {
        /// <summary>
        /// Initializes a folder-backed provider.
        /// </summary>
        /// <param name="folderPath">The folder to enumerate documents from.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="folderPath"/> is <c>null</c>.
        /// </exception>
        public FolderAasV2EnvironmentProvider(string folderPath)
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
                if (!IsSupported(extension))
                {
                    continue;
                }

                AasEnvironment? environment = await ReadAsync(path, extension, cancellationToken)
                    .ConfigureAwait(false);
                if (environment is not null)
                {
                    yield return environment;
                }
            }
        }

        private static bool IsSupported(string extension)
        {
            return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".aasx", StringComparison.OrdinalIgnoreCase);
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

        private async System.Threading.Tasks.Task<AasEnvironment?> ReadAsync(
            string path,
            string extension,
            CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(path);
            if (string.Equals(extension, ".aasx", StringComparison.OrdinalIgnoreCase))
            {
                AasxPackageReadResult package = await new AasxPackageReader()
                    .ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!package.Succeeded || package.Environment is null)
                {
                    m_diagnostics.Add($"The AAS V2 package '{path}' could not be read: {package.Error}");
                    return null;
                }
                return package.Environment;
            }

            AasDocumentReadResult result =
                string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
                    ? await new AasJsonReader().ReadAsync(stream, cancellationToken).ConfigureAwait(false)
                    : await new AasXmlReader().ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || result.Environment is null)
            {
                m_diagnostics.Add($"The AAS V2 document '{path}' could not be read: {result.Error}");
                return null;
            }

            return result.Environment;
        }

        private readonly string m_folderPath;
        private readonly List<string> m_diagnostics = [];
    }
}
