using Demo.Worker.Consumers;
using MassTransit;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, config) =>
    config.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddMassTransit(x =>
{
    // 註冊 Consumer
    x.AddConsumer<ItemCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // 自動為所有已註冊的 Consumer 設定 Queue
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
