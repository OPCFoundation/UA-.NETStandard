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
        /// Returns batches of a collection for processing.
        /// </summary>
        /// <remarks>
        /// Returns the original collection if batchsize is 0 or the
        /// collection count is smaller than the batch size.
        /// </remarks>
        /// <typeparam name="TElement">The type of the items in the
        /// collection. </typeparam>
        /// <typeparam name="TCollection">The type of the items in the
        /// collection.</typeparam>
        /// <param name="collection">The collection from which items are
        /// batched.</param>
        /// <param name="batchSize">The size of a batch.</param>
        /// <returns>The collection.</returns>
        public static IEnumerable<TCollection> Batch<TElement, TCollection>(
            this TCollection collection, uint batchSize)
            where TCollection : List<TElement>, new()
        {
            if (collection.Count < batchSize || batchSize == 0)
            {
                yield return collection;
            }
            else
            {
                var nextbatch = new TCollection
                {
                    Capacity = (int)batchSize
                };
                foreach (var item in collection)
                {
                    nextbatch.Add(item);
                    if (nextbatch.Count == batchSize)
                    {
                        yield return nextbatch;
                        nextbatch = new TCollection
                        {
                            Capacity = (int)batchSize
                        };
                    }
                }
                if (nextbatch.Count > 0)
                {
                    yield return nextbatch;
                }
            }
        }

        /// <summary>
        /// Helper to batch a list. Same as above but for List type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="batchSize"></param>
        /// <returns></returns>
        public static IEnumerable<List<T>> Batch<T>(this List<T> collection,
            uint batchSize)
        {
            return Batch<T, List<T>>(collection, batchSize);
        }
    }
}
