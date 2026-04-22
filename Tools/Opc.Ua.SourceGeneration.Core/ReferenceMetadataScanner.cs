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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Reads <c>OpcUaModelDependencyAttribute</c> occurrences from a referenced
    /// assembly via <see cref="MetadataReader"/>. No reflection on live types
    /// (AOT-safe).
    /// </summary>
    internal static class ReferenceMetadataScanner
    {
        private const string AttributeNamespace = "Opc.Ua";
        private const string AttributeName = "OpcUaModelDependencyAttribute";

        /// <summary>
        /// Returns the dependency attributes recorded by the given assembly.
        /// Returns <see cref="ImmutableArray{T}.Empty"/> if the file is missing,
        /// unreadable, or does not carry the attribute.
        /// </summary>
        public static ImmutableArray<ModelDependencyReference> Scan(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            {
                return ImmutableArray<ModelDependencyReference>.Empty;
            }

            try
            {
                using FileStream stream = File.OpenRead(assemblyPath);
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                {
                    return ImmutableArray<ModelDependencyReference>.Empty;
                }

                MetadataReader reader = peReader.GetMetadataReader();
                if (!reader.IsAssembly)
                {
                    return ImmutableArray<ModelDependencyReference>.Empty;
                }

                string assemblyName = reader.GetString(reader.GetAssemblyDefinition().Name);
                List<ModelDependencyReference> results = null;

                foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
                {
                    if (!IsTargetAttribute(reader, handle))
                    {
                        continue;
                    }

                    if (TryReadAttributeStrings(reader, handle, out string[] args))
                    {
                        results ??= [];
                        results.Add(new ModelDependencyReference(
                            assemblyName: assemblyName,
                            modelUri: GetArg(args, 0),
                            prefix: GetArg(args, 1),
                            version: GetArg(args, 2),
                            publicationDate: GetArg(args, 3)));
                    }
                }

                return results == null
                    ? ImmutableArray<ModelDependencyReference>.Empty
                    : [.. results];
            }
            catch
            {
                // Unreadable reference - treated as no annotations.
                return ImmutableArray<ModelDependencyReference>.Empty;
            }
        }

        private static bool IsTargetAttribute(MetadataReader reader, CustomAttributeHandle handle)
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            StringHandle typeName;
            StringHandle typeNamespace;

            switch (attribute.Constructor.Kind)
            {
                case HandleKind.MemberReference:
                {
                    MemberReference mref = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                    if (mref.Parent.Kind != HandleKind.TypeReference)
                    {
                        return false;
                    }
                    TypeReference tref = reader.GetTypeReference((TypeReferenceHandle)mref.Parent);
                    typeName = tref.Name;
                    typeNamespace = tref.Namespace;
                    break;
                }
                case HandleKind.MethodDefinition:
                {
                    MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                    TypeDefinition type = reader.GetTypeDefinition(method.GetDeclaringType());
                    typeName = type.Name;
                    typeNamespace = type.Namespace;
                    break;
                }
                default:
                    return false;
            }

            return reader.StringComparer.Equals(typeName, AttributeName)
                && reader.StringComparer.Equals(typeNamespace, AttributeNamespace);
        }

        /// <summary>
        /// Decodes up to four fixed string arguments from a custom attribute blob.
        /// Null (SerString 0xFF) is returned as an empty string.
        /// </summary>
        private static bool TryReadAttributeStrings(MetadataReader reader, CustomAttributeHandle handle, out string[] args)
        {
            args = null;
            BlobHandle blobHandle = reader.GetCustomAttribute(handle).Value;
            if (blobHandle.IsNil)
            {
                return false;
            }

            BlobReader blob = reader.GetBlobReader(blobHandle);
            // Prolog: two bytes 0x01 0x00.
            if (blob.Length < 2)
            {
                return false;
            }
            if (blob.ReadUInt16() != 0x0001)
            {
                return false;
            }

            // Read up to four SerString fixed args; ignore trailing NumNamed section.
            var list = new List<string>(4);
            for (int i = 0; i < 4 && blob.RemainingBytes > 0; i++)
            {
                if (!TryReadSerString(ref blob, out string value))
                {
                    break;
                }
                list.Add(value ?? string.Empty);
            }

            args = list.ToArray();
            return args.Length >= 2; // need at least ModelUri + Prefix
        }

        private static bool TryReadSerString(ref BlobReader blob, out string value)
        {
            if (blob.RemainingBytes < 1)
            {
                value = null;
                return false;
            }

            // Peek the first byte for the null marker without advancing unrecoverably.
            int startOffset = blob.Offset;
            byte first = blob.ReadByte();
            if (first == 0xFF)
            {
                value = null;
                return true;
            }

            // Back up one byte and let ReadSerializedString do the length-compressed decode.
            blob.Offset = startOffset;
            value = blob.ReadSerializedString();
            return true;
        }

        private static string GetArg(string[] args, int index)
        {
            return args != null && index < args.Length ? args[index] : string.Empty;
        }
    }
}
