﻿/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

using System.IO;
using Newtonsoft.Json;

namespace Opc.Ua.Gds.Server.Database.Linq
{
    public class JsonApplicationsDatabase : LinqApplicationsDatabase
    {
        #region Constructors
        public JsonApplicationsDatabase(string fileName)
        {
            m_fileName = fileName;
        }
        static public JsonApplicationsDatabase Load(string fileName)
        {
            try
            {
                string json = File.ReadAllText(fileName);
                JsonApplicationsDatabase db = JsonConvert.DeserializeObject<JsonApplicationsDatabase>(json);
                db.FileName = fileName;
                return db;
            }
            catch
            {
                return new JsonApplicationsDatabase(fileName);
            }
        }
        #endregion
        #region Public Members
        public override void Save()
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(m_fileName, json);
        }
        [JsonIgnore]
        public string FileName { get { return m_fileName; } private set { m_fileName = value; } }
        #endregion
        #region Private Fields
        [JsonIgnore]
        string m_fileName;
        #endregion
    }
}
