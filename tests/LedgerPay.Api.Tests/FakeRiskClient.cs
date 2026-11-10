using System.Collections.Concurrent;
using Grpc.Core;
using LedgerPay.Risk.V1;

namespace LedgerPay.Api.Tests;

public class FakeRiskClient : RiskCheck.RiskCheckClient
{
    private static readonly CheckTransferResponse Approved = new() { Decision = Decision.Approved };

    private readonly ConcurrentDictionary<string, Func<CheckTransferRequest, CheckTransferResponse>> _answers = new();
    private readonly ConcurrentQueue<RiskCall> _calls = new();

    public void AnswerFor(Guid toAccountId, Func<CheckTransferRequest, CheckTransferResponse> answer) =>
        _answers[toAccountId.ToString()] = answer;

    public void ApproveFor(Guid toAccountId) => AnswerFor(toAccountId, _ => Approved);

    public void RejectFor(Guid toAccountId, string reason) =>
        AnswerFor(toAccountId, _ => new CheckTransferResponse { Decision = Decision.Rejected, Reason = reason });

    public void FailFor(Guid toAccountId, StatusCode status) =>
        AnswerFor(toAccountId, _ => throw new RpcException(new Status(status, "The fake risk service failed.")));

    public List<RiskCall> CallsFor(Guid toAccountId) =>
        _calls.Where(call => call.Request.ToAccountId == toAccountId.ToString()).ToList();

    public override AsyncUnaryCall<CheckTransferResponse> CheckTransferAsync(
        CheckTransferRequest request, CallOptions options)
    {
        _calls.Enqueue(new RiskCall(request, options.Deadline));

        Task<CheckTransferResponse> response;
        try
        {
            var answer = _answers.TryGetValue(request.ToAccountId, out var custom) ? custom : _ => Approved;
            response = Task.FromResult(answer(request));
        }
        catch (RpcException e)
        {
            response = Task.FromException<CheckTransferResponse>(e);
        }

        return new AsyncUnaryCall<CheckTransferResponse>(
            response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    }
}

public record RiskCall(CheckTransferRequest Request, DateTime? Deadline);
