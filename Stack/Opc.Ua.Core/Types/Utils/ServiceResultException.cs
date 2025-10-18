/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// An exception thrown when a UA defined error occurs.
    /// </summary>
    [DataContract]
    [Serializable]
    public class ServiceResultException : Exception
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServiceResultException()
            : base(Strings.DefaultMessage)
        {
            Result = StatusCodes.Bad;
        }

        /// <summary>
        /// Initializes the exception with a message.
        /// </summary>
        public ServiceResultException(string message)
            : base(message)
        {
            Result = StatusCodes.Bad;
        }

        /// <summary>
        /// Initializes the exception with a message and an exception.
        /// </summary>
        public ServiceResultException(Exception e, uint defaultCode)
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
            Result = StatusCodes.Bad;
        }

        /// <summary>
        /// Initializes the exception with a status code.
        /// </summary>
        public ServiceResultException(uint statusCode)
            : base(GetMessage(statusCode))
        {
            Result = new ServiceResult(statusCode);
        }

        /// <summary>
        /// Initializes the exception with a status code and a message.
        /// </summary>
        public ServiceResultException(uint statusCode, string message)
            : base(message)
        {
            Result = new ServiceResult(statusCode, message);
        }

        /// <summary>
        /// Initializes the exception with a status code and an inner exception.
        /// </summary>
        public ServiceResultException(uint statusCode, Exception e)
            : base(GetMessage(statusCode), e)
        {
            Result = new ServiceResult(statusCode, e);
        }

        /// <summary>
        /// Initializes the exception with a status code, a message and an inner exception.
        /// </summary>
        public ServiceResultException(uint statusCode, string message, Exception e)
            : base(message, e)
        {
            Result = new ServiceResult(statusCode, message, e);
        }

        /// <summary>
        /// Initializes the exception with a Result object.
        /// </summary>
        public ServiceResultException(ServiceResult status)
            : base(GetMessage(status))
        {
            Result = status ?? new ServiceResult(StatusCodes.Bad);
        }

        /// <summary>
        /// The identifier for the status code.
        /// </summary>
        public uint StatusCode => Result.Code;

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
        /// Returns the status result associated with the exception.
        /// </summary>
        public ServiceResult Result { get; }

        /// <summary>
        /// Nested error information.
        /// </summary>
        public ServiceResult InnerResult => Result.InnerResult;

        /// <summary>
        /// Returns a formatted string with the contents of exception.
        /// </summary>
        public string ToLongString()
        {
            var buffer = new StringBuilder();
            buffer.AppendLine(Message);
            Result.AppendLong(buffer);
            return buffer.ToString();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToLongString();
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

            return new ServiceResultException(code, Utils.Format(format, args));
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

            return new ServiceResultException(code, Utils.Format(format, args), e);
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
                Utils.Format(format, args);
#if DEBUGCHK
            Debug.Fail(message);
#endif
            Debug.WriteLine($"{message}\n{new StackTrace()}");
#endif
            if (format == null)
            {
                return new ServiceResultException(
                    StatusCodes.BadUnexpectedError);
            }
            return new ServiceResultException(
                StatusCodes.BadUnexpectedError,
                Utils.Format(format, args));
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
