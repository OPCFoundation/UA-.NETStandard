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

#nullable enable

using System;
using System.Xml;
using NUnit.Framework;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Tests for the <see cref="ServiceResult"/> class covering constructors,
    /// static factory methods, status check helpers, ToString, and diagnostics.
    /// </summary>
    [TestFixture]
    [Category("ServiceResult")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServiceResultTests
    {
        #region Static Properties
        [Test]
        public void GoodPropertyReturnsGoodStatusCode()
        {
            var result = ServiceResult.Good;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good.Code));
        }

        [Test]
        public void BadPropertyReturnsBadStatusCode()
        {
            var result = ServiceResult.Bad;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Bad.Code));
        }
        #endregion

        #region Constructors - StatusCode
        [Test]
        public void ConstructorWithStatusCodeSetsProperties()
        {
            // Covers: ServiceResult(StatusCode code) - line 171-174
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.Null);
            Assert.That(result.LocalizedText.IsNullOrEmpty, Is.True);
            Assert.That(result.NamespaceUri, Is.Null);
            Assert.That(result.AdditionalInfo, Is.Null);
        }

        [Test]
        public void ConstructorWithStatusCodeAndLocalizedText()
        {
            // Covers: ServiceResult(StatusCode code, LocalizedText localizedText) - line 179-182
            var text = new LocalizedText("en", "Something failed");
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, text);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Something failed"));
        }

        [Test]
        public void ConstructorWithStatusCodeAndInnerResult()
        {
            // Covers: ServiceResult(StatusCode code, ServiceResult innerResult) - line 163-166
            var inner = new ServiceResult(StatusCodes.BadDecodingError);
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, inner);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.Not.Null);
            Assert.That(result.InnerResult!.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void ConstructorWithAllProperties()
        {
            // Covers: ServiceResult(string, StatusCode, LocalizedText, string, ServiceResult) - lines 123-135
            var inner = new ServiceResult(StatusCodes.BadDecodingError);
            var text = new LocalizedText("en", "Error occurred");
            var result = new ServiceResult(
                "http://test.org",
                StatusCodes.BadUnexpectedError,
                text,
                "Additional debug info",
                inner);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://test.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Error occurred"));
            Assert.That(result.AdditionalInfo, Is.EqualTo("Additional debug info"));
            Assert.That(result.InnerResult, Is.SameAs(inner));
        }

        [Test]
        public void ConstructorWithNamespaceAndCodeAndLocalizedTextAndAdditionalInfo()
        {
            // Covers: ServiceResult(string, StatusCode, LocalizedText, string) - lines 187-199
            var text = new LocalizedText("en", "test");
            var result = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                text,
                "extra info");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("test"));
            Assert.That(result.AdditionalInfo, Is.EqualTo("extra info"));
            Assert.That(result.InnerResult, Is.Null);
        }

        [Test]
        public void ConstructorWithNamespaceAndCodeAndLocalizedText()
        {
            // Covers: ServiceResult(string, StatusCode, LocalizedText) - lines 204-215
            var text = new LocalizedText("en", "test");
            var result = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                text);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("test"));
            Assert.That(result.AdditionalInfo, Is.Null);
        }

        [Test]
        public void ConstructorWithXmlQualifiedNameAndLocalizedText()
        {
            // Covers: ServiceResult(StatusCode, XmlQualifiedName, LocalizedText) - lines 220-231
            var symbolicId = new XmlQualifiedName("MyError", "http://test.org");
            var text = new LocalizedText("en", "custom error");
            var result = new ServiceResult(
                StatusCodes.BadUnexpectedError,
                symbolicId,
                text);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.SymbolicId, Is.EqualTo("MyError"));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://test.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("custom error"));
        }

        [Test]
        public void ConstructorWithXmlQualifiedNameNullUsesNull()
        {
            // Covers: null path in XmlQualifiedName constructor - line 225-226
            var text = new LocalizedText("en", "error");
            var result = new ServiceResult(
                StatusCodes.BadUnexpectedError,
                (XmlQualifiedName?)null,
                text);

            Assert.That(result.NamespaceUri, Is.Null);
        }
        #endregion

        #region Constructors - Copy/Chain
        [Test]
        public void CopyConstructorCopiesProperties()
        {
            // Covers: ServiceResult(ServiceResult outerResult, ServiceResult innerResult) - lines 148-158
            var original = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "error"),
                "extra info");

            var copy = new ServiceResult(original);
            Assert.That(copy.Code, Is.EqualTo(original.Code));
            Assert.That(copy.NamespaceUri, Is.EqualTo(original.NamespaceUri));
            Assert.That(copy.LocalizedText, Is.EqualTo(original.LocalizedText));
            Assert.That(copy.AdditionalInfo, Is.EqualTo(original.AdditionalInfo));
            Assert.That(copy.InnerResult, Is.Null);
        }

        [Test]
        public void CopyConstructorWithInnerResultBuildsChain()
        {
            // Covers: ServiceResult(ServiceResult outerResult, ServiceResult innerResult) - lines 148-158
            var outer = new ServiceResult(StatusCodes.BadUnexpectedError);
            var inner = new ServiceResult(StatusCodes.BadDecodingError);
            var result = new ServiceResult(outer, inner);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.SameAs(inner));
        }
        #endregion

        #region Constructors - Exception wrapping
        [Test]
        public void ConstructorWithExceptionSetsDefaultBadCode()
        {
            // Covers: ServiceResult(Exception) - lines 413-416
            var exception = new InvalidOperationException("test error");
            var result = new ServiceResult(exception);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
            Assert.That(result.AdditionalInfo, Is.Not.Null.And.Not.Empty);
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void ConstructorWithExceptionAndDefaultCode()
        {
            // Covers: ServiceResult(Exception, StatusCode) - lines 403-408
            var exception = new ArgumentException("bad arg");
            var result = new ServiceResult(exception, StatusCodes.BadUnexpectedError);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.AdditionalInfo, Does.Contain("ArgumentException"));
        }

        [Test]
        public void ConstructorWithExceptionAndNamespaceAndDefaultCode()
        {
            // Covers: ServiceResult(Exception, string, StatusCode) - lines 385-395
            var exception = new InvalidOperationException("oops");
            var result = new ServiceResult(exception, "http://ns.org", StatusCodes.BadUnexpectedError);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void ConstructorWithExceptionDefaultCodeAndLocalizedText()
        {
            // Covers: ServiceResult(Exception, StatusCode, LocalizedText) - lines 370-376
            var exception = new InvalidOperationException("oops");
            var text = new LocalizedText("en", "localized error");
            var result = new ServiceResult(exception, StatusCodes.BadUnexpectedError, text);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("localized error"));
        }

        [Test]
        public void ConstructorWithExceptionAllParameters()
        {
            // Covers: ServiceResult(Exception, string, StatusCode, LocalizedText) - lines 331-361
            // Non-ServiceResultException path (else branch at line 353-358)
            var exception = new InvalidOperationException("test");
            var text = new LocalizedText("en", "error detail");
            var result = new ServiceResult(
                exception,
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                text);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("error detail"));
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void ConstructorWithServiceResultExceptionExtractsResult()
        {
            // Covers: ServiceResultException path in ServiceResult(Exception, string, StatusCode, LocalizedText) - lines 341-352
            var innerResult = new ServiceResult(
                "http://inner.org",
                StatusCodes.BadDecodingError,
                new LocalizedText("en", "decode failed"));
            var sre = new ServiceResultException(innerResult);

            var result = new ServiceResult(
                sre,
                "http://outer.org",
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "outer"));

            // ServiceResultException takes priority: uses exception's status code
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://inner.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("decode failed"));
        }

        [Test]
        public void ConstructorWithServiceResultExceptionUsesDefaultTextWhenEmpty()
        {
            // Covers: lines 348-351 - LocalizedText.IsNullOrEmpty path
            var innerResult = new ServiceResult(StatusCodes.BadDecodingError);
            var sre = new ServiceResultException(innerResult);

            var defaultText = new LocalizedText("en", "fallback text");
            var result = new ServiceResult(
                sre,
                null,
                StatusCodes.BadUnexpectedError,
                defaultText);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("fallback text"));
        }

        [Test]
        public void ConstructorWithAggregateExceptionUnwrapsSingleInner()
        {
            // Covers: AggregateException with single inner exception - lines 337-340
            var inner = new InvalidOperationException("inner error");
            var aggregate = new AggregateException(inner);

            var result = new ServiceResult(
                aggregate,
                null,
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "test"));

            // Should unwrap to the inner exception (InvalidOperationException, not AggregateException)
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void ConstructorWithAggregateExceptionMultipleInnerNotUnwrapped()
        {
            // Covers: AggregateException with multiple inners is NOT unwrapped - lines 337
            var inner1 = new InvalidOperationException("inner1");
            var inner2 = new ArgumentException("inner2");
            var aggregate = new AggregateException(inner1, inner2);

            var result = new ServiceResult(
                aggregate,
                null,
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "test"));

            // AggregateException with 2 inners should NOT be unwrapped
            Assert.That(result.AdditionalInfo, Does.Contain("AggregateException"));
        }
        #endregion

        #region Constructors - Exception with innerException parameter
        [Test]
        public void ConstructorWithCodeAndInnerException()
        {
            // Covers: ServiceResult(StatusCode, Exception) - lines 291-294
            var exception = new InvalidOperationException("inner");
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, exception);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithCodeLocalizedTextAndInnerException()
        {
            // Covers: ServiceResult(StatusCode, LocalizedText, Exception) - lines 316-322
            var exception = new InvalidOperationException("inner");
            var text = new LocalizedText("en", "outer error");
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, text, exception);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("outer error"));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithNamespaceCodeAndInnerException()
        {
            // Covers: ServiceResult(string, StatusCode, Exception) - lines 302-308
            var exception = new InvalidOperationException("inner");
            var result = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                exception);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithNamespaceCodeLocalizedTextAndInnerException()
        {
            // Covers: ServiceResult(string, StatusCode, LocalizedText, Exception) - lines 276-283
            var exception = new InvalidOperationException("inner");
            var text = new LocalizedText("en", "error detail");
            var result = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                text,
                exception);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("error detail"));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithInnerExceptionCollapseWhenNoNewInfo()
        {
            // Covers: lines 249-258 - the "no new information" collapse branch
            // When code matches inner exception's code, localized text is null, and additionalInfo is null
            var innerException = new ServiceResultException(StatusCodes.BadDecodingError);
            var result = new ServiceResult(
                null,
                StatusCodes.BadDecodingError,
                LocalizedText.Null,
                (string?)null,
                (Exception)innerException);

            // Should collapse - takes inner result's properties directly
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void ConstructorWithInnerExceptionDoesNotCollapseWhenNewInfoProvided()
        {
            // Covers: lines 260-267 - the else branch when new information IS provided
            var innerException = new ServiceResultException(StatusCodes.BadDecodingError);
            var result = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "new info"),
                "extra",
                (Exception)innerException);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("new info"));
            Assert.That(result.AdditionalInfo, Is.EqualTo("extra"));
            Assert.That(result.InnerResult, Is.Not.Null);
        }
        #endregion

        #region Constructors - Obsolete (uint-based)
        [Test]
        public void ObsoleteConstructorWithUintCodeAndInnerResult()
        {
            // Covers: Obsolete ServiceResult(uint, string, string, LocalizedText, string, ServiceResult) - lines 59-73
            var inner = new ServiceResult(StatusCodes.BadDecodingError);
            var text = new LocalizedText("en", "error");
            var result = new ServiceResult(
                StatusCodes.BadUnexpectedError.Code,
                "BadUnexpectedError",
                "http://ns.org",
                text,
                "extra",
                inner);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("error"));
            Assert.That(result.AdditionalInfo, Is.EqualTo("extra"));
            Assert.That(result.InnerResult, Is.SameAs(inner));
        }

        [Test]
        public void ObsoleteConstructorWithUintCodeAndInnerException()
        {
            // Covers: Obsolete ServiceResult(uint, string, string, LocalizedText, Exception) - lines 82-95
            var exception = new InvalidOperationException("inner");
            var text = new LocalizedText("en", "error");
            var result = new ServiceResult(
                StatusCodes.BadUnexpectedError.Code,
                "BadUnexpectedError",
                "http://ns.org",
                text,
                exception);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void ObsoleteConstructorWithUintCodeAdditionalInfoAndInnerException()
        {
            // Covers: Obsolete ServiceResult(uint, string, string, LocalizedText, string, Exception) - lines 104-118
            var exception = new InvalidOperationException("inner");
            var text = new LocalizedText("en", "error");
            var result = new ServiceResult(
                StatusCodes.BadUnexpectedError.Code,
                "BadUnexpectedError",
                "http://ns.org",
                text,
                "additional info",
                exception);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.Not.Null);
        }
        #endregion

        #region Constructors - DiagnosticInfo
        [Test]
        public void ConstructorWithDiagnosticInfoSetsProperties()
        {
            // Covers: ServiceResult(StatusCode, DiagnosticInfo, ArrayOf<string>) - lines 421-447
            var stringTable = new ArrayOf<string>(
                new[] { "http://ns.org", "MySymbolicId", "en-US", "A localized message" });
            var diagInfo = new DiagnosticInfo {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3,
                AdditionalInfo = "debug info"
            };

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, diagInfo, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.SymbolicId, Is.EqualTo("MySymbolicId"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("A localized message"));
            Assert.That(result.AdditionalInfo, Is.EqualTo("debug info"));
        }

        [Test]
        public void ConstructorWithDiagnosticInfoNullLeavesDefaults()
        {
            // Covers: null diagnosticInfo path - line 428
            var stringTable = new ArrayOf<string>(new[] { "unused" });
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, (DiagnosticInfo?)null, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.Null);
            Assert.That(result.AdditionalInfo, Is.Null);
        }

        [Test]
        public void ConstructorWithDiagnosticInfoAndBadInnerStatusCreatesInnerResult()
        {
            // Covers: lines 439-445 - inner status code is bad, creates InnerResult
            var stringTable = new ArrayOf<string>(new[] { "http://ns.org", "SymId", "en", "text" });
            var innerDiag = new DiagnosticInfo {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3
            };
            var diagInfo = new DiagnosticInfo {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3,
                InnerStatusCode = StatusCodes.BadDecodingError,
                InnerDiagnosticInfo = innerDiag
            };

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, diagInfo, stringTable);

            Assert.That(result.InnerResult, Is.Not.Null);
            Assert.That(result.InnerResult!.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void ConstructorWithDiagnosticInfoAndGoodInnerStatusNoInnerResult()
        {
            // Covers: line 439 - inner status code is good, no InnerResult
            var stringTable = new ArrayOf<string>(new[] { "http://ns.org", "SymId", "en", "text" });
            var diagInfo = new DiagnosticInfo {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3,
                InnerStatusCode = StatusCodes.Good
            };

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, diagInfo, stringTable);

            Assert.That(result.InnerResult, Is.Null);
        }

        [Test]
        public void ConstructorWithIndexedDiagnosticInfoSetsProperties()
        {
            // Covers: ServiceResult(StatusCode, int, ArrayOf<DiagnosticInfo>, ArrayOf<string>) - lines 452-484
            var stringTable = new ArrayOf<string>(
                new[] { "http://ns.org", "SymId", "en", "indexed message" });
            var diagInfo = new DiagnosticInfo {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3,
                AdditionalInfo = "indexed info"
            };
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>(new[] { diagInfo });

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, 0, diagnosticInfos, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.SymbolicId, Is.EqualTo("SymId"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("indexed message"));
            Assert.That(result.AdditionalInfo, Is.EqualTo("indexed info"));
        }

        [Test]
        public void ConstructorWithIndexedDiagnosticInfoOutOfRangeIgnored()
        {
            // Covers: index out of range path - line 460
            var stringTable = new ArrayOf<string>(new[] { "unused" });
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>(new DiagnosticInfo[] { new DiagnosticInfo() });

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, 5, diagnosticInfos, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.Null);
        }

        [Test]
        public void ConstructorWithIndexedDiagnosticInfoNegativeIndexIgnored()
        {
            // Covers: negative index path - line 460
            var stringTable = new ArrayOf<string>(new[] { "unused" });
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>(new DiagnosticInfo[] { new DiagnosticInfo() });

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, -1, diagnosticInfos, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.Null);
        }

        [Test]
        public void ConstructorWithIndexedDiagnosticInfoNullEntryIgnored()
        {
            // Covers: null diagnosticInfo entry at valid index - line 464
            var stringTable = new ArrayOf<string>(new[] { "unused" });
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>(new DiagnosticInfo?[] { null }!);

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, 0, diagnosticInfos, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.Null);
        }

        [Test]
        public void ConstructorWithIndexedDiagnosticInfoBadInnerStatusCreatesInnerResult()
        {
            // Covers: lines 475-481 - inner status code is bad for indexed constructor
            var stringTable = new ArrayOf<string>(new[] { "http://ns.org", "SymId", "en", "text" });
            var diagInfo = new DiagnosticInfo {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3,
                InnerStatusCode = StatusCodes.BadDecodingError
            };
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>(new[] { diagInfo });

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, 0, diagnosticInfos, stringTable);

            Assert.That(result.InnerResult, Is.Not.Null);
            Assert.That(result.InnerResult!.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void ConstructorWithDiagnosticInfoOutOfBoundsStringIndex()
        {
            // Covers: LookupString with out-of-bounds index returns null - lines 800-808
            var stringTable = new ArrayOf<string>(new[] { "only one" });
            var diagInfo = new DiagnosticInfo {
                NamespaceUri = 99, // out of bounds
                SymbolicId = -1,   // negative
                Locale = 0,
                LocalizedText = 0
            };

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, diagInfo, stringTable);

            Assert.That(result.NamespaceUri, Is.Null);
        }
        #endregion

        #region Static Factory Methods - Create
        [Test]
        public void CreateWithStatusCodeAndNullTranslationReturnsCodeOnly()
        {
            // Covers: Create(StatusCode, TranslationInfo) - lines 499-504, IsNull path
            var result = ServiceResult.Create(StatusCodes.BadUnexpectedError, TranslationInfo.Null);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.IsNullOrEmpty, Is.True);
        }

        [Test]
        public void CreateWithStatusCodeAndTranslation()
        {
            // Covers: Create(StatusCode, TranslationInfo) - lines 506, non-null path
            var translation = new TranslationInfo("key", "en", "error message");
            var result = ServiceResult.Create(StatusCodes.BadUnexpectedError, translation);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("error message"));
        }

        [Test]
        public void CreateWithExceptionAndNullTranslation()
        {
            // Covers: Create(Exception, TranslationInfo, StatusCode) - lines 512-527
            var exception = new InvalidOperationException("test");
            var result = ServiceResult.Create(exception, TranslationInfo.Null, StatusCodes.BadUnexpectedError);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void CreateWithExceptionAndTranslation()
        {
            // Covers: Create(Exception, TranslationInfo, StatusCode) - lines 529
            var exception = new InvalidOperationException("test");
            var translation = new TranslationInfo("key", "en", "translated error");
            var result = ServiceResult.Create(exception, translation, StatusCodes.BadUnexpectedError);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("translated error"));
        }

        [Test]
        public void CreateWithServiceResultExceptionUsesExceptionStatusCode()
        {
            // Covers: Create(Exception, TranslationInfo, StatusCode) - lines 519-522
            var sre = new ServiceResultException(StatusCodes.BadDecodingError);
            var result = ServiceResult.Create(sre, TranslationInfo.Null, StatusCodes.BadUnexpectedError);

            // Should use the exception's status code, not the default
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void CreateWithFormatNullReturnsCodeOnly()
        {
            // Covers: Create(StatusCode, string, params object[]) - lines 537-540
            var result = ServiceResult.Create(StatusCodes.BadUnexpectedError, null!);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.IsNullOrEmpty, Is.True);
        }

        [Test]
        public void CreateWithFormatNoArgsReturnsFormatAsText()
        {
            // Covers: Create(StatusCode, string, params object[]) - lines 542-545
            var result = ServiceResult.Create(StatusCodes.BadUnexpectedError, "Simple message");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Simple message"));
        }

        [Test]
        public void CreateWithFormatAndArgsReturnsFormattedText()
        {
            // Covers: Create(StatusCode, string, params object[]) - line 547
            var result = ServiceResult.Create(
                StatusCodes.BadUnexpectedError,
                "Error at index {0}: {1}",
                42, "detail");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Error at index 42: detail"));
        }

        [Test]
        public void CreateWithExceptionFormatNullReturnsCodeOnly()
        {
            // Covers: Create(Exception, StatusCode, string, params object[]) - lines 566-568
            var exception = new InvalidOperationException("test");
            var result = ServiceResult.Create(exception, StatusCodes.BadUnexpectedError, null!);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void CreateWithExceptionFormatEmptyReturnsCodeOnly()
        {
            // Covers: Create(Exception, StatusCode, string, params object[]) - lines 566-568
            var exception = new InvalidOperationException("test");
            var result = ServiceResult.Create(exception, StatusCodes.BadUnexpectedError, "");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void CreateWithExceptionFormatNoArgsReturnsFormatAsText()
        {
            // Covers: Create(Exception, StatusCode, string, params object[]) - lines 571-573
            var exception = new InvalidOperationException("test");
            var result = ServiceResult.Create(
                exception,
                StatusCodes.BadUnexpectedError,
                "Error writing variable.");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Error writing variable."));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void CreateWithExceptionFormatAndArgsReturnsFormattedText()
        {
            // Covers: Create(Exception, StatusCode, string, params object[]) - line 576
            var exception = new InvalidOperationException("test");
            var result = ServiceResult.Create(
                exception,
                StatusCodes.BadUnexpectedError,
                "Error at {0}",
                "nodeId");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Error at nodeId"));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void CreateWithServiceResultExceptionOverridesDefaultCode()
        {
            // Covers: Create(Exception, StatusCode, string, params object[]) - lines 561-564
            var sre = new ServiceResultException(StatusCodes.BadDecodingError);
            var result = ServiceResult.Create(
                sre,
                StatusCodes.BadUnexpectedError,
                "Error message");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }
        #endregion

        #region Static Status Check Methods - IsGood / IsBad / IsUncertain
        [Test]
        public void IsGoodWithGoodStatusReturnsTrue()
        {
            // Covers: IsGood(ServiceResult) - lines 582-590
            var result = new ServiceResult(StatusCodes.Good);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void IsGoodWithBadStatusReturnsFalse()
        {
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            Assert.That(ServiceResult.IsGood(result), Is.False);
        }

        [Test]
        public void IsGoodWithNullReturnsTrue()
        {
            // Covers: null path - lines 584, 589
            Assert.That(ServiceResult.IsGood(null!), Is.True);
        }

        [Test]
        public void IsNotGoodWithBadStatusReturnsTrue()
        {
            // Covers: IsNotGood(ServiceResult) - lines 595-603
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            Assert.That(ServiceResult.IsNotGood(result), Is.True);
        }

        [Test]
        public void IsNotGoodWithGoodStatusReturnsFalse()
        {
            var result = new ServiceResult(StatusCodes.Good);
            Assert.That(ServiceResult.IsNotGood(result), Is.False);
        }

        [Test]
        public void IsNotGoodWithNullReturnsTrue()
        {
            // Covers: null path - lines 597, 602
            Assert.That(ServiceResult.IsNotGood(null!), Is.True);
        }

        [Test]
        public void IsUncertainWithUncertainStatusReturnsTrue()
        {
            // Covers: IsUncertain(ServiceResult) - lines 608-616
            var result = new ServiceResult(StatusCodes.Uncertain);
            Assert.That(ServiceResult.IsUncertain(result), Is.True);
        }

        [Test]
        public void IsUncertainWithGoodStatusReturnsFalse()
        {
            var result = new ServiceResult(StatusCodes.Good);
            Assert.That(ServiceResult.IsUncertain(result), Is.False);
        }

        [Test]
        public void IsUncertainWithNullReturnsFalse()
        {
            // Covers: null path - lines 610, 615
            Assert.That(ServiceResult.IsUncertain(null!), Is.False);
        }

        [Test]
        public void IsGoodOrUncertainWithGoodStatusReturnsTrue()
        {
            // Covers: IsGoodOrUncertain(ServiceResult) - lines 621-629
            var result = new ServiceResult(StatusCodes.Good);
            Assert.That(ServiceResult.IsGoodOrUncertain(result), Is.True);
        }

        [Test]
        public void IsGoodOrUncertainWithUncertainStatusReturnsTrue()
        {
            var result = new ServiceResult(StatusCodes.Uncertain);
            Assert.That(ServiceResult.IsGoodOrUncertain(result), Is.True);
        }

        [Test]
        public void IsGoodOrUncertainWithBadStatusReturnsFalse()
        {
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            Assert.That(ServiceResult.IsGoodOrUncertain(result), Is.False);
        }

        [Test]
        public void IsGoodOrUncertainWithNullReturnsFalse()
        {
            // Covers: null path - lines 622, 628
            Assert.That(ServiceResult.IsGoodOrUncertain(null!), Is.False);
        }

        [Test]
        public void IsNotUncertainWithGoodStatusReturnsTrue()
        {
            // Covers: IsNotUncertain(ServiceResult) - lines 634-642
            var result = new ServiceResult(StatusCodes.Good);
            Assert.That(ServiceResult.IsNotUncertain(result), Is.True);
        }

        [Test]
        public void IsNotUncertainWithUncertainStatusReturnsFalse()
        {
            var result = new ServiceResult(StatusCodes.Uncertain);
            Assert.That(ServiceResult.IsNotUncertain(result), Is.False);
        }

        [Test]
        public void IsNotUncertainWithNullReturnsTrue()
        {
            // Covers: null path - lines 636, 641
            Assert.That(ServiceResult.IsNotUncertain(null!), Is.True);
        }

        [Test]
        public void IsBadWithBadStatusReturnsTrue()
        {
            // Covers: IsBad(ServiceResult) - lines 647-655
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            Assert.That(ServiceResult.IsBad(result), Is.True);
        }

        [Test]
        public void IsBadWithGoodStatusReturnsFalse()
        {
            var result = new ServiceResult(StatusCodes.Good);
            Assert.That(ServiceResult.IsBad(result), Is.False);
        }

        [Test]
        public void IsBadWithNullReturnsFalse()
        {
            // Covers: null path - lines 649, 654
            Assert.That(ServiceResult.IsBad(null!), Is.False);
        }

        [Test]
        public void IsNotBadWithGoodStatusReturnsTrue()
        {
            // Covers: IsNotBad(ServiceResult) - lines 660-668
            var result = new ServiceResult(StatusCodes.Good);
            Assert.That(ServiceResult.IsNotBad(result), Is.True);
        }

        [Test]
        public void IsNotBadWithBadStatusReturnsFalse()
        {
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            Assert.That(ServiceResult.IsNotBad(result), Is.False);
        }

        [Test]
        public void IsNotBadWithNullReturnsTrue()
        {
            // Covers: null path - lines 662, 667
            Assert.That(ServiceResult.IsNotBad(null!), Is.True);
        }
        #endregion

        #region Implicit/Explicit Conversion Operators
        [Test]
        public void ImplicitConversionFromStatusCode()
        {
            // Covers: implicit operator ServiceResult(StatusCode) - lines 673-676
            ServiceResult result = StatusCodes.BadUnexpectedError;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void ExplicitConversionToStatusCode()
        {
            // Covers: explicit operator StatusCode(ServiceResult) - lines 681-689
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            var statusCode = (StatusCode)result;
            Assert.That(statusCode.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void ExplicitConversionFromNullReturnsGood()
        {
            // Covers: null path - lines 683-686
            ServiceResult? nullResult = null;
            var statusCode = (StatusCode)nullResult!;
            Assert.That(StatusCode.IsGood(statusCode), Is.True);
        }
        #endregion

        #region LookupSymbolicId (Obsolete)
        [Test]
        public void LookupSymbolicIdDelegatesToStatusCode()
        {
            // Covers: LookupSymbolicId(uint) - lines 695-698
            // Verify the obsolete wrapper returns same result as the StatusCode method
            uint code = StatusCodes.BadUnexpectedError.Code;
            var fromServiceResult = ServiceResult.LookupSymbolicId(code);
            var fromStatusCode = StatusCode.LookupSymbolicId(code);
            Assert.That(fromServiceResult, Is.EqualTo(fromStatusCode));
        }

        [Test]
        public void LookupSymbolicIdForUnknownCodeReturnsNull()
        {
            // Covers: LookupSymbolicId(uint) with unknown code
            var symbolicId = ServiceResult.LookupSymbolicId(0xDEAD0000);
            Assert.That(symbolicId, Is.Null);
        }
        #endregion

        #region GetServiceResultException
        [Test]
        public void GetServiceResultExceptionReturnsWrappingException()
        {
            // Covers: GetServiceResultException() - lines 50-53
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            var exception = result.GetServiceResultException();

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception, Is.InstanceOf<ServiceResultException>());
            Assert.That(exception.Result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }
        #endregion

        #region ToString / Append
        [Test]
        public void ToStringContainsStatusCodeHex()
        {
            // Covers: ToString() and Append() - lines 748-795
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            var str = result.ToString();
            Assert.That(str, Does.Contain("80010000"));
        }

        [Test]
        public void ToStringWithSymbolicIdContainsSymbolicId()
        {
            // Covers: Append - lines 764-777, SymbolicId without namespace
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            var str = result.ToString();
            Assert.That(str, Does.Contain("BadUnexpectedError"));
        }

        [Test]
        public void ToStringWithNamespaceAndSymbolicIdContainsBoth()
        {
            // Covers: Append - lines 766-773, SymbolicId with namespace
            var symbolicId = new XmlQualifiedName("CustomError", "http://custom.org");
            var result = new ServiceResult(
                StatusCodes.BadUnexpectedError,
                symbolicId,
                LocalizedText.Null);
            var str = result.ToString();

            Assert.That(str, Does.Contain("http://custom.org"));
            Assert.That(str, Does.Contain("CustomError"));
        }

        [Test]
        public void ToStringWithLocalizedTextContainsText()
        {
            // Covers: Append - lines 780-783
            var text = new LocalizedText("en", "Something bad happened");
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, text);
            var str = result.ToString();

            Assert.That(str, Does.Contain("Something bad happened"));
        }

        [Test]
        public void ToStringWithAdditionalInfoContainsInfo()
        {
            // Covers: Append - lines 790-794
            var result = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                LocalizedText.Null,
                "Debug trace info here");
            var str = result.ToString();

            Assert.That(str, Does.Contain("Debug trace info here"));
        }

        [Test]
        public void ToStringWithSubCodeIncludesSubCode()
        {
            // Covers: Append - lines 785-788
            // StatusCode with non-zero lower 16 bits triggers the [XXXX] sub-code format
            uint codeWithSubCode = StatusCodes.BadUnexpectedError.Code | 0x0001;
            var result = new ServiceResult(new StatusCode(codeWithSubCode));
            var str = result.ToString();

            Assert.That(str, Does.Contain("[0001]"));
        }
        #endregion

        #region SymbolicId Property Setter
        [Test]
        public void SymbolicIdPropertySetViaConstructor()
        {
            // Covers: SymbolicId setter - line 724
            // The setter is invoked via the DiagnosticInfo constructor path
            var stringTable = new ArrayOf<string>(new[] { "ns", "MyId", "en", "text" });
            var diagInfo = new DiagnosticInfo {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3
            };

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, diagInfo, stringTable);

            Assert.That(result.SymbolicId, Is.EqualTo("MyId"));
            // Verify the code is preserved after setting SymbolicId
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }
        #endregion

        #region BuildExceptionTrace (via AdditionalInfo)
        [Test]
        public void BuildExceptionTraceIncludesExceptionType()
        {
            // Covers: BuildExceptionTrace(Exception) - lines 813-818
            var exception = new InvalidOperationException("test error message");
            var result = new ServiceResult(exception);

            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
            Assert.That(result.AdditionalInfo, Does.Contain("test error message"));
        }

        [Test]
        public void BuildExceptionTraceIncludesNestedExceptions()
        {
            // Covers: BuildExceptionTrace with nested exceptions
            var innerEx = new ArgumentException("inner cause");
            var outerEx = new InvalidOperationException("outer error", innerEx);
            var result = new ServiceResult(outerEx);

            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
            Assert.That(result.AdditionalInfo, Does.Contain("ArgumentException"));
            Assert.That(result.AdditionalInfo, Does.Contain("inner cause"));
        }
        #endregion

        #region GetDefaultMessage (via constructor paths)
        [Test]
        public void GetDefaultMessageFormatsExceptionTypeName()
        {
            // Covers: GetDefaultMessage(Exception) - lines 823-862
            // When exception message doesn't start with '[', it wraps with [TypeName] message
            var exception = new InvalidOperationException("simple message");
            var result = new ServiceResult(exception, StatusCodes.BadUnexpectedError);

            Assert.That(result.LocalizedText.Text, Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void GetDefaultMessageWithBracketPrefixReturnsAsIs()
        {
            // Covers: lines 837-839 - message starting with '['
            var exception = new InvalidOperationException("[Already formatted] some detail");
            var result = new ServiceResult(exception, StatusCodes.BadUnexpectedError);

            Assert.That(result.LocalizedText.Text, Is.EqualTo("[Already formatted] some detail"));
        }

        [Test]
        public void GetDefaultMessageWithNullExceptionThrows()
        {
            // Covers: lines 825-828 - null exception path in GetDefaultMessage
            // Also covers: BuildExceptionTrace throws ArgumentNullException for null
            // The constructor that calls GetDefaultMessage then calls BuildExceptionTrace,
            // which throws because AppendException does not accept null.
            Assert.That(() => new ServiceResult(
                (Exception)null!,
                "http://ns.org",
                StatusCodes.BadUnexpectedError),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void GetDefaultMessageWithAggregateExceptionSingleInnerUnwraps()
        {
            // Covers: lines 830-833 - AggregateException with single inner
            var inner = new InvalidOperationException("inner message");
            var aggregate = new AggregateException(inner);
            var result = new ServiceResult(aggregate, StatusCodes.BadUnexpectedError);

            // Should unwrap and show InvalidOperationException, not AggregateException
            Assert.That(result.LocalizedText.Text, Does.Contain("InvalidOperationException"));
        }
        #endregion
    }
}
