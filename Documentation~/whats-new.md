# What's new in the 2D Sprite Shape package

Version 18.0.0 adds Fill Sprite support, so Unity applies secondary textures to the Fill area of a Sprite Shape Profile.

Discover new features and performance improvements in the latest update to 2D Sprite Shape.

For a full list of changes and updates in each version, refer to the [2D Sprite Shape changelog](../changelog/CHANGELOG.html).

## Version 18.0.0

### Use a sprite to fill a Sprite Shape Profile

A Fill Sprite now defines the Fill area of a [Sprite Shape Profile](SSProfile.md), instead of a Fill Texture. Unity applies the secondary textures of the Fill Sprite to the Fill area, so a filled shape can use normal maps and mask textures in the same way as the sprites along its edge.

Unity automatically upgrades existing Profiles if their Fill Texture is imported as a single sprite that covers the whole texture. Profiles that Unity can't upgrade keep rendering unchanged. `SpriteShape.fillTexture` is deprecated. Use `SpriteShape.fillSprite` instead. Refer to [Upgrade a Fill Texture](SSProfile.md#upgrade-a-fill-texture).

## Version 12.0.1

### Disable automatic updating

The Sprite Shape Controller component has a new **Update Geometry** parameter that you can use to disable automatic updating. As a result, Unity no longer checks for geometry changes each frame. Disable this parameter when your sprite shape is static to reduce runtime overhead.

## Additional resources

- [Changelog](../changelog/CHANGELOG.html)
