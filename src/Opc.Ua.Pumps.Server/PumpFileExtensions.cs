// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Serves optional pump files through the shared session-owned FileType service.
    /// </summary>
    public static class PumpFileExtensions
    {
        /// <summary>
        /// Binds a declared documentation file. The address-space owner disposes the returned binding.
        /// </summary>
        /// <remarks>
        /// With no commit callback the file is read-only. A callback owns validation and durable storage
        /// of an authorized replacement; the shared binder enforces session handle and open-mode ownership.
        /// </remarks>
        public static IFileTypeBinding BindDocumentationFile(
            this PumpsRuntime runtime,
            IPumpBuilder pump,
            string browseName,
            ByteString content,
            IFileTypeBinder binder,
            Func<ByteString, CancellationToken, ValueTask>? commit = null,
            int maximumBytes = 16 * 1024 * 1024)
        {
            ValidateOwner(runtime, pump, binder);
            ValidateContent(content, maximumBytes);
            if (pump.Documentation.Child(browseName) is not FileState file)
            {
                throw new ArgumentException(
                    "The documentation member must be a declared FileType.", nameof(browseName));
            }
            if (file.Open?.OnCallMethod2Async is not null)
            {
                throw new InvalidOperationException("The documentation file is already bound.");
            }
            IFileTypeBinding binding = binder.Bind(
                file, runtime.SystemContext, content, commit, commitOnClose: true, maximumBytes: maximumBytes);
            runtime.Manager.AddNode(file);
            runtime.Manager.RegisterResource(binding);
            return binding;
        }

        /// <summary>
        /// Adds one named marking file to Identification/Markings and binds its standard file methods.
        /// </summary>
        public static IFileTypeBinding BindMarkingFile(
            this PumpsRuntime runtime,
            IPumpBuilder pump,
            string name,
            ByteString content,
            IFileTypeBinder binder,
            int maximumBytes = 16 * 1024 * 1024)
        {
            ValidateOwner(runtime, pump, binder);
            ValidateContent(content, maximumBytes);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A marking name is required.", nameof(name));
            }
            NodeState markings = pump.Identification.Nested(BrowseNames.Markings).Node;
            var browseName = new QualifiedName(name, runtime.InstanceNamespaceIndex);
            if (markings.FindChild(runtime.SystemContext, browseName) is not null)
            {
                throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
            }
            FileState file = runtime.SystemContext.CreateInstanceOfFileType(markings, browseName);
            file.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            IFileTypeBinding binding = binder.Bind(
                file, runtime.SystemContext, content, maximumBytes: maximumBytes);
            markings.AddChild(file);
            runtime.Manager.AddNode(file);
            runtime.Manager.RegisterResource(binding);
            return binding;
        }

        /// <summary>
        /// Rejects invalid file bounds before materializing an optional node.
        /// </summary>
        private static void ValidateContent(ByteString content, int maximumBytes)
        {
            if (maximumBytes <= 0 || content.Span.Length > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
        }

        /// <summary>
        /// Requires a binder and a pump registered with this address-space owner.
        /// </summary>
        private static void ValidateOwner(PumpsRuntime runtime, IPumpBuilder pump, IFileTypeBinder binder)
        {
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (pump is null)
            {
                throw new ArgumentNullException(nameof(pump));
            }
            if (binder is null)
            {
                throw new ArgumentNullException(nameof(binder));
            }
            if (!ReferenceEquals(runtime.Pump(pump.NodeId)?.Pump, pump.Pump))
            {
                throw new ArgumentException("The pump must belong to this address-space owner.", nameof(pump));
            }
        }
    }
}
