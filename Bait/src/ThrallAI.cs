using System;
using System.Collections.Generic;
using UnityEngine;

// Follow-the-owner task injected into a thrall's AI list (custom tasks can't be named in XML).
public class EAIFollowMaster : EAIBase
{
    Vector3 lastGoal;
    float nextRepath;

    public EAIFollowMaster() { MutexBits = 3; }

    EntityAlive Owner()
    {
        LiveThrall lt;
        if (!Thralls.Live.TryGetValue(theEntity.entityId, out lt)) return null;
        var o = theEntity.world.GetEntity(lt.OwnerEntityId) as EntityAlive;
        return (o == null || o.IsDead()) ? null : o;
    }

    public override bool CanExecute()
    {
        var o = Owner();
        if (o == null) return false;
        if (theEntity.GetAttackTarget() != null) return false;
        return (o.position - theEntity.position).magnitude > BaitConfig.FollowDistance;
    }

    public override bool Continue()
    {
        var o = Owner();
        if (o == null || theEntity.GetAttackTarget() != null) return false;
        return (o.position - theEntity.position).magnitude > BaitConfig.FollowDistance * 0.6f;
    }

    public override void Start() { nextRepath = 0f; }

    public override void Update()
    {
        var o = Owner();
        if (o == null) return;
        float dist = (o.position - theEntity.position).magnitude;
        if (dist > BaitConfig.TeleportDistance)
        {
            theEntity.SetPosition(o.position + new Vector3(UnityEngine.Random.Range(-2f, 2f), 0.5f, UnityEngine.Random.Range(-2f, 2f)));
            return;
        }
        if (Time.time >= nextRepath || (lastGoal - o.position).sqrMagnitude > 9f)
        {
            lastGoal = o.position;
            nextRepath = Time.time + 1.5f;
            theEntity.FindPath(o.position, theEntity.GetMoveSpeedPanic(), false, this);
        }
    }

    public override void Reset() { theEntity.navigator?.clearPath(); }
}