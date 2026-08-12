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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Aas.Server
{
    /// <summary>
    /// Default value provider that serves the values carried by loaded documents.
    /// </summary>
    public sealed class DocumentAasValueProvider : IAasValueProvider
    {
        /// <summary>
        /// Initializes an empty provider.
        /// </summary>
        public DocumentAasValueProvider()
        {
        }

        /// <summary>
        /// Initializes a provider from environments.
        /// </summary>
        public DocumentAasValueProvider(ArrayOf<AasEnvironment> environments)
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
        /// Adds all document-carried values from an environment.
        /// </summary>
        public void AddEnvironment(AasEnvironment environment)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            if (!environment.Submodels.IsPresent)
            {
                return;
            }

            foreach (AasSubmodel submodel in environment.Submodels.Value.Span)
            {
                if (submodel.SubmodelElements.IsPresent)
                {
                    AddElements(submodel.Id, string.Empty, submodel.SubmodelElements.Value);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<AasValueReadResult> ReadValueAsync(
            NodeId valueNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (!m_values.TryGetValue(valueNodeId, out Variant value))
                {
                    return new AasValueReadResult(
                        StatusCodes.BadNodeIdUnknown,
                        Variant.Null,
                        StatusCodes.BadNodeIdUnknown,
                        DateTime.UtcNow);
                }
                return new AasValueReadResult(ServiceResult.Good, value, StatusCodes.Good, DateTime.UtcNow);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ServiceResult> WriteValueAsync(
            NodeId valueNodeId,
            Variant value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                m_values[valueNodeId] = value;
                return ServiceResult.Good;
            }
        }

        private void AddElements(string ownerId, string parentPath, ArrayOf<AasSubmodelElement> elements)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                AddElement(ownerId, parentPath, elements[i]);
            }
        }

        private void AddElement(string ownerId, string parentPath, AasSubmodelElement element)
        {
            string path = string.IsNullOrEmpty(parentPath)
                ? element.IdShort.Value
                : parentPath + "." + element.IdShort.Value;
            string elementId = AasNodeIdEncoding.CreateElementId(ownerId, path);
            switch (element)
            {
                case AasProperty property when property.Value.IsPresent:
                    AddMember(elementId, "Value", property.Value.Value);
                    break;
                case AasMultiLanguageProperty multiLanguage when multiLanguage.Value.IsPresent:
                    AddMember(elementId, "Value", StructureArrayVariant(multiLanguage.Value.Value));
                    break;
                case AasBlob blob when blob.Value.IsPresent:
                    AddMember(elementId, "Value", new Variant(blob.Value.Value));
                    break;
                case AasFile file when file.Value.IsPresent:
                    AddMember(elementId, "Value", new Variant(file.Value.Value));
                    break;
                case AasReferenceElement referenceElement when referenceElement.Value.IsPresent:
                    AddMember(elementId, "Value", new Variant(new ExtensionObject(referenceElement.Value.Value)));
                    break;
                case AasSubmodelElementCollection collection when collection.Value.IsPresent:
                    AddElements(ownerId, path, collection.Value.Value);
                    break;
                case AasSubmodelElementList list when list.Value.IsPresent:
                    AddElements(ownerId, path, list.Value.Value);
                    break;
                case AasOperation operation:
                    AddOperationVariables(ownerId, path, operation);
                    break;
            }
        }

        private void AddOperationVariables(string ownerId, string path, AasOperation operation)
        {
            AddOperationRole(ownerId, path, AasOperationVariableRole.Input, operation.InputVariables);
            AddOperationRole(ownerId, path, AasOperationVariableRole.Output, operation.OutputVariables);
            AddOperationRole(ownerId, path, AasOperationVariableRole.Inoutput, operation.InoutputVariables);
        }

        private void AddOperationRole(
            string ownerId,
            string path,
            AasOperationVariableRole role,
            AasOptional<ArrayOf<AasSubmodelElement>> variables)
        {
            if (!variables.IsPresent)
            {
                return;
            }

            for (int i = 0; i < variables.Value.Count; i++)
            {
                AddElement(ownerId, AasIdShortPath.AppendOperationVariable(path, role, i), variables.Value[i]);
            }
        }

        private void AddMember(string elementId, string browseName, Variant value)
        {
            m_values[new NodeId(elementId + "." + AasNodeIdEncoding.Escape(browseName), 1)] = value;
        }

        private static Variant StructureArrayVariant(ArrayOf<AASLangStringDataType> values)
        {
            var extensions = new ExtensionObject[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                extensions[i] = new ExtensionObject(values[i]);
            }
            return Variant.From(new ArrayOf<ExtensionObject>(extensions));
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<NodeId, Variant> m_values = [];
    }
}
