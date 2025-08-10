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
        /// <summary>
        /// Creates the watcher for the configuration.
        /// </summary>
        public ConfigurationWatcher(ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var fileInfo = new FileInfo(configuration.SourceFilePath);

            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("Could not load configuration file", configuration.SourceFilePath);
            }

            m_configuration = configuration;
            m_lastWriteTime = fileInfo.LastWriteTimeUtc;
            m_watcher = new System.Threading.Timer(Watcher_Changed, null, 5000, 5000);
        }

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
            if (disposing && m_watcher != null)
            {
                m_watcher.Dispose();
                m_watcher = null;
            }
        }

        /// <summary>
        /// Raised when the configuration file changes.
        /// </summary>
        public event EventHandler<ConfigurationWatcherEventArgs> Changed
        {
            add => m_Changed += value;
            remove => m_Changed -= value;
        }

        /// <summary>
        /// Handles a file changed event.
        /// </summary>
        private void Watcher_Changed(object state)
        {
            try
            {
                var fileInfo = new FileInfo(m_configuration.SourceFilePath);

                if (!fileInfo.Exists)
                {
                    return;
                }

                if (fileInfo.LastWriteTimeUtc <= m_lastWriteTime)
                {
                    return;
                }

                m_lastWriteTime = fileInfo.LastWriteTimeUtc;

                m_Changed?.Invoke(
                    this,
                    new ConfigurationWatcherEventArgs(m_configuration, m_configuration.SourceFilePath)
                );
            }
            catch (Exception exception)
            {
                Utils.LogError(exception, "Unexpected error raising configuration file changed event.");
            }
        }

        private readonly ApplicationConfiguration m_configuration;
        private System.Threading.Timer m_watcher;
        private DateTime m_lastWriteTime;
        private event EventHandler<ConfigurationWatcherEventArgs> m_Changed;
    }

    /// <summary>
    /// Stores the arguments passed when the configuration file changes.
    /// </summary>
    public class ConfigurationWatcherEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes the object with a configuration and a file path.
        /// </summary>
        public ConfigurationWatcherEventArgs(ApplicationConfiguration configuration, string filePath)
        {
            Configuration = configuration;
            FilePath = filePath;
        }

        /// <summary>
        /// The application configuration which changed.
        /// </summary>
        public ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// The path to the application configuration file.
        /// </summary>
        public string FilePath { get; }
    }
}
