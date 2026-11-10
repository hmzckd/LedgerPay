using LedgerPay.Risk.Checks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddOptions<RiskRules>()
    .BindConfiguration(RiskRules.Section)
    .Validate(rules => rules.MaxAmount > 0, "Risk:MaxAmount must be greater than zero.")
    .ValidateOnStart();

var app = builder.Build();

app.MapGrpcService<RiskCheckService>();

app.Run();
