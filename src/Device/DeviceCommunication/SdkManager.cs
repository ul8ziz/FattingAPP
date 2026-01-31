using System;
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
            {
                return;
            }

            try
            {
                // Setup environment
                SdkConfiguration.SetupEnvironment();

                // Get ProductManager instance
                _productManager = SDLibMain.GetProductManagerInstance();
                
                if (_productManager == null)
                {
                    throw new InvalidOperationException("Failed to get ProductManager instance");
                }

                // Load library
                var libPath = libraryPath ?? SdkConfiguration.GetLibraryPath();
                
                if (!File.Exists(libPath))
                {
                    throw new FileNotFoundException($"Library file not found: {libPath}");
                }

                _library = _productManager.LoadLibraryFromFile(libPath);
                
                if (_library == null)
                {
                    throw new InvalidOperationException("Failed to load library");
                }

                // Get product
                if (_library.Products != null && _library.Products.Count > 0)
                {
                    IProductDefinition? productDef = null;
                    
                    if (!string.IsNullOrEmpty(productDescription))
                    {
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
                        var enumerator = _library.Products.GetEnumerator();
                        if (enumerator.MoveNext())
                        {
                            productDef = enumerator.Current as IProductDefinition;
                        }
                    }

                    if (productDef != null)
                    {
                        _product = productDef.CreateProduct();
                    }
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _isInitialized = false;
                throw new InvalidOperationException($"Failed to initialize SDK: {ex.Message}", ex);
            }
        }

        public IProduct? GetProduct()
        {
            return _product;
        }

        public void Dispose()
        {
            // SDK cleanup if needed
            _product = null;
            _library = null;
            _productManager = null;
            _isInitialized = false;
        }
    }
}
