using System.ComponentModel.DataAnnotations;

namespace FinanceApp.Core.Models;

public class Industry
{
    public int Id { get; set; }
    public int SectorId { get; set; }
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(200)]
    public string NormalizedName { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Sector Sector { get; set; } = null!;
    public ICollection<Stock> Stocks { get; set; } = new List<Stock>();
}
