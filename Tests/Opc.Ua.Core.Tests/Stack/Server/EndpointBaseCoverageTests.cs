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
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EndpointBaseCoverageTests
    {
        private ILogger m_logger;

        [SetUp]
        public void SetUp()
        {
            m_logger = new LoggerFactory().CreateLogger("Test");
        }

        [Test]
        public void CreateFaultWithServiceResultException()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    RequestHandle = 42,
                    ReturnDiagnostics = (uint)DiagnosticsMasks.All
                }
            };
            var exception = new ServiceResultException(StatusCodes.BadNotFound, "Item not found");

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault, Is.Not.Null);
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(42u));
            Assert.That(fault.ResponseHeader.Timestamp, Is.Not.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void CreateFaultWithGenericException()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    RequestHandle = 100
                }
            };
            var exception = new InvalidOperationException("Something went wrong");

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault, Is.Not.Null);
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public void CreateFaultWithNullRequest()
        {
            var exception = new ServiceResultException(StatusCodes.BadSessionClosed);
            var fault = EndpointBase.CreateFault(m_logger, null, exception);

            Assert.That(fault, Is.Not.Null);
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadSessionClosed));
        }

        [Test]
        public void CreateFaultWithBadUnexpectedError()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            };
            var exception = new ServiceResultException(StatusCodes.BadUnexpectedError, "Unexpected");

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault, Is.Not.Null);
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public void CreateFaultWithBadNoSubscription()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            };
            var exception = new ServiceResultException(StatusCodes.BadNoSubscription);

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public void CreateFaultWithBadSecurityChecksFailed()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            };
            var exception = new ServiceResultException(StatusCodes.BadSecurityChecksFailed);

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void CreateFaultWithBadCertificateInvalid()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            };
            var exception = new ServiceResultException(StatusCodes.BadCertificateInvalid);

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public void CreateFaultWithBadServerHalted()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            };
            var exception = new ServiceResultException(StatusCodes.BadServerHalted);

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerHalted));
        }

        [Test]
        public void CreateFaultDiagnosticsIncluded()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    RequestHandle = 1,
                    ReturnDiagnostics = (uint)DiagnosticsMasks.All
                }
            };
            var exception = new ServiceResultException(StatusCodes.BadNotFound, "Not found");

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault.ResponseHeader.ServiceDiagnostics, Is.Not.Null);
            Assert.That(fault.ResponseHeader.StringTable.IsNull, Is.False);
        }

        [Test]
        public void TryExtractActivityContextFromNullParametersReturnsFalse()
        {
            bool result = EndpointBase.TryExtractActivityContextFromParameters(null, out ActivityContext context);
            Assert.That(result, Is.False);
            Assert.That(context, Is.EqualTo(default(ActivityContext)));
        }

        [Test]
        public void TryExtractActivityContextFromEmptyParametersReturnsFalse()
        {
            var parameters = new AdditionalParametersType();
            bool result = EndpointBase.TryExtractActivityContextFromParameters(parameters, out ActivityContext context);
            Assert.That(result, Is.False);
            Assert.That(context, Is.EqualTo(default(ActivityContext)));
        }

        [Test]
        public void TryExtractActivityContextWithNonSpanContextKey()
        {
            var parameters = new AdditionalParametersType
            {
                Parameters = [
                    new Opc.Ua.KeyValuePair
                    {
                        Key = new QualifiedName("SomeOtherKey"),
                        Value = Variant.From(42)
                    }
                ]
            };
            bool result = EndpointBase.TryExtractActivityContextFromParameters(parameters, out ActivityContext context);
            Assert.That(result, Is.False);
        }

        [Test]
        public void TryExtractActivityContextWithInvalidSpanContextValue()
        {
            var parameters = new AdditionalParametersType
            {
                Parameters = [
                    new Opc.Ua.KeyValuePair
                    {
                        Key = new QualifiedName("SpanContext"),
                        Value = Variant.From("not a span context")
                    }
                ]
            };
            bool result = EndpointBase.TryExtractActivityContextFromParameters(parameters, out ActivityContext context);
            Assert.That(result, Is.False);
        }

        [Test]
        public void CreateFaultWithAggregateException()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 10 }
            };
            var exception = new AggregateException("Multiple errors",
                new InvalidOperationException("Error 1"),
                new ArgumentException("Error 2"));

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            Assert.That(fault, Is.Not.Null);
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        [Test]
        public void CreateFaultWithNullException()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            };

            Assert.Throws<NullReferenceException>(() => EndpointBase.CreateFault(m_logger, request, null));
        }

        [Test]
        public void CreateFaultTimestampIsRecent()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            };
            var exception = new ServiceResultException(StatusCodes.BadNotFound);
            var before = DateTime.UtcNow;

            var fault = EndpointBase.CreateFault(m_logger, request, exception);

            var after = DateTime.UtcNow;
            Assert.That((DateTime)fault.ResponseHeader.Timestamp >= before, Is.True);
            Assert.That((DateTime)fault.ResponseHeader.Timestamp <= after, Is.True);
        }
    }
}
