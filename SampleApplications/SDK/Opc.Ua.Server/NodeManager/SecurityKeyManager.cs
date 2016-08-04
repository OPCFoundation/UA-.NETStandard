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

namespace Opc.Ua.Server
{
    public class SecurityKeyManager
    {
        private object m_lock = new object();
        private Dictionary<string, SecurityGroup> m_groups;
        private Random m_rng;

        public SecurityKeyManager()
        {
            m_groups = new Dictionary<string, SecurityGroup>();
            m_rng = new Random();
        }

        public SecurityGroup New(
            string groupId, 
            string securityPolicyUri, 
            DateTime startTime, 
            TimeSpan lifeTime,
            IList<string> allowedScopes)
        {
            lock (m_lock)
            {
                SecurityGroup group = new SecurityGroup()
                {
                    Id = groupId,
                    SecurityPolicyUri = securityPolicyUri,
                    AllowedScopes = allowedScopes
                };

                group.Initialize(startTime, lifeTime, m_rng);
                m_groups[groupId] = group;

                return group;
            }
        }

        public void Delete(string groupId)
        {
            lock (m_lock)
            {
                m_groups.Remove(groupId);
            }
        }

        public SecurityGroup Find(string groupId)
        {
            SecurityGroup group = null;

            if (!m_groups.TryGetValue(groupId, out group))
            {
                return null;
            }

            return group;
        }

        public StatusCode GetSecurityKeys(
           ISystemContext context,
           string securityGroupId,
           uint futureKeyCount,
           ref string securityPolicyUri,
           ref uint currentTokenId,
           ref byte[] currentKey,
           ref byte[][] nextKeys,
           ref uint timeToNextKey,
           ref uint keyLifetime)
        {
            // check for encryption.
            var endpoint = SecureChannelContext.Current.EndpointDescription;

            if (endpoint == null || (endpoint.SecurityPolicyUri == SecurityPolicies.None && !endpoint.EndpointUrl.StartsWith(Utils.UriSchemeHttps)) || endpoint.SecurityMode == MessageSecurityMode.Sign)
            {
                return StatusCodes.BadSecurityModeInsufficient;
            }

            // check for group.
            var group = Find(securityGroupId);

            if (group == null)
            {
                return StatusCodes.BadNotFound;
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

            keyLifetime = (uint)group.KeyLifetime.TotalSeconds;
            timeToNextKey = (uint)(group.KeyLifetime - (DateTime.UtcNow - group.CurrentKeyIssueTime)).TotalSeconds;

            return StatusCodes.Good;
        }
    }

    public class SecurityGroup
    {
        private object m_lock = new object();
        private LinkedList<byte[]> m_keys;
        private Random m_rng;

        public string Id { get; internal set; }

        public string SecurityPolicyUri { get; internal set; }

        public IList<string> AllowedScopes { get; internal set; }

        public uint TokenId { get; private set; }

        public uint KeySize { get; private set; }

        public byte[] CurrentKey { get; private set; }

        public TimeSpan KeyLifetime { get; private set; }

        public DateTime CurrentKeyIssueTime { get; private set; }

        public void Initialize(DateTime startTime, TimeSpan lifeTime, Random rng)
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
                rng.NextBytes(CurrentKey);
                m_keys = new LinkedList<byte[]>();
                m_keys.AddLast(CurrentKey);
                m_rng = rng;
            }
        }

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
                    m_rng.NextBytes(key);
                    m_keys.AddLast(key);
                }

                CurrentKey = m_keys.First.Value;
            }
        }
    }
}
