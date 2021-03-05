﻿namespace SourceEngine.Demo.Stats.Models
{
    public class PlayerPositionsInstance
    {
        public PlayerPositionsInstance() { }

        public int Round { get; set; }

        public int TimeInRound { get; set; }

        public string TeamSide { get; set; }

        public long SteamID { get; set; }

        public double XPosition { get; set; }

        public double YPosition { get; set; }

        public double ZPosition { get; set; }
    }
}
