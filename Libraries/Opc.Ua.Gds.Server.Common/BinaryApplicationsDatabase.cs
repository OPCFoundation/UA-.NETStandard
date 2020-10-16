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
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace Opc.Ua.Gds.Server.Database.Linq
{
    [Serializable]
    public class BinaryApplicationsDatabase : LinqApplicationsDatabase
    {
        #region Constructors
        public BinaryApplicationsDatabase(string fileName)
        {
            m_fileName = fileName;
        }
        static public BinaryApplicationsDatabase Load(string fileName)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                Stream stream = new FileStream(fileName,
                    FileMode.Open, FileAccess.Read, FileShare.Read);
                BinaryApplicationsDatabase db = (BinaryApplicationsDatabase)formatter.Deserialize(stream);
                stream.Close();
                db.FileName = fileName;
                return db;
            }
            catch
            {
                return new BinaryApplicationsDatabase(fileName);
            }
        }
        #endregion
        #region Public Members
        public override void Save()
        {
            IFormatter formatter = new BinaryFormatter();
            Stream stream = new FileStream(m_fileName,
                FileMode.Create, FileAccess.Write, FileShare.None);
            formatter.Serialize(stream, this);
            stream.Close();
        }
        public string FileName { get { return m_fileName; } private set { m_fileName = value; } }
        #endregion
        #region Private Fields
        [NonSerialized]
        string m_fileName;
        #endregion
    }
}
