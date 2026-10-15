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
using static Opc.Ua.PlasticsRubber.Server.ProductionDatasetValidation;

namespace Opc.Ua.PlasticsRubber.Server
{
    public sealed partial class ProductionDatasetBinding
    {
        /// <summary>
        /// Gets the injected clock's current UTC timestamp.
        /// </summary>
        private DateTimeUtc Now => (DateTimeUtc)m_clock.GetUtcNow().UtcDateTime;

        /// <summary>
        /// Installs typed control-layer methods and authorized value access.
        /// </summary>
        private void BindStatus(ProductionDatasetStatusState status, StorageEnumeration storage)
        {
            BindStatusRead(status.Information!, status, storage,
                value => Variant.FromStructure(Copy(value.Information)));
            if (status.Modified != null)
            {
                BindStatusRead(status.Modified, status, storage, value => Variant.From(value.Modified));
            }
            if (status.Frozen != null)
            {
                BaseVariableState frozen = status.Frozen;
                BindStatusRead(frozen, status, storage, value => Variant.From(value.Frozen));
                if (frozen.OnWriteValue != null || frozen.OnWriteValueAsync != null ||
                    frozen.OnSimpleWriteValue != null || frozen.OnSimpleWriteValueAsync != null)
                {
                    throw new InvalidOperationException("Frozen already has a write handler.");
                }
                frozen.OnWriteValueAsync = async (context, _, range, value, ct) =>
                {
                    ServiceResult result = await InvokeAsync(
                        context, status.NodeId, status, frozen, async (request, token) =>
                        {
                            if (!range.IsNull)
                            {
                                throw Error(StatusCodes.BadIndexRangeInvalid, "Frozen is a scalar.");
                            }
                            if (!value.TryGetValue(out bool requested))
                            {
                                throw Error(StatusCodes.BadTypeMismatch, "Frozen requires a Boolean.");
                            }
                            ProductionDatasetStatus committed = await m_provider.SetFrozenAsync(
                                request, storage, requested, token).ConfigureAwait(false);
                            ValidateStatus(committed);
                            if (committed.Frozen != requested)
                            {
                                throw Error(StatusCodes.BadInvalidState, "The authority did not commit Frozen.");
                            }
                            PublishStatus(storage, committed);
                        }, ct, publish: true).ConfigureAwait(false);
                    return new AttributeWriteResult(result);
                };
                frozen.AccessLevel |= AccessLevels.CurrentWrite;
                frozen.UserAccessLevel |= AccessLevels.CurrentWrite;
            }
            if (status.Load != null)
            {
                LoadMethodState load = status.Load;
                Own(load, load.OnCall != null || load.OnCallAsync != null);
                load.OnCallAsync = async (context, _, target, name, components, ct) =>
                    new LoadMethodStateResult
                    {
                        ServiceResult = await InvokeAsync(
                            context, target, status, load, async (request, token) =>
                            {
                                Text(name, m_options, true);
                                Components(components, m_options);
                                await EnsureMutableAsync(request, storage, token).ConfigureAwait(false);
                                ProductionDatasetStatus committed = await m_provider.LoadAsync(
                                    request, storage, name, components, token).ConfigureAwait(false);
                                ValidateStatus(committed);
                                PublishStatus(storage, committed);
                            }, ct, publish: true).ConfigureAwait(false)
                    };
            }
            if (status.Save != null)
            {
                SaveMethodState save = status.Save;
                Own(save, save.OnCall != null || save.OnCallAsync != null);
                save.OnCallAsync = async (context, _, target, name, ct) =>
                    new SaveMethodStateResult
                    {
                        ServiceResult = await InvokeAsync(
                            context, target, status, save, async (request, token) =>
                            {
                                Text(name, m_options, true);
                                await EnsureMutableAsync(request, storage, token).ConfigureAwait(false);
                                ProductionDatasetStatus committed = await m_provider.SaveAsync(
                                    request, storage, name, token).ConfigureAwait(false);
                                ValidateStatus(committed);
                                PublishStatus(storage, committed);
                            }, ct, publish: true).ConfigureAwait(false)
                    };
            }
        }

        /// <summary>
        /// Reads actual device state rather than exposing stale initialized or optimistically written values.
        /// </summary>
        private void BindStatusRead(
            BaseVariableState variable,
            ProductionDatasetStatusState status,
            StorageEnumeration storage,
            Func<ProductionDatasetStatus, Variant> select)
        {
            if (variable.OnReadValue != null || variable.OnReadValueAsync != null ||
                variable.OnSimpleReadValue != null || variable.OnSimpleReadValueAsync != null)
            {
                throw new InvalidOperationException("A dataset status property already has a read handler.");
            }
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.OnReadValueAsync = async (context, _, range, encoding, ct) =>
            {
                Variant value = Variant.Null;
                ServiceResult result = await InvokeAsync(
                    context, status.NodeId, status, variable, async (request, token) =>
                    {
                        if (!range.IsNull)
                        {
                            throw Error(StatusCodes.BadIndexRangeInvalid, "Dataset status properties are scalar.");
                        }
                        if (!encoding.IsNull)
                        {
                            throw Error(
                                StatusCodes.BadDataEncodingUnsupported,
                                    "Dataset status has no alternate data encoding.");
                        }
                        ProductionDatasetStatus snapshot = await ReadAndPublishAsync(request, storage, token)
                            .ConfigureAwait(false);
                        value = select(snapshot);
                    }, ct).ConfigureAwait(false);
                return new AttributeReadResult(result, value, result.StatusCode, variable.Timestamp);
            };
            m_variables.Add(variable);
        }

        /// <summary>
        /// Binds generated list and handle-scoped description methods.
        /// </summary>
        private void BindListsAndInformation()
        {
            if (Node.ProductionDatasetLists is { } lists)
            {
                GetProductionDatasetListMethodState get = lists.GetProductionDatasetList!;
                Own(get, get.OnCall != null || get.OnCallAsync != null);
                get.OnCallAsync = async (context, _, target, nameFilter, mouldId, ct) =>
                {
                    ArrayOf<ProductionDatasetInformationType> information = [];
                    ServiceResult result = await InvokeAsync(
                        context, target, lists, get, async (request, token) =>
                        {
                            Text(nameFilter, m_options);
                            Text(mouldId, m_options);
                            ArrayOf<ProductionDatasetInformationType> candidates = CopyList(
                                await m_provider.GetListAsync(request, nameFilter, mouldId,
                                    token).ConfigureAwait(false));
                            var selected = new List<ProductionDatasetInformationType>();
                            foreach (ProductionDatasetInformationType candidate in candidates)
                            {
                                token.ThrowIfCancellationRequested();
                                if (MatchesName(candidate.Name ?? string.Empty, nameFilter) &&
                                    (string.IsNullOrEmpty(mouldId) ||
                                        string.Equals(candidate.MouldId, mouldId, StringComparison.Ordinal)))
                                {
                                    selected.Add(candidate);
                                }
                            }
                            information = selected.ToArray().ToArrayOf();
                        }, ct).ConfigureAwait(false);
                    return new GetProductionDatasetListMethodStateResult
                    {
                        ServiceResult = result,
                        ProductionDatasetList = information
                    };
                };
                SendProductionDatasetListMethodState send = lists.SendProductionDatasetList!;
                Own(send, send.OnCall != null || send.OnCallAsync != null);
                send.OnCallAsync = async (context, _, target, information, ct) =>
                    new SendProductionDatasetListMethodStateResult
                    {
                        ServiceResult = await InvokeAsync(
                            context, target, lists, send,
                            (request, token) => m_provider.ReceiveListAsync(request, CopyList(information), token),
                            ct).ConfigureAwait(false)
                    };
                lists.EventNotifier |= EventNotifiers.SubscribeToEvents;
                Node.AddNotifier(m_owner.SystemContext, ReferenceTypeIds.HasEventSource, false, lists);
                lists.AddNotifier(m_owner.SystemContext, ReferenceTypeIds.HasEventSource, true, Node);
            }
            if (Node.GetProductionDatasetInformation is { } getInformation)
            {
                Own(getInformation, getInformation.OnCall != null || getInformation.OnCallAsync != null);
                getInformation.OnCallAsync = async (context, _, target, handle, ct) =>
                {
                    var information = new ProductionDatasetInformationType();
                    ServiceResult result = await InvokeAsync(
                        context, target, Node, getInformation, async (request, _) =>
                        {
                            ProductionDatasetTransferSlot slot = await RequireTransferAsync(
                                request.SystemContext, handle).ConfigureAwait(false);
                            if (slot.WriteOptions != null)
                            {
                                throw Error(StatusCodes.BadInvalidArgument, "Information requires a read handle.");
                            }
                            information = Copy(slot.Information!);
                        }, ct).ConfigureAwait(false);
                    return new GetProductionDatasetInformationMethodStateResult
                    {
                        ServiceResult = result,
                        Information = information
                    };
                };
            }
            if (Node.SendProductionDatasetInformation is { } sendInformation)
            {
                Own(sendInformation, sendInformation.OnCall != null || sendInformation.OnCallAsync != null);
                sendInformation.OnCallAsync = async (context, _, target, handle, information, ct) =>
                    new SendProductionDatasetInformationMethodStateResult
                    {
                        ServiceResult = await InvokeAsync(
                            context, target, Node, sendInformation, async (request, token) =>
                            {
                                ProductionDatasetTransferSlot slot = await RequireTransferAsync(
                                    request.SystemContext, handle).ConfigureAwait(false);
                                if (slot.WriteOptions == null)
                                {
                                    throw Error(StatusCodes.BadInvalidArgument, "Information requires a write handle.");
                                }
                                Information(information, m_options);
                                if (!string.Equals(information.Name, slot.WriteOptions.Name, StringComparison.Ordinal))
                                {
                                    throw Error(StatusCodes.BadInvalidArgument, "Information names another dataset.");
                                }
                                await EnsureMutableAsync(request, slot.WriteOptions.Storage, token)
                                    .ConfigureAwait(false);
                                slot.Information = Copy(information);
                            }, ct).ConfigureAwait(false)
                    };
            }
        }

        /// <summary>
        /// Validates and detaches every list entry, retaining ordering and repeated component codes.
        /// </summary>
        private ArrayOf<ProductionDatasetInformationType> CopyList(
            ArrayOf<ProductionDatasetInformationType> information)
        {
            if (information.Count > m_options.MaximumDatasetCount)
            {
                throw Error(StatusCodes.BadEncodingLimitsExceeded, "The dataset list limit was exceeded.");
            }
            var copies = new ProductionDatasetInformationType[information.Count];
            for (int i = 0; i < information.Count; i++)
            {
                Information(information[i], m_options);
                Text(information[i].Name, m_options, true);
                copies[i] = Copy(information[i]);
            }
            return copies.ToArrayOf();
        }

        /// <summary>
        /// Matches only the chapter's two metacharacters, without regex or recursive wildcard expansion.
        /// </summary>
        private static bool MatchesName(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }
            int position = 0;
            int token = 0;
            int star = -1;
            int retry = 0;
            while (position < text.Length)
            {
                if (token < pattern.Length && (pattern[token] == '?' || pattern[token] == text[position]))
                {
                    position++;
                    token++;
                }
                else if (token < pattern.Length && pattern[token] == '*')
                {
                    star = token++;
                    retry = position;
                }
                else if (star >= 0)
                {
                    token = star + 1;
                    position = ++retry;
                }
                else
                {
                    return false;
                }
            }
            while (token < pattern.Length && pattern[token] == '*')
            {
                token++;
            }
            return token == pattern.Length;
        }

        /// <summary>
        /// Validates a provider response before any UA status fields are changed.
        /// </summary>
        private void ValidateStatus(ProductionDatasetStatus status)
        {
            if (status == null)
            {
                throw Error(StatusCodes.BadUnexpectedError, "The authority did not return a dataset status.");
            }
            Information(status.Information, m_options);
        }

        /// <summary>
        /// Checks every selected control layer before a mutation; the provider must recheck atomically.
        /// </summary>
        private async ValueTask EnsureMutableAsync(
            PlasticsRubberRequestContext context, StorageEnumeration destinations, CancellationToken ct)
        {
            Preparation(destinations, m_options);
            if ((destinations & StorageEnumeration.PRODUCTION) != 0)
            {
                ProductionDatasetStatus status = await ReadAndPublishAsync(context, StorageEnumeration.PRODUCTION, ct)
                    .ConfigureAwait(false);
                if (status.Frozen)
                {
                    throw Error(StatusCodes.BadInvalidState, "The active production dataset is frozen.");
                }
            }
            if ((destinations & StorageEnumeration.PREPARATION) != 0)
            {
                ProductionDatasetStatus status = await ReadAndPublishAsync(context, StorageEnumeration.PREPARATION, ct)
                    .ConfigureAwait(false);
                if (status.Frozen)
                {
                    throw Error(StatusCodes.BadInvalidState, "The production dataset in preparation is frozen.");
                }
            }
        }

        /// <summary>
        /// Fetches one current status using the caller of the operation that requires it.
        /// </summary>
        private async ValueTask<ProductionDatasetStatus> ReadAndPublishAsync(
            PlasticsRubberRequestContext context, StorageEnumeration storage, CancellationToken ct)
        {
            ProductionDatasetStatus status = await m_provider.ReadStatusAsync(context, storage, ct)
                .ConfigureAwait(false);
            ValidateStatus(status);
            PublishStatus(storage, status);
            return status;
        }

        /// <summary>
        /// Publishes only validated, authoritative snapshots.
        /// </summary>
        private void PublishStatus(StorageEnumeration storage, ProductionDatasetStatus status)
        {
            ProductionDatasetStatusState node = storage == StorageEnumeration.PRODUCTION
                ? Node.ActiveProductionDatasetStatus! : Node.ProductionDatasetInPreparationStatus!;
            if (node.Information!.StatusCode != StatusCodes.Good ||
                !node.Information.Value.IsEqual(status.Information))
            {
                node.Information.Value = Copy(status.Information);
                node.Information.StatusCode = StatusCodes.Good;
                node.Information.Timestamp = Now;
            }
            if (node.Modified != null &&
                (node.Modified.StatusCode != StatusCodes.Good || node.Modified.Value != status.Modified))
            {
                node.Modified.Value = status.Modified;
                node.Modified.StatusCode = StatusCodes.Good;
                node.Modified.Timestamp = Now;
            }
            if (node.Frozen != null &&
                (node.Frozen.StatusCode != StatusCodes.Good || node.Frozen.Value != status.Frozen))
            {
                node.Frozen.Value = status.Frozen;
                node.Frozen.StatusCode = StatusCodes.Good;
                node.Frozen.Timestamp = Now;
            }
        }
    }
}
