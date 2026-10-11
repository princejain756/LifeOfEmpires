using System;
using System.Collections.Generic;

namespace LifeOfEmpires
{
    /// <summary>
    /// Owns one team's stockpile. The dictionary is the storage; every mutation is delegated to
    /// <see cref="EconomyBalance"/> so all arithmetic — including the refusal of unaffordable
    /// spends — lives in exactly one place. This type never clamps: an illegal spend is refused
    /// and explained, never silently truncated to whatever the player holds.
    /// </summary>
    public class ResourceManager
    {
        private readonly Dictionary<ResourceType, int> _stock = new Dictionary<ResourceType, int>(ResourceTypeInfo.Count);

        /// <summary>Fired after a successful mutation. Arguments: resource, value before, value after.</summary>
        public event Action<ResourceType, int, int> OnResourceChanged;

        /// <summary>Fired once per successful <see cref="TrySpend"/> after all per-resource events.</summary>
        public event Action<ResourceCost> OnSpend;

        public Team Team { get; }

        public ResourceManager(Team team, ResourceBundle starting = default)
        {
            Team = team;
            for (int i = 0; i < ResourceTypeInfo.All.Length; i++)
            {
                _stock[ResourceTypeInfo.All[i]] = 0;
            }

            EconomyBalance.AddBundle(_stock, starting);
        }

        /// <summary>Current amount held. Missing keys read as zero.</summary>
        public int Get(ResourceType type) => EconomyBalance.Get(_stock, type);

        /// <summary>Total across every resource. Useful for scoring and debugging only.</summary>
        public int Total
        {
            get
            {
                int total = 0;
                for (int i = 0; i < ResourceTypeInfo.All.Length; i++) total += Get(ResourceTypeInfo.All[i]);
                return total;
            }
        }

        /// <summary>Grant resources. Negative amounts are a programming error, not a debit.</summary>
        public void Add(ResourceType type, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "use TrySpend to remove resources");

            int before = Get(type);
            EconomyBalance.Add(_stock, type, amount);
            int after = Get(type);
            if (after != before) OnResourceChanged?.Invoke(type, before, after);
        }

        public void Add(ResourceBundle bundle)
        {
            for (int i = 0; i < ResourceTypeInfo.All.Length; i++)
            {
                ResourceType type = ResourceTypeInfo.All[i];
                int amount = bundle.AmountOf(type);
                if (amount > 0) Add(type, amount);
            }
        }

        public bool CanAfford(ResourceCost cost) => EconomyBalance.CanAfford(_stock, cost);

        /// <summary>
        /// Deduct <paramref name="cost"/> only if the whole cost is covered. On refusal the
        /// stockpile is untouched and <paramref name="reason"/> names the missing resource.
        /// </summary>
        public bool TrySpend(ResourceCost cost, out string reason)
        {
            // Snapshot before-values so change events can report accurate deltas.
            int woodBefore = Get(ResourceType.Wood);
            int foodBefore = Get(ResourceType.Food);
            int goldBefore = Get(ResourceType.Gold);
            int crownsBefore = Get(ResourceType.Crowns);

            if (!EconomyBalance.TrySpend(_stock, cost, out reason)) return false;

            RaiseIfChanged(ResourceType.Wood, woodBefore);
            RaiseIfChanged(ResourceType.Food, foodBefore);
            RaiseIfChanged(ResourceType.Gold, goldBefore);
            RaiseIfChanged(ResourceType.Crowns, crownsBefore);
            OnSpend?.Invoke(cost);
            return true;
        }

        /// <summary>Give a cost back — cancelled production, demolished placement, refunded purchase.</summary>
        public void Refund(ResourceCost cost)
        {
            int woodBefore = Get(ResourceType.Wood);
            int foodBefore = Get(ResourceType.Food);
            int goldBefore = Get(ResourceType.Gold);
            int crownsBefore = Get(ResourceType.Crowns);

            EconomyBalance.Refund(_stock, cost);

            RaiseIfChanged(ResourceType.Wood, woodBefore);
            RaiseIfChanged(ResourceType.Food, foodBefore);
            RaiseIfChanged(ResourceType.Gold, goldBefore);
            RaiseIfChanged(ResourceType.Crowns, crownsBefore);
        }

        public ResourceBundle Snapshot() => new ResourceBundle(
            Get(ResourceType.Wood),
            Get(ResourceType.Food),
            Get(ResourceType.Gold),
            Get(ResourceType.Crowns));

        /// <summary>Force-refresh listeners, e.g. after loading a save directly into the stockpile.</summary>
        public void RaiseAllChanged()
        {
            for (int i = 0; i < ResourceTypeInfo.All.Length; i++)
            {
                ResourceType type = ResourceTypeInfo.All[i];
                OnResourceChanged?.Invoke(type, Get(type), Get(type));
            }
        }

        private void RaiseIfChanged(ResourceType type, int before)
        {
            int after = Get(type);
            if (after != before) OnResourceChanged?.Invoke(type, before, after);
        }
    }
}
