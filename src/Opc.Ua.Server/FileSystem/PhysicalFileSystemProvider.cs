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
    /// Any provider-relative path that
    /// resolves outside the configured root (for example via
    /// <c>..</c> segments) is rejected with
    /// <see cref="UnauthorizedAccessException"/>. Symbolic links, junctions and
    /// other reparse points are rejected, including in the root and its ancestors.
    /// Recursive operations validate entries before descending into directories.
    /// These path-based checks are not atomic with subsequent I/O: the host must
    /// prevent untrusted local users from replacing entries or root ancestors
    /// during operations. Hard links and filesystem mount points are not isolated.
    /// </para>
    /// <para>
    /// Write streams exclude other readers and writers until disposed. Explicit
    /// exclusive sharing is required on Unix as well as Windows.
    /// </para>
    /// <para>
    /// The provider is async-friendly but the underlying
    /// <see cref="System.IO"/> APIs used here are largely synchronous;
    /// callers should not rely on these implementations to release the
    /// calling thread for I/O.
    /// </para>
    /// </remarks>
    public sealed class PhysicalFileSystemProvider : IFileSystemProvider, IFileSystemPathIdentityProvider
    {
        /// <summary>
        /// Mounts a single physical directory as the root of an
        /// <see cref="IFileSystemProvider"/>.
        /// </summary>
        /// <param name="rootDirectory">
        /// Absolute path to the directory to expose. Created if it
        /// does not already exist. Neither it nor its ancestors may
        /// be symbolic links, junctions or other reparse points.
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
            string volumeRoot = Path.GetPathRoot(m_rootDirectory)!;
            if (m_rootDirectory.Length > volumeRoot.Length)
            {
                m_rootDirectory = m_rootDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
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

            RejectLinks(m_rootDirectory);
            if (!Directory.Exists(m_rootDirectory))
            {
                Directory.CreateDirectory(m_rootDirectory);
            }
            RejectLinks(m_rootDirectory);

            MountName = !string.IsNullOrEmpty(mountName)
                ? mountName!
                : new DirectoryInfo(m_rootDirectory).Name;
            IsWritable = isWritable;
        }

        /// <inheritdoc/>
        public string MountName { get; }

        /// <inheritdoc/>
        public bool IsWritable { get; }

        /// <inheritdoc/>
        public string GetPathIdentity(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }
            string full = ResolveAbsolute(path);
            return Path.DirectorySeparatorChar == '\\' ? full.ToUpperInvariant() : full;
        }

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

            var files = new List<string>();
            foreach (string entryPath in Directory.EnumerateFileSystemEntries(full))
            {
                ct.ThrowIfCancellationRequested();
                RejectLinks(entryPath);
                FileAttributes attributes = RejectLink(entryPath);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    yield return BuildEntry(
                        JoinProviderPath(path, Path.GetFileName(entryPath)),
                        entryPath,
                        isDirectory: true);
                }
                else
                {
                    files.Add(entryPath);
                }
            }

            foreach (string entryPath in files)
            {
                ct.ThrowIfCancellationRequested();
                RejectLinks(entryPath);
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
                new FileStream(full, fileMode, FileAccess.Write, FileShare.None));
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
            EnsureNotRoot(full);
            if (Directory.Exists(full))
            {
                GetDirectoryTree(full, ct, out List<string> directories, out List<string> files);
                foreach (string file in files)
                {
                    RejectLinks(file);
                    File.Delete(file);
                }
                for (int ii = directories.Count - 1; ii >= 0; ii--)
                {
                    RejectLinks(directories[ii]);
                    Directory.Delete(directories[ii]);
                }
                RejectLinks(full);
                Directory.Delete(full);
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
            EnsureNotRoot(sourceFull);
            EnsureNotRoot(targetFull);

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
                GetDirectoryTree(sourceFull, ct, out _, out _);
                RejectLinks(sourceFull);
                RejectLinks(targetFull);
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
            EnsureNotRoot(sourceFull);
            EnsureNotRoot(targetFull);

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
                CopyDirectoryRecursive(sourceFull, targetFull, ct);
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
        /// Rejects destructive or copy operations that target the mounted root directory itself.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException"></exception>
        private void EnsureNotRoot(string fullPath)
        {
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                m_rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison))
            {
                throw new UnauthorizedAccessException("The file-system mount root cannot be deleted, moved or copied.");
            }
        }

        /// <summary>
        /// Translates a provider-relative path to an absolute host
        /// path while rejecting any attempt to escape the configured
        /// root via <c>..</c> segments or rooted paths, and rejecting linked components.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">
        /// <paramref name="path"/> escapes the configured provider root or
        /// contains a link, including in the root or one of its ancestors.
        /// </exception>
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

            RejectLinks(full);
            return full;
        }

        /// <summary>
        /// Rejects links in every existing component, including the root's ancestors.
        /// Missing components are permitted for creation; other I/O failures propagate.
        /// This check is not atomic with the subsequent filesystem operation.
        /// </summary>
        private static void RejectLinks(string fullPath)
        {
            string current = Path.GetPathRoot(fullPath)!;
            string[] segments = fullPath[current.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            RejectLink(current);
            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                try
                {
                    RejectLink(current);
                }
                catch (FileNotFoundException)
                {
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Checks attributes without using Exists, which can hide dangling links and access failures.
        /// </summary>
        private static FileAttributes RejectLink(string fullPath)
        {
            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException(
                    $"Symbolic links, junctions and reparse points are not allowed: '{fullPath}'.");
            }
            return attributes;
        }

        /// <summary>
        /// Converts a provider-relative path with <c>/</c> separators
        /// to a host-relative path with native separators. Strips a
        /// leading slash and the empty root.
        /// </summary>
        private string NormaliseRelative(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return string.Empty;
            }
            string relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) ||
                (Path.DirectorySeparatorChar == '\\' && relative.Contains(':', StringComparison.Ordinal)))
            {
                throw new UnauthorizedAccessException($"Path '{path}' must be provider-relative.");
            }
            if (Path.DirectorySeparatorChar == '\\' &&
                !m_rootDirectory.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                string[] segments = relative.Split('\\');
                for (int ii = 0; ii < segments.Length; ii++)
                {
                    if (segments[ii] is not "." and not "..")
                    {
                        segments[ii] = segments[ii].TrimEnd(' ', '.');
                    }
                }
                relative = string.Join("\\", segments);
            }
            return relative;
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
                : providerPath[(providerPath.LastIndexOf('/') + 1)..];

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

        /// <summary>
        /// Snapshots a directory tree one level at a time, rejecting links before traversal.
        /// </summary>
        private static void GetDirectoryTree(
            string source,
            CancellationToken ct,
            out List<string> directories,
            out List<string> files)
        {
            directories = [];
            files = [];
            var pending = new Stack<string>();
            pending.Push(source);
            while (pending.Count != 0)
            {
                ct.ThrowIfCancellationRequested();
                string current = pending.Pop();
                RejectLinks(current);
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    ct.ThrowIfCancellationRequested();
                    RejectLinks(entry);
                    FileAttributes attributes = RejectLink(entry);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Add(entry);
                        pending.Push(entry);
                    }
                    else
                    {
                        files.Add(entry);
                    }
                }
            }
        }

        /// <summary>
        /// Copies a validated snapshot, rechecking source and destination paths before I/O.
        /// </summary>
        private static void CopyDirectoryRecursive(string source, string target, CancellationToken ct)
        {
            // Enumerate the source before creating the target so a target inside
            // the source never becomes part of the copy.
            GetDirectoryTree(source, ct, out List<string> directories, out List<string> files);
            RejectLinks(target);
            Directory.CreateDirectory(target);
            foreach (string dir in directories)
            {
                ct.ThrowIfCancellationRequested();
                string sub = dir[source.Length..].TrimStart(Path.DirectorySeparatorChar);
                string destination = Path.Combine(target, sub);
                RejectLinks(dir);
                RejectLinks(destination);
                Directory.CreateDirectory(destination);
            }
            foreach (string file in files)
            {
                ct.ThrowIfCancellationRequested();
                string sub = file[source.Length..].TrimStart(Path.DirectorySeparatorChar);
                string destination = Path.Combine(target, sub);
                RejectLinks(file);
                RejectLinks(destination);
                File.Copy(file, destination, overwrite: false);
            }
        }

        private readonly string m_rootDirectory;
        private readonly string m_rootPrefix;
    }
}
