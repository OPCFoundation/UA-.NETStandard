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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;
using System.Runtime.Loader;
#endif
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.OpenUsd.Connector
{
    /// <summary>
    /// Finds the optional viewport assembly at run time. Keeping the renderer out of the
    /// connector's own references is what lets the connector package stay small and keep
    /// targeting .NET Framework, while the viewport needs .NET 10, Avalonia, and a
    /// per-architecture native OpenUSD payload.
    /// </summary>
    internal static class UsdViewHostLoader
    {
#if NET8_0_OR_GREATER
        private const string ViewerAssemblyName = "Opc.Ua.OpenUsd.Connector.Viewer";
        private const string ViewerTypeName =
            "Opc.Ua.OpenUsd.Connector.Viewer.OpenUsdViewHost";
#endif

        /// <summary>
        /// Loads the viewport implementation, or explains why it is unavailable.
        /// </summary>
        /// <param name="host">The loaded host, or <c>null</c> when unavailable.</param>
        /// <param name="reason">A user-facing explanation when <paramref name="host"/> is <c>null</c>.</param>
        /// <returns><c>true</c> when a host was loaded.</returns>
#if NET8_0_OR_GREATER
        [RequiresUnreferencedCode(
            "The viewport is discovered by assembly and type name, so trimming cannot see it.")]
        [RequiresDynamicCode(
            "The viewport is instantiated reflectively and is not available in a Native AOT publish.")]
#endif
        public static bool TryLoad(out IUsdViewHost? host, out string reason)
        {
            host = null;
#if !NET8_0_OR_GREATER
            reason =
                "Rendering requires .NET 8 or later. Run the connector on a modern " +
                "target framework to use the view option.";
            return false;
#else
            Assembly assembly;
            try
            {
                assembly = LoadViewerAssembly();
            }
            catch (Exception exception) when (
                exception is System.IO.FileNotFoundException or
                    System.IO.FileLoadException or BadImageFormatException)
            {
                reason =
                    $"The optional '{ViewerAssemblyName}' assembly was not found next to the " +
                    "connector. Install the matching " +
                    "OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector.Viewer package, or run " +
                    "the connector without the view option to author the override layer only.";
                return false;
            }

            Type? type = assembly.GetType(ViewerTypeName, throwOnError: false);
            if (type is null)
            {
                reason =
                    $"'{ViewerAssemblyName}' does not contain '{ViewerTypeName}'. The viewport " +
                    "package does not match this connector version.";
                return false;
            }

            object? instance;
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (MissingMethodException)
            {
                reason = $"'{ViewerTypeName}' has no public parameterless constructor.";
                return false;
            }
            catch (TargetInvocationException exception)
            {
                reason = $"The viewport could not be created: {exception.InnerException?.Message}";
                return false;
            }

            if (instance is not IUsdViewHost loaded)
            {
                reason = $"'{ViewerTypeName}' does not implement IUsdViewHost.";
                return false;
            }

            host = loaded;
            reason = string.Empty;
            return true;
#endif
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Loads the viewport assembly from the connector's own directory and teaches the
        /// default load context to resolve its dependency graph from there too. The
        /// viewport is installed side by side rather than referenced, so it is absent from
        /// the connector's dependency manifest and plain assembly binding cannot find it.
        /// </summary>
        [RequiresUnreferencedCode(
            "The viewport and its dependencies are resolved by path, so trimming cannot see them.")]
        private static Assembly LoadViewerAssembly()
        {
            string path = Path.Combine(
                AppContext.BaseDirectory, ViewerAssemblyName + ".dll");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The optional viewport assembly is not installed.", path);
            }

            var resolver = new AssemblyDependencyResolver(path);
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                string? resolved = resolver.ResolveAssemblyToPath(name);
                if (resolved is null || !File.Exists(resolved))
                {
                    // The publish layout is flat, so fall back to a sibling probe for
                    // assemblies the component manifest does not place by path.
                    resolved = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
                    if (!File.Exists(resolved))
                    {
                        return null;
                    }
                }
                return context.LoadFromAssemblyPath(resolved);
            };
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, unmanaged) =>
            {
                string? resolved = resolver.ResolveUnmanagedDllToPath(unmanaged);
                return resolved is not null && File.Exists(resolved)
                    ? NativeLibrary.Load(resolved)
                    : IntPtr.Zero;
            };
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
#endif
    }
}
