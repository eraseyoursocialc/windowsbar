using System;
using Forms = System.Windows.Forms;

namespace WindowsBar.Services
{
    /// <summary>Notices when the charger is plugged in or pulled out.</summary>
    public sealed class BatteryWatcher
    {
        private bool? _wasPluggedIn;

        /// <summary>Percent (0-100) and whether the charger is now plugged in.</summary>
        public event Action<int, bool>? PowerChanged;

        public bool HasBattery { get; private set; }
        public int Percent { get; private set; }
        public bool PluggedIn { get; private set; }

        public void Poll()
        {
            var status = Forms.SystemInformation.PowerStatus;
            HasBattery = !status.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery) &&
                         status.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown;
            if (!HasBattery)
                return;

            Percent = (int)Math.Round(status.BatteryLifePercent * 100);
            PluggedIn = status.PowerLineStatus == Forms.PowerLineStatus.Online;

            if (_wasPluggedIn.HasValue && _wasPluggedIn != PluggedIn)
                PowerChanged?.Invoke(Percent, PluggedIn);
            _wasPluggedIn = PluggedIn;
        }
    }
}
