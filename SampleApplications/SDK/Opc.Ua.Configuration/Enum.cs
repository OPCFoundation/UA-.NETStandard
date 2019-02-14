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
    #region ServiceStatus Enum
    /// <summary>
    /// Represents the service status.
    /// </summary>
    public enum ServiceStatus
    {
        /// <summary>
        /// The service is stopped
        /// </summary>
        Stopped,
        /// <summary>
        /// The service is going to process a start request
        /// </summary>
        StartPending,
        /// <summary>
        /// The service is going to process a stop request
        /// </summary>
        StopPending,
        /// <summary>
        /// The service started
        /// </summary>
        Running,
        /// <summary>
        /// The service is going to process a continue request
        /// </summary>
        ContinuePending,
        /// <summary>
        /// The service is going to process a pause request
        /// </summary>
        PausePending,
        /// <summary>
        /// The service is paused
        /// </summary>
        Paused,
        /// <summary>
        /// Unknown status
        /// </summary>
        Unknown
    }
    #endregion

    #region internal

    #region ServiceAccess Enum

    /// <summary>
    /// Access to the service. Before granting the requested access, the
    /// system checks the access token of the calling process. 
    /// </summary>
    [Flags]
    internal enum ServiceAccess : uint
    {
        /// <summary>
        /// Required to call the QueryServiceConfig and 
        /// QueryServiceConfig2 functions to query the service configuration.
        /// </summary>
        QueryConfig = 0x00001,

        /// <summary>
        /// Required to call the ChangeServiceConfig or ChangeServiceConfig2 function 
        /// to change the service configuration. Because this grants the caller 
        /// the right to change the executable file that the system runs, 
        /// it should be granted only to administrators.
        /// </summary>
        ChangeConfig = 0x00002,

        /// <summary>
        /// Required to call the QueryServiceStatusEx function to ask the service 
        /// control manager about the status of the service.
        /// </summary>
        QueryStatus = 0x00004,

        /// <summary>
        /// Required to call the EnumDependentServices function to enumerate all 
        /// the services dependent on the service.
        /// </summary>
        EnumerateDependents = 0x00008,

        /// <summary>
        /// Required to call the StartService function to start the service.
        /// </summary>
        Start = 0x00010,

        /// <summary>
        ///     Required to call the ControlService function to stop the service.
        /// </summary>
        Stop = 0x00020,

        /// <summary>
        /// Required to call the ControlService function to pause or continue 
        /// the service.
        /// </summary>
        PauseContinue = 0x00040,

        /// <summary>
        /// Required to call the EnumDependentServices function to enumerate all
        /// the services dependent on the service.
        /// </summary>
        Interrogate = 0x00080,

        /// <summary>
        /// Required to call the ControlService function to specify a user-defined
        /// control code.
        /// </summary>
        UserDefinedControl = 0x00100,

        /// <summary>
        /// Includes STANDARD_RIGHTS_REQUIRED in addition to all access rights in this table.
        /// </summary>
        AllAccess = (ACCESS_MASK.STANDARD_RIGHTS_REQUIRED |
            QueryConfig |
            ChangeConfig |
            QueryStatus |
            EnumerateDependents |
            Start |
            Stop |
            PauseContinue |
            Interrogate |
            UserDefinedControl),

        /// <summary>
        /// Generic read
        /// </summary>
        GenericRead = ACCESS_MASK.STANDARD_RIGHTS_READ |
            QueryConfig |
            QueryStatus |
            Interrogate |
            EnumerateDependents,

        /// <summary>
        /// Generic Write
        /// </summary>
        GenericWrite = ACCESS_MASK.STANDARD_RIGHTS_WRITE |
            ChangeConfig,

        /// <summary>
        /// Generic Execute
        /// </summary>
        GenericExecute = ACCESS_MASK.STANDARD_RIGHTS_EXECUTE |
            Start |
            Stop |
            PauseContinue |
            UserDefinedControl,

        /// <summary>
        /// Required to call the QueryServiceObjectSecurity or 
        /// SetServiceObjectSecurity function to access the SACL. The proper
        /// way to obtain this access is to enable the SE_SECURITY_NAME 
        /// privilege in the caller's current access token, open the handle 
        /// for ACCESS_SYSTEM_SECURITY access, and then disable the privilege.
        /// </summary>
        SystemSecurity = ACCESS_MASK.ACCESS_SYSTEM_SECURITY,

        /// <summary>
        /// Required to call the DeleteService function to delete the service.
        /// </summary>
        Delete = ACCESS_MASK.DELETE,

        /// <summary>
        /// Required to call the QueryServiceObjectSecurity function to query
        /// the security descriptor of the service object.
        /// </summary>
        ReadControl = ACCESS_MASK.READ_CONTROL,

        /// <summary>
        /// Required to call the SetServiceObjectSecurity function to modify
        /// the Dacl member of the service object's security descriptor.
        /// </summary>
        WriteDac = ACCESS_MASK.WRITE_DAC,

        /// <summary>
        /// Required to call the SetServiceObjectSecurity function to modify 
        /// the Owner and Group members of the service object's security 
        /// descriptor.
        /// </summary>
        WriteOwner = ACCESS_MASK.WRITE_OWNER,
    }

    [Flags]
    internal enum ACCESS_MASK : uint
    {
        DELETE = 0x00010000,
        READ_CONTROL = 0x00020000,
        WRITE_DAC = 0x00040000,
        WRITE_OWNER = 0x00080000,
        SYNCHRONIZE = 0x00100000,

        STANDARD_RIGHTS_REQUIRED = 0x000f0000,

        STANDARD_RIGHTS_READ = 0x00020000,
        STANDARD_RIGHTS_WRITE = 0x00020000,
        STANDARD_RIGHTS_EXECUTE = 0x00020000,

        STANDARD_RIGHTS_ALL = 0x001f0000,

        SPECIFIC_RIGHTS_ALL = 0x0000ffff,

        ACCESS_SYSTEM_SECURITY = 0x01000000,

        MAXIMUM_ALLOWED = 0x02000000,

        GENERIC_READ = 0x80000000,
        GENERIC_WRITE = 0x40000000,
        GENERIC_EXECUTE = 0x20000000,
        GENERIC_ALL = 0x10000000,

        DESKTOP_READOBJECTS = 0x00000001,
        DESKTOP_CREATEWINDOW = 0x00000002,
        DESKTOP_CREATEMENU = 0x00000004,
        DESKTOP_HOOKCONTROL = 0x00000008,
        DESKTOP_JOURNALRECORD = 0x00000010,
        DESKTOP_JOURNALPLAYBACK = 0x00000020,
        DESKTOP_ENUMERATE = 0x00000040,
        DESKTOP_WRITEOBJECTS = 0x00000080,
        DESKTOP_SWITCHDESKTOP = 0x00000100,

        WINSTA_ENUMDESKTOPS = 0x00000001,
        WINSTA_READATTRIBUTES = 0x00000002,
        WINSTA_ACCESSCLIPBOARD = 0x00000004,
        WINSTA_CREATEDESKTOP = 0x00000008,
        WINSTA_WRITEATTRIBUTES = 0x00000010,
        WINSTA_ACCESSGLOBALATOMS = 0x00000020,
        WINSTA_EXITWINDOWS = 0x00000040,
        WINSTA_ENUMERATE = 0x00000100,
        WINSTA_READSCREEN = 0x00000200,

        WINSTA_ALL_ACCESS = 0x0000037f
    }

    #endregion

    #region ServiceType Enum
    /// <summary>
    /// Service types.
    /// </summary>
    [Flags]
    internal enum ServiceType : uint
    {
        /// <summary>
        /// Driver service.
        /// </summary>
        KernelDriver = 0x00000001,

        /// <summary>
        /// File system driver service.
        /// </summary>
        FileSystemDriver = 0x00000002,

        /// <summary>
        /// Service that runs in its own process.
        /// </summary>
        OwnProcess = 0x00000010,

        /// <summary>
        /// Service that shares a process with one or more other services.
        /// </summary>
        ShareProcess = 0x00000020,

        /// <summary>
        /// The service can interact with the desktop.
        /// </summary>
        InteractiveProcess = 0x00000100,
    }

    #endregion

    #region ServiceError Enum

    /// <summary>
    /// Severity of the error, and action taken, if this service fails 
    /// to start.
    /// </summary>
    internal enum ServiceError
    {
        /// <summary>
        /// The startup program ignores the error and continues the startup
        /// operation.
        /// </summary>
        ErrorIgnore = 0x00000000,

        /// <summary>
        /// The startup program logs the error in the event log but continues
        /// the startup operation.
        /// </summary>
        ErrorNormal = 0x00000001,

        /// <summary>
        /// The startup program logs the error in the event log. If the 
        /// last-known-good configuration is being started, the startup 
        /// operation continues. Otherwise, the system is restarted with 
        /// the last-known-good configuration.
        /// </summary>
        ErrorSevere = 0x00000002,

        /// <summary>
        /// The startup program logs the error in the event log, if possible.
        /// If the last-known-good configuration is being started, the startup
        /// operation fails. Otherwise, the system is restarted with the 
        /// last-known good configuration.
        /// </summary>
        ErrorCritical = 0x00000003,
    }


    #endregion

    #endregion
}
