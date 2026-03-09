using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyStore.Functions.Middleware;
using MyStore.Functions.Services;
using MyStore.Repositories;
using MyStore.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.UseMiddleware<JwtAuthenticationMiddleware>();
        builder.UseMiddleware<CompanyAccessMiddleware>();
        builder.UseMiddleware<RbacMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

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
        services.AddScoped<LogoStorageService>();
    })
    .Build();

host.Run();

