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

    bool Held()
    {
        LiveThrall lt;
        return Thralls.Live.TryGetValue(theEntity.entityId, out lt) && lt.Hold;
    }

    public override bool CanExecute()
    {
        var o = Owner();
        if (o == null || Held()) return false;
        if (theEntity.GetAttackTarget() != null) return false;
        return (o.position - theEntity.position).magnitude > BaitConfig.FollowDistance;
    }

    public override bool Continue()
    {
        var o = Owner();
        if (o == null || Held() || theEntity.GetAttackTarget() != null) return false;
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
            if (o.AttachedToEntity != null) return;   // riding: thralls are stowed, and one must never be dropped in front of a vehicle
            theEntity.SetPosition(Thralls.BehindOwner(o));
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

// Daycare task: stay inside a circle around a fixed point. Wanders to random spots inside it, walks back when
// it strays or after a fight, and snaps back if it gets far out. Lets go while it has an attack target.
public class EAITether : EAIBase
{
    readonly Vector3 center;
    readonly float radius;
    float nextMove, nextRepath;
    Vector3 goal;
    bool returning;

    public EAITether(Vector3 center, float radius) { this.center = center; this.radius = Mathf.Max(2f, radius); MutexBits = 3; }

    float FlatDist() { var d = theEntity.position - center; d.y = 0f; return d.magnitude; }

    public override bool CanExecute() { return theEntity.GetAttackTarget() == null; }
    public override bool Continue() { return theEntity.GetAttackTarget() == null; }
    public override void Start() { nextMove = Time.time + UnityEngine.Random.Range(1f, 4f); returning = false; }

    public override void Update()
    {
        float d = FlatDist();
        if (d > radius * 4f + 10f)
        {
            theEntity.SetPosition(center + new Vector3(0f, 0.5f, 0f));
            return;
        }
        float now = Time.time;
        if (d > radius)
        {
            if (!returning || now >= nextRepath)
            {
                returning = true;
                nextRepath = now + 2f;
                theEntity.FindPath(center, theEntity.GetMoveSpeedPanic(), false, this);
            }
            return;
        }
        returning = false;
        if (now >= nextMove)
        {
            nextMove = now + UnityEngine.Random.Range(4f, 10f);
            Vector2 c = UnityEngine.Random.insideUnitCircle * (radius * 0.8f);
            goal = new Vector3(center.x + c.x, center.y, center.z + c.y);
            theEntity.FindPath(goal, theEntity.GetMoveSpeedPanic() * 0.5f, false, this);
        }
    }

    public override void Reset() { theEntity.navigator?.clearPath(); }
}
