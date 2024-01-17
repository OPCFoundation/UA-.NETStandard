/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using Newtonsoft.Json;

namespace Opc.Ua.Server.UserDatabase
{
    /// <summary>
    /// A user database with JSON storage.
    /// </summary>
    /// <remarks>
    /// This db is good for testing but not for production use.
    /// </remarks>
    public class JsonUserDatabase : LinqUserDatabase
    {
        #region Constructors
        /// <summary>
        /// Create a JSON database.
        /// </summary>
        public JsonUserDatabase(string fileName)
        {
            m_fileName = fileName;
        }

        /// <summary>
        /// Load the JSON application database.
        /// </summary>
        static public JsonUserDatabase Load(string fileName)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            try
            {
                if (File.Exists(fileName))
                {
                    string json = File.ReadAllText(fileName);
                    JsonUserDatabase db = JsonConvert.DeserializeObject<JsonUserDatabase>(json);
                    db.FileName = fileName;
                    return db;
                }
            }
            catch
            {

            }
            return new JsonUserDatabase(fileName);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Save the complete database.
        /// </summary>
        public override void Save()
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(m_fileName, json);
        }

        /// <summary>
        /// Get or set the filename.
        /// </summary>
        [JsonIgnore]
        public string FileName { get { return m_fileName; } private set { m_fileName = value; } }
        #endregion

        #region Private Fields
        [JsonIgnore]
        string m_fileName;
        #endregion
    }
}
