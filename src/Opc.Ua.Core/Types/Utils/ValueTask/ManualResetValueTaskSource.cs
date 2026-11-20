/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Opc.Ua
{
    /// <summary>
    /// A reusable value task source.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class ManualResetValueTaskSource<T> : IValueTaskSource<T>, IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<T> m_core;

        public bool RunContinuationsAsynchronously
        {
            get => m_core.RunContinuationsAsynchronously;
            set => m_core.RunContinuationsAsynchronously = value;
        }

        public short Version => m_core.Version;

        public void Reset()
        {
            m_core.Reset();
        }

        public void SetResult(T result)
        {
            m_core.SetResult(result);
        }

        public void SetException(Exception error)
        {
            m_core.SetException(error);
        }

        public T GetResult(short token)
        {
            return m_core.GetResult(token);
        }

        void IValueTaskSource.GetResult(short token)
        {
            m_core.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return m_core.GetStatus(token);
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            m_core.OnCompleted(continuation, state, token, flags);
        }

        public ValueTask<T> Task => new(this, m_core.Version);
        public ValueTask SourceTask => new(this, m_core.Version);
    }
}
