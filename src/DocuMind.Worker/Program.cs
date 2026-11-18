using DocuMind.Application;
using DocuMind.Application.Abstractions;
using DocuMind.Infrastructure;
using DocuMind.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddApplication();
builder.AddInfrastructure();

// The worker has no HTTP user; it acts as the system.
builder.Services.AddSingleton<ICurrentUser, SystemUser>();
builder.Services.Configure<IngestionWorkerOptions>(builder.Configuration.GetSection(IngestionWorkerOptions.SectionName));
builder.Services.AddHostedService<IngestionWorker>();

builder.Build().Run();
