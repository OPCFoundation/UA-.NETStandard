/* ========================================================================
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

using System.Runtime.Serialization;
using System.IO;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Specifies how to configure an application during installation.
    /// </summary>
    public partial class InstalledApplication
    {
        #region Public Methods
        /// <summary>
        /// Loads the application configuration from a configuration section.
        /// </summary>
        public static InstalledApplicationCollection Load(string filePath)
        {
            FileInfo file = new FileInfo(filePath);

            // look in current directory.
            if (!file.Exists)
            {
                file = new FileInfo(Utils.Format("{0}{1}{2}", Directory.GetCurrentDirectory(), Path.DirectorySeparatorChar, filePath));
            }

            // look in executable directory.
            if (!file.Exists)
            {
                file = new FileInfo(Utils.GetAbsoluteFilePath(filePath));
            }

            // file not found.
            if (!file.Exists)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "File does not exist: {0}\r\nCurrent directory is: {1}",
                    filePath,
                    Directory.GetCurrentDirectory());
            }

            return Load(file);
        }

        /// <summary>
        /// Loads a collection of security applications.
        /// </summary>
        public static InstalledApplicationCollection Load(FileInfo file)
        {
            FileStream reader = file.Open(FileMode.Open, FileAccess.Read);

            try
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(InstalledApplicationCollection));
                return serializer.ReadObject(reader) as InstalledApplicationCollection;
            }
            finally
            {
                reader.Dispose();
            }
        }
        #endregion
    }
}
