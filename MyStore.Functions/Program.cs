using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyStore.Functions.Middleware;
using MyStore.Functions.Services;
using MyStore.Repositories;
using MyStore.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.UseMiddleware<CorsMiddleware>();
        builder.UseMiddleware<JwtAuthenticationMiddleware>();
        builder.UseMiddleware<CompanyAccessMiddleware>();
        builder.UseMiddleware<RbacMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<CorsMiddleware>();
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var connectionString = context.Configuration["ConnectionStrings__DefaultConnection"];
        var jwtSecretKey = context.Configuration["JwtAuthentication__SecretKey"]
            ?? context.Configuration["JwtSecret"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var startupLogger = services.BuildServiceProvider()
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Startup");
            startupLogger.LogWarning(
                "ConnectionStrings__DefaultConnection is not configured. " +
                "All endpoints that touch the database will fail. " +
                "Add this setting to Azure Function App configuration (or local.settings.json for local dev). " +
                "Verify the password in the connection string matches the current database user password.");
        }

        if (string.IsNullOrWhiteSpace(jwtSecretKey))
        {
            var startupLogger = services.BuildServiceProvider()
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Startup");
            startupLogger.LogWarning(
                "JwtAuthentication__SecretKey is not configured. " +
                "All requests to protected endpoints will be rejected. " +
                "Add this setting to Azure Function App configuration (or local.settings.json for local dev). " +
                "See local.settings.example.json for the required key name.");
        }

        // Stripe configuration
        services.Configure<StripeOptions>(context.Configuration.GetSection(StripeOptions.SectionName));
        var stripeSecretKey = context.Configuration["Stripe:SecretKey"];
        if (!string.IsNullOrWhiteSpace(stripeSecretKey))
        {
            Stripe.StripeConfiguration.ApiKey = stripeSecretKey;
        }
        
        // Register repositories
        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ISalesRepository, SalesRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IConsignmentRepository, ConsignmentRepository>();
        
        // Register services
        services.AddHttpClient<IIgdbService, IgdbService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ITrialConversionService, TrialConversionService>();
        services.AddScoped<ITrialSuspensionService, TrialSuspensionService>();
        services.AddScoped<ISubscriptionChangeService, SubscriptionChangeService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<IConsignmentService, ConsignmentService>();
        services.AddScoped<ITradeInRepository, TradeInRepository>();
        services.AddScoped<ITradeInService, TradeInService>();
        services.AddScoped<ILoyaltyRepository, LoyaltyRepository>();
        services.AddScoped<ILoyaltyService, LoyaltyService>();
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<LogoStorageService>();
        services.AddScoped<Stripe.InvoiceService>();
        services.AddScoped<Stripe.SubscriptionService>();
        services.AddScoped<Stripe.ProductService>();
    })
    .Build();

host.Run();

