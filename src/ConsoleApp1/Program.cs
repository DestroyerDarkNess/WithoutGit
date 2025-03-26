using System.Diagnostics;
using WithoutGit;

// Create a new instance (optionally with a GitHub token)
// Generate a token on GitHub Settings > Developer Settings > Personal Access Tokens
var Token = "";
using (var cloner = new Clone(Token))
{
    cloner.CloningStarted += (sender, args) => Console.WriteLine("Cloning started");
    cloner.CloningProgress += (sender, args) =>
    {
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write($"Progress: {args.Percentage:F2}% ({args.ProcessedFiles}/{args.TotalFiles})");
    };
    cloner.CloningCompleted += (sender, args) => Console.WriteLine($"{Environment.NewLine} Cloning completed: {args.Success}");
    cloner.LogErrors += (sender, args) => Console.WriteLine($"Error: {args.ErrorMessage}");

    // this config is for the CloneRepositoryAsync method
    cloner.MultiThreading = true;
    cloner.MaxConcurrentThreads = 10000;
    cloner.PriorityMode = Clone.DownloadPriority.SmallFilesFirst;  // using with  CloneRepositoryAsync, File per File

    // All config
    cloner.ExcludePatterns = new System.Collections.Generic.List<string> { "*.md", "LICENSE" };

    var stopwatch = new Stopwatch();

    stopwatch.Start();

    try
    {

        // If there are more than 20 files, a token is required.
        /*await cloner.CloneRepositoryAsync("DestroyerDarkNess", "Xylon.Yara.Rules", @"WithoutGit\Xylon.Yara.Rules");*/ // Clone File per File , With cloner.MultiThreading = true Time :  00:01:47.7062470 | Without cloner.MultiThreading Time :  00:17:00.3423189

        // --------------------------------------------
        // this method is faster than the previous one, but it requires more memory. | No token required.
        await cloner.CloneRepositoryFastAsync("DestroyerDarkNess", "Xylon.Yara.Rules", @"WithoutGit\Xylon.Yara.Rules"); // Clone All , Time :  00:00:10.4130789 

    }
    catch (Exception ex)
    {
        Console.WriteLine($"Critical error: {ex.Message}");
    }

    stopwatch.Stop();

    Console.WriteLine($"Time: {stopwatch.Elapsed}"); // Time may vary depending on your internet speed.
    Console.ReadKey();
}
