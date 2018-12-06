/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;
 
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <remarks />
    public class SecurityKeyManager
    {
        private object m_lock = new object();
        private Dictionary<string, SecurityGroup> m_groups;
        private RandomNumberGenerator m_rng;

        /// <remarks />
        public SecurityKeyManager()
        {
            m_groups = new Dictionary<string, SecurityGroup>();
            m_rng = new RNGCryptoServiceProvider();
        }

        /// <remarks />
        public SecurityGroup New(
            string groupId, 
            string securityPolicyUri, 
            DateTime startTime, 
            TimeSpan lifeTime,
            IList<NodeId> allowedRoles)
        {
            lock (m_lock)
            {
                SecurityGroup group = new SecurityGroup()
                {
                    Id = groupId,
                    SecurityPolicyUri = securityPolicyUri,
                    AllowedRoles = allowedRoles
                };

                group.Initialize(startTime, lifeTime, m_rng);
                m_groups[groupId] = group;

                return group;
            }
        }

        /// <remarks />
        public void Delete(string groupId)
        {
            lock (m_lock)
            {
                m_groups.Remove(groupId);
            }
        }

        /// <remarks />
        public SecurityGroup Find(string groupId)
        {
            SecurityGroup group = null;

            if (!m_groups.TryGetValue(groupId, out group))
            {
                return null;
            }

            return group;
        }

        /// <remarks />
        public StatusCode GetSecurityKeys(
           ISystemContext context,
           string securityGroupId,
           uint futureKeyCount,
           ref string securityPolicyUri,
           ref uint currentTokenId,
           ref byte[] currentKey,
           ref byte[][] nextKeys,
           ref double timeToNextKey,
           ref double keyLifetime)
        {
            // check for encryption.
            var endpoint = SecureChannelContext.Current.EndpointDescription;

            if (endpoint == null || (endpoint.SecurityPolicyUri == SecurityPolicies.None && !endpoint.EndpointUrl.StartsWith(Uri.UriSchemeHttps)) || endpoint.SecurityMode == MessageSecurityMode.Sign)
            {
                return StatusCodes.BadSecurityModeInsufficient;
            }

            // check for group.
            var group = Find(securityGroupId);

            if (group == null)
            {
                return StatusCodes.BadNotFound;
            }

            // check access.
            bool allowed = false;

            RoleBasedIdentity identity = context.UserIdentity as RoleBasedIdentity;

            if (identity != null && group.AllowedRoles != null)
            {
                foreach (var role in group.AllowedRoles)
                {
                    if (identity.Roles != null && identity.Roles.Contains(role))
                    {
                        allowed = true;
                        break;
                    }
                }
            }

            if (!allowed)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            // get keys.
            group.GenerateKeys(futureKeyCount + 1);

            securityPolicyUri = group.SecurityPolicyUri;
            currentTokenId = group.TokenId;
            currentKey = group.CurrentKey;

            if (futureKeyCount > 0)
            {
                nextKeys = new byte[futureKeyCount][];
                group.GetFutureKeys(nextKeys);
            }

            keyLifetime = (uint)group.KeyLifetime.TotalMilliseconds;
            timeToNextKey = (uint)(group.KeyLifetime - (DateTime.UtcNow - group.CurrentKeyIssueTime)).TotalMilliseconds;

            return StatusCodes.Good;
        }
    }

    /// <remarks />
    public class SecurityGroup
    {
        private object m_lock = new object();
        private LinkedList<byte[]> m_keys;
        private RandomNumberGenerator m_rng;

        /// <remarks />
        public string Id { get; internal set; }

        /// <remarks />
        public string SecurityPolicyUri { get; internal set; }

        /// <remarks />
        public IList<NodeId> AllowedRoles { get; internal set; }

        /// <remarks />
        public uint TokenId { get; private set; }

        /// <remarks />
        public uint KeySize { get; private set; }

        /// <remarks />
        public byte[] CurrentKey { get; private set; }

        /// <remarks />
        public TimeSpan KeyLifetime { get; private set; }

        /// <remarks />
        public DateTime CurrentKeyIssueTime { get; private set; }

        /// <remarks />
        public void Initialize(DateTime startTime, TimeSpan lifeTime, RandomNumberGenerator rng)
        {
            lock (m_lock)
            {
                TokenId = 1;
                CurrentKeyIssueTime = startTime;
                KeyLifetime = lifeTime;

                switch (SecurityPolicyUri)
                {
                    case SecurityPolicies.Basic128Rsa15:
                    {
                        KeySize = 16 * 2 + 4;
                        break;
                    }

                    default:
                    case SecurityPolicies.Basic256Sha256:
                    {
                        KeySize = 32 * 2 + 4;
                        break;
                    }
                }

                CurrentKey = new byte[KeySize];
                rng.GetNonZeroBytes(CurrentKey);
                m_keys = new LinkedList<byte[]>();
                m_keys.AddLast(CurrentKey);
                m_rng = rng;
            }
        }

        /// <remarks />
        public void GetFutureKeys(byte[][] keys)
        {
            lock (m_lock)
            {
                int index = 0;

                if (m_keys.Count > 0)
                {
                    for (var ii = m_keys.First.Next; index < keys.Length && ii != null; ii = ii.Next)
                    {
                        keys[index++] = ii.Value;
                    }
                }
            }
        }

        /// <remarks />
        public void GenerateKeys(uint count)
        {
            lock (m_lock)
            {
                DateTime now = DateTime.UtcNow;

                while (now - CurrentKeyIssueTime > KeyLifetime)
                {
                    if (m_keys.Count > 0)
                    {
                        m_keys.RemoveFirst();
                    }

                    CurrentKeyIssueTime += KeyLifetime;
                    TokenId++;
                }

                while (m_keys.Count < count)
                {
                    var key = new byte[KeySize];
                    m_rng.GetNonZeroBytes(key);
                    m_keys.AddLast(key);
                }

                CurrentKey = m_keys.First.Value;
            }
        }
    }
}
