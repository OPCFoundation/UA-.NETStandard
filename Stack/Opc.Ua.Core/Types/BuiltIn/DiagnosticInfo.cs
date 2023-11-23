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
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Contains diagnostic information associated with a StatusCode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DiagnosticInfo BinaryEncoding is defined in <b>Part 6 - Mappings, Section 6.2.2.13</b>, titled 
    /// <b>Mappings</b>.
    /// <br/></para>
    /// <para>
    /// The DiagnosticInfo object is an object that contains diagnostic information, and is intended to be used
    /// in provide diagnostic information in a uniform way.
    /// <br/></para>
    /// </remarks>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public sealed class DiagnosticInfo : ICloneable, IFormattable
    {
        /// <summary>
        /// Limits the recursion depth for the InnerDiagnosticInfo field.
        /// </summary>
        public static readonly int MaxInnerDepth = 5;

        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public DiagnosticInfo()
        {
            Initialize();
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the object while copying the value passed in.
        /// If InnerDiagnosticInfo exceeds the recursion limit, it is not copied.
        /// </remarks>
        /// <param name="value">The value to copy</param>
        /// <exception cref="ArgumentNullException">Thrown when the value is null</exception>
        public DiagnosticInfo(DiagnosticInfo value) : this(value, 0)
        {
        }

        /// <summary>
        /// Creates a deep copy of the value, but limits the recursion depth.
        /// </summary>
        private DiagnosticInfo(DiagnosticInfo value, int depth)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            m_symbolicId = value.m_symbolicId;
            m_namespaceUri = value.m_namespaceUri;
            m_locale = value.m_locale;
            m_localizedText = value.m_localizedText;
            m_additionalInfo = value.m_additionalInfo;
            m_innerStatusCode = value.m_innerStatusCode;

            if (value.m_innerDiagnosticInfo != null && depth < MaxInnerDepth)
            {
                m_innerDiagnosticInfo = new DiagnosticInfo(value.m_innerDiagnosticInfo, depth + 1);
            }
        }

        /// <summary>
        /// Initializes the object with specific values.
        /// </summary>
        /// <param name="symbolicId">The symbolic ID</param>
        /// <param name="namespaceUri">The namespace URI applicable</param>
        /// <param name="locale">The locale for the localized text value</param>
        /// <param name="localizedText">The localized text value</param>
        /// <param name="additionalInfo">Additional, textual information</param>
        public DiagnosticInfo(
            int symbolicId,
            int namespaceUri,
            int locale,
            int localizedText,
            string additionalInfo)
        {
            m_symbolicId = symbolicId;
            m_namespaceUri = namespaceUri;
            m_locale = locale;
            m_localizedText = localizedText;
            m_additionalInfo = additionalInfo;
        }

        /// <summary>
        /// Initializes the object with a ServiceResult.
        /// </summary>
        /// <param name="diagnosticsMask">The bitmask describing the diagnostic data</param>
        /// <param name="result">The overall transaction result</param>
        /// <param name="serviceLevel">The service level</param>
        /// <param name="stringTable">A table of strings carrying more diagnostic data</param>
        public DiagnosticInfo(
            ServiceResult result,
            DiagnosticsMasks diagnosticsMask,
            bool serviceLevel,
            StringTable stringTable)
            : this(result, diagnosticsMask, serviceLevel, stringTable, 0)
        {
        }

        /// <summary>
        /// Initializes the object with a ServiceResult.
        /// Limits the recursion depth for the InnerDiagnosticInfo field.
        /// </summary>
        /// <param name="diagnosticsMask">The bitmask describing the diagnostic data</param>
        /// <param name="result">The overall transaction result</param>
        /// <param name="serviceLevel">The service level</param>
        /// <param name="stringTable">A table of strings carrying more diagnostic data</param>
        /// <param name="depth">The recursion depth of the inner diagnostics field</param>
        private DiagnosticInfo(
            ServiceResult result,
            DiagnosticsMasks diagnosticsMask,
            bool serviceLevel,
            StringTable stringTable,
            int depth)
        {
            uint mask = (uint)diagnosticsMask;

            if (!serviceLevel)
            {
                mask >>= 5;
            }

            diagnosticsMask = (DiagnosticsMasks)mask;

            Initialize(result, diagnosticsMask, stringTable, depth);
        }

        /// <summary>
        /// Initializes the object with an exception.
        /// </summary>
        /// <param name="diagnosticsMask">A bitmask describing the type of diagnostic data</param>
        /// <param name="exception">The exception to associated with the diagnostic data</param>
        /// <param name="serviceLevel">The service level</param>
        /// <param name="stringTable">A table of strings that may contain additional diagnostic data</param>
        public DiagnosticInfo(
            Exception exception,
            DiagnosticsMasks diagnosticsMask,
            bool serviceLevel,
            StringTable stringTable)
        {
            uint mask = (uint)diagnosticsMask;

            if (!serviceLevel)
            {
                mask >>= 5;
            }

            diagnosticsMask = (DiagnosticsMasks)mask;

            Initialize(new ServiceResult(exception), diagnosticsMask, stringTable, 0);
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context information of an underlying data-stream</param>
        [OnDeserializing()]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "context")]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <remarks>
        /// Initializes the object with default values during deserialization.
        /// </remarks>
        private void Initialize()
        {
            m_symbolicId = -1;
            m_namespaceUri = -1;
            m_locale = -1;
            m_localizedText = -1;
            m_additionalInfo = null;
            m_innerStatusCode = StatusCodes.Good;
            m_innerDiagnosticInfo = null;
        }

        /// <summary>
        /// Initializes the object with a service result.
        /// </summary>
        /// <param name="diagnosticsMask">The bitmask describing the type of diagnostic data</param>
        /// <param name="result">The transaction result</param>
        /// <param name="stringTable">An array of strings that may be used to provide additional diagnostic details</param>
        /// <param name="depth">The depth of the inner diagnostics property</param>
        private void Initialize(
            ServiceResult result,
            DiagnosticsMasks diagnosticsMask,
            StringTable stringTable,
            int depth)
        {
            if (stringTable == null) throw new ArgumentNullException(nameof(stringTable));

            Initialize();

            if ((DiagnosticsMasks.ServiceSymbolicId & diagnosticsMask) != 0)
            {
                string symbolicId = result.SymbolicId;
                string namespaceUri = result.NamespaceUri;

                if (!String.IsNullOrEmpty(symbolicId))
                {
                    m_symbolicId = stringTable.GetIndex(result.SymbolicId);

                    if (m_symbolicId == -1)
                    {
                        m_symbolicId = stringTable.Count;
                        stringTable.Append(symbolicId);
                    }

                    if (!String.IsNullOrEmpty(namespaceUri))
                    {
                        m_namespaceUri = stringTable.GetIndex(namespaceUri);

                        if (m_namespaceUri == -1)
                        {
                            m_namespaceUri = stringTable.Count;
                            stringTable.Append(namespaceUri);
                        }
                    }
                }
            }

            if ((DiagnosticsMasks.ServiceLocalizedText & diagnosticsMask) != 0)
            {
                if (!Opc.Ua.LocalizedText.IsNullOrEmpty(result.LocalizedText))
                {
                    if (!String.IsNullOrEmpty(result.LocalizedText.Locale))
                    {
                        m_locale = stringTable.GetIndex(result.LocalizedText.Locale);

                        if (m_locale == -1)
                        {
                            m_locale = stringTable.Count;
                            stringTable.Append(result.LocalizedText.Locale);
                        }
                    }

                    m_localizedText = stringTable.GetIndex(result.LocalizedText.Text);

                    if (m_localizedText == -1)
                    {
                        m_localizedText = stringTable.Count;
                        stringTable.Append(result.LocalizedText.Text);
                    }
                }
            }

            if ((DiagnosticsMasks.ServiceAdditionalInfo & diagnosticsMask) != 0 &&
                (DiagnosticsMasks.UserPermissionAdditionalInfo & diagnosticsMask) != 0)
            {
                m_additionalInfo = result.AdditionalInfo;
            }

            if (result.InnerResult != null)
            {
                if ((DiagnosticsMasks.ServiceInnerStatusCode & diagnosticsMask) != 0)
                {
                    m_innerStatusCode = result.InnerResult.StatusCode;
                }

                // recursively append the inner diagnostics.
                if ((DiagnosticsMasks.ServiceInnerDiagnostics & diagnosticsMask) != 0)
                {
                    if (depth < MaxInnerDepth)
                    {
                        m_innerDiagnosticInfo = new DiagnosticInfo(
                            result.InnerResult,
                            diagnosticsMask,
                            true,
                            stringTable,
                            depth + 1);
                    }
                    else
                    {
                        Utils.LogWarning(
                            "Inner diagnostics truncated. Max depth of {0} exceeded.",
                            MaxInnerDepth);
                    }
                }
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The index of the symbolic id in the string table.
        /// </summary>
        [DataMember(Order = 1, IsRequired = false)]
        public int SymbolicId
        {
            get { return m_symbolicId; }
            set { m_symbolicId = value; }
        }

        /// <summary>
        /// The index of the namespace uri in the string table.
        /// </summary>
        [DataMember(Order = 2, IsRequired = false)]
        public int NamespaceUri
        {
            get { return m_namespaceUri; }
            set { m_namespaceUri = value; }
        }

        /// <summary>
        /// The index of the locale associated with the localized text.
        /// </summary>
        [DataMember(Order = 3, IsRequired = false)]
        public int Locale
        {
            get { return m_locale; }
            set { m_locale = value; }
        }

        /// <summary>
        /// The index of the localized text in the string table.
        /// </summary>
        [DataMember(Order = 4, IsRequired = false)]
        public int LocalizedText
        {
            get { return m_localizedText; }
            set { m_localizedText = value; }
        }

        /// <summary>
        /// The additional debugging or trace information.
        /// </summary>
        [DataMember(Order = 5, IsRequired = false, EmitDefaultValue = false)]
        public string AdditionalInfo
        {
            get { return m_additionalInfo; }
            set { m_additionalInfo = value; }
        }

        /// <summary>
        /// The status code returned from an underlying system.
        /// </summary>
        [DataMember(Order = 6, IsRequired = false)]
        public StatusCode InnerStatusCode
        {
            get { return m_innerStatusCode; }
            set { m_innerStatusCode = value; }
        }

        /// <summary>
        /// The diagnostic info returned from a underlying system.
        /// </summary>
        [DataMember(Order = 7, IsRequired = false, EmitDefaultValue = false)]
        public DiagnosticInfo InnerDiagnosticInfo
        {
            get { return m_innerDiagnosticInfo; }
            set { m_innerDiagnosticInfo = value; }
        }

        /// <summary>
        /// Whether the object represents a Null DiagnosticInfo.
        /// </summary>
        public bool IsNullDiagnosticInfo
        {
            get
            {
                if (m_symbolicId == -1 &&
                    m_locale == -1 &&
                    m_localizedText == -1 &&
                    m_namespaceUri == -1 &&
                    m_additionalInfo == null &&
                    m_innerDiagnosticInfo == null &&
                    m_innerStatusCode == StatusCodes.Good)
                {
                    return true;
                }
                return false;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        public override bool Equals(object obj)
        {
            return Equals(obj, 0);
        }

        /// <summary>
        /// Returns a unique hashcode for the object.
        /// </summary>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            GetHashCode(ref hash, 0);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Converts the value to a human readable string.
        /// </summary>
        /// <remarks>
        /// Converts the value to a human readable string.
        /// </remarks>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
        /// </remarks>
        /// <param name="format">(Unused). Always pass a null</param>
        /// <param name="formatProvider">(Unused) The provider.</param>
        /// <exception cref="FormatException">Thrown if the <i>format</i> parameter is NOT null</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return Utils.Format("{0}:{1}:{2}:{3}", m_symbolicId, m_namespaceUri, m_locale, m_localizedText);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region ICloneable Members
        /// <inheritdoc/>
        public object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        public new object MemberwiseClone()
        {
            return new DiagnosticInfo(this);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds the hashcodes for the object.
        /// Limits the recursion depth to prevent stack overflow.
        /// </summary>
        private void GetHashCode(ref HashCode hash, int depth)
        {
            hash.Add(this.m_symbolicId);
            hash.Add(this.m_namespaceUri);
            hash.Add(this.m_locale);
            hash.Add(this.m_localizedText);

            if (this.m_additionalInfo != null)
            {
                hash.Add(this.m_additionalInfo);
            }

            hash.Add(this.m_innerStatusCode);

            if (this.m_innerDiagnosticInfo != null && depth < MaxInnerDepth)
            {
                this.m_innerDiagnosticInfo.GetHashCode(ref hash, depth + 1);
            }
        }

        /// <summary>
        /// Determines if the specified object is equal to this object.
        /// Limits the depth of the comparison to avoid infinite recursion.
        /// </summary>
        private bool Equals(object obj, int depth)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj == null && IsNullDiagnosticInfo)
            {
                return true;
            }


            if (obj is DiagnosticInfo value)
            {

                if (this.m_symbolicId != value.m_symbolicId)
                {
                    return false;
                }

                if (this.m_namespaceUri != value.m_namespaceUri)
                {
                    return false;
                }

                if (this.m_locale != value.m_locale)
                {
                    return false;
                }

                if (this.m_localizedText != value.m_localizedText)
                {
                    return false;
                }

                if (this.m_additionalInfo != value.m_additionalInfo)
                {
                    return false;
                }

                if (this.m_innerStatusCode != value.m_innerStatusCode)
                {
                    return false;
                }

                if (this.m_innerDiagnosticInfo != null)
                {
                    if (depth < MaxInnerDepth)
                    {
                        return this.m_innerDiagnosticInfo.Equals(value.m_innerDiagnosticInfo, depth + 1);
                    }
                    else
                    {
                        // ignore the remaining inner diagnostic info and consider it equal.
                        return true;
                    }
                }

                return value.m_innerDiagnosticInfo == null;
            }

            return false;
        }
        #endregion

        #region Private Members
        private int m_symbolicId;
        private int m_namespaceUri;
        private int m_locale;
        private int m_localizedText;
        private string m_additionalInfo;
        private StatusCode m_innerStatusCode;
        private DiagnosticInfo m_innerDiagnosticInfo;
        #endregion
    }

    #region DiagnosticInfoCollection Class
    /// <summary>
    /// A collection of DiagnosticInfo objects.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of DiagnosticInfo objects.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfDiagnosticInfo", Namespace = Namespaces.OpcUaXsd, ItemName = "DiagnosticInfo")]
    public partial class DiagnosticInfoCollection : List<DiagnosticInfo>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public DiagnosticInfoCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">The collection to copy the contents from</param>
        public DiagnosticInfoCollection(IEnumerable<DiagnosticInfo> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The max capacity of the collection</param>
        public DiagnosticInfoCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="DiagnosticInfo"/> objects to return within a collection</param>
        public static DiagnosticInfoCollection ToDiagnosticInfoCollection(DiagnosticInfo[] values)
        {
            if (values != null)
            {
                return new DiagnosticInfoCollection(values);
            }

            return new DiagnosticInfoCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="DiagnosticInfo"/> objects to return within a collection</param>
        public static implicit operator DiagnosticInfoCollection(DiagnosticInfo[] values)
        {
            return ToDiagnosticInfoCollection(values);
        }

        #region ICloneable
        /// <inheritdoc/>
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            DiagnosticInfoCollection clone = new DiagnosticInfoCollection(this.Count);

            foreach (DiagnosticInfo element in this)
            {
                clone.Add((DiagnosticInfo)Utils.Clone(element));
            }

            return clone;
        }
        #endregion

    }//class
    #endregion

}//namespace
