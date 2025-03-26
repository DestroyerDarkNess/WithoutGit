using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Written by Destroyer | 26/03/2025
// https://github.com/DestroyerDarkNess/WithoutGit/ 
//
// This code is licensed under the MIT License.
// See the LICENSE file in the project root for more information.
// 
namespace WithoutGit
{
    /// <summary>
    /// Main class for cloning GitHub repositories using the REST API.
    /// This class allows you to clone GitHub repositories without using Git, by leveraging
    /// the GitHub REST API to download repository contents. It provides functionality
    /// for filtering files, multi-threading, and monitoring progress.
    /// </summary>
    public class Clone : IDisposable
    {
        #region Events

        /// <summary>
        /// Triggers the CloningStarted event.
        /// Called when the cloning process begins.
        /// </summary>
        public event EventHandler CloningStarted;

        /// <summary>
        /// Triggers the CloningProgress event.
        /// Called periodically to report progress.
        /// </summary>
        public event EventHandler<CloningProgressEventArgs> CloningProgress;

        /// <summary>
        /// Triggers the CloningCompleted event.
        /// Called when the cloning process finishes.
        /// </summary>
        public event EventHandler<CloningCompletedEventArgs> CloningCompleted;

        /// <summary>
        /// Triggers the LogErrors event.
        /// Called when an error occurs.
        /// </summary>
        public event EventHandler<LoggingEventArgs> LogErrors;

        /// <summary>
        /// Triggers the DownloadFileEvent event.
        /// Called when a file is being downloaded with custom handling.
        /// </summary>
        public event EventHandler<DownloadEventArgs> DownloadFileEvent;

        #endregion

        #region Fields and Constants

        /// <summary>
        /// HttpClientHandler with optimized settings for downloads
        /// </summary>
        private static readonly HttpClientHandler _handler = new HttpClientHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = 100
        };

        /// <summary>
        /// Base URL for GitHub API requests
        /// </summary>
        private const string GitHubApiBase = "https://api.github.com/repos/";

        /// <summary>
        /// GitHub personal access token for authentication
        /// </summary>
        private readonly string _githubToken;

        /// <summary>
        /// HttpClient instance for making HTTP requests
        /// </summary>
        private readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// The structure of the repository being cloned
        /// </summary>
        private RepositoryItem _repositoryStructure;

        /// <summary>
        /// Total number of files to be processed
        /// </summary>
        private int _totalFiles;

        /// <summary>
        /// Number of files already processed
        /// </summary>
        private int _processedFiles;

        /// <summary>
        /// Compiled regex patterns for include file filtering
        /// </summary>
        private List<Regex> _includeRegex = new List<Regex>();

        /// <summary>
        /// Compiled regex patterns for exclude file filtering
        /// </summary>
        private List<Regex> _excludeRegex = new List<Regex>();

        /// <summary>
        /// Glob patterns for including files/directories.
        /// Example: ["**/*.cs", "docs/**"]
        /// </summary>
        public List<string> IncludePaths { get; set; } = new List<string>();

        /// <summary>
        /// Glob patterns for excluding files/directories.
        /// Example: ["temp/*", "*.log"]
        /// </summary>
        public List<string> ExcludePatterns { get; set; } = new List<string>();

        /// <summary>
        /// For controlling concurrent operations
        /// </summary>
        private SemaphoreSlim _semaphore;

        /// <summary>
        /// Enables multi-threaded processing
        /// </summary>
        private bool _multiThreading = false;

        /// <summary>
        /// Maximum number of concurrent threads
        /// </summary>
        private int _maxConcurrentThreads = 4;

        /// <summary>
        /// Enables or disables multi-threaded processing (Default: false)
        /// </summary>
        public bool MultiThreading
        {
            get => _multiThreading;
            set => _multiThreading = value;
        }

        /// <summary>
        /// Maximum number of concurrent threads (Default: 4)
        /// </summary>
        public int MaxConcurrentThreads
        {
            get => _maxConcurrentThreads;
            set => _maxConcurrentThreads = Math.Max(1, value);
        }

        /// <summary>
        /// Enables custom download handling via events
        /// </summary>
        public bool CustomDownload { get; set; } = false;

        /// <summary>
        /// When enabled, ensures local directory exactly matches remote
        /// </summary>
        public bool MirrorMode { get; set; } = false;

        /// <summary>
        /// Previous repository structure for comparison in update operations
        /// </summary>
        private RepositoryItem _previousStructure;

        /// <summary>
        /// Filename for storing repository metadata
        /// </summary>
        private const string MetadataFileName = ".clone_metadata.csv";

        #endregion

        #region Download Priority

        /// <summary>
        /// Enum that controls the order in which files are downloaded
        /// </summary>
        public enum DownloadPriority
        {
            /// <summary>
            /// Orders by file size (smaller files first)
            /// </summary>
            SmallFilesFirst,

            /// <summary>
            /// Prioritizes specific file extensions
            /// </summary>
            ByFileType,

            /// <summary>
            /// Downloads files in the current directory first
            /// </summary>
            DepthFirst,

            /// <summary>
            /// No specific priority
            /// </summary>
            None
        }

        /// <summary>
        /// Current download priority setting
        /// </summary>
        public DownloadPriority PriorityMode { get; set; } = DownloadPriority.DepthFirst;

        /// <summary>
        /// List of file extensions to prioritize when using ByFileType
        /// </summary>
        public List<string> PriorityFileTypes { get; set; } = new List<string> { ".md" };

        #endregion

        #region Constructor
        /// <summary>
        /// Creates a new Clone instance without a GitHub token.
        /// Configures the HttpClient with optimized settings for performance.
        /// </summary>
        public Clone()
        {
            _httpClient = new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            ConfigureHttpClient();
        }

        /// <summary>
        /// Creates a new Clone instance with an optional GitHub token.
        /// Configures the HttpClient with optimized settings for performance.
        /// </summary>
        /// <param name="githubToken">GitHub personal access token for authentication</param>
        public Clone(string githubToken = null)
        {
            _githubToken = githubToken;
            _httpClient = new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            ConfigureHttpClient();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Clones a complete GitHub repository.
        /// This method downloads all files from a GitHub repository using the API,
        /// applying any include/exclude filters that have been configured.
        /// </summary>
        /// <param name="owner">Repository owner username</param>
        /// <param name="repositoryName">Name of the repository</param>
        /// <param name="localPath">Local path to save the files</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task CloneRepositoryAsync(string owner, string repositoryName, string localPath)
        {
            try
            {
                ValidatePatterns();
                _includeRegex = CompileGlobPatterns(IncludePaths);
                _excludeRegex = CompileGlobPatterns(ExcludePatterns);

                ValidateParameters(owner, repositoryName, localPath);
                OnCloningStarted();

                _repositoryStructure = await MapRepositoryStructure(owner, repositoryName);
                _totalFiles = CountFilesInStructure(_repositoryStructure);
                _processedFiles = 0;

                if (MultiThreading)
                {
                    _semaphore = new SemaphoreSlim(MaxConcurrentThreads);
                }

                Directory.CreateDirectory(localPath);
                await ProcessRepositoryStructure(_repositoryStructure, localPath);

                OnCloningCompleted(true, null);
            }
            catch (Exception ex)
            {
                OnCloningCompleted(false, ex.Message);
                throw;
            }
            finally
            {
                _semaphore?.Dispose();
            }
        }

        /// <summary>
        /// Clones a repository using the zipball approach (faster than file-by-file).
        /// This method downloads the repository as a zip file and extracts it,
        /// which is significantly faster than downloading each file individually.
        /// </summary>
        /// <param name="owner">Repository owner username</param>
        /// <param name="repositoryName">Name of the repository</param>
        /// <param name="localPath">Local path to save the files</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task CloneRepositoryFastAsync(string owner, string repositoryName, string localPath)
        {
            try
            {
                ValidatePatterns();
                _includeRegex = CompileGlobPatterns(IncludePaths);
                _excludeRegex = CompileGlobPatterns(ExcludePatterns);

                OnCloningStarted();

                var zipUrl = $"{GitHubApiBase}{owner}/{repositoryName}/zipball/main";
                var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
                var tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

                using (var response = await GetWithRetryAsync(zipUrl))
                {
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                            if (CloningProgress != null && totalBytes > 0) OnCloningProgress((int)totalRead, (int)totalBytes, (double)totalRead / totalBytes * 50);
                        }
                    }
                }

                if (CloningProgress != null) OnCloningProgress(1, 1, (double)50);

                Directory.CreateDirectory(tempExtractPath);

                using (var archive = ZipFile.OpenRead(tempFile))
                {
                    var entries = archive.Entries.Where(e => !e.FullName.EndsWith("/"));
                    var totalFiles = entries.Count();
                    var processed = 0;
                    var extractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 1. Extract files directly to the final directory
                    foreach (var entry in entries)
                    {
                        try
                        {
                            int slashIndex = entry.FullName.IndexOf('/');
                            if (slashIndex == -1)
                            {
                                OnLogErrors(new Exception("Invalid entry format"), $"Entry '{entry.FullName}' is invalid");
                                continue;
                            }

                            var relativePath = entry.FullName.Substring(slashIndex + 1);
                            var destPath = Path.Combine(localPath, relativePath);

                            if (IsPathAllowed(relativePath.Replace('\\', '/')))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                                entry.ExtractToFile(destPath, true);
                                extractedPaths.Add(relativePath.Replace('\\', '/'));
                            }

                            processed++;
                            if (CloningProgress != null) OnCloningProgress(processed, totalFiles, 50 + (double)processed / totalFiles * 50);
                        }
                        catch (Exception ex)
                        {
                            OnLogErrors(ex, $"Error extracting {entry.FullName}");
                        }
                    }

                    if (MirrorMode)
                    {
                        var existingFiles = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories)
                            .Select(f => f.Substring(localPath.Length + 1).Replace('\\', '/'));

                        foreach (var file in existingFiles)
                        {
                            if (!extractedPaths.Contains(file))
                            {
                                File.Delete(Path.Combine(localPath, file));
                            }
                        }

                        foreach (var dir in Directory.GetDirectories(localPath, "*", SearchOption.AllDirectories)
                            .OrderByDescending(d => d.Length))
                        {
                            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            {
                                Directory.Delete(dir);
                            }
                        }

                        SaveMetadata(await MapRepositoryStructure(owner, repositoryName), localPath);
                    }

                }

                if (File.Exists(tempFile)) File.Delete(tempFile);

                OnCloningCompleted(true, null);
            }
            catch (Exception ex)
            {
                OnCloningCompleted(false, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Updates an existing repository clone by downloading only changed files.
        /// This method compares the local repository with the remote one and only
        /// downloads files that have been added or modified, and removes files that
        /// have been deleted (if MirrorMode is enabled).
        /// </summary>
        /// <param name="owner">Repository owner username</param>
        /// <param name="repositoryName">Name of the repository</param>
        /// <param name="localPath">Local path where files are saved</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task UpdateRepositoryAsync(string owner, string repositoryName, string localPath)
        {
            ValidateParameters(owner, repositoryName, localPath);
            OnCloningStarted();

            _previousStructure = LoadMetadata(localPath);

            var newStructure = await MapRepositoryStructure(owner, repositoryName);
            _totalFiles = CountFilesInStructure(newStructure);
            _processedFiles = 0;

            var changes = CompareStructures(_previousStructure, newStructure);

            await ApplyChanges(changes, localPath);

            SaveMetadata(newStructure, localPath);

            OnCloningCompleted(true, null);
        }

        #endregion

        #region Patterns

        /// <summary>
        /// Validates include/exclude glob patterns.
        /// Checks for invalid pattern combinations that could cause unexpected behavior.
        /// </summary>
        private void ValidatePatterns()
        {
            if (IncludePaths.Any() && ExcludePatterns.Any(p => p.Contains("**")))
                throw new ArgumentException("Cannot use ** in exclusions with active includes");
        }

        /// <summary>
        /// Converts glob patterns to compiled regex patterns for efficient matching.
        /// </summary>
        /// <param name="patterns">List of glob patterns to compile</param>
        /// <returns>List of compiled Regex objects</returns>
        private List<Regex> CompileGlobPatterns(List<string> patterns)
        {
            return patterns.Select(p => new Regex(GlobToRegex(p), RegexOptions.Compiled)).ToList();
        }

        /// <summary>
        /// Converts a single glob pattern to a regex pattern.
        /// Translates wildcard characters (* and ?) to their regex equivalents.
        /// </summary>
        /// <param name="glob">The glob pattern to convert</param>
        /// <returns>A regex pattern string</returns>
        private string GlobToRegex(string glob)
        {
            string regex = Regex.Escape(glob)
                .Replace("\\*\\*", ".*")      // ** → .*
                .Replace("\\*", "[^/]*")       // *  → [^/]*
                .Replace("\\?", "[^/]");       // ?  → [^/]

            return $"^{regex}$";
        }

        /// <summary>
        /// Checks if a path should be included based on the configured include/exclude patterns.
        /// A path is included if:
        /// 1. It matches at least one include pattern (or there are no include patterns)
        /// 2. It doesn't match any exclude patterns
        /// </summary>
        /// <param name="path">The path to check</param>
        /// <returns>True if the path should be included, false otherwise</returns>
        private bool IsPathAllowed(string path)
        {
            bool isIncluded = !_includeRegex.Any() || _includeRegex.Any(r => r.IsMatch(path));
            bool isExcluded = _excludeRegex.Any(r => r.IsMatch(path));
            return isIncluded && !isExcluded;
        }

        #endregion

        #region Diff Logic

        /// <summary>
        /// Represents a change in repository structure (added, modified, or deleted item)
        /// </summary>
        private class StructureChange
        {
            /// <summary>
            /// The repository item that has changed
            /// </summary>
            public RepositoryItem Item { get; set; }

            /// <summary>
            /// The type of change (Added, Modified, or Deleted)
            /// </summary>
            public ChangeType Type { get; set; }
        }

        /// <summary>
        /// Enum for the type of change in repository structure
        /// </summary>
        private enum ChangeType
        {
            /// <summary>
            /// Item was added in the new structure
            /// </summary>
            Added,

            /// <summary>
            /// Item was modified (content changed)
            /// </summary>
            Modified,

            /// <summary>
            /// Item was deleted in the new structure
            /// </summary>
            Deleted
        }

        /// <summary>
        /// Compares old and new repository structures to detect changes.
        /// </summary>
        /// <param name="oldStructure">The old repository structure</param>
        /// <param name="newStructure">The new repository structure</param>
        /// <returns>A list of changes between the structures</returns>
        private List<StructureChange> CompareStructures(RepositoryItem oldStructure, RepositoryItem newStructure)
        {
            var changes = new List<StructureChange>();
            CompareNodes(oldStructure, newStructure, changes);
            return changes;
        }

        /// <summary>
        /// Recursively compares nodes in repository structure to detect changes.
        /// </summary>
        /// <param name="oldNode">The old node in the repository structure</param>
        /// <param name="newNode">The new node in the repository structure</param>
        /// <param name="changes">The list to collect detected changes</param>
        private void CompareNodes(RepositoryItem oldNode, RepositoryItem newNode, List<StructureChange> changes)
        {
            var oldItems = oldNode?.Children.ToDictionary(i => i.Path) ?? new Dictionary<string, RepositoryItem>();

            foreach (var newItem in newNode.Children)
            {
                if (!oldItems.TryGetValue(newItem.Path, out var oldItem))
                {
                    changes.Add(new StructureChange { Item = newItem, Type = ChangeType.Added });
                }
                else
                {
                    if (newItem.IsFile && (newItem.Sha != oldItem.Sha))
                    {
                        changes.Add(new StructureChange { Item = newItem, Type = ChangeType.Modified });
                    }
                    CompareNodes(oldItem, newItem, changes);
                }
            }

            if (MirrorMode)
            {
                foreach (var oldItem in oldItems.Values)
                {
                    if (!newNode.Children.Any(n => n.Path == oldItem.Path))
                    {
                        changes.Add(new StructureChange { Item = oldItem, Type = ChangeType.Deleted });
                    }
                }
            }
        }

        /// <summary>
        /// Applies detected changes to the local directory.
        /// Processes additions, modifications, and deletions based on the detected changes.
        /// </summary>
        /// <param name="changes">The list of changes to apply</param>
        /// <param name="localPath">The local directory path</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task ApplyChanges(List<StructureChange> changes, string localPath)
        {
            var tasks = new List<Task>();

            foreach (var change in changes)
            {
                var localFilePath = Path.Combine(localPath, change.Item.Path);

                switch (change.Type)
                {
                    case ChangeType.Added:
                    case ChangeType.Modified:
                        if (change.Item.IsFile)
                        {
                            tasks.Add(ProcessFileAsync(change.Item.DownloadUrl, localFilePath));
                        }
                        else
                        {
                            Directory.CreateDirectory(localFilePath);
                        }
                        break;

                    case ChangeType.Deleted:
                        if (File.Exists(localFilePath)) File.Delete(localFilePath);
                        if (Directory.Exists(localFilePath)) Directory.Delete(localFilePath, true);
                        break;
                }
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Saves repository metadata to a CSV file.
        /// This metadata is used for future update operations.
        /// </summary>
        /// <param name="structure">The repository structure to save</param>
        /// <param name="localPath">The local directory path</param>
        private void SaveMetadata(RepositoryItem structure, string localPath)
        {
            var csvLines = new List<string> { "Path,Type,Sha,DownloadUrl" };
            BuildMetadataCsv(structure, csvLines);

            File.WriteAllText(
                Path.Combine(localPath, MetadataFileName),
                string.Join("\n", csvLines)
            );
        }

        /// <summary>
        /// Builds CSV lines for repository metadata.
        /// Recursively adds entries for all items in the repository structure.
        /// </summary>
        /// <param name="node">The current node in the repository structure</param>
        /// <param name="csvLines">The list to collect CSV lines</param>
        private void BuildMetadataCsv(RepositoryItem node, List<string> csvLines)
        {
            foreach (var child in node.Children)
            {
                var line = $@"""{child.Path}"",{child.Type},""{child.Sha}"",""{child.DownloadUrl}""";
                csvLines.Add(line);

                if (child.IsDirectory)
                {
                    BuildMetadataCsv(child, csvLines);
                }
            }
        }

        /// <summary>
        /// Loads repository metadata from a CSV file.
        /// This metadata is used for update operations to detect changes.
        /// </summary>
        /// <param name="localPath">The local directory path</param>
        /// <returns>The loaded repository structure, or null if no metadata exists</returns>
        private RepositoryItem LoadMetadata(string localPath)
        {
            var metadataFile = Path.Combine(localPath, MetadataFileName);
            if (!File.Exists(metadataFile)) return null;

            var root = new RepositoryItem { Type = "dir", Path = "" };
            var lines = File.ReadAllLines(metadataFile).Skip(1); // Skip header

            foreach (var line in lines)
            {
                var match = Regex.Match(line,
                    @"^""(?<path>[^""]*)"",(?<type>dir|file),""(?<sha>[^""]*)"",""(?<url>[^""]*)""$");

                if (!match.Success) continue;

                var item = new RepositoryItem
                {
                    Path = match.Groups["path"].Value,
                    Type = match.Groups["type"].Value,
                    Sha = match.Groups["sha"].Value,
                    DownloadUrl = match.Groups["url"].Value
                };

                AddToStructure(root, item);
            }

            return root;
        }

        /// <summary>
        /// Adds an item to the repository structure.
        /// Creates the necessary directory structure if it doesn't exist.
        /// </summary>
        /// <param name="root">The root of the repository structure</param>
        /// <param name="item">The item to add</param>
        private void AddToStructure(RepositoryItem root, RepositoryItem item)
        {
            var pathParts = item.Path.Split('/');
            var current = root;

            foreach (var part in pathParts)
            {
                var existing = current.Children.FirstOrDefault(c => c.Name == part);
                if (existing == null)
                {
                    existing = new RepositoryItem
                    {
                        Name = part,
                        Path = current.Path == "" ? part : $"{current.Path}/{part}",
                        Type = "dir"
                    };
                    current.Children.Add(existing);
                }
                current = existing;
            }

            // Update with actual data
            current.Type = item.Type;
            current.Sha = item.Sha;
            current.DownloadUrl = item.DownloadUrl;
        }

        #endregion

        #region Private Methods
        /// <summary>
        /// Configures the HttpClient with optimal settings for performance.
        /// Sets up connection limits, compression, user agent, and authentication.
        /// </summary>
        private void ConfigureHttpClient()
        {
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubRepositoryCloner");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            if (!string.IsNullOrEmpty(_githubToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _githubToken);
            }
        }

        /// <summary>
        /// Maps the structure of a GitHub repository.
        /// Recursively fetches the contents of the repository starting from the specified path.
        /// </summary>
        /// <param name="owner">Repository owner username</param>
        /// <param name="repoName">Name of the repository</param>
        /// <param name="currentPath">Current path within the repository (default: "")</param>
        /// <returns>A repository item representing the structure at the current path</returns>
        private async Task<RepositoryItem> MapRepositoryStructure(string owner, string repoName, string currentPath = "")
        {
            var apiUrl = $"{GitHubApiBase}{owner}/{repoName}/contents/{currentPath}";
            var response = await _httpClient.GetAsync(apiUrl);

            if (!CheckRateLimit(response)) { OnLogErrors(new Exception($"{apiUrl}|{response.StatusCode}"), "Request limit reached"); return null; }

            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                OnLogErrors(new Exception($"{apiUrl}|{response.StatusCode}"), content);
                return null;
            }

            var entries = GitHubContentEntry.FromJson(content);
            var node = new RepositoryItem
            {
                Path = currentPath,
                Type = "dir"
            };

            foreach (var entry in entries)
            {
                var item = new RepositoryItem
                {
                    Name = entry.Name,
                    Path = entry.Path,
                    Type = entry.Type,
                    DownloadUrl = entry.DownloadUrl
                };

                if (entry.Type == "dir")
                {
                    var childNode = await MapRepositoryStructure(owner, repoName, entry.Path);
                    if (childNode != null)
                    {
                        item.Children.AddRange(childNode.Children);
                    }
                }

                node.Children.Add(item);
            }

            return node;
        }

        /// <summary>
        /// Counts the files in a repository structure.
        /// Recursively traverses the structure and counts all file items.
        /// </summary>
        /// <param name="node">The repository structure node to count files in</param>
        /// <returns>The number of files in the structure</returns>
        private int CountFilesInStructure(RepositoryItem node)
        {
            if (node == null) return 0;

            int count = 0;
            foreach (var child in node.Children)
            {
                if (child.IsFile && !string.IsNullOrEmpty(child.DownloadUrl))
                {
                    count++;
                }
                else if (child.IsDirectory)
                {
                    count += CountFilesInStructure(child);
                }
            }
            return count;
        }

        /// <summary>
        /// Processes a repository structure for cloning.
        /// Creates directories and downloads files according to the structure.
        /// </summary>
        /// <param name="node">The repository structure node to process</param>
        /// <param name="localPath">The local directory path</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task ProcessRepositoryStructure(RepositoryItem node, string localPath)
        {
            if (node == null) return;

            var orderedChildren = OrderChildren(node.Children);
            var tasks = new List<Task>();

            foreach (var child in orderedChildren)
            {
                string normalizedPath = child.Path.Replace('\\', '/');

                if (!IsPathAllowed(normalizedPath)) continue;

                var childPath = Path.Combine(localPath, child.Name);

                if (child.IsDirectory)
                {
                    Directory.CreateDirectory(childPath);
                    await ProcessRepositoryStructure(child, childPath);
                }
                else if (child.IsFile && !string.IsNullOrEmpty(child.DownloadUrl))
                {
                    if (MultiThreading)
                    {
                        tasks.Add(ProcessFileAsync(child.DownloadUrl, childPath));
                    }
                    else
                    {
                        await ProcessFileAsync(child.DownloadUrl, childPath);
                    }
                }
            }

            if (MultiThreading && tasks.Any())
            {
                await Task.WhenAll(tasks);
            }
        }

        /// <summary>
        /// Orders children nodes based on priority settings.
        /// Different ordering strategies are applied based on the PriorityMode setting.
        /// </summary>
        /// <param name="children">The children nodes to order</param>
        /// <returns>The ordered children nodes</returns>
        private IEnumerable<RepositoryItem> OrderChildren(IEnumerable<RepositoryItem> children)
        {
            switch (PriorityMode)
            {
                case DownloadPriority.SmallFilesFirst:
                    return children.OrderBy(c => c.IsDirectory ? 1 : 0)
                                   .ThenBy(c => c.Size);

                case DownloadPriority.ByFileType:
                    return children.OrderBy(c => GetTypePriority(c.FileExtension))
                                   .ThenBy(c => c.Name);

                case DownloadPriority.DepthFirst:
                    return children.OrderBy(c => c.IsDirectory ? 1 : 0);

                default:
                    return children;
            }
        }

        /// <summary>
        /// Gets the priority for a file extension.
        /// Lower priority values are processed first.
        /// </summary>
        /// <param name="fileExtension">The file extension to get priority for</param>
        /// <returns>The priority value (lower is higher priority)</returns>
        private int GetTypePriority(string fileExtension)
        {
            var index = PriorityFileTypes.FindIndex(ext =>
                ext.Equals(fileExtension, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : int.MaxValue;
        }

        /// <summary>
        /// Processes a file download asynchronously.
        /// Handles thread synchronization if multi-threading is enabled.
        /// </summary>
        /// <param name="downloadUrl">The URL to download from</param>
        /// <param name="localPath">The local path to save to</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task ProcessFileAsync(string downloadUrl, string localPath)
        {
            if (MultiThreading)
            {
                await _semaphore.WaitAsync();
                try
                {
                    await DownloadFile(downloadUrl, localPath);
                    UpdateProgressThreadSafe();
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            else
            {
                await DownloadFile(downloadUrl, localPath);
                UpdateProgress();
            }
        }

        /// <summary>
        /// Updates progress in a thread-safe manner.
        /// Used when multi-threading is enabled to avoid race conditions.
        /// </summary>
        private void UpdateProgressThreadSafe()
        {
            Interlocked.Increment(ref _processedFiles);
            if (_totalFiles >= 1)
            {
                var processed = Interlocked.CompareExchange(ref _processedFiles, 0, 0);
                var percentage = (double)processed / _totalFiles * 100;
                OnCloningProgress(processed, _totalFiles, percentage);
            }
        }

        /// <summary>
        /// Checks if GitHub rate limit has been reached.
        /// Examines response headers to determine remaining API calls.
        /// </summary>
        /// <param name="response">The HTTP response to check</param>
        /// <returns>True if there are API calls remaining, false otherwise</returns>
        private bool CheckRateLimit(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
            {
                var remaining = int.Parse(remainingValues.FirstOrDefault());
                return remaining > 0;
            }
            return true;
        }

        /// <summary>
        /// Makes a GET request with retry logic.
        /// Handles rate limiting and transient errors with exponential backoff.
        /// </summary>
        /// <param name="requestUri">The URI to request</param>
        /// <param name="maxRetries">Maximum number of retry attempts (default: 5)</param>
        /// <returns>The HTTP response message</returns>
        /// <exception cref="HttpRequestException">Thrown when max retries are exceeded</exception>
        private async Task<HttpResponseMessage> GetWithRetryAsync(string requestUri, int maxRetries = 5)
        {
            int delay = 1000; // Initial delay in milliseconds
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && response.Headers.Contains("X-RateLimit-Remaining") && response.Headers.GetValues("X-RateLimit-Remaining").First() == "0")
                    {
                        // Rate limit exceeded, wait and retry
                        await Task.Delay(delay);
                        delay *= 2; // Exponential backoff
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }
                catch (HttpRequestException ex) when (ex.InnerException is IOException)
                {
                    // Handle network-related exceptions
                    await Task.Delay(delay);
                    delay *= 2; // Exponential backoff
                }
            }
            throw new HttpRequestException("Max retries exceeded.");
        }

        /// <summary>
        /// Downloads a file from a URL to a local path.
        /// Uses custom download handling if enabled, otherwise downloads directly.
        /// </summary>
        /// <param name="downloadUrl">The URL to download from</param>
        /// <param name="localPath">The local path to save to</param>
        /// <returns>A task representing the asynchronous operation</returns>
        private async Task DownloadFile(string downloadUrl, string localPath)
        {
            if (!CustomDownload)
            {
                if (MultiThreading)
                {
                    using (var response = await GetWithRetryAsync(downloadUrl))
                    using (var streamToRead = await response.Content.ReadAsStreamAsync())
                    using (var streamToWrite = File.Open(localPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920]; // 80KB buffer
                        int bytesRead;

                        while ((bytesRead = await streamToRead.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await streamToWrite.WriteAsync(buffer, 0, bytesRead);
                        }
                    }
                }
                else
                {
                    var fileBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
                    File.WriteAllBytes(localPath, fileBytes);
                }
            }
            else
            {
                OnFileDonwload(downloadUrl, localPath);
            }
        }

        /// <summary>
        /// Updates the progress counter and triggers progress event.
        /// Used when multi-threading is disabled.
        /// </summary>
        private void UpdateProgress()
        {
            _processedFiles++;
            if (_totalFiles >= 1)
            {
                try
                {
                    var percentage = (double)_processedFiles / _totalFiles * 100;
                    OnCloningProgress(_processedFiles, _totalFiles, percentage);
                }
                catch (Exception ex) { OnLogErrors(ex, ex.Message); }
            }
        }

        /// <summary>
        /// Validates method parameters.
        /// Ensures that required parameters are not null or empty.
        /// </summary>
        /// <param name="owner">Repository owner username</param>
        /// <param name="repoName">Name of the repository</param>
        /// <param name="localPath">Local path to save files</param>
        /// <exception cref="ArgumentException">Thrown when a parameter is invalid</exception>
        private void ValidateParameters(string owner, string repoName, string localPath)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("Invalid owner");

            if (string.IsNullOrWhiteSpace(repoName))
                throw new ArgumentException("Invalid repository name");

            if (string.IsNullOrWhiteSpace(localPath))
                throw new ArgumentException("Invalid local path");
        }
        #endregion

        #region Event Triggers
        /// <summary>
        /// Triggers the CloningStarted event.
        /// Called when the cloning process begins.
        /// </summary>
        protected virtual void OnCloningStarted()
        {
            CloningStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Triggers the CloningProgress event.
        /// Called periodically to report progress.
        /// </summary>
        /// <param name="processed">Number of processed files</param>
        /// <param name="total">Total number of files</param>
        /// <param name="percentage">Percentage complete (0-100)</param>
        protected virtual void OnCloningProgress(int processed, int total, double percentage)
        {
            CloningProgress?.Invoke(this, new CloningProgressEventArgs
            {
                ProcessedFiles = processed,
                TotalFiles = total,
                Percentage = percentage
            });
        }

        /// <summary>
        /// Triggers the CloningCompleted event.
        /// Called when the cloning process finishes.
        /// </summary>
        /// <param name="success">Whether the operation was successful</param>
        /// <param name="errorMessage">Error message if not successful</param>
        protected virtual void OnCloningCompleted(bool success, string errorMessage)
        {
            CloningCompleted?.Invoke(this, new CloningCompletedEventArgs
            {
                Success = success,
                ErrorMessage = errorMessage
            });
        }

        /// <summary>
        /// Triggers the LogErrors event.
        /// Called when an error occurs.
        /// </summary>
        /// <param name="exeption">The exception that occurred</param>
        /// <param name="errorMessage">A description of the error</param>
        protected virtual void OnLogErrors(Exception exeption, string errorMessage)
        {
            LogErrors?.Invoke(this, new LoggingEventArgs
            {
                Exeption = exeption,
                ErrorMessage = errorMessage
            });
        }

        /// <summary>
        /// Triggers the DownloadFileEvent event.
        /// Called when a file is being downloaded with custom handling.
        /// </summary>
        /// <param name="url">The URL being downloaded</param>
        /// <param name="Path">The local path being saved to</param>
        protected virtual void OnFileDonwload(string url, string Path)
        {
            DownloadFileEvent?.Invoke(this, new DownloadEventArgs
            {
                DownloadUrl = url,
                SavePath = Path
            });
        }
        #endregion

        #region Resource Cleanup

        /// <summary>
        /// Implements IDisposable to clean up resources.
        /// Disposes the HttpClient and semaphore.
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
            _semaphore?.Dispose();
        }

        #endregion
    }

    #region Support Classes

    /// <summary>
    /// Represents an item in the repository (file or directory).
    /// Contains information about the item's name, path, type, and other metadata.
    /// </summary>
    public class RepositoryItem
    {
        /// <summary>
        /// The name of the repository item
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The full path of the repository item
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// The type of the repository item ("file" or "dir")
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// The URL to download the item's content
        /// </summary>
        public string DownloadUrl { get; set; }

        /// <summary>
        /// The SHA hash of the item's content
        /// </summary>
        public string Sha { get; set; }

        /// <summary>
        /// The children of this item (for directories)
        /// </summary>
        public List<RepositoryItem> Children { get; } = new List<RepositoryItem>();

        /// <summary>
        /// Whether this item is a file
        /// </summary>
        public bool IsFile => Type == "file";

        /// <summary>
        /// Whether this item is a directory
        /// </summary>
        public bool IsDirectory => Type == "dir";

        /// <summary>
        /// The size of the item in bytes
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// The file extension of the item (or empty string for directories)
        /// </summary>
        public string FileExtension => IsFile ?
            System.IO.Path.GetExtension(Name)?.ToLowerInvariant() :
            string.Empty;
    }

    /// <summary>
    /// Represents an entry in GitHub content (file or directory)
    /// Contains data received from the GitHub API for a repository item.
    /// </summary>
    public class GitHubContentEntry
    {
        /// <summary>
        /// Regular expression for parsing JSON data
        /// </summary>
        private static readonly Regex JsonParser = new Regex(
            @"\""(name|path|type|download_url)\"":\s*(\""([^\""]*)\""|null)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// The name of the content entry
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The full path of the content entry
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// The type of the content entry ("file" or "dir")
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// The size of the content entry in bytes
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// The URL to download the content entry
        /// </summary>
        public string DownloadUrl { get; set; }

        /// <summary>
        /// Parses a JSON string into a list of GitHubContentEntry objects.
        /// Uses a lightweight regex-based approach to parse the JSON.
        /// </summary>
        /// <param name="json">The JSON string to parse</param>
        /// <returns>A list of GitHubContentEntry objects</returns>
        public static List<GitHubContentEntry> FromJson(string json)
        {
            var entries = new List<GitHubContentEntry>();
            var entriesRaw = SplitJsonObjects(json);

            foreach (var entryJson in entriesRaw)
            {
                var entry = ParseSingleEntry(entryJson);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>
        /// Splits a JSON array into individual JSON objects.
        /// Handles nested objects correctly by tracking depth.
        /// </summary>
        /// <param name="json">The JSON array string to split</param>
        /// <returns>A list of individual JSON object strings</returns>
        private static List<string> SplitJsonObjects(string json)
        {
            var objects = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        objects.Add(json.Substring(start, i - start + 1));
                    }
                }
            }
            return objects;
        }

        /// <summary>
        /// Parses a single JSON object into a GitHubContentEntry.
        /// Uses regex to extract key fields from the JSON.
        /// </summary>
        /// <param name="entryJson">The JSON string to parse</param>
        /// <returns>A GitHubContentEntry object, or null if parsing failed</returns>
        private static GitHubContentEntry ParseSingleEntry(string entryJson)
        {
            var entry = new GitHubContentEntry();
            var matches = JsonParser.Matches(entryJson);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (!match.Success) continue;

                var key = match.Groups[1].Value.ToLower();
                var value = match.Groups[3].Success ? match.Groups[3].Value : null;

                switch (key)
                {
                    case "name":
                        entry.Name = value;
                        break;
                    case "path":
                        entry.Path = value;
                        break;
                    case "type":
                        entry.Type = value;
                        break;
                    case "size" when long.TryParse(value, out long size):
                        entry.Size = size;
                        break;
                    case "download_url":
                        entry.DownloadUrl = value;
                        break;
                }
            }

            return !string.IsNullOrEmpty(entry.Name) ? entry : null;
        }
    }

    /// <summary>
    /// Arguments for the cloning progress event.
    /// Contains information about the current progress of the cloning operation.
    /// </summary>
    public class CloningProgressEventArgs : EventArgs
    {
        /// <summary>
        /// Number of files processed so far
        /// </summary>
        public int ProcessedFiles { get; set; }

        /// <summary>
        /// Total number of files to process
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// Percentage complete (0-100)
        /// </summary>
        public double Percentage { get; set; }

    }

    /// <summary>
    /// Arguments for the cloning completed event.
    /// Contains information about the result of the cloning operation.
    /// </summary>
    public class CloningCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// Whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if not successful
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Arguments for the logging event.
    /// Contains information about an error that occurred.
    /// </summary>
    public class LoggingEventArgs : EventArgs
    {
        /// <summary>
        /// The exception that occurred
        /// </summary>
        public Exception Exeption { get; set; }

        /// <summary>
        /// A description of the error
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Arguments for the download file event.
    /// Contains information about a file being downloaded.
    /// </summary>
    public class DownloadEventArgs : EventArgs
    {
        /// <summary>
        /// The URL being downloaded
        /// </summary>
        public string DownloadUrl { get; set; }

        /// <summary>
        /// The local path being saved to
        /// </summary>
        public string SavePath { get; set; }
    }
    #endregion

}
