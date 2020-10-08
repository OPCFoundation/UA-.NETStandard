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

namespace Opc.Ua
{
    /// <summary>
    /// Human readable qualified with a locale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The LocalizedText is defined in <b>Part 3 - Address Space Model, Section 7.5</b>, titled 
    /// <b>LocalizedText</b>.
    /// <br/></para>
    /// <para>
    /// This class provides a wrapper for posting text that is qualified with the locale that it is
    /// intended for.
    /// <br/></para>
    /// </remarks>
    /// <example>
    /// <para>The following example shows a very simple use of this class to localize a 
    /// welcome message</para>
    /// <code lang="C#">
    /// LocalizedText welcomeUS = new LocalizedText( "Hi Everyone", "EN-US" );
    /// LocalizedText welcomeGB = new LocalizedText( "Hello Everyone", "EN-GB" );
    /// LocalizedText welcomeNoLocale = new LocalizedText( "Welcome" );
    /// 
    /// Utils.Trace( welcomeUS.ToString() );
    /// Utils.Trace( welcomeGB.ToString() );
    /// Utils.Trace( welcomeNoLocale.ToString() );
    /// </code>
    /// <code lang="Visual Basic">
    /// Dim welcomeUS As LocalizedText = New LocalizedText( "Hi Everyone", "EN-GB" )
    /// Dim welcomeGB As LocalizedText = New LocalizedText( "Hello Everyone", "EN-GB" )
    /// Dim welcomeNoLocale As LocalizedText = New LocalizedText( "Welcome" )
    /// 
    /// Utils.Trace( welcomeUS.ToString() )
    /// Utils.Trace( welcomeGB.ToString() )
    /// Utils.Trace( welcomeNoLocale.ToString() )
    /// </code>
    /// <para>
    /// This produces the following output:<br/>
    /// [EN-US]:Hi Everyone<br/>
    /// [EN-GB]:Hello Everyone<br/>
    /// Welcome<br/>
    /// </para>
    /// </example>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public partial class LocalizedText : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with the default values.
        /// </summary>
        /// <remarks>
        /// Initializes the object with the default values.
        /// </remarks>
        private LocalizedText()
        {
            m_locale = null;
            m_text = null;
        }


        /// <summary>
        /// Formats the text with the arguments using the specified locale.
        /// </summary>
        public LocalizedText(string key, string locale, string text, params object[] args)
        :
            this(new TranslationInfo(key, locale, text, args))
        {
        }

        /// <summary>
        /// Creates text from a TranslationInfo object.
        /// </summary>
        public LocalizedText(TranslationInfo translationInfo)
        {
            if (translationInfo == null) throw new ArgumentNullException(nameof(translationInfo));

            m_locale = translationInfo.Locale;
            m_text = translationInfo.Text;
            m_translationInfo = translationInfo;

            if (m_translationInfo.Args == null || m_translationInfo.Args.Length == 0)
            {
                return;
            }

            CultureInfo culture = CultureInfo.InvariantCulture;

            if (!String.IsNullOrEmpty(m_locale))
            {
                try
                {
                    culture = new CultureInfo(m_locale);
                }
                catch
                {
                    culture = CultureInfo.InvariantCulture;
                }
            }

            try
            {
                m_text = String.Format(culture, m_translationInfo.Text, m_translationInfo.Args);
            }
            catch
            {
                m_text = m_translationInfo.Text;
            }
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the value.
        /// </remarks>
        /// <param name="value">The text to create an instance from</param>
        /// <exception cref="ArgumentNullException">Thrown when the value is null</exception>
        public LocalizedText(LocalizedText value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            m_locale = value.m_locale;
            m_text = value.m_text;
        }

        /// <summary>
        /// Initializes the object with a text and the default locale.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a text and the default locale.
        /// </remarks>
        /// <param name="text">The plain text stored within this object</param>
        public LocalizedText(string text)
        {
            m_locale = null;
            m_text = text;
        }

        /// <summary>
        /// Initializes the object with a locale and text.
        /// </summary>
        /// <remarks>
        /// Initializes the object with a locale and text.
        /// </remarks>
        /// <param name="locale">The locale code applicable for the specified text</param>
        /// <param name="text">The text to store</param>
        public LocalizedText(string locale, string text)
        {
            m_locale = locale;
            m_text = text;
        }

        /// <summary>
        /// Initializes the locale with a key, locale and text.
        /// </summary>
        /// <param name="key">A key used to look up the text for different locales</param>
        /// <param name="locale">The locale for the text provides</param>
        /// <param name="text">The localized text</param>
        public LocalizedText(string key, string locale, string text)
        {
            m_locale = locale;
            m_text = text;

            if (!String.IsNullOrEmpty(key))
            {
                m_translationInfo = new TranslationInfo(key, locale, text);
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The locale used to create the text.
        /// </summary>
        /// <remarks>
        /// The locale used to create the text.
        /// </remarks>
        public string Locale => m_locale;

        /// <summary cref="LocalizedText.Locale" />
        [DataMember(Name = "Locale", Order = 1)]
        internal string XmlEncodedLocale
        {
            get { return m_locale; }
            set { m_locale = value; }
        }

        /// <summary>
        /// The localized text.
        /// </summary>
        /// <remarks>
        /// The localized text.
        /// </remarks>
        public string Text => m_text;

        /// <summary cref="LocalizedText.Text" />
        [DataMember(Name = "Text", Order = 2)]
        internal string XmlEncodedText
        {
            get { return m_text; }
            set { m_text = value; }
        }

        /// <summary>
        /// A key that can be used to look to the localized text in different locales.
        /// </summary>
        /// <remarks>
        /// This value is used within a process to facilite localization. It is not transmitted on the wire.
        /// </remarks>
        public string Key
        {
            get
            {
                if (m_translationInfo != null)
                {
                    return m_translationInfo.Key;
                }

                return null;
            }

            set
            {
                if (m_translationInfo != null)
                {
                    m_translationInfo.Key = value;
                    return;
                }

                m_translationInfo = new TranslationInfo(value, m_locale, m_text);
            }
        }

        /// <summary>
        /// The information required to translate the text into other locales.
        /// </summary>
        public TranslationInfo TranslationInfo
        {
            get { return m_translationInfo; }
            set { m_translationInfo = value; }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        /// <param name="obj">The object to compare to this</param>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            LocalizedText ltext = obj as LocalizedText;

            if (ltext == null)
            {
                return false;
            }

            if (ltext.m_locale != m_locale)
            {
                if (!(String.IsNullOrEmpty(ltext.m_locale) && String.IsNullOrEmpty(m_locale)))
                {
                    return false;
                }
            }

            return ltext.m_text == m_text;
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the two objects are equal.
        /// </remarks>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator ==(LocalizedText value1, LocalizedText value2)
        {
            if (!Object.ReferenceEquals(value1, null))
            {
                return value1.Equals(value2);
            }

            return Object.ReferenceEquals(value2, null);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the two objects are not equal.
        /// </remarks>
        /// <param name="value1">The first value to compare</param>
        /// <param name="value2">The second value to compare</param>
        public static bool operator !=(LocalizedText value1, LocalizedText value2)
        {
            if (!Object.ReferenceEquals(value1, null))
            {
                return !value1.Equals(value2);
            }

            return !Object.ReferenceEquals(value2, null);
        }

        /// <summary>
        /// Returns a suitable hash code for the object.
        /// </summary>
        /// <remarks>
        /// Returns a suitable hash code for the object.
        /// </remarks>
        public override int GetHashCode()
        {
            if (m_text != null)
            {
                return m_text.GetHashCode();
            }

            return 0;
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of the object.
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
        /// <param name="format">(Unused). Always pass NULL/NOTHING</param>
        /// <param name="formatProvider">(Unused). Always pass NULL/NOTHING</param>
        /// <exception cref="FormatException">Thrown if non-null parameters are used</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return String.Format(formatProvider, "{0}", this.m_text);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region ICloneable Members
        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <remarks>
        /// Makes a deep copy of the object.
        /// </remarks>
        public new object MemberwiseClone()
        {
            // this object cannot be altered after it is created so no new allocation is necessary.
            return this;
        }
        #endregion

        #region Static Methods
        /// <summary>
        /// Converts a string to a localized text.
        /// </summary>
        /// <remarks>
        /// Converts a string to a localized text.
        /// </remarks>
        /// <param name="value">The string to store as localized text</param>
        public static LocalizedText ToLocalizedText(string value)
        {
            return new LocalizedText(value);
        }

        /// <summary>
        /// Converts a string to a localized text.
        /// </summary>
        /// <remarks>
        /// Converts a string to a localized text.
        /// </remarks>
        /// <param name="value">The string to store as localized text</param>
        public static implicit operator LocalizedText(string value)
        {
            return new LocalizedText(value);
        }

        /// <summary>
        /// Returns an instance of a null LocalizedText.
        /// </summary>
        public static LocalizedText Null => s_Null;

        private static readonly LocalizedText s_Null = new LocalizedText();

        /// <summary>
        /// Returns true if the text is a null or empty string.
        /// </summary>
        public static bool IsNullOrEmpty(LocalizedText value)
        {
            if (value == null)
            {
                return true;
            }

            return String.IsNullOrEmpty(value.m_text);
        }
        #endregion

        #region Private Fields
        private string m_locale;
        private string m_text;
        private TranslationInfo m_translationInfo;
        #endregion
    }

    #region LocalizedTextCollection Class
    /// <summary>
    /// A collection of LocalizedText objects.
    /// </summary>
    /// <remarks>
    /// A strongly-typed collection of LocalizedText objects.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfLocalizedText", Namespace = Namespaces.OpcUaXsd, ItemName = "LocalizedText")]
    public partial class LocalizedTextCollection : List<LocalizedText>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public LocalizedTextCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        /// <param name="collection">The collection to copy into this new instance</param>
        public LocalizedTextCollection(IEnumerable<LocalizedText> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        /// <param name="capacity">The max capacity of this collection</param>
        public LocalizedTextCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">Array of localized text values to convert to a collection</param>
        public static LocalizedTextCollection ToLocalizedTextCollection(LocalizedText[] values)
        {
            if (values != null)
            {
                return new LocalizedTextCollection(values);
            }

            return new LocalizedTextCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">Array of localized text values to convert to a collection</param>
        public static implicit operator LocalizedTextCollection(LocalizedText[] values)
        {
            return ToLocalizedTextCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            LocalizedTextCollection clone = new LocalizedTextCollection(this.Count);

            foreach (LocalizedText element in this)
            {
                clone.Add((LocalizedText)Utils.Clone(element));
            }

            return clone;
        }
        #endregion
    }//class
}//namespace
