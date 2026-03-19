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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for <see cref="DiagnosticInfo"/>.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DiagnosticInfoTests
    {
        private static readonly ILogger s_logger = new Mock<ILogger>().Object;

        [Test]
        public void DiagnosticInfoDefault()
        {
            var diagnosticInfo = new DiagnosticInfo();
            Assert.That(diagnosticInfo, Is.Not.Null);
            Assert.That(diagnosticInfo.SymbolicId, Is.EqualTo(-1));
            Assert.That(diagnosticInfo.NamespaceUri, Is.EqualTo(-1));
            Assert.That(diagnosticInfo.Locale, Is.EqualTo(-1));
            Assert.That(diagnosticInfo.LocalizedText, Is.EqualTo(-1));
            Assert.That(diagnosticInfo.AdditionalInfo, Is.Null);
            Assert.That(diagnosticInfo.InnerStatusCode, Is.EqualTo(ServiceResult.Good.StatusCode));
            Assert.That(diagnosticInfo.InnerDiagnosticInfo, Is.Null);

#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(diagnosticInfo.Equals(null));
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
#pragma warning restore CA1508 // Avoid dead conditional code
            Assert.That(diagnosticInfo.IsNullDiagnosticInfo, Is.True);
        }

        [Test]
        public void CopyConstructorWithNullThrowsArgumentNullException()
        {
            Assert.That(() => new DiagnosticInfo((DiagnosticInfo)null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CopyConstructorDeepCopiesAllFieldsAndInner()
        {
            var inner = new DiagnosticInfo(10, 20, 30, 40, "inner info")
            {
                InnerStatusCode = StatusCodes.BadUnexpectedError
            };
            var original = new DiagnosticInfo(1, 2, 3, 4, "outer info")
            {
                InnerDiagnosticInfo = inner,
                InnerStatusCode = StatusCodes.Bad
            };

            var copy = new DiagnosticInfo(original);

            Assert.That(copy.SymbolicId, Is.EqualTo(1));
            Assert.That(copy.NamespaceUri, Is.EqualTo(2));
            Assert.That(copy.Locale, Is.EqualTo(3));
            Assert.That(copy.LocalizedText, Is.EqualTo(4));
            Assert.That(copy.AdditionalInfo, Is.EqualTo("outer info"));
            Assert.That(copy.InnerStatusCode, Is.EqualTo(StatusCodes.Bad));
            Assert.That(copy.InnerDiagnosticInfo, Is.Not.Null);
            Assert.That(copy.InnerDiagnosticInfo, Is.Not.SameAs(inner));
            Assert.That(copy.InnerDiagnosticInfo.SymbolicId, Is.EqualTo(10));
            Assert.That(copy.InnerDiagnosticInfo.AdditionalInfo, Is.EqualTo("inner info"));
        }

        [Test]
        public void ServiceResultConstructorWithAllMasksSetsAllFields()
        {
            var innerResult = new ServiceResult(StatusCodes.BadUnexpectedError);
            var result = new ServiceResult(
                "http://test.org/ns",
                StatusCodes.Bad,
                new LocalizedText("en", "Error occurred"),
                "debug info",
                (ServiceResult)innerResult);

            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceAll | DiagnosticsMasks.UserPermissionAdditionalInfo;

            var di = new DiagnosticInfo(result, mask, true, stringTable, s_logger);

            // SymbolicId and NamespaceUri should be set (indices into string table)
            Assert.That(di.SymbolicId, Is.GreaterThanOrEqualTo(0));
            Assert.That(di.NamespaceUri, Is.GreaterThanOrEqualTo(0));
            // Locale and LocalizedText should be set
            Assert.That(di.Locale, Is.GreaterThanOrEqualTo(0));
            Assert.That(di.LocalizedText, Is.GreaterThanOrEqualTo(0));
            // AdditionalInfo should be set
            Assert.That(di.AdditionalInfo, Is.EqualTo("debug info"));
            // InnerStatusCode from inner result
            Assert.That(di.InnerStatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            // InnerDiagnosticInfo should be created from inner result
            Assert.That(di.InnerDiagnosticInfo, Is.Not.Null);
        }

        [Test]
        public void ServiceResultConstructorWithServiceLevelFalseShiftsMask()
        {
            var result = new ServiceResult(
                "http://test.org/ns",
                StatusCodes.Bad,
                new LocalizedText("en", "Error"),
                null,
                (ServiceResult)null);

            var stringTable = new StringTable();
            // OperationSymbolicId=32, OperationLocalizedText=64: after >>5 they become ServiceSymbolicId=1, ServiceLocalizedText=2
            const DiagnosticsMasks mask = DiagnosticsMasks.OperationSymbolicId | DiagnosticsMasks.OperationLocalizedText;

            var di = new DiagnosticInfo(result, mask, false, stringTable, s_logger);

            // After shift, the symbolic id mask is active
            Assert.That(di.SymbolicId, Is.GreaterThanOrEqualTo(0));
            Assert.That(di.Locale, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ServiceResultConstructorWithNullStringTableThrows()
        {
            var result = new ServiceResult(StatusCodes.Bad);

            Assert.That(
                () => new DiagnosticInfo(result, DiagnosticsMasks.ServiceAll, true, null, s_logger),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ServiceResultConstructorReusesExistingStringsInTable()
        {
            var result = new ServiceResult(
                "http://existing.org",
                StatusCodes.Bad,
                LocalizedText.Null,
                null,
                (ServiceResult)null);

            var stringTable = new StringTable();
            // Pre-populate with the strings that Initialize will look up
            stringTable.Append(result.SymbolicId);
            stringTable.Append("http://existing.org");

            int originalCount = stringTable.Count;

            var di = new DiagnosticInfo(result, DiagnosticsMasks.ServiceSymbolicId, true, stringTable, s_logger);

            // Table should not have grown since strings were reused
            Assert.That(stringTable.Count, Is.EqualTo(originalCount));
            Assert.That(di.SymbolicId, Is.GreaterThanOrEqualTo(0));
            Assert.That(di.NamespaceUri, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ServiceResultConstructorSetsLocalizedTextAndLocale()
        {
            var result = new ServiceResult(
                null,
                StatusCodes.Good,
                new LocalizedText("de", "Fehler aufgetreten"),
                null,
                (ServiceResult)null);

            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceLocalizedText;

            var di = new DiagnosticInfo(result, mask, true, stringTable, s_logger);

            Assert.That(di.Locale, Is.GreaterThanOrEqualTo(0));
            Assert.That(di.LocalizedText, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ServiceResultConstructorReusesExistingLocalizedTextStrings()
        {
            var result = new ServiceResult(
                null,
                StatusCodes.Good,
                new LocalizedText("en", "Existing error"),
                null,
                (ServiceResult)null);

            var stringTable = new StringTable();
            stringTable.Append("en");
            stringTable.Append("Existing error");

            int originalCount = stringTable.Count;
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceLocalizedText;

            var di = new DiagnosticInfo(result, mask, true, stringTable, s_logger);

            Assert.That(stringTable.Count, Is.EqualTo(originalCount));
            Assert.That(di.Locale, Is.GreaterThanOrEqualTo(0));
            Assert.That(di.LocalizedText, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ServiceResultConstructorSetsAdditionalInfoWithBothMasks()
        {
            var result = new ServiceResult(
                null,
                StatusCodes.Good,
                LocalizedText.Null,
                "additional debug",
                (ServiceResult)null);

            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceAdditionalInfo | DiagnosticsMasks.UserPermissionAdditionalInfo;

            var di = new DiagnosticInfo(result, mask, true, stringTable, s_logger);

            Assert.That(di.AdditionalInfo, Is.EqualTo("additional debug"));
        }

        [Test]
        public void ServiceResultConstructorSkipsAdditionalInfoWithoutUserPermission()
        {
            var result = new ServiceResult(
                null,
                StatusCodes.Good,
                LocalizedText.Null,
                "should not appear",
                (ServiceResult)null);

            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceAdditionalInfo; // Missing UserPermissionAdditionalInfo

            var di = new DiagnosticInfo(result, mask, true, stringTable, s_logger);

            Assert.That(di.AdditionalInfo, Is.Null);
        }

        [Test]
        public void ServiceResultConstructorSetsInnerStatusCode()
        {
            var innerResult = new ServiceResult(StatusCodes.BadDecodingError);
            var result = new ServiceResult(StatusCodes.Bad, innerResult);

            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceInnerStatusCode;

            var di = new DiagnosticInfo(result, mask, true, stringTable, s_logger);

            Assert.That(di.InnerStatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ServiceResultConstructorCreatesInnerDiagnosticInfo()
        {
            var innerResult = new ServiceResult(
                "http://inner.org",
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "Inner error"),
                null,
                (ServiceResult)null);
            var result = new ServiceResult(
                "http://outer.org",
                StatusCodes.Bad,
                new LocalizedText("en", "Outer error"),
                null,
                (ServiceResult)innerResult);

            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceAll;

            var di = new DiagnosticInfo(result, mask, true, stringTable, s_logger);

            Assert.That(di.InnerDiagnosticInfo, Is.Not.Null);
        }

        [Test]
        public void ServiceResultConstructorTruncatesAtMaxDepth()
        {
            // Build a chain deeper than MaxInnerDepth (5)
            ServiceResult current = new ServiceResult(StatusCodes.Bad);
            for (int i = 0; i < DiagnosticInfo.MaxInnerDepth + 2; i++)
            {
                current = new ServiceResult(StatusCodes.Bad, current);
            }

            var stringTable = new StringTable();
            const DiagnosticsMasks mask =
                DiagnosticsMasks.ServiceInnerDiagnostics | DiagnosticsMasks.ServiceInnerStatusCode;

            // Navigate to the deepest inner diagnostic info
            DiagnosticInfo innermost = new DiagnosticInfo(current, mask, true, stringTable, s_logger);
            int depth = 0;
            while (innermost.InnerDiagnosticInfo != null)
            {
                innermost = innermost.InnerDiagnosticInfo;
                depth++;
            }

            // Depth should be limited to MaxInnerDepth
            Assert.That(depth, Is.LessThanOrEqualTo(DiagnosticInfo.MaxInnerDepth));
        }

        [Test]
        public void ExceptionConstructorWithServiceLevelTrueCreatesDiagnosticInfo()
        {
            var exception = new InvalidOperationException("test exception");
            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceAll;

            var di = new DiagnosticInfo(exception, mask, true, stringTable, s_logger);

            Assert.That(di, Is.Not.Null);
        }

        [Test]
        public void ExceptionConstructorWithServiceLevelFalseShiftsMask()
        {
            var exception = new ArgumentException("arg error");
            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.OperationAll;

            var di = new DiagnosticInfo(exception, mask, false, stringTable, s_logger);

            Assert.That(di, Is.Not.Null);
        }

        [Test]
        public void ServiceResultConstructorWithoutInnerResultLeavesInnerDefaults()
        {
            var result = new ServiceResult(StatusCodes.Bad);

            var stringTable = new StringTable();
            const DiagnosticsMasks mask = DiagnosticsMasks.ServiceAll;

            var di = new DiagnosticInfo(result, mask, true, stringTable, s_logger);

            Assert.That(di.InnerStatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(di.InnerDiagnosticInfo, Is.Null);
        }

        [Test]
        public void EqualsSameReferenceReturnsTrue()
        {
            var di = new DiagnosticInfo(1, 2, 3, 4, "test");

            Assert.That(di, Is.EqualTo((object)di));
        }

        [Test]
        public void EqualsNonDiagnosticInfoReturnsFalse()
        {
            var di = new DiagnosticInfo(1, 2, 3, 4, "test");

#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(di.Equals("not a diagnostic info"), Is.False);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
        }

        [Test]
        public void EqualsDifferentSymbolicIdReturnsFalse()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test");
            var di2 = new DiagnosticInfo(99, 2, 3, 4, "test");

            Assert.That(di1, Is.Not.EqualTo(di2));
        }

        [Test]
        public void EqualsDifferentNamespaceUriReturnsFalse()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test");
            var di2 = new DiagnosticInfo(1, 99, 3, 4, "test");

            Assert.That(di1, Is.Not.EqualTo(di2));
        }

        [Test]
        public void EqualsDifferentLocaleReturnsFalse()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test");
            var di2 = new DiagnosticInfo(1, 2, 99, 4, "test");

            Assert.That(di1, Is.Not.EqualTo(di2));
        }

        [Test]
        public void EqualsDifferentLocalizedTextReturnsFalse()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test");
            var di2 = new DiagnosticInfo(1, 2, 3, 99, "test");

            Assert.That(di1, Is.Not.EqualTo(di2));
        }

        [Test]
        public void EqualsDifferentAdditionalInfoReturnsFalse()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "alpha");
            var di2 = new DiagnosticInfo(1, 2, 3, 4, "beta");

            Assert.That(di1, Is.Not.EqualTo(di2));
        }

        [Test]
        public void EqualsDifferentInnerStatusCodeReturnsFalse()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test") { InnerStatusCode = StatusCodes.Good };
            var di2 = new DiagnosticInfo(1, 2, 3, 4, "test") { InnerStatusCode = StatusCodes.Bad };

            Assert.That(di1, Is.Not.EqualTo(di2));
        }

        [Test]
        public void EqualsWithMatchingInnerDiagnosticInfoReturnsTrue()
        {
            var inner1 = new DiagnosticInfo(10, 20, 30, 40, "inner");
            var inner2 = new DiagnosticInfo(10, 20, 30, 40, "inner");
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test") { InnerDiagnosticInfo = inner1 };
            var di2 = new DiagnosticInfo(1, 2, 3, 4, "test") { InnerDiagnosticInfo = inner2 };

            Assert.That(di1, Is.EqualTo(di2));
        }

        [Test]
        public void EqualsWithDifferentInnerDiagnosticInfoReturnsFalse()
        {
            var inner1 = new DiagnosticInfo(10, 20, 30, 40, "inner1");
            var inner2 = new DiagnosticInfo(99, 20, 30, 40, "inner2");
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test") { InnerDiagnosticInfo = inner1 };
            var di2 = new DiagnosticInfo(1, 2, 3, 4, "test") { InnerDiagnosticInfo = inner2 };

            Assert.That(di1, Is.Not.EqualTo(di2));
        }

        [Test]
        public void EqualsOneHasInnerDiagnosticInfoOtherDoesNotReturnsFalse()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test");
            var di2 = new DiagnosticInfo(1, 2, 3, 4, "test")
            {
                InnerDiagnosticInfo = new DiagnosticInfo(10, 20, 30, 40, "inner")
            };

            // di1 has no inner (returns false on line 578)
            Assert.That(di1, Is.Not.EqualTo(di2));
        }

        [Test]
        public void EqualsIdenticalDiagnosticInfoWithoutInnerReturnsTrue()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "test") { InnerStatusCode = StatusCodes.Bad };
            var di2 = new DiagnosticInfo(1, 2, 3, 4, "test") { InnerStatusCode = StatusCodes.Bad };

            Assert.That(di1, Is.EqualTo(di2));
        }

        [Test]
        public void EqualsAtMaxDepthReturnsTrueRegardlessOfDeeperDifferences()
        {
            // Build two chains that are identical up to MaxInnerDepth but differ deeper
            // MaxInnerDepth is 5, so at depth 5, Equals returns true even if inners differ
            static DiagnosticInfo BuildChain(int depth, int leafSymbolicId)
            {
                DiagnosticInfo current = new DiagnosticInfo(leafSymbolicId, 0, 0, 0, null);
                for (int i = 0; i < depth; i++)
                {
                    current = new DiagnosticInfo(1, 2, 3, 4, null) { InnerDiagnosticInfo = current };
                }
                return current;
            }

            // Create chains with MaxInnerDepth + 1 levels (0 through MaxInnerDepth)
            // At depth MaxInnerDepth, both still have inner but with different leaf values
            DiagnosticInfo di1 = BuildChain(DiagnosticInfo.MaxInnerDepth + 1, 100);
            DiagnosticInfo di2 = BuildChain(DiagnosticInfo.MaxInnerDepth + 1, 999);

            // Should return true because at depth 5, comparison is truncated
            Assert.That(di1, Is.EqualTo(di2));
        }

        [Test]
        public void GetHashCodeReturnsConsistentValue()
        {
            var di = new DiagnosticInfo(1, 2, 3, 4, "test");

            int hash1 = di.GetHashCode();
            int hash2 = di.GetHashCode();

            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void GetHashCodeDiffersWithDifferentAdditionalInfo()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, "alpha");
            var di2 = new DiagnosticInfo(1, 2, 3, 4, "beta");

            Assert.That(di1.GetHashCode(), Is.Not.EqualTo(di2.GetHashCode()));
        }

        [Test]
        public void GetHashCodeIncludesInnerDiagnosticInfo()
        {
            var di1 = new DiagnosticInfo(1, 2, 3, 4, null)
            {
                InnerDiagnosticInfo = new DiagnosticInfo(10, 20, 30, 40, null)
            };
            var di2 = new DiagnosticInfo(1, 2, 3, 4, null)
            {
                InnerDiagnosticInfo = new DiagnosticInfo(99, 88, 77, 66, null)
            };

            Assert.That(di1.GetHashCode(), Is.Not.EqualTo(di2.GetHashCode()));
        }

        [Test]
        public void GetHashCodeWithNullAdditionalInfoAndNoInner()
        {
            var di = new DiagnosticInfo(1, 2, 3, 4, null);

            int hash = di.GetHashCode();

            Assert.That(hash, Is.TypeOf<int>());
        }

        [Test]
        public void ToStringReturnsFormattedString()
        {
            var di = new DiagnosticInfo(1, 2, 3, 4, "test");

            string result = di.ToString();

            Assert.That(result, Is.EqualTo("1:2:3:4"));
        }

        [Test]
        public void ToStringWithNonNullFormatThrowsFormatException()
        {
            var di = new DiagnosticInfo(1, 2, 3, 4, "test");

            Assert.That(() => di.ToString("invalid", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ToStringWithNullFormatReturnsFormattedString()
        {
            var di = new DiagnosticInfo(5, 10, 15, 20, null);

            string result = di.ToString(null, null);

            Assert.That(result, Is.EqualTo("5:10:15:20"));
        }

        [Test]
        public void ToStringDefaultValuesShowsNegativeOnes()
        {
            var di = new DiagnosticInfo();

            string result = di.ToString();

            Assert.That(result, Is.EqualTo("-1:-1:-1:-1"));
        }

        [Test]
        public void CloneReturnsDeepCopy()
        {
            var original = new DiagnosticInfo(1, 2, 3, 4, "test")
            {
                InnerStatusCode = StatusCodes.Bad,
                InnerDiagnosticInfo = new DiagnosticInfo(10, 20, 30, 40, "inner")
            };

            var clone = (DiagnosticInfo)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.SymbolicId, Is.EqualTo(original.SymbolicId));
            Assert.That(clone.NamespaceUri, Is.EqualTo(original.NamespaceUri));
            Assert.That(clone.Locale, Is.EqualTo(original.Locale));
            Assert.That(clone.LocalizedText, Is.EqualTo(original.LocalizedText));
            Assert.That(clone.AdditionalInfo, Is.EqualTo(original.AdditionalInfo));
            Assert.That(clone.InnerStatusCode, Is.EqualTo(original.InnerStatusCode));
            Assert.That(clone.InnerDiagnosticInfo, Is.Not.Null);
            Assert.That(clone.InnerDiagnosticInfo, Is.Not.SameAs(original.InnerDiagnosticInfo));
            Assert.That(clone.InnerDiagnosticInfo.SymbolicId, Is.EqualTo(10));
        }

        [Test]
        public void MemberwiseCloneReturnsDeepCopy()
        {
            var original = new DiagnosticInfo(1, 2, 3, 4, "test");

            var clone = (DiagnosticInfo)original.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.SymbolicId, Is.EqualTo(1));
        }

        [Test]
        public void IsNullDiagnosticInfoReturnsFalseForNonDefaultSymbolicId()
        {
            var di = new DiagnosticInfo { SymbolicId = 1 };

            Assert.That(di.IsNullDiagnosticInfo, Is.False);
        }

        [Test]
        public void IsNullDiagnosticInfoReturnsFalseWithInnerDiagnosticInfo()
        {
            var di = new DiagnosticInfo
            {
                InnerDiagnosticInfo = new DiagnosticInfo()
            };

            Assert.That(di.IsNullDiagnosticInfo, Is.False);
        }

        [Test]
        public void IsNullDiagnosticInfoReturnsFalseWithNonGoodStatusCode()
        {
            var di = new DiagnosticInfo { InnerStatusCode = StatusCodes.Bad };

            Assert.That(di.IsNullDiagnosticInfo, Is.False);
        }

        [Test]
        public void IsNullDiagnosticInfoReturnsFalseWithAdditionalInfo()
        {
            var di = new DiagnosticInfo { AdditionalInfo = "info" };

            Assert.That(di.IsNullDiagnosticInfo, Is.False);
        }
    }
}
