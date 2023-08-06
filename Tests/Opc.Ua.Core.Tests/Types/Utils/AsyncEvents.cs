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

using System.Buffers;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;


#if mist

namespace Opc.Ua.Core.Tests.Stack.Bindings
{
    [TestFixture, Category("AsyncEvent")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [MemoryDiagnoser]
    [BenchmarkCategory("AsyncEvent")]
    public class AsyncEventBenchmarks
    {
        //[Params(8192, 65535, 1024 * 1024 - 1)]
        public int BufferSize { get; set; } = 65535;

        /// <summary>
        /// Benchmark allocation with new.
        /// </summary>
        [Benchmark(Baseline = true)]
        [Test]
        public void AutoResetEvent_Set()
        {
            m_autoResetEvent.Set();
        }

        /// <summary>
        /// Benchmark allocation with new.
        /// </summary>
        [Benchmark(Baseline = true)]
        [Test]
        public async Task AutoResetEvent_WaitAsync()
        {
            m_autoResetEvent.Set();
            await m_autoResetEvent.WaitAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Benchmark Buffer allocation.
        /// </summary>
        [Benchmark]
        [Test]
        public async Task ArrayPoolCreateTooSmall()
        {
            m_autoResetEvent.Set();
            await m_autoResetEvent.WaitAsync().ConfigureAwait(false);
        }


        #region Private Methods
        #endregion

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_autoResetEvent = new AsyncAutoResetEvent();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_autoResetEvent = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_autoResetEvent = new AsyncAutoResetEvent();
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_autoResetEvent = null;
        }
        #endregion

        #region Private Fields
        AsyncAutoResetEvent m_autoResetEvent;
        #endregion
    }
}
#endif
