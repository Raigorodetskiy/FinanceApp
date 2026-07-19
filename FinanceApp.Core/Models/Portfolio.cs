namespace FinanceApp.Core.Models;

public class Portfolio
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public User User { get; set; } = null!;
    public List<PortfolioItem> Items { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
}
