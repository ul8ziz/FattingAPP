using System;
using System.Diagnostics;
using System.IO;
using SDLib;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
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

        public void Initialize(string? libraryPath = null, string? productDescription = null)
        {
            if (_isInitialized)
                return;

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
                        _product = productDef.CreateProduct();
                    }
                    else
                    {
                        Debug.WriteLine("WARNING: No matching product definition found");
                    }
                }

                _isInitialized = true;
                Debug.WriteLine("SDK initialized successfully");
            }
            catch (Exception ex)
            {
                _isInitialized = false;
                Debug.WriteLine($"SDK initialization failed: {ex.Message}");
                throw new InvalidOperationException($"Failed to initialize SDK: {ex.Message}", ex);
            }
        }

        public IProduct? GetProduct()
        {
            return _product;
        }

        public void Dispose()
        {
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
        }
    }
}
