/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
            if (configuration == null) throw new ArgumentNullException("configuration");

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
                Utils.Trace(exception, "Unexpected error raising configuration file changed event.");
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
