/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Threading;
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

        public bool RunContinuationsAsynchronously { get => m_core.RunContinuationsAsynchronously; set => m_core.RunContinuationsAsynchronously = value; }
        public short Version => m_core.Version;
        public void Reset() => m_core.Reset();
        public void SetResult(T result) => m_core.SetResult(result);
        public void SetException(Exception error) => m_core.SetException(error);

        public T GetResult(short token) => m_core.GetResult(token);
        void IValueTaskSource.GetResult(short token) => m_core.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => m_core.GetStatus(token);
        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
            => m_core.OnCompleted(continuation, state, token, flags);

        public ValueTask<T> Task => new ValueTask<T>(this, m_core.Version);
        public ValueTask SourceTask => new ValueTask(this, m_core.Version);
    }
}
