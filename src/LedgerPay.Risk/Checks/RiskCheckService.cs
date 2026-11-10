using System.Globalization;
using Grpc.Core;
using LedgerPay.Risk.V1;
using Microsoft.Extensions.Options;

namespace LedgerPay.Risk.Checks;

internal sealed class RiskCheckService(IOptions<RiskRules> rules, ILogger<RiskCheckService> logger)
    : RiskCheck.RiskCheckBase
{
    public override Task<CheckTransferResponse> CheckTransfer(
        CheckTransferRequest request, ServerCallContext context)
    {
        var maxAmount = rules.Value.MaxAmount;
        if (request.Amount > maxAmount)
        {
            logger.LogInformation(
                "Rejected {Amount} from {FromAccountId} to {ToAccountId}.",
                request.Amount, request.FromAccountId, request.ToAccountId);
            return Task.FromResult(new CheckTransferResponse
            {
                Decision = Decision.Rejected,
                Reason = string.Create(
                    CultureInfo.InvariantCulture, $"The amount is over the {maxAmount / 100m:0.00} TRY limit."),
            });
        }

        logger.LogInformation(
            "Approved {Amount} from {FromAccountId} to {ToAccountId}.",
            request.Amount, request.FromAccountId, request.ToAccountId);
        return Task.FromResult(new CheckTransferResponse { Decision = Decision.Approved });
    }
}
