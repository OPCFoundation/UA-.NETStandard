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

namespace Opc.Ua
{
    /// <summary>
    /// An interface to a object that can translate itself.
    /// </summary>
    public interface ITranslatableObject
    {
        /// <summary>
        /// Returns a copy of the object with translateable strings replaced.
        /// </summary>
        /// <param name="manager">The manager which provides the translations.</param>
        /// <param name="preferredLocales">The locales to use.</param>
        /// <returns>A copy of the object</returns>
        /// <remarks>
        /// The copy is not necessarily a deep copy and may reference components of the original object.
        /// The original object is not changed.
        /// </remarks>
        ITranslatableObject Translate(ITranslationManager manager, IList<string> preferredLocales);
    }

    /// <summary>
    /// An interface to a object that provides translations.
    /// </summary>
    public interface ITranslationManager
    {
        /// <summary>
        /// Translates the text and then formats it with the arguments provided.
        /// </summary>
        /// <param name="preferredLocales">The list of preferred locales</param>
        /// <param name="key">The key used to look up the translation</param>
        /// <param name="text">The text to translate</param>
        /// <param name="args">The format argumente</param>
        /// <returns>The translated text</returns>
        /// <remarks>
        /// If any error occur during format the unformatted text is used instead.
        /// </remarks>
        LocalizedText Translate(IList<string> preferredLocales, string key, string text, params object[] args);

        /// <summary>
        /// Translates the LocalizedText using the information in the TranslationInfo property.
        /// </summary>
        /// <seealso cref="Translate(IList{string},string,string,object[])" />
        LocalizedText Translate(IList<string> preferredLocales, LocalizedText text);

        /// <summary>
        /// Translates a service result.
        /// </summary>
        /// <param name="preferredLocales">The preferred locales.</param>
        /// <param name="result">The result.</param>
        /// <returns>The result with all localized text translated.</returns>
        /// <remarks>Recusively translates text in inner results.</remarks>
        ServiceResult Translate(IList<string> preferredLocales, ServiceResult result);
    }

    /// <summary>
    /// Stores the information requires to translate a string.
    /// </summary>
    public class TranslationInfo
    {
        #region Constructors
        /// <summary>
        /// Creates an empty object.
        /// </summary>
        public TranslationInfo()
        {
        }

        /// <summary>
        /// Creates an object from a key and a LocalizedText.
        /// </summary>
        public TranslationInfo(string key, LocalizedText text)
        {
            m_key = key;

            if (text != null)
            {
                m_text = text.Text;
                m_locale = text.Locale;
            }
        }

        /// <summary>
        /// Stores the arguments for uses with a SymbolicId that is used to look up default text. 
        /// </summary>
        public TranslationInfo(System.Xml.XmlQualifiedName symbolicId, params object[] args)
        {
            m_key = symbolicId.ToString();
            m_locale = String.Empty;
            m_text = String.Empty;
            m_args = args;
        }

        /// <summary>
        /// Creates an object from a key and a text.
        /// </summary>
        public TranslationInfo(string key, string locale, string text)
        {
            m_key = key;
            m_locale = locale;
            m_text = text;
        }

        /// <summary>
        /// Creates an object from a key with text and format arguements.
        /// </summary>
        public TranslationInfo(string key, string locale, string format, params object[] args)
        {
            m_key = key;
            m_locale = locale;
            m_text = format;
            m_args = args;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The key used to look up translations.
        /// </summary>
        public string Key
        {
            get { return m_key; }
            set { m_key = value; }
        }

        /// <summary>
        /// The default locale for the text.
        /// </summary>
        public string Locale
        {
            get { return m_locale; }
            set { m_locale = value; }
        }

        /// <summary>
        /// The text to translate.
        /// </summary>
        public string Text
        {
            get { return m_text; }
            set { m_text = value; }
        }

        /// <summary>
        /// The arguments that are used when formatting the text after translation.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public object[] Args
        {
            get { return m_args; }
            set { m_args = value; }
        }
        #endregion

        #region Private Fields
        private string m_key;
        private string m_locale;
        private string m_text;
        private object[] m_args;
        #endregion
    }

}
