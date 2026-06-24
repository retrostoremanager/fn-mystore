using Xunit;

namespace MyStore.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that only runs when <c>RUN_DB_INTEGRATION_TESTS</c> is set in the
/// environment. The dedicated integration-tests workflow sets it (alongside an ephemeral Postgres
/// and <c>MYSTORE_TEST_DB</c>); ordinary unit-test runs leave it unset, so these data-layer tests
/// are skipped there — keeping the fast per-commit gate DB-free.
/// </summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("RUN_DB_INTEGRATION_TESTS")))
        {
            Skip = "DB integration test — set RUN_DB_INTEGRATION_TESTS and MYSTORE_TEST_DB to run (see integration-tests.yml).";
        }
    }
}
