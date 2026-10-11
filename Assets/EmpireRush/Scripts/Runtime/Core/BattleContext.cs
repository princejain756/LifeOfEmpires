using System;
using System.Collections.Generic;

namespace EmpireRush
{
    /// <summary>Which side of the battlefield something belongs to.</summary>
    public enum Team
    {
        Neutral = 0,
        Player = 1,
        Enemy = 2
    }

    /// <summary>Terminal state of a battle.</summary>
    public enum BattleOutcome
    {
        InProgress = 0,
        Victory = 1,
        Defeat = 2
    }

    /// <summary>
    /// The mutable state of exactly one battle: who is fighting, with what, and how it is going.
    /// One instance exists per battle; <see cref="Current"/> is null outside a battle, which is what
    /// lets EditMode tests construct a context without a scene.
    /// </summary>
    public class BattleContext
    {
        /// <summary>The live battle, or null when no battle is running.</summary>
        public static BattleContext Current { get; private set; }

        public MissionDefinition Mission { get; private set; }
        public GameDatabase Database { get; private set; }
        public DifficultyProfile Difficulty { get; private set; }
        public EconomyBalance Balance { get; private set; }

        public ResourceManager PlayerResources { get; private set; }
        public ResourceManager EnemyResources { get; private set; }

        public PathfindingManager Pathfinding { get; private set; }
        public CombatSystem Combat { get; private set; }

        public readonly List<UnitController> PlayerUnits = new List<UnitController>(64);
        public readonly List<UnitController> EnemyUnits = new List<UnitController>(64);
        public readonly List<BuildingController> PlayerBuildings = new List<BuildingController>(32);
        public readonly List<BuildingController> EnemyBuildings = new List<BuildingController>(32);

        public float ElapsedSeconds { get; set; }
        public BattleOutcome Outcome { get; set; } = BattleOutcome.InProgress;

        internal BattleContext(
            MissionDefinition mission,
            GameDatabase database,
            EconomyBalance balance,
            ResourceManager playerResources,
            ResourceManager enemyResources,
            PathfindingManager pathfinding,
            CombatSystem combat)
        {
            Mission = mission;
            Database = database;
            Balance = balance;
            Difficulty = mission != null ? mission.difficulty : null;
            PlayerResources = playerResources;
            EnemyResources = enemyResources;
            Pathfinding = pathfinding;
            Combat = combat;
        }

        /// <summary>
        /// Creates the battle and makes it current. Callers are responsible for having built the
        /// resource managers and pathfinding grid first — <see cref="GameBootstrap"/> owns that order.
        /// </summary>
        public static BattleContext Begin(
            MissionDefinition mission,
            GameDatabase database,
            EconomyBalance balance,
            ResourceManager playerResources,
            ResourceManager enemyResources,
            PathfindingManager pathfinding,
            CombatSystem combat)
        {
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            if (playerResources == null) throw new ArgumentNullException(nameof(playerResources));

            Current = new BattleContext(mission, database, balance, playerResources, enemyResources, pathfinding, combat);
            return Current;
        }

        public static void End()
        {
            Current = null;
        }

        public List<UnitController> UnitsOf(Team team) => team == Team.Player ? PlayerUnits : EnemyUnits;

        public List<BuildingController> BuildingsOf(Team team) => team == Team.Player ? PlayerBuildings : EnemyBuildings;

        /// <summary>
        /// The living town hall belonging to <paramref name="team"/>, or null once it has been
        /// destroyed. Liveness is health-based rather than flag-based because a fatal hit and the
        /// owner's death handling run in separate callbacks on the same damage event: an observer
        /// that reacts to the death before the building's own handler must already see the hall as
        /// gone.
        /// </summary>
        public BuildingController TownHallOf(Team team)
        {
            List<BuildingController> list = BuildingsOf(team);
            for (int i = 0; i < list.Count; i++)
            {
                BuildingController building = list[i];
                if (building != null && building.IsAlive && building.Definition != null && building.Definition.isTownHall)
                {
                    return building;
                }
            }

            return null;
        }

        public int CountUnits(Team team, bool combatOnly)
        {
            List<UnitController> list = UnitsOf(team);
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                UnitController unit = list[i];
                if (unit == null || unit.IsDead) continue;
                if (combatOnly && !unit.IsCombatUnit) continue;
                count++;
            }

            return count;
        }

        /// <summary>Drops dead entries so long battles do not accumulate nulls in the rosters.</summary>
        public void PruneRosters()
        {
            Prune(PlayerUnits);
            Prune(EnemyUnits);
            PruneBuildings(PlayerBuildings);
            PruneBuildings(EnemyBuildings);
        }

        private static void Prune(List<UnitController> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null || list[i].IsDead) list.RemoveAt(i);
            }
        }

        private static void PruneBuildings(List<BuildingController> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) list.RemoveAt(i);
            }
        }
    }
}
