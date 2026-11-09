/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Dependency
{
    /// <summary>
    /// Security attributes retained when a declaration is reconstructed from a referenced model.
    /// </summary>
    public sealed class DependencySecurity
    {
        /// <summary>
        /// The declaration's WriteMask.
        /// </summary>
        public uint WriteAccess { get; set; }

        /// <summary>
        /// The independently configured UserWriteMask, or null for the legacy WriteMask fallback.
        /// </summary>
        public uint? UserWriteAccess { get; set; }

        /// <summary>
        /// The explicitly configured inverse Executable attribute, or null when unspecified.
        /// </summary>
        public bool? NonExecutable { get; set; }

        /// <summary>
        /// The independently configured UserExecutable attribute, or null for the Executable fallback.
        /// </summary>
        public bool? UserExecutable { get; set; }

        /// <summary>
        /// The declaration's ACL, including wire identities for roles not generated in the consumer.
        /// </summary>
        public RolePermissionSet? RolePermissions { get; set; }

        /// <summary>
        /// The ACL applied to instances of the declaration.
        /// </summary>
        public RolePermissionSet? DefaultRolePermissions { get; set; }

        /// <summary>
        /// The declaration's channel restrictions, or null when unspecified.
        /// </summary>
        public AccessRestrictions? AccessRestrictions { get; set; }

        /// <summary>
        /// The channel restrictions applied to instances, or null when unspecified.
        /// </summary>
        public AccessRestrictions? DefaultAccessRestrictions { get; set; }

        /// <summary>
        /// Captures effective security metadata with namespace-qualified role identities.
        /// </summary>
        internal static DependencySecurity? FromNode(NodeDesign node, IModelDesign model)
        {
            var method = node as MethodDesign;
            if (node.WriteAccess == 0 &&
                !node.UserWriteAccessSpecified &&
                method?.NonExecutableSpecified != true &&
                method?.UserExecutableSpecified != true &&
                node.RolePermissions == null &&
                node.DefaultRolePermissions == null &&
                !node.AccessRestrictionsSpecified &&
                !node.DefaultAccessRestrictionsSpecified)
            {
                return null;
            }
            return new DependencySecurity
            {
                WriteAccess = node.WriteAccess,
                UserWriteAccess = node.UserWriteAccessSpecified ? node.UserWriteAccess : null,
                NonExecutable = method?.NonExecutableSpecified == true ? method.NonExecutable : null,
                UserExecutable = method?.UserExecutableSpecified == true ? method.UserExecutable : null,
                RolePermissions = ResolveRoleIds(node.RolePermissions, model),
                DefaultRolePermissions = ResolveRoleIds(node.DefaultRolePermissions, model),
                AccessRestrictions = node.AccessRestrictionsSpecified ? node.AccessRestrictions : null,
                DefaultAccessRestrictions = node.DefaultAccessRestrictionsSpecified
                    ? node.DefaultAccessRestrictions
                    : null
            };
        }

        /// <summary>
        /// Restores the security attributes without treating unspecified values as explicit overrides.
        /// </summary>
        internal void ApplyTo(NodeDesign node)
        {
            node.WriteAccess = WriteAccess;
            node.UserWriteAccess = UserWriteAccess.GetValueOrDefault();
            node.UserWriteAccessSpecified = UserWriteAccess.HasValue;
            node.RolePermissions = RolePermissions!;
            node.DefaultRolePermissions = DefaultRolePermissions!;
            node.AccessRestrictions = AccessRestrictions.GetValueOrDefault();
            node.AccessRestrictionsSpecified = AccessRestrictions.HasValue;
            node.DefaultAccessRestrictions = DefaultAccessRestrictions.GetValueOrDefault();
            node.DefaultAccessRestrictionsSpecified = DefaultAccessRestrictions.HasValue;
            if (node is MethodDesign method)
            {
                method.NonExecutable = NonExecutable.GetValueOrDefault();
                method.NonExecutableSpecified = NonExecutable.HasValue;
                method.UserExecutable = UserExecutable.GetValueOrDefault();
                method.UserExecutableSpecified = UserExecutable.HasValue;
            }
        }

        /// <summary>
        /// Retains each permission even when the role itself is absent from the dependency type table.
        /// </summary>
        private static RolePermissionSet? ResolveRoleIds(RolePermissionSet? input, IModelDesign model)
        {
            if (input == null)
            {
                return null;
            }
            RolePermission[]? permissions = null;
            if (input.RolePermission != null)
            {
                permissions = new RolePermission[input.RolePermission.Length];
                for (int index = 0; index < permissions.Length; index++)
                {
                    RolePermission permission = input.RolePermission[index];
                    permissions[index] = new RolePermission
                    {
                        Role = permission.Role,
                        RoleId = permission.ResolveRoleId(model).FormatRoleId(),
                        Permission = permission.Permission
                    };
                }
            }
            return new RolePermissionSet
            {
                Name = input.Name,
                DoNotInheirit = input.DoNotInheirit,
                RolePermission = permissions!
            };
        }
    }
}
