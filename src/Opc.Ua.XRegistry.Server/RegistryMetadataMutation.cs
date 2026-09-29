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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// A fully validated metadata candidate. The store commits it before a projection may expose it.
    /// </summary>
    public readonly record struct RegistryMetadataCommit(
        RegistryObjectValueDataType Document,
        uint TargetEpoch,
        bool Changed);

    /// <summary>
    /// Shared resulting-state metadata validation, exact no-op comparison and revision advancement.
    /// Hosts authorize requests before calling this layer and commit its result through their state store.
    /// </summary>
    public static class RegistryMetadataMutation
    {
        /// <summary>
        /// Applies native Set/Remove operations to one existing Group or metadata Resource.
        /// All mutations are prepared on a copy; any failure leaves source unchanged.
        /// </summary>
        public static RegistryMetadataCommit Apply(
            RegistryObjectValueDataType source,
            RegistryChangeRequestDataType request,
            ArrayOf<string> collections,
            ArrayOf<string> protectedPaths,
            Action<RegistryObjectValueDataType> validate)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            return Prepare(source, request.TargetXid!, request.ExpectedEpoch, collections, protectedPaths, validate,
                previous => previous is null
                    ? throw new ServiceResultException(StatusCodes.BadNotFound)
                    : (RegistryObjectValueDataType)RegistryValues.ApplyChanges(previous, request.Changes));
        }

        /// <summary>
        /// Registers or replaces one complete metadata entity, preserving server-owned fields.
        /// </summary>
        public static RegistryMetadataCommit Replace(
            RegistryObjectValueDataType source,
            string targetXid,
            RegistryObjectValueDataType definition,
            uint expectedEpoch,
            ArrayOf<string> collections,
            ArrayOf<string> protectedPaths,
            Action<RegistryObjectValueDataType> validate)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            RegistryValues.Validate(definition);
            return Prepare(source, targetXid, expectedEpoch, collections, protectedPaths, validate,
                _ => (RegistryObjectValueDataType)definition.Clone());
        }

        /// <summary>
        /// Applies the optional RFC 7396 compatibility patch through the same commit rules.
        /// A null object member removes it; it does not perform native Set(null).
        /// </summary>
        public static RegistryMetadataCommit Patch(
            RegistryObjectValueDataType source,
            string targetXid,
            RegistryObjectValueDataType patch,
            uint expectedEpoch,
            ArrayOf<string> collections,
            ArrayOf<string> protectedPaths,
            Action<RegistryObjectValueDataType> validate)
        {
            RegistryValues.Validate(patch);
            CheckPatch(patch);
            return Prepare(source, targetXid, expectedEpoch, collections, protectedPaths, validate,
                previous => (RegistryObjectValueDataType)Merge(previous, patch));
        }

        /// <summary>
        /// Deletes a metadata entity without changing any live transport or credential.
        /// </summary>
        public static RegistryMetadataCommit Delete(
            RegistryObjectValueDataType source,
            string targetXid,
            uint expectedEpoch,
            ArrayOf<string> collections,
            ArrayOf<string> protectedPaths,
            Action<RegistryObjectValueDataType> validate)
        {
            return Prepare(source, targetXid, expectedEpoch, collections, protectedPaths, validate,
                previous => previous is null
                    ? throw new ServiceResultException(StatusCodes.BadNotFound)
                    : null);
        }

        /// <summary>
        /// Reads a committed entity epoch without rounding or substituting a parent revision.
        /// </summary>
        public static uint Epoch(RegistryObjectValueDataType entity)
        {
            if (Get(entity, "epoch") is not RegistryNumberValueDataType number)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The entity has no committed epoch.");
            }
            byte[] bytes = number.Coefficient.ToArray();
            Array.Reverse(bytes);
            var coefficient = new BigInteger(bytes);
            if (coefficient.Sign <= 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The committed epoch is outside UInt32.");
            }
            string digits = coefficient.ToString(CultureInfo.InvariantCulture);
            if (number.Exponent >= 0)
            {
                if (number.Exponent > 9 || digits.Length + number.Exponent > 10)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The committed epoch is outside UInt32.");
                }
                digits += new string('0', (int)number.Exponent);
            }
            else
            {
                if (number.Exponent < -(long)digits.Length)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The committed epoch is not integral.");
                }
                int remove = checked((int)-number.Exponent);
                for (int index = digits.Length - remove; index < digits.Length; index++)
                {
                    if (digits[index] != '0')
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState, "The committed epoch is not integral.");
                    }
                }
                digits = digits.Substring(0, digits.Length - remove);
            }
            if (!uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out uint epoch) || epoch == 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The committed epoch is outside UInt32.");
            }
            return epoch;
        }

        private static RegistryMetadataCommit Prepare(
            RegistryObjectValueDataType source,
            string targetXid,
            uint expectedEpoch,
            ArrayOf<string> collections,
            ArrayOf<string> protectedPaths,
            Action<RegistryObjectValueDataType> validate,
            Func<RegistryObjectValueDataType?, RegistryObjectValueDataType?> change)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (validate is null)
            {
                throw new ArgumentNullException(nameof(validate));
            }
            RegistryValues.Validate(source);
            string[] path = TargetPath(targetXid, collections);
            RegistryObjectValueDataType? previous = Find(source, path) as RegistryObjectValueDataType;
            if (expectedEpoch != 0 && (previous is null || Epoch(previous) != expectedEpoch))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "ExpectedEpoch differs from the target.");
            }
            RegistryObjectValueDataType? replacement = change(previous);
            if (replacement is not null)
            {
                Owned(previous, replacement);
            }
            var current = (RegistryObjectValueDataType)source.Clone();
            RegistryObjectValueDataType? collection = Get(current, path[0]) as RegistryObjectValueDataType;
            if (collection is null)
            {
                if (path.Length != 2 || replacement is null)
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                }
                collection = new RegistryObjectValueDataType { Kind = 5, Members = [] };
                Set(current, path[0], collection);
            }
            RegistryObjectValueDataType owner = collection;
            string key = path[1];
            if (path.Length == 4)
            {
                if (Get(collection, key) is not RegistryObjectValueDataType group)
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound, "The parent Group does not exist.");
                }
                if (Get(group, "messages") is not RegistryObjectValueDataType resources)
                {
                    resources = new RegistryObjectValueDataType { Kind = 5, Members = [] };
                    Set(group, "messages", resources);
                }
                owner = resources;
                key = path[3];
            }
            Set(owner, key, replacement);
            foreach (string protectedPath in protectedPaths)
            {
                string[] parts = TargetPath(protectedPath, collections);
                RegistryValueDataType? oldValue = Find(source, parts);
                RegistryValueDataType? newValue = Find(current, parts);
                if (oldValue is null || newValue is null || !RegistryValues.Identical(oldValue, newValue))
                {
                    throw new ServiceResultException(StatusCodes.BadNotWritable,
                        "A surfaced entity cannot be changed through a direct or ancestor mutation.");
                }
            }
            if (RegistryValues.Identical(source, current))
            {
                return new RegistryMetadataCommit((RegistryObjectValueDataType)source.Clone(),
                    previous is null ? 0 : Epoch(previous), false);
            }
            foreach (string name in collections)
            {
                if (Get(current, name) is not RegistryObjectValueDataType groups)
                {
                    continue;
                }
                foreach (RegistryMemberDataType groupEntry in groups.Members)
                {
                    if (groupEntry.Value is not RegistryObjectValueDataType group)
                    {
                        throw new ArgumentException("A registry Group must be an object.");
                    }
                    RegistryObjectValueDataType? oldGroup = Find(source, [name, groupEntry.Name!])
                        as RegistryObjectValueDataType;
                    if (Get(group, "messages") is RegistryObjectValueDataType resources)
                    {
                        foreach (RegistryMemberDataType resourceEntry in resources.Members)
                        {
                            if (resourceEntry.Value is not RegistryObjectValueDataType resource)
                            {
                                throw new ArgumentException("A metadata Resource must be an object.");
                            }
                            RegistryObjectValueDataType? old = oldGroup is null
                                ? null
                                : Find(oldGroup, ["messages", resourceEntry.Name!]) as RegistryObjectValueDataType;
                            if (old is null || !RegistryValues.Identical(old, resource))
                            {
                                Set(resource, "epoch", Number(Next(old)));
                            }
                        }
                        if (Get(group, "messagescount") is not null)
                        {
                            Set(group, "messagescount", Number((uint)resources.Members.Count));
                        }
                    }
                    if (oldGroup is null || !RegistryValues.Identical(oldGroup, group))
                    {
                        Set(group, "epoch", Number(Next(oldGroup)));
                    }
                }
                if (Get(current, name + "count") is not null)
                {
                    Set(current, name + "count", Number((uint)groups.Members.Count));
                }
            }
            Set(current, "epoch", Number(Next(source)));
            validate(current);
            RegistryValues.Validate(current);
            uint revision = Find(current, path) is RegistryObjectValueDataType target ? Epoch(target) : 0;
            return new RegistryMetadataCommit(current, revision, true);
        }

        private static void Owned(RegistryObjectValueDataType? previous, RegistryObjectValueDataType current)
        {
            foreach (string name in s_owned)
            {
                RegistryValueDataType? old = previous is null ? null : Get(previous, name);
                RegistryValueDataType? next = Get(current, name);
                if ((old is null) != (next is null) ||
                    (old is not null && next is not null && !RegistryValues.Identical(old, next)))
                {
                    throw new ServiceResultException(StatusCodes.BadNotWritable, "A server-owned field changed: " + name);
                }
            }
            foreach (string collectionName in s_children)
            {
                if (Get(current, collectionName) is not RegistryObjectValueDataType children)
                {
                    continue;
                }
                foreach (RegistryMemberDataType child in children.Members)
                {
                    if (child.Value is RegistryObjectValueDataType entity)
                    {
                        Owned(previous is null ? null :
                            Find(previous, [collectionName, child.Name!]) as RegistryObjectValueDataType, entity);
                    }
                }
            }
        }

        private static void CheckPatch(RegistryObjectValueDataType patch)
        {
            foreach (string name in s_owned)
            {
                if (Get(patch, name) is not null)
                {
                    throw new ServiceResultException(StatusCodes.BadNotWritable,
                        "A compatibility patch cannot set a server-owned field: " + name);
                }
            }
            foreach (string name in s_children)
            {
                if (Get(patch, name) is RegistryObjectValueDataType children)
                {
                    foreach (RegistryMemberDataType child in children.Members)
                    {
                        if (child.Value is RegistryObjectValueDataType entity)
                        {
                            CheckPatch(entity);
                        }
                    }
                }
            }
        }

        private static RegistryValueDataType Merge(RegistryValueDataType? previous, RegistryValueDataType patch)
        {
            if (patch is not RegistryObjectValueDataType updates)
            {
                return (RegistryValueDataType)patch.Clone();
            }
            var result = previous is RegistryObjectValueDataType existing
                ? (RegistryObjectValueDataType)existing.Clone()
                : new RegistryObjectValueDataType { Kind = 5, Members = [] };
            foreach (RegistryMemberDataType member in updates.Members)
            {
                Set(result, member.Name!, member.Value is RegistryNullValueDataType
                    ? null
                    : Merge(Get(result, member.Name!), member.Value));
            }
            return result;
        }

        private static uint Next(RegistryObjectValueDataType? previous)
        {
            if (previous is null)
            {
                return 1;
            }
            uint epoch = Epoch(previous);
            return epoch == uint.MaxValue
                ? throw new ServiceResultException(StatusCodes.BadOutOfRange, "The metadata epoch is exhausted.")
                : epoch + 1;
        }

        private static RegistryNumberValueDataType Number(uint value)
        {
            byte[] bytes = new BigInteger(value).ToByteArray();
            Array.Reverse(bytes);
            return new RegistryNumberValueDataType
            {
                Kind = 3,
                Coefficient = ByteString.From(bytes),
                IsInteger = true
            };
        }

        private static string[] TargetPath(string xid, ArrayOf<string> collections)
        {
            if (string.IsNullOrEmpty(xid) || xid[0] != '/')
            {
                throw new ArgumentException("A concrete collection-qualified Xid is required.", nameof(xid));
            }
            string[] parts = xid.Substring(1).Split('/');
            bool supported = false;
            foreach (string collection in collections)
            {
                supported |= collection == parts[0];
            }
            if (!supported || (parts.Length != 2 && parts.Length != 4) ||
                (parts.Length == 4 && parts[2] != "messages"))
            {
                throw new ArgumentException("The Xid must select a declared Group or metadata Resource.", nameof(xid));
            }
            foreach (string part in parts)
            {
                if (part.Length == 0 || part is "." or "..")
                {
                    throw new ArgumentException("Invalid registry Xid segment.", nameof(xid));
                }
            }
            return parts;
        }

        private static RegistryValueDataType? Find(RegistryObjectValueDataType source, string[] path)
        {
            RegistryValueDataType? value = source;
            foreach (string part in path)
            {
                value = value is RegistryObjectValueDataType map ? Get(map, part) : null;
            }
            return value;
        }

        private static RegistryValueDataType? Get(RegistryObjectValueDataType source, string name)
        {
            foreach (RegistryMemberDataType member in source.Members)
            {
                if (member.Name == name)
                {
                    return member.Value;
                }
            }
            return null;
        }

        private static void Set(RegistryObjectValueDataType source, string name, RegistryValueDataType? value)
        {
            var members = new List<RegistryMemberDataType>();
            bool found = false;
            foreach (RegistryMemberDataType member in source.Members)
            {
                if (member.Name != name)
                {
                    members.Add(member);
                }
                else
                {
                    found = true;
                    if (value is not null)
                    {
                        members.Add(new RegistryMemberDataType { Name = name, Value = value });
                    }
                }
            }
            if (!found && value is not null)
            {
                members.Add(new RegistryMemberDataType { Name = name, Value = value });
            }
            source.Members = members.ToArray();
        }

        private static readonly string[] s_owned = ["epoch", "xid", "createdat", "modifiedat", "self"];
        private static readonly string[] s_children = ["messages", "versions"];
    }
}
