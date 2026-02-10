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
using System.Text;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// An exception thrown when a UA defined error occurs.
    /// </summary>
    public class ServiceResultException : Exception
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServiceResultException()
            : base(Strings.DefaultMessage)
        {
            Result = ServiceResult.Bad;
        }

        /// <summary>
        /// Initializes the exception with a message.
        /// </summary>
        public ServiceResultException(string message)
            : base(message)
        {
            Result = ServiceResult.Bad;
        }

        /// <summary>
        /// Initializes the exception with a message and an exception.
        /// </summary>
        public ServiceResultException(Exception e, StatusCode defaultCode)
            : base(e.Message, e)
        {
            Result = ServiceResult.Create(e, defaultCode, string.Empty);
        }

        /// <summary>
        /// Initializes the exception with a message and an exception.
        /// </summary>
        public ServiceResultException(string message, Exception e)
            : base(message, e)
        {
            Result = ServiceResult.Bad;
        }

        /// <summary>
        /// Initializes the exception with a status code.
        /// </summary>
        public ServiceResultException(StatusCode statusCode)
            : base(GetMessage(statusCode))
        {
            Result = new ServiceResult(statusCode);
        }

        /// <summary>
        /// Initializes the exception with a status code and a message.
        /// </summary>
        public ServiceResultException(StatusCode statusCode, string message)
            : base(message)
        {
            Result = new ServiceResult(statusCode, message);
        }

        /// <summary>
        /// Initializes the exception with a status code and an inner exception.
        /// </summary>
        public ServiceResultException(StatusCode statusCode, Exception e)
            : base(GetMessage(statusCode), e)
        {
            Result = new ServiceResult(statusCode, e);
        }

        /// <summary>
        /// Initializes the exception with a status code, a message and an inner exception.
        /// </summary>
        public ServiceResultException(StatusCode statusCode, string message, Exception e)
            : base(message, e)
        {
            Result = new ServiceResult(message, statusCode, e);
        }

        /// <summary>
        /// Initializes the exception with a Result object.
        /// </summary>
        public ServiceResultException(ServiceResult status)
            : base(GetMessage(status), status.InnerResult != null ? status.InnerResult.GetServiceResultException() : null)
        {
            Result = status ?? ServiceResult.Bad;
        }

        /// <summary>
        /// The identifier for the status code.
        /// </summary>
        public uint StatusCode => Result.StatusCode.Code;

        /// <summary>
        /// The identifier for the status code.
        /// </summary>
        public StatusCode Code => Result.StatusCode;

        /// <summary>
        /// The namespace that qualifies symbolic identifier.
        /// </summary>
        public string NamespaceUri => Result.NamespaceUri;

        /// <summary>
        /// The qualified name of the symbolic identifier associated with the status code.
        /// </summary>
        public string SymbolicId => Result.SymbolicId;

        /// <summary>
        /// The localized description for the status code.
        /// </summary>
        public LocalizedText LocalizedText => Result.LocalizedText;

        /// <summary>
        /// Additional diagnostic/debugging information associated with the operation.
        /// </summary>
        public string AdditionalInfo => Result.AdditionalInfo;

        /// <summary>
        /// Nested error information.
        /// </summary>
        public ServiceResult InnerResult => Result.InnerResult;

        /// <summary>
        /// Returns the status result associated with the exception.
        /// </summary>
        public ServiceResult Result { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            var buffer = new StringBuilder();
            buffer.AppendLine(Message);
            Result.Append(buffer);
            return buffer.ToString();
        }

        /// <summary>
        /// Creates a new instance of a ServiceResultException
        /// </summary>
        public static ServiceResultException Create(uint code, string format, params object[] args)
        {
            if (format == null)
            {
                return new ServiceResultException(code);
            }

            return new ServiceResultException(code, CoreUtils.Format(format, args));
        }

        /// <summary>
        /// Creates a new instance of a ServiceResultException
        /// </summary>
        public static ServiceResultException Create(
            uint code,
            Exception e,
            string format,
            params object[] args)
        {
            if (format == null)
            {
                return new ServiceResultException(code, e);
            }

            return new ServiceResultException(code, CoreUtils.Format(format, args), e);
        }

        /// <summary>
        /// Creates a new instance of a ServiceResultException
        /// </summary>
        public static ServiceResultException Create(
            StatusCode code,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            IList<string> stringTable)
        {
            return new ServiceResultException(
                new ServiceResult(code, index, diagnosticInfos, stringTable));
        }

        /// <summary>
        /// Unexpected error occurred
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static ServiceResultException Unexpected(
            string format,
            params object[] args)
        {
#if DEBUG
            string message = format == null ?
                "An unexpected error occurred" :
                CoreUtils.Format(format, args);
#if DEBUGCHK
            System.Diagnostics.Debug.Fail(message);
#endif
            System.Diagnostics.Debug.WriteLine($"{message}\n{new System.Diagnostics.StackTrace()}");
#endif
            if (format == null)
            {
                return new ServiceResultException(
                    StatusCodes.BadUnexpectedError);
            }
            return new ServiceResultException(
                StatusCodes.BadUnexpectedError,
                CoreUtils.Format(format, args));
        }

        /// <summary>
        /// Extracts an exception message from a Result object.
        /// </summary>
        private static string GetMessage(ServiceResult status)
        {
            if (status == null)
            {
                return Strings.DefaultMessage;
            }

            return status.ToString();
        }

        /// <summary>
        /// Wraps string constants defined in the class.
        /// </summary>
        private static class Strings
        {
            public const string DefaultMessage = "A UA specific error occurred.";
        }
    }
}
