using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Aptechka.Android.E2E.Tests;

[CollectionDefinition("AndroidE2E", DisableParallelization = true)]
public sealed class AndroidE2ECollection : ICollectionFixture<AndroidSession>
{
}

public sealed class FullyQualifiedNameTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases
            .OrderBy(test => test.TestMethod.TestClass.Class.Name, StringComparer.Ordinal)
            .ThenBy(test => test.TestMethod.Method.Name, StringComparer.Ordinal);
}
