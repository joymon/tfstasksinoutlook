﻿using System;
using System.Linq;
using System.Reactive.Linq;
using Microsoft.Office.Interop.Outlook;
using System.Text.RegularExpressions;

namespace TFSTasksInOutlook.Calendar
{
    class CalendarManager
    {
        #region Internal APIs        
        /// <summary>
        /// Creates the or updates work item in calendar.
        /// </summary>
        /// <param name="item">The TFS work item as <see cref="WorkItemInfo"/>.</param>
        /// <param name="useStartAndFinishDatesofWorkItemToCreateCalendarEntries">if set to <c>true</c> [use start and finish dates of work item to create calendar entries].</param>
        /// <remarks> The <paramref name="useStartAndFinishDatesofWorkItemToCreateCalendarEntries"/> only affect new items not the updates. </remarks>
        internal static void CreateOrUpdatesItemInCalendar(WorkItemInfo item,bool useStartAndFinishDatesofWorkItemToCreateCalendarEntries)
        {
            var expl = Globals.ThisAddIn.Application.ActiveExplorer();
            
            var calView = expl.CurrentView as CalendarView;
            
            if (calView == null || calView.ViewType != OlViewType.olCalendarView) return;
            if (expl.Selection.Count > 0)
            {
                _UpdateItemsInCalendar(item, expl);
            }
            else
            {
                _CreateNewItemsInCalendar(item, calView, expl,useStartAndFinishDatesofWorkItemToCreateCalendarEntries);
            }
        }
        #endregion
        #region Private helpers
        private static void _UpdateItemsInCalendar(WorkItemInfo item, Explorer explorer)
        {
            foreach (var selected in explorer.Selection)
            {
                if (!(selected is AppointmentItem)) continue;
                var appointment = selected as AppointmentItem;
                var rgx = new Regex(@"^(#\d+)");
                appointment.Subject =
                  rgx.IsMatch(appointment.Subject.Trim())
                    ? rgx.Replace(appointment.Subject, "#" + item.Id, 1)
                    : appointment.Subject.Trim().Length > 0
                      ? string.Format("#{0} {1}", item.Id, appointment.Subject)
                      : _WorkItemInfoToText(item);
                appointment.Save();
            }
        }

        private static void _AddAppointment(Items items, string subject, DateTime start, DateTime end)
        {
            var appointment = items.Add("IPM.Appointment") as AppointmentItem;
            if (appointment == null) return;
            appointment.Subject = subject;
            appointment.Start = start;
            appointment.End = end;
            appointment.Save();
        }

        private static string _WorkItemInfoToText(WorkItemInfo wi)
        {
            if (Properties.Settings.Default.ShowOnlyWorkItemIdInCalendar)
                return $"#{wi.Id}";
            else
                return $"#{wi.Id} {wi.Title}";
        }

        private static void _CreateNewItemsInCalendar(WorkItemInfo item, 
                                                        CalendarView calView, 
                                                        Explorer explorer,
                                                        bool useStartAndFinishDatesofWorkItemToCreateCalendarEntries)
        {
            DateTime dstart, dend;
            if (_canUseStartAndEndTimesFromWorkItem(item) && useStartAndFinishDatesofWorkItemToCreateCalendarEntries)
            {
                dstart = item.StartDate.Value;
                dend = item.FinishDate.Value;
            }
            else
            {
                dstart = calView.SelectedStartTime;
                dend = calView.SelectedEndTime;
            }

            if (_IsStartOrEndDates1YearAgo(dstart, dend))
                return;

            Folder folder = explorer.CurrentFolder as Folder;
            if (folder == null) return;

            // Check for multiday event
            // Add 8-hour event for each day 9:00 - 17:00
            if (dend - dstart >= TimeSpan.FromDays(1))
            {
                Observable.Generate(
                  dstart + TimeSpan.FromHours(9),
                  date => date <= dend,
                  date => date + TimeSpan.FromDays(1),
                  date => new { Start = date, End = date + TimeSpan.FromHours(8) })
                  .Subscribe(i =>
                    _AddAppointment(folder.Items,
                      _WorkItemInfoToText(item),
                      i.Start, i.End));

                return;
            }
            // Handle single day event differently - check for existing appointments
            // Criteria for any appointment, overlapping with [Start, End] interval
            var restrictCriteria = "[Start] <= '" + dend.ToString("g") + "'" +
                                   " AND [End] >= '" + dstart.ToString("g") + "'";


            // Filter items according to the criteria and add fake item to the end 
            // as an artifitial upper boundary
            Items items = folder.Items;
            items.IncludeRecurrences = true;
            items.Sort("[Start]", Type.Missing);
            items = items.Restrict(restrictCriteria);

            var appointments = items
              .Cast<AppointmentItem>()
              .Where(i => !i.AllDayEvent)
              .Select(i => new { i.Start, i.End })
              .ToList();
            appointments.Add(new { Start = dend, End = dend.AddHours(1) });

            appointments.ToObservable()
              .Where(i => i.End > dstart)
              .Subscribe(i =>
              {
                  if (dstart < i.Start)
                      _AddAppointment(folder.Items,
                  _WorkItemInfoToText(item),
                  dstart, i.Start);

                  dstart = i.End;
              });
        }

        private static bool _IsStartOrEndDates1YearAgo(DateTime dstart, DateTime dend)
        {
            DateTime date1YearAgo = DateTime.Today - TimeSpan.FromDays(365);
            return dstart <= date1YearAgo || dend <= date1YearAgo;
        }

        /// <summary>
        /// Determines whether this instance of <paramref name="workItem"/>'s start and end times can be used for calendar.
        /// </summary>
        /// <param name="workItem">The workItem.</param>
        /// <returns>
        ///   <c>true</c> if this instance [can use start and end times from work item] the specified item; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>If the tasks spans more than 1 day, do to use it to clutter the calendar.</remarks>
        private static bool _canUseStartAndEndTimesFromWorkItem(WorkItemInfo workItem)
        {
            return workItem.StartDate != null && workItem.FinishDate != null && workItem.StartDate.Value.Date == workItem.FinishDate.Value.Date;
        }
        #endregion
    }
}
