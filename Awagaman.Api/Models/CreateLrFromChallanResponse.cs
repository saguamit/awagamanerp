namespace Awagaman.Api.Models;

public sealed class CreateLrFromChallanResponse
{
    public LREntry Entry { get; set; } = new();
    public ChallanEntry? LinkedChallan { get; set; }
}
