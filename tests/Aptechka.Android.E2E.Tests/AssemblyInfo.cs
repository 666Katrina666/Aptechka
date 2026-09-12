using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
[assembly: TestCaseOrderer(
    "Aptechka.Android.E2E.Tests.FullyQualifiedNameTestCaseOrderer",
    "Aptechka.Android.E2E.Tests")]
