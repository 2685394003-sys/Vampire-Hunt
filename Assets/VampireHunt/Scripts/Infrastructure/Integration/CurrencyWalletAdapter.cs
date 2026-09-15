using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CoreStatsHandler))]
    public sealed class CurrencyWalletAdapter : MonoBehaviour, ICurrencyWallet
    {
        [SerializeField] private CoreStatsHandler coreStats;

        public int Balance => coreStats != null
            ? Mathf.Max(0, Mathf.FloorToInt(coreStats.GetCurrentValue(StatKeys.Coin)))
            : 0;

        private void Awake()
        {
            if (coreStats == null) coreStats = GetComponent<CoreStatsHandler>();
        }

        public bool CanAfford(int amount) => amount >= 0 && Balance >= amount;

        public bool TrySpend(int amount)
        {
            return amount >= 0 && coreStats != null &&
                   coreStats.TryConsumeStat(StatKeys.Coin, amount);
        }

        public void Credit(int amount)
        {
            if (amount > 0 && coreStats != null)
                coreStats.ModifyStat(StatKeys.Coin, amount, 0, ModificationSource.Direct);
        }
    }
}
