using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    public sealed class BossDamageService : MonoBehaviour, IBossDamageService
    {
        public bool TryApply(in DamageRequest request, out ResolvedDamage result)
        {
            var application = new BossDamageApplication(request, Float3.Zero);
            return TryApply(application, out result);
        }

        public bool TryApply(in BossDamageApplication application, out ResolvedDamage result)
        {
            result = default;
            DamageRequest request = application.Damage;
            if (request.Target.IsNone || request.BaseDamage <= 0f ||
                !BossNetworkPlayerResolver.TryGetServerManager(out NetworkManager manager) ||
                !BossNetworkPlayerResolver.TryFindPlayer(manager, request.Target, out NetworkObject player) ||
                !BossNetworkPlayerResolver.TryGetPort(player, out IDamageReceiver receiver)) return false;

            if (!receiver.TryApplyDamage(request, out result)) return false;
            if (application.ImpactForce.SqrMagnitude > 0.0001f &&
                BossNetworkPlayerResolver.TryGetPort(player, out ICombatImpulseTarget impulseTarget))
            {
                impulseTarget.TryApplyImpulse(application.ImpactForce);
            }

            return true;
        }
    }
}
