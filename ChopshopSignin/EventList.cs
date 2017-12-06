using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChopshopSignin
{
    /// <summary>
    /// Class to manage event timers
    /// </summary>
    class EventList
    {
        /// <summary>
        /// Events that can be timed
        /// </summary>
        public enum Event { ResetLastScan, UpdateTotalTime, ClearDisplayStatus, SignOutRemaining }

        /// <summary>
        /// Creates a list of all the events, with all being disabled
        /// </summary>
        public EventList()
        {
            eventList = Enum.GetValues(typeof(Event))
                            .Cast<Event>()
                            .ToDictionary(x => x, x => (DateTime?)null);
        }

        /// <summary>
        /// Check if an event has expired, and if so, remove it from the list
        /// </summary>
        /// <param name="timeEvent">The event to check</param>
        /// <param name="timeToCheck">The time to check the event against. This should be relatively close to 'Now'</param>
        /// <returns>Whether the event has passed</returns>
        public bool HasExpired(Event timeEvent, DateTime timeToCheck)
        {
            bool expired = (eventList[timeEvent] ?? DateTime.MaxValue) < timeToCheck;
            if (expired)
                eventList[timeEvent] = null;

            return expired;
        }

        /// <summary>
        /// Determine if an event is enabled
        /// </summary>
        /// <param name="timeEvent">The event to check</param>
        /// <returns>Whether the event is enabled</returns>
        public bool IsEnabled(Event timeEvent)
        {
            return eventList[timeEvent] != null;
        }

        /// <summary>
        /// Set up the event for sometime in the future
        /// </summary>
        /// <param name="timeEvent">The event to set</param>
        /// <param name="timeUntil">The length of time until the event</param>
        public void Set(Event timeEvent, TimeSpan timeUntil)
        {
            eventList[timeEvent] = DateTime.Now + timeUntil;
        }

        /// <summary>
        /// Clear an event
        /// </summary>
        /// <param name="timeEvent">The event to clear</param>
        public void Clear(Event timeEvent)
        {
            eventList[timeEvent] = null;
        }

        /// <summary>
        /// 
        /// </summary>
        private Dictionary<Event, DateTime?> eventList;
    }
}