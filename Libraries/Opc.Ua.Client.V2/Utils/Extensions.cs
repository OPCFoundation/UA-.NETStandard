// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua
{
    using System.Collections.Generic;

    /// <summary>
    /// Internal Extensions
    /// </summary>
    internal static class Extensions
    {
        /// <summary>
        /// Returns batches of a list for processing.
        /// </summary>
        /// <remarks>
        /// Returns the original list if batchsize is 0 or the
        /// collection count is smaller than the batch size.
        /// </remarks>
        /// <typeparam name="T">The type of the items in the
        /// list.</typeparam>
        /// <param name="collection">The list from which items are
        /// batched.</param>
        /// <param name="batchSize">The size of a batch.</param>
        /// <returns>The batched lists.</returns>
        public static IEnumerable<List<T>> Batch<T>(this List<T> collection,
            uint batchSize)
        {
            if (collection.Count < batchSize || batchSize == 0)
            {
                yield return collection;
            }
            else
            {
                var nextbatch = new List<T>((int)batchSize);
                foreach (var item in collection)
                {
                    nextbatch.Add(item);
                    if (nextbatch.Count == batchSize)
                    {
                        yield return nextbatch;
                        nextbatch = new List<T>((int)batchSize);
                    }
                }
                if (nextbatch.Count > 0)
                {
                    yield return nextbatch;
                }
            }
        }
    }
}
