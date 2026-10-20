using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;

namespace WindowsBar.Services
{
    public sealed class CountdownTimer : INotifyPropertyChanged
    {
        private TimeSpan _remaining;
        private DateTime _endsAt;
        private bool _isRunning;

        public CountdownTimer(TimeSpan length)
        {
            Length = length;
            _remaining = length;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public TimeSpan Length { get; }
        public TimeSpan Remaining => _remaining;
        public bool IsRunning => _isRunning;
        public string RemainingText => Format(_remaining);
        public string PauseGlyph => _isRunning ? "" : "";

        public void Start()
        {
            _endsAt = DateTime.Now + _remaining;
            _isRunning = true;
            Notify();
        }

        public void Pause()
        {
            Update();
            _isRunning = false;
            Notify();
        }

        /// <summary>Returns true the moment the timer reaches zero.</summary>
        internal bool Update()
        {
            if (!_isRunning)
                return false;
            _remaining = _endsAt - DateTime.Now;
            if (_remaining <= TimeSpan.Zero)
            {
                _remaining = TimeSpan.Zero;
                _isRunning = false;
                Notify();
                return true;
            }
            Notify();
            return false;
        }

        private void Notify()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PauseGlyph)));
        }

        public static string Format(TimeSpan t)
        {
            if (t < TimeSpan.Zero)
                t = TimeSpan.Zero;
            // Round up so a fresh 5:00 timer doesn't flash 4:59 straight away.
            t = TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds));
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";
        }
    }

    /// <summary>Several countdowns running side by side.</summary>
    public sealed class TimerService
    {
        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

        public TimerService()
        {
            _tick.Tick += (_, _) => OnTick();
        }

        public ObservableCollection<CountdownTimer> Timers { get; } = new();

        /// <summary>Raised a few times a second while any timer runs.</summary>
        public event Action? Ticked;
        public event Action<CountdownTimer>? Finished;

        /// <summary>The running timer that ends first, for the collapsed pill.</summary>
        public CountdownTimer? Soonest => Timers.Where(t => t.IsRunning).OrderBy(t => t.Remaining).FirstOrDefault();

        public void Add(TimeSpan length)
        {
            if (length <= TimeSpan.Zero)
                return;
            var timer = new CountdownTimer(length);
            Timers.Add(timer);
            timer.Start();
            _tick.Start();
            Ticked?.Invoke();
        }

        public void Toggle(CountdownTimer timer)
        {
            if (timer.IsRunning)
                timer.Pause();
            else
                timer.Start();
            _tick.Start();
            Ticked?.Invoke();
        }

        public void Remove(CountdownTimer timer)
        {
            Timers.Remove(timer);
            Ticked?.Invoke();
        }

        private void OnTick()
        {
            foreach (var timer in Timers.ToList())
            {
                if (timer.Update())
                {
                    Timers.Remove(timer);
                    Finished?.Invoke(timer);
                }
            }
            if (!Timers.Any(t => t.IsRunning))
                _tick.Stop();
            Ticked?.Invoke();
        }
    }
}
