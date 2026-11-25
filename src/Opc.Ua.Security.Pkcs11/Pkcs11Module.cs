// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// Coordinates the native library lifetime and token-wide login state without
    /// sharing any application sessions or object handles.
    /// </summary>
    internal sealed class Pkcs11Module
    {
        private Pkcs11Module(Registry registry, string path, IPkcs11Library library)
        {
            m_registry = registry;
            m_path = path;
            Library = library;
        }

        /// <summary>
        /// The library shared by every live acquisition of the module.
        /// </summary>
        public IPkcs11Library Library { get; }

        /// <summary>
        /// Takes a reference to the initialized library, loading it if necessary.
        /// </summary>
        public static Pkcs11Module Acquire(
            IPkcs11LibraryLoader loader,
            Pkcs11InteropFactories factories,
            string modulePath)
        {
            string path = Pkcs11ModulePath.Canonicalize(modulePath);
            Registry registry = s_registries.GetValue(loader, static _ => new Registry());
            return registry.Acquire(loader, factories, path);
        }

        /// <summary>
        /// Opens a separate session on the slot and joins only a compatible login.
        /// </summary>
        public ISession OpenSession(ISlot slot, ulong slotId, string? pin)
        {
            byte[]? pinBytes = string.IsNullOrEmpty(pin) ? null : ConvertUtils.Utf8StringToBytes(pin);
            byte[]? proof = null;
            try
            {
                if (pinBytes != null)
                {
                    using var hmac = new HMACSHA256(s_proofKey);
                    proof = hmac.ComputeHash(pinBytes);
                }

                lock (m_sessionGate)
                {
                    m_slots.TryGetValue(slotId, out Login? login);
                    if (login != null && !login.Matches(proof))
                    {
                        throw new CryptographicException(
                            "The requested PKCS#11 login is incompatible with active sessions on this token.");
                    }

                    ISession session = slot.OpenSession(SessionType.ReadWrite);
                    try
                    {
                        if (login == null && pinBytes != null)
                        {
                            // An already-logged-in response cannot validate this credential.
                            session.Login(CKU.CKU_USER, pinBytes);
                        }
                        else
                        {
                            CKS expected = proof == null ? CKS.CKS_RW_PUBLIC_SESSION : CKS.CKS_RW_USER_FUNCTIONS;
                            if (session.GetSessionInfo().State != expected)
                            {
                                throw new CryptographicException(
                                    "The PKCS#11 token is not in the expected login state.");
                            }
                        }

                        if (login == null)
                        {
                            login = new Login(proof);
                            m_slots.Add(slotId, login);
                            proof = null;
                        }

                        login.Sessions++;
                        return session;
                    }
                    catch
                    {
                        CloseSession(session);
                        throw;
                    }
                }
            }
            finally
            {
                Clear(pinBytes);
                Clear(proof);
            }
        }

        /// <summary>
        /// Closes one owned session and logs out only when its login ends.
        /// </summary>
        public void ReleaseSession(ulong slotId, ISession session)
        {
            lock (m_sessionGate)
            {
                Login login = m_slots[slotId];
                try
                {
                    if (--login.Sessions == 0)
                    {
                        m_slots.Remove(slotId);
                        login.End(session);
                    }
                }
                finally
                {
                    CloseSession(session);
                }
            }
        }

        /// <summary>
        /// Releases a library reference, finalizing only after the last is released.
        /// </summary>
        public void Release()
        {
            m_registry.Release(this);
        }

        private static void CloseSession(ISession session)
        {
            try
            {
                session.Dispose();
            }
            catch (Pkcs11Exception)
            {
                // A removed token may have already closed its sessions.
            }
        }

        private static void Clear(byte[]? bytes)
        {
            if (bytes != null)
            {
                CryptoUtils.ZeroMemory(bytes);
            }
        }

        private static byte[] CreateProofKey()
        {
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            return key;
        }

        private static readonly ConditionalWeakTable<IPkcs11LibraryLoader, Registry> s_registries = new();
        private static readonly byte[] s_proofKey = CreateProofKey();
        private readonly Registry m_registry;
        private readonly string m_path;
        private readonly Lock m_sessionGate = new();
        private readonly Dictionary<ulong, Login> m_slots = [];
        private int m_references;

        private sealed class Registry
        {
            public Pkcs11Module Acquire(IPkcs11LibraryLoader loader, Pkcs11InteropFactories factories, string path)
            {
                lock (m_gate)
                {
                    if (!m_modules.TryGetValue(path, out Pkcs11Module? module))
                    {
                        module = new Pkcs11Module(this, path, loader.Load(factories, path));
                        m_modules.Add(path, module);
                    }

                    module.m_references++;
                    return module;
                }
            }

            public void Release(Pkcs11Module module)
            {
                lock (m_gate)
                {
                    if (--module.m_references == 0)
                    {
                        m_modules.Remove(module.m_path);
                        module.Library.Dispose();
                    }
                }
            }

            private readonly Lock m_gate = new();
            private readonly Dictionary<string, Pkcs11Module> m_modules = new(Pkcs11ModulePath.Comparer);
        }

        private sealed class Login(byte[]? proof)
        {
            public int Sessions { get; set; }

            public bool Matches(byte[]? candidate)
            {
                return proof == null
                    ? candidate == null
                    : candidate != null && CryptoUtils.FixedTimeEquals(proof, candidate);
            }

            public void End(ISession session)
            {
                if (proof == null)
                {
                    return;
                }

                try
                {
                    session.Logout();
                }
                catch (Pkcs11Exception)
                {
                    // The session or the login may already be gone.
                }
                finally
                {
                    Clear(proof);
                }
            }
        }
    }
}
