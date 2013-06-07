using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ChopshopSignin
{
    sealed class Settings
    {
        /// <summary>
        /// The instance of the singleton Settings
        /// </summary>
        public static Settings Instance { get { return lazy.Value; } }

        /// <summary>
        /// The day that the season starts (typically the first Saturday in January)
        /// </summary>
        public DateTime Kickoff { get; private set; }

        /// <summary>
        /// The day that the robot has to be finished (Midnight, the Wednesday after the last full week)
        /// </summary>
        public DateTime Ship { get; private set; }

        /// <summary>
        /// The length of time (in seconds) between total time spent updates
        /// </summary>
        public int TotalTimeUpdateInterval { get; private set; }

        /// <summary>
        /// The length of time (in seconds) before requiring someone to scan their name again
        /// </summary>
        public int ScanInTimeoutWindow { get; private set; }

        /// <summary>
        /// The length of time (in seconds) before the current scan will be cleared unless a '\r' is entered
        /// </summary>
        public int ScanDataResetTime { get; private set; }

        /// <summary>
        /// The length of time (in seconds) before the scan status string will be cleared
        /// </summary>
        public int ClearScanStatusTime { get; private set; }

        /// <summary>
        /// Choose whether or not to show time remaining until ship
        /// </summary>
        public bool ShowTimeUntilShip { get; private set; }

        /// <summary>
        /// The location of the program
        /// </summary>
        public string OutputFolder { get; private set; }

        /// <summary>
        /// The data file with the scan data
        /// </summary>
        public string DataFile { get; private set; }

        /// <summary>
        /// The file with the roster information
        /// </summary>
        public string RosterFile { get; private set; }

        /// <summary>
        /// The folder under OutputFolder where backup data files will be located
        /// </summary>
        public string BackupFolder { get; private set; }

        /// <summary>
        /// Whether the summary outputs will be created every time the program is closed
        /// </summary>
        public bool CreateSummaryOnExit { get; private set; }

        /// <summary>
        /// The maximum number of backup files that will be kept
        /// </summary>
        public int MaxBackupFilesToKeep { get; private set; }

        /// <summary>
        /// Static member holding the single instance
        /// </summary>
        private static readonly Lazy<Settings> lazy = new Lazy<Settings>(() => new Settings(Properties.Settings.Default.SettingsFileName));

        /// <summary>
        /// Constructor to set all to default
        /// </summary>
        private Settings()
        {
            OutputFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            DataFile = System.IO.Path.Combine(OutputFolder, Properties.Settings.Default.ScanDataFileName);
            RosterFile = System.IO.Path.Combine(OutputFolder, Properties.Settings.Default.RosterFile);
            BackupFolder = System.IO.Path.Combine(OutputFolder, Properties.Settings.Default.BackupFolder);

            Kickoff = Enumerable.Range(1, 7)
                                .Select(x => new DateTime(DateTime.Today.Year, 1, x))
                                .Single(x => x.DayOfWeek == DayOfWeek.Saturday);

            Ship = Enumerable.Range(1, 7)
                             .Select(x => Kickoff.AddDays(Properties.Settings.Default.SeasonLengthWeeks * 7).AddDays(x))
                             .Single(s => s.DayOfWeek == DayOfWeek.Wednesday);

            TotalTimeUpdateInterval = Properties.Settings.Default.TotalTimeUpdateInterval;
            ScanInTimeoutWindow = Properties.Settings.Default.ScanInTimeoutWindow;
            ScanDataResetTime = Properties.Settings.Default.ScanDataResetTime;
            ClearScanStatusTime = Properties.Settings.Default.ClearScanStatusTime;
            ShowTimeUntilShip = Properties.Settings.Default.ShowTimeUntilShip;
            BackupFolder = Properties.Settings.Default.BackupFolder;
            CreateSummaryOnExit = Properties.Settings.Default.CreateSummaryOnExit;
            MaxBackupFilesToKeep = Properties.Settings.Default.MaxBackupFilesToKeep;

        }

        /// <summary>
        /// Constructor to load settings from a settings file
        /// </summary>
        /// <param name="executingAssembly">The location returned by System.Reflection.Assembly.GetExecutingAssembly().Location</param>
        /// <param name="settingsFile">The settings file name</param>
        private Settings(string settingsFile)
            : this()
        {
            var file = System.IO.Path.Combine(OutputFolder, settingsFile);

            if (System.IO.File.Exists(file))
            {
                try
                {
                    var settingsData = XElement.Load(file);

                    if ((DateTime?)settingsData.Element("Kickoff") != null)
                        Kickoff = (DateTime)settingsData.Element("Kickoff");

                    if ((DateTime?)settingsData.Element("Ship") != null)
                        Ship = (DateTime)settingsData.Element("Ship");

                    BackupFolder = (string)settingsData.Element("BackupFolder") ??
                                   Properties.Settings.Default.BackupFolder;

                    MaxBackupFilesToKeep = (int?)settingsData.Element("MaxBackupFilesToKeep") ??
                                           Properties.Settings.Default.MaxBackupFilesToKeep;


                    CreateSummaryOnExit = (bool?)settingsData.Element("CreateSummaryOnExit") ??
                                          Properties.Settings.Default.CreateSummaryOnExit;

                    TotalTimeUpdateInterval = (int?)settingsData.Element("TotalTimeUpdateInterval") ??
                                               Properties.Settings.Default.TotalTimeUpdateInterval;

                    ScanInTimeoutWindow = (int?)settingsData.Element("ScanInTimeoutWindow") ??
                                          Properties.Settings.Default.ScanInTimeoutWindow;

                    ScanDataResetTime = (int?)settingsData.Element("ScanDataResetTime") ??
                                        Properties.Settings.Default.ScanDataResetTime;

                    ClearScanStatusTime = (int?)settingsData.Element("ClearScanStatusTime") ??
                                          Properties.Settings.Default.ClearScanStatusTime;

                    ShowTimeUntilShip = (bool?)settingsData.Element("ShowTimeUntilShip") ??
                                        Properties.Settings.Default.ShowTimeUntilShip;
                }
                catch (FormatException)
                {
                    System.Windows.MessageBox.Show("There was an error in the settings file. Please fix it and run the program again", "Error Reading Settings File", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }
}