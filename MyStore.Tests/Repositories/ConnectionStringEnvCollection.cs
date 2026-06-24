using Xunit;

namespace MyStore.Tests.Repositories;

// Repository tests that toggle the process-global ConnectionStrings__DefaultConnection /
// PostgresConnectionString environment variables must not run in parallel with one another:
// one class clearing the var (to assert the constructor throws) races another class setting it,
// which intermittently fails Constructor_NoConnectionStringEnvVar_Throws. Placing all of them in
// a single collection makes xUnit run them serially relative to each other, removing the race
// while the rest of the suite still runs in parallel.
[CollectionDefinition("ConnectionStringEnv")]
public class ConnectionStringEnvCollection
{
}
