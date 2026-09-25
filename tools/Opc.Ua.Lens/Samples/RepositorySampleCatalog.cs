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
using System.IO;
using System.Runtime.InteropServices;
using Opc.Ua;

namespace UaLens.Samples
{
    internal sealed record RepositorySampleDescriptor(
        RepositorySampleId Id,
        string Title,
        string ProjectDirectory,
        string AssemblyName,
        string ApplicationName,
        string ProductUri,
        string EndpointPath);

    /// <summary>
    /// Compile-time repository allowlist. No document can add an executable, project,
    /// argument, environment variable, native publish target or rendering entry.
    /// </summary>
    internal static class RepositorySampleCatalog
    {
        public static ArrayOf<RepositorySampleDescriptor> Entries =>
        [
            Get(RepositorySampleId.ConsoleReferenceServer),
            Get(RepositorySampleId.PumpSoftwareUpdateSimulator),
            Get(RepositorySampleId.VisualInspectionCell)
        ];

        public static RepositorySampleDescriptor Get(RepositorySampleId sample)
        {
            return sample switch
            {
                RepositorySampleId.ConsoleReferenceServer => new(
                    sample,
                    "Console reference server",
                    Path.Combine("samples", "Reference", "ConsoleReferenceServer"),
                    "ConsoleReferenceServer",
                    "ConsoleReferenceServer",
                    "uri:opcfoundation.org:Quickstarts:ReferenceServer",
                    "/Quickstarts/ReferenceServer"),
                RepositorySampleId.PumpSoftwareUpdateSimulator => new(
                    sample,
                    "DI pump software-update simulator (opt-in)",
                    Path.Combine("samples", "DI", "PumpDeviceIntegrationServer"),
                    "PumpDeviceIntegrationServer",
                    "PumpDeviceIntegrationServer",
                    "uri:opcfoundation.org:PumpDeviceIntegrationServer",
                    "/PumpDeviceIntegrationServer"),
                RepositorySampleId.VisualInspectionCell => new(
                    sample,
                    "Vision fixture inspection cell (managed .NET 10)",
                    Path.Combine("samples", "Vision", "VisualInspectionCell"),
                    "VisualInspectionCell",
                    "VisualInspectionCell",
                    "uri:opcfoundation.org:VisualInspectionCell",
                    "/VisualInspectionCell"),
                _ => throw new ArgumentOutOfRangeException(nameof(sample), "The repository sample is not allowlisted.")
            };
        }

        public static string GetBuildDirectory(RepositorySampleSource source, RepositorySampleId sample)
        {
            ArgumentNullException.ThrowIfNull(source);
            string configuration = source.Configuration switch
            {
                RepositorySampleBuildConfiguration.Debug => "Debug",
                RepositorySampleBuildConfiguration.Release => "Release",
                _ => throw new ArgumentOutOfRangeException(nameof(source), "The build configuration is not supported.")
            };
            string framework = source.Framework switch
            {
                RepositorySampleFramework.Net8 => "net8.0",
                RepositorySampleFramework.Net9 => "net9.0",
                RepositorySampleFramework.Net10 => "net10.0",
                _ => throw new ArgumentOutOfRangeException(nameof(source), "The managed framework is not supported.")
            };
            string path = Path.Combine(Get(sample).ProjectDirectory, "bin", configuration, framework);
            path = source.Layout switch
            {
                RepositorySampleBuildLayout.FrameworkDirectory => path,
                RepositorySampleBuildLayout.CurrentRuntimeDirectory =>
                    Path.Combine(path, RuntimeInformation.RuntimeIdentifier),
                _ => throw new ArgumentOutOfRangeException(nameof(source), "The build layout is not supported.")
            };
            return RepositorySamplePaths.ResolveChild(source.Root, path);
        }
    }

    /// <summary>
    /// No-follow checks for an explicitly trusted local source or a generated run.
    /// These checks are not a sandbox for executing an untrusted repository.
    /// </summary>
    internal static class RepositorySamplePaths
    {
        private static StringComparison PathComparison =>
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public static string ValidateRoot(string root)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            if (!Path.IsPathFullyQualified(root) ||
                root.StartsWith(@"\\", StringComparison.Ordinal) ||
                root.StartsWith("//", StringComparison.Ordinal))
            {
                throw new ArgumentException("Select an absolute local directory.", nameof(root));
            }
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (string.Equals(full, Path.GetPathRoot(full), PathComparison))
            {
                throw new ArgumentException("A filesystem root cannot be selected for samples.", nameof(root));
            }
            return full;
        }

        public static string ResolveChild(string root, string relativePath)
        {
            root = ValidateRoot(root);
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            if (Path.IsPathRooted(relativePath))
            {
                throw new ArgumentException("A sample manifest path must be relative.", nameof(relativePath));
            }
            string full = Path.GetFullPath(Path.Combine(root, relativePath));
            string relative = Path.GetRelativePath(root, full);
            if (relative == "." ||
                relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                throw new ArgumentException("The sample path escapes its selected root.", nameof(relativePath));
            }
            return full;
        }

        public static void EnsureNoLinks(string root, string path)
        {
            root = ValidateRoot(root);
            string full = Path.GetFullPath(path);
            if (!string.Equals(root, full, PathComparison))
            {
                _ = ResolveChild(root, Path.GetRelativePath(root, full));
            }
            string? current = full;
            while (current is not null)
            {
                CheckExistingEntry(current);
                if (string.Equals(current, root, PathComparison))
                {
                    return;
                }
                current = Path.GetDirectoryName(current);
            }
            throw new IOException("The sample path could not be checked within its owned root.");
        }

        public static void RejectReparsePoint(FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Symbolic links and reparse points are not supported in owned sample paths.");
            }
        }

        private static void CheckExistingEntry(string path)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                // Missing entries are handled by prerequisite checks or owned creation.
                return;
            }
            catch (DirectoryNotFoundException)
            {
                // Missing parents are handled by prerequisite checks or owned creation.
                return;
            }
            RejectReparsePoint(attributes);
        }
    }
}
