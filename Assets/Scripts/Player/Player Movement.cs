using System;
using UnityEngine;

/// <summary>
/// Compatibility shim for legacy references. PlayerController now owns all
/// input and server-authoritative movement.
/// </summary>
[Obsolete("Use PlayerController. PlayerMovement no longer performs local simulation.")]
public sealed class PlayerMovement : MonoBehaviour
{
    public PlayerAttact PlayerAttact;

    private PlayerController controller;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        if (controller == null)
        {
            Debug.LogError("[PlayerMovement] PlayerController is required after the network migration.", this);
        }
    }

    public void Knockback(Transform enemy, float force, float stunTime)
    {
        controller?.Knockback(enemy, force, stunTime);
    }
}
