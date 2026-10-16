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
using Automatic = Opc.Ua.Vision.MachineVision.VisionAutomaticModeStateMachineTypeIds;
using Control = Opc.Ua.Vision.MachineVision.VisionStateMachineTypeIds;

namespace Opc.Ua.Vision.Server
{
    public sealed partial class MachineVisionSystemHandle
    {
        private async ValueTask BindManagementAsync(CancellationToken ct)
        {
            if (Has(MachineVisionFeatures.Recipes))
            {
                RecipeManagementState recipes = MachineVisionNodes.Require<RecipeManagementState>(
                    m_context.Manager, State, "RecipeManagement");
                Bind(recipes, "AddRecipe", AddRecipeAsync);
                Bind(recipes, "GetRecipeListFiltered", GetRecipesAsync);
                Bind(recipes, "PrepareRecipe", (ctx, args, output, token) =>
                    PrepareRecipeAsync(Structure<RecipeIdExternalDataType>(args, 0),
                        Structure<RecipeIdInternalDataType>(args, 1), true, output, token));
                Bind(recipes, "UnprepareRecipe", (ctx, args, output, token) =>
                    PrepareRecipeAsync(Structure<RecipeIdExternalDataType>(args, 0),
                        Structure<RecipeIdInternalDataType>(args, 1), false, output, token));
                Bind(recipes, "PrepareProduct", (ctx, args, output, token) =>
                    PrepareProductAsync(Structure<ProductIdDataType>(args, 0), true, output, token));
                Bind(recipes, "UnprepareProduct", (ctx, args, output, token) =>
                    PrepareProductAsync(Structure<ProductIdDataType>(args, 0), false, output, token));
                Bind(recipes, "UnlinkProduct", (ctx, args, output, token) =>
                    LinkProductAsync(Structure<RecipeIdInternalDataType>(args, 0),
                        Structure<ProductIdDataType>(args, 1), false, output, token));
                Bind(recipes, "RemoveRecipe", RemoveRecipeAsync);
                Bind(recipes, "ReleaseRecipeHandle", (ctx, args, output, token) =>
                    ReleaseAsync(ctx, MachineVisionDocumentKind.Recipe, UInt32(args, 0), output));
            }
            if (Has(MachineVisionFeatures.Configurations))
            {
                ConfigurationManagementState configurations = MachineVisionNodes.Require<ConfigurationManagementState>(
                    m_context.Manager, State, "ConfigurationManagement");
                Bind(configurations, "AddConfiguration", AddConfigurationAsync);
                Bind(configurations, "GetConfigurationById", GetConfigurationAsync);
                Bind(configurations, "GetConfigurationList", GetConfigurationsAsync);
                Bind(configurations, "ActivateConfiguration", ActivateConfigurationAsync);
                Bind(configurations, "RemoveConfiguration", RemoveConfigurationAsync);
                Bind(configurations, "ReleaseConfigurationHandle", (ctx, args, output, token) =>
                    ReleaseAsync(ctx, MachineVisionDocumentKind.Configuration, UInt32(args, 0), output));
                MachineVisionNodes.Require<BaseVariableState>(m_context.Manager, configurations, "ActiveConfiguration");
            }
            if (Has(MachineVisionFeatures.Results))
            {
                ResultManagementState results = MachineVisionNodes.Require<ResultManagementState>(
                    m_context.Manager, State, "ResultManagement");
                Bind(results, "GetResultById", (ctx, args, output, token) =>
                    GetResultAsync(ctx, args, output, false, token));
                Bind(results, "GetResultComponentsById", (ctx, args, output, token) =>
                    GetResultAsync(ctx, args, output, true, token));
                Bind(results, "GetResultListFiltered", GetResultsAsync);
                Bind(results, "ReleaseResultHandle", (ctx, args, output, token) =>
                    ReleaseAsync(ctx, MachineVisionDocumentKind.Result, UInt32(args, 0), output));
            }
            await BindTransfersAsync(ct).ConfigureAwait(false);
            MachineVisionDocument? active = await ActiveConfigurationAsync(ct).ConfigureAwait(false);
            if (active != null)
            {
                EnsureProviderSucceeded(await m_provider.ActivateConfigurationAsync(active, ct).ConfigureAwait(false));
                if (State.ConfigurationManagement?.ActiveConfiguration is BaseVariableState value)
                {
                    MachineVisionNodes.Set(m_context.Context, value, active.Metadata, Now);
                }
            }
            ArrayOf<MachineVisionDocument> prepared = await m_store.ListAsync(MachineVisionDocumentKind.Recipe, ct)
                .ConfigureAwait(false);
            for (int ii = 0; ii < prepared.Count; ii++)
            {
                if (RecipeRecord.From(prepared[ii]).Prepared)
                {
                    EnsureProviderSucceeded(await m_provider.PrepareRecipeAsync(prepared[ii], true, ct)
                        .ConfigureAwait(false));
                }
            }
            await RefreshFoldersAsync(ct).ConfigureAwait(false);
        }

        private async ValueTask<MachineVisionOperationResult> AddRecipeAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            RecipeIdExternalDataType external = CopyId(Structure<RecipeIdExternalDataType>(args, 0));
            external.Id = RequiredId(external.Id);
            ProductIdDataType product = Structure<ProductIdDataType>(args, 1);
            MachineVisionDocument? identical = null;
            ArrayOf<MachineVisionDocument> stored = await m_store.ListAsync(MachineVisionDocumentKind.Recipe, ct)
                .ConfigureAwait(false);
            foreach (MachineVisionDocument document in stored)
            {
                var candidate = RecipeRecord.From(document);
                if (!document.Content.IsNull && SameContent(external, candidate.ExternalId))
                {
                    identical = document;
                    break;
                }
            }
            RecipeRecord recipe = identical == null
                ? new RecipeRecord(external, new RecipeIdInternalDataType { Id = Guid.NewGuid().ToString("N") },
                    [], false, Now)
                : RecipeRecord.From(identical);
            recipe = recipe.WithProduct(product, true);
            ByteString content = default;
            if (identical is { } existing)
            {
                content = existing.Content;
            }
            var added = new MachineVisionDocument(MachineVisionDocumentKind.Recipe, RequiredId(recipe.InternalId.Id),
                recipe.ToVariant(), content);
            await m_store.WriteAsync(added, ct).ConfigureAwait(false);
            await RefreshFoldersAsync(ct).ConfigureAwait(false);
            Outputs(output, Variant.FromStructure(recipe.InternalId),
                Variant.From(DocumentNodeId(MachineVisionDocumentKind.Recipe, added.Id)),
                Variant.From(ProductNodeId(product.Id)), Variant.From(identical == null), Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> PrepareRecipeAsync(
            RecipeIdExternalDataType external,
            RecipeIdInternalDataType internalId,
            bool prepare,
            List<Variant> output,
            CancellationToken ct,
            bool productCause = false)
        {
            MachineVisionDocument document = await FindRecipeAsync(external, internalId, ct).ConfigureAwait(false);
            if (prepare && document.Content.IsNull)
            {
                throw InvalidState("Recipe content has not been committed.");
            }
            RecipeRecord recipe = RecipeRecord.From(document) with { Prepared = prepare };
            MachineVisionOperationResult outcome = await PersistAndApplyAsync(
                document with { Metadata = recipe.ToVariant() },
                token => m_provider.PrepareRecipeAsync(document, prepare, token), ct).ConfigureAwait(false);
            if (!outcome.Succeeded)
            {
                return outcome;
            }
            await RefreshFoldersAsync(ct).ConfigureAwait(false);
            await UpdateReadyStateAsync(ct,
                productCause ? Automatic.TransitionIds.InitializedToReadyProduct :
                    Automatic.TransitionIds.InitializedToReadyRecipe,
                productCause ? Automatic.TransitionIds.ReadyToInitializedProduct :
                    Automatic.TransitionIds.ReadyToInitializedRecipe)
                .ConfigureAwait(false);
            if (prepare)
            {
                await EmitAsync(new RecipePreparedEventState(null), "Recipe preparation completed.", notification =>
                {
                    notification.CreateOrReplaceExternalId(m_context.Context, null).Value = recipe.ExternalId;
                    notification.CreateOrReplaceInternalId(m_context.Context, null).Value = recipe.InternalId;
                    if (!recipe.Products.IsEmpty)
                    {
                        notification.CreateOrReplaceProductId(m_context.Context, null).Value = recipe.Products[0];
                    }
                }).ConfigureAwait(false);
                Outputs(output, Variant.FromStructure(recipe.InternalId), Variant.From(true), Variant.From(0));
            }
            else
            {
                Outputs(output, Variant.FromStructure(recipe.InternalId), Variant.From(0));
            }
            return outcome;
        }

        private async ValueTask<MachineVisionOperationResult> PrepareProductAsync(
            ProductIdDataType product, bool prepare, List<Variant> output, CancellationToken ct)
        {
            string productId = RequiredId(product.Id);
            ArrayOf<MachineVisionDocument> stored = await m_store.ListAsync(MachineVisionDocumentKind.Recipe, ct)
                .ConfigureAwait(false);
            for (int ii = 0; ii < stored.Count; ii++)
            {
                var recipe = RecipeRecord.From(stored[ii]);
                if (recipe.HasProduct(productId))
                {
                    MachineVisionOperationResult outcome = await PrepareRecipeAsync(
                        new RecipeIdExternalDataType(), recipe.InternalId, prepare, output, ct, productCause: true)
                        .ConfigureAwait(false);
                    if (outcome.Succeeded)
                    {
                        Outputs(output, Variant.FromStructure(recipe.InternalId), Variant.From(0));
                    }
                    return outcome;
                }
            }
            throw NotFound("No recipe is linked to this product.");
        }

        private async ValueTask<MachineVisionOperationResult> LinkProductAsync(
            RecipeIdInternalDataType internalId,
            ProductIdDataType product,
            bool link,
            List<Variant> output,
            CancellationToken ct)
        {
            RequiredId(product.Id);
            MachineVisionDocument document = await FindRecipeAsync(
                new RecipeIdExternalDataType(), internalId, ct).ConfigureAwait(false);
            RecipeRecord recipe = RecipeRecord.From(document).WithProduct(product, link);
            await m_store.WriteAsync(document with { Metadata = recipe.ToVariant() }, ct).ConfigureAwait(false);
            await RefreshFoldersAsync(ct).ConfigureAwait(false);
            Outputs(output, Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> RemoveRecipeAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            RecipeIdExternalDataType external = Structure<RecipeIdExternalDataType>(args, 0);
            MachineVisionDocument document = await FindRecipeAsync(external, new RecipeIdInternalDataType(), ct)
                .ConfigureAwait(false);
            if (RecipeRecord.From(document).Prepared)
            {
                throw InvalidState("A prepared recipe cannot be removed.");
            }
            await m_store.DeleteAsync(document.Kind, document.Id, ct).ConfigureAwait(false);
            await RefreshFoldersAsync(ct).ConfigureAwait(false);
            Outputs(output, Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> GetRecipesAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            RecipeIdExternalDataType external = Structure<RecipeIdExternalDataType>(args, 0);
            ProductIdDataType product = Structure<ProductIdDataType>(args, 1);
            int prepared = args[2].TryGetValue(out TriStateBooleanDataType tri) ? (int)tri : Int32(args, 2);
            if (prepared is < 0 or > 2)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "Unknown prepared-state filter.");
            }
            ArrayOf<MachineVisionDocument> stored = await m_store.ListAsync(MachineVisionDocumentKind.Recipe, ct)
                .ConfigureAwait(false);
            var matches = new List<RecipeIdInternalDataType>();
            foreach (MachineVisionDocument document in stored)
            {
                var recipe = RecipeRecord.From(document);
                if (MatchesIdentity(external, recipe.ExternalId) &&
                    (string.IsNullOrEmpty(product.Id) || recipe.HasProduct(product.Id)) &&
                    (prepared == 2 || recipe.Prepared == (prepared == 1)))
                {
                    matches.Add(recipe.InternalId);
                }
            }
            ArrayOf<RecipeIdInternalDataType> page = Page(matches, UInt32(args, 3), UInt32(args, 4), out bool complete);
            uint handle = Pin(context, MachineVisionDocumentKind.Recipe, Int32(args, 5));
            Outputs(output, Variant.From(complete), Variant.From((uint)page.Count), Variant.From(handle),
                Variant.FromStructure(page), Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> AddConfigurationAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            ConfigurationIdDataType external = CopyId(Structure<ConfigurationIdDataType>(args, 0));
            external.Id = RequiredId(external.Id);
            ArrayOf<MachineVisionDocument> stored = await m_store.ListAsync(MachineVisionDocumentKind.Configuration, ct)
                .ConfigureAwait(false);
            MachineVisionDocument? identical = null;
            foreach (MachineVisionDocument candidate in stored)
            {
                ConfigurationDataType metadata = Metadata<ConfigurationDataType>(candidate);
                if (!candidate.Content.IsNull && SameContent(external, metadata.ExternalId))
                {
                    identical = candidate;
                    break;
                }
            }
            ConfigurationDataType configuration = identical == null
                ? new ConfigurationDataType
                {
                    ExternalId = external,
                    InternalId = new ConfigurationIdDataType { Id = Guid.NewGuid().ToString("N") },
                    LastModified = Now,
                    EncodingMask = (uint)ConfigurationDataTypeFields.ExternalId
                }
                : Metadata<ConfigurationDataType>(identical);
            if (identical == null)
            {
                await m_store.WriteAsync(new MachineVisionDocument(
                    MachineVisionDocumentKind.Configuration, RequiredId(configuration.InternalId.Id),
                    Variant.FromStructure(configuration), default), ct).ConfigureAwait(false);
                await RefreshFoldersAsync(ct).ConfigureAwait(false);
            }
            Outputs(output, Variant.FromStructure(configuration.InternalId),
                Variant.From(DocumentNodeId(MachineVisionDocumentKind.Configuration, configuration.InternalId.Id)),
                Variant.From(identical == null), Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> GetConfigurationAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            MachineVisionDocument document = await RequiredConfigurationAsync(
                Structure<ConfigurationIdDataType>(args, 0), ct).ConfigureAwait(false);
            Outputs(output, Variant.From(Pin(context, document.Kind, Int32(args, 1))),
                document.Metadata, Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> GetConfigurationsAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            ArrayOf<MachineVisionDocument> documents = await m_store.ListAsync(
                MachineVisionDocumentKind.Configuration, ct).ConfigureAwait(false);
            var values = new List<ConfigurationDataType>();
            foreach (MachineVisionDocument document in documents)
            {
                values.Add(Metadata<ConfigurationDataType>(document));
            }
            ArrayOf<ConfigurationDataType> page = Page(values, UInt32(args, 0), UInt32(args, 1), out bool complete);
            Outputs(output, Variant.From(complete), Variant.From((uint)page.Count),
                Variant.From(Pin(context, MachineVisionDocumentKind.Configuration, Int32(args, 2))),
                Variant.FromStructure(page), Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> ActivateConfigurationAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            MachineVisionDocument document = await RequiredConfigurationAsync(
                Structure<ConfigurationIdDataType>(args, 0), ct).ConfigureAwait(false);
            if (document.Content.IsNull)
            {
                throw InvalidState("Configuration content has not been committed.");
            }
            MachineVisionOperationResult outcome = await PersistAndApplyAsync(new MachineVisionDocument(
                MachineVisionDocumentKind.Runtime, "ActiveConfiguration", Variant.From(document.Id), default),
                token => m_provider.ActivateConfigurationAsync(document, token), ct).ConfigureAwait(false);
            if (!outcome.Succeeded)
            {
                return outcome;
            }
            MachineVisionNodes.Set(context, State.ConfigurationManagement!.ActiveConfiguration!,
                document.Metadata, Now);
            await State.ConfigurationManagement.ActiveConfiguration!.ClearChangeMasksAsync(
                context, false, CancellationToken.None)
                .ConfigureAwait(false);
            Outputs(output, Variant.From(0));
            return outcome;
        }

        private async ValueTask<MachineVisionOperationResult> PersistAndApplyAsync(
            MachineVisionDocument requested,
            Func<CancellationToken, ValueTask<MachineVisionOperationResult>> apply,
            CancellationToken ct)
        {
            MachineVisionDocument? previous = await m_store.ReadAsync(requested.Kind, requested.Id, ct)
                .ConfigureAwait(false);
            await m_store.WriteAsync(requested, ct).ConfigureAwait(false);
            MachineVisionOperationResult outcome;
            try
            {
                outcome = await apply(ct).ConfigureAwait(false) ??
                    throw new InvalidOperationException("The provider returned no operation outcome.");
                if (outcome.ServiceResult == null || outcome.Error > 0)
                {
                    throw new InvalidOperationException("The provider returned an invalid operation outcome.");
                }
            }
            catch (Exception failure)
            {
                await RestoreStateAsync(requested, previous, failure).ConfigureAwait(false);
                throw;
            }
            if (!outcome.Succeeded)
            {
                Exception failure = ServiceResult.IsBad(outcome.ServiceResult)
                    ? new ServiceResultException(outcome.ServiceResult)
                    : InvalidState("The provider refused the state change with domain error " + outcome.Error);
                await RestoreStateAsync(requested, previous, failure).ConfigureAwait(false);
            }
            return outcome;
        }

        private async ValueTask RestoreStateAsync(
            MachineVisionDocument requested, MachineVisionDocument? previous, Exception failure)
        {
            try
            {
                if (previous == null)
                {
                    await m_store.DeleteAsync(requested.Kind, requested.Id, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    await m_store.WriteAsync(previous, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception rollback)
            {
                m_statePersistenceFault = true;
                try
                {
                    await EnterErrorAsync(
                        "State persistence rollback failed; repair the store and restart the runtime.",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception reporting)
                {
                    throw new AggregateException("State application, rollback, and error reporting failed.",
                        failure, rollback, reporting);
                }
                throw new AggregateException("State application and persistence rollback failed.", failure, rollback);
            }
        }

        private async ValueTask<MachineVisionOperationResult> RemoveConfigurationAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            MachineVisionDocument document = await RequiredConfigurationAsync(
                Structure<ConfigurationIdDataType>(args, 0), ct).ConfigureAwait(false);
            string id = document.Id;
            MachineVisionDocument? active = await ActiveConfigurationAsync(ct).ConfigureAwait(false);
            if (active?.Id == id)
            {
                throw InvalidState("The active configuration cannot be removed.");
            }
            if (!await m_store.DeleteAsync(MachineVisionDocumentKind.Configuration, id, ct).ConfigureAwait(false))
            {
                throw NotFound("The configuration does not exist.");
            }
            await RefreshFoldersAsync(ct).ConfigureAwait(false);
            Outputs(output, Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> GetResultAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, bool components, CancellationToken ct)
        {
            MachineVisionDocument document = await RequiredDocumentAsync(
                MachineVisionDocumentKind.Result, Structure<ResultIdDataType>(args, 0).Id, ct).ConfigureAwait(false);
            uint handle = Pin(context, document.Kind, Int32(args, 1));
            ResultDataType result = Metadata<ResultDataType>(document);
            if (components)
            {
                Outputs(output, Variant.From(result.HasTransferableDataOnFile), Variant.From(handle),
                    Variant.From(result.IsPartial), Variant.From(result.IsSimulated), Variant.From(result.ResultState),
                    Variant.FromStructure(result.MeasId), Variant.FromStructure(result.PartId),
                    Variant.FromStructure(result.ExternalRecipeId), Variant.FromStructure(result.InternalRecipeId),
                    Variant.FromStructure(result.ProductId), Variant.FromStructure(result.ExternalConfigurationId),
                    Variant.FromStructure(result.InternalConfigurationId), Variant.FromStructure(result.JobId),
                    Variant.From(result.CreationTime), Variant.FromStructure(result.ProcessingTimes),
                    Variant.From(result.ResultContent), Variant.From(0));
            }
            else
            {
                Outputs(output, Variant.From(handle), document.Metadata, Variant.From(0));
            }
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionOperationResult> GetResultsAsync(
            ISystemContext context, ArrayOf<Variant> args, List<Variant> output, CancellationToken ct)
        {
            int state = Int32(args, 0);
            string? meas = Structure<MeasIdDataType>(args, 1).Id;
            string? part = Structure<PartIdDataType>(args, 2).Id;
            RecipeIdExternalDataType externalRecipe = Structure<RecipeIdExternalDataType>(args, 3);
            RecipeIdInternalDataType internalRecipe = Structure<RecipeIdInternalDataType>(args, 4);
            ConfigurationIdDataType externalConfiguration = Structure<ConfigurationIdDataType>(args, 5);
            ConfigurationIdDataType internalConfiguration = Structure<ConfigurationIdDataType>(args, 6);
            string? product = Structure<ProductIdDataType>(args, 7).Id;
            string? job = Structure<JobIdDataType>(args, 8).Id;
            ArrayOf<MachineVisionDocument> documents = await m_store.ListAsync(
                MachineVisionDocumentKind.Result, ct).ConfigureAwait(false);
            var matches = new List<ResultDataType>();
            foreach (MachineVisionDocument document in documents)
            {
                ResultDataType result = Metadata<ResultDataType>(document);
                if ((state == 0 || state == result.ResultState) &&
                    Matches(meas, result.MeasId.Id) &&
                    Matches(part, result.PartId.Id) &&
                    MatchesIdentity(externalRecipe, result.ExternalRecipeId) &&
                    MatchesIdentity(internalRecipe, result.InternalRecipeId) &&
                    MatchesIdentity(externalConfiguration, result.ExternalConfigurationId) &&
                    MatchesIdentity(internalConfiguration, result.InternalConfigurationId) &&
                    Matches(product, result.ProductId.Id) &&
                    Matches(job, result.JobId.Id))
                {
                    matches.Add(result);
                }
            }
            ArrayOf<ResultDataType> page = Page(matches, UInt32(args, 9), UInt32(args, 10), out bool complete);
            Outputs(output, Variant.From(complete), Variant.From((uint)page.Count),
                Variant.From(Pin(context, MachineVisionDocumentKind.Result, Int32(args, 11))),
                Variant.FromStructure(page), Variant.From(0));
            return MachineVisionOperationResult.Good;
        }

        private async ValueTask<MachineVisionDocument> FindRecipeAsync(
            RecipeIdExternalDataType external, RecipeIdInternalDataType internalId, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(internalId.Id))
            {
                MachineVisionDocument found = await RequiredDocumentAsync(
                    MachineVisionDocumentKind.Recipe, internalId.Id, ct)
                    .ConfigureAwait(false);
                return MatchesIdentity(internalId, RecipeRecord.From(found).InternalId)
                    ? found : throw NotFound("The requested internal recipe version no longer exists.");
            }
            string id = RequiredId(external.Id);
            ArrayOf<MachineVisionDocument> documents = await m_store.ListAsync(MachineVisionDocumentKind.Recipe, ct)
                .ConfigureAwait(false);
            foreach (MachineVisionDocument document in documents)
            {
                var recipe = RecipeRecord.From(document);
                if (Matches(id, recipe.ExternalId.Id) && MatchesIdentity(external, recipe.ExternalId))
                {
                    return document;
                }
            }
            throw NotFound("The requested recipe does not exist.");
        }

        private async ValueTask<MachineVisionDocument?> SelectRecipeAsync(
            RecipeIdExternalDataType external, ProductIdDataType product, CancellationToken ct)
        {
            if (!Has(MachineVisionFeatures.Recipes))
            {
                return null;
            }
            ArrayOf<MachineVisionDocument> documents = await m_store.ListAsync(MachineVisionDocumentKind.Recipe, ct)
                .ConfigureAwait(false);
            foreach (MachineVisionDocument document in documents)
            {
                var recipe = RecipeRecord.From(document);
                if (recipe.Prepared &&
                    MatchesIdentity(external, recipe.ExternalId) &&
                    (string.IsNullOrEmpty(product.Id) || recipe.HasProduct(product.Id)))
                {
                    return document;
                }
            }
            if (m_externalReady && string.IsNullOrEmpty(external.Id) && string.IsNullOrEmpty(product.Id))
            {
                return null;
            }
            throw InvalidState("No prepared recipe matches the start request.");
        }

        private async ValueTask<MachineVisionDocument?> ActiveConfigurationAsync(CancellationToken ct)
        {
            MachineVisionDocument? active = await m_store.ReadAsync(
                MachineVisionDocumentKind.Runtime, "ActiveConfiguration", ct).ConfigureAwait(false);
            if (active == null)
            {
                return null;
            }
            if (!active.Metadata.TryGetValue(out string id))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError, "Invalid stored active configuration.");
            }
            return await RequiredDocumentAsync(MachineVisionDocumentKind.Configuration, id, ct).ConfigureAwait(false);
        }

        private async ValueTask UpdateReadyStateAsync(
            CancellationToken ct,
            uint readyTransition = Automatic.TransitionIds.InitializedToReadyAuto,
            uint unreadyTransition = Automatic.TransitionIds.ReadyToInitializedAuto)
        {
            if (ControlState.GetStateId(ControlState.CurrentState!.Id!.Value) != Control.StateIds.Operational)
            {
                return;
            }
            bool ready = m_externalReady;
            ArrayOf<MachineVisionDocument> recipes = await m_store.ListAsync(MachineVisionDocumentKind.Recipe, ct)
                .ConfigureAwait(false);
            foreach (MachineVisionDocument document in recipes)
            {
                ready |= RecipeRecord.From(document).Prepared;
            }
            uint state = AutomaticState.GetStateId(AutomaticState.CurrentState!.Id!.Value);
            if (ready &&
                state == Automatic.StateIds.Initialized &&
                CurrentStepCompletion().Status == TaskStatus.RanToCompletion)
            {
                await TransitionAsync(AutomaticState, readyTransition).ConfigureAwait(false);
            }
            else if (!ready && state == Automatic.StateIds.Ready)
            {
                await TransitionAsync(AutomaticState, unreadyTransition).ConfigureAwait(false);
            }
        }

        private async ValueTask<MachineVisionDocument> RequiredDocumentAsync(
            MachineVisionDocumentKind kind, string? id, CancellationToken ct)
        {
            return await m_store.ReadAsync(kind, RequiredId(id), ct).ConfigureAwait(false)
                ?? throw NotFound("The requested vision document does not exist.");
        }

        private async ValueTask<MachineVisionDocument> RequiredConfigurationAsync(
            ConfigurationIdDataType requested, CancellationToken ct)
        {
            MachineVisionDocument document = await RequiredDocumentAsync(
                MachineVisionDocumentKind.Configuration, requested.Id, ct).ConfigureAwait(false);
            return MatchesIdentity(requested, Metadata<ConfigurationDataType>(document).InternalId)
                ? document : throw NotFound("The requested configuration version no longer exists.");
        }

        private uint Pin(ISystemContext context, MachineVisionDocumentKind kind, int timeout)
        {
            DateTimeOffset now = m_clock.GetUtcNow();
            var expired = new List<uint>();
            foreach (KeyValuePair<uint, MetadataHandle> pair in m_metadataHandles)
            {
                if (pair.Value.Expires <= now)
                {
                    expired.Add(pair.Key);
                }
            }
            foreach (uint handle in expired)
            {
                m_metadataHandles.Remove(handle);
            }
            if (m_metadataHandles.Count >= m_options.MaximumHandles)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTooManyOperations, "Release a vision metadata handle.");
            }
            long next = Interlocked.Increment(ref s_nextMetadataHandle);
            if (next > uint.MaxValue)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadResourceUnavailable, "Metadata handle space exhausted.");
            }
            TimeSpan lifetime = timeout > 0
                ? TimeSpan.FromMilliseconds(Math.Min(timeout, m_options.HandleTimeout.TotalMilliseconds))
                : m_options.HandleTimeout;
            uint id = (uint)next;
            m_metadataHandles.Add(id, new MetadataHandle(
                (context as ISessionSystemContext)?.SessionId ?? NodeId.Null, kind, now + lifetime));
            return id;
        }

        private ValueTask<MachineVisionOperationResult> ReleaseAsync(
            ISystemContext context, MachineVisionDocumentKind kind, uint handle, List<Variant> output)
        {
            NodeId session = (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
            if (!m_metadataHandles.TryGetValue(handle, out MetadataHandle entry) ||
                entry.Kind != kind ||
                entry.Session != session ||
                entry.Expires <= m_clock.GetUtcNow())
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, "Unknown or foreign metadata handle.");
            }
            m_metadataHandles.Remove(handle);
            Outputs(output, Variant.From(0));
            return new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good);
        }

        private static ArrayOf<T> Page<T>(List<T> values, uint maximum, uint start, out bool complete)
        {
            if (maximum != 0 && start % maximum != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, "StartIndex must be a multiple of MaxResults.");
            }
            int offset = (int)Math.Min(start, (uint)values.Count);
            int count = maximum == 0 ? values.Count - offset : (int)Math.Min(maximum, (uint)(values.Count - offset));
            complete = offset + count == values.Count;
            return values.GetRange(offset, count).ToArrayOf();
        }

        private static T Metadata<T>(MachineVisionDocument document) where T : class, IEncodeable
        {
            return document.Metadata.TryGetStructure<T>(out T? value) ? value
                : throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Invalid stored vision metadata.");
        }

        private static T CopyId<T>(T source) where T : BinaryIdBaseDataType
        {
            T copy = CloneStructure(source);
            copy.Id = copy.Id?.Trim();
            copy.Version = copy.Version?.Trim();
            if (!string.IsNullOrEmpty(copy.Version))
            {
                copy.EncodingMask |= (uint)BinaryIdBaseDataTypeFields.Version;
            }
            if (!copy.Hash.IsNull)
            {
                copy.EncodingMask |= (uint)BinaryIdBaseDataTypeFields.Hash;
            }
            if (!string.IsNullOrEmpty(copy.HashAlgorithm))
            {
                copy.EncodingMask |= (uint)BinaryIdBaseDataTypeFields.HashAlgorithm;
            }
            if (!copy.Description.IsNull)
            {
                copy.EncodingMask |= (uint)BinaryIdBaseDataTypeFields.Description;
            }
            return copy;
        }

        private static T CloneStructure<T>(T source) where T : class, IEncodeable
        {
            return CoreUtils.Clone(source) ??
                throw new InvalidOperationException("The structure clone unexpectedly returned null.");
        }

        private static string RequiredId(string? id)
        {
            return string.IsNullOrWhiteSpace(id)
                ? throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, "A non-empty identifier is required.")
                : id.Trim();
        }

        private static bool Matches(string? filter, string? value)
        {
            return string.IsNullOrWhiteSpace(filter) || string.Equals(filter.Trim(), value, StringComparison.Ordinal);
        }

        private static bool SameContent(BinaryIdBaseDataType requested, BinaryIdBaseDataType stored)
        {
            return requested.Id == stored.Id &&
                requested.Version == stored.Version &&
                !requested.Hash.IsNull &&
                !requested.Hash.Span.IsEmpty &&
                requested.Hash.Span.SequenceEqual(stored.Hash.Span) &&
                NormalizeHashAlgorithm(requested.HashAlgorithm) == NormalizeHashAlgorithm(stored.HashAlgorithm);
        }

        private static bool MatchesIdentity(BinaryIdBaseDataType filter, BinaryIdBaseDataType value)
        {
            return Matches(filter.Id, value.Id) &&
                Matches(filter.Version, value.Version) &&
                (string.IsNullOrWhiteSpace(filter.HashAlgorithm) ||
                    NormalizeHashAlgorithm(filter.HashAlgorithm) == NormalizeHashAlgorithm(value.HashAlgorithm)) &&
                (filter.Hash.IsNull ||
                    filter.Hash.Span.IsEmpty ||
                    (filter.Hash.Span.SequenceEqual(value.Hash.Span) &&
                        NormalizeHashAlgorithm(filter.HashAlgorithm) == NormalizeHashAlgorithm(value.HashAlgorithm)));
        }

        private static void EnsureProviderSucceeded(MachineVisionOperationResult outcome)
        {
            if (!outcome.Succeeded)
            {
                throw ServiceResult.IsBad(outcome.ServiceResult)
                    ? new ServiceResultException(outcome.ServiceResult)
                    : InvalidState("The provider returned domain error " + outcome.Error);
            }
        }

        private static ServiceResultException NotFound(string message)
        {
            return ServiceResultException.Create(StatusCodes.BadNotFound, "{0}", message);
        }

        private readonly Dictionary<uint, MetadataHandle> m_metadataHandles = [];
        private static long s_nextMetadataHandle;

        private readonly record struct MetadataHandle(
            NodeId Session, MachineVisionDocumentKind Kind, DateTimeOffset Expires);
    }
}
