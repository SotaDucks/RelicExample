using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Shared health for enemies and the player. Disabling does not restore health.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Kaoru/Health")]
public sealed class KaoruHealth : MonoBehaviour
{
    [Tooltip("最大生命值。飞刀默认每次命中造成 1 点伤害。")]
    [Min(1)] [SerializeField] private int maxHealth = 3;
    [SerializeField] private UnityEvent onDamaged = new UnityEvent();
    [SerializeField] private UnityEvent onDied = new UnityEvent();

    public event Action<KaoruHealth, GameObject> Damaged;
    public event Action<KaoruHealth, GameObject> Died;

    public int MaxHealth => Mathf.Max(1, maxHealth);
    public int CurrentHealth { get; private set; }
    public bool IsDead => initialized && CurrentHealth <= 0;

    private bool initialized;

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized)
            return;
        CurrentHealth = MaxHealth;
        initialized = true;
    }

    /// <summary>Returns true only when this hit actually removes health.</summary>
    public bool TakeDamage(int amount, GameObject source = null)
    {
        Initialize();
        if (!isActiveAndEnabled || IsDead || amount <= 0)
            return false;

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        // Capture this before callbacks: another hit inside a callback must not
        // make the outer hit dispatch a second death notification.
        bool lethal = IsDead;
        Damaged?.Invoke(this, source);
        onDamaged.Invoke();
        if (lethal)
        {
            Died?.Invoke(this, source);
            onDied.Invoke();
        }
        return true;
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
    }
}
