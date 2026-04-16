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
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

#pragma warning disable CS0618 // Obsolete members tested intentionally for coverage
#pragma warning disable IDE0004 // Remove Unnecessary Cast

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UtilsFormattingTests
    {
        [Test]
        public void FormatNullTextThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Utils.Format(null));
        }

        [Test]
        public void FormatSimpleTextReturnsText()
        {
            string result = Utils.Format("Hello World");
            Assert.That(result, Is.EqualTo("Hello World"));
        }

        [Test]
        public void FormatWithArgsSubstitutes()
        {
            string result = Utils.Format("Value={0}", 42);
            Assert.That(result, Is.EqualTo("Value=42"));
        }

        [Test]
        public void FormatWithMultipleArgs()
        {
            string result = Utils.Format("{0}+{1}={2}", 1, 2, 3);
            Assert.That(result, Is.EqualTo("1+2=3"));
        }

        [Test]
        public void FormatWithBadFormatThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => Utils.Format("{0}{1}{2}", "only_one"));
        }

        [Test]
        public void ToHexStringEmptyArrayReturnsEmpty()
        {
            string result = Utils.ToHexString([]);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ToHexStringReturnsUppercaseHex()
        {
            byte[] data = [0xAB, 0xCD, 0xEF];
            string result = Utils.ToHexString(data);
            Assert.That(result, Is.EqualTo("ABCDEF"));
        }

        [Test]
        public void ToHexStringInvertedEndian()
        {
            byte[] data = [0x01, 0x02, 0x03];
            string result = Utils.ToHexString(data, invertEndian: true);
            Assert.That(result, Is.EqualTo("030201"));
        }

        [Test]
        public void ToHexStringSingleByte()
        {
            byte[] data = [0x0F];
            string result = Utils.ToHexString(data);
            Assert.That(result, Is.EqualTo("0F"));
        }

        [Test]
        public void FromHexStringRoundTrip()
        {
            byte[] original = [0xDE, 0xAD, 0xBE, 0xEF];
            string hex = Utils.ToHexString(original);
            byte[] result = Utils.FromHexString(hex);
            Assert.That(result, Is.EqualTo(original));
        }

        [Test]
        public void FromHexStringNullReturnsNull()
        {
            Assert.That(Utils.FromHexString(null), Is.Null);
        }

        [Test]
        public void FromHexStringEmptyReturnsEmptyArray()
        {
            byte[] result = Utils.FromHexString(string.Empty);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FromHexStringLowercaseWorks()
        {
            byte[] result = Utils.FromHexString("abcd");
            Assert.That(result, Is.EqualTo(new byte[] { 0xAB, 0xCD }));
        }

        [Test]
        public void IncrementIdentifierUintIncrementsAndReturns()
        {
            uint id = 10;
            uint result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(11));
            Assert.That(id, Is.EqualTo(11));
        }

        [Test]
        public void IncrementIdentifierUintWrapsOnOverflow()
        {
            uint id = uint.MaxValue;
            uint result = Utils.IncrementIdentifier(ref id);
            // Wraps around using unchecked arithmetic
            Assert.That(result, Is.EqualTo(1));
        }

        [Test]
        public void IncrementIdentifierIntIncrementsAndReturns()
        {
            int id = 5;
            int result = Utils.IncrementIdentifier(ref id);
            Assert.That(result, Is.EqualTo(6));
            Assert.That(id, Is.EqualTo(6));
        }

        [Test]
        public void IsEqualGenericSameValuesReturnsTrue()
        {
            Assert.That(Utils.IsEqual(42, 42), Is.True);
        }

        [Test]
        public void IsEqualGenericDifferentValuesReturnsFalse()
        {
            Assert.That(Utils.IsEqual(1, 2), Is.False);
        }

        [Test]
        public void IsEqualObjectBothNullReturnsTrue()
        {
            Assert.That(Utils.IsEqual((object)null, (object)null), Is.True);
        }

        [Test]
        public void IsEqualObjectOneNullReturnsFalse()
        {
            Assert.That(Utils.IsEqual((object)"a", (object)null), Is.False);
            Assert.That(Utils.IsEqual((object)null, (object)"a"), Is.False);
        }

        [Test]
        public void IsEqualObjectSameReturnsTrue()
        {
            Assert.That(Utils.IsEqual((object)"test", (object)"test"), Is.True);
        }

        [Test]
        public void IsEqualEnumerableSameReturnsTrue()
        {
            var list1 = new List<int> { 1, 2, 3 };
            var list2 = new List<int> { 1, 2, 3 };
            Assert.That(Utils.IsEqual(list1, list2), Is.True);
        }

        [Test]
        public void IsEqualEnumerableDifferentReturnsFalse()
        {
            var list1 = new List<int> { 1, 2, 3 };
            var list2 = new List<int> { 1, 2, 4 };
            Assert.That(Utils.IsEqual(list1, list2), Is.False);
        }

        [Test]
        public void IsEqualEnumerableDifferentLengthReturnsFalse()
        {
            var list1 = new List<int> { 1, 2 };
            var list2 = new List<int> { 1, 2, 3 };
            Assert.That(Utils.IsEqual(list1, list2), Is.False);
        }

        [Test]
        public void IsEqualEnumerableBothNullReturnsTrue()
        {
            Assert.That(
#pragma warning disable IDE0004 // Remove Unnecessary Cast
                Utils.IsEqual((IEnumerable<int>)null, (IEnumerable<int>)null),
#pragma warning restore IDE0004 // Remove Unnecessary Cast
                Is.True);
        }

        [Test]
        public void IsEqualEnumerableOneNullReturnsFalse()
        {
            Assert.That(
#pragma warning disable IDE0004 // Remove Unnecessary Cast
                Utils.IsEqual(new List<int> { 1 }, (IEnumerable<int>)null),
#pragma warning restore IDE0004 // Remove Unnecessary Cast
                Is.False);
        }

        [Test]
        public void IsEqualArraySameReturnsTrue()
        {
            int[] a = [1, 2, 3];
            int[] b = [1, 2, 3];
            Assert.That(Utils.IsEqual(a, b), Is.True);
        }

        [Test]
        public void IsEqualArrayDifferentReturnsFalse()
        {
            int[] a = [1, 2];
            int[] b = [3, 4];
            Assert.That(Utils.IsEqual(a, b), Is.False);
        }

        [Test]
        public void AppendNullReturnsEmpty()
        {
            byte[] result = Utils.Append(null);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Length.EqualTo(0));
        }

        [Test]
        public void AppendSingleArrayReturnsCopy()
        {
            byte[] input = [1, 2, 3];
            byte[] result = Utils.Append(input);
            Assert.That(result, Is.EqualTo(input));
        }

        [Test]
        public void AppendMultipleArraysConcatenates()
        {
            byte[] a = [1, 2];
            byte[] b = [3, 4];
            byte[] result = Utils.Append(a, b);
            Assert.That(result, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void AppendWithNullArrayInMiddleSkipsNull()
        {
            byte[] a = [1];
            byte[] b = [2];
            byte[] result = Utils.Append(a, null, b);
            Assert.That(result, Is.EqualTo(new byte[] { 1, 2 }));
        }

        [Test]
        public void AppendEmptyArraysReturnsEmpty()
        {
            byte[] result = Utils.Append(
                [], []);
            Assert.That(result, Has.Length.EqualTo(0));
        }

        [Test]
        public void GetAssemblyBuildNumberReturnsNonEmpty()
        {
            string buildNumber = Utils.GetAssemblyBuildNumber();
            Assert.That(buildNumber, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void GetAssemblySoftwareVersionReturnsNonEmpty()
        {
            string version = Utils.GetAssemblySoftwareVersion();
            Assert.That(version, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void GetAssemblyTimestampReturnsValidDate()
        {
            DateTime timestamp = Utils.GetAssemblyTimestamp();
            Assert.That(timestamp, Is.GreaterThan(DateTime.MinValue));
        }

        [Test]
        public void SetTraceMaskAndGetTraceMask()
        {
            int originalMask = Utils.TraceMask;
            try
            {
                Utils.SetTraceMask(Utils.TraceMasks.All);
                Assert.That(Utils.TraceMask, Is.EqualTo(Utils.TraceMasks.All));
            }
            finally
            {
                Utils.SetTraceMask(originalMask);
            }
        }

        [Test]
        public void LogErrorStringDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogError("Test error message {0}", 42));
        }

        [Test]
        public void LogErrorExceptionDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogError(
                    new InvalidOperationException("test"),
                    "Error occurred: {0}", "detail"));
        }

        [Test]
        public void LogErrorWithEventIdDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogError(new EventId(1, "TestEvent"),
                    "Error event: {0}", "test"));
        }

        [Test]
        public void LogErrorWithEventIdAndExceptionDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogError(
                    new EventId(2, "TestEvent"),
                    new InvalidOperationException("err"),
                    "Error: {0}", "detail"));
        }

        [Test]
        public void LogWarningStringDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogWarning("Warning: {0}", "test"));
        }

        [Test]
        public void LogWarningExceptionDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogWarning(
                    new InvalidOperationException("warn"),
                    "Warn: {0}", "detail"));
        }

        [Test]
        public void LogWarningWithEventIdDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogWarning(
                    new EventId(3, "WarnEvent"),
                    "Warn event: {0}", "test"));
        }

        [Test]
        public void LogInfoStringDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogInfo("Info: {0}", "test"));
        }

        [Test]
        public void LogInfoExceptionDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogInfo(
                    new InvalidOperationException("info"),
                    "Info: {0}", "detail"));
        }

        [Test]
        public void LogInfoWithEventIdDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogInfo(
                    new EventId(4, "InfoEvent"),
                    "Info event: {0}", "test"));
        }

        [Test]
        public void LogTraceStringDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogTrace("Trace: {0}", "test"));
        }

        [Test]
        public void LogTraceExceptionDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogTrace(
                    new InvalidOperationException("trace"),
                    "Trace: {0}", "detail"));
        }

        [Test]
        public void LogTraceWithEventIdDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogTrace(
                    new EventId(5, "TraceEvent"),
                    "Trace event: {0}", "test"));
        }

        [Test]
        public void LogTraceWithEventIdAndExceptionDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogTrace(
                    new EventId(6, "TraceEvent"),
                    new InvalidOperationException("trace"),
                    "Trace: {0}", "detail"));
        }

        [Test]
        public void LogWarningWithEventIdAndExceptionDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogWarning(
                    new EventId(7, "WarnEvent"),
                    new InvalidOperationException("warn"),
                    "Warn: {0}", "detail"));
        }

        [Test]
        public void LogInfoWithEventIdAndExceptionDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogInfo(
                    new EventId(8, "InfoEvent"),
                    new InvalidOperationException("info"),
                    "Info: {0}", "detail"));
        }

        [Test]
        public void LogErrorWithHandledFlagDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => Utils.LogError(
                    new InvalidOperationException("err"),
                    "Error: {0}", true, "detail"));
        }

#if NET5_0_OR_GREATER
        [Test]
        public void ToHexStringSpanVariant()
        {
            byte[] data = [0x01, 0xFF];
            string result = Utils.ToHexString((ReadOnlySpan<byte>)data);
            Assert.That(result, Is.EqualTo("01FF"));
        }

        [Test]
        public void ToHexStringSpanInvertedEndian()
        {
            byte[] data = [0x01, 0xFF];
            string result = Utils.ToHexString((ReadOnlySpan<byte>)data, invertEndian: true);
            Assert.That(result, Is.EqualTo("FF01"));
        }
#endif
    }
}
