using Xunit;

// Win32 shell-icon extraction (SHGetFileInfo + GDI) is not safe to run concurrently
// across test classes — parallel xUnit threads contend and intermittently return null.
// The suite is tiny, so disable parallelization for deterministic results.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
