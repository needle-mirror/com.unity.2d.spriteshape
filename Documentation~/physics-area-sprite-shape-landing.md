# Add physics areas to sprite shapes

To make a sprite shape interact with the [2D Physics Core](https://docs.unity.com/en-us/engine/latest/manual/unity2d/2d-physics-api/introduction) physics system, add a Physics Area Sprite Shape component to it. Bodies in the 2D Physics Core system can then collide with the sprite shape or detect it as a trigger. The shapes of the physics area follow the outline of the sprite shape as you edit its spline, in the Unity Editor and at runtime.

The following table lists the pages in this section.

| **Topic** | **Description** |
|:--|:--|
| [Add a Physics Area Sprite Shape component](physics-area-sprite-shape.md) | Add a Physics Area Sprite Shape component to a sprite shape, and choose the kind of shapes it creates. |
| [Optimize physics areas on sprite shapes](physics-area-sprite-shape-optimize.md) | Keep the physics shapes of a sprite shape fast to rebuild and low in memory use. |
| [Physics Area Sprite Shape component reference](physics-area-sprite-shape-reference.md) | Explore the properties of the Physics Area Sprite Shape component, its callback sources, and the events it sends. |
| [React to collisions and triggers on a sprite shape](physics-area-sprite-shape-events.md) | Find out which side of the spline a body touches, from a contact or trigger event. |

## Additional resources

- [Add a collider to a sprite shape](SSCollision.md)
- [Sprite Shape Controller component reference](SSController.md)
