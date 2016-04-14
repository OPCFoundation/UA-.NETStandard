/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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

using System.Runtime.Serialization;
using System.IO;
using Windows.Storage;

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
                file = new FileInfo(Utils.Format("{0}\\{1}", ApplicationData.Current.LocalFolder.Path, filePath));
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
                    ApplicationData.Current.LocalFolder.Path);
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
