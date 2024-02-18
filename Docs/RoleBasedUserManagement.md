# Role Based User Management

## Overview

OPC UA Supports role based user management in a way that assigns permissions to nodes.
Those permissions are then assigned to a role.
The role is assigned to one or multiple Identities by the Server.

Since #2444 the OPC UA .NET Standard Stack implements the well known roles from:
https://reference.opcfoundation.org/Core/Part3/v105/docs/4.9.2

- Anonymous
- AuthenticatedUser
- Observer
- Operator
- Engineer
- Supervisor
- ConfigureAdmin
- SecurityAdmin

## Implementation

To get started using well known roles in your server the first thing you have to do is returning a RoleBasedIdentity in the overriden SessionManager_ImpersonateUser Method.

https://github.com/OPCFoundation/UA-.NETStandard/blob/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Applications/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs#L229C22-L229C52

https://github.com/OPCFoundation/UA-.NETStandard/blob/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Applications/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs#L327

You can add as many roles to your returned identity as needed.

All well knwon roles are created as static properties in the Role class of the server:

https://github.com/OPCFoundation/UA-.NETStandard/blob/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Libraries/Opc.Ua.Server/RoleBasedUserManagement/RoleBasedIdentity.cs

If you want to add additional roles you can refer to the GDS implementation which adds some user defined roles.

To make it easier to implement a real user name / pw implementation, avoiding hardcoded passwords, the Server Library provides an interface and a sample implementation for a users database:

https://github.com/OPCFoundation/UA-.NETStandard/tree/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Libraries/Opc.Ua.Server/RoleBasedUserManagement/UserDatabase

'''
public interface IUserDatabase
    {
        /// <summary>
        /// Initialize User Database
        /// </summary>
        void Initialize();
        /// <summary>
        /// Register new user
        /// </summary>
        /// <param name="userName">the username</param>
        /// <param name="password">the password</param>
        /// <param name="roles">the role of the new user</param>
        /// <returns>true if registered sucessfull</returns>
        bool CreateUser(string userName, string password, IEnumerable<Role> roles);
        /// <summary>
        /// Delete existring user
        /// </summary>
        /// <param name="userName">the user to delete</param>
        /// <returns>true if deleted sucessfully</returns>
        bool DeleteUser(string userName);
        /// <summary>
        /// checks if the provided credentials fit a user
        /// </summary>
        /// <param name="userName">the username</param>
        /// <param name="password">the password</param>
        /// <returns>true if userName + PW combination is correct</returns>
        bool CheckCredentials(string userName, string password);
        /// <summary>
        /// returns the Role of the provided user
        /// </summary>
        /// <param name="userName"></param>
        /// <returns>the Role of the provided users</returns>
        /// <exception cref="ArgumentException">When the user is not found</exception>
        IEnumerable<Role> GetUserRoles(string userName);
        /// <summary>
        /// changes the password of an existing users
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="oldPassword"></param>
        /// <param name="newPassword"></param>
        /// <returns>true if change was sucessfull</returns>
        bool ChangePassword(string userName, string oldPassword, string newPassword);
    }
'''

An implementation targeting SQL Server using Entity Framework 6 is available in the Samples Repo:
https://github.com/OPCFoundation/UA-.NETStandard-Samples/blob/e100ac787507988da95223a031af76fe57b5e11d/Samples/GDS/Server/SqlUsersDatabase.cs

To enable the authorization for your servers methods you can take a look at the HasApplicationSecureAdminAccess method of the ConfigurationNodeManager:

https://github.com/OPCFoundation/UA-.NETStandard/blob/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Libraries/Opc.Ua.Server/Configuration/ConfigurationNodeManager.cs#L300C9-L319C10

This method verifies the current session has the needed roles to access the methods.

## GDS

The GDS supports some additional well known roles starting with #2338

https://reference.opcfoundation.org/GDS/v105/docs/6.2
https://reference.opcfoundation.org/GDS/v105/docs/7.2

- DiscoveryAdmin
- SecurityAdmin
- CertificateAuthorityAdmin
- RegistrationAuthorityAdmin

https://github.com/OPCFoundation/UA-.NETStandard/blob/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Libraries/Opc.Ua.Gds.Server.Common/GdsRole.cs


Additionally the ApplicationSelfAdmin privilege is supported.
In the UA .NET Standard Stack the ApplicationSelfAdmin privilege is implemented using a user defined role.

To store its users the GDS implements the IUserDatabase interface from the server library.

https://github.com/OPCFoundation/UA-.NETStandard/tree/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Libraries/Opc.Ua.Server/RoleBasedUserManagement/UserDatabase

The JsonUserDatabase path is stored in the GDS Configuration:
https://github.com/OPCFoundation/UA-.NETStandard/blob/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Libraries/Opc.Ua.Gds.Server.Common/GlobalDiscoveryServerConfiguration.cs#L91

The GDS allows library users to supply their own implementation using the constructor:
https://github.com/OPCFoundation/UA-.NETStandard/blob/61edad9d6361b566baa5fdd69a23e7ac58c3433d/Libraries/Opc.Ua.Gds.Server.Common/GlobalDiscoverySampleServer.cs#L55C2-L63C10

## Limitations

- Does not really make use of the permissions <-> roles 
- Does not implement the RoleBasedSecurity Information Model from: https://reference.opcfoundation.org/Core/Part18/v105/docs/