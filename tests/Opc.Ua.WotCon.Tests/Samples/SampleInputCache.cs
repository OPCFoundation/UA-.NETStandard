// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using SampleDocument = Opc.Ua.WotCon.Tests.Samples.WotAggregationDocumentGenerator.SampleDocument;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Reads and converts each checked-in sample input once per test process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sample tests run in parallel, so everything handed out here is
    /// shared between concurrently running tests and must be immutable to
    /// them: <see cref="ArrayOf{T}"/> and <see cref="ByteString"/> expose only
    /// read-only views, and <see cref="WotDocumentNodeResolver"/> only reads
    /// its indexes once it is built. A mutable <see cref="UANodeSet"/> is never
    /// cached: tests change the NodeSets they read, and reading one is cheap.
    /// </para>
    /// <para>
    /// The explicit writer tests rewrite the checked-in documents. They are
    /// <c>[NonParallelizable]</c>, and every write goes through
    /// <see cref="Invalidate"/>, so no test is served what a writer replaced.
    /// A failed load is not cached: the next caller retries it.
    /// </para>
    /// </remarks>
    internal static class SampleInputCache
    {
        /// <summary>
        /// The node resolver over the DI, Machinery and Pumps companion models,
        /// built from their source NodeSets.
        /// </summary>
        /// <param name="repositoryRoot">The repository root.</param>
        public static IWotNodeResolver CompanionNodeResolver(string repositoryRoot)
        {
            return GetOrLoad(
                s_companionNodeResolvers,
                Key(repositoryRoot),
                WotAggregationDocumentGenerator.CreateCompanionNodeResolver);
        }

        /// <summary>
        /// The checked-in documents the manifest selects, in manifest order.
        /// </summary>
        /// <param name="documentsDirectory">The sample documents directory.</param>
        public static ArrayOf<SampleDocument> ManifestDocuments(string documentsDirectory)
        {
            return GetOrLoad(
                s_manifestDocuments,
                Key(documentsDirectory),
                WotAggregationDocumentGenerator.ReadManifestDocuments);
        }

        /// <summary>
        /// The unbound declaration documents converted from a pump NodeSet.
        /// </summary>
        /// <param name="sourcePath">The pump NodeSet path.</param>
        public static ArrayOf<SampleDocument> PumpDeclarationDocuments(string sourcePath)
        {
            return GetOrLoad(
                s_pumpDeclarationDocuments,
                Key(sourcePath),
                path => WotAggregationDocumentGenerator.GeneratePumpDeclarationDocuments(
                    WotAggregationDocumentGenerator.ReadNodeSet(path)));
        }

        /// <summary>
        /// Forgets every cached input. Called after each write to the
        /// checked-in sample documents.
        /// </summary>
        public static void Invalidate()
        {
            s_companionNodeResolvers.Clear();
            s_manifestDocuments.Clear();
            s_pumpDeclarationDocuments.Clear();
        }

        private static T GetOrLoad<T>(
            ConcurrentDictionary<string, Lazy<T>> cache,
            string key,
            Func<string, T> load)
        {
            Lazy<T> entry = cache.GetOrAdd(
                key,
                path => new Lazy<T>(() => load(path), LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                return entry.Value;
            }
            catch
            {
                ((ICollection<KeyValuePair<string, Lazy<T>>>)cache).Remove(
                    new KeyValuePair<string, Lazy<T>>(key, entry));
                throw;
            }
        }

        private static string Key(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static readonly ConcurrentDictionary<string, Lazy<IWotNodeResolver>> s_companionNodeResolvers =
            new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, Lazy<ArrayOf<SampleDocument>>> s_manifestDocuments =
            new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, Lazy<ArrayOf<SampleDocument>>> s_pumpDeclarationDocuments =
            new(StringComparer.Ordinal);
    }
}
