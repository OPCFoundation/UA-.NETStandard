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
using Opc.Ua.Vision.MachineVision;
using Mv = Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Vision.Server
{
    public sealed partial class MachineVisionSystemHandle
    {
        /// <summary>
        /// Publishes a provider-generated result outside the execution stream, for example
        /// a result completed asynchronously by another acquisition interface.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="result"/> is <c>null</c>.</exception>
        public async ValueTask PublishResultAsync(
            ResultDataType result,
            ByteString fileContent = default,
            CancellationToken cancellationToken = default)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            await EnterRuntimeAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await StoreResultAsync(CloneStructure(result), fileContent, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        private async ValueTask PublishResultCoreAsync(
            MachineVisionJob job, MachineVisionAcquisition acquisition, CancellationToken ct)
        {
            ResultDataType result = CloneStructure(acquisition.Result!);
            if (string.IsNullOrEmpty(result.ResultId.Id))
            {
                result.ResultId = new ResultIdDataType { Id = Guid.NewGuid().ToString("N") };
            }
            result.JobId = job.JobId;
            result.MeasId = job.MeasurementId;
            result.PartId = job.PartId;
            result.ProductId = job.ProductId;
            result.IsSimulated = job.Simulated;
            result.EncodingMask |= (uint)(ResultDataTypeFields.MeasId |
                ResultDataTypeFields.PartId |
                ResultDataTypeFields.ProductId |
                ResultDataTypeFields.IsSimulated);
            if (job.Recipe != null)
            {
                var recipe = RecipeRecord.From(job.Recipe);
                result.InternalRecipeId = recipe.InternalId;
                result.ExternalRecipeId = recipe.ExternalId;
                result.EncodingMask |= (uint)ResultDataTypeFields.ExternalRecipeId;
            }
            if (job.Configuration != null)
            {
                ConfigurationDataType configuration = Metadata<ConfigurationDataType>(job.Configuration);
                result.InternalConfigurationId = configuration.InternalId;
                result.ExternalConfigurationId = configuration.ExternalId;
                result.EncodingMask |= (uint)ResultDataTypeFields.ExternalConfigurationId;
            }
            await StoreResultAsync(result, acquisition.FileContent, ct).ConfigureAwait(false);
        }

        private async ValueTask StoreResultAsync(ResultDataType result, ByteString content, CancellationToken ct)
        {
            string id = RequiredId(result.ResultId.Id);
            if (result.ResultState == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, "A published result must not have the Undefined result state.");
            }
            result.HasTransferableDataOnFile = !content.IsNull;
            result.EncodingMask |= (uint)ResultDataTypeFields.HasTransferableDataOnFile;
            if (result.CreationTime == default)
            {
                result.CreationTime = Now;
            }
            await m_store.WriteAsync(new MachineVisionDocument(
                MachineVisionDocumentKind.Result, id, Variant.FromStructure(result), content), ct)
                .ConfigureAwait(false);
            await RefreshFoldersAsync(ct).ConfigureAwait(false);
            await EmitAsync(new ResultReadyEventState(null), "Vision result available.",
                notification => SetResultMembers(notification, result)).ConfigureAwait(false);
        }

        private async ValueTask RefreshFoldersAsync(CancellationToken ct, NodeId committingFile = default)
        {
            if (!Has(MachineVisionFeatures.Folders))
            {
                return;
            }
            var present = new HashSet<(MachineVisionDocumentKind Kind, string Id)>();
            var products = new Dictionary<string, ProductIdDataType>(StringComparer.Ordinal);
            foreach (MachineVisionDocumentKind kind in s_documentKinds)
            {
                NodeState? management = kind switch
                {
                    MachineVisionDocumentKind.Recipe => State.RecipeManagement,
                    MachineVisionDocumentKind.Configuration => State.ConfigurationManagement,
                    MachineVisionDocumentKind.Result => State.ResultManagement,
                    _ => null
                };
                if (management == null)
                {
                    continue;
                }
                string folderName = kind switch
                {
                    MachineVisionDocumentKind.Recipe => "Recipes",
                    MachineVisionDocumentKind.Configuration => "Configurations",
                    _ => "Results"
                };
                BaseObjectState folder = MachineVisionNodes.Require<BaseObjectState>(
                    m_context.Manager, management, folderName);
                ArrayOf<MachineVisionDocument> documents = await m_store.ListAsync(kind, ct).ConfigureAwait(false);
                for (int ii = 0; ii < documents.Count; ii++)
                {
                    MachineVisionDocument document = documents[ii];
                    var key = (kind, document.Id);
                    present.Add(key);
                    if (!m_documentNodes.TryGetValue(key, out BaseInstanceState? node))
                    {
                        var name = new QualifiedName(document.Id, m_context.InstanceNamespaceIndex);
                        if (kind == MachineVisionDocumentKind.Recipe)
                        {
                            RecipeState recipe = m_context.Context.CreateInstanceOfRecipeType(folder, name);
                            BindRecipeInstance(recipe, document.Id);
                            node = recipe;
                        }
                        else if (kind == MachineVisionDocumentKind.Result)
                        {
                            node = m_context.Context.CreateInstanceOfResultType(folder, name);
                        }
                        else
                        {
                            node = CreateMetadataVariable(folder, name, Mv.DataTypeIds.ConfigurationDataType);
                        }
                        node.ReferenceTypeId = ReferenceTypeIds.Organizes;
                        folder.AddChild(node);
                        m_documentNodes.Add(key, node);
                    }
                    if (node is RecipeState recipeNode)
                    {
                        var recipe = RecipeRecord.From(document);
                        recipeNode.InternalId!.Value = recipe.InternalId;
                        recipeNode.CreateOrReplaceExternalId(m_context.Context, null).Value = recipe.ExternalId;
                        recipeNode.IsPrepared!.Value = recipe.Prepared;
                        recipeNode.LastModified!.Value = recipe.LastModified;
                        recipeNode.CreateOrReplaceLinkedProducts(m_context.Context, null).Value = recipe.Products;
                        foreach (ProductIdDataType product in recipe.Products)
                        {
                            products[RequiredId(product.Id)] = product;
                        }
                        if (Has(MachineVisionFeatures.FileTransfer))
                        {
                            await BindRecipeFileAsync(document, recipeNode, committingFile, ct).ConfigureAwait(false);
                        }
                    }
                    else if (node is BaseVariableState variable)
                    {
                        MachineVisionNodes.Set(m_context.Context, variable, document.Metadata, Now);
                        if (kind == MachineVisionDocumentKind.Result)
                        {
                            SetResultMembers(node, Metadata<ResultDataType>(document));
                        }
                    }
                    await m_context.Manager.AddPredefinedNodeAsync(node, ct).ConfigureAwait(false);
                    await node.ClearChangeMasksAsync(m_context.Context, true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                await m_context.Manager.AddPredefinedNodeAsync(folder, ct).ConfigureAwait(false);
            }
            var removed = new List<(MachineVisionDocumentKind Kind, string Id)>();
            foreach (KeyValuePair<(MachineVisionDocumentKind Kind, string Id), BaseInstanceState> pair
                in m_documentNodes)
            {
                if (!present.Contains(pair.Key))
                {
                    pair.Value.Parent?.RemoveChild(pair.Value);
                    await m_context.Manager.DeleteNodeAsync(
                        m_context.Manager.SystemContext, pair.Value.NodeId, ct).ConfigureAwait(false);
                    removed.Add(pair.Key);
                }
            }
            foreach ((MachineVisionDocumentKind kind, string id) in removed)
            {
                m_documentNodes.Remove((kind, id));
            }
            if (State.RecipeManagement != null)
            {
                BaseObjectState folder = MachineVisionNodes.Require<BaseObjectState>(
                    m_context.Manager, State.RecipeManagement, "Products");
                foreach (KeyValuePair<string, ProductIdDataType> pair in products)
                {
                    if (!m_productNodes.TryGetValue(pair.Key, out BaseVariableState? node))
                    {
                        node = CreateMetadataVariable(folder,
                            new QualifiedName(pair.Key, m_context.InstanceNamespaceIndex),
                            Mv.DataTypeIds.ProductDataType);
                        node.ReferenceTypeId = ReferenceTypeIds.Organizes;
                        folder.AddChild(node);
                        m_productNodes.Add(pair.Key, node);
                    }
                    MachineVisionNodes.Set(m_context.Context, node,
                        Variant.FromStructure(new ProductDataType { ExternalId = pair.Value }), Now);
                    await m_context.Manager.AddPredefinedNodeAsync(node, ct).ConfigureAwait(false);
                    await node.ClearChangeMasksAsync(m_context.Context, false, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                var deletedProducts = new List<string>();
                foreach (KeyValuePair<string, BaseVariableState> pair in m_productNodes)
                {
                    if (!products.ContainsKey(pair.Key))
                    {
                        folder.RemoveChild(pair.Value);
                        await m_context.Manager.DeleteNodeAsync(
                            m_context.Manager.SystemContext, pair.Value.NodeId, ct).ConfigureAwait(false);
                        deletedProducts.Add(pair.Key);
                    }
                }
                foreach (string id in deletedProducts)
                {
                    m_productNodes.Remove(id);
                }
                await m_context.Manager.AddPredefinedNodeAsync(folder, ct).ConfigureAwait(false);
            }
        }

        private void BindRecipeInstance(RecipeState recipe, string id)
        {
            var internalId = new RecipeIdInternalDataType { Id = id };
            Bind(recipe, "Prepare", async (context, args, output, ct) =>
            {
                MachineVisionOperationResult outcome = await PrepareRecipeAsync(
                    new RecipeIdExternalDataType(), internalId, true, output, ct).ConfigureAwait(false);
                if (outcome.Succeeded)
                {
                    Outputs(output, Variant.From(true), Variant.From(0));
                }
                return outcome;
            });
            Bind(recipe, "Unprepare", async (context, args, output, ct) =>
            {
                MachineVisionOperationResult outcome = await PrepareRecipeAsync(
                    new RecipeIdExternalDataType(), internalId, false, output, ct).ConfigureAwait(false);
                if (outcome.Succeeded)
                {
                    Outputs(output, Variant.From(0));
                }
                return outcome;
            });
            Bind(recipe, "LinkProduct", (context, args, output, ct) =>
                LinkProductAsync(internalId, Structure<ProductIdDataType>(args, 0), true, output, ct));
            Bind(recipe, "UnlinkProduct", (context, args, output, ct) =>
                LinkProductAsync(internalId, Structure<ProductIdDataType>(args, 0), false, output, ct));
        }

        private BaseDataVariableState CreateMetadataVariable(
            NodeState parent, QualifiedName name, ExpandedNodeId dataType)
        {
            var variable = new BaseDataVariableState(parent);
            variable.Create(m_context.Context, NodeId.Null, name, new LocalizedText(name.Name), true);
            variable.DataType = ExpandedNodeId.ToNodeId(dataType, m_context.Context.NamespaceUris);
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            MachineVisionNodes.Rebase(m_context.Context, variable);
            return variable;
        }

        private NodeId DocumentNodeId(MachineVisionDocumentKind kind, string? id)
        {
            return id != null && m_documentNodes.TryGetValue((kind, id), out BaseInstanceState? node)
                ? node.NodeId : NodeId.Null;
        }

        private NodeId ProductNodeId(string? id)
        {
            return !string.IsNullOrEmpty(id) && m_productNodes.TryGetValue(id, out BaseVariableState? node)
                ? node.NodeId : NodeId.Null;
        }

        private void SetResultMembers(NodeState node, ResultDataType result)
        {
            var fields = new (string Name, Variant Value, ResultDataTypeFields OptionalField)[]
            {
                ("ResultId", Variant.FromStructure(result.ResultId), 0),
                ("IsPartial", Variant.From(result.IsPartial), 0),
                ("IsSimulated", Variant.From(result.IsSimulated), ResultDataTypeFields.IsSimulated),
                ("ResultState", Variant.From(result.ResultState), 0),
                ("MeasId", Variant.FromStructure(result.MeasId), ResultDataTypeFields.MeasId),
                ("PartId", Variant.FromStructure(result.PartId), ResultDataTypeFields.PartId),
                ("ExternalRecipeId", Variant.FromStructure(result.ExternalRecipeId),
                    ResultDataTypeFields.ExternalRecipeId),
                ("InternalRecipeId", Variant.FromStructure(result.InternalRecipeId), 0),
                ("ProductId", Variant.FromStructure(result.ProductId), ResultDataTypeFields.ProductId),
                ("ExternalConfigurationId", Variant.FromStructure(result.ExternalConfigurationId),
                    ResultDataTypeFields.ExternalConfigurationId),
                ("InternalConfigurationId", Variant.FromStructure(result.InternalConfigurationId), 0),
                ("JobId", Variant.FromStructure(result.JobId), 0),
                ("CreationTime", Variant.From(result.CreationTime), 0),
                ("ProcessingTimes", Variant.FromStructure(result.ProcessingTimes),
                    ResultDataTypeFields.ProcessingTimes),
                ("ResultContent", Variant.From(result.ResultContent), ResultDataTypeFields.ResultContent)
            };
            ushort ns = (ushort)m_context.Context.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri);
            foreach ((string name, Variant value, ResultDataTypeFields optional) in fields)
            {
                if (optional != 0 && (result.EncodingMask & (uint)optional) == 0)
                {
                    if (node.FindChild(m_context.Context, new QualifiedName(name, ns)) is BaseVariableState absent)
                    {
                        MachineVisionNodes.Set(m_context.Context, absent, Variant.Null, Now);
                    }
                    continue;
                }
                BaseVariableState child = MachineVisionNodes.Require<BaseVariableState>(m_context.Manager, node, name);
                MachineVisionNodes.Set(m_context.Context, child, value, Now);
            }
            if (node is ResultVariableState resultNode)
            {
                MachineVisionNodes.Set(m_context.Context,
                    MachineVisionNodes.Require<BaseVariableState>(
                        m_context.Manager, resultNode, "HasTransferableDataOnFile"),
                    Variant.From(result.HasTransferableDataOnFile), Now);
            }
        }

        private readonly Dictionary<(MachineVisionDocumentKind Kind, string Id), BaseInstanceState> m_documentNodes =
            [];

        private readonly Dictionary<string, BaseVariableState> m_productNodes = new(StringComparer.Ordinal);

        private static readonly MachineVisionDocumentKind[] s_documentKinds =
        [
            MachineVisionDocumentKind.Recipe,
            MachineVisionDocumentKind.Configuration,
            MachineVisionDocumentKind.Result
        ];

        private sealed record RecipeRecord(
            RecipeIdExternalDataType ExternalId,
            RecipeIdInternalDataType InternalId,
            ArrayOf<ProductIdDataType> Products,
            bool Prepared,
            DateTimeUtc LastModified)
        {
            public Variant ToVariant()
            {
                ArrayOf<Variant> values =
                [
                    Variant.FromStructure(ExternalId),
                    Variant.FromStructure(InternalId),
                    Variant.FromStructure(Products),
                    Variant.From(Prepared),
                    Variant.From(LastModified)
                ];
                return Variant.From(values);
            }

            public bool HasProduct(string id)
            {
                foreach (ProductIdDataType product in Products)
                {
                    if (product.Id == id)
                    {
                        return true;
                    }
                }
                return false;
            }

            public RecipeRecord WithProduct(ProductIdDataType product, bool linked)
            {
                if (string.IsNullOrWhiteSpace(product.Id))
                {
                    return this;
                }
                string id = product.Id.Trim();
                var products = new List<ProductIdDataType>();
                foreach (ProductIdDataType existing in Products)
                {
                    if (existing.Id != id)
                    {
                        products.Add(existing);
                    }
                }
                if (linked)
                {
                    ProductIdDataType copy = CloneStructure(product);
                    copy.Id = id;
                    products.Add(copy);
                }
                return this with { Products = products.ToArrayOf() };
            }

            public static RecipeRecord From(MachineVisionDocument document)
            {
                if (!document.Metadata.TryGetValue(out ArrayOf<Variant> values) ||
                    values.Count != 5 ||
                    !values[0].TryGetStructure<RecipeIdExternalDataType>(out RecipeIdExternalDataType? external) ||
                    !values[1].TryGetStructure<RecipeIdInternalDataType>(out RecipeIdInternalDataType? internalId) ||
                    !values[2].TryGetStructure(out ArrayOf<ProductIdDataType> products) ||
                    !values[3].TryGetValue(out bool prepared) ||
                    !values[4].TryGetValue(out DateTimeUtc modified))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError, "Invalid stored recipe metadata.");
                }
                return new RecipeRecord(external, internalId, products, prepared, modified);
            }
        }
    }
}
