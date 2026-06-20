namespace Awagaman.Api.Models;

public class CBSAccountEntry
{
    public int Id { get; set; }
    public int Sr { get; set; }
    public string? AccountName { get; set; }
    public bool IsActive { get; set; } = true;
}
