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

using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Model compiler options
    /// </summary>
    internal sealed record class ModelCompilationOptions
    {
        /// <summary>
        /// Model options
        /// </summary>
        public DesignFileOptions Options { get; set; }

        /// <summary>
        /// -useAllowSubtypes
        /// </summary>
        public bool UseAllowSubtypes { get; set; }

        /// <summary>
        /// -exclude [id;id;id]
        /// </summary>
        public IReadOnlyList<string> Exclude { get; set; }

        /// <summary>
        /// Opt in to <see cref="ObjectMethodProxyGenerator"/>. When
        /// <c>true</c>, the source generator emits a typed asynchronous
        /// client wrapper (<c>{TypeName}Client</c>) for every
        /// <c>ObjectType</c> in the model — including types without
        /// methods, so downstream models can derive from the emitted
        /// proxies. Each generated proxy inherits from the proxy of its
        /// parent ObjectType (forming a chain that mirrors the OPC UA
        /// type hierarchy and ultimately roots at the hand-authored
        /// <c>Opc.Ua.ObjectTypeClient</c> base). Methods that share a
        /// name with an ancestor method are emitted with the C#
        /// <c>new</c> modifier so the derived signature shadows the
        /// ancestor. Surfaced from MSBuild via the
        /// <c>ModelSourceGeneratorGenerateObjectMethodProxies</c> property.
        /// </summary>
        public bool GenerateObjectMethodProxies { get; set; }

        /// <summary>
        /// Opt in to ProxiesOnly mode for the
        /// <see cref="ObjectMethodProxyGenerator"/>. When <c>true</c>,
        /// the standard per-model generators (Constants, NodeIds,
        /// NodeStates, DataTypes, schemas) are skipped and only the
        /// proxy generator is run. Used when the proxies must land in a
        /// downstream assembly that already references a project
        /// containing the generated constants and types. Surfaced from
        /// MSBuild via the
        /// <c>ModelSourceGeneratorGenerateObjectMethodProxiesOnly</c>
        /// property. Implies <see cref="GenerateObjectMethodProxies"/>.
        /// </summary>
        public bool GenerateObjectMethodProxiesOnly { get; set; }

        /// <summary>
        /// Optional override for the C# namespace used by classes emitted
        /// by the <see cref="ObjectMethodProxyGenerator"/>. By default
        /// proxies are emitted into the model's own namespace (i.e. the
        /// C# prefix of the model's target namespace) — for example the
        /// standard UA NodeSet emits into <c>Opc.Ua</c>. Set this option
        /// to redirect proxy emission into a different namespace.
        /// Surfaced from MSBuild via the
        /// <c>ModelSourceGeneratorObjectMethodProxyNamespace</c> property.
        /// </summary>
        public string ObjectMethodProxyNamespace { get; set; }

        /// <summary>
        /// Get options from options provider
        /// </summary>
        /// <param name="provider"></param>
        /// <returns></returns>
        public static ModelCompilationOptions From(AnalyzerConfigOptionsProvider provider)
        {
            return new ModelCompilationOptions
            {
                Options = new DesignFileOptions
                {
                    Version = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.Version)) ??
                        "v105",
                    StartId = (uint)provider.GlobalOptions.GetInteger(
                        nameof(DesignFileOptions.StartId)),
                    ModelVersion = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.ModelVersion)),
                    ModelPublicationDate = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.ModelPublicationDate)),
                    ReleaseCandidate = provider.GlobalOptions.GetBool(
                        nameof(DesignFileOptions.ReleaseCandidate))
                },
                Exclude = provider.GlobalOptions.GetStrings(nameof(Exclude)),
                UseAllowSubtypes = provider.GlobalOptions.GetBool(nameof(UseAllowSubtypes)),
                GenerateObjectMethodProxies = provider.GlobalOptions.GetBool(
                    nameof(GenerateObjectMethodProxies)),
                GenerateObjectMethodProxiesOnly = provider.GlobalOptions.GetBool(
                    nameof(GenerateObjectMethodProxiesOnly)),
                ObjectMethodProxyNamespace = provider.GlobalOptions.GetString(
                    nameof(ObjectMethodProxyNamespace))
            };
        }
    }
}
