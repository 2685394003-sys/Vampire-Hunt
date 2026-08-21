# Legacy GameplayAbilities compatibility surface

The authoritative shared-rules implementation is now in the pure C# modules
`VampireHunt.Abilities`, `VampireHunt.Stats` and `VampireHunt.Combat`. The types
in this directory are retained only for existing ScriptableObject, Prefab and
AnimationEvent serialization while Player/Enemy adapters finish switching
their call sites. Do not add new gameplay rules here.

## Runtime boundaries

- `GameplayAbilityDefinition` / `GameplayEffectDefinition`: serialization-only
  authoring data. New runtime code must convert it to an immutable
  `VampireHunt.Abilities.Contracts.GameplayEffectSpec`.
- `GameplayAbilitySystem`: deprecated compatibility facade. Its damage and
  healing executions are translated through `CombatApplicationService`; it is
  not a second health mutation service.
- `GameplayTagContainer`: forwards to the Abilities domain `GameplayTagSet`.
- `EnemyInstanceStatModifiers`: forwards calculation and ownership to
  `VampireHunt.Stats.StatModifierCollection`.
- `GameplayCueEvent`: compatibility payload only; new presentation consumes
  `GameplayCueEvent` from the Abilities Contracts namespace.

ScriptableObject assets contain definitions only. Stack counts, timers, cooldowns
and captured context always live in runtime specs or active effects.

## Authority and replication

The new composition root owns the module runtime and ticks it in the server
simulation. Existing `PlayerNetworkState` and `EnemyHealth` fields remain
serialization/network compatibility shells until their adapter work is complete;
they must not become new rule authorities. Existing player stat
`NetworkVariable`s remain replication caches. Enemies layer per-instance
modifiers over `EnemyRunStats`, so a slow or vulnerability affects only its
target instead of every spawned enemy.

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

## Adding a blood pact (legacy assets)

1. Keep existing serialized `GameplayAbilityDefinition` entries stable.
2. Add the corresponding `GameplayEffectSpec` conversion in the Abilities
   authoring/composition layer; do not extend the legacy runner.
3. Route damage/healing through `CombatApplicationService` and publish the
   resulting event for presentation.
4. Add a module EditMode regression before marking the conversion complete.

Currently implemented advanced pacts:

- `update_015` Ember Fire: 30% on-hit chance, four seconds of burning and two
  server-authoritative damage each second.
- `update_016` Frost Breath: 20% on-hit chance to freeze movement and attacks for
  two seconds.
- `update_017` Scarlet Kiss: heals 15% of actual attack damage, minimum one.
- `update_021` Berserker Rage: missing-health attack/crit scaling refreshed on
  authoritative health changes.

Run the Abilities, Combat and Stats EditMode suites after editing an implemented
pact. The retired Editor validator exercised the deleted legacy runtime surface
and must not be restored as a second gameplay-rule authority.
