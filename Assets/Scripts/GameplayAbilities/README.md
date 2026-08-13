# Vampire Hunt Gameplay Ability Framework

This is a compact, server-authoritative framework inspired by Unreal GAS. It is
designed for blood pacts, run upgrades, buffs and debuffs without replacing the
project's existing Netcode for GameObjects state.

## Runtime boundaries

- `GameplayAbilityDefinition`: listens for one gameplay event and applies effects.
- `GameplayEffectDefinition`: immutable effect data, including duration, period,
  stacking, tag requirements, modifiers, executions and a cosmetic cue tag.
- `GameplayEffectSpec`: captures source and event context for one application.
- `GameplayAbilitySystem`: owns granted abilities, active effects, tags and timers.
- `IGameplayAbilitySystemHost`: narrow bridge to player/enemy stats and presentation.
- `GameplayCueEvent`: cosmetic-only output; gameplay must never depend on a cue.

ScriptableObject assets contain definitions only. Stack counts, timers, cooldowns
and captured context always live in runtime specs or active effects.

## Authority and replication

`PlayerNetworkState` owns the player's ability system. Only the server/offline host
sends gameplay events or mutates effects. Existing derived stat `NetworkVariable`s
remain the authoritative replicated values. A compact `NetworkList` of pact IDs and
stacks supports UI and late joiners. Cues use RPCs and are explicitly cosmetic.

## Adding a blood pact

1. Add one or more `GameplayAbilityDefinition` entries to the pact row.
2. Choose a trigger from `GameplayEventType`.
3. Compose instant, duration or infinite effects.
4. Route any new combat fact through `PlayerNetworkState.Report...`; effects should
   not subscribe directly to unrelated MonoBehaviours.
5. Add a validator case before marking a special-effect description implemented.

Currently implemented advanced pacts:

- `update_017` Scarlet Kiss: heals 15% of actual attack damage, minimum one.
- `update_021` Berserker Rage: missing-health attack/crit scaling refreshed on
  authoritative health changes.

Run `Vampire Hunt/Validation/Validate Gameplay Ability Framework` after editing
the framework or either pact.
