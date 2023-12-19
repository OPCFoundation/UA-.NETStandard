/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;
using System.Threading;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Bindings
{
    [TestFixture, Category("BufferManager")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [MemoryDiagnoser]
    [BenchmarkCategory("BufferManager")]
    public class BufferManagerBenchmarks
    {
        //[Params(8192, 65535, 1024 * 1024 - 1)]
        public int BufferSize { get; set; } = 65535;

        //[Params( /*8,*/ 64, 256, 1024)]
        public int Allocations { get; set; } = 256;

        //[Params(4, 32, 256)]
        public int BucketSize { get; set; } = 32;

        /// <summary>
        /// Benchmark allocation with new.
        /// </summary>
        // [Benchmark(Baseline = true)]
        [Test]
        public void BuffersWithNew()
        {
            for (int i = 0; i < Allocations; i++)
            {
                m_bufferArray[i] = new byte[BufferSize + 1];
                m_bufferArray[i][i] = (byte)i;
            }
            for (int i = 0; i < Allocations; i++)
            {
                m_bufferArray[i] = null;
            }
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Benchmark allocation with new.
        /// </summary>
        [Benchmark]
        [Test]
        public void BuffersWithAllocateUninitializedArray()
        {
            for (int i = 0; i < Allocations; i++)
            {
                m_bufferArray[i] = GC.AllocateUninitializedArray<byte>(BufferSize+1);
                m_bufferArray[i][i] = (byte)i;
            }
            for (int i = 0; i < Allocations; i++)
            {
                m_bufferArray[i] = null;
            }
        }
#endif

        /// <summary>
        /// Benchmark Buffer allocation.
        /// </summary>
        [Benchmark]
        [Test]
        public void ArrayPoolCreateTooSmall()
        {
            for (int i = 0; i < Allocations; i++)
            {
                m_bufferArray[i] = m_arrayPoolTooSmall.Rent(BufferSize + 1);
                m_bufferArray[i][i] = (byte)i;
            }
            foreach (var buffer in m_bufferArray)
            {
                m_arrayPoolTooSmall.Return(buffer);
            }
        }

        /// <summary>
        /// Benchmark Buffer allocation.
        /// </summary>
        [Benchmark]
        [Test]
        public void ArrayPoolCreate()
        {
            for (int i = 0; i < Allocations; i++)
            {
                m_bufferArray[i] = (m_arrayPool.Rent(BufferSize + 1));
                m_bufferArray[i][i] = (byte)i;
            }
            foreach (var buffer in m_bufferArray)
            {
                m_arrayPool.Return(buffer);
            }
        }
        /// <summary>
        /// Benchmark Buffer allocation.
        /// </summary>
        [Benchmark]
        [Test]
        public void ArrayPoolShared()
        {
            for (int i = 0; i < Allocations; i++)
            {
                m_bufferArray[i] = m_arrayPoolShared.Rent(BufferSize + 1);
                m_bufferArray[i][i] = (byte)i;
            }
            foreach (var buffer in m_bufferArray)
            {
                m_arrayPoolShared.Return(buffer);
            }
        }

        /// <summary>
        /// Benchmark Buffer allocation.
        /// </summary>
        [Benchmark]
        [Test]
        public void BufferManager()
        {
            for (int i = 0; i < Allocations; i++)
            {
                m_bufferArray[i] = m_bufferManager.TakeBuffer(BufferSize, nameof(BufferManager));
                m_bufferArray[i][i] = (byte)i;
            }
            foreach (var buffer in m_bufferArray)
            {
                m_bufferManager.ReturnBuffer(buffer, nameof(BufferManager));
            }
        }

        [Benchmark]
        [Test]
        public void ReadWriterLockSlim()
        {
            int readValue;
            try
            {
                m_readerWriterLockSlim.EnterReadLock();
                readValue = maxBufferSize;
            }
            finally
            {
                m_readerWriterLockSlim.ExitReadLock();
            }
        }

        [Benchmark]
        [Test]
        public void Lock()
        {
            int readValue;
            lock(m_lock)
            {
                readValue = maxBufferSize;
            }
        }

        #region Private Methods
        #endregion

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_bufferArray = new byte[Allocations][];
            m_arrayPoolTooSmall = ArrayPool<byte>.Create(BufferSize, BucketSize);
            m_arrayPool = ArrayPool<byte>.Create(BufferSize + 1, BucketSize);
            m_arrayPoolShared = ArrayPool<byte>.Shared;
            m_bufferManager = new BufferManager(nameof(BufferManager), BufferSize);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_arrayPoolTooSmall = null;
            m_arrayPool = null;
            m_arrayPoolShared = null;
            m_bufferManager = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_bufferArray = new byte[Allocations][];
            m_arrayPoolTooSmall = ArrayPool<byte>.Create(BufferSize, BucketSize);
            m_arrayPool = ArrayPool<byte>.Create(BufferSize + 1, BucketSize);
            m_arrayPoolShared = ArrayPool<byte>.Shared;
            m_bufferManager = new BufferManager(nameof(BufferManager), BufferSize);
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_arrayPoolTooSmall = null;
            m_arrayPool = null;
            m_arrayPoolShared = null;
            m_bufferManager = null;
        }
        #endregion

        #region Private Fields
        byte[][] m_bufferArray;
        ArrayPool<byte> m_arrayPoolTooSmall;
        ArrayPool<byte> m_arrayPool;
        ArrayPool<byte> m_arrayPoolShared;
        BufferManager m_bufferManager;
        int maxBufferSize = 1234;
        ReaderWriterLockSlim m_readerWriterLockSlim = new ReaderWriterLockSlim();
        readonly object m_lock = new object();
        #endregion
    }
}
