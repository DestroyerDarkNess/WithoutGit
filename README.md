# WithoutGit - GitHub Repository Cloner

A .NET library for cloning GitHub repositories without using Git. This library uses the GitHub REST API to download repository contents, with features like filtering, multi-threading, and progress tracking.

## Features

- Clone entire GitHub repositories without Git installation
- Fast cloning using GitHub's zipball endpoint
- Filter files using glob patterns
- Track download progress via events
- Multi-threading support for faster downloads
- Repository updating (detect and apply changes)
- Mirror mode to ensure local directory exactly matches remote

## Installation

Add a reference to the WithoutGit library in your project.

```xml
<PackageReference Include="WithoutGit" Version="1.0.0" />
```

## Basic Usage

### Clone a Repository

```csharp
using WithoutGit;

// Create a new instance (optionally with a GitHub token)
using (var cloner = new Clone("your_github_token"))
{
    // Subscribe to events
    cloner.CloningStarted += (sender, args) => Console.WriteLine("Cloning started");
    cloner.CloningProgress += (sender, args) => Console.WriteLine($"Progress: {args.Percentage:F2}% ({args.ProcessedFiles}/{args.TotalFiles})");
    cloner.CloningCompleted += (sender, args) => Console.WriteLine($"Cloning completed: {args.Success}");
    cloner.LogErrors += (sender, args) => Console.WriteLine($"Error: {args.ErrorMessage}");

    // Clone a repository
    await cloner.CloneRepositoryAsync("owner", "repository", @"C:\path\to\local\directory");
}
```

### Fast Clone (Using Zipball)

```csharp
using WithoutGit;

using (var cloner = new Clone("your_github_token"))
{
    // Clone faster using zipball approach
    await cloner.CloneRepositoryFastAsync("owner", "repository", @"C:\path\to\local\directory");
}
```

### Update Existing Clone

```csharp
using WithoutGit;

using (var cloner = new Clone("your_github_token"))
{
    // Enable mirror mode to ensure exact matching
    cloner.MirrorMode = true;
    
    // Update an existing clone
    await cloner.UpdateRepositoryAsync("owner", "repository", @"C:\path\to\local\directory");
}
```

## Advanced Features

### File Filtering

```csharp
using WithoutGit;

using (var cloner = new Clone("your_github_token"))
{
    // Include only specific files
    cloner.IncludePaths.Add("**/*.cs");  // All C# files
    cloner.IncludePaths.Add("docs/**");  // All files in docs directory
    
    // Exclude specific files
    cloner.ExcludePatterns.Add("**/bin/**");  // Exclude bin directories
    cloner.ExcludePatterns.Add("**/*.exe");   // Exclude executable files
    
    // Clone with filters applied
    await cloner.CloneRepositoryAsync("owner", "repository", @"C:\path\to\local\directory");
}
```

### Multi-threading

```csharp
using WithoutGit;

using (var cloner = new Clone("your_github_token"))
{
    // Enable multi-threading
    cloner.MultiThreading = true;
    cloner.MaxConcurrentThreads = 8;  // Set maximum concurrent threads
    
    // Clone with multi-threading
    await cloner.CloneRepositoryAsync("owner", "repository", @"C:\path\to\local\directory");
}
```

### Download Priority

```csharp
using WithoutGit;

using (var cloner = new Clone("your_github_token"))
{
    // Set download priority mode
    cloner.PriorityMode = Clone.DownloadPriority.SmallFilesFirst;  // Download smaller files first
    
    // Or prioritize specific file types
    cloner.PriorityMode = Clone.DownloadPriority.ByFileType;
    cloner.PriorityFileTypes.Clear();
    cloner.PriorityFileTypes.Add(".md");   // Download markdown files first
    cloner.PriorityFileTypes.Add(".json"); // Then JSON files
    
    // Clone with priority settings
    await cloner.CloneRepositoryAsync("owner", "repository", @"C:\path\to\local\directory");
}
```

### Custom Download Handling

```csharp
using WithoutGit;

using (var cloner = new Clone("your_github_token"))
{
    // Enable custom download handling
    cloner.CustomDownload = true;
    
    // Handle download events
    cloner.DownloadFileEvent += (sender, args) => 
    {
        Console.WriteLine($"Downloading {args.DownloadUrl} to {args.SavePath}");
        // Custom download logic here
    };
    
    // Clone with custom download handling
    await cloner.CloneRepositoryAsync("owner", "repository", @"C:\path\to\local\directory");
}
```

## License

MIT

## Notes

- This library uses the GitHub REST API, which has rate limits. Using a GitHub token is recommended to increase these limits.
- For large repositories, the `CloneRepositoryFastAsync` method is significantly faster.
- Mirror mode ensures that your local directory exactly matches the remote repository, removing any files that don't exist remotely. 
