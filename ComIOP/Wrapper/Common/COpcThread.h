/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

#ifndef _COpcThread_H_
#define _COpcThread_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcString.h"

//==============================================================================
// TYPEDEF: PfnOpcThreadControl
// PURPOSE: Pointer to a function that controls a thread.

typedef void (WINAPI *FnOpcThreadControl)(void* pData, bool bStopThread);
typedef FnOpcThreadControl PfnOpcThreadControl;

//==============================================================================
// CLASS:   COpcThread
// PURPOSE: Manages startup and shutdown of a thread.

class OPCUTILS_API COpcThread
{
    OPC_CLASS_NEW_DELETE()

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcThread();

    // Destructor
    ~COpcThread();

    //==========================================================================
    // Public Methods

    // Start
    bool Start(
        PfnOpcThreadControl pfnStartProc, 
        void*               pData, 
        DWORD               dwTimeout = INFINITE,
		int                 iPriority = THREAD_PRIORITY_NORMAL);

    // Stop
    void Stop(DWORD dwTimeout = INFINITE);

    // WaitingForStop
    bool WaitingForStop() { return m_bWaitingForStop; }

    // Run
    DWORD Run();

    // PostMessage
    bool PostMessage(UINT uMsgID, WPARAM wParam, LPARAM lParam);

private:

    //==========================================================================
    // Private Members

    DWORD               m_dwID;
    HANDLE              m_hThread;
    HANDLE              m_hEvent;
    bool                m_bWaitingForStop;

    PfnOpcThreadControl m_pfnControl;
    void*               m_pData;
};

#endif // _COpcThread_H_
