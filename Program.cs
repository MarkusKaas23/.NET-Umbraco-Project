WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MyCustomUmbracoProject.Services.ChatHistoryService>();

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();


WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.MapControllers();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();

