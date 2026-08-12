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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Aas.Server.Registry;

namespace Opc.Ua.Aas.Server.Materialization
{
    /// <summary>
    /// Per-session access policy used when exporting an AAS environment document.
    /// </summary>
    public interface IAasEnvironmentExportAccessPolicy
    {
        /// <summary>
        /// Gets whether a session may browse and read a metamodel node path.
        /// </summary>
        bool CanRead(ISystemContext? context, string path);
    }

    /// <summary>
    /// Export request for an AASEnvironmentFileType resource.
    /// </summary>
    public sealed class AasEnvironmentExportRequest
    {
        /// <summary>
        /// Gets or sets the calling system context.
        /// </summary>
        public ISystemContext? Context { get; set; }

        /// <summary>
        /// Gets or sets the requested format.
        /// </summary>
        public string Format { get; set; } = "aas/3.0+json";
    }

    /// <summary>
    /// Exported environment document bytes and caller-dependent metadata.
    /// </summary>
    public sealed class AasEnvironmentExportResult
    {
        /// <summary>
        /// Initializes a result.
        /// </summary>
        public AasEnvironmentExportResult(ByteString content, string format, bool filtered, ByteString digest)
        {
            Content = content;
            Format = format ?? string.Empty;
            Filtered = filtered;
            Digest = digest;
        }

        /// <summary>
        /// Gets the exported bytes.
        /// </summary>
        public ByteString Content { get; }

        /// <summary>
        /// Gets the exported format.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Gets whether the document omitted nodes for the caller.
        /// </summary>
        public bool Filtered { get; }

        /// <summary>
        /// Gets the digest for lossless, unfiltered bytes; empty for filtered bytes.
        /// </summary>
        public ByteString Digest { get; }
    }

    /// <summary>
    /// Exports whole materialized AAS environments as caller-filtered documents.
    /// </summary>
    public sealed class AasEnvironmentExporter
    {
        /// <summary>
        /// Initializes an exporter.
        /// </summary>
        public AasEnvironmentExporter(IAasEnvironmentExportAccessPolicy? accessPolicy = null)
        {
            m_accessPolicy = accessPolicy ?? AllowAllAasEnvironmentExportAccessPolicy.Instance;
        }

        /// <summary>
        /// Exports a whole environment in JSON or AASX format.
        /// </summary>
        public async ValueTask<AasEnvironmentExportResult> ExportAsync(
            AasEnvironment environment,
            AasEnvironmentExportRequest request,
            CancellationToken cancellationToken = default)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            FilterResult filtered = Filter(environment, request.Context);
            using var stream = new MemoryStream();
            if (string.Equals(request.Format, "aasx/3.0", StringComparison.OrdinalIgnoreCase))
            {
                await new AasxPackageWriter()
                    .WriteAsync(stream, filtered.Environment, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(request.Format, "aas/3.0+json", StringComparison.OrdinalIgnoreCase))
            {
                await new AasJsonWriter()
                    .WriteAsync(stream, filtered.Environment, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException("The requested AAS environment export format is not supported.");
            }

            ByteString content = ByteString.From(stream.ToArray());
            ByteString digest = filtered.Filtered
                ? ByteString.Empty
                : AasRegistryContentDigest.Compute(content);
            return new AasEnvironmentExportResult(content, request.Format, filtered.Filtered, digest);
        }

        private FilterResult Filter(AasEnvironment environment, ISystemContext? context)
        {
            bool filtered = false;
            ArrayOf<AasShell> shells = FilterIdentifiables(
                environment.AssetAdministrationShells,
                "shells",
                context,
                ref filtered);
            ArrayOf<AasSubmodel> submodels = FilterSubmodels(
                environment.Submodels,
                context,
                ref filtered);
            ArrayOf<AasConceptDescription> concepts = FilterIdentifiables(
                environment.ConceptDescriptions,
                "conceptDescriptions",
                context,
                ref filtered);
            return new FilterResult(
                new AasEnvironment
                {
                    AssetAdministrationShells = environment.AssetAdministrationShells.IsPresent
                        ? AasOptional<ArrayOf<AasShell>>.Present(shells)
                        : default,
                    Submodels = environment.Submodels.IsPresent
                        ? AasOptional<ArrayOf<AasSubmodel>>.Present(submodels)
                        : default,
                    ConceptDescriptions = environment.ConceptDescriptions.IsPresent
                        ? AasOptional<ArrayOf<AasConceptDescription>>.Present(concepts)
                        : default
                },
                filtered);
        }

        private ArrayOf<T> FilterIdentifiables<T>(
            AasOptional<ArrayOf<T>> values,
            string collectionName,
            ISystemContext? context,
            ref bool filtered)
            where T : AasIdentifiable
        {
            if (!values.IsPresent)
            {
                return [];
            }
            var result = new List<T>();
            foreach (T value in values.Value)
            {
                if (Allowed(context, collectionName + "/" + value.Id))
                {
                    result.Add(value);
                }
                else
                {
                    filtered = true;
                }
            }
            return new ArrayOf<T>(result.ToArray());
        }

        private ArrayOf<AasSubmodel> FilterSubmodels(
            AasOptional<ArrayOf<AasSubmodel>> values,
            ISystemContext? context,
            ref bool filtered)
        {
            if (!values.IsPresent)
            {
                return [];
            }
            var result = new List<AasSubmodel>();
            foreach (AasSubmodel submodel in values.Value)
            {
                string path = "submodels/" + submodel.Id;
                if (!Allowed(context, path))
                {
                    filtered = true;
                    continue;
                }
                if (!submodel.SubmodelElements.IsPresent)
                {
                    result.Add(submodel);
                    continue;
                }
                ArrayOf<AasSubmodelElement> elements = FilterElements(
                    submodel.SubmodelElements.Value,
                    path,
                    context,
                    ref filtered);
                result.Add(submodel with { SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(elements) });
            }
            return new ArrayOf<AasSubmodel>(result.ToArray());
        }

        private ArrayOf<AasSubmodelElement> FilterElements(
            ArrayOf<AasSubmodelElement> elements,
            string parentPath,
            ISystemContext? context,
            ref bool filtered)
        {
            var result = new List<AasSubmodelElement>();
            foreach (AasSubmodelElement element in elements)
            {
                string idShort = element.IdShort.IsPresent
                    ? element.IdShort.Value
                    : result.Count.ToString(CultureInfo.InvariantCulture);
                string path = parentPath + "/" + idShort;
                if (!Allowed(context, path))
                {
                    filtered = true;
                    continue;
                }
                result.Add(FilterElement(element, path, context, ref filtered));
            }
            return new ArrayOf<AasSubmodelElement>(result.ToArray());
        }

        private AasSubmodelElement FilterElement(
            AasSubmodelElement element,
            string path,
            ISystemContext? context,
            ref bool filtered)
        {
            if (element is AasSubmodelElementCollection collection && collection.Value.IsPresent)
            {
                return collection with
                {
                    Value = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                        FilterElements(collection.Value.Value, path, context, ref filtered))
                };
            }
            if (element is AasSubmodelElementList list && list.Value.IsPresent)
            {
                return list with
                {
                    Value = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                        FilterElements(list.Value.Value, path, context, ref filtered))
                };
            }
            return element;
        }

        private bool Allowed(ISystemContext? context, string path)
        {
            return m_accessPolicy.CanRead(context, path);
        }

        private sealed record FilterResult(AasEnvironment Environment, bool Filtered);

        private sealed class AllowAllAasEnvironmentExportAccessPolicy : IAasEnvironmentExportAccessPolicy
        {
            public static AllowAllAasEnvironmentExportAccessPolicy Instance { get; } = new();

            public bool CanRead(ISystemContext? context, string path)
            {
                return true;
            }
        }

        private readonly IAasEnvironmentExportAccessPolicy m_accessPolicy;
    }
}
