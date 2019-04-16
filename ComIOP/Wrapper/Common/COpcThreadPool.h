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

#ifndef _COpcThreadPool_H_
#define _COpcThreadPool_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcList.h"
#include "COpcCriticalSection.h"

class COpcMessage;

//==============================================================================
// INTERFACE: IOpcMessageCallback
// PURPOSE:   A interface to an object that processes messages.

interface IOpcMessageCallback : public IUnknown
{
	// ProcessMessage
	virtual void ProcessMessage(COpcMessage& cMsg) = 0;
};

//==============================================================================
// CLASS:   COpcMessage
// PURPOSE: A base class for a message.

class OPCUTILS_API COpcMessage
{
    OPC_CLASS_NEW_DELETE();

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcMessage(UINT uType, IOpcMessageCallback* ipCallback);

    // Copy Constructor
    COpcMessage(const COpcMessage& cMessage);

	// Destructor
    virtual ~COpcMessage();

    //==========================================================================
    // Public Methods

	// Process
	virtual void Process()
	{
		if (m_ipCallback != NULL)
		{
			m_ipCallback->ProcessMessage(*this);
		}
	}

	// GetID
	UINT GetID() { return m_uID; }

	// GetType
	UINT GetType() { return m_uType; }

protected:

    //==========================================================================
    // Protected Operators

	UINT                 m_uID;
	UINT                 m_uType;
	IOpcMessageCallback* m_ipCallback;
};

//==============================================================================
// CLASS:   COpcThreadPool
// PURPOSE: Manages a pool of threads that process queued messages.

class OPCUTILS_API COpcThreadPool : public COpcSynchObject
{
    OPC_CLASS_NEW_DELETE();

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcThreadPool();

	// Destructor
    ~COpcThreadPool();

    //==========================================================================
    // Public Methods
     
	// Start
	bool Start();

	// Stop
	void Stop();

	// Run
	void Run();

    // QueueMessage
	bool QueueMessage(COpcMessage* pMsg);

	// SetSize
	void SetSize(UINT uMinThreads, UINT uMaxThreads);

private:

    //==========================================================================
    // Private Members

	HANDLE                  m_hEvent;
	COpcList<COpcMessage*>  m_cQueue;

	UINT                    m_uTotalThreads;
	UINT                    m_uWaitingThreads;
	UINT                    m_uMinThreads;
	UINT                    m_uMaxThreads;
};

#endif // _COpcThreadPool_H_
