using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Awagaman_ERP.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class WinAppDriverSmokeTests
    {
        private WinAppDriverClient _driver;

        [SetUp]
        public void SetUp()
        {
            StopExistingAppInstances();
            _driver = new WinAppDriverClient(GetAppExePath());

            WaitFor(() => _driver.TryFindElement("accessibility id", "TabDashboard", out _), TimeSpan.FromSeconds(20),
                "Main window did not load dashboard controls in time.");
        }

        [TearDown]
        public void TearDown()
        {
            _driver?.Dispose();
            _driver = null;
        }

        [Test]
        public void App_Launches_On_Dashboard()
        {
            Assert.That(_driver.GetWindowTitle(), Does.Contain("Awagaman"));
            Assert.That(_driver.TryFindElement("accessibility id", "TabDashboard", out _), Is.True,
                "Dashboard tab/button not found.");
            Assert.That(_driver.TryFindElement("accessibility id", "TabCBSLedger", out _), Is.True,
                "CBS tab/button not found.");
        }

        [Test]
        public void Can_Open_Challan_Form()
        {
            var challanTab = _driver.FindElement("accessibility id", "TabDeliveryChallans");
            _driver.Click(challanTab);

            var challanButton = _driver.FindElement("name", "+ Create Delivery Challan");
            _driver.Click(challanButton);

            WaitFor(() => _driver.TryFindElement("accessibility id", "ChallanNoBox", out var challanNoBox) && _driver.IsDisplayed(challanNoBox),
                TimeSpan.FromSeconds(15), "Challan form did not open.");
        }

        [Test]
        public void Can_Open_LR_Form()
        {
            var lrTab = _driver.FindElement("accessibility id", "TabLRLedger");
            _driver.Click(lrTab);

            var lrButton = _driver.FindElement("name", "+ Create LR");
            _driver.Click(lrButton);

            WaitFor(() => _driver.TryFindElement("accessibility id", "ConsignorNameBox", out var consignorNameBox) && _driver.IsDisplayed(consignorNameBox),
                TimeSpan.FromSeconds(15), "LR form did not open.");
        }

        [Test]
        public void Can_Open_Bill_Form()
        {
            var billTab = _driver.FindElement("accessibility id", "TabBillLedger");
            _driver.Click(billTab);

            var billButton = _driver.FindElement("name", "+ Generate Bill");
            _driver.Click(billButton);

            WaitFor(() => _driver.TryFindElement("accessibility id", "BillPartyBox", out var billPartyBox) && _driver.IsDisplayed(billPartyBox),
                TimeSpan.FromSeconds(15), "Bill form did not open.");
        }

        [Test]
        public void Can_Open_Cbs_Ledger_And_Add_Account_Dialog()
        {
            var cbsTab = _driver.FindElement("accessibility id", "TabCBSLedger");
            _driver.Click(cbsTab);

            WaitFor(() => _driver.TryFindElement("accessibility id", "CBSSearchBox", out var searchBox) && _driver.IsDisplayed(searchBox),
                TimeSpan.FromSeconds(15), "CBS ledger did not open.");

            var addAccountButton = _driver.FindElement("name", "+ Account");
            _driver.Click(addAccountButton);

            WaitFor(() => _driver.TryFindElement("accessibility id", "CBSAccountNameBox", out var accountBox) && _driver.IsDisplayed(accountBox),
                TimeSpan.FromSeconds(15), "Add Account dialog did not open.");
        }

        [Test]
        public void Can_Open_Cbs_Add_Entry_Dialog()
        {
            var cbsTab = _driver.FindElement("accessibility id", "TabCBSLedger");
            _driver.Click(cbsTab);

            WaitFor(() => _driver.TryFindElement("accessibility id", "CBSSearchBox", out var searchBox) && _driver.IsDisplayed(searchBox),
                TimeSpan.FromSeconds(15), "CBS ledger did not open.");

            var addEntryButton = _driver.FindElement("name", "+ Entry");
            _driver.Click(addEntryButton);

            WaitFor(() => _driver.TryFindElement("accessibility id", "CBSAddEntryAccountBox", out var accountBox) && _driver.IsDisplayed(accountBox),
                TimeSpan.FromSeconds(15), "Add CBS Entry dialog did not open.");
        }

        [Test]
        public void Can_Open_Cbs_Summary_Window()
        {
            var cbsTab = _driver.FindElement("accessibility id", "TabCBSLedger");
            _driver.Click(cbsTab);

            WaitFor(() => _driver.TryFindElement("accessibility id", "CBSSearchBox", out var searchBox) && _driver.IsDisplayed(searchBox),
                TimeSpan.FromSeconds(15), "CBS ledger did not open.");

            var summaryButton = _driver.FindElement("name", "Summary");
            _driver.Click(summaryButton);

            WaitFor(() => _driver.TryFindElement("accessibility id", "CBSAccountList", out var accountList) && _driver.IsDisplayed(accountList),
                TimeSpan.FromSeconds(15), "CBS summary window did not open.");
        }

        [Test]
        public void Can_Open_Cbs_And_Return_To_Dashboard()
        {
            var cbsButton = _driver.FindElement("accessibility id", "TabCBSLedger");
            _driver.Click(cbsButton);

            WaitFor(() => _driver.TryFindElement("accessibility id", "CBSSearchBox", out var searchBox) && _driver.IsDisplayed(searchBox),
                TimeSpan.FromSeconds(15), "CBS ledger did not open.");

            var dashboardButton = _driver.FindElement("accessibility id", "TabDashboard");
            _driver.Click(dashboardButton);

            WaitFor(() => !_driver.TryFindElement("accessibility id", "CBSSearchBox", out var searchBox) || !_driver.IsDisplayed(searchBox),
                TimeSpan.FromSeconds(15), "Dashboard did not reopen.");
        }

        private static string GetAppExePath()
        {
            var testDir = TestContext.CurrentContext.TestDirectory;
            var exePath = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "Awagaman ERP", "bin", "Debug", "Awagaman ERP.exe"));
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("Debug executable not found. Build Awagaman ERP in Debug first.", exePath);
            }

            return exePath;
        }

        private static void StopExistingAppInstances()
        {
            foreach (var process in Process.GetProcessesByName("Awagaman ERP"))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch
                {
                    // Best effort only.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void WaitFor(Func<bool> condition, TimeSpan timeout, string failureMessage)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (condition())
                {
                    return;
                }

                Thread.Sleep(500);
            }

            Assert.Fail(failureMessage);
        }
    }
}
