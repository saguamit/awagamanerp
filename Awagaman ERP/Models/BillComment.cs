using System;

namespace Awagaman_ERP.Models
{
    public class BillComment
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public string Comment { get; set; }
        public DateTime CreatedAt { get; set; }
        public string DisplayText => $"{CreatedAt:dd-MMM-yyyy HH:mm} - {Comment}";
    }
}
