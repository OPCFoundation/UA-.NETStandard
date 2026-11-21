// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using NUnit.Framework;

namespace Opc.Ua.Core.Encoders.Tests
{
    [TestFixture]
    [Category("JsonEncoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class JsonEncoderDateTimeBenchmark
    {
        [Params(0, 4, 7)]
        public int DateTimeOmittedZeros { get; set; }

        [Benchmark]
        [Test]
        public void DateTimeEncodeToString()
        {
            _ = m_dateTime.ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture);
        }

        [Benchmark]
        [Test]
        public void ConvertToUniversalTime()
        {
#if NET6_0_OR_GREATER
            Span<char> valueString = stackalloc char[DateTimeHelper.DateTimeRoundTripKindLength];
            DateTimeHelper.ConvertUniversalTimeToString(m_dateTime, valueString, out int charsWritten);
            _ = valueString[..charsWritten];
#else
            _ = DateTimeHelper.ConvertUniversalTimeToString(m_dateTime);
#endif
        }

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

        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            // for validating benchmark tests
            switch (DateTimeOmittedZeros)
            {
                case 4:
                    m_dateTime = new DateTime(2011, 11, 11, 11, 11, 11, 999, DateTimeKind.Utc);
                    break;
                case 7:
                    m_dateTime = new DateTime(2011, 11, 11, 11, 11, 11, DateTimeKind.Utc);
                    break;
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

        private DateTime m_dateTime;
    }
}
