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
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class JsonEncoderDateTimeBenchmark
    {
        [Params(0, 4, 7)]
        public int DateTimeOmittedZeros { get; set; } = 0;

        [Benchmark]
        [Test]
        public void DateTimeEncodeToString()
        {
            _ = m_dateTime.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture);
        }

        [Benchmark]
        [Test]
        public void ConvertToUniversalTime()
        {
            _ = JsonEncoder.ConvertUniversalTimeToString(m_dateTime);
        }

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // for validating benchmark tests
            m_dateTime = DateTime.UtcNow;
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
            // for validating benchmark tests
            switch (DateTimeOmittedZeros)
            {
                case 4: m_dateTime = new DateTime(2011, 11, 11, 11, 11, 11, 999, DateTimeKind.Utc); break;
                case 7: m_dateTime = new DateTime(2011, 11, 11, 11, 11, 11, DateTimeKind.Utc); break;
                default:
                    do
                    {
                        m_dateTime = DateTime.UtcNow;
                    } while (m_dateTime.Ticks % 10 == 0);
                    break;
            }
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
        }
        #endregion

        #region Private Fields
        private DateTime m_dateTime;
        #endregion
    }
}
