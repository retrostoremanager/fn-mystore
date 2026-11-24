using MyStore.Models;

namespace MyStore.Services;

public interface IAdminService
{
    Task<CreateTestCompanyResponse> CreateTestCompanyAsync(CreateTestCompanyRequest request);
}
