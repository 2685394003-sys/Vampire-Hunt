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

`PlayerNetworkState` and `EnemyHealth` own their respective ability systems. Only
the server/offline host sends gameplay events, changes attributes or ticks periodic
effects. Existing player stat `NetworkVariable`s remain authoritative. Enemies layer
per-instance modifiers over `EnemyRunStats`, so a slow or vulnerability affects only
its target instead of every spawned enemy.

A compact player `NetworkList` of pact IDs/stacks supports UI. Each enemy also keeps
a persistent cue list for active debuffs, allowing late joiners to reconstruct burning,
frozen and future status VFX. Executed cue pulses use RPCs and remain cosmetic.

All ordinary enemies resolve the shared
`Resources/GameVFX/EnemyStatusVfxConfig` asset. Assign burning and frozen prefabs
there once; each presenter instantiates a local copy only while that enemy needs the
status. Empty slots retain the generated-particle fallback. A presenter-level config
override is available only for exceptional enemy variants.

Status prefabs that contain Piloto Studio `OverlayFX` components are rebound at
runtime to the enemy's largest `SkinnedMeshRenderer`, falling back to its largest
`MeshRenderer`. Overlay materials are removed when the status ends, and separate
burning/frozen overlays can coexist on the same renderer.

Periodic damage reports its actual server-confirmed health loss through the combat
text presentation channel. It deliberately does not emit another `AttackHit` event,
so burning ticks cannot recursively trigger on-hit blood pacts.

## Adding a blood pact

1. Add one or more `GameplayAbilityDefinition` entries to the pact row.
2. Choose a trigger from `GameplayEventType`.
3. Compose instant, duration or infinite effects.
4. Route any new combat fact through `PlayerNetworkState.Report...`; effects should
   not subscribe directly to unrelated MonoBehaviours.
5. Add a validator case before marking a special-effect description implemented.

Currently implemented advanced pacts:

- `update_015` Ember Fire: 30% on-hit chance, four seconds of burning and two
  server-authoritative damage each second.
- `update_016` Frost Breath: 20% on-hit chance to freeze movement and attacks for
  two seconds.
- `update_017` Scarlet Kiss: heals 15% of actual attack damage, minimum one.
- `update_021` Berserker Rage: missing-health attack/crit scaling refreshed on
  authoritative health changes.

Run `Vampire Hunt/Validation/Validate Gameplay Ability Framework` after editing
the framework or an implemented pact. The validator covers enemy damage periods,
control-tag expiry and persistent status VFX lifecycle in addition to player effects.
