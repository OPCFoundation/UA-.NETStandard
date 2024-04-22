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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture, Category("Utils")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class UtilsIsEqualTests
    {
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int memcmp(byte[] b1, byte[] b2, long count);

        [Params(32, 128, 1024, 4096, 65536)]
        public int PayLoadSize { get; set; } = 1024;

        private bool _windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Test IsEqual using the generic IsEqual from previous versions.
        /// </summary>
        [Benchmark(Baseline = true)]
        public bool UtilsIsEqualGenericByteArrayCompare()
        {
            return IsEqualGeneric(m_bufferA, m_bufferB);
        }

        /// <summary>
        /// Test IsEqual using the byte[] direct call.
        /// </summary>
        [Benchmark]
        public bool UtilsIsEqualByteArrayCompare()
        {
            return IsEqual(m_bufferA, m_bufferB);
        }

        /// <summary>
        /// Test IsEqual using the template T[] direct call.
        /// </summary>
        [Benchmark]
        public bool UtilsIsEqualTemplateByteArrayCompare()
        {
            return Utils.IsEqual(m_bufferA, m_bufferB);
        }

        /// <summary>
        /// Test IsEqual using the latest generic implementation as object.
        /// </summary>
        [Benchmark]
        public bool UtilsIsEqualObjectCompare()
        {
            return Utils.IsEqual((object)m_bufferA, (object)m_bufferB);
        }

        /// <summary>
        /// Test IsEqual using the latest implementation as IEnumerable.
        /// </summary>
        [Benchmark]
        public bool UtilsIsEqualIEnumerableCompare()
        {
            return Utils.IsEqual((IEnumerable<byte>)m_bufferA, (IEnumerable<byte>)m_bufferB);
        }

        /// <summary>
        /// Directly compare a byte [] using SequenceEqual.
        /// </summary>
        [Benchmark]
        public bool SequenceEqualsByteArrayCompare()
        {
            return m_bufferA.SequenceEqual(m_bufferB);
        }

        /// <summary>
        /// Compare the byte[] using a for loop with index.
        /// </summary>
        [Benchmark]
        public bool ForLoopBinaryCompare()
        {
            if (m_bufferA.Length != m_bufferB.Length) return false;
            int payloadsize = m_bufferA.Length;
            for (int ii = 0; ii < payloadsize; ii++)
            {
                if (m_bufferA[ii] != m_bufferB[ii])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Test the memory compare using P/Invoke.
        /// </summary>
        [Benchmark]
        public bool MemCmpByteArrayCompare()
        {
            if (_windows)
            {
                // Validate buffers are the same length.
                // This also ensures that the count does not exceed the length of either buffer.  
                return m_bufferA.Length == m_bufferB.Length && memcmp(m_bufferA, m_bufferB, m_bufferA.Length) == 0;
            }
            else
            {
                return ForLoopBinaryCompare();
            }
        }

        /// <summary>
        /// Validate result of benchmark functions.
        /// </summary>
        [Test]
        public void UtilsIsEqualObjectCompareTest()
        {
            bool result;
            result = UtilsIsEqualGenericByteArrayCompare();
            Assert.True(result);
            result = UtilsIsEqualByteArrayCompare();
            Assert.True(result);
            result = UtilsIsEqualObjectCompare();
            Assert.True(result);
            result = UtilsIsEqualIEnumerableCompare();
            Assert.True(result);
            result = SequenceEqualsByteArrayCompare();
            Assert.True(result);
            result = ForLoopBinaryCompare();
            Assert.True(result);
            result = MemCmpByteArrayCompare();
            Assert.True(result);
        }

        [Test]
        public void UtilsIsEqualArrayEqualsByteArrayTest()
        {
            // byte arrays and null
            Assert.AreEqual(Utils.IsEqual((object)m_bufferA, (object)m_bufferB), Utils.IsEqual(m_bufferA, m_bufferB));
            Assert.AreEqual(Utils.IsEqual((object)null, (object)m_bufferB), Utils.IsEqual(null, m_bufferB));
            Assert.AreEqual(Utils.IsEqual((object)m_bufferA, (object)null), Utils.IsEqual(m_bufferA, null));
            Assert.AreEqual(Utils.IsEqual((object)null, (object)null), Utils.IsEqual((byte[])null, (byte[])null));

            Assert.AreEqual(Utils.IsEqual((object)m_bufferA, (object)m_bufferB), Utils.IsEqual((IEnumerable)m_bufferA, (IEnumerable)m_bufferB));
            Assert.AreEqual(Utils.IsEqual((object)null, (object)m_bufferB), Utils.IsEqual((IEnumerable)null, (IEnumerable)m_bufferB));
            Assert.AreEqual(Utils.IsEqual((object)m_bufferA, (object)null), Utils.IsEqual((IEnumerable)m_bufferA, (IEnumerable)null));

            Assert.AreEqual(Utils.IsEqual((object)m_bufferA, (object)m_bufferB), Utils.IsEqual((Array)m_bufferA, (Array)m_bufferB));
            Assert.AreEqual(Utils.IsEqual((object)null, (object)m_bufferB), Utils.IsEqual((Array)null, (Array)m_bufferB));
            Assert.AreEqual(Utils.IsEqual((object)m_bufferA, (object)null), Utils.IsEqual((Array)m_bufferA, (Array)null));

            int i = 1;
            Assert.AreEqual(Utils.IsEqual((object)i, (object)m_bufferB), Utils.IsEqual(i, m_bufferB));
        }

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // for validating benchmark tests
            m_random = new Random(0x62541);
            m_bufferA = new byte[PayLoadSize];
            m_bufferB = new byte[PayLoadSize];
            m_random.NextBytes(m_bufferA);
            Array.Copy(m_bufferA, m_bufferB, m_bufferA.Length);
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
            m_random = new Random(0x62541);
            m_bufferA = new byte[PayLoadSize];
            m_bufferB = new byte[PayLoadSize];
            m_random.NextBytes(m_bufferA);
            Array.Copy(m_bufferA, m_bufferB, m_bufferA.Length);
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
        }
        #endregion

        #region IsEqualByteArray
        /// <summary>
        /// Checks if two byte[] values are equal.
        /// </summary>
        public static bool IsEqual(byte[] value1, byte[] value2)
        {
            // check for reference equality.
            if (Object.ReferenceEquals(value1, value2))
            {
                return true;
            }

            if (Object.ReferenceEquals(value1, null) || Object.ReferenceEquals(value2, null))
            {
                return false;
            }

            return value1.SequenceEqual(value2);
        }
        #endregion

        #region IsEqual up to 1.4.372.106
        /// <summary>
        /// For backward comparison the original generic version of IsEqual up to release 1.4.372.106.
        /// </summary>
        public static bool IsEqualGeneric(object value1, object value2)
        {
            // check for reference equality.
            if (Object.ReferenceEquals(value1, value2))
            {
                return true;
            }

            // check for null values.
            if (value1 == null)
            {
                if (value2 != null)
                {
                    return value2.Equals(value1);
                }

                return true;
            }

            // check for null values.
            if (value2 == null)
            {
                return value1.Equals(value2);
            }

            // check that data types are the same.
            if (value1.GetType() != value2.GetType())
            {
                return value1.Equals(value2);
            }

            // check for DateTime objects
            if (value1 is DateTime time1)
            {
                return Utils.IsEqual(time1, (DateTime)value2);
            }

            // check for compareable objects.

            if (value1 is IComparable comparable1)
            {
                return comparable1.CompareTo(value2) == 0;
            }

            // check for encodeable objects.

            if (value1 is IEncodeable encodeable1)
            {
                if (!(value2 is IEncodeable encodeable2))
                {
                    return false;
                }

                return encodeable1.IsEqual(encodeable2);
            }

            // check for XmlElement objects.

            if (value1 is XmlElement element1)
            {
                if (!(value2 is XmlElement element2))
                {
                    return false;
                }

                return element1.OuterXml == element2.OuterXml;
            }

            // check for arrays.

            if (value1 is Array array1)
            {
                // arrays are greater than non-arrays.
                if (!(value2 is Array array2))
                {
                    return false;
                }

                // shorter arrays are less than longer arrays.
                if (array1.Length != array2.Length)
                {
                    return false;
                }

                // compare the array dimension
                if (array1.Rank != array2.Rank)
                {
                    return false;
                }

                // compare each rank.
                for (int ii = 0; ii < array1.Rank; ii++)
                {
                    if (array1.GetLowerBound(ii) != array2.GetLowerBound(ii) ||
                        array1.GetUpperBound(ii) != array2.GetUpperBound(ii))
                    {
                        return false;
                    }
                }

                IEnumerator enumerator1 = array1.GetEnumerator();
                IEnumerator enumerator2 = array2.GetEnumerator();

                // compare each element.
                while (enumerator1.MoveNext())
                {
                    // length is already checked
                    enumerator2.MoveNext();

                    bool result = Utils.IsEqual(enumerator1.Current, enumerator2.Current);

                    if (!result)
                    {
                        return false;
                    }
                }

                // arrays are identical.
                return true;
            }

            // check enumerables.

            if (value1 is IEnumerable enumerable1)
            {
                // collections are greater than non-collections.
                if (!(value2 is IEnumerable enumerable2))
                {
                    return false;
                }

                IEnumerator enumerator1 = enumerable1.GetEnumerator();
                IEnumerator enumerator2 = enumerable2.GetEnumerator();

                while (enumerator1.MoveNext())
                {
                    // enumerable2 must be shorter. 
                    if (!enumerator2.MoveNext())
                    {
                        return false;
                    }

                    bool result = Utils.IsEqual(enumerator1.Current, enumerator2.Current);

                    if (!result)
                    {
                        return false;
                    }
                }

                // enumerable2 must be longer.
                if (enumerator2.MoveNext())
                {
                    return false;
                }

                // must be equal.
                return true;
            }

            // check for objects that override the Equals function.
            return value1.Equals(value2);
        }
        #endregion

        #region Private Fields
        private Random m_random;
        private byte[] m_bufferA;
        private byte[] m_bufferB;
        #endregion
    }
}
