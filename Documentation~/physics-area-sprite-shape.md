# Add a Physics Area Sprite Shape component

Use the Physics Area Sprite Shape component to make a sprite shape interact with the physics world of the 2D Physics Core system.

The Physics Area Sprite Shape component creates shapes from the Sprite Shape Controller component on the same GameObject. The shapes follow the collision outline that the Sprite Shape Controller generates, which is the same outline that an Edge Collider 2D or Polygon Collider 2D component uses.

The component keeps the shapes up to date. When the Sprite Shape Controller generates a new outline, for example when you move a control point or change the tangents of the spline, the component rebuilds the shapes to match it.

## Prerequisites

Install the [2D Physics Core](https://docs.unity3d.com/Packages/com.unity.2d.physics@latest) package, version 2.0.0 or later. The Physics Area Sprite Shape component is available only when this package is installed in your project.

## Add a physics area to a sprite shape

To add a physics area to a sprite shape:

1. Select a GameObject with a Sprite Shape Controller component in the **Hierarchy** window.
2. In the **Inspector** window, select **Add Component** > **Physics 2D (Core)** > **Physics Area Sprite Shape**.
3. If the **Physics Area Sprite Shape** component shows a message that it has no Physics Pose, select **Add Physics Pose** in the message to create the body that the shapes belong to.
4. For ground or walls that never move, set **Body Type** to **Static**.
5. In the **Geometry** section, set **Output Type** to the kind of shapes you need. For more information, refer to [Choose the kind of shapes](#choose-the-kind-of-shapes).

The shapes appear in the **Scene** view while the GameObject is selected.

## Choose the kind of shapes

The **Output Type** property in the **Geometry** section sets whether the component creates polygons or segments:

- Polygons are solid shapes that fill the outline. If the spline is open, the component closes the outline to fill it. Use polygons for sprite shapes that act as triggers (for example, water or hazards), and for sprite shapes that bodies can end up inside.
- Segments are one-sided edges along the outline. Use segments for ground and walls that characters slide or roll along, and for open splines (for example, a path or a rope bridge). The default is **Segments**.

Segments create no shapes inside the outline, so the physics area is hollow. A body that ends up fully inside the outline doesn't collide with it, and a trigger made of segments stops detecting a body when the body is fully inside it.

To find out which side of the spline a body touches in a contact or trigger event, use segments. Only segments store the side of the spline they lie along. For more information, refer to [React to collisions and triggers on a sprite shape](physics-area-sprite-shape-events.md).

## Choose the side segments collide on

Each segment collides on one side only. A body that approaches a segment from the other side passes through it.

By default, the segments of a closed spline collide on the outside of the outline. To make them collide on the inside instead, for example, for the walls of a cave that bodies move around in, enable **Reverse Winding** in the **Geometry** section.

For an open spline, the segments collide on one side of the strip. If bodies pass through the strip from the side you need, enable **Reverse Winding** to make the segments collide on the other side.

**Reverse Winding** affects only segments. Polygons are solid, so they collide on every side.

## Control the outline

The component creates the shapes from the outline that the Sprite Shape Controller generates. To set the outline, use the **Collision** section of the **Sprite Shape Controller** component. This section appears when the GameObject has a Physics Area Sprite Shape component, and contains the following properties:

- **Update Collision**: Keeps the outline up to date as you edit the sprite shape. Disable it to keep the current shapes while you edit the sprite shape.
- **Offset**: Moves the outline toward or away from the edge of the sprite shape.
- **Detail**: Sets how closely the outline follows curved parts of the spline. A lower quality creates fewer, longer edges.

**Offset** and **Detail** are available only when **Update Collision** is enabled.

For more information, refer to [Sprite Shape Controller component reference](SSController.md#collision).

## Update the shapes of an off-screen sprite shape

A sprite shape generates a new outline only while it's visible to a camera. If a script changes the spline of an off-screen sprite shape, its shapes keep the old outline until the sprite shape is visible again.

To update the shapes immediately, call `PhysicsAreaSpriteShape.RebuildFromSpline` after you change the spline.

## Additional resources

- [Add a collider to a sprite shape](SSCollision.md)
- [Edit the spline of a sprite shape](sprite-shape-edit-spline.md)
- [Optimize physics areas on sprite shapes](physics-area-sprite-shape-optimize.md)
- [Physics Area Sprite Shape component reference](physics-area-sprite-shape-reference.md)
- [React to collisions and triggers on a sprite shape](physics-area-sprite-shape-events.md)
