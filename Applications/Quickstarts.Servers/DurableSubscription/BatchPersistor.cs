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
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Opc.Ua;
using System.Linq;

namespace Quickstarts.Servers
{
    /// <summary>
    /// Persists batches of queue values to disk
    /// </summary>
    public interface IBatchPersistor
    {
        /// <summary>
        /// Request that a batch shall be persisted in a background thread
        /// </summary>
        /// <param name="batch"></param>
        void RequestBatchPersist(BatchBase batch);
        /// <summary>
        /// Persist a batch in the main thread
        /// </summary>
        /// <param name="batch"></param>
        public void PersistSynchronously(BatchBase batch);
        /// <summary>
        /// Request that a batch shall be restored in a background thread
        /// </summary>
        /// <param name="batch"></param>
        void RequestBatchRestore(BatchBase batch);

        /// <summary>
        /// Restore a batch in the main thread
        /// </summary>
        /// <param name="batch"></param>
        public void RestoreSynchronously(BatchBase batch);
        /// <summary>
        /// Delete all batches from disk for a monitored item
        /// </summary>
        /// <param name="batchesToKeep">MonitoredItemIds of the batches to keep on disk</param>
        void DeleteBatches(IEnumerable<uint> batchesToKeep);

        /// <summary>
        /// Delete a single batch from disk
        /// </summary>
        /// <param name="batchToRemove">The Batch to remove</param>
        void DeleteBatch(BatchBase batchToRemove);
    }
    public class BatchPersistor : IBatchPersistor
    {
        private static readonly JsonSerializerSettings s_settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
        private static readonly string s_storage_path = Path.Combine(Environment.CurrentDirectory, "Durable Subscriptions", "Batches");
        private static readonly string s_baseFilename = "_batch.txt";

        #region IBatchPersistor Members
        /// <inheritdoc/>
        public void RequestBatchPersist(BatchBase batch)
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

        /// <inheritdoc/>
        public void RequestBatchRestore(BatchBase batch)
        {
            if (!batch.IsPersisted || batch.RestoreInProgress || batch.PersistingInProgress)
            {
                return;
            }

            batch.RestoreInProgress = true;

            if (m_batchesToRestore.TryAdd(batch.Id, batch))
            {
                _ = Task.Run(() => RestoreSynchronously(batch));
            }
        }

        /// <inheritdoc/>
        public void RestoreSynchronously(BatchBase batch)
        {
            string filePath = Path.Combine(s_storage_path, $"{batch.MonitoredItemId}_{batch.Id}{s_baseFilename}");
            object result = null;
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    result = JsonConvert.DeserializeObject(json, batch.GetType(), s_settings);

                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Utils.LogError(ex, "Failed to restore batch");

                batch.RestoreInProgress = false;
                m_batchesToRestore.TryRemove(batch.Id, out _);

                return;
            }

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

        /// <inheritdoc/>
        public void PersistSynchronously(BatchBase batch)
        {
            try
            {
                string result = JsonConvert.SerializeObject(batch, s_settings);

                if (!Directory.Exists(s_storage_path))
                {
                    Directory.CreateDirectory(s_storage_path);
                }

                string filePath = Path.Combine(s_storage_path, $"{batch.MonitoredItemId}_{batch.Id}{s_baseFilename}");

                File.WriteAllText(filePath, result);
            }
            catch (Exception ex)
            {
                Opc.Ua.Utils.LogWarning(ex, "Failed to store batch");

                batch.PersistingInProgress = false;
                m_batchesToPersist.TryRemove(batch.Id, out _);

                return;
            }

            batch.SetPersisted();
            m_batchesToPersist.TryRemove(batch.Id, out _);
        }
        #endregion

        /// <inheritdoc/>
        public void DeleteBatches(IEnumerable<uint> batchesToKeep)
        {
            try
            {
                if (Directory.Exists(s_storage_path))
                {
                    var directory = new DirectoryInfo(s_storage_path);

                    // Create a single regex pattern that matches any of the batches to keep
                    var pattern = string.Join("|", batchesToKeep.Select(batch => $@"{batch}_.*{s_baseFilename}$"));
                    var regex = new Regex(pattern, RegexOptions.Compiled);

                    foreach (var file in directory.GetFiles())
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
                Opc.Ua.Utils.LogWarning(ex, "Failed to clean up batches");
            }
            
        }

        public void DeleteBatch(BatchBase batchToRemove)
        {
            try
            {
                if (Directory.Exists(s_storage_path))
                {
                    var directory = new DirectoryInfo(s_storage_path);
                    var regex = new Regex($@"{batchToRemove.MonitoredItemId}_.{batchToRemove.Id}._{s_baseFilename}$", RegexOptions.Compiled);

                    foreach (var file in directory.GetFiles())
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
                Opc.Ua.Utils.LogWarning(ex, "Failed to clean up single batch");
            }
            
        }

        private readonly ConcurrentDictionary<Guid, BatchBase> m_batchesToRestore = new ConcurrentDictionary<Guid, BatchBase>();
        private readonly ConcurrentDictionary<Guid, BatchBase> m_batchesToPersist = new ConcurrentDictionary<Guid, BatchBase>();
    }
}
