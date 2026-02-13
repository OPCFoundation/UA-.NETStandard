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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Opc.Ua;

namespace Quickstarts.Servers
{
    /// <inheritdoc/>
    public class BatchPersistor : IBatchPersistor
    {
        private static readonly JsonSerializerOptions s_settings = new()
        {
            // TypeInfoResolver = DefaultJsonTypeInfoResolver.Instance,
        };

        private static readonly string s_storage_path = Path.Combine(
            Environment.CurrentDirectory,
            "Durable Subscriptions",
            "Batches");

        private const string kBaseFilename = "_batch.txt";

        public BatchPersistor(ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<DurableDataChangeMonitoredItemQueue>();
            m_telemetry = telemetry;
        }

        /// <inheritdoc/>
        public void RequestBatchPersist(BatchBase batch)
        {
            lock (batch)
            {
                if (batch.IsPersisted || batch.PersistingInProgress || batch.RestoreInProgress)
                {
                    return;
                }
                batch.PersistingInProgress = true;

                if (m_batchesToPersist.TryAdd(batch.Id, batch))
                {
                    _ = Task.Run(() => PersistSynchronously(batch));
                }
            }
        }

        /// <inheritdoc/>
        public void RequestBatchRestore(BatchBase batch)
        {
            lock (batch)
            {
                if (!batch.IsPersisted || batch.RestoreInProgress || batch.PersistingInProgress)
                {
                    if (batch.PersistingInProgress)
                    {
                        batch.CancelBatchPersist?.Cancel();
                    }
                    return;
                }

                batch.RestoreInProgress = true;

                if (m_batchesToRestore.TryAdd(batch.Id, batch))
                {
                    _ = Task.Run(() => RestoreSynchronously(batch));
                }
            }
        }

        /// <inheritdoc/>
        public void RestoreSynchronously(BatchBase batch)
        {
            string filePath = Path.Combine(
                s_storage_path,
                $"{batch.MonitoredItemId}_{batch.Id}{kBaseFilename}");
            object result = null;
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
                    result = JsonSerializer.Deserialize(json, batch.GetType(), s_settings);

                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to restore batch");

                batch.RestoreInProgress = false;
                m_batchesToRestore.TryRemove(batch.Id, out _);

                return;
            }
            lock (batch)
            {
                if (batch is DataChangeBatch dataChangeBatch)
                {
                    var newBatch = result as DataChangeBatch;
                    dataChangeBatch.Restore(newBatch.Values);
                }
                else if (batch is EventBatch eventBatch)
                {
                    var newBatch = result as EventBatch;
                    eventBatch.Restore(newBatch.Events);
                }
                m_batchesToRestore.TryRemove(batch.Id, out _);
            }
        }

        /// <inheritdoc/>
        public void PersistSynchronously(BatchBase batch)
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            batch.CancelBatchPersist = cancellationTokenSource;
            try
            {
                using IDisposable scope = AmbientMessageContext.SetScopedContext(m_telemetry);
                string result = JsonSerializer.Serialize(batch, s_settings);

                if (!Directory.Exists(s_storage_path))
                {
                    Directory.CreateDirectory(s_storage_path);
                }

                string filePath = Path.Combine(
                    s_storage_path,
                    $"{batch.MonitoredItemId}_{batch.Id}{kBaseFilename}");

                File.WriteAllText(filePath, result);

                if (cancellationTokenSource.IsCancellationRequested)
                {
                    File.Delete(filePath);
                    lock (batch)
                    {
                        batch.PersistingInProgress = false;
                        batch.CancelBatchPersist = null;
                    }
                }
                else
                {
                    lock (batch)
                    {
                        batch.SetPersisted();
                    }
                }
                m_batchesToPersist.TryRemove(batch.Id, out _);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to store batch");
                lock (batch)
                {
                    batch.PersistingInProgress = false;
                    m_batchesToPersist.TryRemove(batch.Id, out _);
                    batch.CancelBatchPersist = null;
                }
            }
        }

        /// <inheritdoc/>
        public void DeleteBatches(IEnumerable<uint> batchesToKeep)
        {
            try
            {
                if (Directory.Exists(s_storage_path))
                {
                    var directory = new DirectoryInfo(s_storage_path);

                    // Create a single regex pattern that matches any of the batches to keep
                    string pattern = string.Join(
                        "|",
                        batchesToKeep.Select(batch => $"{batch}_.*{kBaseFilename}$"));
                    var regex = new Regex(pattern, RegexOptions.Compiled);

                    foreach (FileInfo file in directory.GetFiles())
                    {
                        if (!regex.IsMatch(file.Name))
                        {
                            file.Delete();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to clean up batches");
            }
        }

        public void DeleteBatch(BatchBase batchToRemove)
        {
            try
            {
                if (Directory.Exists(s_storage_path))
                {
                    var directory = new DirectoryInfo(s_storage_path);
                    var regex = new Regex(
                        $"{batchToRemove.MonitoredItemId}_.{batchToRemove.Id}._{kBaseFilename}$",
                        RegexOptions.Compiled);

                    foreach (FileInfo file in directory.GetFiles())
                    {
                        if (!regex.IsMatch(file.Name))
                        {
                            file.Delete();
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to clean up single batch");
            }
        }

        private readonly ConcurrentDictionary<Uuid, BatchBase> m_batchesToRestore = new();
        private readonly ConcurrentDictionary<Uuid, BatchBase> m_batchesToPersist = new();
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
    }
}
