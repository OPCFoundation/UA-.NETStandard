// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Reflection;
#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;
using System.Runtime.Loader;
#endif
using Opc.Ua.OpenUsd.Client;

namespace BinPickingClient
{
    /// <summary>
    /// Reflection-based loader for the optional OpenUSD viewport assembly. Mirrors the
    /// shape used by <c>IntentViewerClient</c> so a machine without the native renderer
    /// payload gracefully falls back to headless operation with a clear reason.
    /// </summary>
    internal static class UsdViewHostLoader
    {
        public static bool TryLoad(out IUsdViewHost? host, out string reason)
        {
            host = null;
#if !NET8_0_OR_GREATER
            reason = "Rendering requires a .NET 8 or later target.";
            return false;
#else
            Assembly assembly;
            try
            {
                assembly = LoadViewerAssembly();
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                reason =
                    $"The optional '{ViewerAssemblyName}' assembly or its native payload was not found " +
                    "next to the sample. " +
                    "Run without --view for headless mode, or publish/run with the matching viewport package.";
                return false;
            }

            Type? type = assembly.GetType(ViewerTypeName, throwOnError: false);
            if (type is null)
            {
                reason = $"'{ViewerAssemblyName}' does not contain '{ViewerTypeName}'.";
                return false;
            }

            try
            {
                if (Activator.CreateInstance(type) is IUsdViewHost loaded)
                {
                    host = loaded;
                    reason = string.Empty;
                    return true;
                }
            }
            catch (Exception exception) when (exception is MissingMethodException or TargetInvocationException)
            {
                reason = $"The viewport could not be created: {exception.Message}";
                return false;
            }

            reason = $"'{ViewerTypeName}' does not implement IUsdViewHost.";
            return false;
#endif
        }

#if NET8_0_OR_GREATER
        private static Assembly LoadViewerAssembly()
        {
            string path = Path.Combine(AppContext.BaseDirectory, ViewerAssemblyName + ".dll");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The optional viewport assembly is not installed.", path);
            }

            var resolver = new AssemblyDependencyResolver(path);
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                string? resolved = resolver.ResolveAssemblyToPath(name);
                if (resolved is null || !File.Exists(resolved))
                {
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
                return resolved is not null && File.Exists(resolved) ? NativeLibrary.Load(resolved) : IntPtr.Zero;
            };
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
#endif

#if NET8_0_OR_GREATER
        private const string ViewerAssemblyName = "Opc.Ua.OpenUsd.Connector.Viewer";
        private const string ViewerTypeName = "Opc.Ua.OpenUsd.Connector.Viewer.OpenUsdViewHost";
#endif
    }
}
