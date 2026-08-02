using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public EnemyHurtFlash hurtFlash;
    public int enemyCurrentHealth;

    [Header("血包预制体 / Health Pack Prefab")]
    public GameObject healthPackPrefab;
    [Header("掉落概率 0~1 / Drop Chance (0=never, 1=always)")]
    [Range(0f, 1f)] public float dropRate = 0.3f;

    [Header("金币预制体 / Coin Prefab")]
    public GameObject coinPrefab;
    [Range(0f,1f)] public float coinDropRate = 0.4f;

    private void Start()
    {
        // 增加空值保护，防止StatsManager不存在报错
        if (StatsManager.Instance != null)
        {
            enemyCurrentHealth = StatsManager.Instance.enemymaxHealth;
        }

        hurtFlash = GetComponent<EnemyHurtFlash>();
    }

    public void ChangeEnemyHealth(int amount)
    {
        enemyCurrentHealth -= amount;

        hurtFlash?.StartHurtFlash(); // 空值简化写法

        // 血量超过上限修正，提前处理
        if (StatsManager.Instance != null && enemyCurrentHealth > StatsManager.Instance.enemymaxHealth)
        {
            enemyCurrentHealth = StatsManager.Instance.enemymaxHealth;
        }    

        // 受伤音效，区分死亡与普通受伤
        if (SFXManager.Instance != null && enemyCurrentHealth > 0)
        {
            SFXManager.Instance.PlayAttackhunt();
        }

        // 死亡逻辑：先生成掉落，再销毁怪物
        if(enemyCurrentHealth <= 0)
        {
            if (SFXManager.Instance != null)
                SFXManager.Instance.PlayAttackdead();
            TryDropPack();
            Destroy(gameObject);
        }
    }

    void Awake()
    {
        EnemyHurtFlash[] allFlash = GetComponents<EnemyHurtFlash>();
        // 删掉除第一个以外所有重复脚本
        for(int i = 1; i < allFlash.Length; i++)
        {
            Destroy(allFlash[i]);
        }
    }

    public void TryDropPack()
    {
        // 生成血包
        if (healthPackPrefab != null)
        {
            float randomValue = Random.Range(0f, 1f);
            if (randomValue <= dropRate)
            {
                Vector2 randomOffset = new Vector2(Random.Range(-0.5f,0.5f), Random.Range(-0.5f,0.5f));
                Vector2 spawnPos = (Vector2)transform.position + randomOffset;
                Instantiate(healthPackPrefab, spawnPos, Quaternion.identity);
            }
        }

        // 生成金币
        if (coinPrefab != null)
        {
            float coinRandom = Random.Range(0f,1f);
            if (coinRandom <= coinDropRate)
            {
                Vector2 randomOffset = new Vector2(Random.Range(-0.5f,0.5f), Random.Range(-0.5f,0.5f));
                Vector2 spawnPos = (Vector2)transform.position + randomOffset;
                Instantiate(coinPrefab, spawnPos, Quaternion.identity);
            }
        }
    }
}