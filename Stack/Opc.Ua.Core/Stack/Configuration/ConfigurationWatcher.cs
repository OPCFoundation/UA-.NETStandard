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
using System.IO;

namespace Opc.Ua
{   
    /// <summary>
    /// Watches the configuration file and reports any changes.
    /// </summary>
    public class ConfigurationWatcher : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Creates the watcher for the configuration.
        /// </summary>
        public ConfigurationWatcher(ApplicationConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            FileInfo fileInfo = new FileInfo(configuration.SourceFilePath);

            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("Could not load configuration file", configuration.SourceFilePath);
            }

            m_configuration = configuration;
            m_lastWriteTime = fileInfo.LastWriteTimeUtc;
            m_watcher = new System.Threading.Timer(Watcher_Changed, null, 5000, 5000);
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing) 
            {
                if (m_watcher != null)
                {
                    m_watcher.Dispose();
                    m_watcher = null;
                }
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Raised when the configuration file changes.
        /// </summary>
        public event EventHandler<ConfigurationWatcherEventArgs> Changed
        {
            add
            {
                lock (m_lock)
                {
                    m_Changed += value;
                }
            }

            remove
            {
                lock (m_lock)
                {
                    m_Changed -= value;
                }
            }
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Handles a file changed event.
        /// </summary>
        private void Watcher_Changed(object state)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(m_configuration.SourceFilePath);

                if (!fileInfo.Exists)
                {
                    return;
                }

                if (fileInfo.LastWriteTimeUtc <= m_lastWriteTime)
                {
                    return;
                }

                m_lastWriteTime = fileInfo.LastWriteTimeUtc;

                EventHandler<ConfigurationWatcherEventArgs> callback = m_Changed;

                if (callback != null)
                {
                    callback(this, new ConfigurationWatcherEventArgs(m_configuration, m_configuration.SourceFilePath));
                }
            }
            catch (Exception exception)
            {
                Utils.LogError(exception, "Unexpected error raising configuration file changed event.");
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ApplicationConfiguration m_configuration;
        private System.Threading.Timer m_watcher;
        private DateTime m_lastWriteTime;
        private event EventHandler<ConfigurationWatcherEventArgs> m_Changed;
        #endregion
    }
    
    #region ConfigurationWatcherEventArgs Class
    /// <summary>
    /// Stores the arguments passed when the configuration file changes.
    /// </summary>
    public class ConfigurationWatcherEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with a configuration and a file path.
        /// </summary>
        public ConfigurationWatcherEventArgs(
            ApplicationConfiguration configuration,
            string filePath)
        {
            m_configuration = configuration;
            m_filePath = filePath;
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The application configuration which changed.
        /// </summary>
        public ApplicationConfiguration Configuration
        {
            get { return m_configuration; }
        }
        
        /// <summary>
        /// The path to the application configuration file.
        /// </summary>
        public string FilePath
        {
            get { return m_filePath; }
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private string m_filePath;
        #endregion
    }
    #endregion
}
