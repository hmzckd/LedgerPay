using System.Diagnostics;
using Npgsql;

namespace LedgerPay.Api.Data;

public static class QueryTracing
{
    public static void OnlyInsideATrace(NpgsqlTracingOptionsBuilder tracing) =>
        tracing.ConfigureCommandFilter(_ => Activity.Current is not null);
}
