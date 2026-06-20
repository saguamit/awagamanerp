namespace Awagaman.Api.Models;

public class VehicleEntry
{
    public int Id { get; set; }
    public int Sr { get; set; }
    public string? VehicleNumber { get; set; }
    public string? OwnerName { get; set; }
    public string? PANNumber { get; set; }
    public string? EngineNumber { get; set; }
    public string? ChassisNumber { get; set; }
    public string? VehicleType { get; set; }
}
