# Optimize physics areas on sprite shapes

Reduce the time it takes to rebuild the physics shapes of a sprite shape, and keep their memory use low.

Any change to the spline of a sprite shape rebuilds every shape of its Physics Area Sprite Shape component, however small the change is. The following tips help keep sprite shapes fast to edit, in the Unity Editor and at runtime.

## Simplify the outline

Keep the outline as simple as the gameplay needs. Rebuild time grows with the number of points in the outline.

To use fewer points in the outline, do either of the following:

- In the **Collision** section of the **Sprite Shape Controller** component, set **Detail** to a lower quality.
- Use fewer control points on curved parts of the spline.

## Use segments for long shapes

For long shapes, for example terrain, set **Output Type** to **Segments**. Segments create one edge for each point of the outline, which uses more memory than polygons, but their rebuild time depends only on the length of the outline. The rebuild time of polygons also grows with how jagged the outline is, so an outline with many deep notches is slow to fill.

## Split very large shapes

A single sprite shape can generate only a limited amount of geometry. The limit depends on the length of the spline, the sprites in the Sprite Shape Profile, and the **Detail** settings, and an outline can never have more than about 32,000 points. If a sprite shape reaches the limit, Unity stops generating the rest of the sprite shape, and the shapes don't cover the whole sprite shape. Build a long level from several sprite shapes instead.

## Move the body instead of the spline

Don't edit the spline every frame. Each change makes the sprite shape generate its outline again, and then rebuilds every shape.

For a moving platform, add the Physics Pose to the same GameObject, set **Body Type** to **Kinematic**, and move the body instead of the control points. The shapes then move with the body, and the component doesn't rebuild them.

## Additional resources

- [Add a Physics Area Sprite Shape component](physics-area-sprite-shape.md)
- [Physics Area Sprite Shape component reference](physics-area-sprite-shape-reference.md)
- [Sprite Shape Controller component reference](SSController.md)
