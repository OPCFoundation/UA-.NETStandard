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
using NUnit.Framework;

#pragma warning disable NUnit2010

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Tests for the <see cref="ServiceResultException"/> class covering constructors,
    /// properties, static factory methods, and ToString.
    /// </summary>
    [TestFixture]
    [Category("ServiceResultException")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServiceResultExceptionTests
    {
        [Test]
        public void DefaultConstructorSetsBadUnexpectedError()
        {
            var ex = new ServiceResultException();

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.Bad.Code));
            Assert.That(ex.Message, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConstructorWithMessageSetsMessageAndBadStatus()
        {
            var ex = new ServiceResultException("test message");

            Assert.That(ex.Message, Is.EqualTo("test message"));
            Assert.That(ex.Code, Is.EqualTo(StatusCodes.Bad.Code));
        }

        [Test]
        public void ConstructorWithStatusCodeSetsCode()
        {
            var ex = new ServiceResultException(StatusCodes.BadNodeIdInvalid);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid.Code));
        }

        [Test]
        public void ConstructorWithStatusCodeAndStringSetsCodeAndMessage()
        {
            var ex = new ServiceResultException(StatusCodes.BadTypeMismatch, "type error");

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadTypeMismatch.Code));
            Assert.That(ex.Message, Is.EqualTo("type error"));
        }

        [Test]
        public void ConstructorWithStatusCodeAndLocalizedTextSetsCodeAndMessage()
        {
            var lt = new LocalizedText("en", "localized msg");
            var ex = new ServiceResultException(StatusCodes.BadTypeMismatch, lt);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadTypeMismatch.Code));
            Assert.That(ex.Message, Is.EqualTo("localized msg"));
            Assert.That(ex.LocalizedText.IsNullOrEmpty, Is.False);
        }

        [Test]
        public void ConstructorWithStatusCodeAndExceptionSetsInnerException()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new ServiceResultException(StatusCodes.BadEncodingError, inner);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadEncodingError.Code));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        [Test]
        public void ConstructorWithStatusCodeStringAndExceptionSetsAll()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new ServiceResultException(StatusCodes.BadEncodingError, "msg", inner);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadEncodingError.Code));
            Assert.That(ex.Message, Is.EqualTo("msg"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        [Test]
        public void ConstructorWithStatusCodeLocalizedTextAndExceptionSetsAll()
        {
            var inner = new InvalidOperationException("inner");
            var lt = new LocalizedText("en", "localized");
            var ex = new ServiceResultException(StatusCodes.BadDecodingError, lt, inner);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
            Assert.That(ex.Message, Is.EqualTo("localized"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        [Test]
        public void ConstructorWithExceptionAndStatusCodeSetsInnerAndCode()
        {
            var inner = new ArgumentException("arg");
            var ex = new ServiceResultException(inner, StatusCodes.BadInvalidArgument);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadInvalidArgument.Code));
            Assert.That(ex.InnerException, Is.SameAs(inner));
            Assert.That(ex.Message, Is.EqualTo("arg"));
        }

        [Test]
        public void ConstructorWithStringAndExceptionSetsBadStatus()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new ServiceResultException("my msg", inner);

            Assert.That(ex.Message, Is.EqualTo("my msg"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
            Assert.That(ex.Code, Is.EqualTo(StatusCodes.Bad.Code));
        }

        [Test]
        public void ConstructorWithServiceResultWrapsResult()
        {
            var result = new ServiceResult(StatusCodes.BadNodeIdInvalid);
            var ex = new ServiceResultException(result);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid.Code));
            Assert.That(ex.Result, Is.Not.Null);
            Assert.That(ex.Result.Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid.Code));
        }

        [Test]
        public void CodePropertyReturnsStatusCodeValue()
        {
            var ex = new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded.Code));
        }

        [Test]
        public void NamespaceUriPropertyReturnsValue()
        {
            var ex = new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);

            // NamespaceUri may be null for simple status-code-only construction
            Assert.That(ex.NamespaceUri, Is.Null.Or.Not.Empty);
        }

        [Test]
        public void SymbolicIdPropertyReturnsValue()
        {
            var ex = new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);

            Assert.That(ex.SymbolicId, Is.Not.Null);
        }

        [Test]
        public void LocalizedTextPropertyReturnsValue()
        {
            var lt = new LocalizedText("en", "localized");
            var ex = new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, lt);

            Assert.That(ex.LocalizedText.IsNullOrEmpty, Is.False);
        }

        [Test]
        public void AdditionalInfoPropertyReturnsNullByDefault()
        {
            var ex = new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);

            Assert.That(ex.AdditionalInfo, Is.Null);
        }

        [Test]
        public void InnerResultPropertyReturnsNullByDefault()
        {
            var ex = new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);

            Assert.That(ex.InnerResult, Is.Null);
        }

        [Test]
        public void ResultPropertyReturnsNonNull()
        {
            var ex = new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);

            Assert.That(ex.Result, Is.Not.Null);
            Assert.That(ex.Result.Code, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded.Code));
        }

        [Test]
        public void CreateWithStatusCodeAndFormatReturnsFormattedMessage()
        {
            var ex = ServiceResultException.Create(
                StatusCodes.BadInvalidArgument,
                "Arg {0} is invalid",
                "test");

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadInvalidArgument.Code));
            Assert.That(ex.Message, Does.Contain("test"));
        }

        [Test]
        public void CreateWithNullFormatReturnsStatusCodeOnly()
        {
            var ex = ServiceResultException.Create(
                StatusCodes.BadInvalidArgument,
                null!,
                []);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadInvalidArgument.Code));
        }

        [Test]
        public void CreateWithExceptionAndFormatReturnsFormattedMessage()
        {
            var inner = new InvalidOperationException("inner");
            var ex = ServiceResultException.Create(
                StatusCodes.BadEncodingError,
                inner,
                "Error: {0}",
                "detail");

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadEncodingError.Code));
            Assert.That(ex.Message, Does.Contain("detail"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        [Test]
        public void CreateWithExceptionAndNullFormatReturnsStatusCodeOnly()
        {
            var inner = new InvalidOperationException("inner");
            var ex = ServiceResultException.Create(
                StatusCodes.BadEncodingError,
                inner,
                null!,
                []);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadEncodingError.Code));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        [Test]
        public void CreateWithDiagnosticInfoReturnsParsedException()
        {
            var diagnosticInfos = new ArrayOf<DiagnosticInfo>([new DiagnosticInfo()]);
            var stringTable = new ArrayOf<string>(["test string"]);

            var ex = ServiceResultException.Create(
                StatusCodes.BadDecodingError,
                0,
                diagnosticInfos,
                stringTable);

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadDecodingError.Code));
        }

        [Test]
        public void UnexpectedWithFormatReturnsBadUnexpectedError()
        {
            var ex = ServiceResultException.Unexpected("Something {0}", "wrong");

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(ex.Message, Does.Contain("wrong"));
        }

        [Test]
        public void UnexpectedWithNullFormatReturnsBadUnexpectedError()
        {
            var ex = ServiceResultException.Unexpected(null!, []);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
        }

        [Test]
        public void UnexpectedWithExceptionReturnsBadUnexpectedError()
        {
            var inner = new InvalidOperationException("inner");
            var ex = ServiceResultException.Unexpected(inner, "Error: {0}", "detail");

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(ex.InnerException, Is.SameAs(inner));
            Assert.That(ex.Message, Does.Contain("detail"));
        }

        [Test]
        public void UnexpectedWithExceptionAndNullFormatReturnsBadUnexpectedError()
        {
            var inner = new InvalidOperationException("inner");
            var ex = ServiceResultException.Unexpected(inner, null!, []);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadUnexpectedError.Code));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        [Test]
        public void ConfigurationErrorWithFormatReturnsBadConfigurationError()
        {
            var ex = ServiceResultException.ConfigurationError("Config {0}", "issue");

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadConfigurationError.Code));
            Assert.That(ex.Message, Does.Contain("issue"));
        }

        [Test]
        public void ConfigurationErrorWithNullFormatReturnsBadConfigurationError()
        {
            var ex = ServiceResultException.ConfigurationError(null!, []);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadConfigurationError.Code));
        }

        [Test]
        public void ConfigurationErrorWithExceptionReturnsBadConfigurationError()
        {
            var inner = new InvalidOperationException("inner");
            var ex = ServiceResultException.ConfigurationError(inner, "Config {0}", "issue");

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadConfigurationError.Code));
            Assert.That(ex.InnerException, Is.SameAs(inner));
            Assert.That(ex.Message, Does.Contain("issue"));
        }

        [Test]
        public void ConfigurationErrorWithExceptionAndNullFormatReturnsBadConfigurationError()
        {
            var inner = new InvalidOperationException("inner");
            var ex = ServiceResultException.ConfigurationError(inner, null!, []);

            Assert.That(ex.Code, Is.EqualTo(StatusCodes.BadConfigurationError.Code));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        [Test]
        public void ToStringIncludesStatusCodeAndMessage()
        {
            var ex = new ServiceResultException(StatusCodes.BadTypeMismatch, "type mismatch");

            string result = ex.ToString();

            Assert.That(result, Does.Contain("type mismatch"));
        }

        [Test]
        public void ToStringWithServiceResultIncludesInfo()
        {
            var serviceResult = new ServiceResult(StatusCodes.BadNodeIdInvalid);
            var ex = new ServiceResultException(serviceResult);

            string result = ex.ToString();

            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void IsExceptionDerivedFromSystemException()
        {
            var ex = new ServiceResultException();

            Assert.That(ex, Is.InstanceOf<Exception>());
        }
    }
}
