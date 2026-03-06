namespace MyStore.Models;

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? CompanyId { get; set; }
    public bool IsSystemRole => CompanyId == null;
    public List<string> Permissions { get; set; } = new();
}
