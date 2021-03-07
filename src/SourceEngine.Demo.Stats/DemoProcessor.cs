using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using SourceEngine.Demo.Parser;
using SourceEngine.Demo.Parser.Entities;
using SourceEngine.Demo.Parser.Structs;
using SourceEngine.Demo.Stats.Models;

namespace SourceEngine.Demo.Stats
{
    internal enum PSTATUS
    {
        ONSERVER,
        PLAYING,
        ALIVE,
    }

    public class TickCounter
    {
        public string detectedName = "NOT FOUND";
        public long ticksAlive;
        public long ticksOnServer;
        public long ticksPlaying;
    }

    public class MatchData
    {
        private static DemoParser dp;
        public readonly Dictionary<int, long> playerLookups = new();
        public readonly Dictionary<int, int> playerReplacements = new();

        private readonly Dictionary<int, TickCounter> playerTicks = new();

        private readonly DemoInformation demoInfo;

        // Used in ValidateBombsite() for knowing when a bombsite plant site has been changed from '?' to an actual bombsite letter
        public bool changingPlantedRoundsToA, changingPlantedRoundsToB;

        public Dictionary<Type, List<object>> events = new();

        public bool passed;

        private void addEvent(Type type, object ev)
        {
            //Create if doesn't exist
            if (!events.ContainsKey(type))
                events.Add(type, new List<object>());

            events[type].Add(ev);
        }

        /// <summary>
        /// Adds new player lookups and tick values
        /// </summary>
        /// <param name="p"></param>
        /// <returns>Whether or not the userID given has newly been / was previously stored</returns>
        public bool BindPlayer(Player p)
        {
            int duplicateIdToRemoveTicks = 0;
            int duplicateIdToRemoveLookup = 0;

            if (p.Name != "unconnected" && p.Name != "GOTV")
            {
                if (!playerTicks.ContainsKey(p.UserID))
                {
                    // check if player has been added twice with different UserIDs
                    (int userId, TickCounter counter) = playerTicks.FirstOrDefault(x => x.Value.detectedName == p.Name);

                    if (userId != 0)
                    {
                        // copy duplicate's information across
                        playerTicks.Add(
                            p.UserID,
                            new TickCounter
                            {
                                detectedName = counter.detectedName,
                                ticksAlive = counter.ticksAlive,
                                ticksOnServer = counter.ticksOnServer,
                                ticksPlaying = counter.ticksPlaying,
                            }
                        );

                        duplicateIdToRemoveTicks = userId;
                    }
                    else
                    {
                        var detectedName = string.IsNullOrWhiteSpace(p.Name) ? "NOT FOUND" : p.Name;
                        playerTicks.Add(p.UserID, new TickCounter { detectedName = detectedName });
                    }
                }

                if (!playerLookups.ContainsKey(p.UserID))
                {
                    // check if player has been added twice with different UserIDs
                    KeyValuePair<int, long> duplicate = playerLookups.FirstOrDefault(x => x.Value == p.SteamID);

                    if (duplicate.Key == 0) // if the steam ID was 0
                        duplicate = playerLookups.FirstOrDefault(x => x.Key == duplicateIdToRemoveTicks);

                    if (p.SteamID != 0)
                        playerLookups.Add(p.UserID, p.SteamID);
                    else if (p.SteamID == 0 && duplicate.Key != 0)
                        playerLookups.Add(p.UserID, duplicate.Value);

                    duplicateIdToRemoveLookup = duplicate.Key;
                }

                // remove duplicates
                if (duplicateIdToRemoveTicks != 0 || duplicateIdToRemoveLookup != 0)
                {
                    if (duplicateIdToRemoveTicks != 0)
                        playerTicks.Remove(duplicateIdToRemoveTicks);

                    if (duplicateIdToRemoveLookup != 0)
                        playerLookups.Remove(duplicateIdToRemoveLookup);

                    /* store duplicate userIDs for replacing in events later on */
                    var idRemoved = duplicateIdToRemoveLookup != 0
                        ? duplicateIdToRemoveLookup
                        : duplicateIdToRemoveTicks;

                    // removes any instance of the old userID pointing to a different userID
                    if (playerReplacements.Any(r => r.Key == idRemoved))
                        playerReplacements.Remove(idRemoved);

                    // tries to avoid infinite loops by removing the old entry
                    if (playerReplacements.Any(r => r.Key == p.UserID && r.Value == idRemoved))
                        playerReplacements.Remove(p.UserID);

                    // replace current mappings between an ancient userID & the old userID, to use the new userID as the value instead
                    if (playerReplacements.Any(r => r.Value == idRemoved))
                    {
                        IEnumerable<int> keysToReplaceValue =
                            playerReplacements.Where(r => r.Value == idRemoved).Select(r => r.Key);

                        foreach (var userId in keysToReplaceValue.ToList())
                            playerReplacements[userId] = p.UserID;
                    }

                    playerReplacements.Add(
                        idRemoved,
                        p.UserID
                    ); // Creates a new entry that maps the player's old user ID to their new user ID
                }

                return true;
            }

            return false;
        }

        private void addTick(Player p, PSTATUS status)
        {
            bool userIdStored = BindPlayer(p);

            if (userIdStored)
            {
                if (status == PSTATUS.ONSERVER)
                    playerTicks[p.UserID].ticksOnServer++;

                if (status == PSTATUS.ALIVE)
                    playerTicks[p.UserID].ticksAlive++;

                if (status == PSTATUS.PLAYING)
                    playerTicks[p.UserID].ticksPlaying++;
            }
        }

        public MatchData(
            DemoInformation demoInfo,
            bool parseChickens,
            bool parsePlayerPositions,
            uint? hostagerescuezonecountoverride,
            bool lowOutputMode)
        {
            string file = demoInfo.DemoName;
            this.demoInfo = demoInfo;

            // automatically decides rescue zone amounts unless overridden with a provided parameter
            if (hostagerescuezonecountoverride is not { } hostageRescueZones)
            {
                if (demoInfo.GameMode is GameMode.DangerZone)
                    hostageRescueZones = 2;
                else if (demoInfo.GameMode is GameMode.Hostage)
                    hostageRescueZones = 1;
                else
                    hostageRescueZones = 0;
            }

            //Create demo parser instance
            dp = new DemoParser(
                File.OpenRead(file),
                parseChickens,
                parsePlayerPositions,
                hostageRescueZones
            );

            dp.ParseHeader();

            dp.PlayerBind += (_, e) => { BindPlayer(e.Player); };

            dp.PlayerPositions += (_, e) =>
            {
                foreach (PlayerPositionEventArgs playerPosition in e.PlayerPositions)
                {
                    if (events.Count > 0 && events.Any(k => k.Key.Name.ToString() == "FreezetimeEndedEventArgs"))
                    {
                        int round = GetCurrentRoundNum(this, demoInfo.GameMode);

                        if (round > 0 && playerPosition.Player.SteamID > 0)
                        {
                            bool playerAlive = CheckIfPlayerAliveAtThisPointInRound(this, playerPosition.Player, round);
                            List<object> freezetimeEndedEvents = events
                                .Where(k => k.Key.Name.ToString() == "FreezetimeEndedEventArgs").Select(v => v.Value)
                                .ElementAt(0);

                            var freezetimeEndedEventLast =
                                (FreezetimeEndedEventArgs)freezetimeEndedEvents.LastOrDefault();

                            var freezetimeEndedThisRound = freezetimeEndedEvents.Count >= round;

                            if (playerAlive && freezetimeEndedThisRound)
                            {
                                var playerPositionsInstance = new PlayerPositionsInstance
                                {
                                    Round = round,
                                    TimeInRound = (int)e.CurrentTime - (int)freezetimeEndedEventLast.TimeEnd,
                                    SteamID = playerPosition.Player.SteamID,
                                    TeamSide = playerPosition.Player.Team is Team.Terrorist ? "T" : "CT",
                                    XPosition = playerPosition.Player.Position.X,
                                    YPosition = playerPosition.Player.Position.Y,
                                    ZPosition = playerPosition.Player.Position.Z,
                                };

                                addEvent(typeof(PlayerPositionsInstance), playerPositionsInstance);
                            }
                        }
                    }
                }
            };

            // SERVER EVENTS ===================================================
            dp.MatchStarted += (_, e) =>
            {
                var currentfeedbackMessages = new List<FeedbackMessage>();

                //stores all fb messages so that they aren't lost when stats are reset
                if (events.Count > 0 && events.Any(k => k.Key.Name.ToString() == "FeedbackMessage"))
                    foreach (FeedbackMessage message in events.Where(k => k.Key.Name.ToString() == "FeedbackMessage")
                        .Select(v => v.Value).ElementAt(0))
                    {
                        var text = message.Message;

                        if (IsMessageFeedback(text))

                            //Sets round to 0 as anything before a match start event should always be classed as warmup
                            currentfeedbackMessages.Add(
                                new FeedbackMessage
                                {
                                    Round = 0,
                                    SteamID = message.SteamID,
                                    TeamName = message.TeamName,
                                    XCurrentPosition = message.XCurrentPosition,
                                    YCurrentPosition = message.YCurrentPosition,
                                    ZCurrentPosition = message.ZCurrentPosition,
                                    XLastAlivePosition = message.XLastAlivePosition,
                                    YLastAlivePosition = message.YLastAlivePosition,
                                    ZLastAlivePosition = message.ZLastAlivePosition,
                                    XCurrentViewAngle = message.XCurrentViewAngle,
                                    YCurrentViewAngle = message.YCurrentViewAngle,
                                    SetPosCommandCurrentPosition = message.SetPosCommandCurrentPosition,
                                    Message = message.Message,
                                    TimeInRound =
                                        0, // overwrites whatever the TimeInRound value was before, 0 is generally used for messages sent in Warmup
                                }
                            );
                    }

                events = new Dictionary<Type, List<object>>(); //resets all stats stored

                addEvent(typeof(MatchStartedEventArgs), e);

                //adds all stored fb messages back
                foreach (FeedbackMessage feedbackMessage in currentfeedbackMessages)
                    addEvent(typeof(FeedbackMessage), feedbackMessage);

                //print rounds complete out to console
                if (!lowOutputMode)
                {
                    Console.WriteLine("\n");
                    Console.WriteLine("Match restarted.");
                }
            };

            dp.ChickenKilled += (_, e) => { addEvent(typeof(ChickenKilledEventArgs), e); };

            dp.SayText2 += (_, e) =>
            {
                addEvent(typeof(SayText2EventArgs), e);

                var text = e.Text.ToString();

                if (IsMessageFeedback(text))
                {
                    int round = GetCurrentRoundNum(this, demoInfo.GameMode);

                    long steamId = e.Sender?.SteamID ?? 0;

                    Player player = null;

                    if (steamId != 0)
                        player = dp.Participants.FirstOrDefault(p => p.SteamID == steamId);

                    var teamName = player?.Team.ToString();
                    teamName = teamName == "Spectate" ? "Spectator" : teamName;

                    bool playerAlive = CheckIfPlayerAliveAtThisPointInRound(this, player, round);

                    List<object> roundsOfficiallyEndedEvents =
                        events.Any(k => k.Key.Name.ToString() == "RoundOfficiallyEndedEventArgs")
                            ? events.Where(k => k.Key.Name.ToString() == "RoundOfficiallyEndedEventArgs")
                                .Select(v => v.Value).ElementAt(0)
                            : null;

                    List<object> freezetimesEndedEvents =
                        events.Any(k => k.Key.Name.ToString() == "FreezetimeEndedEventArgs")
                            ? events.Where(k => k.Key.Name.ToString() == "FreezetimeEndedEventArgs")
                                .Select(v => v.Value).ElementAt(0)
                            : null;

                    int numOfRoundsOfficiallyEnded = roundsOfficiallyEndedEvents?.Count > 0
                        ? roundsOfficiallyEndedEvents.Count
                        : 0;

                    int numOfFreezetimesEnded = freezetimesEndedEvents?.Count > 0 ? freezetimesEndedEvents.Count : 0;
                    float timeInRound = 0; // Stays as '0' if sent during freezetime

                    if (numOfFreezetimesEnded > numOfRoundsOfficiallyEnded)
                    {
                        // would it be better to use '.OrderByDescending(f => f.TimeEnd).FirstOrDefault()' ?
                        var freezetimeEnded = (FreezetimeEndedEventArgs)freezetimesEndedEvents.LastOrDefault();
                        timeInRound = dp.CurrentTime - freezetimeEnded.TimeEnd;
                    }

                    var feedbackMessage = new FeedbackMessage
                    {
                        Round = round,
                        SteamID = steamId,
                        TeamName = teamName, // works out TeamName in GetFeedbackMessages() if it is null
                        XCurrentPosition = player?.Position.X,
                        YCurrentPosition = player?.Position.Y,
                        ZCurrentPosition = player?.Position.Z,
                        XLastAlivePosition = playerAlive ? player?.LastAlivePosition.X : null,
                        YLastAlivePosition = playerAlive ? player?.LastAlivePosition.Y : null,
                        ZLastAlivePosition = playerAlive ? player?.LastAlivePosition.Z : null,
                        XCurrentViewAngle = player?.ViewDirectionX,
                        YCurrentViewAngle = player?.ViewDirectionY,
                        SetPosCommandCurrentPosition = GenerateSetPosCommand(player),
                        Message = text,
                        // counts messages sent after the round_end event fires as the next round, set to '0' as if it
                        // was the next round's warmup (done this way instead of using round starts to avoid potential
                        // issues when restarting rounds)
                        TimeInRound = timeInRound,
                    };

                    addEvent(typeof(FeedbackMessage), feedbackMessage);
                }
            };

            dp.RoundEnd += (_, e) =>
            {
                IEnumerable<List<object>> roundsEndedEvents =
                    events.Where(k => k.Key.Name.ToString() == "RoundEndedEventArgs").Select(v => v.Value);

                IEnumerable<List<object>> roundsOfficiallyEndedEvents = events
                    .Where(k => k.Key.Name.ToString() == "RoundOfficiallyEndedEventArgs").Select(v => v.Value);

                IEnumerable<List<object>> freezetimesEndedEvents = events
                    .Where(k => k.Key.Name.ToString() == "FreezetimeEndedEventArgs").Select(v => v.Value);

                int numOfRoundsEnded = roundsEndedEvents.Any() ? roundsEndedEvents.ElementAt(0).Count : 0;
                int numOfRoundsOfficiallyEnded = roundsOfficiallyEndedEvents.Any()
                    ? roundsOfficiallyEndedEvents.ElementAt(0).Count
                    : 0;

                int numOfFreezetimesEnded =
                    freezetimesEndedEvents.Any() ? freezetimesEndedEvents.ElementAt(0).Count : 0;

                //Console.WriteLine("dp.RoundEnd -- " + numOfRoundsEnded + " - " + numOfRoundsOfficiallyEnded + " - " + numOfFreezetimesEnded);

                // if round_officially_ended event did not get fired in this round due to error
                while (numOfRoundsEnded > numOfRoundsOfficiallyEnded)
                {
                    var roundEndedEvent =
                        (RoundEndedEventArgs)roundsEndedEvents.ElementAt(0).ElementAt(numOfRoundsOfficiallyEnded);

                    dp.RaiseRoundOfficiallyEnded(
                        new RoundOfficiallyEndedEventArgs // adds the missing RoundOfficiallyEndedEvent
                        {
                            Message = roundEndedEvent.Message,
                            Reason = roundEndedEvent.Reason,
                            Winner = roundEndedEvent.Winner,
                            Length = roundEndedEvent.Length + 4, // guesses the round length
                        }
                    );

                    numOfRoundsOfficiallyEnded = roundsOfficiallyEndedEvents.ElementAt(0).Count;
                }

                // if round_freeze_end event did not get fired in this round due to error
                while (numOfRoundsEnded >= numOfFreezetimesEnded)
                {
                    dp.RaiseFreezetimeEnded(
                        new FreezetimeEndedEventArgs
                        {
                            TimeEnd = -1, // no idea when this actually ended without guessing
                        }
                    );

                    numOfFreezetimesEnded = freezetimesEndedEvents.ElementAt(0).Count;

                    // set the TimeInRound value to '-1' for any feedback messages sent this round, as it will be wrong
                    if (events.Any(k => k.Key.Name.ToString() == "FeedbackMessage"))
                        foreach (FeedbackMessage message in events
                            .Where(k => k.Key.Name.ToString() == "FeedbackMessage").Select(v => v.Value).ElementAt(0))
                        {
                            if (message.Round == numOfFreezetimesEnded)
                                message.TimeInRound = -1;
                        }
                }

                addEvent(typeof(RoundEndedEventArgs), e);
            };

            dp.RoundOfficiallyEnded += (_, e) =>
            {
                IEnumerable<List<object>> roundsEndedEvents =
                    events.Where(k => k.Key.Name.ToString() == "RoundEndedEventArgs").Select(v => v.Value);

                IEnumerable<List<object>> roundsOfficiallyEndedEvents = events
                    .Where(k => k.Key.Name.ToString() == "RoundOfficiallyEndedEventArgs").Select(v => v.Value);

                IEnumerable<List<object>> freezetimesEndedEvents = events
                    .Where(k => k.Key.Name.ToString() == "FreezetimeEndedEventArgs").Select(v => v.Value);

                int numOfRoundsEnded = roundsEndedEvents.Any() ? roundsEndedEvents.ElementAt(0).Count : 0;
                int numOfRoundsOfficiallyEnded = roundsOfficiallyEndedEvents.Any()
                    ? roundsOfficiallyEndedEvents.ElementAt(0).Count
                    : 0;

                int numOfFreezetimesEnded =
                    freezetimesEndedEvents.Any() ? freezetimesEndedEvents.ElementAt(0).Count : 0;

                //Console.WriteLine("dp.RoundOfficiallyEnded -- " + numOfRoundsEnded + " - " + numOfRoundsOfficiallyEnded + " - " + numOfFreezetimesEnded);

                // if round_end event did not get fired in this round due to error
                while (numOfRoundsOfficiallyEnded >= numOfRoundsEnded)
                {
                    dp.RaiseRoundEnd(
                        new RoundEndedEventArgs
                        {
                            Winner = Team.Unknown,
                            Message = "Unknown",
                            Reason = RoundEndReason.Unknown,
                            Length = 0,
                        }
                    );

                    numOfRoundsEnded = roundsEndedEvents.ElementAt(0).Count;
                }

                // if round_freeze_end event did not get fired in this round due to error
                while (numOfRoundsOfficiallyEnded >= numOfFreezetimesEnded)
                {
                    dp.RaiseFreezetimeEnded(
                        new FreezetimeEndedEventArgs
                        {
                            TimeEnd = -1, // no idea when this actually ended without guessing
                        }
                    );

                    numOfFreezetimesEnded = freezetimesEndedEvents.ElementAt(0).Count;

                    // set the TimeInRound value to '-1' for any feedback messages sent this round, as it will be wrong
                    if (events.Any(k => k.Key.Name.ToString() == "FeedbackMessage"))
                        foreach (FeedbackMessage message in events
                            .Where(k => k.Key.Name.ToString() == "FeedbackMessage").Select(v => v.Value).ElementAt(0))
                        {
                            if (message.Round == numOfFreezetimesEnded)
                                message.TimeInRound = -1;
                        }
                }

                // update round length round_end event for this round
                var roundEndedEvent = (RoundEndedEventArgs)roundsEndedEvents.ElementAt(0).LastOrDefault();
                e.Message = roundEndedEvent.Message;
                e.Reason = roundEndedEvent.Reason;
                e.Winner = roundEndedEvent.Winner;

                addEvent(typeof(RoundOfficiallyEndedEventArgs), e);

                //print rounds complete out to console
                if (!lowOutputMode)
                {
                    int roundsCount = GetEvents<RoundOfficiallyEndedEventArgs>().Count;

                    //stops the progress bar getting in the way of the first row
                    if (roundsCount == 1)
                        Console.WriteLine("\n");

                    Console.WriteLine("Round " + roundsCount + " complete.");
                }
            };

            dp.SwitchSides += (_, _) =>
            {
                int roundsCount = GetEvents<RoundOfficiallyEndedEventArgs>().Count;

                var switchSidesEventArgs = new SwitchSidesEventArgs
                {
                    RoundBeforeSwitch = roundsCount + 1,
                }; // announce_phase_end event occurs before round_officially_ended event

                addEvent(typeof(SwitchSidesEventArgs), switchSidesEventArgs);
            };

            dp.FreezetimeEnded += (_, e) =>
            {
                IEnumerable<List<object>> freezetimesEndedEvents = events
                    .Where(k => k.Key.Name.ToString() == "FreezetimeEndedEventArgs").Select(v => v.Value);

                IEnumerable<List<object>> roundsEndedEvents =
                    events.Where(k => k.Key.Name.ToString() == "RoundEndedEventArgs").Select(v => v.Value);

                IEnumerable<List<object>> roundsOfficiallyEndedEvents = events
                    .Where(k => k.Key.Name.ToString() == "RoundOfficiallyEndedEventArgs").Select(v => v.Value);

                int numOfFreezetimesEnded =
                    freezetimesEndedEvents.Any() ? freezetimesEndedEvents.ElementAt(0).Count : 0;

                int numOfRoundsEnded = roundsEndedEvents.Any() ? roundsEndedEvents.ElementAt(0).Count : 0;
                int numOfRoundsOfficiallyEnded = roundsOfficiallyEndedEvents.Any()
                    ? roundsOfficiallyEndedEvents.ElementAt(0).Count
                    : 0;

                //Console.WriteLine("dp.FreezetimeEnded -- Ended: " + numOfRoundsEnded + " - " + numOfRoundsOfficiallyEnded + " - " + numOfFreezetimesEnded);

                /*	The final round in a match does not throw a round_officially_ended event, but a round_freeze_end event is thrown after the game ends,
                    so assume that a game has ended if a second round_freeze_end event is found in the same round as a round_end_event and NO round_officially_ended event.
                    This does mean that if a round_officially_ended event is not triggered due to demo error, the parse will mess up. */
                var minRoundsForWin = GetMinRoundsForWin(demoInfo.GameMode, demoInfo.TestType);

                if (numOfFreezetimesEnded == numOfRoundsOfficiallyEnded + 1 && numOfFreezetimesEnded == numOfRoundsEnded
                    && numOfRoundsEnded >= minRoundsForWin)
                {
                    Console.WriteLine("Assuming the parse has finished.");

                    var roundEndedEvent =
                        (RoundEndedEventArgs)roundsEndedEvents.ElementAt(0).ElementAt(numOfRoundsOfficiallyEnded);

                    dp.RaiseRoundOfficiallyEnded(
                        new RoundOfficiallyEndedEventArgs // adds the missing RoundOfficiallyEndedEvent
                        {
                            Reason = roundEndedEvent.Reason,
                            Message = roundEndedEvent.Message,
                            Winner = roundEndedEvent.Winner,
                            Length = roundEndedEvent.Length + 4, // guesses the round length
                        }
                    );

                    dp.stopParsingDemo =
                        true; // forcefully stops the demo from being parsed any further to avoid events

                    // (such as player deaths to world) happening in a next round (a round that never actually occurs)

                    return;
                }

                // if round_end event did not get fired in the previous round due to error
                while (numOfFreezetimesEnded > numOfRoundsEnded)
                {
                    dp.RaiseRoundEnd(
                        new RoundEndedEventArgs
                        {
                            Winner = Team.Unknown,
                            Message = "Unknown",
                            Reason = RoundEndReason.Unknown,
                            Length = 0,
                        }
                    );

                    numOfRoundsEnded = roundsEndedEvents.ElementAt(0).Count;
                }

                // if round_officially_ended event did not get fired in the previous round due to error
                while (numOfFreezetimesEnded > numOfRoundsOfficiallyEnded)
                {
                    var roundEndedEvent =
                        (RoundEndedEventArgs)roundsEndedEvents.ElementAt(0).ElementAt(numOfRoundsOfficiallyEnded);

                    dp.RaiseRoundOfficiallyEnded(
                        new RoundOfficiallyEndedEventArgs // adds the missing RoundOfficiallyEndedEvent
                        {
                            Reason = roundEndedEvent.Reason,
                            Message = roundEndedEvent.Message,
                            Winner = roundEndedEvent.Winner,
                            Length = roundEndedEvent.Length + 4, // guesses the round length
                        }
                    );

                    numOfRoundsOfficiallyEnded = roundsOfficiallyEndedEvents.ElementAt(0).Count;
                }

                addEvent(typeof(FreezetimeEndedEventArgs), e);

                //work out teams at current round
                int roundsCount = GetEvents<RoundOfficiallyEndedEventArgs>().Count;
                IEnumerable<Player> players = dp.PlayingParticipants;

                var teams = new TeamPlayers
                {
                    Terrorists = players.Where(p => p.Team is Team.Terrorist).ToList(),
                    CounterTerrorists = players.Where(p => p.Team is Team.CounterTerrorist).ToList(),
                    Round = roundsCount + 1,
                };

                addEvent(typeof(TeamPlayers), teams);

                int tEquipValue = 0, ctEquipValue = 0;
                int tExpenditure = 0, ctExpenditure = 0;

                foreach (Player player in teams.Terrorists)
                {
                    tEquipValue += player.CurrentEquipmentValue; // player.FreezetimeEndEquipmentValue = 0 ???
                    // (player.FreezetimeEndEquipmentValue = 0 - player.RoundStartEquipmentValue) ???
                    tExpenditure += player.CurrentEquipmentValue - player.RoundStartEquipmentValue;
                }

                foreach (Player player in teams.CounterTerrorists)
                {
                    ctEquipValue += player.CurrentEquipmentValue; // player.FreezetimeEndEquipmentValue = 0 ???
                    // (player.FreezetimeEndEquipmentValue = 0 - player.RoundStartEquipmentValue) ???
                    ctExpenditure += player.CurrentEquipmentValue - player.RoundStartEquipmentValue;
                }

                var teamEquipmentStats = new TeamEquipment
                {
                    Round = roundsCount + 1,
                    TEquipValue = tEquipValue,
                    CTEquipValue = ctEquipValue,
                    TExpenditure = tExpenditure,
                    CTExpenditure = ctExpenditure,
                };

                addEvent(typeof(TeamEquipment), teamEquipmentStats);
            };

            // PLAYER EVENTS ===================================================
            dp.PlayerKilled += (_, e) =>
            {
                e.Round = GetCurrentRoundNum(this, demoInfo.GameMode);

                addEvent(typeof(PlayerKilledEventArgs), e);
            };

            dp.PlayerHurt += (_, e) =>
            {
                var round = GetCurrentRoundNum(this, demoInfo.GameMode);

                if (e.PossiblyKilledByBombExplosion
                ) // a player_death event is not triggered due to death by bomb explosion
                {
                    var playerKilledEventArgs = new PlayerKilledEventArgs
                    {
                        Round = round,
                        TimeInRound = e.TimeInRound,
                        Killer = e.Attacker,
                        KillerBotTakeover = false, // ?
                        Victim = e.Player,
                        VictimBotTakeover = false, // ?
                        Assister = null,
                        AssisterBotTakeover = false, // ?
                        Suicide = false,
                        TeamKill = false,
                        PenetratedObjects = 0,
                        Headshot = false,
                        AssistedFlash = false,
                    };

                    addEvent(typeof(PlayerKilledEventArgs), playerKilledEventArgs);
                }

                var player = new Player(e.Player);
                var attacker = new Player(e.Attacker);

                var playerHurt = new PlayerHurt
                {
                    Round = round,
                    TimeInRound = e.TimeInRound,
                    Player = player,
                    XPositionPlayer = player.Position.X,
                    YPositionPlayer = player.Position.Y,
                    ZPositionPlayer = player.Position.Z,
                    Attacker = attacker,
                    XPositionAttacker = attacker.Position?.X ?? 0,
                    YPositionAttacker = attacker.Position?.Y ?? 0,
                    ZPositionAttacker = attacker.Position?.Z ?? 0,
                    Health = e.Health,
                    Armor = e.Armor,
                    Weapon = e.Weapon,
                    HealthDamage = e.HealthDamage,
                    ArmorDamage = e.ArmorDamage,
                    HitGroup = e.HitGroup,
                    PossiblyKilledByBombExplosion = e.PossiblyKilledByBombExplosion,
                };

                addEvent(typeof(PlayerHurt), playerHurt);
            };

            dp.RoundMVP += (_, e) => { addEvent(typeof(RoundMVPEventArgs), e); };

            dp.PlayerDisconnect += (_, e) =>
            {
                if (e.Player != null && e.Player.Name != "unconnected" && e.Player.Name != "GOTV")
                {
                    int round = GetCurrentRoundNum(this, demoInfo.GameMode);

                    var disconnectedPlayer = new DisconnectedPlayer
                    {
                        PlayerDisconnectEventArgs = e,
                        Round = round - 1,
                    };

                    addEvent(typeof(DisconnectedPlayer), disconnectedPlayer);
                }
            };

            // BOMB EVENTS =====================================================
            dp.BombPlanted += (_, e) =>
            {
                int roundsCount = GetEvents<RoundOfficiallyEndedEventArgs>().Count;

                var bombPlanted = new BombPlanted
                {
                    Round = roundsCount + 1,
                    TimeInRound = e.TimeInRound,
                    Player = e.Player,
                    Bombsite = e.Site,
                };

                addEvent(typeof(BombPlanted), bombPlanted);
            };

            dp.BombExploded += (_, e) =>
            {
                int roundsCount = GetEvents<RoundOfficiallyEndedEventArgs>().Count;

                var bombExploded = new BombExploded
                {
                    Round = roundsCount + 1,
                    TimeInRound = e.TimeInRound,
                    Player = e.Player,
                    Bombsite = e.Site,
                };

                addEvent(typeof(BombExploded), bombExploded);
            };

            dp.BombDefused += (_, e) =>
            {
                int roundsCount = GetEvents<RoundOfficiallyEndedEventArgs>().Count;

                var bombDefused = new BombDefused
                {
                    Round = roundsCount + 1,
                    TimeInRound = e.TimeInRound,
                    Player = e.Player,
                    Bombsite = e.Site,
                    HasKit = e.Player.HasDefuseKit,
                };

                addEvent(typeof(BombDefused), bombDefused);
            };

            // HOSTAGE EVENTS =====================================================
            dp.HostageRescued += (_, e) =>
            {
                int roundsCount = GetEvents<RoundOfficiallyEndedEventArgs>().Count;

                var hostageRescued = new HostageRescued
                {
                    Round = roundsCount + 1,
                    TimeInRound = e.TimeInRound,
                    Player = e.Player,
                    Hostage = e.Hostage,
                    HostageIndex = e.HostageIndex,
                    RescueZone = e.RescueZone,
                };

                addEvent(typeof(HostageRescued), hostageRescued);
            };

            // HOSTAGE EVENTS =====================================================
            dp.HostagePickedUp += (_, e) =>
            {
                int roundsCount = GetEvents<RoundOfficiallyEndedEventArgs>().Count;

                var hostagePickedUp = new HostagePickedUp
                {
                    Round = roundsCount + 1,
                    TimeInRound = e.TimeInRound,
                    Player = e.Player,
                    Hostage = e.Hostage,
                    HostageIndex = e.HostageIndex,
                };

                addEvent(typeof(HostagePickedUp), hostagePickedUp);
            };

            // WEAPON EVENTS ===================================================
            dp.WeaponFired += (_, e) =>
            {
                addEvent(typeof(WeaponFiredEventArgs), e);

                var round = GetCurrentRoundNum(this, demoInfo.GameMode);

                var shotFired = new ShotFired
                {
                    Round = round,
                    TimeInRound = e.TimeInRound,
                    Shooter = e.Shooter,
                    TeamSide = e.Shooter.Team.ToString(),
                    Weapon = new Equipment(e.Weapon),
                };

                addEvent(typeof(ShotFired), shotFired);
            };

            // GRENADE EVENTS ==================================================
            dp.ExplosiveNadeExploded += (_, e) =>
            {
                addEvent(typeof(GrenadeEventArgs), e);
                addEvent(typeof(NadeEventArgs), e);
            };

            dp.FireNadeStarted += (_, e) =>
            {
                addEvent(typeof(FireEventArgs), e);
                addEvent(typeof(NadeEventArgs), e);
            };

            dp.SmokeNadeStarted += (_, e) =>
            {
                addEvent(typeof(SmokeEventArgs), e);
                addEvent(typeof(NadeEventArgs), e);
            };

            dp.FlashNadeExploded += (_, e) =>
            {
                addEvent(typeof(FlashEventArgs), e);
                addEvent(typeof(NadeEventArgs), e);
            };

            dp.DecoyNadeStarted += (_, e) =>
            {
                addEvent(typeof(DecoyEventArgs), e);
                addEvent(typeof(NadeEventArgs), e);
            };

            // PLAYER TICK HANDLER ============================================
            dp.TickDone += (_, _) =>
            {
                foreach (Player p in dp.PlayingParticipants)
                {
                    addTick(p, PSTATUS.PLAYING);

                    if (p.IsAlive)
                        addTick(p, PSTATUS.ALIVE);
                }

                foreach (Player p in dp.Participants)
                {
                    if (!p.Disconnected)
                        addTick(p, PSTATUS.ONSERVER);
                }
            };

            const int interval = 2500;
            int progMod = interval;

            if (!lowOutputMode)
            {
                var pv = new ProgressViewer(Path.GetFileName(file));

                // PROGRESS BAR ==================================================
                dp.TickDone += (_, _) =>
                {
                    progMod++;

                    if (progMod >= interval)
                    {
                        progMod = 0;

                        pv.percent = dp.ParsingProgess;
                        pv.Draw();
                    }
                };

                try
                {
                    dp.ParseToEnd();
                    pv.End();

                    passed = true;
                }
                catch (Exception)
                {
                    pv.Error();
                }
            }
            else
            {
                try
                {
                    dp.ParseToEnd();

                    passed = true;
                }
                catch (Exception) { }
            }

            dp.Dispose();
        }

        public ProcessedData GetProcessedData()
        {
            IEnumerable<MatchStartedEventArgs> mse;
            IEnumerable<SwitchSidesEventArgs> sse;
            IEnumerable<FeedbackMessage> fme;
            IEnumerable<TeamPlayers> tpe;
            IEnumerable<PlayerHurt> ph;
            IEnumerable<PlayerKilledEventArgs> pke;
            var pe = new Dictionary<string, IEnumerable<Player>>();
            IEnumerable<Equipment> pwe;
            IEnumerable<int> poe;
            IEnumerable<BombPlanted> bpe;
            IEnumerable<BombExploded> bee;
            IEnumerable<BombDefused> bde;
            IEnumerable<HostageRescued> hre;
            IEnumerable<HostagePickedUp> hpu;
            IEnumerable<DisconnectedPlayer> dpe;
            IEnumerable<Team> te;
            IEnumerable<RoundEndReason> re;
            IEnumerable<double> le;
            IEnumerable<TeamEquipment> tes;
            IEnumerable<NadeEventArgs> ge;

            //IEnumerable<SmokeEventArgs> gse;
            //IEnumerable<FlashEventArgs> gfe;
            //IEnumerable<GrenadeEventArgs> gge;
            //IEnumerable<FireEventArgs> gie;
            //IEnumerable<DecoyEventArgs> gde;
            IEnumerable<ChickenKilledEventArgs> cke;
            IEnumerable<ShotFired> sfe;
            IEnumerable<PlayerPositionsInstance> ppe;

            mse = from start in GetEvents<MatchStartedEventArgs>() select start as MatchStartedEventArgs;

            sse = from switchSide in GetEvents<SwitchSidesEventArgs>() select switchSide as SwitchSidesEventArgs;

            fme = from message in GetEvents<FeedbackMessage>() select message as FeedbackMessage;

            ph = from player in GetEvents<PlayerHurt>() select player as PlayerHurt;

            pke = from player in GetEvents<PlayerKilledEventArgs>() select player as PlayerKilledEventArgs;

            pe.Add(
                "Kills",
                from player in GetEvents<PlayerKilledEventArgs>() select (player as PlayerKilledEventArgs).Killer
            );

            pe.Add(
                "Deaths",
                from player in GetEvents<PlayerKilledEventArgs>() select (player as PlayerKilledEventArgs).Victim
            );

            pe.Add(
                "Headshots",
                from player in GetEvents<PlayerKilledEventArgs>()
                where (player as PlayerKilledEventArgs).Headshot
                select (player as PlayerKilledEventArgs).Killer
            );

            pe.Add(
                "Assists",
                from player in GetEvents<PlayerKilledEventArgs>()
                where (player as PlayerKilledEventArgs).Assister != null
                select (player as PlayerKilledEventArgs).Assister
            );

            pwe = from weapon in GetEvents<PlayerKilledEventArgs>() select (weapon as PlayerKilledEventArgs).Weapon;

            poe = from penetration in GetEvents<PlayerKilledEventArgs>()
                select (penetration as PlayerKilledEventArgs).PenetratedObjects;

            pe.Add("MVPs", from player in GetEvents<RoundMVPEventArgs>() select (player as RoundMVPEventArgs).Player);

            pe.Add(
                "Shots",
                from player in GetEvents<WeaponFiredEventArgs>() select (player as WeaponFiredEventArgs).Shooter
            );

            pe.Add("Plants", from player in GetEvents<BombPlanted>() select (player as BombPlanted).Player);

            pe.Add("Defuses", from player in GetEvents<BombDefused>() select (player as BombDefused).Player);

            pe.Add("Rescues", from player in GetEvents<HostageRescued>() select (player as HostageRescued).Player);

            bpe = (from plant in GetEvents<BombPlanted>() select plant as BombPlanted).GroupBy(p => p.Round)
                .Select(p => p.FirstOrDefault());

            bee = (from explode in GetEvents<BombExploded>() select explode as BombExploded).GroupBy(p => p.Round)
                .Select(p => p.FirstOrDefault());

            bde = (from defuse in GetEvents<BombDefused>() select defuse as BombDefused).GroupBy(p => p.Round)
                .Select(p => p.FirstOrDefault());

            hre = from hostage in GetEvents<HostageRescued>() select hostage as HostageRescued;

            hpu = from hostage in GetEvents<HostagePickedUp>() select hostage as HostagePickedUp;

            dpe = from disconnection in GetEvents<DisconnectedPlayer>() select disconnection as DisconnectedPlayer;

            te = from team in GetEvents<RoundOfficiallyEndedEventArgs>()
                select (team as RoundOfficiallyEndedEventArgs).Winner;

            re = from reason in GetEvents<RoundOfficiallyEndedEventArgs>()
                select (reason as RoundOfficiallyEndedEventArgs).Reason;

            le = from length in GetEvents<RoundOfficiallyEndedEventArgs>()
                select (length as RoundOfficiallyEndedEventArgs).Length;

            // removes extra TeamPlayers if freezetime_end event triggers once a playtest is finished
            tpe = from teamPlayers in GetEvents<TeamPlayers>()
                where (teamPlayers as TeamPlayers).Round <= te.Count()
                select teamPlayers as TeamPlayers;

            tes = from round in GetEvents<TeamEquipment>() select round as TeamEquipment;

            ge = from nade in GetEvents<NadeEventArgs>() select nade as NadeEventArgs;

            cke = from chickenKill in GetEvents<ChickenKilledEventArgs>() select chickenKill as ChickenKilledEventArgs;

            sfe = from shot in GetEvents<ShotFired>() select shot as ShotFired;

            ppe = from playerPos in GetEvents<PlayerPositionsInstance>() select playerPos as PlayerPositionsInstance;

            tanookiStats tanookiStats = TanookiStatsCreator(tpe, dpe);

            return new ProcessedData
            {
                tanookiStats = tanookiStats,
                MatchStartValues = mse,
                SwitchSidesValues = sse,
                MessagesValues = fme,
                TeamPlayersValues = tpe,
                PlayerHurtValues = ph,
                PlayerKilledEventsValues = pke,
                PlayerValues = pe,
                WeaponValues = pwe,
                PenetrationValues = poe,
                BombsitePlantValues = bpe,
                BombsiteExplodeValues = bee,
                BombsiteDefuseValues = bde,
                HostageRescueValues = hre,
                HostagePickedUpValues = hpu,
                TeamValues = te,
                RoundEndReasonValues = re,
                RoundLengthValues = le,
                TeamEquipmentValues = tes,
                GrenadeValues = ge,
                ChickenValues = cke,
                ShotsFiredValues = sfe,
                PlayerPositionsValues = ppe,
                WriteTicks = true,
            };

        }

        private static tanookiStats TanookiStatsCreator(
            IEnumerable<TeamPlayers> tpe,
            IEnumerable<DisconnectedPlayer> dpe)
        {
            var tanookiStats = new tanookiStats
            {
                Joined = false,
                Left = false,
                RoundJoined = -1,
                RoundLeft = -1,
                RoundsLasted = -1,
            };

            const long tanookiId = 76561198123165941;

            if (tpe.Any(t => t.Terrorists.Any(p => p.SteamID == tanookiId))
                || tpe.Any(t => t.CounterTerrorists.Any(p => p.SteamID == tanookiId)))
            {
                tanookiStats.Joined = true;
                tanookiStats.RoundJoined = 0; // set in case he joined in warmup but does not play any rounds

                IEnumerable<int> playedRoundsT =
                    tpe.Where(t => t.Round > 0 && t.Terrorists.Any(p => p.SteamID == tanookiId)).Select(r => r.Round);

                IEnumerable<int> playedRoundsCT =
                    tpe.Where(t => t.Round > 0 && t.CounterTerrorists.Any(p => p.SteamID == tanookiId))
                        .Select(r => r.Round);

                tanookiStats.RoundsLasted = playedRoundsT.Count() + playedRoundsCT.Count();

                bool playedTSide = playedRoundsT.Any();
                bool playedCTSide = playedRoundsCT.Any();

                tanookiStats.RoundJoined = playedTSide ? playedCTSide ? playedRoundsT.First() < playedRoundsCT.First()
                        ?
                        playedRoundsT.First()
                        : playedRoundsCT.First() : playedRoundsT.First() :
                    playedCTSide ? playedRoundsCT.First() : tanookiStats.RoundJoined;
            }

            if (dpe.Any(
                d => d.PlayerDisconnectEventArgs.Player != null
                    && d.PlayerDisconnectEventArgs.Player.SteamID == tanookiId
            ))
            {
                // checks if he played a round later on than his last disconnect (he left and joined back)
                int finalDisconnectRound = dpe.Where(d => d.PlayerDisconnectEventArgs.Player.SteamID == tanookiId)
                    .Reverse().Select(r => r.Round).First();

                tanookiStats.RoundLeft = finalDisconnectRound > tanookiStats.RoundsLasted
                    ? finalDisconnectRound
                    : tanookiStats.RoundLeft;

                tanookiStats.Left = tanookiStats.RoundLeft > -1;
            }

            return tanookiStats;
        }

        public AllStats GetAllStats(ProcessedData processedData)
        {
            var mapNameSplit = processedData.MatchStartValues.Any()
                ? processedData.MatchStartValues.ElementAt(0).Mapname.Split('/')
                : new[] { demoInfo.MapName };

            DataAndPlayerNames dataAndPlayerNames = GetDataAndPlayerNames(processedData);

            var allStats = new AllStats
            {
                versionNumber = GetVersionNumber(),
                supportedGamemodes = Enum.GetNames(typeof(GameMode)).Select(gm => gm.ToLower()).ToList(),
                mapInfo = GetMapInfo(processedData, mapNameSplit),
                tanookiStats = processedData.tanookiStats,
            };

            if (CheckIfStatsShouldBeCreated("playerStats", demoInfo.GameMode))
                allStats.playerStats = GetPlayerStats(
                    processedData,
                    dataAndPlayerNames.Data,
                    dataAndPlayerNames.PlayerNames
                );

            GeneralroundsStats generalroundsStats = GetGeneralRoundsStats(
                processedData,
                dataAndPlayerNames.PlayerNames
            );

            if (CheckIfStatsShouldBeCreated("winnersStats", demoInfo.GameMode))
                allStats.winnersStats = generalroundsStats.winnersStats;

            if (CheckIfStatsShouldBeCreated("roundsStats", demoInfo.GameMode))
                allStats.roundsStats = generalroundsStats.roundsStats;

            if (CheckIfStatsShouldBeCreated("bombsiteStats", demoInfo.GameMode))
                allStats.bombsiteStats = GetBombsiteStats(processedData);

            if (CheckIfStatsShouldBeCreated("hostageStats", demoInfo.GameMode))
                allStats.hostageStats = GetHostageStats(processedData);

            if (CheckIfStatsShouldBeCreated("rescueZoneStats", demoInfo.GameMode))
                allStats.rescueZoneStats = GetRescueZoneStats();

            Dictionary<EquipmentElement, List<NadeEventArgs>> nadeGroups = processedData.GrenadeValues
                .Where(e => e.NadeType >= EquipmentElement.Decoy && e.NadeType <= EquipmentElement.HE)
                .GroupBy(e => e.NadeType).ToDictionary(g => g.Key, g => g.ToList());

            if (CheckIfStatsShouldBeCreated("grenadesTotalStats", demoInfo.GameMode))
                allStats.grenadesTotalStats = GetGrenadesTotalStats(nadeGroups);

            if (CheckIfStatsShouldBeCreated("grenadesSpecificStats", demoInfo.GameMode))
                allStats.grenadesSpecificStats = GetGrenadesSpecificStats(nadeGroups, dataAndPlayerNames.PlayerNames);

            if (CheckIfStatsShouldBeCreated("killsStats", demoInfo.GameMode))
                allStats.killsStats = GetKillsStats(processedData, dataAndPlayerNames.PlayerNames);

            if (CheckIfStatsShouldBeCreated("feedbackMessages", demoInfo.GameMode))
                allStats.feedbackMessages = GetFeedbackMessages(processedData, dataAndPlayerNames.PlayerNames);

            if (dp.ParseChickens && CheckIfStatsShouldBeCreated(
                "chickenStats",
                demoInfo.GameMode
            ))
                allStats.chickenStats = GetChickenStats(processedData);

            if (CheckIfStatsShouldBeCreated("teamStats", demoInfo.GameMode))
                allStats.teamStats = GetTeamStats(
                    processedData,
                    allStats,
                    dataAndPlayerNames.PlayerNames,
                    generalroundsStats.SwitchSides
                );

            if (CheckIfStatsShouldBeCreated("firstDamageStats", demoInfo.GameMode))
                allStats.firstDamageStats = GetFirstDamageStats(processedData);

            return allStats;
        }

        public AllOutputData CreateFiles(string outputRoot,
            List<string> foldersToProcess,
            bool sameFileName,
            bool sameFolderStructure,
            bool createJsonFile = true)
        {
            ProcessedData processedData = GetProcessedData();
            AllStats allStats = GetAllStats(processedData);
            PlayerPositionsStats playerPositionsStats = null;

            if (dp.ParsePlayerPositions && CheckIfStatsShouldBeCreated(
                "playerPositionsStats",
                demoInfo.GameMode
            ))
            {
                playerPositionsStats = GetPlayerPositionsStats(processedData, allStats);
            }

            if (createJsonFile)
            {
                string path = GetOutputPathWithoutExtension(
                    outputRoot,
                    foldersToProcess,
                    demoInfo,
                    allStats.mapInfo.MapName,
                    sameFileName,
                    sameFolderStructure
                );

                WriteJson(allStats, path + ".json");

                if (playerPositionsStats is not null)
                    WriteJson(playerPositionsStats, path + "_playerpositions.json");
            }

            // return for testing purposes
            return new AllOutputData
            {
                AllStats = allStats,
                PlayerPositionsStats = playerPositionsStats,
            };
        }

        public DataAndPlayerNames GetDataAndPlayerNames(ProcessedData processedData)
        {
            var data = new Dictionary<long, Dictionary<string, long>>();
            var playerNames = new Dictionary<long, Dictionary<string, string>>();

            foreach (string catagory in processedData.PlayerValues.Keys)
            {
                foreach (Player p in processedData.PlayerValues[catagory])
                {
                    //Skip players not in this category
                    if (p == null)
                        continue;

                    // checks for an updated userID for the user, loops in case it has changed more than once
                    int userId = p.UserID;

                    while (CheckForUpdatedUserId(userId) != userId)
                        userId = CheckForUpdatedUserId(userId);

                    if (!playerLookups.ContainsKey(userId))
                        continue;

                    //Add player to collections list if doesn't exist
                    if (!playerNames.ContainsKey(playerLookups[userId]))
                        playerNames.Add(playerLookups[userId], new Dictionary<string, string>());

                    if (!data.ContainsKey(playerLookups[userId]))
                        data.Add(playerLookups[userId], new Dictionary<string, long>());

                    //Add category to dictionary if doesn't exist
                    if (!playerNames[playerLookups[userId]].ContainsKey("Name"))
                        playerNames[playerLookups[userId]].Add("Name", p.Name);

                    if (!data[playerLookups[userId]].ContainsKey(catagory))
                        data[playerLookups[userId]].Add(catagory, 0);

                    //Increment it
                    data[playerLookups[userId]][catagory]++;
                }
            }

            return new DataAndPlayerNames
            {
                Data = data,
                PlayerNames = playerNames,
            };
        }

        public static versionNumber GetVersionNumber()
        {
            return new() { Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3) };
        }

        public mapInfo GetMapInfo(ProcessedData processedData, string[] mapNameSplit)
        {
            var mapInfo = new mapInfo
            {
                MapName = demoInfo.MapName,
                TestType = demoInfo.TestType.ToString().ToLower(),
                TestDate = demoInfo.TestDate,
            };

            mapInfo.MapName =
                mapNameSplit.Length > 2
                    ? mapNameSplit[2]
                    : mapInfo.MapName; // use the map name from inside the demo itself if possible, otherwise use the map name from the demo file's name

            mapInfo.WorkshopID = mapNameSplit.Length > 2 ? mapNameSplit[1] : "unknown";
            mapInfo.DemoName =
                demoInfo.DemoName.Split('\\').Last()
                    .Replace(
                        ".dem",
                        string.Empty
                    ); // the filename of the demo, for Faceit games this is also in the "demo_url" value

            // attempts to get the game mode
            GetRoundsWonReasons(processedData.RoundEndReasonValues);

            // use the provided game mode if given as a parameter
            if (demoInfo.GameMode is not GameMode.Unknown)
            {
                mapInfo.GameMode = demoInfo.GameMode.ToString().ToLower();

                return mapInfo;
            }

            // work out the game mode if it wasn't provided as a parameter
            if (processedData.TeamPlayersValues.Any(
                    t => t.Terrorists.Count > 10
                        && processedData.TeamPlayersValues.Any(ct => ct.CounterTerrorists.Count == 0)
                ) || // assume danger zone if more than 10 Terrorists and 0 CounterTerrorists
                dp.hostageAIndex > -1 && dp.hostageBIndex > -1
                && !processedData.MatchStartValues.Any(
                    m => m.HasBombsites
                ) // assume danger zone if more than one hostage rescue zone
            )
            {
                mapInfo.GameMode = nameof(GameMode.DangerZone).ToLower();
            }
            else if (processedData.TeamPlayersValues.Any(
                t => t.Terrorists.Count > 2 && processedData.TeamPlayersValues.Any(ct => ct.CounterTerrorists.Count > 2)
            ))
            {
                if (dp.bombsiteAIndex > -1 || dp.bombsiteBIndex > -1
                    || processedData.MatchStartValues.Any(m => m.HasBombsites))
                    mapInfo.GameMode = nameof(GameMode.Defuse).ToLower();
                else if ((dp.hostageAIndex > -1 || dp.hostageBIndex > -1)
                    && !processedData.MatchStartValues.Any(m => m.HasBombsites))
                    mapInfo.GameMode = nameof(GameMode.Hostage).ToLower();
                else // what the hell is this game mode ??
                    mapInfo.GameMode = nameof(GameMode.Unknown).ToLower();
            }
            else
            {
                if (dp.bombsiteAIndex > -1 || dp.bombsiteBIndex > -1
                    || processedData.MatchStartValues.Any(m => m.HasBombsites))
                    mapInfo.GameMode = nameof(GameMode.WingmanDefuse).ToLower();
                else if ((dp.hostageAIndex > -1 || dp.hostageBIndex > -1)
                    && !processedData.MatchStartValues.Any(m => m.HasBombsites))
                    mapInfo.GameMode = nameof(GameMode.WingmanHostage).ToLower();
                else // what the hell is this game mode ??
                    mapInfo.GameMode = nameof(GameMode.Unknown).ToLower();
            }

            return mapInfo;
        }

        public List<playerStats> GetPlayerStats(
            ProcessedData processedData,
            Dictionary<long, Dictionary<string, long>> data,
            Dictionary<long, Dictionary<string, string>> playerNames)
        {
            var playerStats = new List<playerStats>();

            // remove team kills and suicides from kills (easy messy implementation)
            foreach (PlayerKilledEventArgs kill in processedData.PlayerKilledEventsValues)
            {
                if (kill.Killer != null && kill.Killer.Name != "unconnected")
                {
                    // checks for an updated userID for the user, loops in case it has changed more than once
                    int userId = kill.Killer.UserID;

                    while (CheckForUpdatedUserId(userId) != userId)
                        userId = CheckForUpdatedUserId(userId);

                    if (kill.Suicide)
                        data[playerLookups[userId]]["Kills"] -= 1;
                    else if (kill.TeamKill)
                        data[playerLookups[userId]]["Kills"] -= 2;
                }
            }

            foreach (long player in data.Keys)
            {
                IEnumerable<KeyValuePair<long, Dictionary<string, string>>> match =
                    playerNames.Where(p => p.Key.ToString() == player.ToString());

                var playerName = match.ElementAt(0).Value.ElementAt(0).Value;
                var steamID = match.ElementAt(0).Key;

                var statsList1 = new List<int>();

                foreach (string catagory in processedData.PlayerValues.Keys)
                {
                    if (data[player].ContainsKey(catagory))
                        statsList1.Add((int)data[player][catagory]);
                    else
                        statsList1.Add(0);
                }

                var statsList2 = new List<long>();

                if (processedData.WriteTicks)
                    if (playerLookups.Any(p => p.Value == player))
                        foreach (int userid in playerLookups.Keys)
                        {
                            if (playerLookups[userid] == player)
                            {
                                statsList2.Add(playerTicks[userid].ticksAlive);

                                statsList2.Add(playerTicks[userid].ticksOnServer);

                                statsList2.Add(playerTicks[userid].ticksPlaying);

                                break;
                            }
                        }

                int numOfKillsAsBot = processedData.PlayerKilledEventsValues.Count(
                    k => k.Killer != null && k.Killer.Name.ToString() == playerName && k.KillerBotTakeover
                );

                int numOfDeathsAsBot = processedData.PlayerKilledEventsValues.Count(
                    k => k.Victim != null && k.Victim.Name.ToString() == playerName && k.VictimBotTakeover
                );

                int numOfAssistsAsBot = processedData.PlayerKilledEventsValues.Count(
                    k => k.Assister != null && k.Assister.Name.ToString() == playerName && k.AssisterBotTakeover
                );

                playerStats.Add(
                    new playerStats
                    {
                        PlayerName = playerName,
                        SteamID = steamID,
                        Kills = statsList1.ElementAt(0) - numOfKillsAsBot,
                        KillsIncludingBots = statsList1.ElementAt(0),
                        Deaths = statsList1.ElementAt(1) - numOfDeathsAsBot,
                        DeathsIncludingBots = statsList1.ElementAt(1),
                        Headshots = statsList1.ElementAt(2),
                        Assists = statsList1.ElementAt(3) - numOfAssistsAsBot,
                        AssistsIncludingBots = statsList1.ElementAt(3),
                        MVPs = statsList1.ElementAt(4),
                        Shots = statsList1.ElementAt(5),
                        Plants = statsList1.ElementAt(6),
                        Defuses = statsList1.ElementAt(7),
                        Rescues = statsList1.ElementAt(8),
                        TicksAlive = statsList2.ElementAt(0),
                        TicksOnServer = statsList2.ElementAt(1),
                        TicksPlaying = statsList2.ElementAt(2),
                    }
                );
            }

            return playerStats;
        }

        public GeneralroundsStats GetGeneralRoundsStats(
            ProcessedData processedData,
            Dictionary<long, Dictionary<string, string>> playerNames)
        {
            var roundsStats = new List<roundsStats>();

            // winning team & total rounds stats
            IEnumerable<SwitchSidesEventArgs> switchSides = processedData.SwitchSidesValues;
            List<Team> roundsWonTeams = GetRoundsWonTeams(processedData.TeamValues);
            List<RoundEndReason> roundsWonReasons = GetRoundsWonReasons(processedData.RoundEndReasonValues);
            int totalRoundsWonTeamAlpha = 0, totalRoundsWonTeamBeta = 0;

            for (int i = 0; i < roundsWonTeams.Count; i++)
            {
                if (roundsWonReasons.Count > i) // game was abandoned early
                {
                    string reason = string.Empty;
                    string half;
                    bool isOvertime = switchSides.Count() >= 2 && i >= switchSides.ElementAt(1).RoundBeforeSwitch;

                    int overtimeCount = 0;
                    double roundLength = processedData.RoundLengthValues.ElementAt(i);

                    // determines which half / side it is
                    if (isOvertime)
                    {
                        int lastNormalTimeRound = switchSides.ElementAt(1).RoundBeforeSwitch;
                        int roundsPerOTHalf = switchSides.Count() >= 3
                            ? switchSides.ElementAt(2).RoundBeforeSwitch - lastNormalTimeRound
                            : 3; // just assume 3 rounds per OT half if it cannot be checked

                        int roundsPerOT = roundsPerOTHalf * 2;

                        int roundsIntoOT = i + 1 - lastNormalTimeRound;
                        overtimeCount = (int)Math.Ceiling((double)roundsIntoOT / roundsPerOT);

                        int currentOTHalf = (int)Math.Ceiling((double)roundsIntoOT / roundsPerOTHalf);
                        half = currentOTHalf % 2 == 1 ? "First" : "Second";
                    }
                    else
                    {
                        half = switchSides.Any()
                            ? i < switchSides.ElementAt(0).RoundBeforeSwitch ? "First" : "Second"
                            : "First";
                    }

                    // total rounds calculation
                    if (GetIfTeamSwapOrdersAreNormalOrderByHalfAndOvertimeCount(half, overtimeCount))
                    {
                        if (roundsWonTeams.ElementAt(i) is Team.Terrorist)
                            totalRoundsWonTeamAlpha++;
                        else if (roundsWonTeams.ElementAt(i) is Team.CounterTerrorist)
                            totalRoundsWonTeamBeta++;
                    }
                    else
                    {
                        if (roundsWonTeams.ElementAt(i) is Team.Terrorist)
                            totalRoundsWonTeamBeta++;
                        else if (roundsWonTeams.ElementAt(i) is Team.CounterTerrorist)
                            totalRoundsWonTeamAlpha++;
                    }

                    //win method
                    reason = roundsWonReasons[i] switch
                    {
                        RoundEndReason.TerroristsWin => "T Kills",
                        RoundEndReason.CTsWin => "CT Kills",
                        RoundEndReason.TargetBombed => "Bombed",
                        RoundEndReason.BombDefused => "Defused",
                        RoundEndReason.HostagesRescued => "HostagesRescued",
                        RoundEndReason.HostagesNotRescued => "HostagesNotRescued",
                        RoundEndReason.TargetSaved => "TSaved",
                        RoundEndReason.SurvivalWin => "Danger Zone Won",
                        RoundEndReason.Unknown => "Unknown",
                        _ => reason,
                    };

                    // team count values
                    int roundNum = i + 1;
                    TeamPlayers currentRoundTeams =
                        processedData.TeamPlayersValues.FirstOrDefault(t => t.Round == roundNum);

                    foreach (Player player in currentRoundTeams.Terrorists) // make sure steamID's aren't 0
                    {
                        player.SteamID = player.SteamID == 0
                            ? GetSteamIdByPlayerName(playerNames, player.Name)
                            : player.SteamID;
                    }

                    foreach (Player player in currentRoundTeams.CounterTerrorists) // make sure steamID's aren't 0
                    {
                        player.SteamID = player.SteamID == 0
                            ? GetSteamIdByPlayerName(playerNames, player.Name)
                            : player.SteamID;
                    }

                    int playerCountTeamA = currentRoundTeams != null
                        ? GetIfTeamSwapOrdersAreNormalOrderByHalfAndOvertimeCount(half, overtimeCount)
                            ?
                            currentRoundTeams.Terrorists.Count
                            : currentRoundTeams.CounterTerrorists.Count
                        : 0;

                    int playerCountTeamB = currentRoundTeams != null
                        ? GetIfTeamSwapOrdersAreNormalOrderByHalfAndOvertimeCount(half, overtimeCount)
                            ?
                            currentRoundTeams.CounterTerrorists.Count
                            : currentRoundTeams.Terrorists.Count
                        : 0;

                    // equip values
                    TeamEquipment teamEquipValues = processedData.TeamEquipmentValues.Count() >= i
                        ? processedData.TeamEquipmentValues.ElementAt(i)
                        : null;

                    int equipValueTeamA = teamEquipValues != null
                        ? GetIfTeamSwapOrdersAreNormalOrderByHalfAndOvertimeCount(half, overtimeCount)
                            ?
                            teamEquipValues.TEquipValue
                            : teamEquipValues.CTEquipValue
                        : 0;

                    int equipValueTeamB = teamEquipValues != null
                        ? GetIfTeamSwapOrdersAreNormalOrderByHalfAndOvertimeCount(half, overtimeCount)
                            ?
                            teamEquipValues.CTEquipValue
                            : teamEquipValues.TEquipValue
                        : 0;

                    int expenditureTeamA = teamEquipValues != null
                        ? GetIfTeamSwapOrdersAreNormalOrderByHalfAndOvertimeCount(half, overtimeCount)
                            ?
                            teamEquipValues.TExpenditure
                            : teamEquipValues.CTExpenditure
                        : 0;

                    int expenditureTeamB = teamEquipValues != null
                        ? GetIfTeamSwapOrdersAreNormalOrderByHalfAndOvertimeCount(half, overtimeCount)
                            ?
                            teamEquipValues.CTExpenditure
                            : teamEquipValues.TExpenditure
                        : 0;

                    // bombsite planted/exploded/defused at
                    string bombsite = null;
                    BombPlantedError bombPlantedError = null;

                    BombPlanted bombPlanted =
                        processedData.BombsitePlantValues.FirstOrDefault(p => p.Round == roundNum);

                    BombExploded bombExploded =
                        processedData.BombsiteExplodeValues.FirstOrDefault(p => p.Round == roundNum);

                    BombDefused bombDefused =
                        processedData.BombsiteDefuseValues.FirstOrDefault(p => p.Round == roundNum);

                    if (bombDefused is not null)
                    {
                        bombsite ??= bombDefused.Bombsite is null ? null : bombDefused.Bombsite.ToString();
                    }
                    else if (bombExploded is not null)
                    {
                        bombsite ??= bombExploded.Bombsite is null ? null : bombExploded.Bombsite.ToString();
                    }
                    else if (bombPlanted is not null)
                    {
                        bombsite = bombPlanted.Bombsite.ToString();

                        //check to see if either of the bombsites have bugged out
                        if (bombsite == "?")
                        {
                            bombPlantedError = ValidateBombsite(
                                processedData.BombsitePlantValues,
                                (char)bombPlanted.Bombsite
                            );

                            //update data to ensure that future references to it are also updated
                            processedData.BombsitePlantValues.FirstOrDefault(p => p.Round == roundNum).Bombsite =
                                bombPlantedError.Bombsite;

                            if (processedData.BombsiteExplodeValues.FirstOrDefault(p => p.Round == roundNum) != null)
                                processedData.BombsiteExplodeValues.FirstOrDefault(p => p.Round == roundNum).Bombsite =
                                    bombPlantedError.Bombsite;

                            if (processedData.BombsiteDefuseValues.FirstOrDefault(p => p.Round == roundNum) != null)
                                processedData.BombsiteDefuseValues.FirstOrDefault(p => p.Round == roundNum).Bombsite =
                                    bombPlantedError.Bombsite;

                            bombsite = bombPlantedError.Bombsite.ToString();
                        }

                        //plant position
                        bombPlanted.XPosition = bombPlanted.Player.LastAlivePosition.X;
                        bombPlanted.YPosition = bombPlanted.Player.LastAlivePosition.Y;
                        bombPlanted.ZPosition = bombPlanted.Player.LastAlivePosition.Z;
                    }

                    var timeInRoundPlanted = bombPlanted?.TimeInRound;
                    var timeInRoundExploded = bombExploded?.TimeInRound;
                    var timeInRoundDefused = bombDefused?.TimeInRound;

                    // hostage picked up/rescued
                    HostagePickedUp hostagePickedUpA = null, hostagePickedUpB = null;
                    HostageRescued hostageRescuedA = null, hostageRescuedB = null;
                    HostagePickedUpError hostageAPickedUpError = null, hostageBPickedUpError = null;

                    if (processedData.HostagePickedUpValues.Any(r => r.Round == roundNum)
                        || processedData.HostageRescueValues.Any(r => r.Round == roundNum))
                    {
                        hostagePickedUpA = processedData.HostagePickedUpValues.FirstOrDefault(
                            r => r.Round == roundNum && r.Hostage == 'A'
                        );

                        hostagePickedUpB = processedData.HostagePickedUpValues.FirstOrDefault(
                            r => r.Round == roundNum && r.Hostage == 'B'
                        );

                        hostageRescuedA = processedData.HostageRescueValues.FirstOrDefault(
                            r => r.Round == roundNum && r.Hostage == 'A'
                        );

                        hostageRescuedB = processedData.HostageRescueValues.FirstOrDefault(
                            r => r.Round == roundNum && r.Hostage == 'B'
                        );

                        if (hostagePickedUpA == null && hostageRescuedA != null)
                        {
                            hostagePickedUpA = GenerateNewHostagePickedUp(hostageRescuedA);

                            hostageAPickedUpError = new HostagePickedUpError
                            {
                                Hostage = hostagePickedUpA.Hostage,
                                HostageIndex = hostagePickedUpA.HostageIndex,
                                ErrorMessage = "Assuming Hostage A was picked up; cannot assume TimeInRound.",
                            };

                            //update data to ensure that future references to it are also updated
                            List<HostagePickedUp> newHostagePickedUpValues =
                                processedData.HostagePickedUpValues.ToList();

                            newHostagePickedUpValues.Add(hostagePickedUpA);
                            processedData.HostagePickedUpValues = newHostagePickedUpValues;
                        }

                        if (hostagePickedUpB == null && hostageRescuedB != null)
                        {
                            hostagePickedUpB = GenerateNewHostagePickedUp(hostageRescuedB);

                            hostageBPickedUpError = new HostagePickedUpError
                            {
                                Hostage = hostagePickedUpB.Hostage,
                                HostageIndex = hostagePickedUpB.HostageIndex,
                                ErrorMessage = "Assuming Hostage B was picked up; cannot assume TimeInRound.",
                            };

                            //update data to ensure that future references to it are also updated
                            List<HostagePickedUp> newHostagePickedUpValues =
                                processedData.HostagePickedUpValues.ToList();

                            newHostagePickedUpValues.Add(hostagePickedUpB);
                            processedData.HostagePickedUpValues = newHostagePickedUpValues;
                        }

                        //rescue position
                        Vector positionRescueA = hostageRescuedA?.Player.LastAlivePosition;
                        if (positionRescueA != null)
                        {
                            hostageRescuedA.XPosition = positionRescueA.X;
                            hostageRescuedA.YPosition = positionRescueA.Y;
                            hostageRescuedA.ZPosition = positionRescueA.Z;
                        }

                        Vector positionRescueB = hostageRescuedB?.Player.LastAlivePosition;
                        if (positionRescueB != null)
                        {
                            hostageRescuedB.XPosition = positionRescueB.X;
                            hostageRescuedB.YPosition = positionRescueB.Y;
                            hostageRescuedB.ZPosition = positionRescueB.Z;
                        }
                    }

                    var timeInRoundRescuedHostageA = hostageRescuedA?.TimeInRound;
                    var timeInRoundRescuedHostageB = hostageRescuedB?.TimeInRound;

                    roundsStats.Add(
                        new roundsStats
                        {
                            Round = i + 1,
                            Half = half,
                            Overtime = overtimeCount,
                            Length = roundLength,
                            Winners = roundsWonTeams[i].ToString(),
                            WinMethod = reason,
                            BombsitePlantedAt = bombsite,
                            BombPlantPositionX = bombPlanted?.XPosition,
                            BombPlantPositionY = bombPlanted?.YPosition,
                            BombPlantPositionZ = bombPlanted?.ZPosition,
                            BombsiteErrorMessage = bombPlantedError?.ErrorMessage,
                            PickedUpHostageA = hostagePickedUpA != null,
                            PickedUpHostageB = hostagePickedUpB != null,
                            PickedUpAllHostages = hostagePickedUpA != null && hostagePickedUpB != null,
                            HostageAPickedUpErrorMessage = hostageAPickedUpError?.ErrorMessage,
                            HostageBPickedUpErrorMessage = hostageBPickedUpError?.ErrorMessage,
                            RescuedHostageA = hostageRescuedA != null,
                            RescuedHostageB = hostageRescuedB != null,
                            RescuedAllHostages = hostageRescuedA != null && hostageRescuedB != null,
                            RescuedHostageAPositionX = hostageRescuedA?.XPosition,
                            RescuedHostageAPositionY = hostageRescuedA?.YPosition,
                            RescuedHostageAPositionZ = hostageRescuedA?.ZPosition,
                            RescuedHostageBPositionX = hostageRescuedB?.XPosition,
                            RescuedHostageBPositionY = hostageRescuedB?.YPosition,
                            RescuedHostageBPositionZ = hostageRescuedB?.ZPosition,
                            TimeInRoundPlanted = timeInRoundPlanted,
                            TimeInRoundExploded =
                                timeInRoundExploded, // for danger zone, this should be the first bomb that explodes
                            TimeInRoundDefused = timeInRoundDefused,
                            TimeInRoundRescuedHostageA = timeInRoundRescuedHostageA,
                            TimeInRoundRescuedHostageB = timeInRoundRescuedHostageB,
                            TeamAlphaPlayerCount = playerCountTeamA,
                            TeamBetaPlayerCount = playerCountTeamB,
                            TeamAlphaEquipValue = equipValueTeamA,
                            TeamBetaEquipValue = equipValueTeamB,
                            TeamAlphaExpenditure = expenditureTeamA,
                            TeamBetaExpenditure = expenditureTeamB,
                        }
                    );
                }
            }

            // work out winning team
            string winningTeam = totalRoundsWonTeamAlpha >= totalRoundsWonTeamBeta
                ? totalRoundsWonTeamAlpha > totalRoundsWonTeamBeta ? "Team Alpha" : "Draw"
                : "Team Bravo";

            // winners stats
            var winnersStats = new winnersStats
            {
                WinningTeam = winningTeam,
                TeamAlphaRounds = totalRoundsWonTeamAlpha,
                TeamBetaRounds = totalRoundsWonTeamBeta,
            };

            return new GeneralroundsStats
            {
                roundsStats = roundsStats,
                winnersStats = winnersStats,
                SwitchSides = switchSides,
            };
        }

        public static List<bombsiteStats> GetBombsiteStats(ProcessedData processedData)
        {
            BoundingBox bombsiteATrigger = dp?.Triggers.GetValueOrDefault(dp.bombsiteAIndex);
            BoundingBox bombsiteBTrigger = dp?.Triggers.GetValueOrDefault(dp.bombsiteBIndex);

            return new()
            {
                new()
                {
                    Bombsite = 'A',
                    Plants = processedData.BombsitePlantValues.Count(plant => plant.Bombsite == 'A'),
                    Explosions = processedData.BombsiteExplodeValues.Count(explosion => explosion.Bombsite == 'A'),
                    Defuses = processedData.BombsiteDefuseValues.Count(defuse => defuse.Bombsite == 'A'),
                    XPositionMin = bombsiteATrigger?.Min.X,
                    YPositionMin = bombsiteATrigger?.Min.Y,
                    ZPositionMin = bombsiteATrigger?.Min.Z,
                    XPositionMax = bombsiteATrigger?.Max.X,
                    YPositionMax = bombsiteATrigger?.Max.Y,
                    ZPositionMax = bombsiteATrigger?.Max.Z,
                },
                new()
                {
                    Bombsite = 'B',
                    Plants = processedData.BombsitePlantValues.Count(plant => plant.Bombsite == 'B'),
                    Explosions = processedData.BombsiteExplodeValues.Count(explosion => explosion.Bombsite == 'B'),
                    Defuses = processedData.BombsiteDefuseValues.Count(defuse => defuse.Bombsite == 'B'),
                    XPositionMin = bombsiteBTrigger?.Min.X,
                    YPositionMin = bombsiteBTrigger?.Min.Y,
                    ZPositionMin = bombsiteBTrigger?.Min.Z,
                    XPositionMax = bombsiteBTrigger?.Max.X,
                    YPositionMax = bombsiteBTrigger?.Max.Y,
                    ZPositionMax = bombsiteBTrigger?.Max.Z,
                },
            };
        }

        public static List<hostageStats> GetHostageStats(ProcessedData processedData)
        {
            return new()
            {
                new()
                {
                    Hostage = 'A',
                    HostageIndex =
                        processedData.HostageRescueValues.FirstOrDefault(r => r.Hostage == 'A')?.HostageIndex,
                    PickedUps = processedData.HostagePickedUpValues.Count(pickup => pickup.Hostage == 'A'),
                    Rescues = processedData.HostageRescueValues.Count(rescue => rescue.Hostage == 'A'),
                },
                new()
                {
                    Hostage = 'B',
                    HostageIndex =
                        processedData.HostageRescueValues.FirstOrDefault(r => r.Hostage == 'B')?.HostageIndex,
                    PickedUps = processedData.HostagePickedUpValues.Count(pickup => pickup.Hostage == 'B'),
                    Rescues = processedData.HostageRescueValues.Count(rescue => rescue.Hostage == 'B'),
                },
            };
        }

        public static List<rescueZoneStats> GetRescueZoneStats()
        {
            var rescueZoneStats = new List<rescueZoneStats>();

            if (dp is null)
                return rescueZoneStats;

            foreach ((int entityId, BoundingBox rescueZone) in dp.Triggers)
            {
                if (entityId == dp.bombsiteAIndex || entityId == dp.bombsiteBIndex)
                    continue;

                rescueZoneStats.Add(
                    new rescueZoneStats
                    {
                        XPositionMin = rescueZone.Min.X,
                        YPositionMin = rescueZone.Min.Y,
                        ZPositionMin = rescueZone.Min.Z,
                        XPositionMax = rescueZone.Max.X,
                        YPositionMax = rescueZone.Max.Y,
                        ZPositionMax = rescueZone.Max.Z,
                    }
                );
            }

            return rescueZoneStats;
        }

        public static List<grenadesTotalStats> GetGrenadesTotalStats(
            Dictionary<EquipmentElement, List<NadeEventArgs>> nadeGroups)
        {
            var grenadesTotalStats = new List<grenadesTotalStats>(nadeGroups.Count);

            foreach ((EquipmentElement nadeType, List<NadeEventArgs> events) in nadeGroups)
            {
                grenadesTotalStats.Add(
                    new grenadesTotalStats
                    {
                        NadeType = nadeType.ToString(),
                        AmountUsed = events.Count,
                    }
                );
            }

            return grenadesTotalStats;
        }

        public static List<grenadesSpecificStats> GetGrenadesSpecificStats(
            Dictionary<EquipmentElement, List<NadeEventArgs>> nadeGroups,
            Dictionary<long, Dictionary<string, string>> playerNames)
        {
            var grenadesSpecificStats = new List<grenadesSpecificStats>(nadeGroups.Count);

            foreach ((EquipmentElement nadeType, List<NadeEventArgs> events) in nadeGroups)
            {
                foreach (NadeEventArgs nade in events)
                {
                    // Retrieve Steam ID using player name if the event does not return it correctly.
                    long steamId = nade.ThrownBy.SteamID == 0
                        ? GetSteamIdByPlayerName(playerNames, nade.ThrownBy.Name)
                        : nade.ThrownBy.SteamID;

                    var stats = new grenadesSpecificStats
                    {
                        NadeType = nade.NadeType.ToString(),
                        SteamID = steamId,
                        XPosition = nade.Position.X,
                        YPosition = nade.Position.Y,
                        ZPosition = nade.Position.Z,
                    };

                    if (nadeType is EquipmentElement.Flash)
                    {
                        var flash = nade as FlashEventArgs;
                        stats.NumPlayersFlashed = flash.FlashedPlayers.Length;
                    }

                    grenadesSpecificStats.Add(stats);
                }
            }

            return grenadesSpecificStats;
        }

        public static List<killsStats> GetKillsStats(
            ProcessedData processedData,
            Dictionary<long, Dictionary<string, string>> playerNames)
        {
            var killsStats = new List<killsStats>();

            var kills = new List<Player>(processedData.PlayerValues["Kills"].ToList());
            var deaths = new List<Player>(processedData.PlayerValues["Deaths"].ToList());

            var weaponKillers = new List<Equipment>(processedData.WeaponValues.ToList());
            var penetrations = new List<int>(processedData.PenetrationValues.ToList());

            for (int i = 0; i < deaths.Count; i++)
            {
                if (kills.ElementAt(i) != null && kills.ElementAt(i).LastAlivePosition != null
                    && deaths.ElementAt(i) != null && deaths.ElementAt(i).LastAlivePosition != null)
                {
                    PlayerKilledEventArgs playerKilledEvent = processedData.PlayerKilledEventsValues.ElementAt(i);

                    if (playerKilledEvent != null)
                    {
                        int round = playerKilledEvent.Round;

                        Vector killPosition = kills.ElementAt(i).LastAlivePosition;
                        Vector deathPosition= deaths.ElementAt(i).LastAlivePosition;

                        //retrieve steam ID using player name if the event does not return it correctly
                        long killerSteamId = kills.ElementAt(i) != null
                            ? kills.ElementAt(i).SteamID == 0
                                ?
                                GetSteamIdByPlayerName(playerNames, kills.ElementAt(i).Name)
                                : kills.ElementAt(i).SteamID
                            : 0;

                        long victimSteamId = deaths.ElementAt(i) != null
                            ? deaths.ElementAt(i).SteamID == 0
                                ?
                                GetSteamIdByPlayerName(playerNames, deaths.ElementAt(i).Name)
                                : deaths.ElementAt(i).SteamID
                            : 0;

                        long assisterSteamId = playerKilledEvent.Assister != null
                            ? playerKilledEvent.Assister.SteamID == 0
                                ?
                                GetSteamIdByPlayerName(playerNames, playerKilledEvent.Assister.Name)
                                : playerKilledEvent.Assister.SteamID
                            : 0;

                        var weaponUsed = weaponKillers.ElementAt(i).Weapon.ToString();
                        var weaponUsedClass = weaponKillers.ElementAt(i).Class.ToString();
                        var weaponUsedType = weaponKillers.ElementAt(i).SubclassName;
                        var numOfPenetrations = penetrations.ElementAt(i);

                        if (string.IsNullOrEmpty(weaponUsed))
                        {
                            weaponUsed = weaponKillers.ElementAt(i).OriginalString;
                            weaponUsedClass = "Unknown";
                            weaponUsedType = "Unknown";
                        }

                        bool firstKillOfTheRound = !killsStats.Any(k => k.Round == round && k.FirstKillOfTheRound);

                        killsStats.Add(
                            new killsStats
                            {
                                Round = round,
                                TimeInRound = playerKilledEvent.TimeInRound,
                                Weapon = weaponUsed,
                                WeaponClass = weaponUsedClass,
                                WeaponType = weaponUsedType,
                                KillerSteamID = killerSteamId,
                                KillerBotTakeover = playerKilledEvent.KillerBotTakeover,
                                XPositionKill = killPosition.X,
                                YPositionKill = killPosition.Y,
                                ZPositionKill = killPosition.Z,
                                VictimSteamID = victimSteamId,
                                VictimBotTakeover = playerKilledEvent.VictimBotTakeover,
                                XPositionDeath = deathPosition.X,
                                YPositionDeath = deathPosition.Y,
                                ZPositionDeath = deathPosition.Z,
                                AssisterSteamID = assisterSteamId,
                                AssisterBotTakeover = playerKilledEvent.AssisterBotTakeover,
                                FirstKillOfTheRound = firstKillOfTheRound,
                                Suicide = playerKilledEvent.Suicide,
                                TeamKill = playerKilledEvent.TeamKill,
                                PenetrationsCount = numOfPenetrations,
                                Headshot = playerKilledEvent.Headshot,
                                AssistedFlash = playerKilledEvent.AssistedFlash,
                            }
                        );
                    }
                }
            }

            return killsStats;
        }

        public static List<FeedbackMessage> GetFeedbackMessages(
            ProcessedData processedData,
            Dictionary<long, Dictionary<string, string>> playerNames)
        {
            var feedbackMessages = new List<FeedbackMessage>();

            foreach (FeedbackMessage message in processedData.MessagesValues)
            {
                TeamPlayers currentRoundTeams =
                    processedData.TeamPlayersValues.FirstOrDefault(t => t.Round == message.Round);

                if (currentRoundTeams != null && (message.SteamID == 0 || message.TeamName == null)
                ) // excludes warmup round
                {
                    // retrieve steam ID using player name if the event does not return it correctly
                    foreach (Player player in currentRoundTeams.Terrorists)
                    {
                        player.SteamID = player.SteamID == 0
                            ? GetSteamIdByPlayerName(playerNames, player.Name)
                            : player.SteamID;
                    }

                    foreach (Player player in currentRoundTeams.CounterTerrorists)
                    {
                        player.SteamID = player.SteamID == 0
                            ? GetSteamIdByPlayerName(playerNames, player.Name)
                            : player.SteamID;
                    }

                    if (currentRoundTeams.Terrorists.Any(p => p.SteamID == message.SteamID))
                        message.TeamName = "Terrorist";
                    else if (currentRoundTeams.CounterTerrorists.Any(p => p.SteamID == message.SteamID))
                        message.TeamName = "CounterTerrorist";
                    else
                        message.TeamName = "Spectator";
                }

                feedbackMessages.Add(message);
            }

            return feedbackMessages;
        }

        public static chickenStats GetChickenStats(ProcessedData processedData)
        {
            return new() { Killed = processedData.ChickenValues.Count() };
        }

        public List<teamStats> GetTeamStats(
            ProcessedData processedData,
            AllStats allStats,
            Dictionary<long, Dictionary<string, string>> playerNames,
            IEnumerable<SwitchSidesEventArgs> switchSides)
        {
            var teamStats = new List<teamStats>();

            int swappedSidesCount = 0;
            int currentRoundChecking = 1;

            foreach (TeamPlayers teamPlayers in processedData.TeamPlayersValues)
            {
                // players in each team per round
                swappedSidesCount = switchSides.Count() > swappedSidesCount
                    ? switchSides.ElementAt(swappedSidesCount).RoundBeforeSwitch == currentRoundChecking - 1
                        ?
                        swappedSidesCount + 1
                        : swappedSidesCount
                    : swappedSidesCount;

                bool firstHalf = swappedSidesCount % 2 == 0;

                TeamPlayers currentRoundTeams =
                    processedData.TeamPlayersValues.FirstOrDefault(t => t.Round == teamPlayers.Round);

                List<Player> alphaPlayers = currentRoundTeams != null
                    ? firstHalf ? currentRoundTeams.Terrorists : currentRoundTeams.CounterTerrorists
                    : null;

                List<Player> bravoPlayers = currentRoundTeams != null
                    ? firstHalf ? currentRoundTeams.CounterTerrorists : currentRoundTeams.Terrorists
                    : null;

                var alphaSteamIds = new List<long>();
                var bravoSteamIds = new List<long>();

                foreach (Player player in alphaPlayers)
                {
                    player.SteamID = player.SteamID == 0
                        ? GetSteamIdByPlayerName(playerNames, player.Name)
                        : player.SteamID;

                    alphaSteamIds.Add(player.SteamID);
                }

                foreach (Player player in bravoPlayers)
                {
                    player.SteamID = player.SteamID == 0
                        ? GetSteamIdByPlayerName(playerNames, player.Name)
                        : player.SteamID;

                    bravoSteamIds.Add(player.SteamID);
                }

                // attempts to remove and stray players that are supposedly on a team, even though they exceed the max players per team and they are not in player lookups
                // (also most likely have a steam ID of 0)
                var alphaSteamIdsToRemove = new List<long>();
                var bravoSteamIdsToRemove = new List<long>();

                if (allStats.mapInfo.TestType.ToLower().Contains("comp") && alphaSteamIds.Count > 5)
                    foreach (var steamId in alphaSteamIds)
                    {
                        if (playerLookups.All(l => l.Value != steamId))
                            alphaSteamIdsToRemove.Add(steamId);
                    }
                else if (allStats.mapInfo.TestType.ToLower().Contains("casual") && alphaSteamIds.Count > 10)
                    foreach (var steamId in alphaSteamIds)
                    {
                        if (playerLookups.All(l => l.Value != steamId))
                            alphaSteamIdsToRemove.Add(steamId);
                    }

                if (allStats.mapInfo.TestType.ToLower().Contains("comp") && bravoSteamIds.Count > 5)
                    foreach (var steamId in bravoSteamIds)
                    {
                        if (playerLookups.All(l => l.Value != steamId))
                            bravoSteamIdsToRemove.Add(steamId);
                    }
                else if (allStats.mapInfo.TestType.ToLower().Contains("casual") && bravoSteamIds.Count > 10)
                    foreach (var steamId in bravoSteamIds)
                    {
                        if (playerLookups.All(l => l.Value != steamId))
                            bravoSteamIdsToRemove.Add(steamId);
                    }

                // remove the steamIDs if necessary
                foreach (var steamId in alphaSteamIdsToRemove)
                    alphaSteamIds.Remove(steamId);

                foreach (var steamId in bravoSteamIdsToRemove)
                    bravoSteamIds.Remove(steamId);

                // kills/death stats this round
                IEnumerable<PlayerKilledEventArgs> deathsThisRound =
                    processedData.PlayerKilledEventsValues.Where(k => k.Round == teamPlayers.Round);

                // kills this round
                int alphaKills =
                    deathsThisRound.Count(d => d.Killer != null && alphaSteamIds.Contains(d.Killer.SteamID));

                int bravoKills =
                    deathsThisRound.Count(d => d.Killer != null && bravoSteamIds.Contains(d.Killer.SteamID));

                // deaths this round
                int alphaDeaths =
                    deathsThisRound.Count(d => d.Victim != null && alphaSteamIds.Contains(d.Victim.SteamID));

                int bravoDeaths =
                    deathsThisRound.Count(d => d.Victim != null && bravoSteamIds.Contains(d.Victim.SteamID));

                // assists this round
                int alphaAssists =
                    deathsThisRound.Count(d => d.Assister != null && alphaSteamIds.Contains(d.Assister.SteamID));

                int bravoAssists =
                    deathsThisRound.Count(d => d.Assister != null && bravoSteamIds.Contains(d.Assister.SteamID));

                // flash assists this round
                int alphaFlashAssists = deathsThisRound.Count(
                    d => d.Assister != null && alphaSteamIds.Contains(d.Assister.SteamID) && d.AssistedFlash
                );

                int bravoFlashAssists = deathsThisRound.Count(
                    d => d.Assister != null && bravoSteamIds.Contains(d.Assister.SteamID) && d.AssistedFlash
                );

                // headshots this round
                int alphaHeadshots = deathsThisRound.Count(
                    d => d.Killer != null && alphaSteamIds.Contains(d.Killer.SteamID) && d.Headshot
                );

                int bravoHeadshots = deathsThisRound.Count(
                    d => d.Killer != null && bravoSteamIds.Contains(d.Killer.SteamID) && d.Headshot
                );

                // team kills this round
                int alphaTeamkills = deathsThisRound.Count(
                    d => d.Killer != null && d.Victim != null && alphaSteamIds.Contains(d.Killer.SteamID)
                        && alphaSteamIds.Contains(d.Victim.SteamID) && d.Killer.SteamID != d.Victim.SteamID
                );

                int bravoTeamkills = deathsThisRound.Count(
                    d => d.Killer != null && d.Victim != null && bravoSteamIds.Contains(d.Killer.SteamID)
                        && bravoSteamIds.Contains(d.Victim.SteamID) && d.Killer.SteamID != d.Victim.SteamID
                );

                // suicides this round
                int alphaSuicides = deathsThisRound.Count(
                    d => d.Killer != null && d.Victim != null && alphaSteamIds.Contains(d.Killer.SteamID)
                        && d.Killer.SteamID != 0 && d.Suicide
                );

                int bravoSuicides = deathsThisRound.Count(
                    d => d.Killer != null && d.Victim != null && bravoSteamIds.Contains(d.Killer.SteamID)
                        && d.Killer.SteamID != 0 && d.Suicide
                );

                // wallbang kills this round
                int alphaWallbangKills = deathsThisRound.Count(
                    d => d.Killer != null && alphaSteamIds.Contains(d.Killer.SteamID) && d.PenetratedObjects > 0
                );

                int bravoWallbangKills = deathsThisRound.Count(
                    d => d.Killer != null && bravoSteamIds.Contains(d.Killer.SteamID) && d.PenetratedObjects > 0
                );

                // total number of walls penetrated through for kills this round
                int alphaWallbangsTotalForAllKills = deathsThisRound
                    .Where(d => d.Killer != null && alphaSteamIds.Contains(d.Killer.SteamID))
                    .Select(d => d.PenetratedObjects).DefaultIfEmpty().Sum();

                int bravoWallbangsTotalForAllKills = deathsThisRound
                    .Where(d => d.Killer != null && bravoSteamIds.Contains(d.Killer.SteamID))
                    .Select(d => d.PenetratedObjects).DefaultIfEmpty().Sum();

                // most number of walls penetrated through in a single kill this round
                int alphaWallbangsMostInOneKill = deathsThisRound
                    .Where(d => d.Killer != null && alphaSteamIds.Contains(d.Killer.SteamID))
                    .Select(d => d.PenetratedObjects).DefaultIfEmpty().Max();

                int bravoWallbangsMostInOneKill = deathsThisRound
                    .Where(d => d.Killer != null && bravoSteamIds.Contains(d.Killer.SteamID))
                    .Select(d => d.PenetratedObjects).DefaultIfEmpty().Max();

                // shots fired this round
                IEnumerable<ShotFired> shotsFiredThisRound =
                    processedData.ShotsFiredValues.Where(s => s.Round == teamPlayers.Round);

                int alphaShotsFired =
                    shotsFiredThisRound.Count(s => s.Shooter != null && alphaSteamIds.Contains(s.Shooter.SteamID));

                int bravoShotsFired =
                    shotsFiredThisRound.Count(s => s.Shooter != null && bravoSteamIds.Contains(s.Shooter.SteamID));

                teamStats.Add(
                    new teamStats
                    {
                        Round = teamPlayers.Round,
                        TeamAlpha = alphaSteamIds,
                        TeamAlphaKills = alphaKills - (alphaTeamkills + alphaSuicides),
                        TeamAlphaDeaths = alphaDeaths,
                        TeamAlphaAssists = alphaAssists,
                        TeamAlphaFlashAssists = alphaFlashAssists,
                        TeamAlphaHeadshots = alphaHeadshots,
                        TeamAlphaTeamkills = alphaTeamkills,
                        TeamAlphaSuicides = alphaSuicides,
                        TeamAlphaWallbangKills = alphaWallbangKills,
                        TeamAlphaWallbangsTotalForAllKills = alphaWallbangsTotalForAllKills,
                        TeamAlphaWallbangsMostInOneKill = alphaWallbangsMostInOneKill,
                        TeamAlphaShotsFired = alphaShotsFired,
                        TeamBravo = bravoSteamIds,
                        TeamBravoKills = bravoKills - (bravoTeamkills + bravoSuicides),
                        TeamBravoDeaths = bravoDeaths,
                        TeamBravoAssists = bravoAssists,
                        TeamBravoFlashAssists = bravoFlashAssists,
                        TeamBravoHeadshots = bravoHeadshots,
                        TeamBravoTeamkills = bravoTeamkills,
                        TeamBravoSuicides = bravoSuicides,
                        TeamBravoWallbangKills = bravoWallbangKills,
                        TeamBravoWallbangsTotalForAllKills = bravoWallbangsTotalForAllKills,
                        TeamBravoWallbangsMostInOneKill = bravoWallbangsMostInOneKill,
                        TeamBravoShotsFired = bravoShotsFired,
                    }
                );

                currentRoundChecking++;
            }

            return teamStats;
        }

        public static List<firstDamageStats> GetFirstDamageStats(ProcessedData processedData)
        {
            var firstDamageStats = new List<firstDamageStats>();

            foreach (var round in processedData.PlayerHurtValues.Select(x => x.Round).Distinct())
            {
                firstDamageStats.Add(
                    new firstDamageStats
                    {
                        Round = round,
                        FirstDamageToEnemyByPlayers = new List<DamageGivenByPlayerInRound>(),
                    }
                );
            }

            foreach (IGrouping<int, PlayerHurt> roundsGroup in processedData.PlayerHurtValues.GroupBy(x => x.Round))
            {
                int lastRound = processedData.RoundEndReasonValues.Count();

                foreach (var round in roundsGroup.Where(x => x.Round > 0 && x.Round <= lastRound).Select(x => x.Round)
                    .Distinct())
                {
                    foreach (IGrouping<long, PlayerHurt> steamIdsGroup in roundsGroup.Where(
                        x => x.Round == round && x.Player?.SteamID != 0 && x.Player?.SteamID != x.Attacker?.SteamID
                            && x.Weapon.Class != EquipmentClass.Grenade && x.Weapon.Class != EquipmentClass.Equipment
                            && x.Weapon.Class != EquipmentClass.Unknown && x.Weapon.Weapon != EquipmentElement.Unknown
                            && x.Weapon.Weapon != EquipmentElement.Bomb && x.Weapon.Weapon != EquipmentElement.World
                    ).OrderBy(x => x.TimeInRound).GroupBy(x => x.Attacker.SteamID))
                    {
                        PlayerHurt firstDamage = steamIdsGroup.FirstOrDefault();

                        var firstDamageByPlayer = new DamageGivenByPlayerInRound
                        {
                            TimeInRound = firstDamage.TimeInRound,
                            TeamSideShooter = firstDamage.Attacker.Team.ToString(),
                            SteamIDShooter = firstDamage.Attacker.SteamID,
                            XPositionShooter = firstDamage.XPositionAttacker,
                            YPositionShooter = firstDamage.YPositionAttacker,
                            ZPositionShooter = firstDamage.ZPositionAttacker,
                            TeamSideVictim = firstDamage.Player.Team.ToString(),
                            SteamIDVictim = firstDamage.Player.SteamID,
                            XPositionVictim = firstDamage.XPositionPlayer,
                            YPositionVictim = firstDamage.YPositionPlayer,
                            ZPositionVictim = firstDamage.ZPositionPlayer,
                            Weapon = firstDamage.Weapon.Weapon.ToString(),
                            WeaponClass = firstDamage.Weapon.Class.ToString(),
                            WeaponType = firstDamage.Weapon.SubclassName,
                        };

                        firstDamageStats[round - 1].FirstDamageToEnemyByPlayers.Add(firstDamageByPlayer);
                    }
                }
            }

            return firstDamageStats;
        }

        public static PlayerPositionsStats GetPlayerPositionsStats(ProcessedData processedData, AllStats allStats)
        {
            var playerPositionByRound = new List<PlayerPositionByRound>();

            // create playerPositionByRound with empty PlayerPositionByTimeInRound
            foreach (IGrouping<int, PlayerPositionsInstance> roundsGroup in processedData.PlayerPositionsValues.GroupBy(
                x => x.Round
            ))
            {
                int lastRound = processedData.RoundEndReasonValues.Count();

                foreach (var round in roundsGroup.Where(x => x.Round > 0 && x.Round <= lastRound).Select(x => x.Round)
                    .Distinct())
                {
                    playerPositionByRound.Add(
                        new PlayerPositionByRound
                        {
                            Round = round,
                            PlayerPositionByTimeInRound = new List<PlayerPositionByTimeInRound>(),
                        }
                    );
                }
            }

            //create PlayerPositionByTimeInRound with empty PlayerPositionBySteamId
            foreach (PlayerPositionByRound playerPositionsStat in playerPositionByRound)
            {
                foreach (IGrouping<int, PlayerPositionsInstance> timeInRoundsGroup in processedData
                    .PlayerPositionsValues.Where(x => x.Round == playerPositionsStat.Round).GroupBy(x => x.TimeInRound))
                {
                    foreach (var timeInRound in timeInRoundsGroup.Select(x => x.TimeInRound).Distinct())
                    {
                        playerPositionsStat.PlayerPositionByTimeInRound.Add(
                            new PlayerPositionByTimeInRound
                            {
                                TimeInRound = timeInRound,
                                PlayerPositionBySteamID = new List<PlayerPositionBySteamID>(),
                            }
                        );
                    }
                }
            }

            //create PlayerPositionBySteamId
            foreach (PlayerPositionByRound playerPositionsStat in playerPositionByRound)
            {
                foreach (PlayerPositionByTimeInRound playerPositionByTimeInRound in playerPositionsStat
                    .PlayerPositionByTimeInRound)
                {
                    foreach (IGrouping<long, PlayerPositionsInstance> steamIdsGroup in processedData
                        .PlayerPositionsValues
                        .Where(
                            x => x.Round == playerPositionsStat.Round
                                && x.TimeInRound == playerPositionByTimeInRound.TimeInRound
                        ).GroupBy(x => x.SteamID).Distinct())
                    {
                        foreach (PlayerPositionsInstance playerPositionsInstance in steamIdsGroup)
                        {
                            // skip players who have died this round
                            if (!processedData.PlayerKilledEventsValues.Any(
                                x => x.Round == playerPositionsStat.Round && x.Victim?.SteamID != 0
                                    && x.Victim.SteamID == playerPositionsInstance.SteamID
                                    && x.TimeInRound <= playerPositionByTimeInRound.TimeInRound
                            ))
                                playerPositionByTimeInRound.PlayerPositionBySteamID.Add(
                                    new PlayerPositionBySteamID
                                    {
                                        SteamID = playerPositionsInstance.SteamID,
                                        TeamSide = playerPositionsInstance.TeamSide,
                                        XPosition = (int)playerPositionsInstance.XPosition,
                                        YPosition = (int)playerPositionsInstance.YPosition,
                                        ZPosition = (int)playerPositionsInstance.ZPosition,
                                    }
                                );
                        }
                    }
                }
            }

            var playerPositionsStats = new PlayerPositionsStats
            {
                DemoName = allStats.mapInfo.DemoName,
                PlayerPositionByRound = playerPositionByRound,
            };

            return playerPositionsStats;
        }

        public static string GetOutputPathWithoutExtension(
            string outputRoot,
            List<string> foldersToProcess,
            DemoInformation demoInfo,
            string mapName,
            bool sameFileName,
            bool sameFolderStructure)
        {
            string filename = sameFileName
                ? Path.GetFileNameWithoutExtension(demoInfo.DemoName)
                : Guid.NewGuid().ToString();

            string mapDateString = demoInfo.TestDate is null
                ? string.Empty
                : demoInfo.TestDate.Replace('/', '_');

            string path = string.Empty;

            if (foldersToProcess.Count > 0 && sameFolderStructure)
                foreach (var folder in foldersToProcess)
                {
                    string[] splitPath = Path.GetDirectoryName(demoInfo.DemoName).Split(
                        new[] { string.Concat(folder, "\\") },
                        StringSplitOptions.None
                    );

                    path = splitPath.Length > 1
                        ? string.Concat(outputRoot, "\\", splitPath.LastOrDefault(), "\\")
                        : string.Concat(outputRoot, "\\");

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        if (!Directory.Exists(path))
                            Directory.CreateDirectory(path);

                        break;
                    }
                }
            else
                path = string.Concat(outputRoot, "\\");

            if (mapDateString != string.Empty)
                path += mapDateString + "_";

            path += mapName + "_" + filename;

            return path;
        }

        public static void WriteJson(object stats, string path)
        {
            try
            {
                using var sw = new StreamWriter(path, false);
                string json = JsonConvert.SerializeObject(stats, Formatting.Indented);
                sw.WriteLine(json);
            }
            catch (Exception)
            {
                Console.WriteLine("Could not create json file.");
                Console.WriteLine(string.Concat("Filename: ", path));
            }
        }

        public static long GetSteamIdByPlayerName(Dictionary<long, Dictionary<string, string>> playerNames, string name)
        {
            if (name == "unconnected") return 0;

            var steamId = playerNames.Where(p => p.Value.Values.ElementAt(0) == name).Select(p => p.Key)
                .FirstOrDefault(); // steamID will be 0 if not found

            return steamId;
        }

        public List<object> GetEvents<T>()
        {
            Type t = typeof(T);

            return events.ContainsKey(t) ? events[t] : new List<object>();
        }

        public static List<Team> GetRoundsWonTeams(IEnumerable<Team> teamValues)
        {
            List<Team> roundsWonTeams = teamValues.ToList();
            roundsWonTeams.RemoveAll(
                team => team is not Team.Terrorist && team is not Team.CounterTerrorist && team is not Team.Unknown
            );

            return roundsWonTeams;
        }

        public static List<RoundEndReason> GetRoundsWonReasons(IEnumerable<RoundEndReason> roundEndReasonValues)
        {
            List<RoundEndReason> roundsWonReasons = roundEndReasonValues.ToList();
            roundsWonReasons.RemoveAll(
                reason => reason is not RoundEndReason.TerroristsWin && reason is not RoundEndReason.CTsWin
                    && reason is not RoundEndReason.TargetBombed && reason is not RoundEndReason.BombDefused
                    && reason is not RoundEndReason.HostagesRescued && reason is not RoundEndReason.HostagesNotRescued
                    && reason is not RoundEndReason.TargetSaved && reason is not RoundEndReason.SurvivalWin
                    && reason is not RoundEndReason.Unknown
            );

            return roundsWonReasons;
        }

        public static int GetCurrentRoundNum(MatchData md, GameMode gameMode)
        {
            int roundsCount = md.GetEvents<RoundOfficiallyEndedEventArgs>().Count;
            List<TeamPlayers> teamPlayersList = md.GetEvents<TeamPlayers>().Cast<TeamPlayers>().ToList();

            int round = 0;

            if (teamPlayersList.Count > 0 && teamPlayersList.Any(t => t.Round == 1))
            {
                TeamPlayers teamPlayers = teamPlayersList.First(t => t.Round == 1);

                if (teamPlayers.Terrorists.Count > 0 && teamPlayers.CounterTerrorists.Count > 0)
                    round = roundsCount + 1;
            }

            // add 1 for roundsCount when in danger zone
            if (gameMode is GameMode.DangerZone)
                round++;

            return round;
        }

        public static bool CheckIfPlayerAliveAtThisPointInRound(MatchData md, Player player, int round)
        {
            IEnumerable<PlayerKilledEventArgs> kills = md.events
                .Where(k => k.Key.Name.ToString() == "PlayerKilledEventArgs")
                .Select(v => (PlayerKilledEventArgs)v.Value.ElementAt(0));

            return !kills.Any(x => x.Round == round && x.Victim?.SteamID != 0 && x.Victim.SteamID == player?.SteamID);
        }

        public int CheckForUpdatedUserId(int userId)
        {
            int newUserId = playerReplacements.Where(u => u.Key == userId).Select(u => u.Value).FirstOrDefault();

            return newUserId == 0 ? userId : newUserId;
        }

        public static string GenerateSetPosCommand(Player player)
        {
            if (player is null)
                return "";

            // Z axis for setang is optional.
            return $"setpos {player.Position.X} {player.Position.Y} {player.Position.Z}; "
                + $"setang {player.ViewDirectionX} {player.ViewDirectionY}";
        }

        public static bool IsMessageFeedback(string text)
        {
            return text.ToLower().StartsWith(">fb") || text.ToLower().StartsWith(">feedback")
                || text.ToLower().StartsWith("!fb") || text.ToLower().StartsWith("!feedback");
        }

        public BombPlantedError ValidateBombsite(IEnumerable<BombPlanted> bombPlantedArray, char bombsite)
        {
            char validatedBombsite = bombsite;
            string errorMessage = null;

            if (bombsite == '?')
            {
                if (bombPlantedArray.Any(x => x.Bombsite == 'A')
                    && (!bombPlantedArray.Any(x => x.Bombsite == 'B') || changingPlantedRoundsToB))
                {
                    //assume B site trigger's bounding box is broken
                    changingPlantedRoundsToB = true;
                    validatedBombsite = 'B';
                    errorMessage = "Assuming plant was at B site.";
                }
                else if (!bombPlantedArray.Any(x => x.Bombsite == 'A')
                    && (bombPlantedArray.Any(x => x.Bombsite == 'B') || changingPlantedRoundsToA))
                {
                    //assume A site trigger's bounding box is broken
                    changingPlantedRoundsToA = true;
                    validatedBombsite = 'A';
                    errorMessage = "Assuming plant was at A site.";
                }
                else
                {
                    //both bombsites are having issues
                    //may be an issue with instances?
                    errorMessage = "Couldn't assume either bombsite was the plant location.";
                }
            }

            return new BombPlantedError
            {
                Bombsite = validatedBombsite,
                ErrorMessage = errorMessage,
            };
        }

        public static HostagePickedUp GenerateNewHostagePickedUp(HostageRescued hostageRescued)
        {
            return new()
            {
                Hostage = hostageRescued.Hostage,
                HostageIndex = hostageRescued.HostageIndex,
                Player = new Player(hostageRescued.Player),
                Round = hostageRescued.Round,
                TimeInRound = -1,
            };
        }

        public static bool GetIfTeamSwapOrdersAreNormalOrderByHalfAndOvertimeCount(string half, int overtimeCount)
        {
            return half == "First" && overtimeCount % 2 == 0
                || half == "Second"
                && overtimeCount % 2
                == 1; // the team playing T Side first switches each OT for example, this checks the OT count for swaps
        }

        public static int? GetMinRoundsForWin(GameMode gameMode, TestType testType)
        {
            switch (gameMode, testType)
            {
                case (GameMode.WingmanDefuse, TestType.Casual):
                case (GameMode.WingmanDefuse, TestType.Competitive):
                case (GameMode.WingmanHostage, TestType.Casual):
                case (GameMode.WingmanHostage, TestType.Competitive):
                    return 9;
                case (GameMode.Defuse, TestType.Casual):
                case (GameMode.Hostage, TestType.Casual):
                case (GameMode.Unknown, TestType.Casual):
                    // assumes that it is a classic match. Would be better giving the -gamemodeoverride parameter to get
                    // around this as it cannot figure out the game mode
                    return 11;
                case (GameMode.Defuse, TestType.Competitive):
                case (GameMode.Hostage, TestType.Competitive):
                case (GameMode.Unknown, TestType.Competitive):
                    // assumes that it is a classic match. Would be better giving the -gamemodeoverride parameter to get
                    // around this as it cannot figure out the game mode
                    return 16;
                case (GameMode.DangerZone, TestType.Casual):
                case (GameMode.DangerZone, TestType.Competitive):
                    return 2;
                default:
                    return null;
            }
        }

        private static bool CheckIfStatsShouldBeCreated(string typeName, GameMode gameMode)
        {
            switch (typeName.ToLower())
            {
                case "tanookiStats":
                case "winnersstats":
                case "bombsitestats":
                case "hostagestats":
                case "teamstats":
                    return gameMode is not GameMode.DangerZone;
                default:
                    return true;
            }
        }
    }
}
