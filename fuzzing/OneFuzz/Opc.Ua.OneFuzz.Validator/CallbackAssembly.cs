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
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Opc.Ua.OneFuzz
{
    internal delegate void SpanCallback(ReadOnlySpan<byte> input);

    internal sealed class CallbackAssembly : IDisposable
    {
        internal CallbackAssembly(string path)
        {
            m_path = Path.GetFullPath(path);
            m_context = new DropLoadContext(m_path);
            m_assembly = m_context.LoadFromAssemblyPath(m_path);
        }

        public void Dispose()
        {
            m_context.Unload();
        }

        internal List<CallbackKey> Discover()
        {
            return [.. m_assembly.GetExportedTypes()
                .SelectMany(static type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(IsCallback)
                .Select(method => new CallbackKey(
                    Path.GetFileName(m_path),
                    method.DeclaringType!.FullName!,
                    method.Name))
                .OrderBy(static callback => callback.Type, StringComparer.Ordinal)
                .ThenBy(static callback => callback.Method, StringComparer.Ordinal)];
        }

        internal SpanCallback GetCallback(string typeName, string methodName)
        {
            Type type = m_assembly.GetType(typeName, throwOnError: true)!;
            MethodInfo[] methods = [.. type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == methodName && IsCallback(method))];
            if (!type.IsVisible || methods.Length != 1)
            {
                throw new InvalidDataException(
                    $"Expected exactly one public static void {typeName}.{methodName}(ReadOnlySpan<byte>).");
            }

            return methods[0].CreateDelegate<SpanCallback>();
        }

        internal HashSet<string> ValidateClosure()
        {
            string root = Path.GetDirectoryName(m_path)!;
            string stem = Path.GetFileNameWithoutExtension(m_path);
            if (m_assembly.EntryPoint is not null ||
                m_assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName !=
                    ".NETCoreApp,Version=v10.0")
            {
                throw new InvalidDataException($"The callback host must be a managed net10.0 class library: {stem}");
            }

            if (m_assembly.GetType("Opc.Ua.Fuzzing.Program") is not null ||
                m_assembly.GetType("Opc.Ua.Fuzzing.FuzzMethods") is not null)
            {
                throw new InvalidDataException($"The local runner must not be compiled into the service host: {stem}");
            }

            var files = new HashSet<string>(StringComparer.Ordinal);
            string runtimeConfig = ContractPaths.Resolve(root, stem + ".runtimeconfig.json");
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(runtimeConfig)))
            {
                JsonElement runtime = document.RootElement.GetProperty("runtimeOptions");
                JsonElement framework = runtime.GetProperty("framework");
                if (JsonContract.String(runtime, "tfm") != "net10.0" ||
                    JsonContract.String(framework, "name") != "Microsoft.NETCore.App" ||
                    !JsonContract.String(framework, "version").StartsWith("10.", StringComparison.Ordinal) ||
                    runtime.TryGetProperty("additionalProbingPaths", out _))
                {
                    throw new InvalidDataException(
                        $"Invalid or nonrelocatable runtime configuration: {runtimeConfig}");
                }
            }

            files.Add(stem + ".runtimeconfig.json");
            ReadDependencyFiles(root, stem + ".deps.json", files);

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<Assembly>();
            pending.Enqueue(m_assembly);
            while (pending.TryDequeue(out Assembly? assembly))
            {
                string name = assembly.GetName().Name!;
                if (!visited.Add(name))
                {
                    continue;
                }

                string relative = ContractPaths.Relative(root, assembly.Location);
                ContractPaths.Resolve(root, relative);
                files.Add(relative);
                using (var stream = File.OpenRead(assembly.Location))
                using (var pe = new PEReader(stream))
                {
                    if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null ||
                        (pe.PEHeaders.CorHeader.Flags & CorFlags.ILOnly) == 0)
                    {
                        throw new InvalidDataException($"A managed, non-AOT assembly is required: {relative}");
                    }

                    if (name.StartsWith("Opc.Ua.", StringComparison.Ordinal))
                    {
                        string pdb = Path.ChangeExtension(relative, ".pdb");
                        ValidatePortablePdb(pe, ContractPaths.Resolve(root, pdb));
                        files.Add(pdb);
                    }
                }

                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (reference.Name?.StartsWith("SharpFuzz", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        throw new InvalidDataException(
                            $"Service drops must not contain SharpFuzz runners or preinstrumentation: {relative}");
                    }

                    if (!DropLoadContext.IsFramework(reference))
                    {
                        pending.Enqueue(m_context.LoadFromAssemblyName(reference));
                    }
                }
            }

            return files;
        }

        private static bool IsCallback(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.IsPublic && method.IsStatic && !method.ContainsGenericParameters &&
                method.ReturnType == typeof(void) && parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(ReadOnlySpan<byte>);
        }

        private static void ValidatePortablePdb(PEReader pe, string path)
        {
            using var stream = File.OpenRead(path);
            using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            DebugMetadataHeader header = provider.GetMetadataReader().DebugMetadataHeader
                ?? throw new InvalidDataException($"Portable PDB has no debug metadata: {path}");
            var id = new BlobContentId(header.Id);
            bool matches = pe.ReadDebugDirectory().Any(entry =>
                entry.Type == DebugDirectoryEntryType.CodeView &&
                pe.ReadCodeViewDebugDirectoryData(entry).Guid == id.Guid);
            if (!matches)
            {
                throw new InvalidDataException($"PDB does not belong to its published assembly: {path}");
            }
        }

        private static void ReadDependencyFiles(string root, string relative, HashSet<string> files)
        {
            string path = ContractPaths.Resolve(root, relative);
            files.Add(relative);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement dependencies = document.RootElement;
            foreach (JsonProperty library in dependencies.GetProperty("libraries").EnumerateObject())
            {
                if (library.Name.StartsWith("SharpFuzz", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"SharpFuzz is forbidden in service dependencies: {relative}");
                }
            }

            foreach (JsonProperty target in dependencies.GetProperty("targets").EnumerateObject())
            {
                foreach (JsonProperty library in target.Value.EnumerateObject())
                {
                    foreach (string kind in new[] { "runtime", "native", "runtimeTargets" })
                    {
                        if (!library.Value.TryGetProperty(kind, out JsonElement assets))
                        {
                            continue;
                        }

                        foreach (JsonProperty asset in assets.EnumerateObject())
                        {
                            if (Path.GetFileName(asset.Name) == "_._")
                            {
                                continue;
                            }

                            string file = kind == "runtimeTargets" ? asset.Name : Path.GetFileName(asset.Name);
                            files.Add(ContractPaths.Relative(root, ContractPaths.Resolve(root, file)));
                        }
                    }
                }
            }
        }

        private readonly string m_path;
        private readonly DropLoadContext m_context;
        private readonly Assembly m_assembly;

        private sealed class DropLoadContext : AssemblyLoadContext
        {
            internal DropLoadContext(string assemblyPath)
                : base(isCollectible: true)
            {
                m_root = Path.GetDirectoryName(assemblyPath)!;
                m_resolver = new AssemblyDependencyResolver(assemblyPath);
            }

            internal static bool IsFramework(AssemblyName name)
            {
                return name.Name is not null && s_frameworkNames.Contains(name.Name);
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                if (IsFramework(assemblyName))
                {
                    return null;
                }

                string path = m_resolver.ResolveAssemblyToPath(assemblyName)
                    ?? Path.Combine(m_root, assemblyName.Name + ".dll");
                string relative = ContractPaths.Relative(m_root, path);
                return LoadFromAssemblyPath(ContractPaths.Resolve(m_root, relative));
            }

            protected override nint LoadUnmanagedDll(string unmanagedDllName)
            {
                string? path = m_resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                if (path is null)
                {
                    return nint.Zero;
                }

                string relative = ContractPaths.Relative(m_root, path);
                return LoadUnmanagedDllFromPath(ContractPaths.Resolve(m_root, relative));
            }

            private static readonly HashSet<string> s_frameworkNames = new(
                Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
                    .Select(static path => Path.GetFileNameWithoutExtension(path)),
                StringComparer.OrdinalIgnoreCase);

            private readonly string m_root;
            private readonly AssemblyDependencyResolver m_resolver;
        }
    }
}
