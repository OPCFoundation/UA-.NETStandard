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
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// A class that combines the status code and diagnostic info structures.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ServiceResult
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        private ServiceResult()
        {
            Code = StatusCodes.Good;
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText,
            string additionalInfo,
            ServiceResult innerResult)
        {
            StatusCode = code;
            SymbolicId = symbolicId;
            NamespaceUri = namespaceUri;
            LocalizedText = localizedText;
            AdditionalInfo = additionalInfo;
            InnerResult = innerResult;
        }

        /// <summary>
        /// Copy constructor taking an inner result as second argument, to build chains of service results.
        /// </summary>
        public ServiceResult(ServiceResult outerResult, ServiceResult innerResult = null)
            : this(
                outerResult.Code,
                outerResult.SymbolicId,
                outerResult.NamespaceUri,
                outerResult.LocalizedText,
                outerResult.AdditionalInfo,
                innerResult)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(StatusCode code, ServiceResult innerResult)
            : this(code, null, null, null, null, innerResult)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText,
            string additionalInfo)
            : this(
                code,
                symbolicId,
                namespaceUri,
                localizedText,
                additionalInfo,
                (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText)
            : this(
                  code,
                  symbolicId,
                  namespaceUri,
                  localizedText,
                  null,
                  (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri)
            : this(
                  code,
                  symbolicId,
                  namespaceUri,
                  (string)null,
                  null,
                  (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            XmlQualifiedName symbolicId,
            LocalizedText localizedText)
            : this(
                code,
                symbolicId?.Name,
                symbolicId?.Namespace,
                localizedText,
                null,
                (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(StatusCode code, LocalizedText localizedText)
            : this(code, null, null, localizedText, null, (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object from a StatusCode.
        /// </summary>
        public ServiceResult(uint code)
            : this(new StatusCode(code))
        {
        }

        /// <summary>
        /// Constructs an object from a StatusCode.
        /// </summary>
        public ServiceResult(StatusCode status)
        {
            Code = status.Code;
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the inner result.
        /// </remarks>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText,
            string additionalInfo,
            Exception innerException)
        {
            var innerResult = new ServiceResult(innerException);

            // check if no new information provided.
            if (code.Code == innerResult.Code &&
                symbolicId == null &&
                localizedText == null &&
                additionalInfo == null)
            {
                Code = innerResult.Code;
                SymbolicId = innerResult.SymbolicId;
                NamespaceUri = innerResult.NamespaceUri;
                LocalizedText = innerResult.LocalizedText;
                AdditionalInfo = innerResult.AdditionalInfo;
                InnerResult = innerResult.InnerResult;
            }
            // make the exception the inner result.
            else
            {
                Code = code.Code;
                SymbolicId = symbolicId;
                NamespaceUri = namespaceUri;
                LocalizedText = localizedText;
                AdditionalInfo = additionalInfo;
                InnerResult = innerResult;
            }
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText,
            Exception innerException)
            : this(code, symbolicId, namespaceUri, localizedText, null, innerException)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            Exception innerException)
            : this(code, symbolicId, namespaceUri, null, null, innerException)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(StatusCode code, LocalizedText localizedText, Exception innerException)
            : this(code, null, null, localizedText, null, innerException)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(StatusCode code, Exception innerException)
            : this(code, null, null, null, null, innerException)
        {
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        /// <remarks>
        /// The code, symbolicId, namespaceUri and localizedText parameters are ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception e,
            uint defaultCode,
            string defaultSymbolicId,
            string defaultNamespaceUri,
            LocalizedText defaultLocalizedText)
        {
            if (e is ServiceResultException sre)
            {
                Code = sre.StatusCode;
                NamespaceUri = sre.NamespaceUri;
                SymbolicId = sre.SymbolicId;
                LocalizedText = sre.LocalizedText;
                InnerResult = sre.Result.InnerResult;

#if !DEBUGX
                if (LocalizedText.IsNullOrEmpty(LocalizedText))
#endif
                {
                    LocalizedText = defaultLocalizedText;
                }
            }
            else
            {
                Code = defaultCode;
                SymbolicId = defaultSymbolicId;
                NamespaceUri = defaultNamespaceUri;
                LocalizedText = defaultLocalizedText;
            }

            AdditionalInfo = BuildExceptionTrace(e);
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        /// <remarks>
        /// The defaultCode and defaultLocalizedText parameters are ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception exception,
            uint defaultCode,
            LocalizedText defaultLocalizedText)
            : this(exception, defaultCode, null, null, defaultLocalizedText)
        {
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        /// <remarks>
        /// The code, symbolicId and namespaceUri parameters are ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception exception,
            uint defaultCode,
            string defaultSymbolicId,
            string defaultNamespaceUri)
            : this(
                  exception,
                  defaultCode,
                  defaultSymbolicId,
                  defaultNamespaceUri,
                  GetDefaultMessage(exception, defaultCode))
        {
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        /// <remarks>
        /// The code parameter is ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(Exception exception, uint defaultCode)
            : this(exception, defaultCode, null, null)
        {
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        public ServiceResult(Exception exception)
            : this(exception, StatusCodes.Bad)
        {
        }

        /// <summary>
        /// Initializes the object with a status code and a diagnostic info structure.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            DiagnosticInfo diagnosticInfo,
            IList<string> stringTable)
        {
            Code = (uint)code;

            if (diagnosticInfo != null)
            {
                NamespaceUri = LookupString(stringTable, diagnosticInfo.NamespaceUri);
                SymbolicId = LookupString(stringTable, diagnosticInfo.SymbolicId);

                string locale = LookupString(stringTable, diagnosticInfo.Locale);
                string localizedText = LookupString(stringTable, diagnosticInfo.LocalizedText);
                LocalizedText = new LocalizedText(locale, localizedText);

                AdditionalInfo = diagnosticInfo.AdditionalInfo;

                if (!StatusCode.IsGood(diagnosticInfo.InnerStatusCode))
                {
                    InnerResult = new ServiceResult(
                        diagnosticInfo.InnerStatusCode,
                        diagnosticInfo.InnerDiagnosticInfo,
                        stringTable);
                }
            }
        }

        /// <summary>
        /// Initializes the object with a status code and a diagnostic info structure.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            int index,
            DiagnosticInfoCollection diagnosticInfos,
            IList<string> stringTable)
        {
            Code = (uint)code;

            if (index >= 0 && diagnosticInfos != null && index < diagnosticInfos.Count)
            {
                DiagnosticInfo diagnosticInfo = diagnosticInfos[index];

                if (diagnosticInfo != null)
                {
                    NamespaceUri = LookupString(stringTable, diagnosticInfo.NamespaceUri);
                    SymbolicId = LookupString(stringTable, diagnosticInfo.SymbolicId);

                    string locale = LookupString(stringTable, diagnosticInfo.Locale);
                    string localizedText = LookupString(stringTable, diagnosticInfo.LocalizedText);
                    LocalizedText = new LocalizedText(locale, localizedText);

                    AdditionalInfo = diagnosticInfo.AdditionalInfo;

                    if (!StatusCode.IsGood(diagnosticInfo.InnerStatusCode))
                    {
                        InnerResult = new ServiceResult(
                            diagnosticInfo.InnerStatusCode,
                            diagnosticInfo.InnerDiagnosticInfo,
                            stringTable);
                    }
                }
            }
        }

        /// <summary>
        /// A result representing a good status.
        /// </summary>
        public static ServiceResult Good { get; } = new ServiceResult();

        /// <summary>
        /// Creates a new instance of a ServiceResult
        /// </summary>
        public static ServiceResult Create(uint code, TranslationInfo translation)
        {
            if (translation == null)
            {
                return new ServiceResult(code);
            }

            return new ServiceResult(code, new LocalizedText(translation));
        }

        /// <summary>
        /// Creates a new instance of a ServiceResult
        /// </summary>
        public static ServiceResult Create(
            Exception e,
            TranslationInfo translation,
            uint defaultCode)
        {
            // replace the default code with the one from the exception.

            if (e is ServiceResultException sre)
            {
                defaultCode = sre.StatusCode;
            }

            if (translation == null)
            {
                return new ServiceResult(e, defaultCode);
            }

            return new ServiceResult(defaultCode, new LocalizedText(translation), e);
        }

        /// <summary>
        /// Creates a new instance of a ServiceResult
        /// </summary>
        public static ServiceResult Create(uint code, string format, params object[] args)
        {
            if (format == null)
            {
                return new ServiceResult(code);
            }

            if (args == null || args.Length == 0)
            {
                return new ServiceResult(code, format);
            }

            return new ServiceResult(code, Utils.Format(format, args));
        }

        /// <summary>
        /// Creates a new instance of a ServiceResult
        /// </summary>
        public static ServiceResult Create(
            Exception e,
            uint defaultCode,
            string format,
            params object[] args)
        {
            // replace the default code with the one from the exception.

            if (e is ServiceResultException sre)
            {
                defaultCode = sre.StatusCode;
            }

            if (format == null)
            {
                return new ServiceResult(e, defaultCode);
            }

            if (args == null || args.Length == 0)
            {
                return new ServiceResult(defaultCode, format, e);
            }

            return new ServiceResult(defaultCode, Utils.Format(format, args), e);
        }

        /// <summary>
        /// Returns true if the status code is good.
        /// </summary>
        public static bool IsGood(ServiceResult status)
        {
            if (status != null)
            {
                return StatusCode.IsGood(status.Code);
            }

            return true;
        }

        /// <summary>
        /// Returns true if the status is bad or uncertain.
        /// </summary>
        public static bool IsNotGood(ServiceResult status)
        {
            if (status != null)
            {
                return StatusCode.IsNotGood(status.Code);
            }

            return true;
        }

        /// <summary>
        /// Returns true if the status code is uncertain.
        /// </summary>
        public static bool IsUncertain(ServiceResult status)
        {
            if (status != null)
            {
                return StatusCode.IsUncertain(status.Code);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the status code is good or uncertain.
        /// </summary>
        public static bool IsGoodOrUncertain(ServiceResult status)
        {
            if (status != null)
            {
                return StatusCode.IsGood(status.Code) || StatusCode.IsUncertain(status.Code);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        public static bool IsNotUncertain(ServiceResult status)
        {
            if (status != null)
            {
                return StatusCode.IsNotUncertain(status.Code);
            }

            return true;
        }

        /// <summary>
        /// Returns true if the status code is bad.
        /// </summary>
        public static bool IsBad(ServiceResult status)
        {
            if (status != null)
            {
                return StatusCode.IsBad(status.Code);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the status is good or uncertain.
        /// </summary>
        public static bool IsNotBad(ServiceResult status)
        {
            if (status != null)
            {
                return StatusCode.IsNotBad(status.Code);
            }

            return true;
        }

        /// <summary>
        /// Converts a 32-bit code a ServiceResult object.
        /// </summary>
        public static implicit operator ServiceResult(uint code)
        {
            return new ServiceResult(code);
        }

        /// <summary>
        /// Converts a StatusCode a ServiceResult object.
        /// </summary>
        public static implicit operator ServiceResult(StatusCode code)
        {
            return new ServiceResult(code);
        }

        /// <summary>
        /// Converts a StatusCode object to a 32-bit code.
        /// </summary>
        public static explicit operator uint(ServiceResult status)
        {
            if (status == null)
            {
                return StatusCodes.Good;
            }

            return status.Code;
        }

        /// <summary>
        /// Looks up the symbolic name for a status code.
        /// </summary>
        public static string LookupSymbolicId(uint code)
        {
            return StatusCodes.GetBrowseName(code & 0xFFFF0000);
        }

        /// <summary>
        /// The status code associated with the result.
        /// </summary>
        public uint Code { get; private set; }

        /// <summary>
        /// The status code associated with the result.
        /// </summary>
        [DataMember(Order = 1)]
        public StatusCode StatusCode
        {
            get => Code;
            private set => Code = value.Code;
        }

        /// <summary>
        /// The namespace that qualifies symbolic identifier.
        /// </summary>
        [DataMember(Order = 2)]
        public string NamespaceUri { get; private set; }

        /// <summary>
        /// The qualified name of the symbolic identifier associated with the status code.
        /// </summary>
        [DataMember(Order = 3)]
        public string SymbolicId { get; private set; }

        /// <summary>
        /// The localized description for the status code.
        /// </summary>
        [DataMember(Order = 4)]
        public LocalizedText LocalizedText { get; private set; }

        /// <summary>
        /// Additional diagnostic/debugging information associated with the operation.
        /// </summary>
        [DataMember(Order = 5)]
        public string AdditionalInfo { get; private set; }

        /// <summary>
        /// Nested error information.
        /// </summary>
        [DataMember(Order = 6)]
        public ServiceResult InnerResult { get; private set; }

        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        public override string ToString()
        {
            var buffer = new StringBuilder();
            Append(buffer);

            return buffer.ToString();
        }

        /// <summary>
        /// Returns a formatted string with the contents of service result.
        /// </summary>
        public string ToLongString()
        {
            var buffer = new StringBuilder();
            AppendLong(buffer);
            return buffer.ToString();
        }

        /// <summary>
        /// Append to buffer
        /// </summary>
        /// <param name="buffer"></param>
        internal void Append(StringBuilder buffer)
        {
            buffer.Append(LookupSymbolicId(Code));

            if (!string.IsNullOrEmpty(SymbolicId))
            {
                if (!string.IsNullOrEmpty(NamespaceUri))
                {
                    buffer.AppendFormat(
                        CultureInfo.InvariantCulture,
                        " ({0}:{1})",
                        NamespaceUri,
                        SymbolicId);
                }
                else if (SymbolicId != buffer.ToString())
                {
                    buffer.AppendFormat(CultureInfo.InvariantCulture, " ({0})", SymbolicId);
                }
            }

            if (!LocalizedText.IsNullOrEmpty(LocalizedText))
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, " '{0}'", LocalizedText);
            }

            if ((0x0000FFFF & Code) != 0)
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, " [{0:X4}]", 0x0000FFFF & Code);
            }

            if (!string.IsNullOrEmpty(AdditionalInfo))
            {
                buffer.AppendLine()
                    .Append(AdditionalInfo);
            }
        }

        /// <summary>
        /// Append details to string buffer
        /// </summary>
        /// <param name="buffer"></param>
        internal void AppendLong(StringBuilder buffer)
        {
            buffer.Append("Id: ")
                .Append(StatusCodes.GetBrowseName(Code));

            if (!string.IsNullOrEmpty(SymbolicId))
            {
                buffer.AppendLine()
                    .Append("SymbolicId: ")
                    .Append(SymbolicId);
            }

            if (!LocalizedText.IsNullOrEmpty(LocalizedText))
            {
                buffer.AppendLine()
                    .Append("Description: ")
                    .Append(LocalizedText);
            }

            if (!string.IsNullOrEmpty(AdditionalInfo))
            {
                buffer.AppendLine()
                    .Append(AdditionalInfo);
            }

            ServiceResult innerResult = InnerResult;

            if (innerResult != null)
            {
                buffer.AppendLine()
                    .AppendLine("===")
                    .Append(innerResult.ToLongString());
            }
        }

        /// <summary>
        /// Looks up a string in a string table.
        /// </summary>
        private static string LookupString(IList<string> stringTable, int index)
        {
            if (index < 0 || stringTable == null || index >= stringTable.Count)
            {
                return null;
            }

            return stringTable[index];
        }

        /// <summary>
        /// Returns a string containing all nested exceptions.
        /// </summary>
        private static string BuildExceptionTrace(Exception exception)
        {
            var buffer = new StringBuilder();

            while (exception != null)
            {
                if (buffer.Length > 0)
                {
                    buffer
                        .AppendLine()
                        .AppendLine(">>>> (Inner) >>>>");
                }

                buffer.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "[{0}]",
                    exception.Message ?? exception.GetType().Name);

                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    AddStackTrace(buffer, exception.StackTrace);
                }

                exception = exception.InnerException;
            }
            return buffer.ToString();
        }

        /// <summary>
        /// Parse and add stack trace to buffer
        /// </summary>
        private static void AddStackTrace(StringBuilder buffer, string stackTrace)
        {
            string[] trace = stackTrace.Split(Environment.NewLine.ToCharArray());
            for (int ii = 0; ii < trace.Length; ii++)
            {
                if (!string.IsNullOrEmpty(trace[ii]))
                {
                    buffer
                        .AppendLine()
                        .AppendFormat(CultureInfo.InvariantCulture, "--- {0}", trace[ii]);
                }
            }
        }

        /// <summary>
        /// Extract a default message from an exception.
        /// </summary>
        private static LocalizedText GetDefaultMessage(Exception exception, uint code)
        {
            if (exception == null)
            {
                return LocalizedText.Null;
            }

            if (exception.Message != null)
            {
                if (exception.Message.StartsWith('['))
                {
                    return exception.Message;
                }
                if (exception is ServiceResultException)
                {
#if !DEBUGX
                    return exception.Message;
#endif
                }
                return Utils.Format("[{0}] {1}",
                    exception.GetType().Name,
#if !DEBUGX
                    exception.Message);
#else
                    BuildExceptionTrace(exception));
#endif
            }

            return Utils.Format("[{0}]",
#if !DEBUGX
                exception.GetType().Name);
#else
                BuildExceptionTrace(exception));
#endif
        }
    }
}
