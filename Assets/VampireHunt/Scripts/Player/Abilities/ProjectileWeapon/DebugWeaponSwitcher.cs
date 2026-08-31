using UnityEngine;
using UnityEngine.InputSystem;
using VampireHunt.Infrastructure.Integration;

namespace VampireHunt.Player.Abilities.ProjectileWeapon
{
    /// <summary>
    /// Temporary single-player weapon switch hook for testing multiple primary weapons.
    /// Press Digit1..DigitN to switch to the Nth weapon id (default: 1 = sword wave, 110 = sniper).
    /// Replace with pact-driven switching ("obtain weapon" pact) later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugWeaponSwitcher : MonoBehaviour
    {
        [SerializeField] private CombatAbilityHost abilityHost;
        [SerializeField] private uint[] weaponIds = { 1, 110 };

        private void Update()
        {
            if (abilityHost == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (weaponIds.Length > 0 && keyboard.digit1Key.wasPressedThisFrame) abilityHost.SetActiveWeapon(weaponIds[0]);
            if (weaponIds.Length > 1 && keyboard.digit2Key.wasPressedThisFrame) abilityHost.SetActiveWeapon(weaponIds[1]);
            if (weaponIds.Length > 2 && keyboard.digit3Key.wasPressedThisFrame) abilityHost.SetActiveWeapon(weaponIds[2]);
            if (weaponIds.Length > 3 && keyboard.digit4Key.wasPressedThisFrame) abilityHost.SetActiveWeapon(weaponIds[3]);
            if (weaponIds.Length > 4 && keyboard.digit5Key.wasPressedThisFrame) abilityHost.SetActiveWeapon(weaponIds[4]);
        }
    }
}
