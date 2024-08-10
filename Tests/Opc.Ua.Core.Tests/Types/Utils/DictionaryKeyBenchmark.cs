/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


using BenchmarkDotNet.Order;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Opc.Ua;
using Opc.Ua.Test;
using System.Collections.Concurrent;
#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Performance tests for uint key dictionaries.
    /// </summary>
    [TestFixture, Category("Dictionary")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    public class DictionaryUIntKeyBenchmark : DictionaryKeyBenchmark<uint>
    {
    }

    /// <summary>
    /// Performance tests for NodeId key dictionaries.
    /// </summary>
    [TestFixture, Category("Dictionary")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    public class DictionaryNodeIdKeyBenchmark : DictionaryKeyBenchmark<NodeId>
    {
        [Params(IdType.Numeric, IdType.String, IdType.Guid, IdType.Opaque, IdType.Opaque + 1)]
        public IdType NodeIdType { get; set; } = (uint)IdType.Numeric;

        private NodeIdDictionary<DataValue> m_nodeIdDictionary = null;

        /// <summary>
        /// Tests performance and memory usage of ImmutableDictionary.
        /// </summary>
        [Test]
        [Benchmark]
        public void TestNodeIdDictionary()
        {
            foreach (NodeId key in m_lookup)
            {
                _ = m_nodeIdDictionary.TryGetValue(key, out _);
            }
        }

        protected override NodeId GetRandomKey(Random random, DataGenerator generator)
        {
            IdType nodeIdType = NodeIdType;
            if (NodeIdType > IdType.Opaque)
            {
                nodeIdType = (IdType)random.Next((int)IdType.Numeric, (int)IdType.Opaque);
            }

            switch (nodeIdType)
            {
                case IdType.String:
                    return new NodeId(generator.GetRandomString(), (ushort)random.Next(0, 10));
                case IdType.Guid:
                    return new NodeId(Guid.NewGuid(), (ushort)random.Next(0, 10));
                case IdType.Opaque:
                    return new NodeId(generator.GetRandomByteString(), (ushort)random.Next(0, 10));
                default:
                case IdType.Numeric:
                    return new NodeId((uint)random.Next(0, 10 * NumElements), (ushort)random.Next(0, 10));
            }
        }

        /// <summary>
        /// Overridden to initialize the NodeIdDictionary.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_nodeIdDictionary = new NodeIdDictionary<DataValue>();
            foreach (var entry in m_regularDictionary)
            {
                m_nodeIdDictionary.Add(entry.Key, entry.Value);
            }
        }
    }

    /// <summary>
    /// Performance tests for ExpandedNodeId key dictionaries.
    /// </summary>
    [TestFixture, Category("Dictionary")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    public class DictionaryExpandedNodeIdKeyBenchmark : DictionaryKeyBenchmark<ExpandedNodeId>
    {
    }

    /// <summary>
    /// Performance tests for T key dictionaries. Abstract to exclude from tests.
    /// </summary>
    public abstract class DictionaryKeyBenchmark<T>
    {
        // [Params(16, 128, 1024)]
        public int NumElements { get; set; } = 1024;

        // Create a Dictionary and a SortedDictionary
        protected T[] m_lookup = null;
        protected Dictionary<T, DataValue> m_regularDictionary = null;
        protected NamespaceTable m_namespaceTable = new NamespaceTable(new List<string> { Namespaces.OpcUa, "urn:myserver", "http://opcfoundation.org/bar", "http://opcfoundation.org/foo" });

        // test dictionaries
        private ImmutableDictionary<T, DataValue> m_immutableDictionary = null;
        private ConcurrentDictionary<T, DataValue> m_concurrentDictionary = null;
        private SortedDictionary<T, DataValue> m_sortedDictionary = null;
        private ReadOnlyDictionary<T, DataValue> m_readonlyDictionary = null;
#if NET8_0_OR_GREATER
        private FrozenDictionary<T, DataValue> m_frozenDictionary = null;
#endif

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Initialize();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            Initialize();
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
        }
        #endregion

        /// <summary>
        /// Tests performance and memory usage of ImmutableDictionary.
        /// </summary>
        [Test]
        [Benchmark]
        public void TestImmutableDictionary()
        {
            foreach (T key in m_lookup)
            {
                _ = m_immutableDictionary.TryGetValue(key, out _);
            }
        }

        /// <summary>
        /// Tests performance and memory usage of SortedDictionary.
        /// </summary>
        [Test]
        [Benchmark]
        public void TestConcurrentDictionary()
        {
            foreach (T key in m_lookup)
            {
                _ = m_concurrentDictionary.TryGetValue(key, out _);
            }
        }

        /// <summary>
        /// Tests performance and memory usage of SortedDictionary.
        /// </summary>
        [Test]
        [Benchmark]
        public void TestSortedDictionary()
        {
            foreach (T key in m_lookup)
            {
                _ = m_sortedDictionary.TryGetValue(key, out _);
            }
        }

        /// <summary>
        /// Tests performance and memory usage of ReadonlyDictionary.
        /// </summary>
        [Test]
        [Benchmark]
        public void TestReadonlyDictionary()
        {
            foreach (T key in m_lookup)
            {
                _ = m_readonlyDictionary.TryGetValue(key, out _);
            }
        }

        /// <summary>
        /// Tests performance and memory usage of Dictionary.
        /// </summary>
        [Test]
        [Benchmark(Baseline = true)]
        public void TestRegularDictionary()
        {
            foreach (T key in m_lookup)
            {
                _ = m_regularDictionary.TryGetValue(key, out _);
            }
        }

        /// <summary>
        /// Tests performance and memory usage of FrozenDictionary.
        /// </summary>
        [Test]
        [Benchmark]
        public void TestFrozenDictionary()
        {
#if NET8_0_OR_GREATER
            foreach (T key in m_lookup)
            {
                _ = m_frozenDictionary.TryGetValue(key, out _);
            }
#else
            TestRegularDictionary();
#endif
        }

        protected virtual T GetRandomKey(Random random, DataGenerator generator)
        {
            if (typeof(T) == typeof(uint))
            {
                return (T)(object)Convert.ToUInt32(random.Next(0, 10 * NumElements)); // Random uint key
            }
            else if (typeof(T) == typeof(NodeId))
            {
                return (T)(object)new NodeId((uint)random.Next(0, 10 * NumElements), (ushort)random.Next(0, 10));
            }
            else if (typeof(T) == typeof(ExpandedNodeId))
            {
                return (T)(object)new ExpandedNodeId((uint)random.Next(0, 10 * NumElements), m_namespaceTable.GetString((uint)random.Next(0, m_namespaceTable.Count - 1)));
            }
            else
            {
                throw new NotSupportedException("Unsupported key type");
            }
        }

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Not used for crypto.")]
        protected virtual void Initialize()
        {
            var random = new Random(0x62541);
            var randomSource = new RandomSource(0x62541);
            var generator = new DataGenerator(randomSource);

            // Populate dictionaries with random data
            m_regularDictionary = new Dictionary<T, DataValue>(NumElements);
            m_lookup = new T[NumElements];
            for (int i = 0; i < NumElements; i++)
            {
                m_lookup[i] = GetRandomKey(random, generator);
                m_regularDictionary[m_lookup[i]] = generator.GetRandomDataValue();
            }

            // initialize other dicts
            m_sortedDictionary = new SortedDictionary<T, DataValue>(m_regularDictionary);
            m_readonlyDictionary = new ReadOnlyDictionary<T, DataValue>(m_regularDictionary);
            m_concurrentDictionary = new ConcurrentDictionary<T, DataValue>(m_regularDictionary);
            m_immutableDictionary = m_regularDictionary.ToImmutableDictionary();
#if NET8_0_OR_GREATER
            m_frozenDictionary = m_regularDictionary.ToFrozenDictionary();
#endif
        }
    }
}
