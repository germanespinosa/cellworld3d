using UnityEngine;
using UnityEngine.Events;

public class VolumeTrigger : MonoBehaviour
{
    [Header("Who triggers this")]
    public string playerTag = "Player";

    [Header("Callbacks")]
    public UnityEvent onPlayerLeave;  // fired when player exits this trigger
    public UnityEvent onPlayerReach;  // fired when player enters this trigger

    private bool IsTargetCollider(Collider other)
    {
        if (other == null || string.IsNullOrWhiteSpace(playerTag))
            return false;

        if (other.CompareTag(playerTag))
        {
            Debug.Log($"[IsTargetCollider] -> YES ");
            return true;
        }

        if (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag(playerTag))
            return true;

        Transform root = other.transform.root;
        if (root != null && root.CompareTag(playerTag))
            return true;

        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsTargetCollider(other)) return;

        Debug.Log($"[IsTargetCollider] -> ENTER");
        onPlayerReach.Invoke();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsTargetCollider(other)) return;

        Debug.Log($"[IsTargetCollider] -> LEAVE ");
        onPlayerLeave.Invoke();
    }
}
