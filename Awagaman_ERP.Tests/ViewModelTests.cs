using NUnit.Framework;
using Moq;
using System.Collections.Generic;
using Awagaman_ERP.Data;
using Awagaman_ERP.Models;
using Awagaman_ERP.ViewModels;

namespace Awagaman_ERP.Tests
{
    [TestFixture]
    public class ChallanViewModelTests
    {
        [Test]
        public void LoadData_LoadsEntriesFromRepository()
        {
            var mockRepo = new Mock<IChallanRepository>();
            var entries = new List<ChallanEntry>
            {
                new ChallanEntry { Id = 1, ChallanNumber = "CH001", Sr = 1 },
                new ChallanEntry { Id = 2, ChallanNumber = "CH002", Sr = 2 }
            };
            mockRepo.Setup(r => r.GetAll()).Returns(entries);
            mockRepo.Setup(r => r.GetTotalCount()).Returns(2);
            mockRepo.Setup(r => r.GetPage(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(entries);

            var vm = new ChallanViewModel(mockRepo.Object);

            Assert.That(vm.PagedEntries.Count, Is.EqualTo(2));
        }

        [Test]
        public void TotalDue_CalculatesCorrectly()
        {
            var mockRepo = new Mock<IChallanRepository>();
            mockRepo.Setup(r => r.GetAll()).Returns(new List<ChallanEntry>());
            mockRepo.Setup(r => r.GetTotalCount()).Returns(2);
            mockRepo.Setup(r => r.GetPage(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(new List<ChallanEntry>());

            var vm = new ChallanViewModel(mockRepo.Object);
            var entry1 = new ChallanEntry { LorryHire = 500m, LessTDS = 50m, AdvanceAmount = 100m };
            entry1.RecalculateBalance();
            vm.Entries.Add(entry1);
            var entry2 = new ChallanEntry { LorryHire = 300m, LessTDS = 30m, AdvanceAmount = 50m };
            entry2.RecalculateBalance();
            vm.Entries.Add(entry2);

            Assert.That(vm.TotalDue, Is.EqualTo(570m));
        }
    }

    [TestFixture]
    public class LRLedgerViewModelTests
    {
        [Test]
        public void LoadData_LoadsEntriesFromRepository()
        {
            var mockRepo = new Mock<ILRRepository>();
            var entries = new List<LREntry>
            {
                new LREntry { Id = 1, LRNo = "LR001", Sr = 1 },
                new LREntry { Id = 2, LRNo = "LR002", Sr = 2 },
                new LREntry { Id = 3, LRNo = "LR003", Sr = 3 }
            };
            mockRepo.Setup(r => r.GetAll()).Returns(entries);
            mockRepo.Setup(r => r.GetTotalCount()).Returns(3);
            mockRepo.Setup(r => r.GetPage(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(entries);

            var vm = new LRLedgerViewModel(mockRepo.Object);

            Assert.That(vm.PagedEntries.Count, Is.EqualTo(3));
        }

        [Test]
        public void AddEntry_IncreasesCount()
        {
            var mockRepo = new Mock<ILRRepository>();
            mockRepo.Setup(r => r.GetAll()).Returns(new List<LREntry>());
            mockRepo.Setup(r => r.GetTotalCount()).Returns(0);
            mockRepo.Setup(r => r.GetPage(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(new List<LREntry>());

            var vm = new LRLedgerViewModel(mockRepo.Object);
            vm.Entries.Add(new LREntry { LRNo = "LR001" });

            Assert.That(vm.Entries.Count, Is.EqualTo(1));
        }
    }

    [TestFixture]
    public class TrackingViewModelTests
    {
        [Test]
        public void LoadData_LoadsEntriesFromRepository()
        {
            var mockRepo = new Mock<ITrackingRepository>();
            mockRepo.Setup(r => r.GetAll()).Returns(new List<TrackingEntry>
            {
                new TrackingEntry { Id = 1, ChallanNo = "CH001", VehicleNo = "MH01AB1234" }
            });
            mockRepo.Setup(r => r.GetLatestReportForAll()).Returns(new Dictionary<int, string>());
            mockRepo.Setup(r => r.GetReportingTracks(It.IsAny<int>())).Returns(new List<ReportingTrackEntry>());

            var vm = new TrackingViewModel(mockRepo.Object);

            Assert.That(vm.Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void FilteredEntriesCount_UpdatesOnAdd()
        {
            var mockRepo = new Mock<ITrackingRepository>();
            mockRepo.Setup(r => r.GetAll()).Returns(new List<TrackingEntry>());
            mockRepo.Setup(r => r.GetLatestReportForAll()).Returns(new Dictionary<int, string>());
            mockRepo.Setup(r => r.GetReportingTracks(It.IsAny<int>())).Returns(new List<ReportingTrackEntry>());

            var vm = new TrackingViewModel(mockRepo.Object);
            vm.AddEntry(new TrackingEntry { ChallanNo = "CH001", VehicleNo = "MH01AB1234" });

            Assert.That(vm.FilteredEntriesCount, Is.EqualTo(1));
        }
    }

    [TestFixture]
    public class TrackingEntryModelTests
    {
        [Test]
        public void Status_NoDispatch_ReturnsPendingDispatch()
        {
            var entry = new TrackingEntry();
            Assert.That(entry.Status, Is.EqualTo("Pending Dispatch"));
        }

        [Test]
        public void Status_DispatchOnly_ReturnsInTransit()
        {
            var entry = new TrackingEntry();
            entry.DispatchDate = System.DateTime.Now;
            Assert.That(entry.Status, Is.EqualTo("In Transit"));
        }

        [Test]
        public void Status_Delivered_ReturnsDelivered()
        {
            var entry = new TrackingEntry();
            entry.DeliveredDate = System.DateTime.Now;
            Assert.That(entry.Status, Is.EqualTo("Delivered"));
        }
    }
}
