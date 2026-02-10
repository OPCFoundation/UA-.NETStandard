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
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A class that combines the status code and diagnostic info structures.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ServiceResult
    {
        /// <summary>
        /// Get according ServiceResultException for ServiceResult
        /// </summary>
        /// <returns>ServiceResultException</returns>
        public ServiceResultException GetServiceResultException()
        {
            return new ServiceResultException(this);
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        [Obsolete("Use StatusCode constructor with symbolic id")]
        public ServiceResult(
            uint code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText,
            string additionalInfo,
            ServiceResult innerResult)
            : this(
                  namespaceUri,
                  new StatusCode(code, symbolicId),
                  localizedText,
                  additionalInfo,
                  innerResult)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the inner result.
        /// </remarks>
        [Obsolete("Use StatusCode constructor with symbolic id")]
        public ServiceResult(
            uint code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText,
            Exception innerException)
            : this(
                  namespaceUri,
                  new StatusCode(code, symbolicId),
                  localizedText,
                  null,
                  innerException)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the inner result.
        /// </remarks>
        [Obsolete("Use StatusCode constructor with symbolic id")]
        public ServiceResult(
            uint code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText,
            string additionalInfo,
            Exception innerException)
            : this(
                  namespaceUri,
                  new StatusCode(code, symbolicId),
                  localizedText,
                  additionalInfo,
                  innerException)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(
            string namespaceUri,
            StatusCode code,
            LocalizedText localizedText,
            string additionalInfo,
            ServiceResult innerResult)
        {
            StatusCode = code;
            NamespaceUri = namespaceUri;
            LocalizedText = localizedText;
            AdditionalInfo = additionalInfo;
            InnerResult = innerResult;
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        internal ServiceResult()
        {
            StatusCode = StatusCodes.Good;
        }

        /// <summary>
        /// Copy constructor taking an inner result as second argument, to build chains of service results.
        /// </summary>
        public ServiceResult(
            ServiceResult outerResult,
            ServiceResult innerResult = null)
            : this(
                outerResult.NamespaceUri,
                outerResult.StatusCode,
                outerResult.LocalizedText,
                outerResult.AdditionalInfo,
                innerResult)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(StatusCode code, ServiceResult innerResult)
            : this(null, code, null, null, innerResult)
        {
        }

        /// <summary>
        /// Constructs an object from a StatusCode.
        /// </summary>
        public ServiceResult(StatusCode code)
            : this(null, code, null, null, (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(StatusCode code, LocalizedText localizedText)
            : this(null, code, localizedText, null, (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(
            string namespaceUri,
            StatusCode code,
            LocalizedText localizedText,
            string additionalInfo)
            : this(
                namespaceUri,
                code,
                localizedText,
                additionalInfo,
                (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        public ServiceResult(
            string namespaceUri,
            StatusCode code,
            LocalizedText localizedText)
            : this(
                  namespaceUri,
                  code,
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
            XmlQualifiedName symbolicId,
            LocalizedText localizedText)
            : this(
                symbolicId?.Namespace,
                new StatusCode(code.Code, symbolicId?.Name),
                localizedText,
                null,
                (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the inner result.
        /// </remarks>
        public ServiceResult(
            string namespaceUri,
            StatusCode code,
            LocalizedText localizedText,
            string additionalInfo,
            Exception innerException)
        {
            var innerResult = new ServiceResult(innerException);

            // check if no new information provided.
            if (code.Code == innerResult.Code &&
                localizedText.IsNullOrEmpty &&
                additionalInfo == null)
            {
                StatusCode = innerResult.Code;
                NamespaceUri = innerResult.NamespaceUri;
                LocalizedText = innerResult.LocalizedText;
                AdditionalInfo = innerResult.AdditionalInfo;
                InnerResult = innerResult.InnerResult;
            }
            // make the exception the inner result.
            else
            {
                StatusCode = code;
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
            string namespaceUri,
            StatusCode code,
            LocalizedText localizedText,
            Exception innerException)
            : this(namespaceUri, code, localizedText, null, innerException)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(StatusCode code, Exception innerException)
            : this(null, code, null, null, innerException)
        {
        }

        /// <summary>
        /// Constructs an object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(
            string namespaceUri,
            StatusCode code,
            Exception innerException)
            : this(namespaceUri, code, null, null, innerException)
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
            LocalizedText localizedText,
            Exception innerException)
            : this(null, code, localizedText, null, innerException)
        {
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        /// <remarks>
        /// The code, symbolicId, namespaceUri and localizedText parameters
        /// are ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception e,
            string defaultNamespaceUri,
            StatusCode defaultCode,
            LocalizedText defaultLocalizedText)
        {
            if (e is AggregateException ae && ae.InnerExceptions.Count == 1)
            {
                e = ae.InnerExceptions[0];
            }
            if (e is ServiceResultException sre)
            {
                StatusCode = sre.Result.StatusCode;
                NamespaceUri = sre.Result.NamespaceUri;
                LocalizedText = sre.Result.LocalizedText;
                InnerResult = sre.Result.InnerResult;

                if (LocalizedText.IsNullOrEmpty)
                {
                    LocalizedText = defaultLocalizedText;
                }
            }
            else
            {
                StatusCode = defaultCode;
                NamespaceUri = defaultNamespaceUri;
                LocalizedText = defaultLocalizedText;
            }

            AdditionalInfo = BuildExceptionTrace(e);
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        /// <remarks>
        /// The defaultCode, defaultSymbolicId and defaultLocalizedText
        /// parameters are ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception exception,
            StatusCode defaultCode,
            LocalizedText defaultLocalizedText)
            : this(exception, null, defaultCode, defaultLocalizedText)
        {
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        /// <remarks>
        /// The defaultCode, defaultSymbolicId and defaultNamespaceUri
        /// parameters are ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception exception,
            string defaultNamespaceUri,
            StatusCode defaultCode)
            : this(
                  exception,
                  defaultNamespaceUri,
                  defaultCode,
                  GetDefaultMessage(exception))
        {
        }

        /// <summary>
        /// Constructs an object from an exception.
        /// </summary>
        /// <remarks>
        /// The code parameter is ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception exception,
            StatusCode defaultCode)
            : this(exception, null, defaultCode, GetDefaultMessage(exception))
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
            StatusCode = code;

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
            StatusCode = code;

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
        public static ServiceResult Good { get; } = new ServiceResult(StatusCodes.Good);

        /// <summary>
        /// A result representing a bad status.
        /// </summary>
        public static ServiceResult Bad { get; } = new ServiceResult(StatusCodes.Bad);

        /// <summary>
        /// Creates a new instance of a ServiceResult
        /// </summary>
        public static ServiceResult Create(StatusCode code, TranslationInfo translation)
        {
            if (translation.IsNull)
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
            StatusCode defaultCode)
        {
            // replace the default code with the one from the exception.

            if (e is ServiceResultException sre)
            {
                defaultCode = sre.StatusCode;
            }

            if (translation.IsNull)
            {
                return new ServiceResult(e, defaultCode);
            }

            return new ServiceResult(defaultCode, new LocalizedText(translation), e);
        }

        /// <summary>
        /// Creates a new instance of a ServiceResult
        /// </summary>
        public static ServiceResult Create(StatusCode code, string format, params object[] args)
        {
            if (format == null)
            {
                return new ServiceResult(code);
            }

            if (args == null || args.Length == 0)
            {
                return new ServiceResult(code, format);
            }

            return new ServiceResult(code, CoreUtils.Format(format, args));
        }

        /// <summary>
        /// Creates a new instance of a ServiceResult
        /// </summary>
        public static ServiceResult Create(
            Exception e,
            StatusCode defaultCode,
            string format,
            params object[] args)
        {
            // replace the default code with the one from the exception.

            if (e is ServiceResultException sre)
            {
                defaultCode = sre.StatusCode;
            }

            if (string.IsNullOrEmpty(format))
            {
                return new ServiceResult(e, defaultCode);
            }

            if (args == null || args.Length == 0)
            {
                return new ServiceResult(defaultCode, format, e);
            }

            return new ServiceResult(defaultCode, CoreUtils.Format(format, args), e);
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
        /// Converts a StatusCode a ServiceResult object.
        /// </summary>
        public static implicit operator ServiceResult(StatusCode code)
        {
            return new ServiceResult(code);
        }

        /// <summary>
        /// Converts a StatusCode object to a 32-bit code.
        /// </summary>
        public static explicit operator StatusCode(ServiceResult status)
        {
            if (status == null)
            {
                return Good.StatusCode;
            }

            return status.StatusCode;
        }

        /// <summary>
        /// Looks up the symbolic name for a status code.
        /// </summary>
        [Obsolete("Use Status code type with symbolic id directly.")]
        public static string LookupSymbolicId(uint code)
        {
            return StatusCode.LookupSymbolicId(code);
        }

        /// <summary>
        /// The status code associated with the result.
        /// </summary>
        public uint Code => StatusCode.Code;

        /// <summary>
        /// The status code associated with the result.
        /// </summary>
        [DataMember(Order = 1)]
        public StatusCode StatusCode { get; private set; }

        /// <summary>
        /// The namespace that qualifies symbolic identifier.
        /// </summary>
        [DataMember(Order = 2)]
        public string NamespaceUri { get; private set; }

        /// <summary>
        /// The qualified name of the symbolic identifier associated with the status code.
        /// </summary>
        [DataMember(Order = 3)]
        public string SymbolicId
        {
            get => StatusCode.SymbolicId;
            private set => StatusCode = new StatusCode(StatusCode.Code, value);
        }

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
        /// Append to buffer
        /// </summary>
        /// <param name="buffer"></param>
        internal void Append(StringBuilder buffer)
        {
            buffer.AppendFormat(CultureInfo.InvariantCulture, "[{0:X}]", 0xFFFF0000 & Code);

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

            if (!LocalizedText.IsNullOrEmpty)
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
            return new StringBuilder()
                .AppendException(exception)
                .ToString();
        }

        /// <summary>
        /// Extract a default message from an exception.
        /// </summary>
        private static LocalizedText GetDefaultMessage(Exception exception)
        {
            if (exception == null)
            {
                return LocalizedText.Null;
            }

            if (exception is AggregateException ae && ae.InnerExceptions.Count == 1)
            {
                exception = ae.InnerExceptions[0];
            }

            if (exception.Message != null)
            {
                if (exception.Message.StartsWith('['))
                {
                    return exception.Message;
                }
                if (exception is ServiceResultException)
                {
#if !DEBUG
                    return exception.Message;
#endif
                }
                return CoreUtils.Format("[{0}] {1}",
                    exception.GetType().Name,
#if !DEBUG
                    exception.Message);
#else
                    BuildExceptionTrace(exception));
#endif
            }

            return CoreUtils.Format("[{0}]",
#if !DEBUG
                exception.GetType().Name);
#else
                BuildExceptionTrace(exception));
#endif
        }
    }
}
