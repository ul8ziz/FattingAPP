using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication
{
    /// <summary>
    /// Holds user-added external library root folders and produces <see cref="LibraryInfo"/> entries
    /// by scanning for *.library files. Thread-safe for UI updates vs SDK gate readers.
    /// Folder enumeration is app-side; SDK loads by file path only (sounddesigner_programmers_guide.pdf section 6.1,
    /// sounddesigner_api_reference.pdf section 2.1.3 — LoadLibraryFromFile).
    /// Recursive scan depth is not specified in SDK docs; we use <see cref="SearchOption.AllDirectories"/> as app policy.
    /// </summary>
    public sealed class ProductLibraryRegistry
    {
        private static readonly ProductLibraryRegistry _instance = new();
        public static ProductLibraryRegistry Instance => _instance;

        private readonly object _sync = new();
        private List<string> _externalRoots = new();

        private ProductLibraryRegistry() { }

        /// <summary>Replaces the list of external root folders (normalized, distinct). Call after loading settings or user edits.</summary>
        public void ReplaceExternalRootFolders(IReadOnlyList<string>? roots)
        {
            lock (_sync)
            {
                _externalRoots = roots == null || roots.Count == 0
                    ? new List<string>()
                    : roots
                        .Where(r => !string.IsNullOrWhiteSpace(r))
                        .Select(r => r.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }
        }

        public IReadOnlyList<string> GetExternalRootFoldersSnapshot()
        {
            lock (_sync)
            {
                return _externalRoots.ToList();
            }
        }

        /// <summary>
        /// Scans all registered roots for *.library files. Skips missing/inaccessible folders.
        /// Not specified in SDK docs whether to search subfolders; app uses recursive search.
        /// </summary>
        public List<LibraryInfo> EnumerateExternalLibraryInfos()
        {
            List<string> roots;
            lock (_sync)
            {
                roots = _externalRoots.ToList();
            }

            var byPath = new Dictionary<string, LibraryInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                string[] files;
                try
                {
                    files = Directory.GetFiles(root, "*.library", SearchOption.AllDirectories);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var fullPath in files)
                {
                    if (byPath.ContainsKey(fullPath))
                        continue;

                    var stem = Path.GetFileNameWithoutExtension(fullPath);
                    byPath[fullPath] = new LibraryInfo
                    {
                        Id = "ext:" + fullPath,
                        SourceKind = LibrarySourceKind.ExternalFolder,
                        SourceRootFolder = root,
                        FileName = stem,
                        FullPath = fullPath,
                        DisplayLabel = stem
                    };
                }
            }

            return byPath.Values.ToList();
        }

        /// <summary>Counts *.library files under a folder (recursive). Used before adding a folder to the catalog.</summary>
        public static int CountLibraryFilesInTree(string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
                return 0;
            try
            {
                return Directory.GetFiles(rootFolder, "*.library", SearchOption.AllDirectories).Length;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }
}
