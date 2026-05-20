/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Default <see cref="IFileSystemProvider"/> implementation that
    /// maps provider-relative paths onto a single physical directory
    /// on the host using <see cref="System.IO"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All operations are sandboxed: any provider-relative path that
    /// resolves outside the configured root (for example via
    /// <c>..</c> segments) is rejected with
    /// <see cref="UnauthorizedAccessException"/>. The check is done
    /// with <see cref="Path.GetFullPath(string)"/> followed by a
    /// prefix-match against the canonicalised root.
    /// </para>
    /// <para>
    /// The provider is async-friendly but the underlying
    /// <see cref="System.IO"/> APIs used here are largely synchronous;
    /// callers should not rely on these implementations to release the
    /// calling thread for I/O.
    /// </para>
    /// </remarks>
    public sealed class PhysicalFileSystemProvider : IFileSystemProvider
    {
        /// <summary>
        /// Mounts a single physical directory as the root of an
        /// <see cref="IFileSystemProvider"/>.
        /// </summary>
        /// <param name="rootDirectory">
        /// Absolute path to the directory to expose. Created if it
        /// does not already exist.
        /// </param>
        /// <param name="mountName">
        /// BrowseName of the root mount node. Defaults to the
        /// directory name when <c>null</c> or empty.
        /// </param>
        /// <param name="isWritable">
        /// When <c>false</c>, all write / create / delete / move /
        /// copy calls fail fast with
        /// <see cref="StatusCodes.BadUserAccessDenied"/> via
        /// <see cref="UnauthorizedAccessException"/>.
        /// </param>
        public PhysicalFileSystemProvider(
            string rootDirectory,
            string? mountName = null,
            bool isWritable = true)
        {
            if (string.IsNullOrEmpty(rootDirectory))
            {
                throw new ArgumentException(
                    "Root directory must not be null or empty.",
                    nameof(rootDirectory));
            }

            m_rootDirectory = Path.GetFullPath(rootDirectory);
            // Normalise the root prefix so subsequent path-traversal
            // checks have a stable, separator-terminated comparison
            // anchor that won't accept "/root123" when the actual
            // root is "/root".
            m_rootPrefix = m_rootDirectory;
            if (!m_rootPrefix.EndsWith(
                    Path.DirectorySeparatorChar.ToString(),
                    StringComparison.Ordinal))
            {
                m_rootPrefix += Path.DirectorySeparatorChar;
            }

            if (!Directory.Exists(m_rootDirectory))
            {
                Directory.CreateDirectory(m_rootDirectory);
            }

            string resolvedMount = !string.IsNullOrEmpty(mountName)
                ? mountName!
                : new DirectoryInfo(m_rootDirectory).Name;
            MountName = resolvedMount;
            IsWritable = isWritable;
        }

        /// <inheritdoc/>
        public string MountName { get; }

        /// <inheritdoc/>
        public bool IsWritable { get; }

        /// <inheritdoc/>
        public ValueTask<FileSystemEntry?> GetEntryAsync(
            string path,
            CancellationToken ct)
        {
            string full = ResolveAbsolute(path);
            if (Directory.Exists(full))
            {
                return new ValueTask<FileSystemEntry?>(
                    BuildEntry(path, full, isDirectory: true));
            }
            if (File.Exists(full))
            {
                return new ValueTask<FileSystemEntry?>(
                    BuildEntry(path, full, isDirectory: false));
            }
            return new ValueTask<FileSystemEntry?>((FileSystemEntry?)null);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
            string path,
            [EnumeratorCancellation] CancellationToken ct)
        {
            string full = ResolveAbsolute(path);
            if (!Directory.Exists(full))
            {
                throw new DirectoryNotFoundException(
                    $"Directory '{path}' does not exist.");
            }

            foreach (string entryPath in Directory.EnumerateDirectories(full))
            {
                ct.ThrowIfCancellationRequested();
                yield return BuildEntry(
                    JoinProviderPath(path, Path.GetFileName(entryPath)),
                    entryPath,
                    isDirectory: true);
            }

            foreach (string entryPath in Directory.EnumerateFiles(full))
            {
                ct.ThrowIfCancellationRequested();
                yield return BuildEntry(
                    JoinProviderPath(path, Path.GetFileName(entryPath)),
                    entryPath,
                    isDirectory: false);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<Stream> OpenReadAsync(string path, CancellationToken ct)
        {
            string full = ResolveAbsolute(path);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException(
                    $"File '{path}' does not exist.", full);
            }
            return new ValueTask<Stream>(
                new FileStream(full, FileMode.Open, FileAccess.Read));
        }

        /// <inheritdoc/>
        public ValueTask<Stream> OpenWriteAsync(
            string path,
            FileWriteMode mode,
            CancellationToken ct)
        {
            EnsureWritable();
            string full = ResolveAbsolute(path);
            FileMode fileMode = mode switch
            {
                FileWriteMode.Truncate => FileMode.Create,
                FileWriteMode.Append => FileMode.Append,
                _ => FileMode.OpenOrCreate
            };
            return new ValueTask<Stream>(
                new FileStream(full, fileMode, FileAccess.Write));
        }

        /// <inheritdoc/>
        public ValueTask CreateDirectoryAsync(string path, CancellationToken ct)
        {
            EnsureWritable();
            string full = ResolveAbsolute(path);
            if (File.Exists(full))
            {
                throw new IOException(
                    $"A file already exists at '{path}'.");
            }
            Directory.CreateDirectory(full);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CreateFileAsync(string path, CancellationToken ct)
        {
            EnsureWritable();
            string full = ResolveAbsolute(path);
            if (File.Exists(full) || Directory.Exists(full))
            {
                throw new IOException(
                    $"A file or directory already exists at '{path}'.");
            }
            using (File.Create(full))
            {
                // Just create and close so the entry exists at zero bytes.
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DeleteAsync(string path, CancellationToken ct)
        {
            EnsureWritable();
            string full = ResolveAbsolute(path);
            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
                return default;
            }
            if (File.Exists(full))
            {
                File.Delete(full);
                return default;
            }
            throw new FileNotFoundException(
                $"Nothing to delete at '{path}'.", full);
        }

        /// <inheritdoc/>
        public ValueTask MoveAsync(
            string source,
            string target,
            CancellationToken ct)
        {
            EnsureWritable();
            string sourceFull = ResolveAbsolute(source);
            string targetFull = ResolveAbsolute(target);

            if (File.Exists(targetFull) || Directory.Exists(targetFull))
            {
                throw new IOException(
                    $"Target '{target}' already exists.");
            }
            if (File.Exists(sourceFull))
            {
                File.Move(sourceFull, targetFull);
                return default;
            }
            if (Directory.Exists(sourceFull))
            {
                Directory.Move(sourceFull, targetFull);
                return default;
            }
            throw new FileNotFoundException(
                $"Nothing to move at '{source}'.", sourceFull);
        }

        /// <inheritdoc/>
        public ValueTask CopyAsync(
            string source,
            string target,
            CancellationToken ct)
        {
            EnsureWritable();
            string sourceFull = ResolveAbsolute(source);
            string targetFull = ResolveAbsolute(target);

            if (File.Exists(targetFull) || Directory.Exists(targetFull))
            {
                throw new IOException(
                    $"Target '{target}' already exists.");
            }
            if (File.Exists(sourceFull))
            {
                File.Copy(sourceFull, targetFull);
                return default;
            }
            if (Directory.Exists(sourceFull))
            {
                CopyDirectoryRecursive(sourceFull, targetFull);
                return default;
            }
            throw new FileNotFoundException(
                $"Nothing to copy at '{source}'.", sourceFull);
        }

        private void EnsureWritable()
        {
            if (!IsWritable)
            {
                throw new UnauthorizedAccessException(
                    "Provider is read-only.");
            }
        }

        /// <summary>
        /// Translates a provider-relative path to an absolute host
        /// path while rejecting any attempt to escape the configured
        /// root via <c>..</c> segments or rooted paths.
        /// </summary>
        private string ResolveAbsolute(string path)
        {
            string relative = NormaliseRelative(path);
            string combined = string.IsNullOrEmpty(relative)
                ? m_rootDirectory
                : Path.Combine(m_rootDirectory, relative);
            string full = Path.GetFullPath(combined);

            if (full.Length < m_rootDirectory.Length ||
                (!string.Equals(full, m_rootDirectory, StringComparison.Ordinal) &&
                    !full.StartsWith(m_rootPrefix, StringComparison.Ordinal)))
            {
                throw new UnauthorizedAccessException(
                    $"Path '{path}' escapes the provider root.");
            }

            return full;
        }

        /// <summary>
        /// Converts a provider-relative path with <c>/</c> separators
        /// to a host-relative path with native separators. Strips a
        /// leading slash and the empty root.
        /// </summary>
        private static string NormaliseRelative(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return string.Empty;
            }
            string trimmed = path.TrimStart('/');
            return trimmed.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string JoinProviderPath(string basePath, string name)
        {
            if (string.IsNullOrEmpty(basePath) || basePath == "/")
            {
                return name;
            }
            return basePath.TrimEnd('/') + "/" + name;
        }

        private FileSystemEntry BuildEntry(
            string providerPath,
            string fullPath,
            bool isDirectory)
        {
            string name = string.IsNullOrEmpty(providerPath)
                ? MountName
                : providerPath.Substring(providerPath.LastIndexOf('/') + 1);

            if (isDirectory)
            {
                DateTime modified = Directory.GetLastWriteTimeUtc(fullPath);
                return new FileSystemEntry(
                    providerPath ?? string.Empty,
                    name,
                    IsDirectory: true,
                    Length: 0,
                    IsWritable: IsWritable,
                    LastModifiedUtc: modified,
                    MimeType: string.Empty);
            }

            var info = new FileInfo(fullPath);
            return new FileSystemEntry(
                providerPath ?? string.Empty,
                name,
                IsDirectory: false,
                Length: info.Length,
                IsWritable: IsWritable && !info.IsReadOnly,
                LastModifiedUtc: info.LastWriteTimeUtc,
                MimeType: GuessMimeType(info.Extension));
        }

        private static string GuessMimeType(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return string.Empty;
            }
            return extension.ToLowerInvariant() switch
            {
                ".txt" => "text/plain",
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".zip" => "application/zip",
                _ => "application/octet-stream"
            };
        }

        private static void CopyDirectoryRecursive(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string sub = dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(target, sub));
            }
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string sub = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                File.Copy(file, Path.Combine(target, sub), overwrite: false);
            }
        }

        private readonly string m_rootDirectory;
        private readonly string m_rootPrefix;
    }
}
