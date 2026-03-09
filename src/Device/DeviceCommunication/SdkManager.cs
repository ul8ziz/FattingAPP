using System;
using System.Diagnostics;
using System.IO;
using SDLib;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Single owner of IProductManager. All Initialize, ReloadForFirmware, and Dispose calls MUST run inside SdkGate.
    /// Create only from DeviceSessionService.EnsureSdkReadyForScanAsync (one instance per session).
    /// </summary>
    public class SdkManager : IDisposable
    {
        private IProductManager? _productManager;
        private ILibrary? _library;
        private IProduct? _product;
        private bool _isInitialized = false;

        public IProductManager ProductManager => _productManager ?? throw new InvalidOperationException("SDK not initialized");

        public bool IsInitialized => _isInitialized;

        public SdkManager()
        {
        }

        /// <summary>Must be called from within SdkGate.Run. Sets SdkLifecycle to Initializing then Ready.</summary>
        public void Initialize(string? libraryPath = null, string? productDescription = null)
        {
            if (SdkLifecycle.IsDisposingOrDisposed)
            {
                Debug.WriteLine("[SdkManager] Initialize skipped — lifecycle Disposing/Disposed");
                throw new InvalidOperationException("SDK is disposing or disposed; cannot initialize.");
            }
            if (_isInitialized)
                return;

            SdkLifecycle.SetState(SdkLifecycleState.Initializing);

            try
            {
                // Setup environment
                SdkConfiguration.SetupEnvironment();

                // Get ProductManager instance
                Debug.WriteLine("Getting ProductManager instance...");
                _productManager = SDLibMain.GetProductManagerInstance();
                
                if (_productManager == null)
                    throw new InvalidOperationException("Failed to get ProductManager instance");

                Debug.WriteLine($"SDK Version: {_productManager.Version}");

                // Load library
                var libPath = libraryPath ?? SdkConfiguration.GetLibraryPath();
                
                if (!File.Exists(libPath))
                    throw new FileNotFoundException($"Library file not found: {libPath}");

                Debug.WriteLine($"Loading library from: {libPath}");
                _library = _productManager.LoadLibraryFromFile(libPath);
                
                if (_library == null)
                    throw new InvalidOperationException("Failed to load library");

                // Get product - following SDK example pattern
                if (_library.Products != null && _library.Products.Count > 0)
                {
                    IProductDefinition? productDef = null;
                    
                    if (!string.IsNullOrEmpty(productDescription))
                    {
                        // Find specific product by description
                        foreach (IProductDefinition pd in _library.Products)
                        {
                            if (pd.Description == productDescription)
                            {
                                productDef = pd;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Use first product if no description specified
                        foreach (IProductDefinition pd in _library.Products)
                        {
                            productDef = pd;
                            break;
                        }
                    }

                    if (productDef != null)
                    {
                        Debug.WriteLine($"Creating product: {productDef.Description}");
                        try
                        {
                            _product = productDef.CreateProduct();
                        }
                        catch (System.Reflection.TargetInvocationException tex)
                        {
                            var inner = tex.InnerException ?? tex;
                            Debug.WriteLine($"[SdkManager] CreateProduct TargetInvocationException inner: {inner.Message}");
                            throw new InvalidOperationException($"CreateProduct failed: {inner.Message}", inner);
                        }
                    }
                    else
                    {
                        Debug.WriteLine("WARNING: No matching product definition found");
                    }
                }

                _isInitialized = true;
                Debug.WriteLine("SDK initialized successfully");
                SdkLifecycle.SetState(SdkLifecycleState.Ready);
                // A) CTK interface dump BEFORE any HI-PRO attempt (exact format: CTK_IF_COUNT, CTK_IF[i])
                ScanDiagnostics.DumpCtkInterfaces(_productManager);
            }
            catch (Exception ex)
            {
                _isInitialized = false;
                SdkLifecycle.SetState(SdkLifecycleState.Uninitialized);
                Debug.WriteLine($"SDK initialization failed: {ex.Message}");
                if (ScanDiagnostics.IsSdException(ex))
                    ScanDiagnostics.LogSdExceptionDetails(_productManager, ex);
                throw new InvalidOperationException($"Failed to initialize SDK: {ex.Message}", ex);
            }
        }

        public IProduct? GetProduct()
        {
            return _product;
        }

        /// <summary>The firmware ID the current product was loaded for (null if default).</summary>
        public string? LoadedFirmwareId { get; private set; }

        /// <summary>Must be called from within SdkGate. Reloads library/product for the given firmware; keeps single ProductManager.</summary>
        public void ReloadForFirmware(string firmwareId)
        {
            if (SdkLifecycle.IsDisposingOrDisposed)
                throw new InvalidOperationException("SDK is disposing or disposed; cannot reload.");
            if (string.IsNullOrWhiteSpace(firmwareId))
                throw new ArgumentException("FirmwareId is required", nameof(firmwareId));

            // Check if already loaded for this firmware
            if (string.Equals(LoadedFirmwareId, firmwareId, StringComparison.OrdinalIgnoreCase) && _isInitialized)
            {
                Debug.WriteLine($"[SdkManager] Already loaded for firmware {firmwareId}, skipping reload");
                return;
            }

            // Find matching library
            var match = LibraryService.FindLibraryForFirmware(firmwareId);
            if (match == null)
                throw new InvalidOperationException($"No library found matching firmware: {firmwareId}");

            Debug.WriteLine($"[SdkManager] Reloading for firmware {firmwareId} → library {match.FileName}");

            // Dispose current product/library (keep ProductManager since it's a singleton)
            try { if (_product is IDisposable dp) dp.Dispose(); } catch { }
            try { if (_library is IDisposable dl) dl.Dispose(); } catch { }
            _product = null;
            _library = null;
            _isInitialized = false;
            LoadedFirmwareId = null;

            // Reload with correct library
            if (_productManager == null)
            {
                SdkConfiguration.SetupEnvironment();
                _productManager = SDLibMain.GetProductManagerInstance();
                if (_productManager == null)
                    throw new InvalidOperationException("Failed to get ProductManager instance");
            }

            _library = _productManager.LoadLibraryFromFile(match.FullPath);
            if (_library == null)
                throw new InvalidOperationException($"Failed to load library: {match.FullPath}");

            if (_library.Products != null && _library.Products.Count > 0)
            {
                foreach (IProductDefinition pd in _library.Products)
                {
                    Debug.WriteLine($"[SdkManager] Creating product for firmware {firmwareId}: {pd.Description}");
                    _product = pd.CreateProduct();
                    break;
                }
            }

            if (_product == null)
                throw new InvalidOperationException($"No product definitions found in library for firmware: {firmwareId}");

            LoadedFirmwareId = firmwareId;
            _isInitialized = true;
            Debug.WriteLine($"[SdkManager] Reload complete: firmware={firmwareId}, library={match.FileName}");
        }

/// <summary>Must be called from within SdkGate after gate drain. Closes product/library; clears ProductManager reference.</summary>
        public void Dispose()
        {
            if (SdkLifecycle.State != SdkLifecycleState.Disposing && SdkLifecycle.State != SdkLifecycleState.Disposed)
                Debug.WriteLine("[Dispose] SdkManager.Dispose called (ensure called from gate after drain)");
            try
            {
                if (_product is IDisposable disposableProduct)
                    disposableProduct.Dispose();
            }
            catch { /* ignore */ }

            try
            {
                if (_library is IDisposable disposableLibrary)
                    disposableLibrary.Dispose();
            }
            catch { /* ignore */ }

            _product = null;
            _library = null;
            _productManager = null;
            _isInitialized = false;
            SdkLifecycle.SetState(SdkLifecycleState.Disposed);
        }
    }
}
