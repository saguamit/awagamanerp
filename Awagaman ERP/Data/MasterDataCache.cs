using System;
using System.Collections.Generic;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    internal static class MasterDataCache
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

        private static List<PartyEntry> _parties;
        private static DateTime _partiesLoadedAt;
        private static List<VehicleEntry> _vehicles;
        private static DateTime _vehiclesLoadedAt;
        private static List<CBSAccountEntry> _cbsAccounts;
        private static DateTime _cbsAccountsLoadedAt;

        public static List<PartyEntry> GetParties(Func<List<PartyEntry>> loader)
        {
            lock (Sync)
            {
                if (_parties == null || IsExpired(_partiesLoadedAt))
                {
                    _parties = loader != null ? loader() : new List<PartyEntry>();
                    _partiesLoadedAt = DateTime.UtcNow;
                }
                return new List<PartyEntry>(_parties);
            }
        }

        public static List<VehicleEntry> GetVehicles(Func<List<VehicleEntry>> loader)
        {
            lock (Sync)
            {
                if (_vehicles == null || IsExpired(_vehiclesLoadedAt))
                {
                    _vehicles = loader != null ? loader() : new List<VehicleEntry>();
                    _vehiclesLoadedAt = DateTime.UtcNow;
                }
                return new List<VehicleEntry>(_vehicles);
            }
        }

        public static List<CBSAccountEntry> GetCBSAccounts(Func<List<CBSAccountEntry>> loader)
        {
            lock (Sync)
            {
                if (_cbsAccounts == null || IsExpired(_cbsAccountsLoadedAt))
                {
                    _cbsAccounts = loader != null ? loader() : new List<CBSAccountEntry>();
                    _cbsAccountsLoadedAt = DateTime.UtcNow;
                }
                return new List<CBSAccountEntry>(_cbsAccounts);
            }
        }

        public static void InvalidateParties()
        {
            lock (Sync)
            {
                _parties = null;
                _partiesLoadedAt = DateTime.MinValue;
            }
        }

        public static void InvalidateVehicles()
        {
            lock (Sync)
            {
                _vehicles = null;
                _vehiclesLoadedAt = DateTime.MinValue;
            }
        }

        public static void InvalidateCBSAccounts()
        {
            lock (Sync)
            {
                _cbsAccounts = null;
                _cbsAccountsLoadedAt = DateTime.MinValue;
            }
        }

        public static void WarmUpRemote()
        {
            if (!BackendSettings.UseRemoteApi) return;

            try { GetParties(() => RemoteApiClient.GetList<PartyEntry>("api/parties")); } catch { }
            try { GetVehicles(() => RemoteApiClient.GetList<VehicleEntry>("api/vehicles")); } catch { }
            try { new CBSAccountRepository().GetAll(); } catch { }
        }

        private static bool IsExpired(DateTime loadedAt)
        {
            return loadedAt == DateTime.MinValue || DateTime.UtcNow - loadedAt > Ttl;
        }
    }
}
