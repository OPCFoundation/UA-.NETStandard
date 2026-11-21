// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Bounds the files a mounted file system keeps open through <c>FileType.Open</c>,
    /// per Session and in total. Every open holds a provider stream, and with it an
    /// operating system handle, until it is closed or its Session ends.
    /// </summary>
    internal sealed class FileOpenQuota
    {
        /// <summary>
        /// The default number of files one Session can keep open in one mount.
        /// </summary>
        public const int DefaultMaxOpenFilesPerSession = 64;

        /// <summary>
        /// The default number of files all Sessions together can keep open in one mount.
        /// </summary>
        public const int DefaultMaxOpenFiles = 1024;

        /// <summary>
        /// The files one Session can keep open; zero means no limit.
        /// </summary>
        public int MaxOpenFilesPerSession
        {
            get => Volatile.Read(ref m_maxPerSession);
            set => Volatile.Write(ref m_maxPerSession, ValidateLimit(value));
        }

        /// <summary>
        /// The files all Sessions together can keep open; zero means no limit.
        /// </summary>
        public int MaxOpenFiles
        {
            get => Volatile.Read(ref m_maxTotal);
            set => Volatile.Write(ref m_maxTotal, ValidateLimit(value));
        }

        /// <summary>
        /// The files open in all Sessions.
        /// </summary>
        public int OpenCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_total;
                }
            }
        }

        /// <summary>
        /// Takes one open for the Session, or reports which limit refuses it.
        /// </summary>
        public bool TryAcquire(NodeId sessionId, out ServiceResult error)
        {
            int maxPerSession = MaxOpenFilesPerSession;
            int maxTotal = MaxOpenFiles;
            lock (m_lock)
            {
                if (maxTotal > 0 && m_total >= maxTotal)
                {
                    error = ServiceResult.Create(
                        StatusCodes.BadTooManyOperations,
                        "The limit of open files was reached.");
                    return false;
                }
                m_perSession.TryGetValue(sessionId, out int sessionCount);
                if (maxPerSession > 0 && sessionCount >= maxPerSession)
                {
                    error = ServiceResult.Create(
                        StatusCodes.BadTooManyOperations,
                        "The limit of open files per Session was reached.");
                    return false;
                }
                m_perSession[sessionId] = sessionCount + 1;
                m_total++;
            }
            error = ServiceResult.Good;
            return true;
        }

        /// <summary>
        /// Returns one open taken by <see cref="TryAcquire"/>.
        /// </summary>
        public void Release(NodeId sessionId)
        {
            lock (m_lock)
            {
                if (!m_perSession.TryGetValue(sessionId, out int sessionCount))
                {
                    return;
                }
                if (sessionCount <= 1)
                {
                    m_perSession.Remove(sessionId);
                }
                else
                {
                    m_perSession[sessionId] = sessionCount - 1;
                }
                m_total--;
            }
        }

        private static int ValidateLimit(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "A limit cannot be negative.");
            }
            return value;
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<NodeId, int> m_perSession = [];
        private int m_total;
        private int m_maxPerSession = DefaultMaxOpenFilesPerSession;
        private int m_maxTotal = DefaultMaxOpenFiles;
    }
}
