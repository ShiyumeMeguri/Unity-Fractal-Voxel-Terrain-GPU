using UnityEngine;

/// <summary>
/// A static instance is similar to a singleton, but instead of destroying any new
/// instances, it overrides the current instance. This is useful for resetting the state
/// and saves you doing it manually.
/// </summary>
public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T s_Instance;

    public static T Instance
    {
        get
        {
            if (s_Instance == null)
            {
                // Corrected: Replaced obsolete FindObjectOfType with FindFirstObjectByType
                s_Instance = FindFirstObjectByType<T>();
            }
            return s_Instance;
        }
    }

    protected virtual void Awake()
    {
        s_Instance = this as T;
    }

    protected virtual void OnApplicationQuit()
    {
        s_Instance = null;
        Destroy(gameObject);
    }
}