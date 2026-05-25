using System.Collections.Generic;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public interface ITrackingRepository
    {
        List<TrackingEntry> GetAll();
        Dictionary<int, string> GetLatestReportForAll();
        List<ReportingTrackEntry> GetReportingTracks(int trackingEntryId);
        void Upsert(TrackingEntry entry);
        void AddReportingTrack(ReportingTrackEntry entry);
        void Delete(TrackingEntry entry);
    }
}
