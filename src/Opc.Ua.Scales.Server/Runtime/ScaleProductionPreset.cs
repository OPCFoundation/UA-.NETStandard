/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Locking;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// Vehicle master data a <c>GetVehicleInformation</c> call loads into a
    /// vehicle product (OPC 40200 §7.49.4).
    /// </summary>
    public sealed record VehicleInformation
    {
        /// <summary>
        /// Gets the preset vehicle tare used by one-pass weighing.
        /// </summary>
        public double? Tare { get; init; }

        /// <summary>
        /// Gets the date the preset tare expires.
        /// </summary>
        public DateTime? TareExpirationDate { get; init; }

        /// <summary>
        /// Gets the carrier id.
        /// </summary>
        public string? CarrierId { get; init; }

        /// <summary>
        /// Gets the carrier's display name.
        /// </summary>
        public LocalizedText CarrierDisplayName { get; init; }

        /// <summary>
        /// Gets the driver id.
        /// </summary>
        public string? DriverId { get; init; }

        /// <summary>
        /// Gets the driver's display name.
        /// </summary>
        public LocalizedText DriverDisplayName { get; init; }

        /// <summary>
        /// Gets the customer.
        /// </summary>
        public LocalizedText Customer { get; init; }

        /// <summary>
        /// Gets the supplier.
        /// </summary>
        public LocalizedText Supplier { get; init; }

        /// <summary>
        /// Gets the destination.
        /// </summary>
        public LocalizedText Destination { get; init; }
    }

    /// <summary>
    /// The production preset of a scale or scale system: its products and
    /// the <c>AddProduct</c>, <c>RemoveProduct</c>, <c>SelectProduct</c>,
    /// <c>DeselectProduct</c> and <c>SwitchProduct</c> methods
    /// (OPC 40200 §7.7, §7.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A product is identified by its <c>ProductId</c>. Selecting it adds the
    /// id to <c>CurrentProducts</c> and sets its <c>ProductMode</c> to
    /// Processing; deselecting reverses both. <c>SwitchProduct</c> is only
    /// allowed while at most one product is in use (§7.7.8).
    /// </para>
    /// <para>
    /// <c>Products/&lt;Product&gt;</c> is a MandatoryPlaceholder, so the last
    /// product cannot be removed. A product created through
    /// <c>AddProduct</c> must be of the product type the scale narrows the
    /// placeholder to, or a subtype (§7.x.3), and is locked for the calling
    /// client when products are lockable (§7.7.4).
    /// </para>
    /// </remarks>
    public sealed class ScaleProductionPreset
    {
        internal ScaleProductionPreset(
            ScaleRuntimeServices services,
            ProductionPresetState preset,
            NodeId allowedProductType,
            Func<ArrayOf<EUInformation>> allowedUnits)
        {
            m_services = services;
            Preset = preset;
            m_allowedProductType = allowedProductType;
            m_allowedUnits = allowedUnits;

            var products = new List<ProductState>();
            if (preset.Products != null)
            {
                var children = new List<BaseInstanceState>();
                preset.Products.GetChildren(services.Context, children);
                products.AddRange(children.OfType<ProductState>());
            }
            foreach (ProductState product in products)
            {
                if (product.ProductId?.Value is { Length: > 0 } id)
                {
                    m_products[id] = product;
                    BindProduct(product);
                }
            }
            BindMethods();
            PublishCurrentProducts();
        }

        /// <summary>
        /// Gets the production preset node.
        /// </summary>
        public ProductionPresetState Preset { get; }

        /// <summary>
        /// Gets whether products carry a DI <c>Lock</c>, the access
        /// restriction of OPC 40200 §7.8.3.
        /// </summary>
        public bool Lockable { get; internal set; }

        /// <summary>
        /// Gets or sets the provider <c>GetVehicleInformation</c> loads
        /// vehicle master data from. Without one the method returns
        /// <c>Bad_NotSupported</c>.
        /// </summary>
        public Func<string, VehicleInformation?>? VehicleInformationProvider { get; set; }

        /// <summary>
        /// Gets or sets a factory for vendor-specific product types that
        /// <c>AddProduct</c> is called with. It receives the requested type,
        /// the <c>Products</c> folder and the browse name, and returns the new
        /// instance or <see langword="null"/> when it does not know the type.
        /// </summary>
        public Func<NodeId, NodeState, QualifiedName, ProductState?>? ProductFactory { get; set; }

        /// <summary>
        /// Raised after the set of current products changed.
        /// </summary>
        public event EventHandler? CurrentProductsChanged;

        /// <summary>
        /// Gets the products, keyed by product id.
        /// </summary>
        public IReadOnlyDictionary<string, ProductState> Products
        {
            get
            {
                lock (m_lock)
                {
                    return new Dictionary<string, ProductState>(m_products, StringComparer.Ordinal);
                }
            }
        }

        /// <summary>
        /// Gets the ids of the products in processing.
        /// </summary>
        public ArrayOf<string> CurrentProducts
        {
            get
            {
                lock (m_lock)
                {
                    return m_currentProducts;
                }
            }
        }

        /// <summary>
        /// Gets the single product in processing, or <see langword="null"/>
        /// when none or more than one is.
        /// </summary>
        public ProductState? ActiveProduct
        {
            get
            {
                lock (m_lock)
                {
                    return m_current.Count == 1 && m_products.TryGetValue(m_current[0], out ProductState? product)
                        ? product
                        : null;
                }
            }
        }

        /// <summary>
        /// Finds a product by id.
        /// </summary>
        /// <param name="productId">The product id.</param>
        public ProductState? Find(string productId)
        {
            lock (m_lock)
            {
                return productId != null && m_products.TryGetValue(productId, out ProductState? product)
                    ? product
                    : null;
            }
        }

        /// <summary>
        /// Adds a product of the scale's product type from the application.
        /// </summary>
        /// <param name="productId">The unique product id.</param>
        /// <param name="productName">The product name.</param>
        /// <param name="configure">Configures the product before it is published.</param>
        /// <returns>The new product.</returns>
        /// <exception cref="ServiceResultException">The product cannot be added.</exception>
        public ProductState AddProduct(
            string productId,
            LocalizedText productName,
            Action<ProductState>? configure = null)
        {
            ProductState product = AddProductCore(
                m_services.Context,
                productId,
                productName,
                m_allowedProductType,
                configure,
                lockForCaller: false,
                out ServiceResult result);
            return ServiceResult.IsBad(result) ? throw new ServiceResultException(result) : product;
        }

        /// <summary>
        /// Selects a product for processing.
        /// </summary>
        /// <param name="productId">The product id.</param>
        public ServiceResult Select(string productId)
        {
            return SelectCore(m_services.Context, productId, checkLock: false);
        }

        /// <summary>
        /// Deselects a product.
        /// </summary>
        /// <param name="productId">The product id.</param>
        public ServiceResult Deselect(string productId)
        {
            return DeselectCore(m_services.Context, productId, checkLock: false);
        }

        /// <summary>
        /// Replaces the single product in processing.
        /// </summary>
        /// <param name="productId">The product id to switch to.</param>
        public ServiceResult Switch(string productId)
        {
            return SwitchCore(m_services.Context, productId, checkLock: false);
        }

        /// <summary>
        /// Removes a product.
        /// </summary>
        /// <param name="productId">The product id.</param>
        /// <param name="cancellationToken">Cancels the removal.</param>
        public ValueTask<ServiceResult> RemoveAsync(
            string productId,
            CancellationToken cancellationToken = default)
        {
            return RemoveCoreAsync(m_services.Context, productId, checkLock: false, cancellationToken);
        }

        private void BindMethods()
        {
            if (Preset.AddProduct != null)
            {
                Preset.AddProduct.OnCall = (
                    ISystemContext context,
                    MethodState method,
                    NodeId objectId,
                    string productName,
                    string productId,
                    NodeId productType,
                    ref NodeId productNodeId) =>
                {
                    ProductState product = AddProductCore(
                        context,
                        productId,
                        new LocalizedText(productName),
                        productType,
                        configure: null,
                        lockForCaller: true,
                        out ServiceResult result);
                    if (ServiceResult.IsGood(result))
                    {
                        productNodeId = product.NodeId;
                    }
                    return result;
                };
            }
            if (Preset.RemoveProduct != null)
            {
                Preset.RemoveProduct.OnCallAsync = async (context, method, objectId, productId, ct) =>
                    new RemoveProductMethodStateResult
                    {
                        ServiceResult = await RemoveCoreAsync(context, productId, checkLock: true, ct)
                            .ConfigureAwait(false)
                    };
            }
            if (Preset.SelectProduct != null)
            {
                Preset.SelectProduct.OnCall = (context, method, objectId, productId) =>
                    SelectCore(context, productId, checkLock: true);
            }
            if (Preset.DeselectProduct != null)
            {
                Preset.DeselectProduct.OnCall = (context, method, objectId, productId) =>
                    DeselectCore(context, productId, checkLock: true);
            }
            if (Preset.SwitchProduct != null)
            {
                Preset.SwitchProduct.OnCall = (context, method, objectId, productId) =>
                    SwitchCore(context, productId, checkLock: true);
            }
        }

        private ProductState AddProductCore(
            ISystemContext callContext,
            string productId,
            LocalizedText productName,
            NodeId productType,
            Action<ProductState>? configure,
            bool lockForCaller,
            out ServiceResult result)
        {
            ProductState? product = null;
            lock (m_lock)
            {
                if (string.IsNullOrEmpty(productId))
                {
                    result = ServiceResult.Create(StatusCodes.BadInvalidArgument, "A product id is required.");
                    return null!;
                }
                if (m_products.ContainsKey(productId))
                {
                    result = ServiceResult.Create(
                        StatusCodes.BadNodeIdExists,
                        "A product with id '{0}' already exists.",
                        productId);
                    return null!;
                }
                if (productType.IsNull)
                {
                    productType = m_allowedProductType;
                }
                if (!m_services.IsTypeOf(productType, m_allowedProductType))
                {
                    result = ServiceResult.Create(
                        StatusCodes.BadTypeMismatch,
                        "'{0}' is not the product type of this scale or a subtype of it.",
                        productType);
                    return null!;
                }
                if (Preset.Products == null)
                {
                    result = ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The production preset has no Products folder.");
                    return null!;
                }

                QualifiedName browseName = m_services.InstanceName(productId);
                product = CreateProduct(productType, Preset.Products, browseName);
                if (product == null)
                {
                    result = ServiceResult.Create(
                        StatusCodes.BadNotSupported,
                        "Instances of product type '{0}' cannot be created by this server.",
                        productType);
                    return null!;
                }

                product.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                product.DisplayName = new LocalizedText(productId);
                InitializeProduct(
                    m_services.Context,
                    product,
                    productId,
                    productName,
                    selectable: Preset.SelectProduct != null || Preset.CurrentProducts != null,
                    lockable: Lockable && m_services.LockService != null);
                configure?.Invoke(product);
                Preset.Products.AddChild(product);
                m_products[productId] = product;
            }

            // Registered outside the lock: registration takes the node
            // manager's own lock and must not nest inside this one.
            if (m_registered)
            {
                m_services.Register(product);
            }
            BindProduct(product);
            if (lockForCaller && Lockable && m_services.LockService is { } lockService)
            {
                lockService.InitLock(callContext, product.NodeId, string.Empty);
            }
            Preset.ClearChangeMasks(m_services.Context, includeChildren: true);
            result = ServiceResult.Good;
            return product;
        }

        private async ValueTask<ServiceResult> RemoveCoreAsync(
            ISystemContext callContext,
            string productId,
            bool checkLock,
            CancellationToken cancellationToken)
        {
            ProductState? product;
            lock (m_lock)
            {
                if (!m_products.TryGetValue(productId ?? string.Empty, out product))
                {
                    return NotFound(productId);
                }
                if (checkLock)
                {
                    ServiceResult access = ScaleValues.CheckLock(m_services, callContext, product.NodeId);
                    if (ServiceResult.IsBad(access))
                    {
                        return access;
                    }
                }
                if (m_current.Contains(productId!))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "Product '{0}' is in processing; deselect it before removing it.",
                        productId ?? string.Empty);
                }
                if (m_products.Count == 1)
                {
                    // Products/<Product> is a MandatoryPlaceholder.
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The last product cannot be removed; the Products folder requires at least one.");
                }
                m_products.Remove(productId!);
            }
            await m_services.Delete(product.NodeId, cancellationToken).ConfigureAwait(false);
            Preset.Products?.RemoveChild(product);
            return ServiceResult.Good;
        }

        private ServiceResult SelectCore(ISystemContext callContext, string productId, bool checkLock)
        {
            lock (m_lock)
            {
                if (!m_products.TryGetValue(productId ?? string.Empty, out ProductState? product))
                {
                    return NotFound(productId);
                }
                if (checkLock)
                {
                    ServiceResult access = ScaleValues.CheckLock(m_services, callContext, product.NodeId);
                    if (ServiceResult.IsBad(access))
                    {
                        return access;
                    }
                }
                if (!m_current.Contains(productId!))
                {
                    m_current.Add(productId!);
                    SetMode(product, processing: true);
                    PublishCurrentProducts();
                }
            }
            CurrentProductsChanged?.Invoke(this, EventArgs.Empty);
            return ServiceResult.Good;
        }

        private ServiceResult DeselectCore(ISystemContext callContext, string productId, bool checkLock)
        {
            lock (m_lock)
            {
                if (!m_products.TryGetValue(productId ?? string.Empty, out ProductState? product))
                {
                    return NotFound(productId);
                }
                if (checkLock)
                {
                    ServiceResult access = ScaleValues.CheckLock(m_services, callContext, product.NodeId);
                    if (ServiceResult.IsBad(access))
                    {
                        return access;
                    }
                }
                if (m_current.Remove(productId!))
                {
                    SetMode(product, processing: false);
                    PublishCurrentProducts();
                }
            }
            CurrentProductsChanged?.Invoke(this, EventArgs.Empty);
            return ServiceResult.Good;
        }

        private ServiceResult SwitchCore(ISystemContext callContext, string productId, bool checkLock)
        {
            lock (m_lock)
            {
                if (!m_products.TryGetValue(productId ?? string.Empty, out ProductState? next))
                {
                    return NotFound(productId);
                }
                if (m_current.Count > 1)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "SwitchProduct requires at most one product in processing; {0} are.",
                        m_current.Count);
                }
                if (checkLock)
                {
                    ServiceResult access = ScaleValues.CheckLock(m_services, callContext, next.NodeId);
                    if (ServiceResult.IsBad(access))
                    {
                        return access;
                    }
                }
                foreach (string current in m_current)
                {
                    if (m_products.TryGetValue(current, out ProductState? previous))
                    {
                        SetMode(previous, processing: false);
                    }
                }
                m_current.Clear();
                m_current.Add(productId!);
                SetMode(next, processing: true);
                PublishCurrentProducts();
            }
            CurrentProductsChanged?.Invoke(this, EventArgs.Empty);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called by the owner once the preset is registered with the node
        /// manager: products added from then on have to be registered
        /// themselves.
        /// </summary>
        internal void OnRegistered()
        {
            m_registered = true;
            foreach (ProductState product in m_products.Values)
            {
                if (product.Lock != null && m_services.LockService is { } lockService)
                {
                    product.Lock.BindToLockService(product.NodeId, lockService);
                }
            }
        }

        private ProductState? CreateProduct(NodeId productType, NodeState parent, QualifiedName browseName)
        {
            if (productType.NamespaceIndex == m_services.Namespaces.Scales &&
                productType.TryGetValue(out uint id) &&
                CreateBuiltInProduct(m_services.Context, id, parent, browseName) is { } product)
            {
                return product;
            }
            return ProductFactory?.Invoke(productType, parent, browseName);
        }

        /// <summary>
        /// Creates an instance of one of the concrete OPC 40200 product types.
        /// </summary>
        internal static ProductState? CreateBuiltInProduct(
            ISystemContext context,
            uint productTypeId,
            NodeState parent,
            QualifiedName browseName)
        {
            return productTypeId switch
            {
                ObjectTypes.SimpleProductType => context.CreateInstanceOfSimpleProductType(parent, browseName),
                ObjectTypes.AutomaticFillingProductType =>
                    context.CreateInstanceOfAutomaticFillingProductType(parent, browseName),
                ObjectTypes.CatchweigherProductType =>
                    context.CreateInstanceOfCatchweigherProductType(parent, browseName),
                ObjectTypes.CheckweigherProductType =>
                    context.CreateInstanceOfCheckweigherProductType(parent, browseName),
                ObjectTypes.AutomaticWeightPriceLabelerProductType =>
                    context.CreateInstanceOfAutomaticWeightPriceLabelerProductType(parent, browseName),
                ObjectTypes.ContinuousProductType =>
                    context.CreateInstanceOfContinuousProductType(parent, browseName),
                ObjectTypes.PieceCountingProductType =>
                    context.CreateInstanceOfPieceCountingProductType(parent, browseName),
                ObjectTypes.RecipeProductType =>
                    context.CreateInstanceOfRecipeProductType(parent, browseName),
                ObjectTypes.TotalizingHopperProductType =>
                    context.CreateInstanceOfTotalizingHopperProductType(parent, browseName),
                ObjectTypes.VehicleProductType =>
                    context.CreateInstanceOfVehicleProductType(parent, browseName),
                _ => null
            };
        }

        /// <summary>
        /// Fills the mandatory members of a new product and materialises the
        /// members the preset's capabilities need.
        /// </summary>
        internal static void InitializeProduct(
            ISystemContext context,
            ProductState product,
            string productId,
            LocalizedText productName,
            bool selectable,
            bool lockable)
        {
            ScaleValues.Set(context, product.ProductId, Variant.From(productId));
            ScaleValues.Set(context, product.ProductName, productName);
            if (selectable)
            {
                product.AddProductMode(context);
                if (product.ProductMode != null)
                {
                    if (product.ProductMode.TrueState != null)
                    {
                        product.ProductMode.TrueState.Value = new LocalizedText("Processing");
                    }
                    if (product.ProductMode.FalseState != null)
                    {
                        product.ProductMode.FalseState.Value = new LocalizedText("NotProcessing");
                    }
                    product.ProductMode.Value = false;
                }
            }
            if (lockable)
            {
                product.AddLock(context);
            }
        }

        private void BindProduct(ProductState product)
        {
            if (product.Lock != null && m_registered && m_services.LockService is { } lockService)
            {
                product.Lock.BindToLockService(product.NodeId, lockService);
            }
            switch (product)
            {
                case CatchweigherProductState catchweigher:
                    BindZones(catchweigher);
                    break;
                case PieceCountingProductState pieceCounting:
                    BindPieceCounting(pieceCounting);
                    break;
                case VehicleProductState vehicle:
                    BindVehicle(vehicle);
                    break;
            }
        }

        private void BindZones(CatchweigherProductState product)
        {
            if (product.AddZone != null)
            {
                product.AddZone.OnCall = (
                    ISystemContext context,
                    MethodState method,
                    NodeId objectId,
                    LocalizedText zoneName,
                    double lowerLimit,
                    double upperLimit,
                    EUInformation engineeringUnits,
                    ref NodeId zoneNodeId) =>
                {
                    ServiceResult access = ScaleValues.CheckLock(m_services, context, product.NodeId);
                    if (ServiceResult.IsBad(access))
                    {
                        return access;
                    }
                    if (zoneName.IsNullOrEmpty)
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidArgument, "A zone name is required.");
                    }
                    if (!(lowerLimit < upperLimit))
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidArgument,
                            "The lower limit of a zone has to be below its upper limit.");
                    }
                    ServiceResult unit = ScaleValues.CheckUnit(
                        engineeringUnits,
                        m_allowedUnits(),
                        m_services.Options.RequireSiUnits);
                    if (ServiceResult.IsBad(unit))
                    {
                        return unit;
                    }
                    ZoneState zone = AddZone(product, zoneName, lowerLimit, upperLimit, engineeringUnits);
                    zoneNodeId = zone.NodeId;
                    return ServiceResult.Good;
                };
            }
            if (product.RemoveZone != null)
            {
                product.RemoveZone.OnCallAsync = async (context, method, objectId, zoneNodeId, ct) =>
                {
                    ServiceResult access = ScaleValues.CheckLock(m_services, context, product.NodeId);
                    if (ServiceResult.IsBad(access))
                    {
                        return new RemoveZoneMethodStateResult { ServiceResult = access };
                    }
                    var children = new List<BaseInstanceState>();
                    product.GetChildren(m_services.Context, children);
                    ZoneState? zone = children.OfType<ZoneState>().FirstOrDefault(z => z.NodeId == zoneNodeId);
                    if (zone == null)
                    {
                        return new RemoveZoneMethodStateResult
                        {
                            ServiceResult = ServiceResult.Create(
                                StatusCodes.BadNodeIdUnknown,
                                "'{0}' is not a zone of this product.",
                                zoneNodeId)
                        };
                    }
                    await m_services.Delete(zone.NodeId, ct).ConfigureAwait(false);
                    product.RemoveChild(zone);
                    return new RemoveZoneMethodStateResult { ServiceResult = ServiceResult.Good };
                };
            }
        }

        /// <summary>
        /// Adds a zone to a catchweigher product (OPC 40200 §7.12.4).
        /// </summary>
        /// <param name="product">The catchweigher product.</param>
        /// <param name="zoneName">The zone name.</param>
        /// <param name="lowerLimit">The lower limit, which takes precedence at a shared boundary.</param>
        /// <param name="upperLimit">The upper limit.</param>
        /// <param name="engineeringUnits">The unit of both limits.</param>
        public ZoneState AddZone(
            CatchweigherProductState product,
            LocalizedText zoneName,
            double lowerLimit,
            double upperLimit,
            EUInformation engineeringUnits)
        {
            if (product == null)
            {
                throw new ArgumentNullException(nameof(product));
            }
            ISystemContext context = m_services.Context;
            ZoneState zone = context.CreateInstanceOfZoneType(
                product,
                m_services.InstanceName(zoneName.Text ?? "Zone"));
            zone.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            zone.DisplayName = zoneName;
            ScaleValues.Set(context, zone.Name, zoneName);
            ScaleValues.Set(context, zone.LowerLimit, Variant.From(lowerLimit));
            ScaleValues.Set(context, zone.UpperLimit, Variant.From(upperLimit));
            ScaleValues.SetUnits(context, zone.LowerLimit, engineeringUnits);
            ScaleValues.SetUnits(context, zone.UpperLimit, engineeringUnits);
            product.AddChild(zone);
            if (m_registered)
            {
                m_services.Register(zone);
            }
            return zone;
        }

        private void BindPieceCounting(PieceCountingProductState product)
        {
            if (product.SetTargetItemCount != null)
            {
                product.SetTargetItemCount.OnCall = (context, method, objectId, targetItemCount) =>
                {
                    ServiceResult access = ScaleValues.CheckLock(m_services, context, product.NodeId);
                    if (ServiceResult.IsBad(access))
                    {
                        return access;
                    }
                    product.AddTargetItemCount(m_services.Context);
                    if (m_registered && product.TargetItemCount != null)
                    {
                        m_services.Register(product.TargetItemCount);
                    }
                    ScaleValues.Set(m_services.Context, product.TargetItemCount, Variant.From(targetItemCount));
                    return ServiceResult.Good;
                };
            }
            if (product.SetTargetPieceCount != null)
            {
                product.SetTargetPieceCount.OnCall = (context, method, objectId, target, plus, minus) =>
                {
                    ServiceResult access = ScaleValues.CheckLock(m_services, context, product.NodeId);
                    if (ServiceResult.IsBad(access))
                    {
                        return access;
                    }
                    ISystemContext own = m_services.Context;
                    product.AddTargetPieceCount(own);
                    TargetItemState? targetPieces = product.TargetPieceCount;
                    if (targetPieces != null)
                    {
                        targetPieces.AddPlusTolerance(own);
                        targetPieces.AddMinusTolerance(own);
                        if (m_registered)
                        {
                            m_services.Register(targetPieces);
                        }
                        ScaleValues.Set(own, targetPieces, Variant.From(target));
                        ScaleValues.Set(own, targetPieces.PlusTolerance, Variant.From(plus));
                        ScaleValues.Set(own, targetPieces.MinusTolerance, Variant.From(minus));
                    }
                    return ServiceResult.Good;
                };
            }
        }

        private void BindVehicle(VehicleProductState product)
        {
            if (product.GetVehicleInformation == null)
            {
                return;
            }
            product.GetVehicleInformation.OnCall = (context, method, objectId, vehicleId) =>
            {
                ServiceResult access = ScaleValues.CheckLock(m_services, context, product.NodeId);
                if (ServiceResult.IsBad(access))
                {
                    return access;
                }
                if (VehicleInformationProvider == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNotSupported,
                        "No vehicle data store is connected to this server.");
                }
                VehicleInformation? info = VehicleInformationProvider(vehicleId);
                if (info == null)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNotFound,
                        "No vehicle with id '{0}' is known.",
                        vehicleId);
                }
                ApplyVehicleInformation(product, vehicleId, info);
                return ServiceResult.Good;
            };
        }

        internal void ApplyVehicleInformation(VehicleProductState product, string vehicleId, VehicleInformation info)
        {
            ISystemContext context = m_services.Context;
            var added = new List<NodeState>();
            void Ensure<T>(T? existing, Action add, Func<T?> after)
                where T : NodeState
            {
                if (existing == null)
                {
                    add();
                    if (after() is { } node)
                    {
                        added.Add(node);
                    }
                }
            }

            ScaleValues.Set(context, product.VehicleId, vehicleId);
            if (info.Tare.HasValue)
            {
                Ensure(product.Tare, () => product.AddTare(context), () => product.Tare);
                ScaleValues.Set(context, product.Tare, Variant.From(info.Tare.Value));
            }
            if (info.TareExpirationDate.HasValue)
            {
                Ensure(product.TareExpirationDate, () => product.AddTareExpirationDate(context), () => product.TareExpirationDate);
                ScaleValues.Set(context, product.TareExpirationDate, (DateTimeUtc)info.TareExpirationDate.Value);
            }
            if (info.CarrierId != null)
            {
                Ensure(product.CarrierId, () => product.AddCarrierId(context), () => product.CarrierId);
                ScaleValues.Set(context, product.CarrierId, info.CarrierId);
            }
            if (!info.CarrierDisplayName.IsNullOrEmpty)
            {
                Ensure(product.CarrierDisplayName, () => product.AddCarrierDisplayName(context), () => product.CarrierDisplayName);
                ScaleValues.Set(context, product.CarrierDisplayName, info.CarrierDisplayName);
            }
            if (info.DriverId != null)
            {
                Ensure(product.DriverId, () => product.AddDriverId(context), () => product.DriverId);
                ScaleValues.Set(context, product.DriverId, info.DriverId);
            }
            if (!info.DriverDisplayName.IsNullOrEmpty)
            {
                Ensure(product.DriverDisplayName, () => product.AddDriverDisplayName(context), () => product.DriverDisplayName);
                ScaleValues.Set(context, product.DriverDisplayName, info.DriverDisplayName);
            }
            if (!info.Customer.IsNullOrEmpty)
            {
                Ensure(product.Customer, () => product.AddCustomer(context), () => product.Customer);
                ScaleValues.Set(context, product.Customer, info.Customer);
            }
            if (!info.Supplier.IsNullOrEmpty)
            {
                Ensure(product.Supplier, () => product.AddSupplier(context), () => product.Supplier);
                ScaleValues.Set(context, product.Supplier, info.Supplier);
            }
            if (!info.Destination.IsNullOrEmpty)
            {
                Ensure(product.Destination, () => product.AddDestination(context), () => product.Destination);
                ScaleValues.Set(context, product.Destination, info.Destination);
            }
            if (m_registered)
            {
                foreach (NodeState node in added)
                {
                    m_services.Register(node);
                }
            }
        }

        private static void SetMode(ProductState product, bool processing)
        {
            if (product.ProductMode != null)
            {
                product.ProductMode.Value = processing;
                product.ProductMode.Timestamp = DateTimeUtc.Now;
            }
        }

        private void PublishCurrentProducts()
        {
            m_currentProducts = m_current.ToArray().ToArrayOf();
            if (Preset.CurrentProducts != null)
            {
                Preset.CurrentProducts.Value = m_currentProducts;
                ScaleValues.Touch(m_services.Context, Preset.CurrentProducts);
            }
            foreach (ProductState product in m_products.Values)
            {
                product.ClearChangeMasks(m_services.Context, includeChildren: true);
            }
        }

        private static ServiceResult NotFound(string? productId)
        {
            return ServiceResult.Create(
                StatusCodes.BadNotFound,
                "No product with id '{0}' exists.",
                productId ?? string.Empty);
        }

        private readonly ScaleRuntimeServices m_services;
        private readonly NodeId m_allowedProductType;
        private readonly Func<ArrayOf<EUInformation>> m_allowedUnits;
        private readonly Dictionary<string, ProductState> m_products = new(StringComparer.Ordinal);
        private readonly List<string> m_current = [];
        private readonly Lock m_lock = new();
        private ArrayOf<string> m_currentProducts = [];
        private bool m_registered;
    }
}
