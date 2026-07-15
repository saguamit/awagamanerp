namespace Awagaman.Api.Models;

public sealed class CreateLrFromChallanRequest
{
    public int ChallanId { get; set; }
    public LREntry Entry { get; set; } = new();
}
