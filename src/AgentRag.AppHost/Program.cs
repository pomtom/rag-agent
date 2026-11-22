// Aspire AppHost: the whole application on your machine, wired together.
//
//   AgentRag (SQL Server)   an EXISTING database - its connection string is in this project's appsettings.json
//     |
//   api                     gets ConnectionStrings__AgentRag
//     |
//   web                     gets the address of "api" (service discovery), so it can call https+http://api/...
//
// Every arrow is just CONFIGURATION handed to the project as environment variables.
//
// RUN:  dotnet run --project src/AgentRag.AppHost      then open the dashboard link printed in the console.

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Not a container: AddConnectionString only reads ConnectionStrings:AgentRag from configuration.
IResourceBuilder<IResourceWithConnectionString> database = builder.AddConnectionString("AgentRag");

IResourceBuilder<ProjectResource> api = builder.AddProject<Projects.AgentRag_Api>("api")
    .WithReference(database);

builder.AddProject<Projects.AgentRag_Web>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
