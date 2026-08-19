using System;
using UnityEngine;

/// <summary>
/// Compatibility shell for the historical Player prefab serialization.
/// Gameplay damage forwarding moved to the VampireHunt feature ports; this
/// type intentionally contains no behaviour.
/// </summary>
[Obsolete("AttackDamageForwarder is retained only for legacy prefab serialization; it has no gameplay logic.")]
public sealed class AttackDamageForwarder : MonoBehaviour
{
}
