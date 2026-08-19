using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using CombatDamageResult = VampireHunt.Combat.Contracts.DamageResult;
using CombatResolvedDamage = VampireHunt.Combat.Contracts.ResolvedDamage;
using CombatDamageReceiver = VampireHunt.Combat.Contracts.IDamageReceiver;
using CombatEntityTarget = VampireHunt.Combat.Contracts.ICombatTarget;
using CombatEntityDirectory = VampireHunt.Combat.Contracts.ICombatEntityDirectory;
using CombatHealingReceiver = VampireHunt.Combat.Contracts.IHealingReceiver;
using CombatKnockbackReceiver = VampireHunt.Combat.Contracts.IKnockbackReceiver;
using EntityId = VampireHunt.Core.EntityId;
using EntityIdAllocator = VampireHunt.Core.EntityIdAllocator;
using WorldPosition = VampireHunt.Core.WorldPosition;

/// <summary>Compatibility entry point retained for old UnityEvent/AnimationEvent assets.</summary>
public interface IDamageable
{
    void TakeDamage(int amount);
}

/// <summary>Compatibility presentation/motor port retained for old player scripts.</summary>
public interface IKnockbackReceiver
{
    void ApplyKnockback(Transform source, float force, float stunTime);
}

public interface IForceKillable
{
    void ForceKill();
}

/// <summary>
/// Unity-side target adapter. It contains no player implementation reference;
/// legacy method names are discovered at the integration boundary and the
/// resulting target is exposed to Combat through small capability ports.
/// </summary>
public static class BossCombatTarget
{
    public static bool TryGetInParent<T>(Component source, out T receiver) where T : class
    {
        receiver = null;
        if (source == null) return false;
        foreach (MonoBehaviour behaviour in source.GetComponentsInParent<MonoBehaviour>(true))
        {
            if (behaviour is T match)
            {
                receiver = match;
                return true;
            }
        }
        return false;
    }

    public static IDamageable EnsurePlayerAdapter(Transform player, bool logMissingMethods)
    {
        if (player == null) return null;
        if (TryGetInParent(player, out IDamageable damageable))
        {
            if (logMissingMethods && damageable is BossPlayerTargetAdapter existing)
                existing.ResolveTargets(true);
            return damageable;
        }

        BossPlayerTargetAdapter adapter = player.GetComponent<BossPlayerTargetAdapter>() ??
            player.gameObject.AddComponent<BossPlayerTargetAdapter>();
        adapter.ResolveTargets(logMissingMethods);
        return adapter;
    }

    public static bool TryGetCombatTarget(Component source, out CombatEntityTarget target)
    {
        target = null;
        if (source == null) return false;
        if (TryGetInParent(source, out BossPlayerTargetAdapter adapter))
        {
            target = adapter;
            return true;
        }

        Transform root = source.transform.root;
        EnsurePlayerAdapter(root, false);
        return TryGetInParent(root, out adapter) && (target = adapter) != null;
    }
}

/// <summary>
/// Reflection is deliberately isolated here for the prototype Player API.
/// Boss logic only sees Combat contracts and therefore remains independent of
/// Player/Enemy MonoBehaviours and safe on a Dedicated Server.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossPlayerTargetAdapter : MonoBehaviour,
    IDamageable,
    IKnockbackReceiver,
    IForceKillable,
    CombatEntityTarget,
    CombatDamageReceiver,
    CombatKnockbackReceiver
{
    private const BindingFlags MethodFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly EntityIdAllocator IdAllocator = new();

    private MonoBehaviour damageTarget;
    private MonoBehaviour knockbackTarget;
    private MonoBehaviour forceDeathTarget;
    private MethodInfo changeHealthMethod;
    private MethodInfo knockbackMethod;
    private MethodInfo forceDeathMethod;
    private PropertyInfo aliveProperty;
    private PropertyInfo deadProperty;
    private PropertyInfo currentHealthProperty;
    private MonoBehaviour aliveTarget;
    private MonoBehaviour deadTarget;
    private MonoBehaviour currentHealthTarget;
    private bool resolved;
    private bool missingMethodsLogged;
    private EntityId id;

    public EntityId Id
    {
        get
        {
            if (!id.IsValid) id = IdAllocator.Allocate();
            return id;
        }
    }

    public bool IsAlive
    {
        get
        {
            EnsureResolved();
            if (aliveProperty != null && TryReadBool(aliveProperty, aliveTarget, out bool alive)) return alive;
            if (deadProperty != null && TryReadBool(deadProperty, deadTarget, out bool dead)) return !dead;
            if (TryReadHealth(out int health)) return health > 0;
            return gameObject != null && gameObject.activeInHierarchy;
        }
    }

    public WorldPosition Position
    {
        get
        {
            Vector3 value = transform != null ? transform.position : Vector3.zero;
            return new WorldPosition(value.x, value.y, value.z);
        }
    }

    private void Awake() => ResolveTargets(false);

    public void ResolveTargets(bool logMissingMethods)
    {
        damageTarget = null;
        knockbackTarget = null;
        forceDeathTarget = null;
        changeHealthMethod = null;
        knockbackMethod = null;
        forceDeathMethod = null;
        aliveProperty = null;
        deadProperty = null;
        currentHealthProperty = null;
        aliveTarget = null;
        deadTarget = null;
        currentHealthTarget = null;

        foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour == this) continue;
            Type type = behaviour.GetType();
            changeHealthMethod ??= FindMethod(type, "ChangeHealth", typeof(int));
            if (changeHealthMethod != null && damageTarget == null) damageTarget = behaviour;
            knockbackMethod ??= FindMethod(type, "Knockback", typeof(Transform), typeof(float), typeof(float));
            if (knockbackMethod != null && knockbackTarget == null) knockbackTarget = behaviour;
            forceDeathMethod ??= FindMethod(type, "ForceDeath");
            if (forceDeathMethod != null && forceDeathTarget == null) forceDeathTarget = behaviour;

            if (aliveProperty == null)
            {
                aliveProperty = FindProperty(type, "IsAlive");
                if (aliveProperty != null) aliveTarget = behaviour;
            }
            if (deadProperty == null)
            {
                deadProperty = FindProperty(type, "IsDead");
                if (deadProperty != null) deadTarget = behaviour;
            }
            if (currentHealthProperty == null)
            {
                currentHealthProperty = FindProperty(type, "CurrentHealth", "Health");
                if (currentHealthProperty != null) currentHealthTarget = behaviour;
            }
        }

        resolved = true;
        if (!logMissingMethods || missingMethodsLogged) return;
        if (changeHealthMethod == null)
            Debug.LogWarning("[Boss 适配] 目标没有 ChangeHealth(int)，Combat 命中将返回零实际伤害。", this);
        if (knockbackMethod == null)
            Debug.LogWarning("[Boss 适配] 目标没有 Knockback(Transform, float, float)，命中仍有效但无击退。", this);
        missingMethodsLogged = true;
    }

    public void TakeDamage(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline() || amount <= 0) return;
        EnsureResolved();
        if (changeHealthMethod != null && damageTarget != null)
            Invoke(changeHealthMethod, damageTarget, new object[] { amount });
    }

    public CombatDamageResult ApplyDamage(in CombatResolvedDamage damage)
    {
        int before = ReadCurrentHealthOrUnknown();
        TakeDamage(damage.FinalDamage);
        int after = ReadCurrentHealthOrUnknown();
        int applied = before >= 0 && after >= 0
            ? Mathf.Clamp(before - after, 0, damage.FinalDamage)
            : damage.FinalDamage;
        bool killed = applied > 0 && after == 0;
        return new CombatDamageResult(
            damage.FinalDamage,
            applied,
            damage.WasCritical && applied > 0,
            killed,
            damage.Hit.Position);
    }

    public void ApplyKnockback(Transform source, float force, float stunTime)
    {
        if (!NetworkAuthority.IsServerOrOffline() || force <= 0f) return;
        EnsureResolved();
        if (knockbackMethod != null && knockbackTarget != null)
            Invoke(knockbackMethod, knockbackTarget,
                new object[] { source != null ? source : transform, force, stunTime });
    }

    public void ApplyKnockback(in VampireHunt.Combat.Contracts.KnockbackImpulse impulse)
    {
        ApplyKnockback(transform, impulse.Force, impulse.Duration);
    }

    public void ForceKill()
    {
        if (!NetworkAuthority.IsServerOrOffline()) return;
        EnsureResolved();
        if (forceDeathMethod != null && forceDeathTarget != null)
            Invoke(forceDeathMethod, forceDeathTarget, Array.Empty<object>());
        else if (damageTarget != null && changeHealthMethod != null)
            Invoke(changeHealthMethod, damageTarget, new object[] { int.MaxValue });
    }

    private void EnsureResolved()
    {
        if (!resolved) ResolveTargets(true);
    }

    private int ReadCurrentHealthOrUnknown() => TryReadHealth(out int value) ? value : -1;

    private bool TryReadHealth(out int value)
    {
        value = -1;
        if (currentHealthProperty == null || currentHealthTarget == null) return false;
        try
        {
            object raw = currentHealthProperty.GetValue(currentHealthTarget);
            if (raw == null) return false;
            value = Convert.ToInt32(raw);
            return true;
        }
        catch { return false; }
    }

    private static bool TryReadBool(PropertyInfo property, MonoBehaviour target, out bool value)
    {
        value = false;
        if (property == null || target == null) return false;
        try
        {
            object raw = property.GetValue(target);
            if (raw == null) return false;
            value = Convert.ToBoolean(raw);
            return true;
        }
        catch { return false; }
    }

    private void Invoke(MethodInfo method, MonoBehaviour target, object[] arguments)
    {
        try { method.Invoke(target, arguments); }
        catch (TargetInvocationException exception)
        {
            Debug.LogError($"[Boss 适配] 调用 {target.GetType().Name}.{method.Name} 失败：{exception.InnerException?.Message ?? exception.Message}", this);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Boss 适配] 调用 {target.GetType().Name}.{method.Name} 失败：{exception.Message}", this);
        }
    }

    private static MethodInfo FindMethod(Type type, string name, params Type[] parameters) =>
        type.GetMethod(name, MethodFlags, null, parameters ?? Type.EmptyTypes, null);

    private static PropertyInfo FindProperty(Type type, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            PropertyInfo property = type.GetProperty(names[i], MethodFlags);
            if (property != null && property.CanRead) return property;
        }
        return null;
    }
}

/// <summary>Adapter directory used by the Boss CombatApplicationService.</summary>
internal sealed class BossCombatEntityDirectory : CombatEntityDirectory
{
    private readonly Dictionary<EntityId, CombatDamageReceiver> damage = new();
    private readonly Dictionary<EntityId, CombatKnockbackReceiver> knockback = new();

    public void Bind(EntityId id, CombatDamageReceiver receiver)
    {
        if (!id.IsValid || receiver == null) return;
        damage[id] = receiver;
        if (receiver is CombatKnockbackReceiver withKnockback) knockback[id] = withKnockback;
    }

    public void Bind(CombatEntityTarget target)
    {
        if (target == null || !target.Id.IsValid) return;
        if (target is CombatDamageReceiver receiver) Bind(target.Id, receiver);
        if (target is CombatKnockbackReceiver withKnockback) knockback[target.Id] = withKnockback;
    }

    public CombatDamageReceiver TryGetDamageReceiver(EntityId id) =>
        damage.TryGetValue(id, out CombatDamageReceiver receiver) ? receiver : null;

    public CombatHealingReceiver TryGetHealingReceiver(EntityId id) => null;

    public CombatKnockbackReceiver TryGetKnockbackReceiver(EntityId id) =>
        knockback.TryGetValue(id, out CombatKnockbackReceiver receiver) ? receiver : null;
}
