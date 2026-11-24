namespace MyStore.Models;

/// <summary>
/// Creates a company with no Stripe involvement at all -- for internal test/demo accounts,
/// not reachable through the public sign-up flow. See AdminFunctions.CreateTestCompany.
/// </summary>
public class CreateTestCompanyRequest
{
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>
    /// Grants the Lot Scanner add-on immediately (no Stripe charge -- see AdminService).
    /// </summary>
    public bool GrantLotScannerAddOn { get; set; } = true;
}

public class CreateTestCompanyResponse
{
    public int CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
