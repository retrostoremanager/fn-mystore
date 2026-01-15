using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyStore.Repositories;
using MyStore.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Register repositories
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ISalesRepository, SalesRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        
        // Register services
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ISalesService, SalesService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ICompanyService, CompanyService>();
    })
    .Build();

host.Run();

