using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeOfEmpires
{
    /// <summary>
    /// Watches a battle and decides when it is over, and how well it was played.
    ///
    /// Victory and defeat are edge-triggered exactly once: <see cref="OnBattleEnded"/> fires on the
    /// first evaluation that proves an outcome, and every later evaluation is a no-op. That matters
    /// because town halls can die on the same frame as their last defender, and a double-fired
    /// victory would double-award crowns and re-trigger the results panel.
    /// </summary>
    public class BattleObjectiveManager : MonoBehaviour
    {
        private MissionDefinition _mission;
        private CombatSystem _combat;
        private BattleOutcome _outcome = BattleOutcome.InProgress;

        private bool _playerTownHallSeen;
        private bool _enemyTownHallSeen;
        private bool _unsupportedConditionReported;
        private bool _unsupportedStarReported;
        private int _enemyBuildingsDestroyed;
        /// <summary>
        /// Buildings already counted as destroyed. Keyed by the building itself — Unity's
        /// Object.Equals/GetHashCode compare instance ids, so this dedupes without the obsolete
        /// GetInstanceID() call.
        /// </summary>
        private readonly HashSet<BuildingController> _countedBuildings = new HashSet<BuildingController>();

        /// <summary>Fired once, when the battle resolves.</summary>
        public event Action<BattleOutcome> OnBattleEnded;

        /// <summary>Fired when the star tally changes. Arguments: earned, total configured.</summary>
        public event Action<int, int> OnStarsChanged;

        public BattleOutcome Outcome => _outcome;

        public bool HasEnded => _outcome != BattleOutcome.InProgress;

        /// <summary>
        /// How many times the end was raised. Always 0 or 1 — asserted by the objective tests, since
        /// it is the only way to prove the "fires once" property from the outside.
        /// </summary>
        public int EndRaisedCount { get; private set; }

        public int EnemyBuildingsDestroyed => _enemyBuildingsDestroyed;

        public MissionDefinition Mission => _mission;

        /// <summary>Attach to a battle. Safe to call before the battlefield is populated.</summary>
        public void Initialize(MissionDefinition mission, CombatSystem combat)
        {
            _mission = mission;
            _combat = combat;
            _outcome = BattleOutcome.InProgress;
            EndRaisedCount = 0;
            _playerTownHallSeen = false;
            _enemyTownHallSeen = false;
            _unsupportedConditionReported = false;
            _unsupportedStarReported = false;
            _enemyBuildingsDestroyed = 0;
            _countedBuildings.Clear();

            if (_combat != null) _combat.OnDied += HandleDied;
        }

        private void HandleDied(IDamageable victim)
        {
            if (victim is BuildingController building && building.Team == Team.Enemy)
            {
                // The same building must not be counted twice if two hits land on the fatal frame.
                if (_countedBuildings.Add(building)) _enemyBuildingsDestroyed++;
            }

            Evaluate();
        }

        /// <summary>
        /// Re-derive the outcome and star tally from the current world state. Cheap, idempotent, and
        /// safe to call every frame as well as from a death callback.
        /// </summary>
        public void Evaluate()
        {
            if (HasEnded) return;

            BattleContext context = BattleContext.Current;
            if (context == null) return;

            // Record that a town hall exists before we can observe it dying. Without this latch a
            // battle would report victory on its first frame, before the enemy hall has spawned.
            if (!_playerTownHallSeen && context.TownHallOf(Team.Player) != null) _playerTownHallSeen = true;
            if (!_enemyTownHallSeen && context.TownHallOf(Team.Enemy) != null) _enemyTownHallSeen = true;

            ObjectiveCondition victory = _mission != null ? _mission.victoryCondition : ObjectiveCondition.DestroyEnemyTownHall;
            ObjectiveCondition defeat = _mission != null ? _mission.defeatCondition : ObjectiveCondition.LosePlayerTownHall;

            if (IsSatisfied(defeat, context, isVictoryCondition: false))
            {
                End(BattleOutcome.Defeat);
                return;
            }

            if (IsSatisfied(victory, context, isVictoryCondition: true))
            {
                End(BattleOutcome.Victory);
            }
        }

        private bool IsSatisfied(ObjectiveCondition condition, BattleContext context, bool isVictoryCondition)
        {
            switch (condition)
            {
                case ObjectiveCondition.DestroyEnemyTownHall:
                    return _enemyTownHallSeen && context.TownHallOf(Team.Enemy) == null;

                case ObjectiveCondition.LosePlayerTownHall:
                    return _playerTownHallSeen && context.TownHallOf(Team.Player) == null;

                default:
                    ReportUnsupported(condition, isVictoryCondition);
                    return false;
            }
        }

        private void ReportUnsupported(ObjectiveCondition condition, bool isVictoryCondition)
        {
            if (_unsupportedConditionReported) return;
            _unsupportedConditionReported = true;
            Debug.LogWarning($"BattleObjectiveManager: '{condition}' is not a supported {(isVictoryCondition ? "victory" : "defeat")} condition and will not end the battle. Only DestroyEnemyTownHall and LosePlayerTownHall are evaluated.");
        }

        private void End(BattleOutcome outcome)
        {
            if (HasEnded) return;

            _outcome = outcome;
            EndRaisedCount++;

            int earned = ScoreStars(BattleContext.Current);
            if (BattleContext.Current != null) BattleContext.Current.Outcome = outcome;

            Debug.Log($"BattleObjectiveManager: battle resolved as {outcome} with {earned} star(s) ({EnemyBuildingsDestroyed} enemy buildings destroyed).");
            OnStarsChanged?.Invoke(earned, TotalStars);
            OnBattleEnded?.Invoke(outcome);
        }

        // -----------------------------------------------------------------------------------------
        // Star objectives
        // -----------------------------------------------------------------------------------------

        public int TotalStars => _mission != null ? _mission.ConfiguredStarCount : 0;

        /// <summary>
        /// Score the mission's optional stars against the live world state. A star whose condition is
        /// not yet supported is reported once and scored as not earned, never silently as earned.
        /// </summary>
        public int ScoreStars(BattleContext context, List<bool> results = null)
        {
            results?.Clear();
            if (_mission == null || _mission.starObjectives == null) return 0;

            int earned = 0;
            for (int i = 0; i < _mission.starObjectives.Length; i++)
            {
                StarObjective star = _mission.starObjectives[i];
                if (!star.IsConfigured) continue;

                bool satisfied = IsStarSatisfied(star, context);
                if (satisfied) earned++;
                results?.Add(satisfied);
            }

            return earned;
        }

        private bool IsStarSatisfied(StarObjective star, BattleContext context)
        {
            if (context == null) return false;

            switch (star.condition)
            {
                case ObjectiveCondition.DestroyEnemyTownHall:
                    return _enemyTownHallSeen && context.TownHallOf(Team.Enemy) == null;

                case ObjectiveCondition.DestroyEnemyBuildings:
                    return _enemyBuildingsDestroyed >= Mathf.Max(1, star.threshold);

                case ObjectiveCondition.TrainUnits:
                    return CountPlayerUnits(context, combatOnly: false) >= Mathf.Max(1, star.threshold);

                case ObjectiveCondition.StockpileResource:
                    return context.PlayerResources != null
                        && context.PlayerResources.Get(star.resource) >= Mathf.Max(1, star.threshold);

                case ObjectiveCondition.TimeLimitExpired:
                default:
                    ReportUnsupportedStar(star.condition);
                    return false;
            }
        }

        private void ReportUnsupportedStar(ObjectiveCondition condition)
        {
            if (_unsupportedStarReported) return;
            _unsupportedStarReported = true;
            Debug.LogWarning($"BattleObjectiveManager: star condition '{condition}' cannot be scored yet and will never be awarded. Author stars from DestroyEnemyTownHall, DestroyEnemyBuildings, TrainUnits or StockpileResource.");
        }

        private static int CountPlayerUnits(BattleContext context, bool combatOnly)
        {
            List<UnitController> units = context.PlayerUnits;
            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                UnitController unit = units[i];
                if (unit == null || unit.IsDead) continue;
                if (combatOnly && !unit.IsCombatUnit) continue;
                count++;
            }

            return count;
        }

        private void OnDestroy()
        {
            if (_combat != null) _combat.OnDied -= HandleDied;
        }
    }
}
