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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Aas.V2;

namespace Opc.Ua.Aas.Server.V2
{
    /// <summary>
    /// Supplies values for an AAS V2 AddressSpace.
    /// </summary>
    /// <remarks>
    /// The contract itself carries no metamodel generation - it is keyed on
    /// <see cref="NodeId"/> and <see cref="Variant"/> - but the registration
    /// has to, because a host that registers both generations would otherwise
    /// resolve one <see cref="IAasValueProvider"/> for both AddressSpaces and
    /// silently serve one generation's values through the other's nodes. This
    /// mirrors the split already made for <see cref="IAasV2EnvironmentProvider"/>.
    /// </remarks>
    public interface IAasV2ValueProvider : IAasValueProvider
    {
    }

    /// <summary>
    /// Default AAS V2 value provider that serves values carried by loaded documents.
    /// </summary>
    public sealed class DocumentAasV2ValueProvider : IAasV2ValueProvider
    {
        /// <summary>
        /// Initializes an empty provider.
        /// </summary>
        public DocumentAasV2ValueProvider()
        {
        }

        /// <summary>
        /// Initializes a provider from AAS V2 environments.
        /// </summary>
        public DocumentAasV2ValueProvider(ArrayOf<AasEnvironment> environments)
        {
            if (!environments.IsNull)
            {
                for (int i = 0; i < environments.Count; i++)
                {
                    AddEnvironment(environments[i]);
                }
            }
        }

        /// <summary>
        /// Adds all document-carried values from an AAS V2 environment.
        /// </summary>
        public void AddEnvironment(AasEnvironment environment)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            if (environment.Submodels.IsPresent)
            {
                foreach (AasSubmodel submodel in environment.Submodels.Value.Span)
                {
                    AddSubmodel(submodel.Identification.Id, submodel);
                }
            }
            if (environment.AssetAdministrationShells.IsPresent)
            {
                foreach (AasShell shell in environment.AssetAdministrationShells.Value.Span)
                {
                    if (shell.Submodels.IsPresent)
                    {
                        foreach (AasSubmodel submodel in shell.Submodels.Value.Span)
                        {
                            AddSubmodel(shell.Identification.Id, submodel);
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask<AasValueReadResult> ReadValueAsync(
            NodeId valueNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (!m_values.TryGetValue(valueNodeId, out Variant value))
                {
                    return new ValueTask<AasValueReadResult>(new AasValueReadResult(
                        StatusCodes.BadNodeIdUnknown,
                        Variant.Null,
                        StatusCodes.BadNodeIdUnknown,
                        DateTime.UtcNow));
                }
                return new ValueTask<AasValueReadResult>(new AasValueReadResult(
                    ServiceResult.Good,
                    value,
                    StatusCodes.Good,
                    DateTime.UtcNow));
            }
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> WriteValueAsync(
            NodeId valueNodeId,
            Variant value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                m_values[valueNodeId] = value;
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        private void AddSubmodel(string ownerId, AasSubmodel submodel)
        {
            AddElements(ownerId, string.Empty, submodel.SubmodelElements, ordered: false);
        }

        private void AddElements(
            string ownerId,
            string parentPath,
            AasOptional<ArrayOf<AasSubmodelElement>> elements,
            bool ordered)
        {
            if (!elements.IsPresent)
            {
                return;
            }

            for (int i = 0; i < elements.Value.Count; i++)
            {
                string path = ordered
                    ? AasIdShortPath.AppendIndex(parentPath, i)
                    : AasIdShortPath.AppendName(parentPath, elements.Value[i].IdShort);
                AddElementAtPath(ownerId, path, elements.Value[i]);
            }
        }

        private void AddElementAtPath(string ownerId, string path, AasSubmodelElement element)
        {
            string elementId = AasNodeIdEncoding.CreateElementId(ownerId, path);
            switch (element)
            {
                case AasBlob blob:
                    AddFileValue(elementId, blob.File, string.Empty);
                    break;
                case AasEntity entity:
                    AddElements(ownerId, path, entity.Statements, ordered: false);
                    break;
                case AasFile file:
                    AddMember(elementId, "Value", new Variant(file.Value));
                    AddFileValue(elementId, file.File, file.MimeType);
                    break;
                case AasMultiLanguageProperty multiLanguage when multiLanguage.Value.IsPresent:
                    AddMember(elementId, "Value", new Variant(multiLanguage.Value.Value));
                    break;
                case AasProperty property when property.Value.IsPresent:
                    AddMember(elementId, "Value", property.Value.Value);
                    break;
                case AasRange range:
                    AddRangeValue(elementId, "Min", range.Min);
                    AddRangeValue(elementId, "Max", range.Max);
                    break;
                case AasAnnotatedRelationshipElement annotated:
                    AddElements(ownerId, path, annotated.DataElements, ordered: false);
                    break;
                case AasOrderedSubmodelElementCollection orderedCollection:
                    AddElements(ownerId, path, orderedCollection.SubmodelElements, ordered: true);
                    break;
                case AasSubmodelElementCollection collection:
                    AddElements(ownerId, path, collection.SubmodelElements, ordered: false);
                    break;
            }
        }

        private void AddFileValue(
            string elementId,
            AasOptional<AasFileObject> file,
            string contentType)
        {
            if (file.IsPresent && file.Value.Value.IsPresent)
            {
                AddMember(elementId + "." + AasNodeIdEncoding.Escape("File"), "Value", new Variant(file.Value.Value.Value));
            }
        }

        private void AddRangeValue(string elementId, string browseName, AasOptional<Variant> value)
        {
            if (value.IsPresent)
            {
                AddMember(elementId, browseName, value.Value);
            }
        }

        private void AddMember(string elementId, string browseName, Variant value)
        {
            m_values[new NodeId(elementId + "." + AasNodeIdEncoding.Escape(browseName), 1)] = value;
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<NodeId, Variant> m_values = [];
    }
}
