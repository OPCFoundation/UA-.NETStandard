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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// A bounded test-only storage authority, not a production or implicit package fallback.
    /// </summary>
    internal sealed class DatasetProvider : IProductionDatasetProvider
    {
        /// <inheritdoc/>
        public ValueTask<ProductionDatasetStatus> ReadStatusAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                return new ValueTask<ProductionDatasetStatus>(storage == StorageEnumeration.PRODUCTION
                    ? m_active : m_prepared);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetDocument> ReadAsync(
            PlasticsRubberRequestContext context,
            ProductionDatasetReadOptionsType options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                string? name = options.Storage switch
                {
                    StorageEnumeration.PRODUCTION => m_active.Information.Name,
                    StorageEnumeration.PREPARATION => m_prepared.Information.Name,
                    _ => options.Name
                };
                return name != null && m_files.TryGetValue(name, out ProductionDatasetDocument? document)
                    ? new ValueTask<ProductionDatasetDocument>(document)
                    : throw new ServiceResultException(StatusCodes.BadNotFound);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetStatus> LoadAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            string name,
            ArrayOf<ushort> components,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                RequireUnfrozen(storage);
                if (!m_files.TryGetValue(name, out ProductionDatasetDocument? document))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                }
                var status = new ProductionDatasetStatus { Information = document.Information };
                if (storage == StorageEnumeration.PRODUCTION)
                {
                    m_active = status;
                }
                else
                {
                    m_prepared = status;
                }
                return new ValueTask<ProductionDatasetStatus>(status);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetStatus> SaveAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            string name,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                ProductionDatasetStatus current = storage == StorageEnumeration.PRODUCTION ? m_active : m_prepared;
                if (current.Information.Name is not string currentName ||
                    !m_files.TryGetValue(currentName, out ProductionDatasetDocument? document))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                }
                m_files[name] = new ProductionDatasetDocument
                {
                    Content = document.Content,
                    Information = Information(name)
                };
                return new ValueTask<ProductionDatasetStatus>(current);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetStatus> SetFrozenAsync(
            PlasticsRubberRequestContext context,
            StorageEnumeration storage,
            bool frozen,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                ProductionDatasetStatus status = (storage == StorageEnumeration.PRODUCTION ? m_active : m_prepared)
                    with { Frozen = frozen };
                if (storage == StorageEnumeration.PRODUCTION)
                {
                    m_active = status;
                }
                else
                {
                    m_prepared = status;
                }
                return new ValueTask<ProductionDatasetStatus>(status);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ProductionDatasetCommitResult> CommitAsync(
            PlasticsRubberRequestContext context,
            ProductionDatasetWriteOptionsType options,
            ByteString content,
            ProductionDatasetInformationType? information,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                RequireUnfrozen(options.Storage);
                if (information == null || content.Length > 1024 || m_files.Count >= 8)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                string name = (options.Storage & StorageEnumeration.FILE_SYSTEM) != 0
                    ? options.Name ?? throw new ServiceResultException(StatusCodes.BadInvalidArgument)
                    : string.Empty;
                var document = new ProductionDatasetDocument { Content = content, Information = information };
                ProductionDatasetStatus? active = null;
                ProductionDatasetStatus? prepared = null;
                if ((options.Storage & StorageEnumeration.PRODUCTION) != 0)
                {
                    m_active = active = new ProductionDatasetStatus { Information = information };
                }
                if ((options.Storage & StorageEnumeration.PREPARATION) != 0)
                {
                    m_prepared = prepared = new ProductionDatasetStatus { Information = information };
                }
                if ((options.Storage & StorageEnumeration.FILE_SYSTEM) != 0)
                {
                    m_files[name] = document;
                }
                return new ValueTask<ProductionDatasetCommitResult>(new ProductionDatasetCommitResult
                {
                    Active = active,
                    Preparation = prepared
                });
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ProductionDatasetInformationType>> GetListAsync(
            PlasticsRubberRequestContext context,
            string nameFilter,
            string mouldId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                return new ValueTask<ArrayOf<ProductionDatasetInformationType>>(
                    m_files.Values.Select(document => document.Information).ToArrayOf());
            }
        }

        /// <inheritdoc/>
        public ValueTask ReceiveListAsync(
            PlasticsRubberRequestContext context,
            ArrayOf<ProductionDatasetInformationType> information,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        /// <summary>
        /// Supplies typed metadata with repeated component codes retained exactly.
        /// </summary>
        internal static ProductionDatasetInformationType Information(string name)
        {
            return new ProductionDatasetInformationType
            {
                Name = name,
                Description = "Test dataset",
                MESId = string.Empty,
                CreationTimestamp = ExtrusionFixture.Timestamp,
                LastModificationTimestamp = ExtrusionFixture.Timestamp,
                LastSaveTimestamp = ExtrusionFixture.Timestamp,
                UserName = "Test",
                Components = [1, 1],
                Manufacturer = "Test",
                SerialNumber = "Dataset",
                Model = "Model",
                ControllerName = "Controller",
                UserMachineName = "Machine",
                LocationName = string.Empty,
                ProductName = ["Product"],
                MouldId = string.Empty
            };
        }

        /// <summary>
        /// Checks all selected destinations before changing any, just as an actual authority must.
        /// </summary>
        private void RequireUnfrozen(StorageEnumeration storage)
        {
            if (((storage & StorageEnumeration.PRODUCTION) != 0 && m_active.Frozen) ||
                ((storage & StorageEnumeration.PREPARATION) != 0 && m_prepared.Frozen))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
        }

        /// <summary>The explicitly bounded test document store.</summary>
        private readonly Dictionary<string, ProductionDatasetDocument> m_files = new();

        /// <summary>Protects atomic multi-destination commits.</summary>
        private readonly Lock m_sync = new();

        /// <summary>The authoritative active control-layer state.</summary>
        private ProductionDatasetStatus m_active = new() { Information = Information("baseline") };

        /// <summary>The independent preparation control-layer state.</summary>
        private ProductionDatasetStatus m_prepared = new() { Information = Information("prepared") };
    }
}
