using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Appointments;

namespace WindowsBar.Services
{
    public sealed record CalendarEvent(DateTimeOffset Start, string Title, bool AllDay);

    /// <summary>
    /// Reads events from the Windows calendar (the accounts set up in the Windows Calendar / Outlook app).
    /// If Windows doesn't allow it, the widget simply shows the dates.
    /// </summary>
    public sealed class CalendarService
    {
        private AppointmentStore? _store;
        private bool _unavailable;

        public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTime day)
        {
            if (_unavailable)
                return Array.Empty<CalendarEvent>();
            try
            {
                _store ??= await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
                var options = new FindAppointmentsOptions { MaxCount = 10 };
                options.FetchProperties.Add(AppointmentProperties.Subject);
                options.FetchProperties.Add(AppointmentProperties.StartTime);
                options.FetchProperties.Add(AppointmentProperties.AllDay);

                var found = await _store.FindAppointmentsAsync(day.Date, TimeSpan.FromDays(1), options);
                return found
                    .Select(a => new CalendarEvent(a.StartTime, a.Subject, a.AllDay))
                    .OrderBy(e => e.AllDay ? DateTimeOffset.MinValue : e.Start)
                    .ToList();
            }
            catch (Exception ex)
            {
                _unavailable = true;
                Log.Error("Windows calendar not available", ex);
                return Array.Empty<CalendarEvent>();
            }
        }
    }
}
