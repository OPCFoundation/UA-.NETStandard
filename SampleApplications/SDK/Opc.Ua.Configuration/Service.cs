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

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Serialization;

namespace Opc.Ua.Configuration
{    
    #region Service class
    /// <summary>
    /// Represents a windows service
    /// </summary>
    public class Service
    {
        #region Constructor
        /// <summary>
        /// Constructor for <see cref="Service"/>
        /// </summary>
        public Service() 
        {
        }

        /// <summary>
        /// Constructor for <see cref="Service"/>
        /// </summary>
        /// <param name="name">The service name.</param>
        public Service(string name)
        { 
            this.m_name = name;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The service name (Windows identifier for the service)
        /// </summary>
        public string Name 
        { 
            get { return m_name;  } 
            set { m_name = value; } 
        }
        
        /// <summary>
        /// The service Display name (the friendly name showed by the Windows Service manager). 
        /// </summary>
        public string DisplayName 
        { 
            get { return m_displayName;  } 
            set { m_displayName = value; }
        }

        /// <summary>
        /// The service caption (usually equals to display name)
        /// </summary>
        public string Caption 
        { 
            get { return m_caption;  } 
            set { m_caption = value; } 
        }
        
        /// <summary>
        /// The service local path 
        /// </summary>
        public string Path 
        { 
            get { return m_path;  } 
            set { m_path = value; } 
        }        
        
        /// <summary>
        /// The service start mode.
        /// </summary>
        public StartMode StartMode 
        { 
            get { return m_startMode;  } 
            set { m_startMode = value; } 
        }
        
        /// <summary>
        /// Account name under which a service runs.
        /// Depending on the service type, the account name may be in the form of DomainName\Username
        /// </summary>
        public string Account 
        { 
            get { return m_account;  } 
            set { m_account = value; }
        }

        /// <summary>
        /// The service description.
        /// </summary>
        public string Description 
        { 
            get { return m_description;  } 
            set { m_description = value; } 
        }
        
        /// <summary>
        /// The processor affinity for this service.
        /// </summary>
        /// <remarks>
        /// If the system has 2 processor and the service is running on processor 2 the affinity bit mask will be : [true][false]
        /// If the system has 2 processor and the service is running on both processors the affinity bit mask will be : [true][true]
        /// </remarks>
        public bool[] ProcessorAffinity 
        { 
            get { return m_processorAffinity;  } 
            set { m_processorAffinity = value; } 
        }

        /// <summary>
        /// Indicates whether the service can be paused
        /// </summary>
        public bool AcceptPause 
        { 
            get { return m_acceptPause;  } 
            set { m_acceptPause = value; } 
        }
        
        /// <summary>
        /// Indicates whether the service can be stopped
        /// </summary>
        public bool AcceptStop 
        { 
            get { return m_acceptStop;  } 
            set { m_acceptStop = value; } 
        }
        #endregion
        
        #region Dynamic Properties
        /// <summary>
        /// The service process. Zero if not running. 
        /// </summary>
        public int ProcessId 
        { 
            get { return m_processId;  } 
            set { m_processId = value; }
        }

        /// <summary>
        /// The service status.
        /// </summary>
        public ServiceStatus Status 
        { 
            get { return m_status;  } 
            set { m_status = value; } 
        }
        #endregion

        #region Private Fields
        private string m_name = null;
        private string m_displayName = null;
        private string m_caption = null;
        private string m_path = null;
        private int m_processId = 0;
        private StartMode m_startMode = StartMode.Auto;
        private ServiceStatus m_status = ServiceStatus.Unknown;
        private string m_account = null;
        private string m_description = null;
        private bool[] m_processorAffinity = null;
        private bool m_acceptPause = true;
        private bool m_acceptStop = true;
        #endregion
    }
    #endregion
}
