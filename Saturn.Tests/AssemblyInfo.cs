using Xunit;

// Disable test parallelization to avoid race conditions on WSL
[assembly: CollectionBehavior(DisableTestParallelization = true)]