using Dapper;
using Npgsql;

namespace MyStore.Repositories;

/// <summary>
/// Opens an Npgsql connection and stamps it with the caller's tenant (company) id via the
/// <c>app.current_company_id</c> session setting. Postgres Row-Level Security policies read this
/// value (<c>current_setting('app.current_company_id')</c>) to filter every statement to the
/// tenant, so a query that forgets a <c>company_id</c> predicate still cannot return another
/// tenant's rows.
///
/// The setting is applied immediately after open, before any query runs, and is session-scoped
/// (<c>set_config(..., is_local =&gt; false)</c>). Every code path that touches an RLS-protected
/// table opens its connection through this helper, so the value is always (re)set for the current
/// tenant before use; a stale value left on a pooled connection is overwritten before any read.
/// If a path ever failed to set it, the RLS policy treats the unset value as "no tenant" and
/// returns zero rows (fail-closed) rather than leaking.
/// </summary>
public static class TenantConnection
{
    /// <summary>
    /// Creates and opens a connection scoped to <paramref name="companyId"/>. Callers own the
    /// returned connection and should dispose it (e.g. <c>await using</c>).
    /// </summary>
    public static async Task<NpgsqlConnection> OpenAsync(string connectionString, int companyId)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "SELECT set_config('app.current_company_id', @company_id, false)",
            new { company_id = companyId.ToString() });
        return connection;
    }
}
