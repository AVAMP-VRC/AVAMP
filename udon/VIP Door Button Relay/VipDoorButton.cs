using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VipDoorButton : UdonSharpBehaviour
{
    [Tooltip("Drag the object with the AvampVipDoor script here")]
    public AvampVipDoor mainDoorScript;

    public override void Interact()
    {
        if (mainDoorScript != null)
        {
            // This triggers the Interact() function on your main script
            mainDoorScript.Interact();
        }
    }
}