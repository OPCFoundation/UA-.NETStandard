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
        [Test]
        public void GoodPropertyReturnsGoodStatusCode()
        {
            ServiceResult result = ServiceResult.Good;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good.Code));
        }

        [Test]
        public void BadPropertyReturnsBadStatusCode()
        {
            ServiceResult result = ServiceResult.Bad;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Bad.Code));
        }

        [Test]
        public void ConstructorWithStatusCodeSetsProperties()
        {
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
            var text = new LocalizedText("en", "Something failed");
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, text);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Something failed"));
        }

        [Test]
        public void ConstructorWithStatusCodeAndInnerResult()
        {
            var inner = new ServiceResult(StatusCodes.BadDecodingError);
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, inner);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.Not.Null);
            Assert.That(result.InnerResult!.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void ConstructorWithAllProperties()
        {
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
            var text = new LocalizedText("en", "error");
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var result = new ServiceResult(
                StatusCodes.BadUnexpectedError,
                (XmlQualifiedName?)null!,
                text);
#pragma warning restore IDE0004 // Remove Unnecessary Cast

            Assert.That(result.NamespaceUri, Is.Null);
        }

        [Test]
        public void CopyConstructorCopiesProperties()
        {
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
            var outer = new ServiceResult(StatusCodes.BadUnexpectedError);
            var inner = new ServiceResult(StatusCodes.BadDecodingError);
            var result = new ServiceResult(outer, inner);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.SameAs(inner));
        }

        [Test]
        public void ConstructorWithExceptionSetsDefaultBadCode()
        {
            var exception = new InvalidOperationException("InvalidOperationException");
            var result = new ServiceResult(exception);

            Assert.That(StatusCode.IsBad(result.StatusCode), Is.True);
            Assert.That(result.AdditionalInfo, Is.Not.Null.And.Not.Empty);
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void ConstructorWithExceptionAndDefaultCode()
        {
            var exception = new ArgumentException("ArgumentException");
            var result = new ServiceResult(exception, StatusCodes.BadUnexpectedError);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.AdditionalInfo, Does.Contain("ArgumentException"));
        }

        [Test]
        public void ConstructorWithExceptionAndNamespaceAndDefaultCode()
        {
            var exception = new InvalidOperationException("InvalidOperationException");
            var result = new ServiceResult(exception, "http://ns.org", StatusCodes.BadUnexpectedError);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void ConstructorWithExceptionDefaultCodeAndLocalizedText()
        {
            var exception = new InvalidOperationException("oops");
            var text = new LocalizedText("en", "localized error");
            var result = new ServiceResult(exception, StatusCodes.BadUnexpectedError, text);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("localized error"));
        }

        [Test]
        public void ConstructorWithExceptionAllParameters()
        {
            var exception = new InvalidOperationException("InvalidOperationException");
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
            var inner = new InvalidOperationException("inner error");
            var aggregate = new AggregateException(inner);

            var result = new ServiceResult(
                aggregate,
                null,
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "test"));
#if DEBUG
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
#else
            Assert.That(result.AdditionalInfo, Does.Contain("inner error"));
#endif
        }

        [Test]
        public void ConstructorWithAggregateExceptionMultipleInnerNotUnwrapped()
        {
            var inner1 = new InvalidOperationException("inner1");
            var inner2 = new ArgumentException("inner2");
            var aggregate = new AggregateException(inner1, inner2);

            var result = new ServiceResult(
                aggregate,
                null,
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "test"));

            // AggregateException with 2 inners should NOT be unwrapped
#if DEBUG
            Assert.That(result.AdditionalInfo, Does.Contain("AggregateException"));
#else
            Assert.That(result.AdditionalInfo, Does.Contain("One or more errors occurred."));
#endif
        }

        [Test]
        public void ConstructorWithCodeAndInnerException()
        {
            var exception = new InvalidOperationException("inner");
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, exception);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithCodeLocalizedTextAndInnerException()
        {
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
            var innerException = new ServiceResultException(StatusCodes.BadDecodingError);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var result = new ServiceResult(
                null,
                StatusCodes.BadDecodingError,
                LocalizedText.Null,
                (string?)null,
                (Exception)innerException);
#pragma warning restore IDE0004 // Remove Unnecessary Cast

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void ConstructorWithInnerExceptionDoesNotCollapseWhenNewInfoProvided()
        {
            var innerException = new ServiceResultException(StatusCodes.BadDecodingError);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var result = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                new LocalizedText("en", "new info"),
                "extra",
                (Exception)innerException);
#pragma warning restore IDE0004 // Remove Unnecessary Cast

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.EqualTo("http://ns.org"));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("new info"));
            Assert.That(result.AdditionalInfo, Is.EqualTo("extra"));
            Assert.That(result.InnerResult, Is.Not.Null);
        }

        [Test]
        public void ObsoleteConstructorWithUintCodeAndInnerResult()
        {
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

        [Test]
        public void ConstructorWithDiagnosticInfoSetsProperties()
        {
            var stringTable = new ArrayOf<string>(
                ["http://ns.org", "MySymbolicId", "en-US", "A localized message"]);
            var diagInfo = new DiagnosticInfo
            {
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
            var stringTable = new ArrayOf<string>(["unused"]);
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, (DiagnosticInfo?)null, stringTable);
#pragma warning restore IDE0004 // Remove Unnecessary Cast

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.Null);
            Assert.That(result.AdditionalInfo, Is.Null);
        }

        [Test]
        public void ConstructorWithDiagnosticInfoAndBadInnerStatusCreatesInnerResult()
        {
            var stringTable = new ArrayOf<string>(["http://ns.org", "SymId", "en", "text"]);
            var innerDiag = new DiagnosticInfo
            {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3
            };
            var diagInfo = new DiagnosticInfo
            {
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
            var stringTable = new ArrayOf<string>(["http://ns.org", "SymId", "en", "text"]);
            var diagInfo = new DiagnosticInfo
            {
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
            var stringTable = new ArrayOf<string>(
                ["http://ns.org", "SymId", "en", "indexed message"]);
            var diagInfo = new DiagnosticInfo
            {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3,
                AdditionalInfo = "indexed info"
            };
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>([diagInfo]);

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
            var stringTable = new ArrayOf<string>(["unused"]);
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>([new DiagnosticInfo()]);

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, 5, diagnosticInfos, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.Null);
        }

        [Test]
        public void ConstructorWithIndexedDiagnosticInfoNegativeIndexIgnored()
        {
            var stringTable = new ArrayOf<string>(["unused"]);
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>([new DiagnosticInfo()]);

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, -1, diagnosticInfos, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.Null);
        }

        [Test]
        public void ConstructorWithIndexedDiagnosticInfoNullEntryIgnored()
        {
            var stringTable = new ArrayOf<string>(["unused"]);
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>(new DiagnosticInfo?[] { null }!);

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, 0, diagnosticInfos, stringTable);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.NamespaceUri, Is.Null);
        }

        [Test]
        public void ConstructorWithIndexedDiagnosticInfoBadInnerStatusCreatesInnerResult()
        {
            var stringTable = new ArrayOf<string>(["http://ns.org", "SymId", "en", "text"]);
            var diagInfo = new DiagnosticInfo
            {
                NamespaceUri = 0,
                SymbolicId = 1,
                Locale = 2,
                LocalizedText = 3,
                InnerStatusCode = StatusCodes.BadDecodingError
            };
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>([diagInfo]);

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, 0, diagnosticInfos, stringTable);

            Assert.That(result.InnerResult, Is.Not.Null);
            Assert.That(result.InnerResult!.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void ConstructorWithDiagnosticInfoOutOfBoundsStringIndex()
        {
            var stringTable = new ArrayOf<string>(["only one"]);
            var diagInfo = new DiagnosticInfo
            {
                NamespaceUri = 99, // out of bounds
                SymbolicId = -1,   // negative
                Locale = 0,
                LocalizedText = 0
            };

            var result = new ServiceResult(StatusCodes.BadUnexpectedError, diagInfo, stringTable);

            Assert.That(result.NamespaceUri, Is.Null);
        }

        [Test]
        public void CreateWithStatusCodeAndNullTranslationReturnsCodeOnly()
        {
            var result = ServiceResult.Create(StatusCodes.BadUnexpectedError, TranslationInfo.Null);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.IsNullOrEmpty, Is.True);
        }

        [Test]
        public void CreateWithStatusCodeAndTranslation()
        {
            var translation = new TranslationInfo("key", "en", "error message");
            var result = ServiceResult.Create(StatusCodes.BadUnexpectedError, translation);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("error message"));
        }

        [Test]
        public void CreateWithExceptionAndNullTranslation()
        {
            var exception = new InvalidOperationException("test");
            var result = ServiceResult.Create(exception, TranslationInfo.Null, StatusCodes.BadUnexpectedError);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void CreateWithExceptionAndTranslation()
        {
            var exception = new InvalidOperationException("test");
            var translation = new TranslationInfo("key", "en", "translated error");
            var result = ServiceResult.Create(exception, translation, StatusCodes.BadUnexpectedError);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("translated error"));
        }

        [Test]
        public void CreateWithServiceResultExceptionUsesExceptionStatusCode()
        {
            var sre = new ServiceResultException(StatusCodes.BadDecodingError);
            var result = ServiceResult.Create(sre, TranslationInfo.Null, StatusCodes.BadUnexpectedError);

            // Should use the exception's status code, not the default
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void CreateWithFormatNullReturnsCodeOnly()
        {
            var result = ServiceResult.Create(StatusCodes.BadUnexpectedError, null!);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.IsNullOrEmpty, Is.True);
        }

        [Test]
        public void CreateWithFormatNoArgsReturnsFormatAsText()
        {
            var result = ServiceResult.Create(StatusCodes.BadUnexpectedError, "Simple message");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(result.LocalizedText.Text, Is.EqualTo("Simple message"));
        }

        [Test]
        public void CreateWithFormatAndArgsReturnsFormattedText()
        {
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
            var exception = new InvalidOperationException("test");
            var result = ServiceResult.Create(exception, StatusCodes.BadUnexpectedError, null!);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void CreateWithExceptionFormatEmptyReturnsCodeOnly()
        {
            var exception = new InvalidOperationException("test");
            var result = ServiceResult.Create(exception, StatusCodes.BadUnexpectedError, string.Empty);

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void CreateWithExceptionFormatNoArgsReturnsFormatAsText()
        {
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
            var sre = new ServiceResultException(StatusCodes.BadDecodingError);
            var result = ServiceResult.Create(
                sre,
                StatusCodes.BadUnexpectedError,
                "Error message");

            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void IsGoodWithGoodStatusReturnsTrue()
        {
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
            Assert.That(ServiceResult.IsGood(null!), Is.True);
        }

        [Test]
        public void IsNotGoodWithBadStatusReturnsTrue()
        {
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
            Assert.That(ServiceResult.IsNotGood(null!), Is.True);
        }

        [Test]
        public void IsUncertainWithUncertainStatusReturnsTrue()
        {
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
            Assert.That(ServiceResult.IsUncertain(null!), Is.False);
        }

        [Test]
        public void IsGoodOrUncertainWithGoodStatusReturnsTrue()
        {
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
            Assert.That(ServiceResult.IsGoodOrUncertain(null!), Is.False);
        }

        [Test]
        public void IsNotUncertainWithGoodStatusReturnsTrue()
        {
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
            Assert.That(ServiceResult.IsNotUncertain(null!), Is.True);
        }

        [Test]
        public void IsBadWithBadStatusReturnsTrue()
        {
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
            Assert.That(ServiceResult.IsBad(null!), Is.False);
        }

        [Test]
        public void IsNotBadWithGoodStatusReturnsTrue()
        {
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
            Assert.That(ServiceResult.IsNotBad(null!), Is.True);
        }

        [Test]
        public void ImplicitConversionFromStatusCode()
        {
            ServiceResult result = StatusCodes.BadUnexpectedError;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void ExplicitConversionToStatusCode()
        {
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            var statusCode = (StatusCode)result;
            Assert.That(statusCode.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void ExplicitConversionFromNullReturnsGood()
        {
            ServiceResult? nullResult = null;
            var statusCode = (StatusCode)nullResult!;
            Assert.That(StatusCode.IsGood(statusCode), Is.True);
        }

        [Test]
        public void LookupSymbolicIdDelegatesToStatusCode()
        {
            // Verify the obsolete wrapper returns same result as the StatusCode method
            uint code = StatusCodes.BadUnexpectedError.Code;
            string? fromServiceResult = ServiceResult.LookupSymbolicId(code);
            string? fromStatusCode = StatusCode.LookupSymbolicId(code);
            Assert.That(fromServiceResult, Is.EqualTo(fromStatusCode));
        }

        [Test]
        public void LookupSymbolicIdForUnknownCodeReturnsNull()
        {
            string? symbolicId = ServiceResult.LookupSymbolicId(0xDEAD0000);
            Assert.That(symbolicId, Is.Null);
        }

        [Test]
        public void GetServiceResultExceptionReturnsWrappingException()
        {
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            ServiceResultException exception = result.GetServiceResultException();

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception, Is.InstanceOf<ServiceResultException>());
            Assert.That(exception.Result.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void ToStringContainsStatusCodeHex()
        {
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            string str = result.ToString();
            Assert.That(str, Does.Contain("80010000"));
        }

        [Test]
        public void ToStringWithSymbolicIdContainsSymbolicId()
        {
            var result = new ServiceResult(StatusCodes.BadUnexpectedError);
            string str = result.ToString();
            Assert.That(str, Does.Contain("BadUnexpectedError"));
        }

        [Test]
        public void ToStringWithNamespaceAndSymbolicIdContainsBoth()
        {
            var symbolicId = new XmlQualifiedName("CustomError", "http://custom.org");
            var result = new ServiceResult(
                StatusCodes.BadUnexpectedError,
                symbolicId,
                LocalizedText.Null);
            string str = result.ToString();

            Assert.That(str, Does.Contain("http://custom.org"));
            Assert.That(str, Does.Contain("CustomError"));
        }

        [Test]
        public void ToStringWithLocalizedTextContainsText()
        {
            var text = new LocalizedText("en", "Something bad happened");
            var result = new ServiceResult(StatusCodes.BadUnexpectedError, text);
            string str = result.ToString();

            Assert.That(str, Does.Contain("Something bad happened"));
        }

        [Test]
        public void ToStringWithAdditionalInfoContainsInfo()
        {
            var result = new ServiceResult(
                "http://ns.org",
                StatusCodes.BadUnexpectedError,
                LocalizedText.Null,
                "Debug trace info here");
            string str = result.ToString();

            Assert.That(str, Does.Contain("Debug trace info here"));
        }

        [Test]
        public void ToStringWithSubCodeIncludesSubCode()
        {
            uint codeWithSubCode = StatusCodes.BadUnexpectedError.Code | 0x0001;
            var result = new ServiceResult(new StatusCode(codeWithSubCode));
            string str = result.ToString();

            Assert.That(str, Does.Contain("[0001]"));
        }

        [Test]
        public void SymbolicIdPropertySetViaConstructor()
        {
            // The setter is invoked via the DiagnosticInfo constructor path
            var stringTable = new ArrayOf<string>(["ns", "MyId", "en", "text"]);
            var diagInfo = new DiagnosticInfo
            {
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

        [Test]
        public void BuildExceptionTraceIncludesExceptionType()
        {
            var exception = new InvalidOperationException("test error message");
            var result = new ServiceResult(exception);
#if DEBUG
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
#else
            Assert.That(result.AdditionalInfo, Does.Contain("test error message"));
#endif
        }

        [Test]
        public void BuildExceptionTraceIncludesNestedExceptions()
        {
            var innerEx = new ArgumentException("inner cause");
            var outerEx = new InvalidOperationException("outer error", innerEx);
            var result = new ServiceResult(outerEx);
#if DEBUG
            Assert.That(result.AdditionalInfo, Does.Contain("InvalidOperationException"));
            Assert.That(result.AdditionalInfo, Does.Contain("ArgumentException"));
#else
            Assert.That(result.AdditionalInfo, Does.Contain("outer error"));
#endif
        }

        [Test]
        public void GetDefaultMessageFormatsExceptionTypeName()
        {
            var exception = new InvalidOperationException("simple message");
            var result = new ServiceResult(exception, StatusCodes.BadUnexpectedError);

            Assert.That(result.LocalizedText.Text, Does.Contain("InvalidOperationException"));
        }

        [Test]
        public void GetDefaultMessageWithBracketPrefixReturnsAsIs()
        {
            var exception = new InvalidOperationException("[Already formatted] some detail");
            var result = new ServiceResult(exception, StatusCodes.BadUnexpectedError);

            Assert.That(result.LocalizedText.Text, Is.EqualTo("[Already formatted] some detail"));
        }

        [Test]
        public void GetDefaultMessageWithNullExceptionThrows()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(() => new ServiceResult(
                (Exception)null!,
                "http://ns.org",
                StatusCodes.BadUnexpectedError),
                Throws.Nothing);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void GetDefaultMessageWithAggregateExceptionSingleInnerUnwraps()
        {
            var inner = new InvalidOperationException("inner message");
            var aggregate = new AggregateException(inner);
            var result = new ServiceResult(aggregate, StatusCodes.BadUnexpectedError);

            // Should unwrap and show InvalidOperationException, not AggregateException
            Assert.That(result.LocalizedText.Text, Does.Contain("InvalidOperationException"));
        }
    }
}
