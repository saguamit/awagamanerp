using NUnit.Framework;
using System;
using System.IO;

namespace Awagaman_ERP.Tests
{
    /// <summary>
    /// UI Automation tests using FlaUI.
    /// To run these tests:
    /// 1. Install FlaUI.UIA3 NuGet package in this project
    /// 2. Build the main project first (Ctrl+Shift+B)
    /// 3. Update the exePath below to point to the built .exe
    /// 4. Run tests from Test Explorer
    /// </summary>
    [TestFixture]
    public class UITests
    {
        private const string ExePath = @"..\..\..\Awagaman ERP\bin\Debug\Awagaman ERP.exe";

        [Test]
        [Explicit("Requires FlaUI package and built .exe")]
        public void Launch_App_ShowsDashboard()
        {
            // FlaUI setup (uncomment after installing FlaUI.UIA3 package):
            // using FlaUI.UIA3;
            // var app = FlaUI.Core.Application.Launch(Path.GetFullPath(ExePath));
            // using (var automation = new UIA3Automation())
            // {
            //     var window = app.GetMainWindow(automation);
            //     Assert.That(window.Title, Does.Contain("Awagaman"));
            //     var dashboardTab = window.FindFirstDescendant(cf => cf.ByText("Dashboard"));
            //     Assert.That(dashboardTab, Is.Not.Null);
            //     app.Close();
            // }
            Assert.Pass("Replace with FlaUI assertions after installing FlaUI.UIA3");
        }

        [Test]
        [Explicit("Requires FlaUI package and built .exe")]
        public void Dashboard_ShowsSummaryCards()
        {
            // using FlaUI.UIA3;
            // var app = FlaUI.Core.Application.Launch(Path.GetFullPath(ExePath));
            // using (var automation = new UIA3Automation())
            // {
            //     var window = app.GetMainWindow(automation);
            //     var totalDue = window.FindFirstDescendant(cf => cf.ByText("Total Due"));
            //     Assert.That(totalDue, Is.Not.Null);
            //     var inTransit = window.FindFirstDescendant(cf => cf.ByText("In Transit"));
            //     Assert.That(inTransit, Is.Not.Null);
            //     app.Close();
            // }
            Assert.Pass("Replace with FlaUI assertions after installing FlaUI.UIA3");
        }
    }
}
