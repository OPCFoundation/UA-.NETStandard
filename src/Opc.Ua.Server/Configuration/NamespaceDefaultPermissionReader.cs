/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Resolves a <see cref="NamespaceMetadataState"/> without awaiting, so synchronous node
    /// managers can read namespace defaults without blocking on asynchronous lookups.
    /// </summary>
    internal interface INamespaceMetadataSnapshot
    {
        /// <summary>
        /// Returns <c>true</c> when the metadata node of <paramref name="namespaceIndex"/>, or its
        /// definitive absence (<paramref name="state"/> is <c>null</c>), is known without awaiting.
        /// Returns <c>false</c> when the answer requires an asynchronous lookup.
        /// </summary>
        bool TryGetNamespaceMetadataState(ushort namespaceIndex, out NamespaceMetadataState? state);
    }

    /// <summary>
    /// The single reader of the namespace defaults (<c>DefaultAccessRestrictions</c>,
    /// <c>DefaultRolePermissions</c> and <c>DefaultUserRolePermissions</c>, OPC 10000-5 6.3.13)
    /// used by permission checks. Values are read with the caller's system context, so
    /// <c>OnReadValue</c>/<c>OnSimpleReadValue</c> callbacks see the caller. A missing property
    /// or a null value is reported as absent, a configured zero/empty value as present. A failed
    /// read, a callback exception, a wrong type or unknown restriction bits produce a Bad
    /// <see cref="NamespaceDefaultPermissions.Status"/>, which callers must treat as a denial and
    /// which is never cached.
    /// </summary>
    internal static class NamespaceDefaultPermissionReader
    {
        /// <summary>
        /// All AccessRestrictionType bits defined by OPC 10000-3 8.56.
        /// </summary>
        internal const ushort kKnownAccessRestrictionBits =
            (ushort)(AccessRestrictionType.SigningRequired |
                AccessRestrictionType.EncryptionRequired |
                AccessRestrictionType.SessionRequired |
                AccessRestrictionType.ApplyRestrictionsToBrowse);

        /// <summary>
        /// The current generation of namespace defaults. Any observed change of a default value,
        /// of the default properties or of the tracked metadata nodes moves it, which retires
        /// every value remembered by an <see cref="OperationContext"/>.
        /// </summary>
        public static long Generation => Interlocked.Read(ref s_generation);

        /// <summary>
        /// Retires all remembered namespace defaults.
        /// </summary>
        public static void Invalidate()
        {
            Interlocked.Increment(ref s_generation);
        }

        /// <summary>
        /// Reads the namespace defaults of <paramref name="namespaceIndex"/> for the caller of
        /// <paramref name="callerContext"/>, using the request cache of its operation context.
        /// </summary>
        public static async ValueTask<NamespaceDefaultPermissions> ReadAsync(
            IServerInternal server,
            ServerSystemContext callerContext,
            ushort namespaceIndex,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (callerContext == null)
            {
                throw new ArgumentNullException(nameof(callerContext));
            }

            OperationContext? operationContext = callerContext.OperationContext;
            if (operationContext != null &&
                operationContext.TryGetDefaultPermissions(namespaceIndex, out NamespaceDefaultPermissions cached))
            {
                return cached;
            }

            long generation = Generation;
            NamespaceMetadataState? state = null;
            IConfigurationNodeManager? configuration = server.NodeManager?.ConfigurationNodeManager;
            if (configuration != null)
            {
                try
                {
                    state = await configuration.GetNamespaceMetadataStateAsync(namespaceIndex, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    return Failed(namespaceIndex, StatusCodes.BadInternalError, generation);
                }
            }

            NamespaceDefaultPermissions result = Decode(callerContext, state, namespaceIndex, generation);
            operationContext?.SetDefaultPermissions(result);
            return result;
        }

        /// <summary>
        /// Reads the namespace defaults of <paramref name="namespaceIndex"/> without awaiting.
        /// When the metadata node cannot be resolved without an asynchronous lookup the result
        /// is <see cref="StatusCodes.BadWouldBlock"/>, never an unrestricted default.
        /// </summary>
        public static NamespaceDefaultPermissions Read(
            IServerInternal server,
            ServerSystemContext callerContext,
            ushort namespaceIndex)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (callerContext == null)
            {
                throw new ArgumentNullException(nameof(callerContext));
            }

            OperationContext? operationContext = callerContext.OperationContext;
            if (operationContext != null &&
                operationContext.TryGetDefaultPermissions(namespaceIndex, out NamespaceDefaultPermissions cached))
            {
                return cached;
            }

            long generation = Generation;
            NamespaceMetadataState? state = null;
            IConfigurationNodeManager? configuration = server.NodeManager?.ConfigurationNodeManager;
            if (configuration != null)
            {
                if (configuration is not INamespaceMetadataSnapshot snapshot)
                {
                    return Failed(namespaceIndex, StatusCodes.BadWouldBlock, generation);
                }

                try
                {
                    if (!snapshot.TryGetNamespaceMetadataState(namespaceIndex, out state))
                    {
                        return Failed(namespaceIndex, StatusCodes.BadWouldBlock, generation);
                    }
                }
                catch (Exception)
                {
                    return Failed(namespaceIndex, StatusCodes.BadInternalError, generation);
                }
            }

            NamespaceDefaultPermissions result = Decode(callerContext, state, namespaceIndex, generation);
            operationContext?.SetDefaultPermissions(result);
            return result;
        }

        /// <summary>
        /// Decodes the defaults of an already resolved metadata node with the caller's context,
        /// without consulting or filling any cache.
        /// </summary>
        public static NamespaceDefaultPermissions Read(
            ServerSystemContext callerContext,
            NamespaceMetadataState? state,
            ushort namespaceIndex)
        {
            if (callerContext == null)
            {
                throw new ArgumentNullException(nameof(callerContext));
            }

            return Decode(callerContext, state, namespaceIndex, Generation);
        }

        /// <summary>
        /// Decodes the three default properties.
        /// </summary>
        private static NamespaceDefaultPermissions Decode(
            ServerSystemContext callerContext,
            NamespaceMetadataState? state,
            ushort namespaceIndex,
            long generation)
        {
            if (state == null)
            {
                return new NamespaceDefaultPermissions(
                    namespaceIndex, AccessRestrictionType.None, default, default, generation);
            }

            StatusCode status = ReadValue(callerContext, state.DefaultAccessRestrictions, out Variant restrictionsValue);
            if (StatusCode.IsBad(status))
            {
                return Failed(namespaceIndex, status, generation);
            }

            bool hasRestrictions = !restrictionsValue.IsNull;
            AccessRestrictionType restrictions = AccessRestrictionType.None;
            if (hasRestrictions)
            {
                status = DecodeAccessRestrictions(restrictionsValue, out restrictions);
                if (StatusCode.IsBad(status))
                {
                    return Failed(namespaceIndex, status, generation);
                }
            }

            status = ReadRolePermissions(
                callerContext,
                state.DefaultRolePermissions,
                out bool hasRoles,
                out ArrayOf<RolePermissionType> roles);
            if (StatusCode.IsBad(status))
            {
                return Failed(namespaceIndex, status, generation);
            }

            status = ReadRolePermissions(
                callerContext,
                state.DefaultUserRolePermissions,
                out bool hasUserRoles,
                out ArrayOf<RolePermissionType> userRoles);
            if (StatusCode.IsBad(status))
            {
                return Failed(namespaceIndex, status, generation);
            }

            return new NamespaceDefaultPermissions(
                namespaceIndex,
                restrictions,
                roles,
                userRoles,
                generation,
                StatusCodes.Good,
                hasRestrictions,
                hasRoles,
                hasUserRoles);
        }

        /// <summary>
        /// Decodes a configured AccessRestrictionType from its UInt16 OptionSet encoding, or an
        /// enumeration/Int32 encoding of an in-range value.
        /// </summary>
        internal static StatusCode DecodeAccessRestrictions(Variant value, out AccessRestrictionType restrictions)
        {
            restrictions = AccessRestrictionType.None;
            int bits;
            if (value.TryGetValue(out ushort uint16))
            {
                bits = uint16;
            }
            else if (value.TryGetValue(out AccessRestrictionType enumeration))
            {
                bits = (int)enumeration;
            }
            else if (value.TryGetValue(out int int32))
            {
                bits = int32;
            }
            else
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (bits < 0 || (bits & ~kKnownAccessRestrictionBits) != 0)
            {
                return StatusCodes.BadOutOfRange;
            }

            restrictions = (AccessRestrictionType)bits;
            return StatusCodes.Good;
        }

        /// <summary>
        /// Reads a role permission default property.
        /// </summary>
        private static StatusCode ReadRolePermissions(
            ServerSystemContext callerContext,
            BaseVariableState? property,
            out bool present,
            out ArrayOf<RolePermissionType> rolePermissions)
        {
            present = false;
            rolePermissions = default;
            StatusCode status = ReadValue(callerContext, property, out Variant value);
            if (StatusCode.IsBad(status) || value.IsNull)
            {
                return status;
            }

            if (!value.TryGetStructure(out rolePermissions))
            {
                rolePermissions = default;
                return StatusCodes.BadTypeMismatch;
            }

            present = true;
            return StatusCodes.Good;
        }

        /// <summary>
        /// Resolves the metadata node of <paramref name="namespaceIndex"/> without awaiting and
        /// reports whether its defaults are stored values. A default served by a read callback can
        /// change without moving <see cref="Generation"/>, so callers that remember a verdict
        /// derived from the defaults must re-read them on every use.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="namespaceIndex">The namespace index.</param>
        /// <param name="state">The metadata node, or <c>null</c> when the namespace has none.</param>
        /// <returns>
        /// <c>true</c> when the node (or its absence) is known without awaiting and none of its
        /// default properties has a read callback.
        /// </returns>
        internal static bool TryGetStaticDefaults(
            IServerInternal server,
            ushort namespaceIndex,
            out NamespaceMetadataState? state)
        {
            state = null;
            IConfigurationNodeManager? configuration = server.NodeManager?.ConfigurationNodeManager;
            if (configuration == null)
            {
                return true;
            }
            try
            {
                return configuration is INamespaceMetadataSnapshot snapshot &&
                    snapshot.TryGetNamespaceMetadataState(namespaceIndex, out state) &&
                    !HasReadCallback(state);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when a default property of the metadata node has a read callback.
        /// </summary>
        /// <param name="state">The metadata node, or <c>null</c>.</param>
        internal static bool HasReadCallback(NamespaceMetadataState? state)
        {
            return state != null &&
                (HasReadCallback(state.DefaultAccessRestrictions) ||
                    HasReadCallback(state.DefaultRolePermissions) ||
                    HasReadCallback(state.DefaultUserRolePermissions));
        }

        private static bool HasReadCallback(BaseVariableState? property)
        {
            return property != null &&
                (property.OnReadValue != null ||
                    property.OnSimpleReadValue != null ||
                    property.OnReadValueAsync != null ||
                    property.OnSimpleReadValueAsync != null);
        }

        /// <summary>
        /// Reads a property value with the caller's context; a missing property is a null value.
        /// </summary>
        private static StatusCode ReadValue(
            ServerSystemContext callerContext,
            BaseVariableState? property,
            out Variant value)
        {
            value = default;
            if (property == null)
            {
                return StatusCodes.Good;
            }

            try
            {
                DateTimeUtc sourceTimestamp = DateTimeUtc.MinValue;
                ServiceResult result = property.ReadAttribute(
                    callerContext,
                    Attributes.Value,
                    ref value,
                    ref sourceTimestamp);
                if (ServiceResult.IsBad(result))
                {
                    value = default;
                    return result.StatusCode;
                }

                return StatusCodes.Good;
            }
            catch (Exception)
            {
                value = default;
                return StatusCodes.BadInternalError;
            }
        }

        /// <summary>
        /// Creates a failed result that carries no usable defaults.
        /// </summary>
        private static NamespaceDefaultPermissions Failed(ushort namespaceIndex, StatusCode status, long generation)
        {
            return new NamespaceDefaultPermissions(
                namespaceIndex,
                AccessRestrictionType.None,
                default,
                default,
                generation,
                status,
                false,
                false,
                false);
        }

        private static long s_generation;
    }
}