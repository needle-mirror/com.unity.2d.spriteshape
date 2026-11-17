# Sprite Shape Renderer component reference

| **Property** | **Function** |
| :--- | :--- |
| **Color** | Define the vertex color of the sprite shape geometry, which tints or recolors the sprite shape. Use the color picker to set the vertex. See the [Color](color) section below this table for examples. |
| **Mask Interaction** | Set how the Sprite Renderer behaves when it interacts with a [Sprite Mask](../mask/mask-landing). Refer to examples of the different options in the [Mask Interaction](mask-interaction) section below. <ul> <li>**None**: The Sprite Shape Renderer does not interact with any Sprite Masks in the Scene. This is the default option.</li> <li>**Visible Inside Mask**: The sprite shape is visible where the Sprite Mask overlays it, but not outside of it.</li> <li>**Visible Outside Mask**: The sprite shape is visible outside of the Sprite Mask, but not inside it. The Sprite Mask hides the sections of the sprite shape it overlays.</li> </ul> |
| **Sorting Layer** | Set the [Sorting Layer](../../class-TagManager) of the sprite shape geometry, which controls its priority during rendering. Select an existing Sorting Layer from the drop-down box, or create a new Sorting Layer. |
| **Order In Layer** | Set the render priority of the sprite shape within its [Sorting Layer](../../class-TagManager). Lower numbered sprite shapes are rendered first, with higher numbered sprite shapes overlapping those below. |

## Additional resources

* [Sprite Shape Controller component reference](SSController.md)
