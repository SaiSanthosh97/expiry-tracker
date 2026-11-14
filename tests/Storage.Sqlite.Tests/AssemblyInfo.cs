// The test classes reset SQLite's shared connection pool when they finish; running them at the same time
// lets one class close connections the other is still using (the test process then crashes).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
