namespace FinanceApp.Core.Models;

public sealed class CatalogMaintenanceLease
{
    public int Id { get; set; }
    public string LeaseName { get; set; } = string.Empty;
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
