# React to collisions and triggers on a sprite shape

Find out which side of a sprite shape a body touches, so that your scripts can react differently to each part of the sprite shape.

For example, you can play a different sound on each side of a building, or make one stretch of ground slippery. When **Output Type** is **Segments**, every `PhysicsShape` that the component creates stores the side of the spline it lies along. Side `i` runs from control point `i` to the next control point. When a contact or trigger event names one of these shapes, pass the shape to `PhysicsAreaSpriteShape.GetSplineIndex` to get the side. Then use the side with spline methods, for example, `Spline.GetPosition`.

> [!NOTE]
> Only segments have a side. When **Output Type** is **Polygons**, the component builds shapes from points anywhere along the outline, so `GetSplineIndex` returns `PhysicsAreaSpriteShape.noSplineIndex` for them.

## Prerequisites

Add a Physics Area Sprite Shape component to your sprite shape, and set **Output Type** to **Segments**. For more information, refer to [Add a Physics Area Sprite Shape component](physics-area-sprite-shape.md).

## Set up events

To receive events from the sprite shape:

1. In the **Definition** section of the **Physics Area Sprite Shape** component, enable the option for each event you need. For more information, refer to [Physics Area Sprite Shape component reference](physics-area-sprite-shape-reference.md#definition).
2. In the **Callbacks** section, set **Callback Source** to **Area**, **Shapes**, or **Events**.
3. If you set **Callback Source** to **Area** or **Shapes**, set **Callback Target** to a script that implements the matching interfaces.

The **Definition** and **Callbacks** sections work the same way as for the other physics area components. For the callback sources, the events each one sends, and the options that enable each event, refer to [Physics Area Sprite Shape component reference](physics-area-sprite-shape-reference.md).

Use **Events** or **Area** when you need to know only that a body touches the sprite shape, for example, when a character lands on the ground. Use **Shapes** when you need every side a body touches, for example, when a character runs from one side of the spline onto the next.

> [!NOTE]
> Contact and trigger events happen only when a contact begins or ends, not while a body stays in contact with the sprite shape. To check what a body touches at the current moment, use `PhysicsBody.GetContacts`, then pass each shape to `GetSplineIndex`. If you enable **Pre Contact Callbacks**, pre-contact callbacks keep running while the contact lasts. For more information, refer to [Use the side in pre-contact, pre-continuous, and contact filter callbacks](#use-the-side-in-pre-contact-pre-continuous-and-contact-filter-callbacks).

## Find the side from an event

A contact event names two shapes, and the shape of the sprite shape can be either of them, so try both. The following helper class gets the sprite shape and the side of the spline from a shape, and returns `false` if the shape isn't a segment of a Physics Area Sprite Shape component:

```csharp
using Unity.U2D.Physics;
using UnityEngine.U2D;

public static class SplineSideLookup
{
    // Gets the sprite shape and the spline side that a shape lies along.
    // Returns false if the shape isn't a segment of a Physics Area Sprite Shape component.
    public static bool TryGetSide(PhysicsShape shape, out SpriteShapeController spriteShapeController, out int splineSide)
    {
        spriteShapeController = null;
        splineSide = PhysicsAreaSpriteShape.noSplineIndex;

        // Ignore shapes that are destroyed or that belong to another kind of physics area.
        if (!shape.isValid || shape.owner is not PhysicsAreaSpriteShape physicsArea)
            return false;

        // Only segments store a spline side. Polygons return noSplineIndex.
        splineSide = physicsArea.GetSplineIndex(shape);
        if (splineSide == PhysicsAreaSpriteShape.noSplineIndex)
            return false;

        spriteShapeController = physicsArea.controller;
        return true;
    }
}
```

Save the helper class as `SplineSideLookup.cs` in your project. The examples on this page use it.

The following script uses the helper class to log the side a body touches:

```csharp
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.U2D;

public class SplineContacts : MonoBehaviour, PhysicsCallbacks.IContactCallback
{
    public void OnContactBegin2D(PhysicsEvents.ContactBeginEvent beginEvent)
    {
        // The sprite shape can be either shape of the contact, so check both.
        if (SplineSideLookup.TryGetSide(beginEvent.shapeA, out SpriteShapeController spriteShapeController, out int splineSide) ||
            SplineSideLookup.TryGetSide(beginEvent.shapeB, out spriteShapeController, out splineSide))
        {
            // Side splineSide starts at the control point with the same index.
            Vector3 sideStartPosition = spriteShapeController.spline.GetPosition(splineSide);
            Debug.Log($"Touched side {splineSide} of {spriteShapeController.name}, which starts at {sideStartPosition}");
        }
    }

    public void OnContactEnd2D(PhysicsEvents.ContactEndEvent endEvent) { }
}
```

To use the script, follow these steps:

1. Add the script to a GameObject.
2. Set the script as the **Callback Target** of the **Physics Area Sprite Shape** component.
3. Set **Callback Source** to **Shapes**.

With **Callback Source** set to **Area**, implement `PhysicsAreaCallbacks.IContactCallback` instead. Its events have `areaA` and `areaB`, which are the two areas, and `beginEvent` or `endEvent`, which is the original shape event. Pass the shapes of the original event to `TryGetSide`:

```csharp
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.U2D;

public class SplineAreaContacts : MonoBehaviour, PhysicsAreaCallbacks.IContactCallback
{
    public void OnContactBegin2D(PhysicsAreaCallbacks.ContactBeginEvent areaEvent)
    {
        // An area event wraps the original shape event, which names the two shapes.
        PhysicsEvents.ContactBeginEvent shapeEvent = areaEvent.beginEvent;

        if (SplineSideLookup.TryGetSide(shapeEvent.shapeA, out SpriteShapeController spriteShapeController, out int splineSide) ||
            SplineSideLookup.TryGetSide(shapeEvent.shapeB, out spriteShapeController, out splineSide))
        {
            Debug.Log($"Touched side {splineSide} of {spriteShapeController.name}");
        }
    }

    public void OnContactEnd2D(PhysicsAreaCallbacks.ContactEndEvent areaEvent) { }
}
```

### Keep the side for end events

An end event can arrive after the shape is gone, because any change to the spline rebuilds every shape of the sprite shape. `GetSplineIndex` then returns `PhysicsAreaSpriteShape.noSplineIndex`.

If you need the side in end events, store it when the contact begins, keyed by the pair of shapes. The following script logs the side a body stops touching:

```csharp
using System.Collections.Generic;
using Unity.U2D.Physics;
using UnityEngine;

public class SidesUnderfoot : MonoBehaviour, PhysicsCallbacks.IContactCallback
{
    // Stores the spline side of each contact, keyed by the two shapes of the contact.
    readonly Dictionary<(PhysicsShape, PhysicsShape), int> m_TouchedSides = new();

    public void OnContactBegin2D(PhysicsEvents.ContactBeginEvent beginEvent)
    {
        // Store the side now, because the shape might be gone when the contact ends.
        if (SplineSideLookup.TryGetSide(beginEvent.shapeA, out _, out int splineSide) ||
            SplineSideLookup.TryGetSide(beginEvent.shapeB, out _, out splineSide))
        {
            m_TouchedSides[(beginEvent.shapeA, beginEvent.shapeB)] = splineSide;
        }
    }

    public void OnContactEnd2D(PhysicsEvents.ContactEndEvent endEvent)
    {
        // Shapes that are gone still match the key they were stored with.
        if (m_TouchedSides.Remove((endEvent.shapeA, endEvent.shapeB), out int splineSide))
            Debug.Log($"Left side {splineSide}");
    }
}
```

If you add or remove control points after the contact begins, the stored side might no longer match the same part of the spline.

## Find the side from a contact hit event

A contact hit event happens when a body hits the sprite shape faster than the world's `contactHitEventThreshold`, which is a speed in meters per second. Use it to react to impacts, for example, to play a sound when something lands on the ground fast enough. The event's `approachSpeed` is how fast the body was moving toward the sprite shape.

The component doesn't send contact hit events to the **Callback Target**. Read them from the world after each simulation step:

```csharp
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.U2D;

public class SplineImpacts : MonoBehaviour
{
    // Read contact hit events from the world after each simulation step.
    void OnEnable() => PhysicsEvents.PostSimulate += OnPostSimulate;
    void OnDisable() => PhysicsEvents.PostSimulate -= OnPostSimulate;

    void OnPostSimulate(PhysicsWorld world, float deltaTime)
    {
        foreach (PhysicsEvents.ContactHitEvent hitEvent in world.contactHitEvents)
        {
            // The sprite shape can be either shape of the hit, so check both.
            if (SplineSideLookup.TryGetSide(hitEvent.shapeA, out SpriteShapeController spriteShapeController, out int splineSide) ||
                SplineSideLookup.TryGetSide(hitEvent.shapeB, out spriteShapeController, out splineSide))
            {
                Debug.Log($"Hit side {splineSide} of {spriteShapeController.name} at {hitEvent.approachSpeed} m/s");
            }
        }
    }
}
```

## Use the side in pre-contact, pre-continuous, and contact filter callbacks

Pre-contact, pre-continuous, and contact filter callbacks work differently from the other callbacks. The physics engine calls them in the middle of a simulation step, and can call them from any thread, not only the main thread. Most Unity APIs work only on the main thread, so in these callbacks you can't call sprite shape or spline methods, for example, `Spline.GetPosition`.

`GetSplineIndex` is safe to call from any thread, so you can still find out which side a body touches.

To use spline data for a side in these callbacks:

1. On the main thread, before the simulation runs, look up the spline data you need and store it by side.
2. In the callback, use `GetSplineIndex` to get the side, then read the data you stored for it.

## Additional resources

- [2D Physics Core package](https://docs.unity3d.com/Packages/com.unity.2d.physics@latest)
- [Edit the spline of a sprite shape](sprite-shape-edit-spline.md)
- [Sprite Shape Controller component reference](SSController.md)
