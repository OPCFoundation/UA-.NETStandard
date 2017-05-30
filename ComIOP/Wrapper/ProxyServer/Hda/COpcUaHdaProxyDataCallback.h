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

#pragma once

using namespace System;
using namespace System::Collections::Generic;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

/// <summary>
/// Used to dispatch callbacks.
/// </summary>>
public ref class COpcUaHdaProxyDataCallback : IComHdaDataCallback
{
public:

	/// <summary>
	/// Creates a new callback,
	/// </summary>
	COpcUaHdaProxyDataCallback(IOPCHDA_DataCallback* ipCallback);

	/// <summary>
	/// Releases all resources used by the callback.
	/// </summary>
	~COpcUaHdaProxyDataCallback();

    /// <summary>
    /// The finializer implementation.
    /// </summary>
    !COpcUaHdaProxyDataCallback();

	virtual void OnDataChange(
		int transactionId, 
		List<HdaReadRequest^>^ results);

	virtual void OnReadComplete(
		int transactionId, 
		List<HdaReadRequest^>^ results);

	virtual void OnReadModifiedComplete(
		int transactionId, 
		List<HdaReadRequest^>^ results);

	virtual void OnReadAttributeComplete(
		int transactionId, 
		List<HdaReadRequest^>^ results);

	virtual void OnReadAnnotations(
		int transactionId, 
		List<HdaReadRequest^>^ results);

	virtual void OnInsertAnnotations(
		int transactionId, 
		List<HdaUpdateRequest^>^ results);

	virtual void OnUpdateComplete(
		int transactionId, 
		List<HdaUpdateRequest^>^ results);

	virtual void OnCancelComplete(int transactionId);

private:

	/// <summary>
	/// An unmanaged container for unmanaged data stored in the channel.
	/// </summary>
	IOPCHDA_DataCallback* m_ipCallback;

	/// <summary>
	/// A synchronization object for the object.
	/// </summary>
	Object^ m_lock;
};
