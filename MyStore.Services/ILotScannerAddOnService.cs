using MyStore.Models;

namespace MyStore.Services;

public interface ILotScannerAddOnService
{
    Task<AddOnChangeResult> SubscribeAsync(int companyId);
    Task<AddOnChangeResult> CancelAsync(int companyId);
}
