using System.Collections.Generic;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public interface IChallanRepository
    {
        List<ChallanEntry> GetAll();
        List<ChallanEntry> GetPage(int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true);
        List<ChallanEntry> Search(string searchFilter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true);
        List<ChallanEntry> SearchAdvanced(string challanNo, string lrNo, string from, string to, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true);
        int GetTotalCount();
        int GetTotalCount(string searchFilter);
        int GetTotalCountAdvanced(string challanNo, string lrNo, string from, string to);
        int GetMaxSr();
        ChallanEntry FindByChallanNumber(string challanNumber);
        HashSet<int> GetChallanIdsWithComments();
        void Upsert(ChallanEntry entry);
        void Delete(ChallanEntry entry);
    }
}
