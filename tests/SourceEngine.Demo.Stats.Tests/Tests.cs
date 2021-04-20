﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Shouldly;

using SourceEngine.Demo.Parser;
using SourceEngine.Demo.Parser.Entities;
using SourceEngine.Demo.Parser.Structs;
using SourceEngine.Demo.Stats.Models;

using Xunit;

namespace SourceEngine.Demo.Stats.Tests
{
    public class TopStatsWaffleTests : IDisposable
    {
        protected Stream Stream;
        protected Processor Processor;
        protected DemoParser DemoParser;
        protected CollectedData CollectedData;

        public TopStatsWaffleTests()
        {
            MockData();

            // Simulate BindPlayer(), but without any of the complicated duplicate logic
            // (since there are no duplicates in the mock data)
            foreach (TeamPlayers teamPlayers in CollectedData.TeamPlayersValues)
            {
                foreach (Player player in teamPlayers.Terrorists.Concat(teamPlayers.CounterTerrorists))
                {
                    CollectedData.PlayerTicks[player.UserID] = new TickCounter { PlayerName = player.Name };
                    CollectedData.PlayerLookups[player.UserID] = player.SteamID;
                }
            }
        }

        public void MockData()
        {
            var DemoInformation = new DemoInformation
            {
                DemoName = "demo1",
                MapName = "de_testmap",
                GameMode = GameMode.Defuse,
                TestType = TestType.Casual,
                TestDate = new DateTime(2020, 1, 1, 0, 0, 0).ToString(),
            };

            Stream = new MemoryStream();
            DemoParser = new DemoParser(Stream);

            var tanookiStats = new tanookiStats
            {
                Joined = true,
                Left = true,
                RoundJoined = 1,
                RoundLeft = 2,
                RoundsLasted = 1,
            };

            var MatchStartValues = new List<MatchStartedEventArgs>
            {
                new()
                {
                    Mapname = "de_testmap",
                    HasBombsites = true,
                },
            };

            var SwitchSidesValues = new List<SwitchSidesEventArgs>
            {
                new()
                {
                    RoundBeforeSwitch = 1,
                },
            };

            var MessagesValues = new List<FeedbackMessage>
            {
                new()
                {
                    Round = 1,
                    SteamID = 12321313213,
                    TeamName = "AlphaTeam",
                    XCurrentPosition = 50,
                    YCurrentPosition = 60,
                    ZCurrentPosition = 70,
                    XLastAlivePosition = 120,
                    YLastAlivePosition = 130,
                    ZLastAlivePosition = 140,
                    XCurrentViewAngle = 45.0f,
                    YCurrentViewAngle = 225.0f,
                    SetPosCommandCurrentPosition = "setpos 50 60 70; setang 45 225",
                    Message = "bad map",
                    TimeInRound = 31.7568,
                },
            };

            var TeamPlayersValues = new List<TeamPlayers>
            {
                new()
                {
                    Round = 1,
                    Terrorists = new List<Player>
                    {
                        new()
                        {
                            Name = "JimWood",
                            SteamID = 32443298432,
                            Team = Team.Terrorist,
                            EntityID = 45,
                            UserID = 1,
                            LastAlivePosition = new Vector
                            {
                                X = 100,
                                Y = 100,
                                Z = 100,
                            },
                            Position = new Vector
                            {
                                X = 200,
                                Y = 200,
                                Z = 200,
                            },
                            Money = 200,
                            RoundStartEquipmentValue = 2700,
                        },
                    },
                    CounterTerrorists = new List<Player>
                    {
                        new()
                        {
                            Name = "TheWhaleMan",
                            SteamID = 12321313213,
                            Team = Team.CounterTerrorist,
                            EntityID = 46,
                            UserID = 2,
                            LastAlivePosition = new Vector
                            {
                                X = 90,
                                Y = 900,
                                Z = 9000,
                            },
                            Position = new Vector
                            {
                                X = 80,
                                Y = 800,
                                Z = 8000,
                            },
                            Money = 200,
                            RoundStartEquipmentValue = 200,
                        },
                    },
                },
                new()
                {
                    Round = 2,
                    Terrorists = new List<Player>
                    {
                        new()
                        {
                            Name = "TheWhaleMan",
                            SteamID = 12321313213,
                            EntityID = 46,
                            UserID = 2,
                            LastAlivePosition = new Vector
                            {
                                X = 400,
                                Y = 400,
                                Z = 400,
                            },
                            Position = new Vector
                            {
                                X = 500,
                                Y = 500,
                                Z = 500,
                            },
                            Money = 1000,
                            RoundStartEquipmentValue = 200,
                        },
                    },
                    CounterTerrorists = new List<Player>
                    {
                        new()
                        {
                            Name = "JimWood",
                            SteamID = 32443298432,
                            EntityID = 45,
                            UserID = 1,
                            LastAlivePosition = new Vector
                            {
                                X = 70,
                                Y = 70,
                                Z = 70,
                            },
                            Position = new Vector
                            {
                                X = 60,
                                Y = 60,
                                Z = 60,
                            },
                            Money = 5000,
                            RoundStartEquipmentValue = 4750,
                        },
                    },
                },
            };

            var PlayerHurtValues = new List<PlayerHurt>
            {
                new()
                {
                    Round = 1,
                    TimeInRound = 40,
                    Player = TeamPlayersValues[0].CounterTerrorists[0],
                    Attacker = TeamPlayersValues[0].Terrorists[0],
                    Health = 0,
                    Armor = 50,
                    Weapon = new Equipment("weapon_ak47"),
                    HealthDamage = 100,
                    ArmorDamage = 50,
                    HitGroup = HitGroup.Head,
                    PossiblyKilledByBombExplosion = false,
                },
                new()
                {
                    Round = 2,
                    TimeInRound = 90,
                    Player = TeamPlayersValues[1].Terrorists[0],
                    Attacker = TeamPlayersValues[1].CounterTerrorists[0],
                    Health = 0,
                    Armor = 25,
                    Weapon = new Equipment("weapon_awp"),
                    HealthDamage = 150,
                    ArmorDamage = 75,
                    HitGroup = HitGroup.Head,
                    PossiblyKilledByBombExplosion = false,
                },
            };

            var PlayerKilledEventsValues = new List<PlayerKilledEventArgs>
            {
                new()
                {
                    Round = 1,
                    TimeInRound = 40,
                    Killer = TeamPlayersValues[0].Terrorists[0],
                    Victim = TeamPlayersValues[0].CounterTerrorists[0],
                    Assister = null,
                    KillerBotTakeover = false,
                    VictimBotTakeover = false,
                    AssisterBotTakeover = false,
                    Headshot = true,
                    Suicide = false,
                    TeamKill = false,
                    PenetratedObjects = 0,
                    AssistedFlash = false,
                },
                new()
                {
                    Round = 2,
                    TimeInRound = 90,
                    Killer = TeamPlayersValues[1].CounterTerrorists[0],
                    Victim = TeamPlayersValues[1].Terrorists[0],
                    Assister = null,
                    KillerBotTakeover = true,
                    VictimBotTakeover = true,
                    AssisterBotTakeover = true,
                    Headshot = true,
                    Suicide = false,
                    TeamKill = false,
                    PenetratedObjects = 1,
                    AssistedFlash = true,
                },
            };

            var PlayerValues = new Dictionary<string, List<Player>>
            {
                {
                    "Kills", new List<Player>
                    {
                        TeamPlayersValues[0].Terrorists[0],
                        TeamPlayersValues[1].CounterTerrorists[0],
                    }
                },
                {
                    "Deaths", new List<Player>
                    {
                        TeamPlayersValues[0].CounterTerrorists[0],
                        TeamPlayersValues[1].Terrorists[0],
                    }
                },
                {
                    "Headshots", new List<Player>
                    {
                        TeamPlayersValues[0].Terrorists[0],
                    }
                },
                {
                    "Assists", new List<Player>
                    {
                        TeamPlayersValues[0].CounterTerrorists[0],
                    }
                },
                {
                    "MVPs", new List<Player>
                    {
                        TeamPlayersValues[0].Terrorists[0],
                        TeamPlayersValues[1].CounterTerrorists[0],
                    }
                },
                {
                    "Shots", new List<Player>
                    {
                        TeamPlayersValues[0].Terrorists[0],
                        TeamPlayersValues[0].Terrorists[0],
                        TeamPlayersValues[0].Terrorists[0],
                        TeamPlayersValues[1].Terrorists[0],
                        TeamPlayersValues[1].CounterTerrorists[0],
                        TeamPlayersValues[1].CounterTerrorists[0],
                        TeamPlayersValues[1].CounterTerrorists[0],
                    }
                },
                {
                    "Plants", new List<Player>
                    {
                        TeamPlayersValues[0].Terrorists[0],
                        TeamPlayersValues[1].Terrorists[0],
                    }
                },
                {
                    "Defuses", new List<Player>
                    {
                        TeamPlayersValues[1].CounterTerrorists[0],
                    }
                },
                {
                    "Rescues", new List<Player>
                    {
                        TeamPlayersValues[0].CounterTerrorists[0],
                        TeamPlayersValues[0].CounterTerrorists[0],
                    }
                },
            };

            var WeaponValues = new List<Equipment>
            {
                new()
                {
                    Owner = TeamPlayersValues[0].Terrorists[0],
                    Weapon = EquipmentElement.AK47,
                },
                new()
                {
                    Owner = TeamPlayersValues[0].CounterTerrorists[0],
                    Weapon = EquipmentElement.AWP,
                },
            };

            var PenetrationValues = new List<int>
            {
                0,
                1,
            };

            var BombsitePlantValues = new List<BombPlanted>
            {
                new()
                {
                    Bombsite = 'A',
                    Player = TeamPlayersValues[0].Terrorists[0],
                    Round = 1,
                    TimeInRound = 35,
                    XPosition = 100,
                    YPosition = 100,
                    ZPosition = 100,
                },
                new()
                {
                    Bombsite = 'B',
                    Player = TeamPlayersValues[1].Terrorists[0],
                    Round = 2,
                    TimeInRound = 60,
                    XPosition = 400,
                    YPosition = 400,
                    ZPosition = 400,
                },
            };

            var BombsiteExplodeValues = new List<BombExploded>
            {
                new()
                {
                    Bombsite = 'A',
                    Player = TeamPlayersValues[0].Terrorists[0],
                    Round = 1,
                    TimeInRound = 75,
                },
            };

            var BombsiteDefuseValues = new List<BombDefused>
            {
                new()
                {
                    Bombsite = 'B',
                    Player = TeamPlayersValues[1].CounterTerrorists[0],
                    Round = 2,
                    TimeInRound = 100,
                    HasKit = true,
                },
            };

            var HostageRescueValues = new List<HostageRescued>
            {
                new()
                {
                    Hostage = 'A',
                    HostageIndex = 250,
                    RescueZone = 0,
                    Player = TeamPlayersValues[0].CounterTerrorists[0],
                    Round = 1,
                    TimeInRound = 50,
                    XPosition = 800,
                    YPosition = 800,
                    ZPosition = 800,
                },
                new()
                {
                    Hostage = 'B',
                    HostageIndex = 251,
                    RescueZone = 0,
                    Player = TeamPlayersValues[0].CounterTerrorists[0],
                    Round = 1,
                    TimeInRound = 51,
                    XPosition = 700,
                    YPosition = 700,
                    ZPosition = 700,
                },
            };

            var HostagePickedUpValues = new List<HostagePickedUp>
            {
                new()
                {
                    Hostage = 'A',
                    HostageIndex = 250,
                    Player = TeamPlayersValues[0].CounterTerrorists[0],
                    Round = 1,
                    TimeInRound = 20,
                },
                new()
                {
                    Hostage = 'B',
                    HostageIndex = 251,
                    Player = TeamPlayersValues[0].CounterTerrorists[0],
                    Round = 1,
                    TimeInRound = 35,
                },
                new()
                {
                    Hostage = 'A',
                    HostageIndex = 250,
                    Player = TeamPlayersValues[1].CounterTerrorists[0],
                    Round = 2,
                    TimeInRound = 40,
                },
            };

            var TeamValues = new List<Team>
            {
                Team.Terrorist,
                Team.CounterTerrorist,
            };

            var RoundEndReasonValues = new List<RoundEndReason>
            {
                RoundEndReason.TargetBombed,
                RoundEndReason.BombDefused,
            };

            var RoundLengthValues = new List<double>
            {
                80,
                105,
            };

            var TeamEquipmentValues = new List<TeamEquipment>
            {
                new()
                {
                    Round = 1,
                    TEquipValue = 2900,
                    TExpenditure = 200,
                    CTEquipValue = 450,
                    CTExpenditure = 50,
                },
                new()
                {
                    Round = 2,
                    TEquipValue = 800,
                    TExpenditure = 600,
                    CTEquipValue = 5750,
                    CTExpenditure = 1000,
                },
            };

            var GrenadeValues = new List<NadeEventArgs>
            {
                new FlashEventArgs
                {
                    NadeType = EquipmentElement.Flash,
                    ThrownBy = TeamPlayersValues[0].Terrorists[0],
                    Position = new Vector
                    {
                        X = 500,
                        Y = 500,
                        Z = 500,
                    },
                    FlashedPlayers = new[] { TeamPlayersValues[0].CounterTerrorists[0] },
                },
                new()
                {
                    NadeType = EquipmentElement.Smoke,
                    ThrownBy = TeamPlayersValues[0].Terrorists[0],
                    Position = new Vector
                    {
                        X = 500,
                        Y = 500,
                        Z = 500,
                    },
                },
                new()
                {
                    NadeType = EquipmentElement.HE,
                    ThrownBy = TeamPlayersValues[0].Terrorists[0],
                    Position = new Vector
                    {
                        X = 500,
                        Y = 500,
                        Z = 500,
                    },
                },
                new()
                {
                    NadeType = EquipmentElement.Molotov,
                    ThrownBy = TeamPlayersValues[0].Terrorists[0],
                    Position = new Vector
                    {
                        X = 500,
                        Y = 500,
                        Z = 500,
                    },
                },
                new()
                {
                    NadeType = EquipmentElement.Incendiary,
                    ThrownBy = TeamPlayersValues[0].Terrorists[0],
                    Position = new Vector
                    {
                        X = 500,
                        Y = 500,
                        Z = 500,
                    },
                },
                new()
                {
                    NadeType = EquipmentElement.Decoy,
                    ThrownBy = TeamPlayersValues[0].Terrorists[0],
                    Position = new Vector
                    {
                        X = 500,
                        Y = 500,
                        Z = 500,
                    },
                },
            };

            var ChickenValues = new List<ChickenKilledEventArgs> { new() };

            var ShotsFiredValues = new List<ShotFired>
            {
                new()
                {
                    Round = 1,
                    TimeInRound = 1,
                    TeamSide = Team.Terrorist.ToString(),
                    Shooter = TeamPlayersValues[0].Terrorists[0],
                },
                new()
                {
                    Round = 1,
                    TimeInRound = 1,
                    TeamSide = Team.Terrorist.ToString(),
                    Shooter = TeamPlayersValues[0].Terrorists[0],
                },
                new()
                {
                    Round = 1,
                    TimeInRound = 1,
                    TeamSide = Team.Terrorist.ToString(),
                    Shooter = TeamPlayersValues[0].Terrorists[0],
                },
                new()
                {
                    Round = 2,
                    TimeInRound = 1,
                    TeamSide = Team.Terrorist.ToString(),
                    Shooter = TeamPlayersValues[1].Terrorists[0],
                },
                new()
                {
                    Round = 2,
                    TimeInRound = 1,
                    TeamSide = Team.CounterTerrorist.ToString(),
                    Shooter = TeamPlayersValues[1].CounterTerrorists[0],
                },
                new()
                {
                    Round = 2,
                    TimeInRound = 1,
                    TeamSide = Team.CounterTerrorist.ToString(),
                    Shooter = TeamPlayersValues[1].CounterTerrorists[0],
                },
                new()
                {
                    Round = 2,
                    TimeInRound = 1,
                    TeamSide = Team.CounterTerrorist.ToString(),
                    Shooter = TeamPlayersValues[1].CounterTerrorists[0],
                },
            };

            var playerPositionsStats = new List<PlayerPositionsInstance>
            {
                new()
                {
                    Round = 1,
                    TimeInRound = 1,
                    TeamSide = Team.Terrorist.ToString(),
                    SteamID = TeamPlayersValues[0].Terrorists[0].SteamID,
                    XPosition = 20,
                    YPosition = 200,
                    ZPosition = 2000,
                },
            };

            CollectedData = new CollectedData
            {
                tanookiStats = tanookiStats,
                MatchStartValues = MatchStartValues,
                SwitchSidesValues = SwitchSidesValues,
                MessagesValues = MessagesValues,
                TeamPlayersValues = TeamPlayersValues,
                PlayerHurtValues = PlayerHurtValues,
                PlayerKilledEventsValues = PlayerKilledEventsValues,
                //DisconnectedPlayerValues
                PlayerValues = PlayerValues,
                WeaponValues = WeaponValues,
                PenetrationValues = PenetrationValues,
                BombsitePlantValues = BombsitePlantValues,
                BombsiteExplodeValues = BombsiteExplodeValues,
                BombsiteDefuseValues = BombsiteDefuseValues,
                HostageRescueValues = HostageRescueValues,
                HostagePickedUpValues = HostagePickedUpValues,
                TeamValues = TeamValues,
                RoundEndReasonValues = RoundEndReasonValues,
                RoundLengthValues = RoundLengthValues,
                TeamEquipmentValues = TeamEquipmentValues,
                GrenadeValues = GrenadeValues,
                ChickenValues = ChickenValues,
                ShotsFiredValues = ShotsFiredValues,
                PlayerPositionsValues = playerPositionsStats,
                WriteTicks = true,
            };

            Processor = new Processor(DemoParser, DemoInformation, CollectedData);
        }

        public class DataValidationTests : TopStatsWaffleTests
        {
            [Fact]
            public void Should_return_bombsite_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.bombsiteStats.Count.ShouldBe(2);
                stats.bombsiteStats[0].Plants.ShouldBe(1);
                stats.bombsiteStats[0].Explosions.ShouldBe(1);
                stats.bombsiteStats[0].Defuses.ShouldBe(0);
                stats.bombsiteStats[1].Plants.ShouldBe(1);
                stats.bombsiteStats[1].Explosions.ShouldBe(0);
                stats.bombsiteStats[1].Defuses.ShouldBe(1);
            }

            [Fact]
            public void Should_return_chicken_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.chickenStats.Killed.ShouldBe(1);
            }

            [Fact]
            public void Should_return_feedback_messages_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.feedbackMessages.Count.ShouldBe(1);
                stats.feedbackMessages[0].Round.ShouldBe(1);
                stats.feedbackMessages[0].Message.ShouldBe("bad map");
            }

            [Fact]
            public void Should_return_first_shot_stats_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.firstDamageStats.Count.ShouldBe(2);
                stats.firstDamageStats[0].Round.ShouldBe(1);
                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().TimeInRound
                    .ShouldBe(40);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().TeamSideShooter
                    .ShouldBe("Terrorist");

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().SteamIDShooter
                    .ShouldBe(32443298432);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().XPositionShooter
                    .ShouldBe(0);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().YPositionShooter
                    .ShouldBe(0);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().ZPositionShooter
                    .ShouldBe(0);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().TeamSideVictim
                    .ShouldBe("CounterTerrorist");

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().SteamIDVictim
                    .ShouldBe(12321313213);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().XPositionVictim
                    .ShouldBe(0);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().YPositionVictim
                    .ShouldBe(0);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().ZPositionVictim
                    .ShouldBe(0);

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().Weapon
                    .ShouldBe("AK47");

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().WeaponClass
                    .ShouldBe("Rifle");

                stats.firstDamageStats[0].FirstDamageToEnemyByPlayers.FirstOrDefault().WeaponType
                    .ShouldBe("AssaultRifle");
            }

            [Fact]
            public void Should_return_grenade_specific_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.grenadesSpecificStats.Count.ShouldBe(6);
                stats.grenadesSpecificStats[0].NadeType.ShouldBe(EquipmentElement.Flash.ToString());
                stats.grenadesSpecificStats[1].NadeType.ShouldBe(EquipmentElement.Smoke.ToString());
                stats.grenadesSpecificStats[2].NadeType.ShouldBe(EquipmentElement.HE.ToString());
                stats.grenadesSpecificStats[3].NadeType.ShouldBe(EquipmentElement.Molotov.ToString());
                stats.grenadesSpecificStats[4].NadeType.ShouldBe(EquipmentElement.Incendiary.ToString());
                stats.grenadesSpecificStats[5].NadeType.ShouldBe(EquipmentElement.Decoy.ToString());
            }

            [Fact]
            public void Should_return_grenade_total_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.grenadesTotalStats.Count.ShouldBe(6);
                stats.grenadesTotalStats[0].NadeType.ShouldBe(EquipmentElement.Flash.ToString());
                stats.grenadesTotalStats[0].AmountUsed.ShouldBe(1);
                stats.grenadesTotalStats[1].NadeType.ShouldBe(EquipmentElement.Smoke.ToString());
                stats.grenadesTotalStats[1].AmountUsed.ShouldBe(1);
                stats.grenadesTotalStats[2].NadeType.ShouldBe(EquipmentElement.HE.ToString());
                stats.grenadesTotalStats[2].AmountUsed.ShouldBe(1);

                // In practice, all fire grenade events returned by the parser use Incendiary only because there's no
                // way to distinguish the grenade type from the game event.
                // However, the processor shouldn't care about this detail - if it somehow gets a Molotov, then it
                // should count it as normal rather than forcing it to count towards Incendiary.
                stats.grenadesTotalStats[3].NadeType.ShouldBe(EquipmentElement.Molotov.ToString());
                stats.grenadesTotalStats[3].AmountUsed.ShouldBe(1);

                stats.grenadesTotalStats[4].NadeType.ShouldBe(EquipmentElement.Incendiary.ToString());
                stats.grenadesTotalStats[4].AmountUsed.ShouldBe(1);
                stats.grenadesTotalStats[5].NadeType.ShouldBe(EquipmentElement.Decoy.ToString());
                stats.grenadesTotalStats[5].AmountUsed.ShouldBe(1);
            }

            [Fact]
            public void Should_return_hostage_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.hostageStats.Count.ShouldBe(2);
                stats.hostageStats[0].Hostage.ShouldBe('A');
                stats.hostageStats[0].HostageIndex.ShouldBe(250);
                stats.hostageStats[0].PickedUps.ShouldBe(2);
                stats.hostageStats[0].Rescues.ShouldBe(1);
                stats.hostageStats[1].Hostage.ShouldBe('B');
                stats.hostageStats[1].HostageIndex.ShouldBe(251);
                stats.hostageStats[1].PickedUps.ShouldBe(1);
                stats.hostageStats[1].Rescues.ShouldBe(1);
            }

            /*[Fact]
            public void Should_return_rescue_zone_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.rescueZoneStats.Count.ShouldBe(1); // cannot test positions as is currently, as DemoParser is not implemented
            }*/

            [Fact]
            public void Should_return_kills_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.killsStats.Count.ShouldBe(2);
                stats.killsStats[0].Round.ShouldBe(1);
                stats.killsStats[0].TimeInRound.ShouldBe(40);
                stats.killsStats[1].Round.ShouldBe(2);
                stats.killsStats[1].TimeInRound.ShouldBe(90);
            }

            [Fact]
            public void Should_return_map_info_correctly_for_defuse_maps()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.mapInfo.DemoName.ShouldBe("demo1");
                stats.mapInfo.MapName.ShouldBe("de_testmap");
                stats.mapInfo.GameMode.ShouldBe(nameof(GameMode.Defuse).ToLower());
                stats.mapInfo.TestType.ShouldBe(nameof(TestType.Casual).ToLower());
                stats.mapInfo.TestDate.ShouldBe(new DateTime(2020, 1, 1, 0, 0, 0).ToString());
            }

            [Fact]
            public void Should_return_map_info_correctly_for_hostage_maps()
            {
                // Arrange
                var demoInfo = new DemoInformation
                {
                    DemoName = "demo2",
                    MapName = "de_testmap2",
                    GameMode = GameMode.Hostage,
                    TestType = TestType.Casual,
                    TestDate = new DateTime(2020, 1, 1, 0, 0, 0).ToString(),
                };

                CollectedData.MatchStartValues = new List<MatchStartedEventArgs>
                {
                    new()
                    {
                        Mapname = "de_testmap2",
                        HasBombsites = false,
                    },
                };

                var processor = new Processor(DemoParser, demoInfo, CollectedData);

                // Act
                AllStats stats = processor.GetAllStats();

                // Assess
                stats.mapInfo.DemoName.ShouldBe("demo2");
                stats.mapInfo.MapName.ShouldBe("de_testmap2");
                stats.mapInfo.GameMode.ShouldBe(nameof(GameMode.Hostage).ToLower());
                stats.mapInfo.TestType.ShouldBe(nameof(TestType.Casual).ToLower());
                stats.mapInfo.TestDate.ShouldBe(new DateTime(2020, 1, 1, 0, 0, 0).ToString());
            }

            [Fact]
            public void Should_return_map_info_correctly_for_wingman_defuse_maps()
            {
                // Arrange
                var demoInfo = new DemoInformation
                {
                    DemoName = "demo3",
                    MapName = "de_testmap3",
                    GameMode = GameMode.WingmanDefuse,
                    TestType = TestType.Casual,
                    TestDate = new DateTime(2020, 1, 1, 0, 0, 0).ToString(),
                };

                CollectedData.MatchStartValues = new List<MatchStartedEventArgs>
                {
                    new()
                    {
                        Mapname = "de_testmap3",
                        HasBombsites = true,
                    },
                };

                var processor = new Processor(DemoParser, demoInfo, CollectedData);

                // Act
                AllStats stats = processor.GetAllStats();

                // Assess
                stats.mapInfo.DemoName.ShouldBe("demo3");
                stats.mapInfo.MapName.ShouldBe("de_testmap3");
                stats.mapInfo.GameMode.ShouldBe(nameof(GameMode.WingmanDefuse).ToLower());
                stats.mapInfo.TestType.ShouldBe(nameof(TestType.Casual).ToLower());
                stats.mapInfo.TestDate.ShouldBe(new DateTime(2020, 1, 1, 0, 0, 0).ToString());
            }

            [Fact]
            public void Should_return_map_info_correctly_for_wingman_hostage_maps()
            {
                // Arrange
                var demoInfo = new DemoInformation
                {
                    DemoName = "demo4",
                    MapName = "de_testmap4",
                    GameMode = GameMode.WingmanHostage,
                    TestType = TestType.Casual,
                    TestDate = new DateTime(2020, 1, 1, 0, 0, 0).ToString(),
                };

                CollectedData.MatchStartValues = new List<MatchStartedEventArgs>
                {
                    new()
                    {
                        Mapname = "de_testmap4",
                        HasBombsites = false,
                    },
                };

                var processor = new Processor(DemoParser, demoInfo, CollectedData);

                // Act
                AllStats stats = processor.GetAllStats();

                // Assess
                stats.mapInfo.DemoName.ShouldBe("demo4");
                stats.mapInfo.MapName.ShouldBe("de_testmap4");
                stats.mapInfo.GameMode.ShouldBe(nameof(GameMode.WingmanHostage).ToLower());
                stats.mapInfo.TestType.ShouldBe(nameof(TestType.Casual).ToLower());
                stats.mapInfo.TestDate.ShouldBe(new DateTime(2020, 1, 1, 0, 0, 0).ToString());
            }

            [Fact]
            public void Should_return_map_info_correctly_for_danger_zone_maps()
            {
                // Arrange
                var demoInfo = new DemoInformation
                {
                    DemoName = "demo5",
                    MapName = "de_testmap5",
                    GameMode = GameMode.DangerZone,
                    TestType = TestType.Casual,
                    TestDate = new DateTime(2020, 1, 1, 0, 0, 0).ToString(),
                };

                CollectedData.MatchStartValues = new List<MatchStartedEventArgs>
                {
                    new()
                    {
                        Mapname = "de_testmap5",
                        HasBombsites = false,
                    },
                };

                var processor = new Processor(DemoParser, demoInfo, CollectedData);

                // Act
                AllStats stats = processor.GetAllStats();

                // Assess
                stats.mapInfo.DemoName.ShouldBe("demo5");
                stats.mapInfo.MapName.ShouldBe("de_testmap5");
                stats.mapInfo.GameMode.ShouldBe(nameof(GameMode.DangerZone).ToLower());
                stats.mapInfo.TestType.ShouldBe(nameof(TestType.Casual).ToLower());
                stats.mapInfo.TestDate.ShouldBe(new DateTime(2020, 1, 1, 0, 0, 0).ToString());
            }

            [Fact]
            public void Should_return_player_positions_stats_correctly()
            {
                // Arrange

                // Act
                AllStats allStats = Processor.GetAllStats();
                PlayerPositionsStats stats = Processor.GetPlayerPositionsStats(CollectedData, allStats);

                // Assess
                stats.PlayerPositionByRound.Count.ShouldBe(1);
                stats.PlayerPositionByRound.FirstOrDefault().Round.ShouldBe(1);
                stats.PlayerPositionByRound.FirstOrDefault().PlayerPositionByTimeInRound
                    .FirstOrDefault().TimeInRound.ShouldBe(1);

                stats.PlayerPositionByRound.FirstOrDefault().PlayerPositionByTimeInRound
                    .FirstOrDefault().PlayerPositionBySteamID.FirstOrDefault().SteamID.ShouldBe(32443298432);

                stats.PlayerPositionByRound.FirstOrDefault().PlayerPositionByTimeInRound
                    .FirstOrDefault().PlayerPositionBySteamID.FirstOrDefault().TeamSide.ShouldBe("Terrorist");

                stats.PlayerPositionByRound.FirstOrDefault().PlayerPositionByTimeInRound
                    .FirstOrDefault().PlayerPositionBySteamID.FirstOrDefault().XPosition.ShouldBe(20);

                stats.PlayerPositionByRound.FirstOrDefault().PlayerPositionByTimeInRound
                    .FirstOrDefault().PlayerPositionBySteamID.FirstOrDefault().YPosition.ShouldBe(200);

                stats.PlayerPositionByRound.FirstOrDefault().PlayerPositionByTimeInRound
                    .FirstOrDefault().PlayerPositionBySteamID.FirstOrDefault().ZPosition.ShouldBe(2000);
            }

            [Fact]
            public void Should_return_player_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.playerStats.Count.ShouldBe(2);

                stats.playerStats[0].Assists.ShouldBe(0);
                stats.playerStats[0].AssistsIncludingBots.ShouldBe(0);
                stats.playerStats[0].Deaths.ShouldBe(0);
                stats.playerStats[0].DeathsIncludingBots.ShouldBe(0);
                stats.playerStats[0].Defuses.ShouldBe(1);
                stats.playerStats[0].Headshots.ShouldBe(1); // took over a bot for one of them
                stats.playerStats[0].Kills.ShouldBe(1); // took over a bot for one of them
                stats.playerStats[0].KillsIncludingBots.ShouldBe(2);
                stats.playerStats[0].MVPs.ShouldBe(2);
                stats.playerStats[0].Plants.ShouldBe(1);
                stats.playerStats[0].PlayerName.ShouldBe("JimWood");
                stats.playerStats[0].Rescues.ShouldBe(0);
                stats.playerStats[0].Shots.ShouldBe(6);
                stats.playerStats[0].SteamID.ShouldBe(32443298432);

                stats.playerStats[1].Assists.ShouldBe(1);
                stats.playerStats[1].AssistsIncludingBots.ShouldBe(1);
                stats.playerStats[1].Deaths.ShouldBe(1); // took over a bot for one of them
                stats.playerStats[1].DeathsIncludingBots.ShouldBe(2);
                stats.playerStats[1].Defuses.ShouldBe(0);
                stats.playerStats[1].Headshots.ShouldBe(0);
                stats.playerStats[1].Kills.ShouldBe(0);
                stats.playerStats[1].KillsIncludingBots.ShouldBe(0);
                stats.playerStats[1].MVPs.ShouldBe(0);
                stats.playerStats[1].Plants.ShouldBe(1);
                stats.playerStats[1].PlayerName.ShouldBe("TheWhaleMan");
                stats.playerStats[1].Rescues.ShouldBe(2);
                stats.playerStats[1].Shots.ShouldBe(1);
                stats.playerStats[1].SteamID.ShouldBe(12321313213);
            }

            [Fact]
            public void Should_return_rounds_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.roundsStats.Count.ShouldBe(2);

                stats.roundsStats[0].BombPlantPositionX.ShouldBe(100);
                stats.roundsStats[0].BombPlantPositionY.ShouldBe(100);
                stats.roundsStats[0].BombPlantPositionZ.ShouldBe(100);
                stats.roundsStats[0].BombsiteErrorMessage.ShouldBeNull();
                stats.roundsStats[0].BombsitePlantedAt.ShouldBe("A");
                stats.roundsStats[0].Half.ShouldBe("First");
                stats.roundsStats[0].HostageAPickedUpErrorMessage.ShouldBeNull();
                stats.roundsStats[0].HostageBPickedUpErrorMessage.ShouldBeNull();
                stats.roundsStats[0].Length.ShouldBe(80);
                stats.roundsStats[0].Overtime.ShouldBe(0);
                stats.roundsStats[0].PickedUpAllHostages.ShouldBe(true);
                stats.roundsStats[0].PickedUpHostageA.ShouldBe(true);
                stats.roundsStats[0].PickedUpHostageB.ShouldBe(true);
                stats.roundsStats[0].RescuedAllHostages.ShouldBe(true);
                stats.roundsStats[0].RescuedHostageA.ShouldBe(true);
                stats.roundsStats[0].RescuedHostageB.ShouldBe(true);
                stats.roundsStats[0].Round.ShouldBe(1);
                stats.roundsStats[0].TimeInRoundPlanted.ShouldBe(35);
                stats.roundsStats[0].TimeInRoundExploded.ShouldBe(75);
                stats.roundsStats[0].TimeInRoundDefused.ShouldBeNull();
                stats.roundsStats[0].TimeInRoundRescuedHostageA.ShouldBe(50);
                stats.roundsStats[0].TimeInRoundRescuedHostageB.ShouldBe(51);
                stats.roundsStats[0].WinMethod.ShouldBe("Bombed");
                stats.roundsStats[0].Winners.ShouldBe("Terrorist");

                stats.roundsStats[1].BombPlantPositionX.ShouldBe(400);
                stats.roundsStats[1].BombPlantPositionY.ShouldBe(400);
                stats.roundsStats[1].BombPlantPositionZ.ShouldBe(400);
                stats.roundsStats[1].BombsiteErrorMessage.ShouldBeNull();
                stats.roundsStats[1].BombsitePlantedAt.ShouldBe("B");
                stats.roundsStats[1].Half.ShouldBe("Second");
                stats.roundsStats[1].HostageAPickedUpErrorMessage.ShouldBeNull();
                stats.roundsStats[1].HostageBPickedUpErrorMessage.ShouldBeNull();
                stats.roundsStats[1].Length.ShouldBe(105);
                stats.roundsStats[1].Overtime.ShouldBe(0);
                stats.roundsStats[1].PickedUpAllHostages.ShouldBe(false);
                stats.roundsStats[1].PickedUpHostageA.ShouldBe(true);
                stats.roundsStats[1].PickedUpHostageB.ShouldBe(false);
                stats.roundsStats[1].RescuedAllHostages.ShouldBe(false);
                stats.roundsStats[1].RescuedHostageA.ShouldBe(false);
                stats.roundsStats[1].RescuedHostageB.ShouldBe(false);
                stats.roundsStats[1].Round.ShouldBe(2);
                stats.roundsStats[1].TimeInRoundPlanted.ShouldBe(60);
                stats.roundsStats[1].TimeInRoundExploded.ShouldBeNull();
                stats.roundsStats[1].TimeInRoundDefused.ShouldBe(100);
                stats.roundsStats[1].TimeInRoundRescuedHostageA.ShouldBeNull();
                stats.roundsStats[1].TimeInRoundRescuedHostageB.ShouldBeNull();
                stats.roundsStats[1].WinMethod.ShouldBe("Defused");
                stats.roundsStats[1].Winners.ShouldBe("CounterTerrorist");
            }

            [Fact]
            public void Should_return_supported_gamemodes_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.supportedGamemodes.Count.ShouldBe(6);
                stats.supportedGamemodes[0].ShouldBe(nameof(GameMode.DangerZone).ToLower());
                stats.supportedGamemodes[1].ShouldBe(nameof(GameMode.Defuse).ToLower());
                stats.supportedGamemodes[2].ShouldBe(nameof(GameMode.Hostage).ToLower());
                stats.supportedGamemodes[3].ShouldBe(nameof(GameMode.WingmanDefuse).ToLower());
                stats.supportedGamemodes[4].ShouldBe(nameof(GameMode.WingmanHostage).ToLower());
                stats.supportedGamemodes[5].ShouldBe(nameof(GameMode.Unknown).ToLower());
            }

            [Fact]
            public void Should_return_tanooki_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.tanookiStats.Joined.ShouldBe(true);
                stats.tanookiStats.Left.ShouldBe(true);
                stats.tanookiStats.RoundJoined.ShouldBe(1);
                stats.tanookiStats.RoundLeft.ShouldBe(2);
                stats.tanookiStats.RoundsLasted.ShouldBe(1);
            }

            [Fact]
            public void Should_return_team_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.teamStats.Count.ShouldBe(2);

                stats.teamStats[0].Round.ShouldBe(1);
                stats.teamStats[0].TeamAlphaKills.ShouldBe(1);
                stats.teamStats[0].TeamAlphaDeaths.ShouldBe(0);
                stats.teamStats[0].TeamAlphaHeadshots.ShouldBe(1);
                stats.teamStats[0].TeamBravoKills.ShouldBe(0);
                stats.teamStats[0].TeamBravoDeaths.ShouldBe(1);
                stats.teamStats[0].TeamBravoHeadshots.ShouldBe(0);
                stats.teamStats[0].TeamAlphaShotsFired.ShouldBe(3);
                stats.teamStats[0].TeamBravoShotsFired.ShouldBe(0);

                stats.teamStats[1].Round.ShouldBe(2);
                stats.teamStats[1].TeamAlphaKills.ShouldBe(1);
                stats.teamStats[1].TeamAlphaDeaths.ShouldBe(0);
                stats.teamStats[1].TeamAlphaHeadshots.ShouldBe(1);
                stats.teamStats[1].TeamBravoKills.ShouldBe(0);
                stats.teamStats[1].TeamBravoDeaths.ShouldBe(1);
                stats.teamStats[1].TeamBravoHeadshots.ShouldBe(0);
                stats.teamStats[1].TeamAlphaShotsFired.ShouldBe(3);
                stats.teamStats[1].TeamBravoShotsFired.ShouldBe(1);
            }

            [Fact]
            public void Should_return_version_number_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.versionNumber.Version.ShouldBe(
                    Assembly.GetExecutingAssembly().GetName().Version.ToString(3)
                );
            }

            [Fact]
            public void Should_return_winners_stats_correctly()
            {
                // Arrange

                // Act
                AllStats stats = Processor.GetAllStats();

                // Assess
                stats.winnersStats.TeamAlphaRounds.ShouldBe(2);
                stats.winnersStats.TeamBetaRounds.ShouldBe(0);
                stats.winnersStats.WinningTeam.ShouldBe("Team Alpha");
            }
        }

        public void Dispose()
        {
            Stream?.Dispose();
            DemoParser?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
