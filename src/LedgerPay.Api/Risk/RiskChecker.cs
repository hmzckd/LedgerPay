using Grpc.Core;
using LedgerPay.Risk.V1;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LedgerPay.Api.Risk;

public class RiskChecker(RiskCheck.RiskCheckClient client, ILogger<RiskChecker> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public async Task<ProblemHttpResult?> CheckAsync(
        Guid fromAccountId, Guid toAccountId, long amount, CancellationToken ct)
    {
        var request = new CheckTransferRequest
        {
            FromAccountId = fromAccountId.ToString(),
            ToAccountId = toAccountId.ToString(),
            Amount = amount,
        };

        CheckTransferResponse response;
        try
        {
            response = await client.CheckTransferAsync(
                request, deadline: DateTime.UtcNow.Add(Timeout), cancellationToken: ct);
        }
        catch (RpcException e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("The risk check failed with {StatusCode}: {Detail}", e.StatusCode, e.Status.Detail);
            return Problems.RiskUnavailable();
        }

        if (response.Decision == Decision.Approved)
        {
            return null;
        }

        if (response.Decision == Decision.Rejected)
        {
            return Problems.RiskRejected(response.Reason);
        }

        logger.LogWarning("The risk check answered without a decision.");
        return Problems.RiskUnavailable();
    }
}
