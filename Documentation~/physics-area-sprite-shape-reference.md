# Physics Area Sprite Shape component reference

Explore the properties of the Physics Area Sprite Shape component to set the kind of physics shapes it creates for a sprite shape and the events it sends.

The Physics Area Sprite Shape component is available only when the [2D Physics Core](https://docs.unity3d.com/Packages/com.unity.2d.physics@latest) package is installed. To add the component, refer to [Add a Physics Area Sprite Shape component](physics-area-sprite-shape.md).

The Physics Area Sprite Shape component has the same sections as the other physics area components, plus a **Geometry** section that only this component has. This page describes the following:

- The **Geometry** section.
- How the **Callbacks** and **Definition** sections behave on a sprite shape.

For the sections and properties that all physics area components share, refer to the [Physics Area Circle component reference](https://docs.unity.com/en-us/engine/latest/manual/unity2d/2d-physics-api/create-objects/reference-area-circle).

## Physics Area Sprite Shape component properties

The Physics Area Sprite Shape component contains the following sections. The **Inspector** window shows only the properties that apply to the current settings.

### Geometry

The **Geometry** section contains the following properties.

| **Property** | **Description** |
|:--|:--|
| **Output Type** | Sets the kind of shapes the component creates from the outline of the sprite shape. For more information, refer to [Choose the kind of shapes](physics-area-sprite-shape.md#choose-the-kind-of-shapes). The following options are available:<ul><li>**Polygons**: Creates solid shapes that fill the outline. If the spline is open, the component closes the outline to fill it.</li><li>**Segments**: Creates one-sided edges along the outline. Each segment stores the side of the spline it lies along. This is the default option.</li></ul> |
| **Reverse Winding** | Flips the side of the outline that the segments collide on. Disabled by default. For more information, refer to [Choose the side segments collide on](physics-area-sprite-shape.md#choose-the-side-segments-collide-on). This property is available only when **Output Type** is **Segments**. |

### Callbacks

The **Callbacks** section contains the following properties.

| **Property** | **Description** |
|:--|:--|
| **Callback Source** | Sets how the component sends events, and which shapes the events name. **Area** and **Events** group events per pair of physics areas, so a body with several physics areas sends events for each area. Grouping requires `PhysicsWorld.eventGroupingAllowed`. When it's disabled, every shape pair sends its own events. The following options are available:<ul><li>**Off**: Sends no events.</li><li>**Area**: Sends events to a script assigned to **Callback Target** that implements the `PhysicsAreaCallbacks` interfaces. Sends one begin event when the first shape of another physics area touches the sprite shape, and one end event when the last shape stops touching it.</li><li>**Shapes**: Sends events to a script assigned to **Callback Target** that implements the `PhysicsCallbacks` interfaces. Sends a begin event and an end event for every shape.</li><li>**Events**: Sends events to methods that you assign in the **Inspector** window, with no script needed. Sends one begin event when the first shape of another physics area touches the sprite shape, and one end event when the last shape stops touching it.</li></ul> |
| **Callback Target** | Sets the script that receives events. This property is available only when **Callback Source** is **Area** or **Shapes**. |

### Definition

The **Definition** section contains the following properties that control which events the sprite shape sends. Some events also need an option enabled on the other body or in the physics world.

| **Property** | **Description** |
|:--|:--|
| **Contact Events** | Sends a begin event when a body starts touching the sprite shape, and an end event when it stops. Enable this property on the sprite shape or on the other body. You can receive these events through the **Area**, **Shapes**, and **Events** callback sources. |
| **Trigger Events** | Sends a begin event when a body enters a trigger and an end event when it leaves. This applies when the sprite shape is the trigger, and when the sprite shape enters or leaves another trigger. Enable this property on both the sprite shape and the other body. To make the sprite shape a trigger, enable **Is Trigger**. You can receive these events through the **Area**, **Shapes**, and **Events** callback sources. |
| **Hit Events** | Adds an event to the `contactHitEvents` list of the physics world when a body hits the sprite shape faster than the `contactHitEventThreshold` of the world. Enable this property on the sprite shape or on the other body. The component doesn't send these events to **Callback Target**. For more information, refer to [Find the side from a contact hit event](physics-area-sprite-shape-events.md#find-the-side-from-a-contact-hit-event). |
| **Pre Contact Callbacks** | Calls pre-contact and pre-continuous callbacks before the physics engine solves a contact with the sprite shape, so you can change or cancel the contact, for example, for one-way platforms. The callbacks run each time the contact updates, for every shape. Enable this property on the sprite shape and in the physics world. You can receive these events through the **Area** and **Shapes** callback sources. |
| **Contact Filter Callbacks** | Calls a contact filter callback when the physics engine finds a body touching the sprite shape, so you can decide whether to create a contact. Works only with bodies that have **Body Type** set to **Dynamic**. Enable this property on the sprite shape and in the physics world. You can receive these events through the **Area** and **Shapes** callback sources, for every shape. |

## Additional resources

- [Add a Physics Area Sprite Shape component](physics-area-sprite-shape.md)
- [Physics Area Circle component reference](https://docs.unity.com/en-us/engine/latest/manual/unity2d/2d-physics-api/create-objects/reference-area-circle)
- [React to collisions and triggers on a sprite shape](physics-area-sprite-shape-events.md)
