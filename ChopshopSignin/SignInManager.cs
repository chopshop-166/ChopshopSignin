using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Windows.Input;

namespace ChopshopSignin
{
    /// <summary>
    /// Class to manager people signing in and out
    /// </summary>
    sealed class SignInManager : IDisposable
    {
        public SignInManager(ViewModel externalModel, string dataFile)
            : this(externalModel)
        {
            xmlDataFile = dataFile;
            people = Person.Load(xmlDataFile).ToDictionary(x => x.FullName, x => x);
            UpdateTotalTime();
        }

        public Func<bool> AllOutConfirmation { get; set; }

        public IList<Person> SignedInPeople { get { return SignedIn.ToArray(); } }

        /// <summary>
        /// True if there is at least one person signed in
        /// </summary>
        public bool AnySignedIn { get { return SignedIn.Any(); } }

        /// <summary>
        /// Determine if any changes have occurred in the scan data file, and if so,
        /// write the changes to the file
        /// </summary>
        public void Commit()
        {
            if (changeCount > 0)
            {
                Person.Save(people.Values, xmlDataFile);
                changeCount = 0;
            }
        }

        /// <summary>
        /// Create CSV files for summarize hours
        /// </summary>
        public void CreateSummaryFiles()
        {
            SummaryFile.CreateSummaryFiles(Utility.OutputFolder, people.Values);
        }

        /// <summary>
        /// Handles data passed in from the input, in order to determine what action to take
        /// </summary>
        /// <param name="scanText"></param>
        public void HandleScanData(string scanText)
        {
            lock (syncObject)
            {
                // Determine if a command was scanned
                var command = ParseCommand(scanText);

                switch (command)
                {
                    case ScanCommand.AllOutNow:
                        SignAllOut();
                        break;

                    // Non-command scan, store the data in the current scan
                    // This depends on the name pattern matching to detect
                    // if the scan is garbage, or a person
                    case ScanCommand.NoCommmand:
                    default:
                        var newPerson = Person.Create(scanText);

                        // If the scan data fits the pattern of a person scan
                        if (newPerson != null)
                        {
                            // Ensure that the person scanned doesn't match the last person scanned
                            if (newPerson != lastScan)
                            {
                                Console.Beep();

                                var name = newPerson.FullName;

                                // If the person isn't already in the dictionary, add them
                                if (!people.ContainsKey(name))
                                    people[name] = newPerson;


                                var result = people[name].Toggle();

                                if (result.OperationSucceeded)
                                {
                                    // Increment the change count
                                    changeCount++;

                                    // Update the display of who's signed in
                                    model.UpdateCheckedInList(people.Values);

                                    // Save the current list
                                    Commit();
                                }

                                // Display the result of the sign in/out operation
                                model.ScanStatus = result.Status;

                                lastScan = newPerson;

                                // Set the reset person timer
                                eventList.Set(EventList.Event.ResetLastScan, DoubleScanIgnoreTimeout);
                                //eventList.Set(EventList.Event.ClearDisplayStatus, )
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Find anyone signed in and sign them out
        /// </summary>
        public void SignRemainingOut()
        {
            var remaining = people.Values.Where(x => x.CurrentLocation == Scan.LocationType.In);
            var status = string.Format("Signed out all {0} remaining at {1}", remaining.Count(), DateTime.Now.ToShortTimeString());

            changeCount += remaining.Count();

            foreach (var person in remaining)
                person.SignInOrOut(Scan.LocationType.Uncounted);

            model.ScanStatus = status;
            model.UpdateCheckedInList(people.Values);

            Commit();
        }

        /// <summary>
        /// Find anyone signed in and sign them out
        /// </summary>
        public void SignAllOut()
        {
            var confirmAllOutCmd = AllOutConfirmation;

            if (confirmAllOutCmd == null)
                throw new NullReferenceException("AllOutConfirmation never set to confirmation function");

            // Sign out all signed in users at the current time
            if (confirmAllOutCmd())
            {
                var remaining = people.Values.Where(x => x.CurrentLocation == Scan.LocationType.In);
                var status = string.Format("Signed out all {0} remaining at {1}", remaining.Count(), DateTime.Now.ToShortTimeString());

                changeCount += remaining.Count();

                foreach (var person in remaining)
                    person.SignInOrOut(Scan.LocationType.Out);

                model.ScanStatus = status;
                model.UpdateCheckedInList(people.Values);
            }
            else
                model.ScanStatus = "Sign everyone out command cancelled";
        }

        /// <summary>
        /// Removes all entries prior to the date specified by the cut-off parameter
        /// </summary>
        /// <param name="cutoff">Date which indicates the oldest scan that will be kept. Time will be ignored, only date is used.</param>
        public void Prune(DateTime cutoff)
        {
            foreach (var person in people.Values)
                person.Prune(cutoff);

            changeCount++;
            //Person.Save(people.Values, xmlDataFile);
        }

        private SignInManager()
        {
            // Events not defined in this dictionary will result in ignoring that event
            eventHandler = new Dictionary<EventList.Event, Action>()
            {
                { EventList.Event.ResetLastScan, ResetLastScanEventTimeout },
                { EventList.Event.UpdateTotalTime, UpdateTotalTimeEventTimeout },
                { EventList.Event.SignOutRemaining, SignOutRemainingEventTimeout }
                // ClearDisplayStatus not used in SignInManager
            };

            eventList = new EventList();

            currentScanData = new StringBuilder();
            DoubleScanIgnoreTimeout = TimeSpan.FromSeconds(Properties.Settings.Default.DoubleScanIgnoreTime);
            ResetScanDataTimeout = TimeSpan.FromSeconds(Properties.Settings.Default.ScanDataResetTime);
            UpdateTotalTimeTimeout = TimeSpan.FromSeconds(Properties.Settings.Default.TotalTimeUpdateInterval);
            SignOutRemainingTime = TimeSpan.FromHours(Properties.Settings.Default.SignOutRemainingTime);
            Properties.Settings.Default.PropertyChanged += SettingChanged;

            UpdateSignOutRemainingTime();

            timer = new Timer(timerInterval);
            timer.Elapsed += ClockTick;
            timer.Enabled = true;
        }

        void SettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var settings = (ChopshopSignin.Properties.Settings)sender;
            switch (e.PropertyName)
            {
                case "DoubleScanIgnoreTime":
                    DoubleScanIgnoreTimeout = TimeSpan.FromDays(settings.DoubleScanIgnoreTime);
                    break;

                case "ScanDataResetTime":
                    ResetScanDataTimeout = TimeSpan.FromSeconds(settings.ScanDataResetTime);
                    break;

                case "TotalTimeUpdateInterval":
                    UpdateTotalTimeTimeout = TimeSpan.FromSeconds(settings.TotalTimeUpdateInterval);
                    // For this case, clear the event scheduled and update the time now, rescheduling it also
                    UpdateTotalTime();
                    break;

                case "SignOutRemainingTimeout":
                    SignOutRemainingTime = TimeSpan.FromHours(settings.SignOutRemainingTime);
                    break;

                case "TimeSince":
                    UpdateTotalTime();
                    break;
            }
        }

        private SignInManager(ViewModel externalModel)
            : this()
        {
            model = externalModel;
            people = new Dictionary<string, Person>();
        }

        private readonly ViewModel model;
        private readonly EventList eventList;
        private TimeSpan DoubleScanIgnoreTimeout;
        private TimeSpan ResetScanDataTimeout;
        private TimeSpan UpdateTotalTimeTimeout;
        private TimeSpan SignOutRemainingTime;
        // Dictionary for determining who is currently signed in
        private readonly Dictionary<string, Person> people;

        // Dictionary to handle events
        private readonly Dictionary<EventList.Event, Action> eventHandler;

        private const int timerInterval = 200;
        private readonly Timer timer;

        // Used to track if the currently loaded file has been changed
        private int changeCount = 0;

        private StringBuilder currentScanData;
        private Person lastScan;
        private string xmlDataFile;

        // Indicates that the object has already been disposed
        private bool disposed = false;

        private readonly object syncObject = new object();

        /// <summary>
        /// The current command to execute based on the scan data
        /// </summary>
        private enum ScanCommand { NoCommmand, AllOutNow }

        /// <summary>
        /// People currently signed in
        /// </summary>
        private IList<Person> SignedIn { get { return people.Values.Where(x => x.CurrentLocation == Scan.LocationType.In).ToReadOnly(); } }

        /// <summary>
        /// Parse the string into the appropriate command
        /// </summary>
        /// <returns>The appropriate command for the string, or NoCommand</returns>
        private ScanCommand ParseCommand(string input)
        {
            ScanCommand result;
            if (!Enum.TryParse<ScanCommand>(input, true, out result))
                return ScanCommand.NoCommmand;

            return result;
        }

        /// <summary>
        /// Every time the timer fires, the evenHandler list will be enumerated and
        /// each event will be checked to see if it expired. If it did expire, the
        /// event handler for that event will be run
        /// </summary>
        private void ClockTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            foreach (var currentEvent in eventHandler.Keys)
                if (eventList.HasExpired(currentEvent, e.SignalTime))
                    eventHandler[currentEvent]();
        }

        /// <summary>
        /// Calculates the total time spent by all people, then sets the timer to update the total again
        /// </summary>
        private void UpdateTotalTime()
        {
            // Queue up the next update
            eventList.Set(EventList.Event.UpdateTotalTime, UpdateTotalTimeTimeout);

            var startingDate = Properties.Settings.Default.TimeSince.Date;

            // Ensure that there are some people
            if (people.Any())
            {
                // Total up all the time since the starting date
                model.TotalTime = people.Values.Aggregate(TimeSpan.Zero, (accumulate, x) => accumulate = accumulate.Add(x.GetTotalTimeSince(startingDate)));
            }
        }

        /// <summary>
        /// Handles when the ResetCurrentPerson event expires and the currently selected
        /// person needs to be reset
        /// </summary>
        private void ResetLastScanEventTimeout()
        {
            lastScan = null;
        }

        /// <summary>
        /// Handles when the UpdateTotalTime event expires and the total
        /// time displayed has to be updated
        /// </summary>
        private void UpdateTotalTimeEventTimeout()
        {
            // Update the total time displayed
            UpdateTotalTime();

            // Schedule another update
            eventList.Set(EventList.Event.UpdateTotalTime, UpdateTotalTimeTimeout);
        }

        /// <summary>
        /// Handles when the SignOutRemaining event expires and any remaining people 
        /// should be signed out
        /// </summary>
        private void SignOutRemainingEventTimeout()
        {
            // Call function to sign out remaining users
            SignRemainingOut();

            // Update timer
            UpdateSignOutRemainingTime();

        }

        private void UpdateSignOutRemainingTime()
        {
            // Schedule another update
            TimeSpan TimeTillMidnight = DateTime.Today.AddDays(1).Subtract(DateTime.Now) + SignOutRemainingTime;
            //TimeSpan TimeTillMidnight = DateTime.Now.AddMinutes(1).Subtract(DateTime.Now);
            eventList.Set(EventList.Event.SignOutRemaining, TimeTillMidnight);
        }

        /// <summary>
        /// Allows the release of the system resources used by the object
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Internal dispose function to handle cleaning up the object
        /// </summary>
        /// <param name="disposing">Indicates that the dispose operation is called from a user dispose</param>
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!disposed)
                {
                    disposed = true;
                    timer.Dispose();
                    GC.SuppressFinalize(this);
                }
            }
        }
    }
}