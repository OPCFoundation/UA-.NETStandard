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

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Serialization;
using System.IO;
using System.Xml;
using System.Reflection;

namespace Opc.Ua.Configuration
{    
    /// <summary>
    /// Specifies how to configure an application during installation.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaSdk + "Installation.xsd")]
    public partial class InstalledApplication : Opc.Ua.Security.SecuredApplication
    {
    	#region Constructors
    	/// <summary>
    	/// The default constructor.
    	/// </summary>
        public InstalledApplication()
    	{
    		Initialize();
    	}
        
    	/// <summary>
    	/// Called by the .NET framework during deserialization.
    	/// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
    	{
    		Initialize();
    	}

    	/// <summary>
    	/// Sets private members to default values.
    	/// </summary>
    	private void Initialize()
    	{
            UseDefaultCertificateStores = true;
            DeleteCertificatesOnUninstall = true;
            ConfigureFirewall = false;
            SetConfigurationFilePermisions = true;
            SetExecutableFilePermissions = true;
            InstallAsService = false;
            ServiceStartMode = StartMode.Manual;
            ServiceUserName = null;
            ServicePassword = null;
            ServiceDescription = null;
            LocallyRegisterOIDs = false;
            MinimumKeySize = 1024;
            LifeTimeInMonths = 300;
    	}
    	#endregion

        #region Persistent Properties
        /// <summary>
        /// Whether to use the default stores.
        /// </summary>
        [DataMember(IsRequired = false, Order = 1)]
        public bool UseDefaultCertificateStores { get; set; }

        /// <summary>
        /// Whether to delete certificates on uninstall.
        /// </summary>
        [DataMember(IsRequired = false, Order = 2)]
        public bool DeleteCertificatesOnUninstall { get; set; }

        /// <summary>
        /// Whether to configure the firewall.
        /// </summary>
        [DataMember(IsRequired = false, Order = 3)]
        public bool ConfigureFirewall { get; set; }

        /// <summary>
        /// Whether to set configuration file permissions.
        /// </summary>
        [DataMember(IsRequired = false, Order = 4)]
        public bool SetConfigurationFilePermisions { get; set; }

        /// <summary>
        /// Whether to set configuration file permissions.
        /// </summary>
        [DataMember(IsRequired = false, Order = 5)]
        public bool SetExecutableFilePermissions { get; set; }

        /// <summary>
        /// Whether to install as a service.
        /// </summary>
        [DataMember(IsRequired = false, Order = 6)]
        public bool InstallAsService { get; set; }

        /// <summary>
        /// The start mode for the service.
        /// </summary>
        [DataMember(IsRequired = false, Order = 7)]
        public StartMode ServiceStartMode { get; set; }

        /// <summary>
        /// The user name for the service.
        /// </summary>
        [DataMember(IsRequired = false, Order = 8)]
        public string ServiceUserName { get; set; }

        /// <summary>
        /// The password for the service.
        /// </summary>
        [DataMember(IsRequired = false, Order = 9)]
        public string ServicePassword { get; set; }

        /// <summary>
        /// A human readable description for the service.
        /// </summary>
        [DataMember(IsRequired = false, Order = 10)]
        public string ServiceDescription { get; set; }

        /// <summary>
        /// Whether to locally register OIDs (use to work around a windows bug when in a domain).
        /// </summary>
        [DataMember(IsRequired = false, Order = 11)]
        public bool LocallyRegisterOIDs { get; set; }

        /// <summary>
        /// The minimum key size for the new certificate.
        /// </summary>
        [DataMember(IsRequired = false, Order = 12)]
        public ushort MinimumKeySize { get; set; }

        /// <summary>
        /// The lifetime for the new certificate.
        /// </summary>
        [DataMember(IsRequired = false, Order = 13)]
        public ushort LifeTimeInMonths { get; set; }

        /// <summary>
        /// Who has access to the critical files.
        /// </summary>
        [DataMember(IsRequired = false, Order = 14)]
        public ApplicationAccessRuleCollection AccessRules { get; set; }

        /// <summary>
        /// The trace configuration for the installed process.
        /// </summary>
        [DataMember(IsRequired = false, Order = 15)]
        public TraceConfiguration TraceConfiguration { get; set; }
        #endregion
    }

    #region InstalledApplicationCollection Class
    /// <summary>
    /// A collection of InstalledApplication objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfInstalledApplication", Namespace = Namespaces.OpcUaConfig, ItemName = "InstalledApplication")]
    public partial class InstalledApplicationCollection : List<InstalledApplication>
    {
        #region Constructors
        /// <summary>
        /// Initializes the collection with default values.
        /// </summary>
        public InstalledApplicationCollection() { }

        /// <summary>
        /// Initializes the collection with an initial capacity.
        /// </summary>
        public InstalledApplicationCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Initializes the collection with another collection.
        /// </summary>
        public InstalledApplicationCollection(IEnumerable<InstalledApplication> collection) : base(collection) { }
        #endregion
    }
    #endregion

    #region StartMode Enum
    /// <summary>
    /// Start mode of the Windows service
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaSdk + "Installation.xsd")]
    public enum StartMode : uint
    {
        /// <summary>
        /// Device driver started by the operating system loader (valid only for driver services).
        /// </summary>
        [EnumMember]
        Boot = 0x00000000,

        /// <summary>
        /// Device driver started by the operating system initialization process. This value is valid only for driver services.
        /// </summary>
        [EnumMember]
        System = 0x00000001,

        /// <summary>
        /// Service to be started automatically during system startup.
        /// </summary>
        [EnumMember]
        Auto = 0x00000002,

        /// <summary>
        /// Service to be started manually by a call to the StartService method.
        /// </summary>
        [EnumMember]
        Manual = 0x00000003,

        /// <summary>
        /// Service that can no longer be started.
        /// </summary>
        [EnumMember]
        Disabled = 0x00000004
    }
    #endregion
}
