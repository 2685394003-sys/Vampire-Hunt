using System;
using System.Reflection;
using UnityEngine;

public interface IDamageable
{
    void TakeDamage(int amount);
}

public interface IKnockbackReceiver
{
    void ApplyKnockback(Transform source, float force, float stunTime);
}

public interface IForceKillable
{
    void ForceKill();
}

public static class BossCombatTarget
{
    public static bool TryGetInParent<T>(Component source, out T receiver) where T : class
    {
        receiver = null;
        if (source == null)
        {
            return false;
        }

        MonoBehaviour[] behaviours = source.GetComponentsInParent<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
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
        if (TryGetInParent(player, out IDamageable damageable))
        {
            if (logMissingMethods && damageable is BossPlayerTargetAdapter existingAdapter)
            {
                existingAdapter.ResolveTargets(true);
            }

            return damageable;
        }

        BossPlayerTargetAdapter adapter = player.GetComponent<BossPlayerTargetAdapter>();
        if (adapter == null)
        {
            adapter = player.gameObject.AddComponent<BossPlayerTargetAdapter>();
        }

        adapter.ResolveTargets(logMissingMethods);
        return adapter;
    }
}

/// <summary>
/// Boss 与现有玩家脚本之间的兼容边界。Boss 只依赖上面的接口；
/// 玩家当前的 ChangeHealth / Knockback / ForceDeath 通过这里适配。
/// 玩家接口改名后项目仍可编译，并会在 Console 中给出明确的适配错误。
/// </summary>
[DisallowMultipleComponent]
public sealed class BossPlayerTargetAdapter : MonoBehaviour, IDamageable, IKnockbackReceiver, IForceKillable
{
    private const BindingFlags MethodFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private MonoBehaviour damageTarget;
    private MonoBehaviour knockbackTarget;
    private MonoBehaviour forceDeathTarget;
    private MethodInfo changeHealthMethod;
    private MethodInfo knockbackMethod;
    private MethodInfo forceDeathMethod;
    private bool resolved;
    private bool missingMethodsLogged;

    private void Awake()
    {
        ResolveTargets(true);
    }

    public void ResolveTargets(bool logMissingMethods)
    {
        damageTarget = null;
        knockbackTarget = null;
        forceDeathTarget = null;
        changeHealthMethod = null;
        knockbackMethod = null;
        forceDeathMethod = null;

        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || behaviour == this)
            {
                continue;
            }

            Type type = behaviour.GetType();
            changeHealthMethod ??= type.GetMethod(
                "ChangeHealth",
                MethodFlags,
                null,
                new[] { typeof(int) },
                null);
            if (changeHealthMethod != null && damageTarget == null)
            {
                damageTarget = behaviour;
            }

            knockbackMethod ??= type.GetMethod(
                "Knockback",
                MethodFlags,
                null,
                new[] { typeof(Transform), typeof(float), typeof(float) },
                null);
            if (knockbackMethod != null && knockbackTarget == null)
            {
                knockbackTarget = behaviour;
            }

            forceDeathMethod ??= type.GetMethod(
                "ForceDeath",
                MethodFlags,
                null,
                Type.EmptyTypes,
                null);
            if (forceDeathMethod != null && forceDeathTarget == null)
            {
                forceDeathTarget = behaviour;
            }
        }

        resolved = true;
        if (logMissingMethods && !missingMethodsLogged)
        {
            if (changeHealthMethod == null)
            {
                Debug.LogError(
                    "[Boss 适配] 玩家上找不到 ChangeHealth(int)。Boss 攻击不会造成伤害，请更新 BossPlayerTargetAdapter。",
                    this);
            }

            if (forceDeathMethod == null)
            {
                Debug.LogWarning(
                    "[Boss 适配] 玩家上找不到 ForceDeath()。契约倒计时结束时将直接停用玩家对象。",
                    this);
            }

            if (knockbackMethod == null)
            {
                Debug.LogWarning(
                    "[Boss 适配] 玩家上找不到 Knockback(Transform, float, float)。伤害仍有效，但不会击退。",
                    this);
            }

            missingMethodsLogged = true;
        }
    }

    public void TakeDamage(int amount)
    {
        EnsureResolved();
        if (amount <= 0 || changeHealthMethod == null || damageTarget == null)
        {
            return;
        }

        Invoke(changeHealthMethod, damageTarget, new object[] { amount });
    }

    public void ApplyKnockback(Transform source, float force, float stunTime)
    {
        EnsureResolved();
        if (force <= 0f || knockbackMethod == null || knockbackTarget == null)
        {
            return;
        }

        Invoke(
            knockbackMethod,
            knockbackTarget,
            new object[] { source != null ? source : transform, force, stunTime });
    }

    public void ForceKill()
    {
        EnsureResolved();
        if (forceDeathMethod != null && forceDeathTarget != null)
        {
            Invoke(forceDeathMethod, forceDeathTarget, Array.Empty<object>());
            return;
        }

        gameObject.SetActive(false);
    }

    private void EnsureResolved()
    {
        if (!resolved)
        {
            ResolveTargets(true);
        }
    }

    private void Invoke(MethodInfo method, MonoBehaviour target, object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            Exception cause = exception.InnerException ?? exception;
            Debug.LogError($"[Boss 适配] 调用 {target.GetType().Name}.{method.Name} 失败：{cause.Message}", this);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Boss 适配] 调用 {target.GetType().Name}.{method.Name} 失败：{exception.Message}", this);
        }
    }
}
