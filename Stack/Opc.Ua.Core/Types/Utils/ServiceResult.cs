/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        private ServiceResult()
        {
            Code = StatusCodes.Good;
        }

        /// <summary>
        /// Constructs a object by specifying each property.
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
        /// <param name="outerResult"></param>
        /// <param name="innerResult"></param>
        public ServiceResult(
            ServiceResult outerResult,
            ServiceResult innerResult = null)
            :
            this(outerResult.Code, outerResult.SymbolicId, outerResult.NamespaceUri, outerResult.LocalizedText, outerResult.AdditionalInfo, innerResult)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            ServiceResult innerResult)
        :
            this(code, null, null, null, null, innerResult)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText,
            string additionalInfo)
        :
            this(code, symbolicId, namespaceUri, localizedText, additionalInfo, (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            LocalizedText localizedText)
        :
            this(code, symbolicId, namespaceUri, localizedText, (string)null, (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri)
        :
            this(code, symbolicId, namespaceUri, (string)null, (string)null, (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            XmlQualifiedName symbolicId,
            LocalizedText localizedText)
        :
            this(code, (symbolicId != null) ? symbolicId.Name : null, (symbolicId != null) ? symbolicId.Namespace : null, localizedText, (string)null, (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        public ServiceResult(
            StatusCode code,
            LocalizedText localizedText)
        :
            this(code, (string)null, (string)null, localizedText, (string)null, (ServiceResult)null)
        {
        }

        /// <summary>
        /// Constructs a object from a StatusCode.
        /// </summary>
        public ServiceResult(StatusCode status)
        {
            m_code = status.Code;
        }

        /// <summary>
        /// Constructs a object from a StatusCode.
        /// </summary>
        public ServiceResult(uint code)
        {
            m_code = code;
        }

        /// <summary>
        /// Constructs a object by specifying each property.
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
            ServiceResult innerResult = new ServiceResult(innerException);

            // check if no new information provided.
            if (code.Code == innerResult.Code && symbolicId == null && localizedText == null && additionalInfo == null)
            {
                m_code = innerResult.Code;
                m_symbolicId = innerResult.SymbolicId;
                m_namespaceUri = innerResult.NamespaceUri;
                m_localizedText = innerResult.LocalizedText;
                m_additionalInfo = innerResult.AdditionalInfo;
                m_innerResult = innerResult.InnerResult;
            }

            // make the exception the inner result.
            else
            {
                m_code = code.Code;
                m_symbolicId = symbolicId;
                m_namespaceUri = namespaceUri;
                m_localizedText = localizedText;
                m_additionalInfo = additionalInfo;
                m_innerResult = innerResult;
            }
        }

        /// <summary>
        /// Constructs a object by specifying each property.
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
        :
            this(code, symbolicId, namespaceUri, localizedText, null, innerException)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(
            StatusCode code,
            string symbolicId,
            string namespaceUri,
            Exception innerException)
        :
            this(code, symbolicId, namespaceUri, null, null, innerException)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(
            StatusCode code,
            LocalizedText localizedText,
            Exception innerException)
        :
            this(code, null, null, localizedText, null, innerException)
        {
        }

        /// <summary>
        /// Constructs a object by specifying each property.
        /// </summary>
        /// <remarks>
        /// The innerException is used to construct the innerResult.
        /// </remarks>
        public ServiceResult(StatusCode code, Exception innerException)
        :
            this(code, null, null, null, null, innerException)
        {
        }

        /// <summary>
        /// Constructs a object from an exception.
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
            ServiceResultException sre = e as ServiceResultException;

            if (sre != null)
            {
                m_code = sre.StatusCode;
                m_namespaceUri = sre.NamespaceUri;
                m_symbolicId = sre.SymbolicId;
                m_localizedText = sre.LocalizedText;
                m_innerResult = sre.Result.InnerResult;

                if (LocalizedText.IsNullOrEmpty(m_localizedText))
                {
                    m_localizedText = defaultLocalizedText;
                }
            }
            else
            {
                m_code = defaultCode;
                m_symbolicId = defaultSymbolicId;
                m_namespaceUri = defaultNamespaceUri;
                m_localizedText = defaultLocalizedText;
            }

            m_additionalInfo = BuildExceptionTrace(e);
        }

        /// <summary>
        /// Constructs a object from an exception.
        /// </summary>
        /// <remarks>
        /// The defaultCode and defaultLocalizedText parameters are ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception exception,
            uint defaultCode,
            LocalizedText defaultLocalizedText)
        :
            this(exception, defaultCode, null, null, defaultLocalizedText)
        {
        }

        /// <summary>
        /// Constructs a object from an exception.
        /// </summary>
        /// <remarks>
        /// The code, symbolicId and namespaceUri parameters are ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception exception,
            uint defaultCode,
            string defaultSymbolicId,
            string defaultNamespaceUri)
        :
            this(exception, defaultCode, defaultSymbolicId, defaultNamespaceUri, null)
        {
        }

        /// <summary>
        /// Constructs a object from an exception.
        /// </summary>
        /// <remarks>
        /// The code parameter is ignored for ServiceResultExceptions.
        /// </remarks>
        public ServiceResult(
            Exception exception,
            uint defaultCode)
        :
            this(exception, defaultCode, null, null, GetDefaultMessage(exception))
        {
        }

        /// <summary>
        /// Constructs a object from an exception.
        /// </summary>
        public ServiceResult(Exception exception)
        :
            this(exception, StatusCodes.Bad, null, null, GetDefaultMessage(exception))
        {
        }

        /// <summary>
        /// Initializes the object with a status code and a diagnostic info structure.
        /// </summary>
        public ServiceResult(StatusCode code, DiagnosticInfo diagnosticInfo, IList<string> stringTable)
        {
            m_code = (uint)code;

            if (diagnosticInfo != null)
            {
                m_namespaceUri = LookupString(stringTable, diagnosticInfo.NamespaceUri);
                m_symbolicId = LookupString(stringTable, diagnosticInfo.SymbolicId);

                string locale = LookupString(stringTable, diagnosticInfo.Locale);
                string localizedText = LookupString(stringTable, diagnosticInfo.LocalizedText);
                m_localizedText = new LocalizedText(locale, localizedText);

                m_additionalInfo = diagnosticInfo.AdditionalInfo;

                if (!StatusCode.IsGood(diagnosticInfo.InnerStatusCode))
                {
                    m_innerResult = new ServiceResult(diagnosticInfo.InnerStatusCode, diagnosticInfo.InnerDiagnosticInfo, stringTable);
                }
            }
        }

        /// <summary>
        /// Initializes the object with a status code and a diagnostic info structure.
        /// </summary>
        public ServiceResult(StatusCode code, int index, DiagnosticInfoCollection diagnosticInfos, IList<string> stringTable)
        {
            m_code = (uint)code;

            if (index >= 0 && diagnosticInfos != null && index < diagnosticInfos.Count)
            {
                DiagnosticInfo diagnosticInfo = diagnosticInfos[index];

                if (diagnosticInfo != null)
                {
                    m_namespaceUri = LookupString(stringTable, diagnosticInfo.NamespaceUri);
                    m_symbolicId = LookupString(stringTable, diagnosticInfo.SymbolicId);

                    string locale = LookupString(stringTable, diagnosticInfo.Locale);
                    string localizedText = LookupString(stringTable, diagnosticInfo.LocalizedText);
                    m_localizedText = new LocalizedText(locale, localizedText);

                    m_additionalInfo = diagnosticInfo.AdditionalInfo;

                    if (!StatusCode.IsGood(diagnosticInfo.InnerStatusCode))
                    {
                        m_innerResult = new ServiceResult(diagnosticInfo.InnerStatusCode, diagnosticInfo.InnerDiagnosticInfo, stringTable);
                    }
                }
            }
        }
        #endregion

        #region Static Interface
        /// <summary>
        /// A result representing a good status.
        /// </summary>
        public static ServiceResult Good => s_Good;

        private static readonly ServiceResult s_Good = new ServiceResult();

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
        public static ServiceResult Create(Exception e, TranslationInfo translation, uint defaultCode)
        {
            // replace the default code with the one from the exception.
            ServiceResultException sre = e as ServiceResultException;

            if (sre != null)
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
        public static ServiceResult Create(Exception e, uint defaultCode, string format, params object[] args)
        {
            // replace the default code with the one from the exception.
            ServiceResultException sre = e as ServiceResultException;

            if (sre != null)
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
                return StatusCode.IsGood(status.m_code);
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
                return StatusCode.IsNotGood(status.m_code);
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
                return StatusCode.IsUncertain(status.m_code);
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
                return StatusCode.IsNotUncertain(status.m_code);
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
                return StatusCode.IsBad(status.m_code);
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
                return StatusCode.IsNotBad(status.m_code);
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
        /// Returns a string containing all nested exceptions.
        /// </summary>
        public static string BuildExceptionTrace(Exception exception)
        {
            StringBuilder buffer = new StringBuilder();

            while (exception != null)
            {
                if (buffer.Length > 0)
                {
                    buffer.AppendLine();
                    buffer.AppendLine();
                }

                buffer.AppendFormat(CultureInfo.InvariantCulture, ">>> {0}", exception.Message);

                if (!String.IsNullOrEmpty(exception.StackTrace))
                {
                    string[] trace = exception.StackTrace.Split(Environment.NewLine.ToCharArray());
                    for (int ii = 0; ii < trace.Length; ii++)
                    {
                        if (trace[ii] != null && trace[ii].Length > 0)
                        {
                            buffer.AppendLine();
                            buffer.AppendFormat(CultureInfo.InvariantCulture, "--- {0}", trace[ii]);
                        }
                    }
                }

                exception = exception.InnerException;
            }

            return buffer.ToString();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The status code associated with the result.
        /// </summary>
        public uint Code
        {
            get { return m_code; }
            private set { m_code = value; }
        }

        /// <summary>
        /// The status code associated with the result.
        /// </summary>
        [DataMember(Order = 1)]
        public StatusCode StatusCode
        {
            get { return m_code; }
            private set { m_code = value.Code; }
        }

        /// <summary>
        /// The namespace that qualifies symbolic identifier.
        /// </summary>
        [DataMember(Order = 2)]
        public string NamespaceUri
        {
            get { return m_namespaceUri; }
            private set { m_namespaceUri = value; }
        }

        /// <summary>
        /// The qualified name of the symbolic identifier associated with the status code.
        /// </summary>	
        [DataMember(Order = 3)]
        public string SymbolicId
        {
            get { return m_symbolicId; }
            private set { m_symbolicId = value; }
        }

        /// <summary>
        /// The localized description for the status code.
        /// </summary>
        [DataMember(Order = 4)]
        public LocalizedText LocalizedText
        {
            get { return m_localizedText; }
            private set { m_localizedText = value; }
        }

        /// <summary>
        /// Additional diagnostic/debugging information associated with the operation.
        /// </summary>
        [DataMember(Order = 5)]
        public string AdditionalInfo
        {
            get { return m_additionalInfo; }
            private set { m_additionalInfo = value; }
        }

        /// <summary>
        /// Nested error information.
        /// </summary>
        [DataMember(Order = 6)]
        public ServiceResult InnerResult
        {
            get { return m_innerResult; }
            private set { m_innerResult = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        public override string ToString()
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append(LookupSymbolicId(m_code));

            if (!String.IsNullOrEmpty(m_symbolicId))
            {
                if (!String.IsNullOrEmpty(m_namespaceUri))
                {
                    buffer.AppendFormat(" ({0}:{1})", m_namespaceUri, m_symbolicId);
                }
                else if (m_symbolicId != buffer.ToString())
                {
                    buffer.AppendFormat(" ({0})", m_symbolicId);
                }
            }

            if (!LocalizedText.IsNullOrEmpty(m_localizedText))
            {
                buffer.AppendFormat(" '{0}'", m_localizedText);
            }

            if ((0x0000FFFF & Code) != 0)
            {
                buffer.AppendFormat(" [{0:X4}]", (0x0000FFFF & Code));
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Returns a formatted string with the contents of exception.
        /// </summary>
        public string ToLongString()
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append("Id: ");
            buffer.Append(StatusCodes.GetBrowseName(m_code));

            if (!String.IsNullOrEmpty(m_symbolicId))
            {
                buffer.AppendLine();
                buffer.Append("SymbolicId: ");
                buffer.Append(m_symbolicId);
            }

            if (!LocalizedText.IsNullOrEmpty(m_localizedText))
            {
                buffer.AppendLine();
                buffer.Append("Description: ");
                buffer.Append(m_localizedText);
            }

            if (AdditionalInfo != null && AdditionalInfo.Length > 0)
            {
                buffer.AppendLine();
                buffer.Append(AdditionalInfo);
            }

            ServiceResult innerResult = m_innerResult;

            if (innerResult != null)
            {
                buffer.AppendLine();
                buffer.Append("===");
                buffer.AppendLine();
                buffer.Append(innerResult.ToLongString());
            }

            return buffer.ToString();
        }
        #endregion

        #region Private Methods
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
        /// Extract a default message from an exception.
        /// </summary>
        /// <param name="exception"></param>
        private static string GetDefaultMessage(Exception exception)
        {
            if (exception != null && exception.Message != null)
            {
                if (exception.Message.StartsWith("[") || exception is ServiceResultException)
                {
                    return exception.Message;
                }

                return String.Format(CultureInfo.InvariantCulture, "[{0}] {1}", exception.GetType().Name, exception.Message);
            }

            return String.Empty;
        }
        #endregion

        #region Private Fields
        private uint m_code;
        private string m_symbolicId;
        private string m_namespaceUri;
        private LocalizedText m_localizedText;
        private string m_additionalInfo;
        private ServiceResult m_innerResult;
        #endregion
    }
}
