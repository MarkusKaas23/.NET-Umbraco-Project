WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MyCustomUmbracoProject.Services.ChatHistoryService>();

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();


WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
        u.AppBuilder.UseMiddleware<MyCustomUmbracoProject.Middleware.SitemapMiddleware>();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();

