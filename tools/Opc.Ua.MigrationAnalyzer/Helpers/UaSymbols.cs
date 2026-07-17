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
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Opc.Ua.MigrationAnalyzer.Helpers
{
    /// <summary>
    /// Cached lookup of well-known OPC UA <see cref="INamedTypeSymbol"/>s for a
    /// single <see cref="Compilation"/>. Analyzers register a compilation-start
    /// action that creates one instance per compilation so symbol resolution
    /// happens at most once per build.
    /// </summary>
    internal sealed class UaSymbols
    {
        private UaSymbols(Compilation compilation)
        {
            Compilation = compilation;

            // Struct built-ins (NodeId, Variant, ...) that became readonly structs in 2.0.
            BuiltInStructTypes = ImmutableArray.Create(
                Get("Opc.Ua.NodeId"),
                Get("Opc.Ua.ExpandedNodeId"),
                Get("Opc.Ua.QualifiedName"),
                Get("Opc.Ua.LocalizedText"),
                Get("Opc.Ua.ExtensionObject"),
                Get("Opc.Ua.DataValue"),
                Get("Opc.Ua.Variant"),
                Get("Opc.Ua.ByteString"));

            UtilsType = Get("Opc.Ua.Utils");
            VariantType = Get("Opc.Ua.Variant");
            VariantNullField = VariantType?.GetMembers("Null").Length > 0 ? "Null" : null;
            DataValueType = Get("Opc.Ua.DataValue");
            NodeIdType = Get("Opc.Ua.NodeId");
            ExpandedNodeIdType = Get("Opc.Ua.ExpandedNodeId");
            ByteStringType = Get("Opc.Ua.ByteString");
            StatusCodeType = Get("Opc.Ua.StatusCode");
            DateTimeUtcType = Get("Opc.Ua.DateTimeUtc");
            UuidType = Get("Opc.Ua.Uuid");
            CertificateIdentifierType = Get("Opc.Ua.CertificateIdentifier");
            UserIdentityType = Get("Opc.Ua.UserIdentity");
            UserIdentityTokenHandlerType = Get("Opc.Ua.IUserIdentityTokenHandler");
            CertificateFactoryType = Get("Opc.Ua.CertificateFactory");
            SessionType = Get("Opc.Ua.Client.Session");
            SessionInterfaceType = Get("Opc.Ua.Client.ISession");
            EncodeableFactoryType = Get("Opc.Ua.EncodeableFactory");
            ServiceMessageContextType = Get("Opc.Ua.ServiceMessageContext");
            TelemetryContextType = Get("Opc.Ua.ITelemetryContext");
            LoggerType = Get("Microsoft.Extensions.Logging.ILogger");
            DataContractType = Get("System.Runtime.Serialization.DataContractAttribute");
            DataMemberType = Get("System.Runtime.Serialization.DataMemberAttribute");
        }

        public Compilation Compilation { get; }

        /// <summary>NodeId, Variant, DataValue, ... — all readonly structs in 2.0.</summary>
        public ImmutableArray<INamedTypeSymbol> BuiltInStructTypes { get; }

        public INamedTypeSymbol UtilsType { get; }
        public INamedTypeSymbol VariantType { get; }

        /// <summary>Name of the static "null" field on Variant (e.g. <c>Variant.Null</c>).</summary>
        public string VariantNullField { get; }

        public INamedTypeSymbol DataValueType { get; }
        public INamedTypeSymbol NodeIdType { get; }
        public INamedTypeSymbol ExpandedNodeIdType { get; }
        public INamedTypeSymbol ByteStringType { get; }
        public INamedTypeSymbol StatusCodeType { get; }
        public INamedTypeSymbol DateTimeUtcType { get; }
        public INamedTypeSymbol UuidType { get; }
        public INamedTypeSymbol CertificateIdentifierType { get; }
        public INamedTypeSymbol UserIdentityType { get; }
        public INamedTypeSymbol UserIdentityTokenHandlerType { get; }
        public INamedTypeSymbol CertificateFactoryType { get; }
        public INamedTypeSymbol SessionType { get; }
        public INamedTypeSymbol SessionInterfaceType { get; }
        public INamedTypeSymbol EncodeableFactoryType { get; }
        public INamedTypeSymbol ServiceMessageContextType { get; }
        public INamedTypeSymbol TelemetryContextType { get; }
        public INamedTypeSymbol LoggerType { get; }
        public INamedTypeSymbol DataContractType { get; }
        public INamedTypeSymbol DataMemberType { get; }

        /// <summary>True iff the compilation references at least one OPC UA 2.0 surface.</summary>
        public bool ReferencesOpcUa =>
            NodeIdType != null || VariantType != null || DataValueType != null;

        public bool IsBuiltInStructType(ITypeSymbol type)
        {
            if (type is null)
            {
                return false;
            }
            foreach (INamedTypeSymbol candidate in BuiltInStructTypes)
            {
                if (candidate != null && SymbolEqualityComparer.Default.Equals(candidate, type))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Resolve a well-known type by fully-qualified metadata name.</summary>
        public INamedTypeSymbol Get(string fullyQualifiedName)
        {
            return Compilation.GetTypeByMetadataName(fullyQualifiedName);
        }

        public static UaSymbols Create(Compilation compilation)
        {
            if (compilation is null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }
            return new UaSymbols(compilation);
        }

        /// <summary>
        /// Convenience: pull a cached <see cref="UaSymbols"/> instance keyed by
        /// the Compilation. Analyzers should call this in their compilation-start
        /// callback so symbols are resolved exactly once per build.
        /// </summary>
        public static UaSymbols For(Compilation compilation, Dictionary<Compilation, UaSymbols> cache)
        {
            if (!cache.TryGetValue(compilation, out UaSymbols symbols))
            {
                symbols = Create(compilation);
                cache[compilation] = symbols;
            }
            return symbols;
        }
    }
}
