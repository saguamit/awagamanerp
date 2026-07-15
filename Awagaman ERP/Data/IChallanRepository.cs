using System.Collections.Generic;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public interface IChallanRepository
    {
        string LedgerMode { get; set; }
        List<ChallanEntry> GetAll();
        List<ChallanEntry> GetPage(int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true, bool useLhsDerived = false);
        List<ChallanEntry> Search(string searchFilter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true, bool useLhsDerived = false);
        List<ChallanEntry> SearchAdvanced(string challanNo, string lrNo, string from, string to, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true, bool useLhsDerived = false);
        int GetTotalCount();
        int GetTotalCount(string searchFilter);
        int GetTotalCountAdvanced(string challanNo, string lrNo, string from, string to);
        int GetDueCount(string searchFilter = "", bool useLhsDerived = false);
        int GetDueCountAdvanced(string challanNo, string lrNo, string from, string to, bool useLhsDerived = false);
        decimal GetTotalDue(string searchFilter = "", bool useLhsDerived = false);
        decimal GetTotalDueAdvanced(string challanNo, string lrNo, string from, string to, bool useLhsDerived = false);
        int GetMaxSr();
        List<ChallanEntry> GetPendingBookingItems(int limit = 0);
        RemotePagedResult<ChallanEntry> GetPendingBookingPage(int pageNumber, int pageSize, string search = "");
        List<ChallanEntry> GetByChallanNumbers(IEnumerable<string> challanNumbers);
        ChallanEntry FindById(int id);
        ChallanEntry FindByChallanNumber(string challanNumber);
        HashSet<int> GetChallanIdsWithComments();
        void Upsert(ChallanEntry entry);
        void Delete(ChallanEntry entry);
    }
}
