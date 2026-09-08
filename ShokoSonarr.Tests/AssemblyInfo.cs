using Xunit;

// ScanCacheStore opens a LiteDB file per instance. Running several instances concurrently
// (even on distinct files) races LiteDB's process-wide static mapper state and loses writes,
// making ScanCacheStoreTests flaky under xUnit's default parallel collections.
// ponytail: assembly-wide serialization, switch to a shared [Collection] for the LiteDB tests if suite runtime matters
[assembly: CollectionBehavior(DisableTestParallelization = true)]
