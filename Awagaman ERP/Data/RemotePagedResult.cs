using System.Collections.Generic;

namespace Awagaman_ERP.Data
{
    public sealed class RemotePagedResult<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
    }
}
