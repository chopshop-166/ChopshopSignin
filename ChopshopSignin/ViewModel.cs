﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace ChopshopSignin
{
    sealed internal class ViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// The scan status text to display
        /// </summary>
        public string ScanStatus
        {
            get { lock (syncObject) { return m_LastScan; } }
            set { lock (syncObject) { m_LastScan = value; FirePropertyChanged("ScanStatus"); } }
        }

        /// <summary>
        /// The formatted string for the current time
        /// </summary>
        public string CurrentTimeString
        {
            get { lock (syncObject) { return CurrentTime.ToString("ddd MMM d, yyyy") + Environment.NewLine + CurrentTime.ToLongTimeString(); } }
        }

        /// <summary>
        /// The current time to display
        /// </summary>
        public DateTime CurrentTime
        {
            get { lock (syncObject) { return m_CurrentTime; } }
            set { lock (syncObject) { m_CurrentTime = value; FirePropertyChanged("CurrentTime"); FirePropertyChanged("CurrentTimeString"); } }
        }

        /// <summary>
        /// The header for the student list, with the number of signed in students
        /// </summary>
        public string StudentListHeader
        {
            get { lock (syncObject) { return m_StudentListHeader; } }
            set { lock (syncObject) { m_StudentListHeader = value; FirePropertyChanged("StudentListHeader"); } }
        }

        /// <summary>
        /// The header for the mentor list, with the number of signed in mentorss
        /// </summary>
        public string MentorListHeader
        {
            get { lock (syncObject) { return m_MentorListHeader; } }
            set { lock (syncObject) { m_MentorListHeader = value; FirePropertyChanged("MentorListHeader"); } }
        }

        /// <summary>
        /// Listing of everyone who is currently checked in
        /// </summary>
        public ObservableCollection<Person> CheckedIn
        {
            get { return m_CheckedIn; }
            set { m_CheckedIn = value; FirePropertyChanged("CheckedIn"); }
        }

        /// <summary>
        /// The total time spent by people at FIRST
        /// </summary>
        public TimeSpan TotalTime
        {
            get { return m_TotalTime; }
            set { m_TotalTime = value; FirePropertyChanged("TotalTime"); FirePropertyChanged("TotalTimeString"); }
        }

        /// <summary>
        /// Formatted string for displaying total time spent at FIRST
        /// </summary>
        public string TotalTimeString
        {
            get { return string.Format("{0:F0} days, {1:F0} hours, {2:F0} minutes", TotalTime.Days, TotalTime.Hours, TotalTime.Minutes, TotalTime.Seconds); }
        }

        /// <summary>
        /// The oldest timestamp, for displaying the total time
        /// </summary>
        public DateTime OldestTime
        {
            get { return m_OldestTime; }
            set { m_OldestTime = value; FirePropertyChanged("OldestTime"); FirePropertyChanged("TimeSpentHeader"); }
        }

        /// <summary>
        /// The header for time spent, with the oldest scan displayed
        /// </summary>
        public string TimeSpentHeader
        {
            get { return string.Format("Time spent since {0}", OldestTime.ToShortDateString()); }
        }

        /// <summary>
        /// Update the checked in list from a list of people
        /// </summary>
        /// <param name="people">The list of people</param>
        public void UpdateCheckedInList(IEnumerable<Person> people)
        {
            // Update the checked in observable
            CheckedIn = new ObservableCollection<Person>(people.Where(x => x.CurrentLocation == Scan.LocationType.In));

            // Get the count of each type
            var studentCount = CheckedIn.Count(x => x.Role == Person.RoleType.Student);
            var mentorCount = CheckedIn.Count(x => x.Role == Person.RoleType.Mentor);

            // Generate the new headers
            StudentListHeader = string.Format("Students Signed In ({0})", studentCount);
            MentorListHeader = string.Format("Mentors Signed In ({0})", mentorCount);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private object syncObject = new object();

        private string m_LastScan = string.Empty;
        private DateTime m_CurrentTime = DateTime.Now;
        private string m_StudentListHeader = string.Empty;
        private string m_MentorListHeader = string.Empty;
        private ObservableCollection<Person> m_CheckedIn = new ObservableCollection<Person>();
        private TimeSpan m_TotalTime = TimeSpan.Zero;
        private DateTime m_OldestTime = DateTime.Now;

        private void FirePropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
