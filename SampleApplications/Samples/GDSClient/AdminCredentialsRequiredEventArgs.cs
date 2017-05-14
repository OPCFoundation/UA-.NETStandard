using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Opc.Ua.Gds
{
    /// <summary>
    /// The arguments passed with a AdminCredentialsRequiredEventArgs event.
    /// </summary>
    public class AdminCredentialsRequiredEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AdminCredentialsRequiredEventArgs"/> class.
        /// </summary>
        public AdminCredentialsRequiredEventArgs()
        {
        }

        /// <summary>
        /// Gets or sets the credentials.
        /// </summary>
        public UserIdentity Credentials { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating the credentials should be cached.
        /// </summary>
        public bool CacheCredentials { get; set; }
    }

    /// <summary>
    /// A delegate used to handle AdminCredentialsRequired events.
    /// </summary>
    public delegate void AdminCredentialsRequiredEventHandler(object sender, AdminCredentialsRequiredEventArgs e);
}
