using System.Collections.Generic;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public interface ILRRepository
    {
        List<LREntry> GetAll();
        List<LREntry> GetPage(int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true);
        List<LREntry> Search(string searchFilter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true);
        int GetTotalCount();
        int GetTotalCount(string searchFilter);
        decimal GetTotalFreight(string searchFilter = "");
        decimal GetTotalBalance(string searchFilter = "");
        int GetMaxSr();
        LREntry GetById(int id);
        List<LREntry> GetByLrNumbers(IEnumerable<string> lrNumbers);
        List<LREntry> GetByBillNo(string billNo);
        void Upsert(LREntry entry);
        void Delete(LREntry entry);
    }
}
