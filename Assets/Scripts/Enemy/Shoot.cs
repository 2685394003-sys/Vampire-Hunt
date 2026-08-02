using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shoot : MonoBehaviour
{
    private float trackCheckTimer;  
    public Transform targetPlayer;
    private Rigidbody2D rb;
    
    void Start()
    {
        trackCheckTimer = StatsManager.Instance.bulletMaxLife;
        rb = GetComponent<Rigidbody2D>();
        if(targetPlayer != null)
        {
            Vector2 direction = (targetPlayer.position - transform.position).normalized;
            rb.linearVelocity = direction * StatsManager.Instance.bulletspeed;
        }
        Destroy(gameObject,StatsManager.Instance.bulletMaxLife);
    }

    private void OnTriggerEnter2D(Collider2D hit)
    {
        if (hit.CompareTag("Player"))
        {
            PlayerHealth playerHp = targetPlayer.GetComponent<PlayerHealth>();
            playerHp.ChangeHealth(StatsManager.Instance.shootDamage);
            Destroy(gameObject);
            return;
        }
        if (hit.CompareTag("Wall"))
        {
            Destroy(gameObject);
        }
    }
}
